# LaunchPad v0.97

## 中文

### 新功能

- 单 EXE 一键安装：自带运行环境、可选安装目录，识别旧版本后原位更新，保留应用、分类、主题和备份。
- 自动更新提示与关于页“检查更新”：支持忽略本版本、下次再说、GitHub/Gitee 自主下载和同版本镜像回退。
- 按文件增量更新、SHA-256 校验、替换失败回滚；提供完整版和精简版的增量资源。
- 首次运行引导，可在关于页重新进入。
- 应用快捷方式默认解析为原文件，可在“通用”关闭。
- 主页右键支持按添加时间、名称、打开时间排序。

### 界面与体验

- 新应用图标，托盘右键菜单与整体风格统一。
- 拖动预览跟随当前主题和图标样式。
- 重复启动应用直接打开已有主界面。
- 优化语言选择控件；保留中英文切换、主题预设导入导出及恢复出厂设置功能。

### 下载与升级

| 附件 | 说明 |
| --- | --- |
| `LaunchPad-v0.97-win-x64-setup.exe` | 推荐；单 EXE 离线安装或更新，可选目录。 |
| `LaunchPad-v0.97-win-x64.zip` | 原有便携完整版，包含运行环境，解压运行。 |
| `LaunchPad-v0.97-win-x64-framework-dependent.zip` | 原有精简版，需要 .NET 8 Desktop Runtime x64。 |
| `LaunchPad-v0.97-sha256.txt` | 发行附件 SHA-256 校验值。 |

支持 Windows 10 / 11 x64。ZIP 解压后保留全部文件，手动覆盖前退出旧版本。无法识别未运行、未注册的便携版时，可在安装器中选择其原目录。数据位于 `%AppData%/LaunchPad`，不需重新导入。安装器负责安装和升级，目前不注册 Windows 卸载入口。

本版统一应用、安装器和清单版本，避免历史标签与程序集版本不一致。旧发行版保持不变。`LaunchPad-update-*.json` 和 `lp-*.gz` 为应用内更新资源，无需手动下载。

## English

### New features

- Single EXE offline setup with runtime included, a selectable folder, and in-place upgrades that preserve apps, categories, themes and backups.
- Startup update notifications and Check for updates in About, with Ignore this version, Later, manual GitHub/Gitee downloads and fallback between mirrors of the same version.
- Per-file incremental downloads, SHA-256 validation and rollback on replacement failure, with assets for both deployment types.
- First-run onboarding, also accessible from About.
- Application shortcuts resolve to their original targets by default; configurable in General.
- Home context menu sorting by added time, name or last-opened time.

### Interface and experience

- New application icon and a tray menu matching the app.
- Drag previews follow the current theme and icon appearance.
- Launching an already running instance opens its main window.
- Refined language picker; Chinese/English switching, theme preset import/export and factory reset remain available.

### Download and upgrade

| Asset | Description |
| --- | --- |
| `LaunchPad-v0.97-win-x64-setup.exe` | Recommended: one EXE for offline installation or upgrades with folder selection. |
| `LaunchPad-v0.97-win-x64.zip` | Existing portable full package with runtime included. Extract and run. |
| `LaunchPad-v0.97-win-x64-framework-dependent.zip` | Existing smaller package requiring .NET 8 Desktop Runtime x64. |
| `LaunchPad-v0.97-sha256.txt` | SHA-256 checksums for release assets. |

Windows 10 / 11 x64. Keep all extracted ZIP files together and exit the old tray instance before a manual overwrite. Select the original folder for an undetected, unregistered portable copy. Data remains in `%AppData%/LaunchPad`. Setup handles installation and upgrades; Windows uninstall registration is not included yet.

Application, installer and manifest versions are aligned, avoiding the historical mismatch with release tags. Older releases remain unchanged. `LaunchPad-update-*.json` and `lp-*.gz` are used by the in-app updater and do not need manual downloading.
