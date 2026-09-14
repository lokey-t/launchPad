# 软件更新 / Software updates

启动后自动检查 GitHub、Gitee 的正式 release；不检查草稿、预发布或普通 tag。忽略版本写入配置，仅抑制该版本的自动提示；关于页手动检查仍可查看。下次再说仅抑制本次运行，再次启动重新检查。检测失败不会阻塞启动或自动弹出报错。

两个来源并发检查，先选最高版本，再按响应时间排序。文件下载超时或校验失败会使用同版本的另一来源。所有程序文件在临时目录校验完成才退出应用。替换失败恢复旧文件；配置、主题、应用列表和备份均在 AppData 中，不参与更新。替换器不请求管理员权限，安装目录不可写时提示自主下载。

## 发布增量资源

版本必须同时匹配项目 `Version / AssemblyVersion / FileVersion`、release tag 和包内程序集。先更新项目版本再发布。v0.97 已统一上述版本。

1. 保留上一正式版对应架构和部署类型的完整发布目录（也可将校验过的完整 ZIP 解压到独立目录）。发布新版本到全新目录，指定基线：

   ```powershell
   dotnet publish LaunchPad.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o bin/publish-update
   powershell -NoProfile -File tools/New-UpdateAssets.ps1 -PublishDirectory bin/publish-update -BaseDirectory bin/previous-framework-dependent -OutputDirectory bin/update-assets -Runtime win-x64
   ```

2. 每种部署类型只生成一个 `LaunchPad-delta-旧版本-to-新版本-架构-部署类型.zip` 和一份 `LaunchPad-update-bundle-架构-部署类型.json`。将两者上传到两个来源的同一 release。ZIP 仅包含 SHA-256 不同及新增的完整文件，不包含未变化的运行库。这是文件粒度增量，不是二进制差分。基线版本必须更旧，部署类型和架构必须与目标一致，不支持单文件基线。
3. 保留完整 ZIP：`LaunchPad-v版本-win-x64.zip` 或 `LaunchPad-v版本-win-x64-framework-dependent.zip`，以及 `LaunchPad-v版本-sha256.txt` / `SHA256SUMS.txt`。完整包仍是首次下载和自动回退路径。GitHub 的附件 digest 也可用于验证。
4. 统一构建入口：`tools/Build-Release.ps1 -Version VERSION -Dotnet PATH -BaseSelfContainedDirectory OLD_SELF -BaseFrameworkDependentDirectory OLD_FDD`。每个基线参数可省略，对应部署类型只发行完整包，不生成增量附件。需要 NSIS 编译器（可传 `-MakeNsis PATH`）。不要把基线设置成用户正在使用的安装目录，应使用干净的旧版本发布目录。

新清单 `Format=2` 包含目标全部文件的路径、大小和 SHA-256，以及 `BaseVersion` 和 `Bundle`（附件名、压缩大小、SHA-256、包含的路径列表）。客户端核对本地程序集的基线版本，逐一校验未打包文件，再下载一个 ZIP。压缩包和每个文件均验证大小、摘要和路径；多余/重复/越界条目被拒绝。镜像失败会切换来源；无适用基线、未打包文件损坏、清单或增量包不可用时，自动回退到经过校验的完整包。取消不会触发继续下载。

迁移兼容：不要生成旧名称的 Format=1 清单，也不要再发布 `.gz` 附件。v0.97 客户端看不到旧名称清单，会通过原有完整 ZIP 更新到支持新协议的版本；后续使用合并增量 ZIP。新客户端仍可读取历史 Format=1 release。完整包下载后也只替换本地有变化的文件。旧程序文件仍保留，不支持清单删除操作；需要强制移除文件的发行版必须另行扩展事务与回滚协议。

更新结果写入 `%AppData%/LaunchPad/update-result.json`。失败的回滚副本保留在 `%TEMP%/LaunchPadUpdate/<UUID>/rollback`。成功启动后清理该次暂存。取消/下载失败清理尚未交接的暂存。未知旧程序文件不会删除；若未来发行版必须删除特定文件，应先扩展协议，不能假设旧文件会被移除。当前协议不执行安装器/MSIX；安装器安装出的应用仍可按 self-contained 清单增量更新；旧单文件 ZIP 可走完整更新并校验 EXE 版本。

2026-09-14 实测：两站现有 v0.96 的 framework-dependent ZIP 是单文件包，其 EXE 内 FileVersion 仍为 0.95.0.0。更新器会检测到 v0.96，但拒绝自动安装此不一致包并显示具体原因，避免重复提示/错误更新。后续发布必须先同步程序集版本与 release tag。此次只读取、验证了远程附件，未修改远程发行版。

## English

LaunchPad checks public stable releases from both hosts after startup. Ignore persists for that version; Later lasts until the next launch. Manual checks bypass ignored versions. The newest version wins; response latency determines mirror preference, with per-file failover and SHA-256 validation.

Publish a matching new build and provide a clean older build to `tools/New-UpdateAssets.ps1 -BaseDirectory OLD`. Format 2 produces one changed-file ZIP and one bundle manifest per deployment flavor. The manifest lists all target files, the base version and the bundle's hash, size and included paths. Omitted files must match locally. The updater verifies the archive and every extracted file, tries both mirrors, and falls back to a verified full ZIP for unsupported bases or unavailable/invalid deltas. Cancellation stops all downloads. This is whole-file replacement, not binary patching; retired files are retained and user data is untouched.

`Build-Release.ps1` accepts `-BaseSelfContainedDirectory` and `-BaseFrameworkDependentDirectory`. Without a baseline, that flavor is full-package-only. Keep full ZIPs and their checksums for fallback. Do not publish the old Format 1 manifest names or per-file gzip assets: v0.97 will use its existing full-package path to migrate, while the new client can still consume historical Format 1 releases. Downloads are staged before shutdown and failed replacement rolls back. NSIS installation is independent of this update format.

Run regression tests with `dotnet run --project Tests/Updates/UpdateTests.csproj -c Release`; add `-- --ui` for isolated WPF appearance checks or `-- --network` for a read-only live release/download check. None installs into the user's running application.

Test bundle generation with `powershell -NoProfile -File Tests/Updates/BundlePackagingTests.ps1` after building/running UpdateTests in Debug. Fixtures stay under obj and temporary test directories.
