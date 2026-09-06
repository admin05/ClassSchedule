#!/bin/zsh
set -e

project_dir="$(cd "$(dirname "$0")" && pwd)"
project_file="$project_dir/WindowsCourseReminder/WindowsCourseReminder.csproj"
publish_dir="$project_dir/WindowsCourseReminder/发布-win-x64"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "未找到 dotnet。请先安装 .NET 8 SDK："
  echo "https://dotnet.microsoft.com/download/dotnet/8.0"
  exit 1
fi

dotnet publish "$project_file" \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableWindowsTargeting=true \
  --output "$publish_dir"

cp "$project_dir/课程表.json" "$publish_dir/课程表.json"
echo "已生成：$publish_dir/课程提醒.exe"
echo "请将该目录复制到 Windows 10 后双击 课程提醒.exe。"
