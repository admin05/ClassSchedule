# Windows 10 课程提醒

这是课程表的 Windows 10 版本，使用 .NET 8 WinForms 编写。

## 在 Windows 上构建

1. 安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。
2. 将整个项目目录复制到 Windows 电脑。
3. 在 PowerShell 中进入项目根目录，执行：

```powershell
.\构建Windows课程提醒.ps1
```

4. 将 `WindowsCourseReminder\发布-win-x64` 目录复制到任意位置，双击其中的 `课程提醒.exe`。

`课程表.json` 必须与 `课程提醒.exe` 位于同一目录。程序会驻留在 Windows 系统托盘中，右键托盘图标可以查看下一节课或退出。

## 在 macOS 上交叉发布 Windows 程序

macOS 可以生成 Windows 自包含程序，但不能在 macOS 上运行或验证 WinForms 界面。先安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)，然后在项目根目录执行：

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
  -p:EnableWindowsTargeting=true \
  --output WindowsCourseReminder/发布-win-x64
cp 课程表.json WindowsCourseReminder/发布-win-x64/课程表.json
```

生成的 `WindowsCourseReminder/发布-win-x64` 目录可以整体复制到 Windows 10，不需要另外安装 .NET 运行时。

启动时如果下一节课距离超过 5 分钟，会先滚动 3 遍下一节课的时间和名称；进入课前 5 分钟后，再在 5、4、3、2、1 分钟分别滚动提示，每次滚动 3 遍。提醒横幅为黑底白字。
