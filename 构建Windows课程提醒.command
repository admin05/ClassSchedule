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
python_cmd="$(command -v python3 || true)"

if [[ -z "$dotnet_cmd" ]]; then
  echo "未找到 dotnet。请先安装 .NET 8 SDK："
  echo "https://dotnet.microsoft.com/download/dotnet/8.0"
  exit 1
fi
if [[ -z "$python_cmd" ]]; then
  echo "未找到 python3，无法创建兼容 Windows 中文文件名的 ZIP。" >&2
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
# Python's ZIP writer marks non-ASCII entry names as UTF-8, unlike Apple's
# bundled zip command, so Windows preserves both Chinese filenames.
rm -f "$archive_path"
"$python_cmd" - "$publish_dir" "$archive_path" <<'PYTHON'
from pathlib import Path
import sys
from zipfile import ZIP_DEFLATED, ZipFile

publish_dir = Path(sys.argv[1])
archive_path = Path(sys.argv[2])
with ZipFile(archive_path, "w", compression=ZIP_DEFLATED, compresslevel=9) as archive:
    for name in ("课程提醒.exe", "课程表.json"):
        archive.write(publish_dir / name, arcname=name)
PYTHON
echo "已生成：$publish_dir/课程提醒.exe"
echo "已压缩发布包：$archive_path"
echo "请将该 ZIP 上传到 GitHub 或复制到 Windows 10 后解压运行。"
