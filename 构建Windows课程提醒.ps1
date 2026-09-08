$ErrorActionPreference = "Stop"

$projectDir = Join-Path $PSScriptRoot "WindowsCourseReminder"
$publishDir = Join-Path $projectDir "发布-win-x64"
$archivePath = Join-Path $projectDir "课程提醒-win-x64.zip"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "未找到 dotnet。请先安装 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0"
}

dotnet publish (Join-Path $projectDir "WindowsCourseReminder.csproj") `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:EnableWindowsTargeting=true `
    --output $publishDir

Copy-Item (Join-Path $PSScriptRoot "课程表.json") $publishDir -Force
# Keep only the executable and schedule configuration in a compressed archive
# for efficient GitHub distribution.
if (Test-Path $archivePath) {
    Remove-Item $archivePath -Force
}
Compress-Archive -Path (Join-Path $publishDir "课程提醒.exe"), (Join-Path $publishDir "课程表.json") `
    -DestinationPath $archivePath -CompressionLevel Optimal
Write-Host "已生成 Windows 发布目录：$publishDir"
Write-Host "已压缩发布包：$archivePath"
Write-Host "请将 ZIP 上传到 GitHub 或复制到 Windows 10 后解压运行。"
