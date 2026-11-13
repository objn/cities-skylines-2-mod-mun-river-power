param([string]$GamePath = 'F:\Cities Skylines II')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$package = Join-Path $root 'dist\MunRiverPower'
$managed = Join-Path $GamePath 'Cities2_Data\Managed'
$null = [Reflection.Assembly]::LoadFrom((Join-Path $managed 'Colossal.Mono.Cecil.dll'))
$resolver = [Colossal.Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($managed)
$resolver.AddSearchDirectory('C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8')
$parameters = [Colossal.Mono.Cecil.ReaderParameters]::new()
$parameters.AssemblyResolver = $resolver
$dll = Join-Path $package 'MunRiverPower.dll'
$assembly = [Colossal.Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll, $parameters)
if ($assembly.Name.Version.ToString() -ne '0.17.6.0') { throw 'Incorrect package version.' }
$mod = $assembly.MainModule.Types | Where-Object FullName -eq 'MunRiverPower.Mod'
if (-not ($mod.Interfaces | Where-Object { $_.InterfaceType.FullName -eq 'Game.Modding.IMod' })) { throw 'IMod entry point missing.' }
foreach ($method in @('OnLoad','OnDispose')) {
    if (-not ($mod.Methods | Where-Object { $_.Name -eq $method -and $_.IsPublic -and $_.HasBody })) { throw "Missing public $method." }
}
$referenceCount = 0
foreach ($reference in $assembly.MainModule.AssemblyReferences) {
    if ($reference.Name -notmatch '^(Game$|Unity|Colossal)') { continue }
    $path = Join-Path $managed ($reference.Name + '.dll')
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing game dependency: $($reference.Name)" }
    $installed = [Colossal.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
    try {
        if ($reference.Version -ne $installed.Name.Version) { throw "Assembly version mismatch: $($reference.Name)" }
    } finally { $installed.Dispose() }
    $referenceCount++
}
$resourceNames = @($assembly.MainModule.Resources | ForEach-Object Name)
foreach ($resource in @('MunRiverPower.FishPassage_Upgrade_LOD0.glb','MunRiverPower.AutomaticSpillway_Upgrade_LOD0.glb','MunRiverPower.Dam_Module75_LOD0.glb')) {
    if ($resourceNames -notcontains $resource) { throw "Embedded upgrade mesh missing: $resource" }
}
$meshFactory = $assembly.MainModule.Types | Where-Object FullName -eq 'MunRiverPower.UpgradeMeshFactory'
foreach ($methodName in @('FishPassage','AutomaticSpillway','ReadGlb')) {
    if (-not ($meshFactory.Methods | Where-Object { $_.Name -eq $methodName -and $_.HasBody })) { throw "Upgrade mesh loader missing $methodName." }
}
$scene = $assembly.MainModule.Types | Where-Object Name -eq DamSceneVisuals
if (-not ($scene.Methods | Where-Object { $_.HasBody -and ($_.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match "MeshRenderer" }) })) { throw "Scene renderer missing" }
if ($scene.Methods | Where-Object { $_.HasBody -and ($_.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'RenderPrefab::ObtainMaterial' }) }) { throw 'Unsafe native road shader applied to procedural mesh.' }
$membersChecked = 0
foreach ($member in $assembly.MainModule.GetMemberReferences()) {
    if ($member.DeclaringType.Scope.Name -notmatch '^(Game$|Unity|Colossal)') { continue }
    if ($member -is [Colossal.Mono.Cecil.MethodReference] -or $member -is [Colossal.Mono.Cecil.FieldReference]) {
        if ($null -eq $member.Resolve()) { throw "Cannot resolve game API: $($member.FullName)" }
        $membersChecked++
    }
}
$factory = $assembly.MainModule.Types | Where-Object FullName -eq 'MunRiverPower.WeirFactory'
$cloneMethod = $factory.Methods | Where-Object Name -eq 'Clone'
foreach ($instruction in $cloneMethod.Body.Instructions) {
    if ($instruction.OpCode.Name -eq 'stfld' -and $instruction.Operand.FullName -match 'NetSectionInfo::m_Section|NetPieceInfo::m_Piece|NetSubSectionInfo::m_Section') {
        throw 'Regression: cloning rewires the native render section/piece graph.'
    }
}
$localizationMethod = $mod.Methods | Where-Object Name -eq 'RegisterText'
if (-not ($localizationMethod.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'PrefabBase::GetPrefabID' })) {
    throw 'Regression: localization must use the actual typed prefab identity.'
}
$loadMethod = $mod.Methods | Where-Object Name -eq 'OnLoad'
foreach ($upgradeKey in @('Assets.UPGRADE_NAME[', 'Assets.UPGRADE_DESCRIPTION[')) {
    if (-not ($localizationMethod.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' -and $_.Operand -eq $upgradeKey })) {
        throw "Missing native upgrade localization key: $upgradeKey"
    }
}
if (-not ($loadMethod.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'UpdateBefore<MunRiverPower.WeirPrefabSystem,Game.Prefabs.PrefabInitializeSystem>' })) {
    throw 'Regression: weir registration must precede PrefabInitializeSystem.'
}
if (-not ($loadMethod.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'UpdateBefore<MunRiverPower.WeirRatedPowerSystem,Game.Simulation.PowerPlantAISystem>' })) {
    throw 'Regression: physical rating must be applied before native electricity production.'
}
$guard = $assembly.MainModule.Types | Where-Object FullName -eq 'MunRiverPower.WeirNativePlacementSystem'
foreach ($registration in @(
    'UpdateBefore<MunRiverPower.WeirEconomicProfilesSystem,Game.Prefabs.PrefabInitializeSystem>',
    'UpdateBefore<MunRiverPower.WeirConstructionRestoreSystem,Game.Net.CostSystem>',
    'UpdateAfter<MunRiverPower.WeirConstructionEconomySystem,Game.Buildings.WaterPoweredInitializeSystem>',
    'UpdateBefore<MunRiverPower.WeirConstructionEconomySystem,Game.Tools.ToolApplySystem>',
    'UpdateBefore<MunRiverPower.WeirPowerPreviewSystem,Game.UI.Tooltip.TempRenewableElectricityProductionTooltipSystem>',
    'UpdateAfter<MunRiverPower.WeirPowerPreviewRestoreSystem,Game.UI.Tooltip.TempRenewableElectricityProductionTooltipSystem>'
    'UpdateBefore<MunRiverPower.FishPassageUpgradeSystem,Game.Prefabs.PrefabInitializeSystem>'
    'UpdateAfter<MunRiverPower.FishPassageUpgradeLinkSystem,Game.Prefabs.BuildingInitializeSystem>'
    'UpdateBefore<MunRiverPower.SpillwayUpgradeSystem,Game.Prefabs.PrefabInitializeSystem>'
    'UpdateAfter<MunRiverPower.SpillwayUpgradeLinkSystem,Game.Prefabs.BuildingInitializeSystem>'
    'UpdateBefore<MunRiverPower.SpillwayInfoSystem,Game.UI.InGame.SelectedInfoUISystem>'
)) {
    if (-not ($loadMethod.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains($registration) })) { throw "Missing economic phase: $registration" }
}
$economy = $assembly.MainModule.Types | Where-Object FullName -eq 'MunRiverPower.WeirConstructionEconomySystem'
$invoiceUpdate = $economy.Methods | Where-Object Name -eq 'OnUpdate'
if (-not ($invoiceUpdate.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.ToString() -match 'Temp::m_Cost' })) {
    throw 'Construction prices must adjust the actual native invoice.'
}
$rating = $assembly.MainModule.Types | Where-Object FullName -eq 'MunRiverPower.WeirRatedPowerSystem'
$preview = $assembly.MainModule.Types | Where-Object FullName -eq 'MunRiverPower.WeirPowerPreviewSystem'
$previewUpdate = $preview.Methods | Where-Object Name -eq 'OnUpdate'
foreach ($requiredCall in @('HydroSpecification::InstalledMW', 'HydroSpecification::PreviewProductionGameUnits', 'HydroSpecification::CapacityHeight')) {
    if (-not ($previewUpdate.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains($requiredCall) })) { throw "Missing preview calibration: $requiredCall" }
}
if ($previewUpdate.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'stfld' -and $_.Operand.ToString() -match 'PrefabRef::m_Prefab' }) { throw 'Preview must never swap the native placement prefab.' }
if (-not ($preview.Methods | Where-Object { $_.Name -eq 'Restore' -and $_.HasBody })) { throw 'Preview cache restoration is required.' }
$fish = $assembly.MainModule.Types | Where-Object FullName -eq 'MunRiverPower.FishPassageUpgradeSystem'
$spillRuntime = $assembly.MainModule.Types | Where-Object FullName -eq 'MunRiverPower.SpillwaySimulationSystem'
if (-not ($spillRuntime.Interfaces | Where-Object { $_.InterfaceType.FullName -eq 'Game.IGPUSystem' })) { throw 'Spillway must use the game GPU simulation queue.' }
if (-not ($loadMethod.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains('RegisterGPUSystem<MunRiverPower.SpillwaySimulationSystem>') })) { throw 'Spillway GPU system is not registered.' }
$gateState = $assembly.MainModule.Types | Where-Object FullName -eq 'MunRiverPower.SpillwayGateState'
if (-not ($gateState.Interfaces | Where-Object { $_.InterfaceType.FullName -eq 'Colossal.Serialization.Entities.ISerializable' })) { throw 'Gate hysteresis state must survive save/load.' }
foreach ($methodName in @('Serialize','Deserialize')) {
    if (-not ($gateState.Methods | Where-Object { $_.Name -eq $methodName -and $_.HasBody })) { throw "Missing gate $methodName" }
}
$adapter = $assembly.MainModule.Types | Where-Object FullName -eq 'MunRiverPower.SpillwayWaterAdapter'
$append = $adapter.Methods | Where-Object Name -eq 'Append'
$dispatches = @($append.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains('CommandBuffer::DispatchCompute') })
if ($dispatches.Count -ne 2) { throw 'Spillway must debit and credit with the same kernel.' }
$game = $resolver.Resolve(($assembly.MainModule.AssemblyReferences | Where-Object Name -eq 'Game'))
$waterType = $game.MainModule.Types | Where-Object FullName -eq 'Game.Simulation.WaterSystem'
$simType = $game.MainModule.Types | Where-Object FullName -eq 'Game.Simulation.WaterSimulation'
foreach ($field in @('m_LastReadyFrame','m_waterSimActiveTilesHelper')) {
    if (-not ($waterType.Fields | Where-Object Name -eq $field)) { throw "Water binding not available in installed game: $field" }
}
foreach ($field in @('m_UpdateShader','m_AddKernel','m_ID_Result','m_ID_Active','m_ID_AddPosition','m_ID_AddRadius','m_ID_AddAmount','m_ID_AddPolluted','m_ID_CellSize','m_ID_Timestep','m_ID_WaterSourceSpeed')) {
    if (-not ($simType.Fields | Where-Object Name -eq $field)) { throw "Water shader binding not available in installed game: $field" }
}
$legacyType = $game.MainModule.Types | Where-Object FullName -eq 'Game.Simulation.WaterSimulationLegacy'
foreach ($field in @('m_UpdateShader','m_AddKernel','m_ID_Result','m_ID_Active','m_ID_AddPosition','m_ID_AddRadius','m_ID_AddAmount','m_ID_AddPolluted','m_ID_CellSize','m_ID_Timestep')) {
    if (-not ($legacyType.Fields | Where-Object Name -eq $field)) { throw "Legacy water shader binding not available: $field" }
}
$fishUpdate = $fish.Methods | Where-Object Name -eq 'OnUpdate'
function Assert-FishAssignment([string]$field, [double]$expectedValue) {
    $instructions = $fishUpdate.Body.Instructions
    for ($index = 1; $index -lt $instructions.Count; $index++) {
        $instruction = $instructions[$index]
        if ($instruction.OpCode.Name -ne 'stfld' -or -not $instruction.Operand.ToString().EndsWith($field)) { continue }
        $literal = $instructions[$index - 1]
        $value = $literal.Operand
        if ($literal.OpCode.Name -match '^ldc.i4.([0-8])$') { $value = [double]$Matches[1] }
        if ($literal.OpCode.Name -notmatch '^ldc\.' -or $null -eq $value -or [double]$value -ne $expectedValue) { throw "Incorrect Fish Passage field: $field" }
        return
    }
    throw "Missing Fish Passage setting: $field"
}
Assert-FishAssignment 'LocalEffectInfo::m_Delta' 2
Assert-FishAssignment 'LocalEffectInfo::m_Radius' 4000
Assert-FishAssignment 'LocalEffectInfo::m_Mode' 1 # Absolute, not Relative (percent).
Assert-FishAssignment 'LocalEffectInfo::m_Type' 3 # Native Wellbeing enum in this game build.
Assert-FishAssignment 'ServiceUpgrade::m_ForbidMultiple' 1
Assert-FishAssignment 'ServiceUpgrade::m_UpgradeCost' 25000
Assert-FishAssignment 'ServiceConsumption::m_Upkeep' 500
$fishLink = $assembly.MainModule.Types | Where-Object FullName -eq 'MunRiverPower.FishPassageUpgradeLinkSystem'
$fishLinkUpdate = $fishLink.Methods | Where-Object Name -eq 'OnUpdate'
foreach ($requiredCall in @('WeirEconomicProfilesSystem::Owns', 'BuildingUpgradeElement::.ctor', 'ServiceUpgradeBuilding::.ctor')) {
    if (-not ($fishLinkUpdate.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains($requiredCall) })) { throw "Missing Fish Passage profile linkage: $requiredCall" }
}
$ratingUpdate = $rating.Methods | Where-Object Name -eq 'OnUpdate'
foreach ($requiredCall in @('HydroSpecification::InstalledMW', 'WeirGeometry::Measure', 'WeirEconomicProfilesSystem::TryProfile')) {
    if (-not ($ratingUpdate.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains($requiredCall) })) { throw "Missing modular economics call: $requiredCall" }
}
$visual = $assembly.MainModule.Types | Where-Object FullName -eq 'MunRiverPower.WeirBuiltVisualSystem'
$visualUpdate = $visual.Methods | Where-Object Name -eq 'OnUpdate'
foreach ($requiredCall in @('WeirNetworkEconomy::Apply', 'DamSceneVisuals::Show')) {
    if (-not ($visualUpdate.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString().Contains($requiredCall) })) { throw "Missing placed-dam visual call: $requiredCall" }
}
if (-not $guard) { throw 'Native placement router missing.' }
$guardCheck = $guard.Methods | Where-Object Name -eq 'PrefabChanged'
if (-not ($guardCheck.Body.Instructions | Where-Object { $_.Operand -and $_.Operand.ToString() -match 'ToolSystem::set_activeTool' })) {
    throw 'Regression: invalid placement data must cancel the native placement tool.'
}
if (-not ($guardCheck.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldsfld' -and $_.Operand.ToString() -match 'WeirFactory::RegisteredRoot' })) {
    throw 'Placement must use the mod dam root.'
}
$expected = @('MunRiverPower.dll','MunRiverPower.pdb','MunRiverPower.dll.config','README.md','TESTING.th.md','SPECIFICATION.th.md','Install.cmd','Install-Local.ps1','Collect-Diagnostics.ps1','verification.json')
foreach ($file in Get-ChildItem -LiteralPath $package -File) {
    if ($file.Name -notin $expected) { throw "Unexpected packaged file: $($file.Name)" }
}
foreach ($required in @('MunRiverPower.dll','README.md','TESTING.th.md','Install.cmd','Install-Local.ps1','Collect-Diagnostics.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $package $required))) { throw "Missing package file: $required" }
}
$report = [ordered]@{
    Version = $assembly.Name.Version.ToString()
    GameBuild = '1.6.2f1 (767.21d1)'
    VerifiedAssemblyReferences = $referenceCount
    ResolvedGameApiMembers = $membersChecked
    EntryPoint = 'Game.Modding.IMod'
    Sha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    Build = 'PASS'
    EmbeddedModels = $resourceNames | Where-Object { $_ -match 'LOD0\.glb$' }
    RegressionChecks = 'PASS: text/upgrade identities, native render/placement, modular rating/pricing, preview restore, Fish Passage, Spillway registration, saved gate state, paired GPU dispatch and installed-game shader field contracts'
    FishPassage = 'Native internal extension; absolute Wellbeing +2, 4000m radius with native distance/efficiency scaling; one per powerhouse; 25000 construction, 500/month upkeep'
    AutomaticSpillway = 'Open <=0.5m freeboard; close at 1m with 5mm tolerance; saved gate state; one bounded debit/credit GPU pulse per new readback. Commands/logic checked, physical water conservation and selected-info rendering NOT TESTED in game.'
    Preview = 'Installed rating by complete 75m units; estimated output uses mod production factor and 3m wet-head gate; mod placement prefab selected'
    Pricing = 'Stock geometry baseline; min(1, installedMW/stockCapacityMW). Below 75m retains stock civil costs. Ratio profiles rounded to 0.000001.'
    RuntimePlacementProductionSaveReload = 'NOT TESTED - user will test'
    Visuals = 'Unity scene meshes follow measured dam curves; road and sidewalk use a safe HDRP Lit material; control rooms are moved to supported service plinths. Runtime appearance awaits testing.'
}
$report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'verification.json') -Encoding utf8
$assembly.Dispose()
$resolver.Dispose()
$report | ConvertTo-Json
