<#
  Re-generates the animated character from the Blender sources and puts the FBX into the Unity project.
    Blender\build.py  -> Blender\Vince_Slim_Walker.blend          (the character: mesh, materials, rig, weights)
    Blender\anim.py   -> Vince_Slim_Walker_Animated.blend + .fbx  (props, all game clips, merged meshes + LODs)
  Then open Unity and run  VinceNoir > 1. Setup Everything  (or .\Tools\BuildAndTest.ps1).

  Examples
    .\Tools\BlenderExport.ps1                 # animations + FBX only (uses the existing Vince_Slim_Walker.blend)
    .\Tools\BlenderExport.ps1 -Model          # rebuild the character model first
    .\Tools\BlenderExport.ps1 -Preview        # also render Blender\out\anim_preview.png (contact sheet of poses)
#>
param([switch]$Model, [switch]$Preview, [string]$BlenderExe = "")
$ErrorActionPreference = "Stop"
$project = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$bl = Join-Path $project "Blender"
if (-not $BlenderExe) {
    $found = Get-ChildItem "C:\Program Files\Blender Foundation" -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
    if ($found) { $BlenderExe = Join-Path $found.FullName "blender.exe" }
}
if (-not (Test-Path $BlenderExe)) { throw "blender.exe not found (Blender 5.x) - pass -BlenderExe" }
# Blender's Python can fail on very long paths: work from a short temp folder
$tmp = Join-Path $env:TEMP "vince_blender"; New-Item -ItemType Directory -Force $tmp | Out-Null
Copy-Item (Join-Path $bl "*.py") $tmp -Force
if ($Model) {
    & $BlenderExe -b --python (Join-Path $tmp "build.py") -- $tmp | Select-String "SAVED|TOTAL|Error|Traceback"
    Copy-Item (Join-Path $tmp "Vince_Slim_Walker.blend") $bl -Force
}
$out = Join-Path $tmp "out"; New-Item -ItemType Directory -Force $out | Out-Null
$extra = @(); if ($Preview) { $extra += "preview" }
& $BlenderExe -b (Join-Path $bl "Vince_Slim_Walker.blend") --python (Join-Path $tmp "anim.py") -- $out @extra | Select-String "EXPORTED|PREVIEW|GAME MESH|Error|Traceback"
Copy-Item (Join-Path $out "Vince_Slim_Walker_Animated.fbx") (Join-Path $project "Assets\VinceNoir\Models\Vince_Slim_Walker_Animated.fbx") -Force
Copy-Item (Join-Path $out "Vince_Slim_Walker_Animated.blend") $bl -Force
if ($Preview) { New-Item -ItemType Directory -Force (Join-Path $bl "out") | Out-Null; Copy-Item (Join-Path $out "anim_preview.png") (Join-Path $bl "out") -Force }
Write-Host "FBX copied to Assets\VinceNoir\Models. Now run VinceNoir > 1. Setup Everything in Unity."
