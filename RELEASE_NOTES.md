# LaunchPad v0.98

## 中文

- **新增实验性插件功能**：支持插件安装与管理、搜索扩展、文件右键菜单、主页快捷操作及独立设置；提供 SDK、开发文档和两个示例。双击 `.qdtplugin` 可进入导入确认，确认后导入并启用。插件按需运行于独立进程，支持超时终止、错误日志与连续失败停用；原生插件仅应来自可信来源，进程隔离不是安全沙箱。
- **优化文件夹逻辑**：拖出、移动或移除条目后，文件夹只剩一个条目时自动解散，剩余条目保留原文件夹位置和自身显示设置；清理空文件夹，保留文件夹内排序与新建流程。
- **优化开机自启速度**：优先使用无延迟登录计划任务，保留注册表启动回退；延迟创建设置窗口，减少启动阶段工作量。实际启动时间取决于系统环境。
- **优化扩展文件图标**：插件、主题和备份分别使用紫色拼图、青色调色盘和蓝色备份箱角标，支持多尺寸显示；补充主题文件双击导入和系统文件夹图标。

本次为代码版本提交，安装包及 Release 附件尚未发布。

## English

- **Experimental plugins**: install and manage plugins with search, file context-menu actions, home quick actions and independent settings. Includes an SDK, documentation and two examples. Double-click `.qdtplugin` to confirm import and enable it. On-demand worker processes support timeouts, error logs and automatic disabling after repeated failures. Native plugins must be trusted; process isolation is not a security sandbox.
- **Folder behavior**: folders automatically dissolve when moving or removing items leaves one entry. The remaining entry retains the folder's position and its own appearance. Empty folders are removed while folder creation and internal sorting remain supported.
- **Startup performance**: prefer an immediate logon scheduled task with registry fallback, and defer settings-window creation. Actual startup time depends on the system.
- **File-type icons**: distinct purple puzzle, teal palette and blue archive badges identify plugin, theme and backup files at multiple sizes. Theme files support double-click import; system folders have a dedicated icon.

This is a source version submission. Installer and Release assets have not been published.

---

# LaunchPad v0.97.1

## 中文

- **更新增量更新方式**：将变化和新增的文件合并为一个 ZIP，每种部署类型只需一个增量包和一份清单，减少发行附件数量。保留双站下载、SHA-256 校验及失败回滚；版本不适用或文件校验失败时自动回退完整包。
- **缩减安装包大小**：安装器改为 NSIS，单 EXE 离线安装包从约 129 MB 缩减到约 45 MB，体积减少约 65%。保留原有界面风格、中英文、目录选择和旧版更新，配置与备份不受影响。

### 下载

- `LaunchPad-v0.97.1-win-x64-setup.exe`：推荐，单 EXE 离线安装或更新。
- `LaunchPad-v0.97.1-win-x64.zip`：包含运行环境的便携版。
- `LaunchPad-v0.97.1-win-x64-framework-dependent.zip`：需要 .NET 8 Desktop Runtime x64 的精简便携版。

支持 Windows 10 / 11 x64。安装包和 ZIP 均在 GitHub、Gitee 提供。v0.97 通过原有完整包方式升级，本版及后续版本支持合并增量 ZIP。增量 ZIP 和清单由应用自动使用，无需手动下载。历史发行版保持不变。

## English

- **Updated incremental delivery**: changed and added files are bundled into one ZIP, with one delta archive and one manifest per deployment flavor. This reduces release attachments while retaining mirror failover, SHA-256 checks and rollback. Unsupported baselines or failed verification fall back to a full package.
- **Smaller installer**: NSIS reduces the single EXE offline installer from approximately 129 MB to 45 MB, about 65% smaller. The existing visual style, Chinese/English UI, folder selection and in-place upgrades are retained; settings and backups are preserved.

### Downloads

- `LaunchPad-v0.97.1-win-x64-setup.exe`: recommended single EXE offline installation or upgrade.
- `LaunchPad-v0.97.1-win-x64.zip`: portable package with runtime included.
- `LaunchPad-v0.97.1-win-x64-framework-dependent.zip`: smaller portable package requiring .NET 8 Desktop Runtime x64.

Windows 10 / 11 x64. Setup and ZIP packages are available on both GitHub and Gitee. v0.97 migrates through its existing full-package update path; this and future versions support bundled deltas. Delta ZIPs and manifests are consumed automatically by the app. Historical releases are unchanged.
