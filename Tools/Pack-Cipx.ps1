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

if (-not (Test-Path $cipx)) {
    throw "打包失败：找不到 $cipx"
}

$file = Get-Item $cipx
Write-Output ""
Write-Output ("{0}  {1:N0} B" -f $file.Name, $file.Length)
Write-Output ("SHA256 {0}" -f (Get-FileHash $cipx -Algorithm SHA256).Hash)
Write-Output ("MD5    {0}" -f (Get-FileHash $cipx -Algorithm MD5).Hash)
