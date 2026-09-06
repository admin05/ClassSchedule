#!/bin/zsh
set -e

project_dir="$(cd "$(dirname "$0")" && pwd)"
app_dir="$project_dir/课程提醒.app"
contents_dir="$app_dir/Contents"
macos_dir="$contents_dir/MacOS"

mkdir -p "$macos_dir"
cache_dir="${TMPDIR:-/tmp}/classschedule-clang-cache"
mkdir -p "$cache_dir"
CLANG_MODULE_CACHE_PATH="$cache_dir" swiftc "$project_dir/课程提醒.swift" -o "$macos_dir/课程提醒" -framework Cocoa

cat > "$contents_dir/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDisplayName</key>
    <string>课程提醒</string>
    <key>CFBundleExecutable</key>
    <string>课程提醒</string>
    <key>CFBundleIdentifier</key>
    <string>com.admin05.classschedule.reminder</string>
    <key>CFBundleName</key>
    <string>课程提醒</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSUIElement</key>
    <true/>
</dict>
</plist>
PLIST

chmod +x "$macos_dir/课程提醒"
if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "$app_dir" >/dev/null
fi
echo "已构建：$app_dir"
echo "双击 课程提醒.app 启动；课程配置应放在同一目录的 课程表.json。"
