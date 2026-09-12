# Windows 10 课程提醒

这是课程表的 Windows 10 版本，使用 .NET 8 WinForms 编写。

Windows 版本与 macOS 版本保持功能和视觉规范一致：提示文字、滚动方向与范围、提醒时机和内嵌字体均同步维护。

## 在 Windows 上构建

1. 在 Windows 电脑上安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（通常选择 **Windows x64 SDK**）。如果是在 macOS 本机交叉编译，请参阅下面的 macOS 说明。
2. 将整个项目目录复制到 Windows 电脑。
3. 在 PowerShell 中进入项目根目录，执行：

```powershell
.\构建Windows课程提醒.ps1
```

4. 构建脚本会生成 `WindowsCourseReminder\课程提醒-win-x64.zip`。该压缩包仅包含 `课程提醒.exe` 和 `课程表.json`，解压后双击 `课程提醒.exe`。

`课程表.json` 必须与 `课程提醒.exe` 位于同一目录。程序会驻留在 Windows 系统托盘中，右键托盘图标或滚动提醒横幅，可以查看下一节课、打开“提醒外观设置…”或退出。

## 在 macOS 上交叉发布 Windows 程序

macOS 可以生成 Windows 自包含程序，但不能在 macOS 上运行或验证 WinForms 界面。请安装与本机芯片匹配的 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)：Apple Silicon 选择 **macOS Arm64**，Intel 选择 **macOS x64**。这是 SDK 的主机架构，发布目标仍然是 Windows `win-x64`。压缩步骤还需要 `python3`，用于以最高 Deflate 级别打包并确保 Windows 正确识别中文文件名。然后在项目根目录执行：

```bash
./构建Windows课程提醒.command
```

也可以使用命令行直接发布：

```bash
dotnet publish WindowsCourseReminder/WindowsCourseReminder.csproj \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:EnableWindowsTargeting=true \
  --output WindowsCourseReminder/发布-win-x64
cp 课程表.json WindowsCourseReminder/发布-win-x64/课程表.json
```

构建脚本还会使用最高压缩级别生成 `WindowsCourseReminder/课程提醒-win-x64.zip`，其中只保留程序和必要的课程配置。向 GitHub 发布 Windows 版本时应上传该 ZIP，不上传未压缩的发布目录；解压后无需另外安装 .NET 运行时。

提示文字默认是“下一节：课程名称（开始时间-结束时间）”。右键托盘图标中的“提醒外观设置…”可以选择 6 套预设配色、使用取色器自定义背景色和字体色，并编辑滚动文字模板。模板支持 `{courseName}`、`{startTime}`、`{endTime}`、`{weekday}`，也支持对应的中文变量。设置保存在 Windows 用户的本地应用数据目录中。

启动时如果下一节课距离超过 5 分钟，会先滚动 3 遍；进入课前 5 分钟后，再在 5、4、3、2、1 分钟分别滚动同样格式的提示，每次滚动 3 遍。提醒横幅为屏幕工作区全宽，从右向左滚动，使用内嵌的 LXGW WenKai Mono Screen 字体，无需在 Windows 电脑另外安装字体。
