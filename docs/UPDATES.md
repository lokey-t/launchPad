# 软件更新 / Software updates

启动后自动检查 GitHub、Gitee 的正式 release；不检查草稿、预发布或普通 tag。忽略版本写入配置，仅抑制该版本的自动提示；关于页手动检查仍可查看。下次再说仅抑制本次运行，再次启动重新检查。检测失败不会阻塞启动或自动弹出报错。

两个来源并发检查，先选最高版本，再按响应时间排序。文件下载超时或校验失败会使用同版本的另一来源。所有程序文件在临时目录校验完成才退出应用。替换失败恢复旧文件；配置、主题、应用列表和备份均在 AppData 中，不参与更新。替换器不请求管理员权限，安装目录不可写时提示自主下载。

## 发布增量资源

版本必须同时匹配项目 `Version / AssemblyVersion / FileVersion`、release tag 和包内程序集。先更新项目版本再发布。v0.97 已统一上述版本。

1. 按目标架构发布到全新目录，例如：

   ```powershell
   dotnet publish LaunchPad.csproj -c Release -r win-x64 --self-contained false -o bin/publish-update
   powershell -NoProfile -File tools/New-UpdateAssets.ps1 -PublishDirectory bin/publish-update -OutputDirectory bin/update-assets -Runtime win-x64
   ```

2. 将输出目录中的 **全部** `.gz` 和 `LaunchPad-update-win-x64-framework-dependent.json` 附件上传到 GitHub、Gitee 对应版本 release。两边附件必须相同。独立运行版使用 `--self-contained true`，生成 `self-contained` 清单；支持 x64 / x86 / arm64，应分别发布。不要启用 single-file 发布，本协议需要独立的 LaunchPad.dll 和可识别的运行时文件。
3. 保留完整 ZIP 下载供初次安装和自主下载。命名：`LaunchPad-v版本-win-x64.zip`（独立版）或 `LaunchPad-v版本-win-x64-framework-dependent.zip`。提供 `LaunchPad-v版本-sha256.txt` 或 `SHA256SUMS.txt`，每行 `SHA256  文件名`。GitHub 的 release asset `digest` 也可用于完整包校验。

增量按文件粒度：本地 SHA-256 相同的文件不下载；变动文件下载各自 gzip 数据并校验解压后大小和 SHA-256。不是二进制块差分。清单 Format=1，包含版本、架构、部署类型、路径、大小、摘要及附件名，不执行清单中的命令。旧 release 没有清单时，按钮明确显示“完整包更新”，按对应架构与部署类型下载、校验完整包，再仅替换变动文件。缺少可信摘要则停止自动安装，提供自主下载。

更新结果写入 `%AppData%/LaunchPad/update-result.json`。失败的回滚副本保留在 `%TEMP%/LaunchPadUpdate/<UUID>/rollback`。成功启动后清理该次暂存。取消/下载失败清理尚未交接的暂存。未知旧程序文件不会删除；若未来发行版必须删除特定文件，应先扩展协议，不能假设旧文件会被移除。当前协议不执行安装器/MSIX；安装器安装出的应用仍可按 self-contained 清单增量更新；旧单文件 ZIP 可走完整更新并校验 EXE 版本。

2026-09-14 实测：两站现有 v0.96 的 framework-dependent ZIP 是单文件包，其 EXE 内 FileVersion 仍为 0.95.0.0。更新器会检测到 v0.96，但拒绝自动安装此不一致包并显示具体原因，避免重复提示/错误更新。后续发布必须先同步程序集版本与 release tag。此次只读取、验证了远程附件，未修改远程发行版。

## English

LaunchPad checks public stable releases from both hosts after startup. Ignore persists for that version; Later lasts until the next launch. Manual checks bypass ignored versions. The newest version wins; response latency determines mirror preference, with per-file failover and SHA-256 validation.

Publish matching architecture/deployment builds, then run `tools/New-UpdateAssets.ps1` and attach all generated files to the same version on both hosts. Incremental updates transfer only changed files, individually gzip-compressed. Existing releases without a manifest explicitly offer a full-package update instead. Full ZIPs require a release digest or checksum list. Downloads are staged before shutdown; failed replacement rolls back, leaving AppData configuration and backups untouched. No elevation is requested. Retired files are retained. The updater does not execute setup/MSIX files; applications installed by our setup use the self-contained incremental manifest; legacy single-file ZIPs can use full-package updates with executable version validation.
Run regression tests with `dotnet run --project Tests/Updates/UpdateTests.csproj -c Release`; add `-- --ui` for isolated WPF appearance checks or `-- --network` for a read-only live release/download check. None installs into the user's running application.
