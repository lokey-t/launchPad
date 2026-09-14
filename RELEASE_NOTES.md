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
