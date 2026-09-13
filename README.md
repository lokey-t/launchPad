# LaunchPad v0.97

**中文** · [English](README.en.md)

轻量的 Windows 应用启动台，通过快捷键呼出，用分类和文件夹整理应用，支持连续拖拽、主题定制和动态背景。

## 预览

| 主页（浅色磨砂） | 主页（深色磨砂） |
| --- | --- |
| ![主页浅色](docs/screenshots/home-light.png) | ![主页深色](docs/screenshots/home-dark.png) |

| 主题中心 | 设置 |
| --- | --- |
| ![主题中心](docs/screenshots/theme-center.png) | ![设置](docs/screenshots/settings.png) |

## 下载与运行

从 [GitHub Releases](https://github.com/lokey-t/launchPad/releases/tag/v0.97) 或 [Gitee Releases](https://gitee.com/lokey-t/launchPad/releases/tag/v0.97) 下载：

| 发行包 | 说明 |
| --- | --- |
| `LaunchPad-v0.97-win-x64-setup.exe` | 推荐。单 EXE 离线安装，自带运行环境，可选目录；识别旧版并更新，保留配置和备份。 |
| `LaunchPad-v0.97-win-x64.zip` | 便携完整版。包含 .NET 运行环境，解压后直接运行 `LaunchPad.exe`。 |
| `LaunchPad-v0.97-win-x64-framework-dependent.zip` | 精简包，需要 .NET 8 Desktop Runtime（x64）。解压后保留整个文件夹运行。 |

支持 Windows 10 / 11 x64。安装包可自动识别旧版本并更新，未注册且未运行的便携版需选择原目录；使用 ZIP 覆盖升级前，从托盘退出旧版本。默认 **Ctrl + Space** 呼出或隐藏，可在设置中修改。关闭主窗口后应用驻留托盘，通过托盘菜单彻底退出。

## 功能

单 EXE 安装包约 129 MB，超过 Gitee 的 100 MB 附件上限，请使用 [GitHub 安装包直达链接](https://github.com/lokey-t/launchPad/releases/download/v0.97/LaunchPad-v0.97-win-x64-setup.exe)。两种 ZIP 可从任一站点下载。

- **应用与文件夹**：拖入文件或快捷方式即可添加；支持搜索、单击或双击启动、右键修改显示名称和图标。重复导入通过 Toast 显示已有条目及分类。
- **连续拖拽**：主网格和文件夹内有排序让位动画。直接放到文件夹即可移入；悬停后展开并选择位置。从文件夹拖出时自动收起，可继续移动。按 Esc 取消内部拖动。
- **分类管理**：分类栏支持左右拖动、滚轮浏览和拖入文件时边缘滚动。分类管理支持拖动排序；条目右键菜单可快速修改分类。
- **悬停时间**：文件夹展开和分类切换分别可设为 0.3–5 秒，在 0.5、1、2 秒附近轻微吸附。悬停浏览不会提前改变条目归属。
- **快捷键**：全局呼出和分类快捷键集中管理。分类快捷键通过弹窗设置、修改或清除；设置过程中快捷键临时失效，确认后立即生效。可选择是否允许分类快捷键关闭窗口。
- **图标外观**：支持底色不透明度、无边框 / 阴影边框 / 直线边框样式选择；阴影方向通过可视化弹窗拖动调节，附带强度滑块与实时预览。可一键恢复默认。
- **界面动效**：关闭、快速、均衡、优化四种模式；连续文件夹变形过渡、细圆角滚动条和滚轮缓动。支持图标尺寸、窗口位置（含“上次位置”）及自动隐藏设置。
- **布局优化**：悬浮式滚动条不挤占内容宽度；小 / 中 / 大图标模式收紧间距，每行容纳更多图标；内容可滚动时底部信息条上方显示柔和阴影。
- **其他**：开机自启、托盘菜单、再次运行直接打开已有主界面、新建分类弹窗点击空白关闭。应用内禁用 Tab 焦点切换与 Alt 按键提示，不改变 Windows 设置。

## v0.97 新增

- 自动更新提示与关于页手动检查：忽略本版本、下次再说、双站自主下载、按文件增量更新及失败回滚。
- 首次运行引导，可从关于页重新进入；应用快捷方式默认解析原文件，可在通用关闭。
- 新图标、统一托盘菜单，拖动预览跟随图标风格，主页右键按添加时间、名称、打开时间排序。
- 通用设置切换中英文；主题预设可保存、删除并以 `.qdtstylebackup` 导入导出，包含素材且不覆盖其他配置。未知新版本配置保留供未来使用。
- 恢复出厂设置保留备份点，升级保留应用、分类、主题与素材。

## 主题中心

主页“主题”或设置 → 外观与动效进入；主页按钮可以隐藏。

1. 选择“全局默认”或具体分类。已单独自定义过主题的分类会在下拉框中标记。
2. 使用云白、午夜、海盐、苔绿、暮紫、蔷薇预设，或编辑底色、文字色和强调色。
3. 上传背景并调整遮罩。支持 PNG / JPG / BMP、GIF，以及 MP4 / WMV / AVI / MOV / M4V 视频；视频播放取决于 Windows 解码支持。
4. 点击“应用并保存”。分类配色与背景可分别覆盖或继承全局。左下角“恢复默认主题”可一键还原全部主题配置（含各分类独立配置）。

“窗口材质”独立于配色，提供普通和磨砂玻璃，也可按分类独立覆盖或继承。磨砂玻璃由 Windows 合成器实时模糊窗口后方的桌面与其他窗口，文字、图标保持清晰；可调节磨砂强度与玻璃颜色深度。磨砂启用后禁用背景上传、背景覆盖与遮罩调整，已有素材保留，切回普通材质恢复。主题中心窗口会实时展示材质，可拖动查看后方内容变化。效果依赖 Windows 合成与透明效果支持。

分类切换时配色与背景平滑过渡。视频静音循环，同一视频调整配色或遮罩不会重新播放；窗口隐藏后动图和视频暂停。无效背景提示后回退至主题底色。

## 配置与数据

- 配置位于 `%AppData%/LaunchPad/config.json`；升级不需要复制旧程序目录。
- 上传的图标和背景复制到 `%AppData%/LaunchPad/Icons` 与 `Backgrounds`，不依赖原文件继续保留。
- 改名仅影响显示名称；删除条目或分类不会删除磁盘文件。
- 设置页点击“完成”保存；分类排序在松手时同步到主页。

## 从源码构建

需要 Windows 和 .NET 8 SDK，无第三方 NuGet 依赖。

```powershell
dotnet build LaunchPad.csproj -c Release
dotnet publish LaunchPad.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o bin/publish
```

构建前退出占用相同输出目录的运行实例，或指定不同输出目录。

| 源码 | 用途 |
| --- | --- |
| `MainWindow.*` | 主界面、拖拽、文件夹过渡、分类滚动与主题 |
| `SettingsWindow.*` | 设置、分类排序、快捷键与滑条吸附 |
| `ThemeCenterWindow.cs` / `Controls` | 主题编辑和背景媒体 |
| `Models` / `Services` | 配置、热键、条目移动、图标、主题和动效 |
| `Themes` / `Converters` | WPF 样式、资源和绑定转换 |

见 [发行说明](RELEASE_NOTES.md)。发行版附带 SHA-256 校验文件。

生成发行包见 [Build-Release.ps1](tools/Build-Release.ps1)，安装细节见 [安装说明](docs/INSTALLER.md)，可复用流程见 [launchpad-release skill](docs/skills/launchpad-release/SKILL.md)。
