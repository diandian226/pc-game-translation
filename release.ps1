# 发布新版本到 GitHub Releases（工具更新、补丁更新都用它）
#
#   .\release.ps1 -Notes "修复了……"            构建、签名、创建 Release 并上传
#   .\release.ps1 -Notes "……" -DryRun          只在 build\release-out 生成文件，不上传
#
# 发补丁更新：改完译文后把 catalog.json 的 revision +1、对应 patches[].patchVersion 改新，再运行本脚本。
# 发工具更新：把 FMGPatcher.csproj 的 <Version> 改新，再运行本脚本。两者可以一起发。
#
# 签名私钥在 signing\update-signing.private.xml（不在仓库里），丢失后已发出的工具将无法再接受更新。
param(
    [Parameter(Mandatory = $true)][string]$Notes,
    [string]$MinToolVersion = "0.2.0",
    [switch]$DryRun,
    [switch]$SkipBuild
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$repo = "diandian226/pc-game-translation"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$utf8 = New-Object Text.UTF8Encoding $false

# ---------- 1. 构建 ----------
if (-not $SkipBuild) { & (Join-Path $root "build.ps1") }
$exe = Join-Path $root "build\FMGPatcher.exe"
$version = (Get-Item $exe).VersionInfo.FileVersion -replace '^(\d+\.\d+\.\d+).*', '$1'
$catalogText = [IO.File]::ReadAllText((Join-Path $root "catalog.json"), [Text.Encoding]::UTF8).Trim()
$catalog = $catalogText | ConvertFrom-Json
$revision = [int]$catalog.revision
Write-Host "工具 v$version · 补丁清单第 $revision 版"

# ---------- 2. 标签 ----------
$tag = "v$version"
function Get-Token {
    $env:GCM_INTERACTIVE = "never"
    $out = "protocol=https`nhost=github.com`n`n" | git -c http.proxy= -c https.proxy= credential fill 2>$null
    $t = ($out | Where-Object { $_ -like "password=*" }) -replace '^password=', ''
    if (-not $t) { throw "没有找到 GitHub 登录信息，请先在本仓库执行一次 git push 完成登录" }
    return $t
}
$headers = $null
if (-not $DryRun) {
    $headers = @{ Authorization = "Bearer $(Get-Token)"; Accept = "application/vnd.github+json"; "User-Agent" = "fmgpatcher-release" }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $exists = $true
    try { Invoke-RestMethod "https://api.github.com/repos/$repo/releases/tags/$tag" -Headers $headers | Out-Null } catch { $exists = $false }
    if ($exists) { $tag = "v$version-r$revision" }
    try { Invoke-RestMethod "https://api.github.com/repos/$repo/releases/tags/$tag" -Headers $headers | Out-Null; throw "标签 $tag 已存在：请提高工具版本号或 catalog.json 的 revision" } catch { if ($_.Exception.Message -like "标签*") { throw } }
}
Write-Host "发布标签 $tag"
$dl = "https://github.com/$repo/releases/download/$tag"

# ---------- 3. 整理发布文件 ----------
$out = Join-Path $root "build\release-out"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory $out | Out-Null

function Hash($p) { (Get-FileHash $p -Algorithm SHA256).Hash.ToLower() }
function J($s) { $s | ConvertTo-Json }   # 把字符串转成带引号、已转义的 JSON 字面量

Copy-Item $exe (Join-Path $out "FMGPatcher.exe")
$payloadEntries = @()
foreach ($g in $catalog.games) {
    foreach ($p in $g.patches) {
        $src = Join-Path $root ("publish\" + ($p.payload -replace '/', '\'))
        if (-not (Test-Path $src)) { throw "缺少补丁包 $src" }
        $name = Split-Path $src -Leaf
        Copy-Item $src (Join-Path $out $name)
        $payloadEntries += "    { ""path"": $(J $p.payload), ""url"": $(J "$dl/$name"), ""size"": $((Get-Item $src).Length), ""sha256"": ""$(Hash $src)"" }"
    }
}

# 给新用户的完整工具包（带版本号的一份，外加固定文件名的一份，方便分享永久链接）
$stage = Join-Path $out "_stage\PC汉化工具-v$version"
New-Item -ItemType Directory -Force (Join-Path $stage "payload") | Out-Null
Copy-Item $exe, (Join-Path $root "build\FMGPatcher.exe.config"), (Join-Path $root "catalog.json") $stage
Copy-Item (Join-Path $root "publish\payload\*") (Join-Path $stage "payload")
$disclaimer = Join-Path $root "..\FMGWorld-汉化补丁-0.2.1\DISCLAIMER.txt"
if (Test-Path $disclaimer) { Copy-Item $disclaimer (Join-Path $stage "免责声明.txt") }
$toolZip = Join-Path $out "FMGWorld-PC-zh-tool-v$version.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $toolZip, 'Optimal', $true)
Copy-Item $toolZip (Join-Path $out "FMGWorld-PC-zh-tool.zip")
Remove-Item (Join-Path $out "_stage") -Recurse -Force

# ---------- 4. update.json ----------
$exeOut = Join-Path $out "FMGPatcher.exe"
$manifest = @"
{
  "schema": 1,
  "published": "$((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))",
  "notes": $(J $Notes),
  "page": $(J "https://github.com/$repo/releases/tag/$tag"),
  "tool": { "version": "$version", "url": $(J "$dl/FMGPatcher.exe"), "size": $((Get-Item $exeOut).Length), "sha256": "$(Hash $exeOut)" },
  "minToolVersion": "$MinToolVersion",
  "catalog": $catalogText,
  "payloads": [
$($payloadEntries -join ",`n")
  ]
}
"@
$manifestPath = Join-Path $out "update.json"
[IO.File]::WriteAllText($manifestPath, $manifest, $utf8)
$null = $manifest | ConvertFrom-Json   # 格式自检

# ---------- 5. 签名并自检 ----------
$privPath = Join-Path $root "signing\update-signing.private.xml"
if (-not (Test-Path $privPath)) { throw "找不到签名私钥 $privPath" }
$bytes = [IO.File]::ReadAllBytes($manifestPath)
$rsa = New-Object Security.Cryptography.RSACryptoServiceProvider
$rsa.FromXmlString([IO.File]::ReadAllText($privPath))
$sig = $rsa.SignData($bytes, "SHA256")
[IO.File]::WriteAllText("$manifestPath.sig", [Convert]::ToBase64String($sig), $utf8)
$pub = New-Object Security.Cryptography.RSACryptoServiceProvider
$pub.FromXmlString([IO.File]::ReadAllText((Join-Path $root "signing\update-signing.public.xml")))
if (-not $pub.VerifyData($bytes, "SHA256", $sig)) { throw "签名自检失败" }
$embedded = Select-String -Path (Join-Path $root "src\Core\UpdateKey.cs") -Pattern $pub.ToXmlString($false) -SimpleMatch -Quiet
if (-not $embedded) { throw "工具内置公钥与 signing\update-signing.public.xml 不一致" }
Write-Host "update.json 已签名"

if ($DryRun) { Write-Host "DryRun：文件在 $out，未上传"; return }

# ---------- 6. 创建 Release 并上传 ----------
$body = @"
$Notes

**非官方汉化 · 需自备正版 · 仅供学习交流**。原游戏含成人内容，仅限 18 岁以上使用。使用前请阅读压缩包内的《免责声明》。

## 下载

- 第一次使用：下载 ``FMGWorld-PC-zh-tool.zip``，解压后双击 ``FMGPatcher.exe``
- 已经在用 v0.2.0 及以上的工具：打开工具会自动提示更新，不用重新下载

其余附件（``update.json``、``FMGPatcher.exe``、补丁包）供工具自动更新使用。

汉化已获原作者许可，版权归原开发者所有。
"@
$req = @{ tag_name = $tag; target_commitish = "main"; name = "FMGWorld 电脑版汉化 · 工具 v$version · 补丁清单 r$revision"; body = $body; draft = $false; prerelease = $false; make_latest = "true" } | ConvertTo-Json
$rel = Invoke-RestMethod "https://api.github.com/repos/$repo/releases" -Method Post -Headers $headers -Body ([Text.Encoding]::UTF8.GetBytes($req)) -ContentType "application/json; charset=utf-8"
Write-Host "已创建 $($rel.html_url)"

# update.json 最后上传：清单出现时，它引用的文件都已就位
$order = Get-ChildItem $out -File | Sort-Object { if ($_.Name -like "update.json*") { 1 } else { 0 } }, Name
foreach ($f in $order) {
    $type = if ($f.Extension -eq ".zip") { "application/zip" } elseif ($f.Extension -eq ".exe") { "application/vnd.microsoft.portable-executable" } else { "application/octet-stream" }
    $url = "https://uploads.github.com/repos/$repo/releases/$($rel.id)/assets?name=$([uri]::EscapeDataString($f.Name))"
    $a = Invoke-RestMethod $url -Method Post -Headers $headers -InFile $f.FullName -ContentType $type
    Write-Host ("  上传 {0}  {1:N1} MB" -f $a.name, ($a.size / 1MB))
}
Write-Host "完成：$($rel.html_url)"
