# 打包可分发用的 .cipx。
#
#   1. dotnet publish，触发 PluginSdk 的 CreateCipx；csproj 里的 CleanStalePublishDir
#      会先清掉残留的 publish\，避免把上一轮 publish 的旧 DLL 一起压进包
#      （那会让包体积翻倍，而且包里同时存在两个版本的 MediaIsland.dll）
#   2. 打印体积与 SHA256/MD5，方便贴进 Release 说明
#
# 插件 id 不在这里改：CE 分支的 manifest.yml 已经是 bywhite.mediaisland.ce。
#
# 用法： powershell -ExecutionPolicy Bypass -File Tools\Pack-Cipx.ps1
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "MediaIsland\MediaIsland.csproj"
$cipx = Join-Path $root "MediaIsland\cipx\MediaIsland.cipx"

# SDK 生成校验和时会 shell out 到 powershell.exe；用 $PSHOME 定位，避免 PATH 里没有它时
# 报 MSB3073 9009（交接文档里踩过一次）。
$psExe = Join-Path $PSHOME "powershell.exe"

dotnet publish $proj -c $Configuration `
    -p:CreateCipx=true `
    "-p:PowershellBinaryName=$psExe" `
    -p:TtmlNativeManifest= -p:AudioNativeManifest= -p:NuGetAudit=false

# dotnet 的失败是靠退出码表达的，不会抛异常（$ErrorActionPreference 管不到原生进程）。
# 不拦的话构建失败时下面会拿上一轮的旧包算哈希，看起来像"打包成功"。
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败（exit code $LASTEXITCODE），未生成新包"
}

if (-not (Test-Path $cipx)) {
    throw "打包失败：找不到 $cipx"
}

$file = Get-Item $cipx
$sha = (Get-FileHash $cipx -Algorithm SHA256).Hash
$md5 = (Get-FileHash $cipx -Algorithm MD5).Hash

# SDK 生成的 checksums.md 是 UTF-16LE（它内部用 Out-File 写的），而 Release 说明里承诺了
# 「SHA256 / MD5 见附件」。这里读出全文（BOM 自动识别）、补上 SHA256，再统一写回 UTF-8——
# 直接往 UTF-16 文件里追加 UTF-8 字节会把整个文件弄成乱码。
$sumFile = Join-Path (Split-Path $cipx) "checksums.md"
$text = if (Test-Path $sumFile) { [System.IO.File]::ReadAllText($sumFile) } else { "" }
if ($text -notmatch [regex]::Escape($sha)) {
    $table = "| 文件 | 大小 | MD5 | SHA256 |`r`n| --- | --- | --- | --- |`r`n| {0} | {1:N0} B | {2} | {3} |" -f $file.Name, $file.Length, $md5, $sha
    $text = $text.TrimEnd() + "`r`n`r`n" + $table + "`r`n"
}
[System.IO.File]::WriteAllText($sumFile, $text, (New-Object System.Text.UTF8Encoding($false)))

Write-Output ""
Write-Output ("{0}  {1:N0} B" -f $file.Name, $file.Length)
Write-Output ("SHA256 {0}" -f $sha)
Write-Output ("MD5    {0}" -f $md5)
