#!/bin/zsh
set -e

project_dir="$(cd "$(dirname "$0")" && pwd)"
project_file="$project_dir/WindowsCourseReminder/WindowsCourseReminder.csproj"
publish_dir="$project_dir/WindowsCourseReminder/发布-win-x64"
archive_path="$project_dir/WindowsCourseReminder/课程提醒-win-x64.zip"

dotnet_cmd="$(command -v dotnet || true)"
if [[ -z "$dotnet_cmd" && -x "/usr/local/share/dotnet/dotnet" ]]; then
  dotnet_cmd="/usr/local/share/dotnet/dotnet"
fi

if [[ -z "$dotnet_cmd" ]]; then
  echo "未找到 dotnet。请先安装 .NET 8 SDK："
  echo "https://dotnet.microsoft.com/download/dotnet/8.0"
  exit 1
fi

"$dotnet_cmd" publish "$project_file" \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:EnableWindowsTargeting=true \
  --output "$publish_dir"

cp "$project_dir/课程表.json" "$publish_dir/课程表.json"
# Keep the distributable compact: include only the executable and its schedule
# configuration, then use maximum DEFLATE compression for GitHub transfer.
rm -f "$archive_path"
(
  cd "$publish_dir"
  zip -9 -q "$archive_path" 课程提醒.exe 课程表.json
)
echo "已生成：$publish_dir/课程提醒.exe"
echo "已压缩发布包：$archive_path"
echo "请将该 ZIP 上传到 GitHub 或复制到 Windows 10 后解压运行。"
