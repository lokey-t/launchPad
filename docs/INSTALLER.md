# 单 EXE 安装包

现行安装程序使用 `Installer/NSIS/LaunchPad.nsi`，保留软件图标、蓝色主按钮、简洁布局和中英文界面，并读取已有语言与深浅色设置。NSIS 使用 LZMA 固实压缩，安装辅助程序与应用共享同一份 .NET 运行环境，因此单 EXE 仍可离线安装，无需用户另装运行环境。

`Installer/Worker/` 复用原有文件校验、退出旧版和事务回滚逻辑，运行于 NSIS 的临时目录，只将清单内的应用文件安装到目标目录。安装辅助程序及其配置不会进入应用安装目录。原 WPF 界面保留用于兼容与回归测试，不再由发行脚本打包。

- 首次默认安装到 `%LOCALAPPDATA%\Programs\LaunchPad`，可更改目录。只写当前用户的安装记录和快捷方式，无需管理员权限；选择只读或受保护目录时提示更换目录。
- 依次检查当前用户安装记录、正在运行的 LaunchPad、开机启动项、备份关联和默认位置。检测到旧版时默认显示“立即更新”并沿用原目录。未运行且未留下记录的便携版，需要选择其原目录。
- 更新先校验所有文件。新版通过现有当前用户命名管道保存配置并退出；旧版使用 Windows Restart Manager 请求正常关闭。无法正常退出时让用户从托盘退出后重试，不强制杀进程。
- 安装只替换清单内程序文件；保留 `%APPDATA%\LaunchPad` 下的配置、主题和备份。安装失败回滚已经替换的文件；回滚失败时保留备份目录并显示路径。
- 不允许覆盖更高版本。相同版本可重新安装修复。不会删除原目录中的未知文件。
- 创建开始菜单快捷方式，可选桌面快捷方式。安装完成后点击“立即启动”。现阶段安装器负责安装/升级，不注册 Windows 卸载入口；移除程序目录与快捷方式即可移除应用，AppData 中的数据仍保留。

## 生成未来发行包

```powershell
./tools/Build-Installer.ps1 -Version 0.97 -Runtime win-x64 -MakeNsis 'C:/Program Files (x86)/NSIS/makensis.exe' -OutputDirectory bin/NSISPreview
```

需要 .NET 8 SDK 和 NSIS 3 Unicode 编译器。省略版本参数使用当前项目版本；脚本同步应用、安装器和清单版本，不更改源码版本号。可通过 `-Dotnet` 指定 dotnet 路径。编译器依次使用 `-MakeNsis`、PATH 中的 makensis，或本地 `bin/BuildTools/nsis-3.12/makensis.exe`。官方工具：[NSIS 下载](https://nsis.sourceforge.io/Download)。未提供 OutputDirectory 时仍输出到 bin/releases/v版本；若已存在同名安装包则停止，避免覆盖已经发行的文件。

输出为 `bin/releases/v版本/LaunchPad-v版本-win-x64-setup.exe` 及 SHA-256 校验文件。生成脚本只打包，不安装，不上传 GitHub/Gitee。正式发布时将同一 EXE 和校验文件上传到两站对应 release，建议按发行流程为 EXE 添加代码签名。

历史 v0.97 WPF 安装包约 129 MB，超过 Gitee 单附件 100 MB 限制，因此该已发行安装包仍由 GitHub 托管。新的 NSIS 构建约 45 MB，未来发行可上传两站，具体以当次构建体积为准；本次替换构建方式不修改历史 Release。`--external-installer` 仅在实际再次超过平台限制时使用。

原有 ZIP 和增量更新方式继续保留。使用脚本输出的应用发布目录运行 `tools/New-UpdateAssets.ps1` 生成 self-contained 增量资源；安装后的应用可继续使用软件内的增量更新。网站仍保留现有两种 ZIP 下载入口，本次没有改变用户选择的下载类型。

## 验证

安装引擎以 ZIP + SHA-256 清单为输入，可用隔离目录验证首次安装、覆盖更新、重复安装、取消、校验失败、路径越界、文件占用和回滚。测试不得对当前用户实际安装目录执行，也不应写用户真实安装注册表或快捷方式。

NSIS 的原始文件输入同样按清单校验大小和 SHA-256。`Tests/Setup/NsisTests.ps1 -Installer <EXE> -Manifest <payload.json>` 使用 `/S /TEST /D=临时目录` 验证真实安装、更新、回滚和重试。`/TEST` 仅允许 `%TEMP%/LaunchPad-NSISTest-*` 目录，跳过关闭旧版、注册和启动，不能用于实际安装。`/LANG=en` 或 `/LANG=zh` 可覆盖安装界面语言。正常 `/D=` 必须位于参数末尾且不加引号，NSIS 会处理其中的空格。
