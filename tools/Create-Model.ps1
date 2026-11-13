# Original mesh source. Metres, Y up. Exports editable FBX/OBJ and a geometry preview.
$ErrorActionPreference = 'Stop'
$outDir = Join-Path (Split-Path $PSScriptRoot) 'assets\ModernWeir'
$null = New-Item -ItemType Directory -Path $outDir -Force
Add-Type -AssemblyName System.Drawing
$parts = [Collections.Generic.List[object]]::new()
function Box([string]$name, [double]$x, [double]$y, [double]$z, [double]$w, [double]$h, [double]$d, [string]$mat) {
    $verts = @(
        @(($x-$w/2),$y,($z-$d/2)), @(($x+$w/2),$y,($z-$d/2)),
        @(($x+$w/2),($y+$h),($z-$d/2)), @(($x-$w/2),($y+$h),($z-$d/2)),
        @(($x-$w/2),$y,($z+$d/2)), @(($x+$w/2),$y,($z+$d/2)),
        @(($x+$w/2),($y+$h),($z+$d/2)), @(($x-$w/2),($y+$h),($z+$d/2))
    )
    $parts.Add(@{Name=$name;Vertices=$verts;Mat=$mat})
}
$mats = [ordered]@{ Concrete=@(0.73,0.77,0.76); Steel=@(0.12,0.20,0.25); Teal=@(0.08,0.43,0.49); Glass=@(0.18,0.36,0.43); Safety=@(0.96,0.66,0.18) }
# Eight 12 m bays with 2 m piers. About 120 x 30 m overall with powerhouse.
Box 'Foundation' 0 -1 0 116 1 16 'Concrete'
for ($i=0; $i -le 8; $i++) {
    $x = -48 + $i*12
    Box "Pier_$i" $x 0 0 2 8 13 'Concrete'
    Box "PierCap_$i" $x 8 0 2.5 0.5 14 'Concrete'
    Box "GateLift_$i" $x 8.5 -2 2.3 2.6 3.8 'Steel'
    Box "LiftCover_$i" $x 11.1 -2 2.8 0.35 4.3 'Teal'
}
for ($i=0; $i -lt 8; $i++) {
    $x=-42+$i*12
    Box "Gate_$i" $x 1.5 -2.2 10 5.2 0.4 'Steel'
    for ($j=0; $j -lt 4; $j++) { Box "GateRib_${i}_$j" $x (2+$j*1.2) -1.9 10 0.16 0.35 'Teal' }
    Box "Gantry_$i" $x 10.4 -2 10 0.45 1.3 'Steel'
}
Box 'ServiceDeck' 0 7.5 4 116 0.5 5 'Concrete'
Box 'DeckStripe' 0 8.01 4 113 0.025 0.12 'Safety'
foreach ($z in @(1.6,6.4)) {
    Box "RailTop_$z" 0 9.1 $z 116 0.07 0.07 'Steel'
    Box "RailMid_$z" 0 8.6 $z 116 0.05 0.05 'Steel'
    for ($x=-57; $x -le 57; $x+=3) { Box "Rail_${z}_$x" $x 8 $z 0.07 1.15 0.07 'Steel' }
}
Box 'PowerhouseBase' 57 0 0 18 8 24 'Concrete'
Box 'Powerhouse' 57 8 -1 17 4 20 'Concrete'
Box 'PowerhouseRoof' 57 12 -1 18.5 0.5 21.5 'Steel'
Box 'FacadeBand' 57 10 9.02 15 1.3 0.08 'Glass'
Box 'FacadeAccent' 57 8.8 9.08 15 0.25 0.12 'Teal'
for ($i=0;$i -lt 4;$i++) {
    Box "RoofVent_$i" (52+$i*3) 12.5 -2 1.5 0.7 3 'Steel'
    Box "Intake_$i" (51.5+$i*3.5) 1 -12.08 2.4 4.8 0.16 'Steel'
}
Box 'Abutment' -55 0 0 10 8 18 'Concrete'
Box 'ControlCabin' -55 8 0 8 3 8 'Concrete'
Box 'CabinGlass' -55 9 4.05 6.5 1.3 0.1 'Glass'
Box 'CabinRoof' -55 11 0 9 0.4 9 'Teal'
$faces = @(@(0,3,2,1),@(4,5,6,7),@(0,1,5,4),@(3,7,6,2),@(0,4,7,3),@(1,2,6,5))
$normals = @(@(0,0,-1),@(0,0,1),@(0,-1,0),@(0,1,0),@(-1,0,0),@(1,0,0))
$inv=[Globalization.CultureInfo]::InvariantCulture
function Num($n) { ([double]$n).ToString('0.######',$inv) }
$obj=[Text.StringBuilder]::new("# Mun River modern weir, metres, Y up. Original geometry.`nmtllib ModernWeir.mtl`n")
$mtl=[Text.StringBuilder]::new()
foreach($m in $mats.Keys) { $null=$mtl.AppendLine("newmtl $m`nKd $($mats[$m] -join ' ')`nKa 0.1 0.1 0.1`nKs 0.2 0.2 0.2`nNs 40`n") }
$fbx=[Text.StringBuilder]::new(@'
; FBX 7.4.0 project file
FBXHeaderExtension: { FBXHeaderVersion: 1003
 FBXVersion: 7400
 Creator: "Mun River Power procedural mesh source"
}
GlobalSettings: { Version: 1000
 Properties70: {
  P: "UpAxis", "int", "Integer", "",1
  P: "UpAxisSign", "int", "Integer", "",1
  P: "FrontAxis", "int", "Integer", "",2
  P: "FrontAxisSign", "int", "Integer", "",-1
  P: "CoordAxis", "int", "Integer", "",0
  P: "CoordAxisSign", "int", "Integer", "",1
  P: "UnitScaleFactor", "double", "Number", "",100
 }
}
Objects: {
'@)
$connections=[Text.StringBuilder]::new()
$matIds=@{}; $id=1000
foreach($m in $mats.Keys) {
    $matIds[$m]=$id; $color=$mats[$m] -join ','
    $null=$fbx.AppendLine("Material: $id, `"Material::$m`", `"`" {`n Version: 102`n ShadingModel: `"phong`"`n Properties70: {`n P: `"DiffuseColor`", `"Color`", `"`", `"A`",$color`n }`n}")
    $id++
}
$offset=1
foreach($p in $parts) {
    $null=$obj.AppendLine("o $($p.Name)`nusemtl $($p.Mat)")
    foreach($v in $p.Vertices) { $null=$obj.AppendLine('v '+ (($v | ForEach-Object {Num $_}) -join ' ')) }
    foreach($f in $faces) { $null=$obj.AppendLine('f '+(($f | ForEach-Object {$_+$offset}) -join ' ')) }
    $offset+=8
    $geoId=$id++; $modelId=$id++
    $coords=($p.Vertices | ForEach-Object {$_ | ForEach-Object {Num $_}}) -join ','
    $indices=($faces | ForEach-Object {$f=$_; $f[0];$f[1];$f[2];(-$f[3]-1)}) -join ','
    $ns=($normals | ForEach-Object {$n=$_;1..4 | ForEach-Object {$n -join ','}}) -join ','
    $null=$fbx.AppendLine(@"
Geometry: $geoId, "Geometry::$($p.Name)", "Mesh" {
 Vertices: *24 { a: $coords }
 PolygonVertexIndex: *24 { a: $indices }
 GeometryVersion: 124
 LayerElementNormal: 0 { Version: 101
  Name: "" MappingInformationType: "ByPolygonVertex" ReferenceInformationType: "Direct"
  Normals: *72 { a: $ns }
 }
 LayerElementUV: 0 { Version: 101
  Name: "UVMap" MappingInformationType: "ByPolygonVertex" ReferenceInformationType: "IndexToDirect"
  UV: *8 { a: 0,0,0,1,1,1,1,0 }
  UVIndex: *24 { a: 0,1,2,3,0,1,2,3,0,1,2,3,0,1,2,3,0,1,2,3,0,1,2,3 }
 }
 LayerElementMaterial: 0 { Version: 101
  Name: "" MappingInformationType: "AllSame" ReferenceInformationType: "IndexToDirect"
  Materials: *1 { a: 0 }
 }
 Layer: 0 { Version: 100
  LayerElement: { Type: "LayerElementNormal" TypedIndex: 0 }
  LayerElement: { Type: "LayerElementUV" TypedIndex: 0 }
  LayerElement: { Type: "LayerElementMaterial" TypedIndex: 0 }
 }
}
Model: $modelId, "Model::$($p.Name)", "Mesh" {
 Version: 232
 Properties70: {
 P: "Lcl Translation", "Lcl Translation", "", "A",0,0,0
 P: "Lcl Rotation", "Lcl Rotation", "", "A",0,0,0
 P: "Lcl Scaling", "Lcl Scaling", "", "A",1,1,1
 }
 Shading: T
 Culling: "CullingOff"
}
"@)
    $null=$connections.AppendLine("C: `"OO`",$geoId,$modelId`nC: `"OO`",$($matIds[$p.Mat]),$modelId`nC: `"OO`",$modelId,0")
}
$null=$fbx.AppendLine("}`nConnections: {`n$connections}`n")
[IO.File]::WriteAllText((Join-Path $outDir 'ModernWeir.obj'),$obj.ToString())
[IO.File]::WriteAllText((Join-Path $outDir 'ModernWeir.mtl'),$mtl.ToString())
[IO.File]::WriteAllText((Join-Path $outDir 'ModernWeir.fbx'),$fbx.ToString())
# Render the same mesh with painter's ordering, without implying an in-game screenshot.
$bmp=[Drawing.Bitmap]::new(1600,900)
$g=[Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([Drawing.Color]::FromArgb(229,239,241))
$polys=[Collections.Generic.List[object]]::new()
foreach($p in $parts) {
    for($j=0;$j -lt 6;$j++) {
        if($j -in @(0,2,4)) {continue}
        $points=[Collections.Generic.List[Drawing.PointF]]::new();$depth=0
        foreach($vi in $faces[$j]) {
            $v=$p.Vertices[$vi];$depth+=$v[0]+$v[2]
            $points.Add([Drawing.PointF]::new([single](800+($v[0]-$v[2])*8.4),[single](560+($v[0]+$v[2])*2.6-$v[1]*13)))
        }
        $c=$mats[$p.Mat];$shade=@{1=0.76;3=1.08;5=0.9}[$j]
        $color=[Drawing.Color]::FromArgb([int][Math]::Min(255,$c[0]*255*$shade),[int][Math]::Min(255,$c[1]*255*$shade),[int][Math]::Min(255,$c[2]*255*$shade))
        $polys.Add(@{Depth=$depth/4;Points=$points.ToArray();Color=$color})
    }
}
foreach($p in ($polys | Sort-Object Depth)) {
    $brush=[Drawing.SolidBrush]::new($p.Color);$g.FillPolygon($brush,$p.Points);$brush.Dispose()
}
$font=[Drawing.Font]::new('Segoe UI',28,[Drawing.FontStyle]::Bold)
$small=[Drawing.Font]::new('Segoe UI',16)
$ink=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(29,57,69))
$g.DrawString('MUN RIVER / MODERN LOW-HEAD WEIR',$font,$ink,65,55)
$g.DrawString('Original 3D mesh preview | 8 spillway bays + bank-side powerhouse',$small,$ink,68,110)
$g.DrawString('Visual asset source - not an in-game screenshot or an installed game asset',$small,$ink,68,825)
$bmp.Save((Join-Path $outDir 'Preview.png'),[Drawing.Imaging.ImageFormat]::Png)
$ink.Dispose();$small.Dispose();$font.Dispose();$g.Dispose();$bmp.Dispose()
@{Parts=$parts.Count;Vertices=$parts.Count*8;Triangles=$parts.Count*12;Units='metres';UpAxis='Y';Status='Visual source only; not integrated into the runtime mod'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outDir 'mesh-info.json')
Write-Output "Exported $($parts.Count) mesh parts to $outDir"
