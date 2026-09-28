# Renders the pictured rooms (UnityProject\Assets\Editor\RoomBaker.cs): twelve modelled rooms, by
# day and by night, each a cube of six faces seen from its centre, into
# UnityProject\Assets\RealGlass\Rooms\RoomAtlas.png (imported as DXT1 with mips, sRGB, no
# power-of-two scaling). Run it after changing a room; then build-bundle.ps1 (the atlas ships in
# the shader bundles), then the mod.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\bake-rooms.ps1

param(
    [string]$Unity = "C:\Program Files\Unity\Editor\Unity.exe"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$project = Join-Path $root "UnityProject"
$log = Join-Path $project "rooms.log"

if (-not (Test-Path $Unity)) { throw "Unity not found at $Unity" }

# The editor compiles every script under Assets first, the preview's copies included.
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "tools\preview\sync-sim.ps1")
if ($LASTEXITCODE -ne 0) { throw "sync-sim.ps1 failed" }

if (Test-Path $log) { Remove-Item $log -Force }
$sw = [Diagnostics.Stopwatch]::StartNew()
# No -nographics: the rooms are rendered on the GPU.
$p = Start-Process -FilePath $Unity -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
    "-batchmode", "-quit",
    "-projectPath", "`"$project`"",
    "-executeMethod", "RoomBaker.Bake",
    "-logFile", "`"$log`"")
"Unity ran for $([int]$sw.Elapsed.TotalSeconds) s, exit code $($p.ExitCode)"

$lines = Get-Content $log
$lines | Where-Object { $_ -match "^ROOM |ROOM BAKE|ROOM ATLAS|error CS|Exception" } | ForEach-Object { $_ }
if ($p.ExitCode -ne 0 -or -not ($lines | Where-Object { $_ -eq "ROOM BAKE OK" })) {
    $lines | Select-Object -Last 40
    throw "the room bake did not finish; see $log"
}

# A contact sheet to judge the rooms by: each room a row, by day then by night, each as its
# left wall, back wall, right wall and floor (faces 3, 0, 2, 5) -- tools\preview\out\rooms.png.
# The layout is RoomBaker's: tile t = (room x 2 + night) x 6 + face at (t % 9, t / 9) x 112 px,
# rows counted from the bottom of the texture (the top of the PNG is its last row).
Add-Type -AssemblyName System.Drawing
$tile = 112; $perRow = 9; $atlasH = 2048
$atlas = [System.Drawing.Image]::FromFile((Join-Path $project "Assets\RealGlass\Rooms\RoomAtlas.png"))
$faces = 3, 0, 2, 5
$sheet = New-Object System.Drawing.Bitmap ($tile * 8 + 8), ($tile * 12)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.Clear([System.Drawing.Color]::Black)
for ($room = 0; $room -lt 12; $room++) {
    for ($night = 0; $night -lt 2; $night++) {
        for ($i = 0; $i -lt 4; $i++) {
            $t = ($room * 2 + $night) * 6 + $faces[$i]
            $sx = ($t % $perRow) * $tile
            $sy = $atlasH - ([math]::Floor($t / $perRow) + 1) * $tile
            $dst = New-Object System.Drawing.Rectangle (($night * 4 + $i) * $tile + $night * 8), ($room * $tile), $tile, $tile
            $g.DrawImage($atlas, $dst, $sx, $sy, $tile, $tile, [System.Drawing.GraphicsUnit]::Pixel)
        }
    }
}
$g.Dispose()
$atlas.Dispose()
$outDir = Join-Path $root "tools\preview\out"
New-Item -ItemType Directory -Force $outDir | Out-Null
$sheetFile = Join-Path $outDir "rooms.png"
$sheet.Save($sheetFile, [System.Drawing.Imaging.ImageFormat]::Png)
$sheet.Dispose()
"saved $sheetFile"
