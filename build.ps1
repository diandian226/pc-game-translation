# 构建 LocaForge 汉化工坊
#   1. 生成 exe 图标（来自安卓版的启动图标原稿）
#   2. 把 PC 补丁目录打包成 payload zip
#   3. 编译并把 exe、catalog.json、payload 放进 publish\
param(
    [string]$PatchSource = (Join-Path $PSScriptRoot "..\FMGWorld-汉化补丁-0.2.1"),
    [string]$IconSource  = (Join-Path $PSScriptRoot "..\FMGWorld_手机汉化工具\artwork\launcher-source.png")
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem

$assets = Join-Path $PSScriptRoot "assets"
New-Item -ItemType Directory -Force $assets | Out-Null

# ---------- 1. 图标：多尺寸 PNG 封装进 .ico ----------
$ico = Join-Path $assets "app.ico"
if (-not (Test-Path $ico)) {
    $src = [System.Drawing.Image]::FromFile((Resolve-Path $IconSource))
    $sizes = 16, 20, 24, 32, 40, 48, 64, 256
    $pngs = foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = "HighQualityBicubic"; $g.SmoothingMode = "AntiAlias"; $g.PixelOffsetMode = "HighQuality"
        $g.DrawImage($src, 0, 0, $s, $s); $g.Dispose()
        $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        , $ms.ToArray()
    }
    $src.Dispose()
    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $out
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
        $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $w.Write($p) }
    [IO.File]::WriteAllBytes($ico, $out.ToArray())
    Write-Host "图标已生成 $ico"
}

# ---------- 2. 补丁包 ----------
$publish = Join-Path $PSScriptRoot "publish"
$payloadDir = Join-Path $publish "payload"
New-Item -ItemType Directory -Force $payloadDir | Out-Null
$zip = Join-Path $payloadDir "fmgworld-zh-0.2.1.zip"
if (Test-Path $zip) { Remove-Item $zip }
$stage = Join-Path $env:TEMP ("fmgpatch-" + [guid]::NewGuid())
New-Item -ItemType Directory $stage | Out-Null
# 只打包 game 目录（说明文档不进游戏目录）
Copy-Item (Join-Path $PatchSource "game") (Join-Path $stage "game") -Recurse
Get-ChildItem $stage -Recurse -Include *.rpyc, *.bak | Remove-Item
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $stage -Recurse -Force
Write-Host ("补丁包 {0:N1} MB" -f ((Get-Item $zip).Length / 1MB))

# ---------- 3. 编译 ----------
dotnet build (Join-Path $PSScriptRoot "LocaForge.csproj") -c Release -o (Join-Path $PSScriptRoot "build") --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "编译失败" }
Copy-Item (Join-Path $PSScriptRoot "build\LocaForge.exe") $publish -Force
Copy-Item (Join-Path $PSScriptRoot "build\LocaForge.exe.config") $publish -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $PSScriptRoot "catalog.json") $publish -Force
Write-Host "完成：$publish"
