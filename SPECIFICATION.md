# Mun River Power 0.17.6 specification

Version 0.17.6 uses the mod's dam prefab and displays 75 m modules along both the preview and the constructed dam. It copies road and dam composition for mod segments only, removes the original concrete wall piece, and draws the road surface, continuous sidewalks, and lane markings directly on the mod model. The HDRP Lit color was adjusted against the connected game road. The entire spillway assembly is moved out of the vehicle lane, with at least 0.65 m between its model edge and the lane edge. The inner rail has been removed, the gate lifting equipment is clear of traffic, and the control room sits on a roadside plinth. The game's road network still handles traffic and deletion. Vanilla dams and other roads are unchanged. The result needs in-game visual verification.

## Real-world data and assumptions

Electricity Generating Authority of Thailand (EGAT) [Pak Mun Dam specifications](https://www.egat.co.th/home/pak-mun-dm-specification/), checked November 13, 2026.

Version 0.8.2 makes the upgrade meshes attached to the powerhouse at the dam end smaller and positions them along the edge of the original building model. Their in-game meshes therefore do not occupy the full 75–90 m span of the Blender source, though they retain the gate and fish passage shapes. Version 0.8.1 changed the GLB reader to Newtonsoft.Json and prevented model loading errors from stopping `PrefabUpdate`. If a model cannot be read, the upgrade registers without 3D visuals and logs an error. GLB files are embedded in the DLL, loaded into the game's geometry asset database, and attached to `BuildingExtensionPrefab.m_Meshes`. They use surface materials from the base powerhouse prefab. Position and orientation still need in-game checks.

Pak Mun Dam is 300 m long, 17 m high from its foundation, and 6 m wide at the crest. It has four 34 MW generators, totaling 136 MW. EGAT gives a minimum water level difference for generation of 3 m and approximately 280 GWh/year of energy.

The 17 m height is not the operating head at all times, and 3 m of head does not automatically produce 136 MW.

**The mod allocates 75 m per turbine, calculated as 300/4.** This is a game layout assumption, not verified turbine spacing. Capacity increases in steps, is not limited to four turbines, and does not yet add or remove separate visible turbine models.

| Completed dam length | Turbines | Installed capacity |
|---|---:|---:|
| Under 75 m | 0 | 0 MW |
| 75 to under 150 m | 1 | 34 MW |
| 150 to under 225 m | 2 | 68 MW |
| 225 to under 300 m | 3 | 102 MW |
| 300 to under 375 m | 4 | 136 MW |
| 375 to under 450 m | 5 | 170 MW |
| 600 m | 8 | 272 MW |

Length follows the actual dam curve in the powerhouse's `SubNet`, not a straight line between its ends or the cached length adjusted for capacity control. A partial interval adds no turbine. If the game creates several independent powerhouses, each counts only its own dam segment.

## Construction cost and upkeep

`R = min(1, mod rating / original dam rating for the same shape)`

`Mod construction cost = original construction quote × R`

`Mod upkeep = original upkeep × R`

Illustrative example: a 300 m mod dam rated at 136 MW has `R = 0.5` if the original dam at the same alignment and level is rated at 272 MW. An original construction quote of 2,000,000 becomes 1,000,000, and upkeep of 10,000 per period becomes 5,000. These are game currency figures, not fixed prices for every site.

- The original dam capacity baseline uses coefficients from the installed game's prefab and structural area calculated from terrain height by the original system. It does not use current generation or lower prices for drought or missing workers.
- When mod capacity exceeds the original baseline, the original price applies. If the baseline is unavailable or the dam is shorter than 75 m, the original civil works cost applies so a water barrier is not free.
- The mod adjusts `Temp.m_Cost`, which the game actually charges, after its alignment and building calculations, then checks it again before `ToolApplySystem` deducts money. It restores the baseline before recalculating to avoid repeated discounts.
- Only the mod dam alignment and powerhouse are adjusted. Nearby roads, power lines, and other buildings are not discounted. Refunds for identifiable mod pieces use the same ratio.
- Building upkeep uses private prefabs per ratio for both `ConsumptionData` and `ServiceUpkeepData`. Dam alignment upkeep uses a private composition that changes only upkeep; shared vanilla dam data is untouched.
- Building upkeep ratios are rounded to 0.000001; prices and refunds are rounded to game currency units. Actual charges still depend on the game's budget rules and rounding.
- Older mod buildings acquire the new specification when the simulation runs. Past construction costs are not refunded. Upkeep may briefly use the original baseline until ratio data becomes available.
- Ratios are written to `ModsData/MunRiverPower/economic-profiles.json` before the save references them, and the corresponding prefabs are created before save loading. Back up this folder with the city when moving computers. If the file cannot be written, the mod does not switch to an unrecorded prefab and logs the error.

## Power generation

Power uses `P = rho × g × Q × H × eta`, with `rho = 1000 kg/m³`, `g = 9.81 m/s²`, and assumed `eta = 90%`. This is not a confirmed efficiency for the real Pak Mun turbines.

10,000 internal game units equal 1 MW (checked against the installed game's formatter). The `Q × H` coefficient is 88.29 game units. For example, `Q = 100 m³/s` and `H = 3 m` produce about 2.6487 MW.

The generation threshold samples five wet cross sections per alignment. If none reaches 3 m of head, available generation is disabled. Actual `Q × H` generation still uses the game's sampled water, velocity, and depth. The mod adjusts the capacity calculation cache to cap output by turbine count without moving structures or terrain. Operating bonuses above 100% cannot raise installed capacity; lower efficiency can reduce available output.

The game's water model is approximate and does not guarantee 280 GWh/year. Prices are not real construction costs in Thailand. The preview shows estimated generation and turbine-count capacity, without raising capacity for deeper foundations. It uses the game's `m_Estimate` with the mod coefficient, caps it at installed capacity, and shows zero if the current wet head is below 3 m. Water can change after construction, so the preview is no guarantee of immediate output. The underlying water model remains the game's.

## Verification status

The build, API references, and offline checks for formulas, length steps, and separation from vanilla dams pass. Actual charges, upkeep, alignment connections, and save/load still need in-game testing per [TESTING.md](TESTING.md).

The 0.5.1 preview fix adjusts only the temporary cache before the tooltip reads it, then immediately restores it. It does not change the placement tool's prefab or let display cache values affect pricing or construction. Offline checks covered a 401 m alignment with 307/568 MW estimates capped to at most 170/170 MW; that terrain has not yet been tested in-game.

## Fish Passage in version 0.6.0

A passage suited to local fish is the concept behind this upgrade. The Well-being effect is a game balance value, not a claim that a real passage changes residents' happiness by this amount. Fish species and populations are not simulated.

It uses `BuildingExtensionPrefab` with `ServiceUpgrade` as an internal building upgrade, requiring no separate building. At version 0.6.0 it had no new 3D model. Its stable identifier is `BuildingExtensionPrefab:MunRiverPower.FishPassage`. One can be installed per mod powerhouse, and vanilla dams do not receive it.

Effect data uses `LocalEffects`, `LocalModifierType.Wellbeing`, `ModifierValueMode.Absolute`, `delta = 2`, `radius = 4000 m`, and `RadiusCombineMode.Maximal`. The normal `ServiceUpgrade` system adds `LocalEffectProvider` to the parent building upon purchase, and the game's local effect system reads `InstalledUpgrade`. No effect is granted before purchase.

At 100% efficiency, the effect is +2 at the center and decreases with distance as `2 × max(0, 1 − (distance/4000)²)`, reaching zero at the radius boundary. Houses within the circle do not all receive +2. Changing powerhouse efficiency lets the game adjust both radius and strength under its normal rules.

Initial balance price is ₡25,000 once, with ₡500/month additional upkeep, using the normal `ServiceUpgradeData` and `ServiceUpkeepData`. Upgrade costs are separate from the dam civil works discount.

`BuildingUpgradeElement` and `ServiceUpgradeBuilding` entries are attached to the main prefab and all existing or newly created upkeep profiles, with duplicate checks. Changing a cost profile preserves the building's `InstalledUpgrade`; Fish Passage does not need repurchase.

API calls and configuration for effects, price, and duplicate prevention were checked. Purchase, happiness changes, upgrade removal, and city save/load had not yet been confirmed in-game for this version.

Version 0.6.1 fixed the upgrade panel name and description: `PrefabUISystem` uses `Assets.UPGRADE_NAME[MunRiverPower.FishPassage]` and `Assets.UPGRADE_DESCRIPTION[MunRiverPower.FishPassage]`, separate from the general building `Assets.NAME` and `Assets.DESCRIPTION`. Both key sets are registered for supported game languages. Save identifier, price, and effect are unchanged.

## Automatic Spillway in version 0.7.0

This internal building upgrade has the stable identifier `BuildingExtensionPrefab:MunRiverPower.AutomaticSpillway`, an initial balance price of ₡100,000, and ₡1,000/month additional upkeep. One is allowed per mod powerhouse. It is unavailable on vanilla dams and adds no turbine capacity.

### Freeboard and control rules

`Freeboard = approximate crest level − upstream water surface level`, in meters. A negative value means water exceeds the reference crest. The measurement reads native `SubNet` curves and dam composition directly, not the cache adjusted to cap turbine capacity, and does not use Pak Mun's 17 m foundation height.

The reference crest is `curve.y + NetCompositionData.SurfaceHeight.min + TerrainComposition.MaxHeightOffset.y`, the structural level used by the original hydro system. It is not the top of a model pole or rail and must be checked against visible overtopping in-game. The mod samples five cross sections per native edge and uses the lowest freeboard among wet sections.

The gate opens at freeboard `<= 0.5 m` and stays open until it returns to `1.0 m`. A 0.005 m tolerance prevents the gate staying open because of floating point limits or tapering near the target. `SpillwayGateState` on the upgrade piece is saved through `ISerializable`, preserving state between thresholds on reload.

Before releasing, the mod checks that the upgrade and parent building are enabled, both intake and outlet are wet, and upstream is at least 0.1 m higher than downstream. It does not pump water uphill. It cannot guarantee flood prevention in every situation or preserve MW output as head changes.

### Water transfer

In game version 1.6.2f1, freshwater sources and pumps use different kernels or interpretations. Instead of pairing positive and negative `WaterSourceData` and assuming they cancel, the mod uses the existing additive kernel and submits paired upstream removal and downstream addition commands to the game's `IGPUSystem` queue. The commands have matching radius and pulse size, and their positions share a grid phase so area weights match. Added pollution is zero. No permanent source or pump entities remain in the save, and the mod does not change sea level or mapwide water simulation speed.

Pulses are limited to 0.025 m per fresh water reading and 2% of the minimum observed depth, and taper near the closing threshold. Both areas need at least 0.25 m of water. The mod checks the grid over intake and outlet areas and prevents multiple powerhouses from reusing the same water reading for repeated transfers. It submits no release commands while the game is paused.

Compute shader bindings are read from the installed game and checked for expected names and types. On a mismatch or error, extra release is disabled and an unsupported status is shown; normal power generation continues. Compatibility with other water engine versions is not guaranteed.

Offline checks confirm opening and closing rules, pulse tapering, protection against duplicate readings, paired removal and addition commands, and field names in the installed game. **Actual net water quantity after GPU simulation has not been verified.** Shallow-water clamping, floating point differences, and other game flows can change the result; test it in a city.

### Building information panel

The mod uses the game's `DescriptionSection` renderer for freeboard and status, without an external UI extension. Text is available in Thai and English according to the game language and is prepared in advance to avoid reloading a dictionary whenever the water level changes. Display values increment by 0.1 m from -5 to 20 m, with an out-of-range message beyond that; control uses unrounded values.

At version 0.7.0 there was no new 3D spillway model. Upgrade panel, freeboard text, water movement, disabling, removal, pause, and save/load still needed testing before this version could be confirmed in-game.

Bindings were checked for both `WaterSimulationLegacy`, the default in the installed game, and the alternative `WaterSimulation` in the same build. The mod uses the active engine's additive kernel without changing the player's engine setting. Other versions are unverified. The initial `SPILLWAY SENSOR` log records crest and water levels and engine name for visual comparison.
