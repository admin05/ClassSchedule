$ErrorActionPreference = "Stop"

$projectDir = Join-Path $PSScriptRoot "WindowsCourseReminder"
$publishDir = Join-Path $projectDir "发布-win-x64"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "未找到 dotnet。请先安装 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0"
}

dotnet publish (Join-Path $projectDir "WindowsCourseReminder.csproj") `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableWindowsTargeting=true `
    --output $publishDir

Copy-Item (Join-Path $PSScriptRoot "课程表.json") $publishDir -Force
Write-Host "已生成 Windows 发布目录：$publishDir"
Write-Host "请将该目录整体复制到 Windows 10，双击 课程提醒.exe。"
