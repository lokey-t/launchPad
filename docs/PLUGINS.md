# LaunchPad 插件开发 / Plugin development

第一阶段 API 版本为 **1**。插件是按需启动的独立原生程序，不会在安装、启用或软件启动时执行。插件 UI 由 LaunchPad 绘制，支持配色、窗口材质与现有控件样式。仅 Windows EXE 入口；任何语言只要遵循标准输入/输出协议均可开发。

## 用户操作

1. 设置 → 插件 → 安装插件，选择 `.qdtplugin` 文件。
2. 在确认框中检查开发者、来源和访问能力，确认后直接导入并启用，无需再次手动启用。
3. 使用插件声明的搜索前缀；主页空白处右键 → 插件快捷操作；文件右键 → 插件操作。
4. 插件卡片可保存文本设置、停用、卸载并查看最近 20 条错误。连续失败 3 次自动停用，重新启用后重置失败计数。

**插件不是安全沙箱。** 独立进程改善故障隔离，但插件原生代码拥有当前用户的系统访问能力。清单权限只限制 LaunchPad 代为执行的接口，不能阻止插件自行读写文件或访问网络。安装包未包含签名认证或商城审核机制，只安装可信来源的插件。

## 最小清单

`.qdtplugin` 是 ZIP，`plugin.json` 必须位于根目录：

```json
{
  "Id": "yourname.plugin",
  "Name": "My plugin",
  "Version": "1.0.0",
  "ApiVersion": 1,
  "Author": "Developer",
  "Description": "What this plugin does",
  "Executable": "MyPlugin.exe",
  "SearchPrefix": "my",
  "Permissions": ["clipboard.write"],
  "Commands": [{"Id": "copy", "Title": "Copy result", "Context": false}],
  "Settings": [{"Key": "label", "Title": "Label", "Default": "Hello"}]
}
```

- `Id` 是稳定的反向域名式标识，小写字母开头，可含数字、点、短横线，3–80 字符。
- `ApiVersion` 与软件版本独立，当前仅支持整数 `1`，未知 API 拒绝安装，不静默运行。
- `Executable` 为包内相对 EXE 路径，不支持绝对路径、`..`、符号链接或命令行脚本入口。
- `SearchPrefix` 可省略。提供时，只有输入以该前缀开头才启动搜索。主程序延迟 250 ms 发起请求，输入改变、清空或隐藏主页时取消旧查询。
- `Commands` 必须声明搜索结果可执行的命令。`Context: true` 出现在文件右键菜单；其余命令出现在主页快捷操作菜单。
- `Settings` 第一阶段支持文本输入，插件自行解释数字或路径；不适合存储密码。所有项有独立“保存”按钮。
- `Permissions` 可声明 `clipboard.write`、`files.open`、`context.read`。没有 `context.read` 时主程序不会传入选中条目的路径。

## 进程协议

每次调用启动一个进程，标准输入传入一行 UTF-8 JSON，然后关闭 stdin。插件输出一行 JSON 响应；收到完整响应后宿主结束 worker，因此不要在响应后安排后台任务。不得把调试日志写入 stdout。stderr 会被持续读取并丢弃，避免阻塞；宿主协议错误与超时写入插件管理日志。

请求例子：

```json
{"ApiVersion":1,"Id":"request-id","Method":"search","Query":"12 * 3","Command":null,"Value":null,"ContextPath":null,"DataDirectory":"C:/.../Plugins/Data/yourname.plugin/files","Settings":{"label":"Hello"}}
```

- `Method` 为 `search` 或 `execute`。`Query` 已去除搜索前缀。
- `execute` 含 `Command`，点击搜索结果时附带对应 `Value`；文件菜单可附带 `ContextPath`。
- 响应必须原样返回请求 `Id`，`ApiVersion` 为 1。

搜索响应：

```json
{"ApiVersion":1,"Id":"request-id","Results":[{"Title":"36","Description":"Click to copy","Command":"copy","Value":"36"}]}
```

执行响应：

```json
{"ApiVersion":1,"Id":"request-id","Results":[],"Action":"clipboard","Value":"36","Message":"Copied"}
```

`Action` 可省略（只显示提示），或为 `clipboard` / `open`。只有用户点击执行时才能返回宿主操作；搜索阶段不允许副作用。`open` 只接受存在的绝对文件/目录路径，拒绝网址和任意 shell 命令。它可以打开可执行文件，因此仍要求用户信任插件。插件无法通过这些接口直接修改应用配置、分类或主题。

单次调用限时 8 秒，全局最多 4 个进程，同一插件串行。响应上限 256 Ki 字符，最多 20 条搜索结果。主程序取消、停用插件和正常退出时会终止活动 worker；这不是限制原生程序自行派生其他进程的系统沙箱。

## .NET SDK 与示例

`PluginSdk/LaunchPad.PluginSdk.csproj` 提供 Request、Response、Result 和 Runner。入口示例：

```csharp
await Runner.Run(request => Task.FromResult(new Response {
    Message = "Hello"
}));
```

Runner 自动回填 Id。插件依赖 SDK 项目，不应引用 LaunchPad 的 WPF 主程序集。

两份完整示例位于 `PluginExamples/`：

- Calculator：输入 `= 12 * 3`，点击结果复制；可设置小数位数。
- PathTools：输入 `path 关键词` 搜索指定目录的直接子项（最多扫描 500 项，不递归）；文件右键复制路径或打开所在目录；快捷操作打开配置目录。

```powershell
./tools/Build-PluginExamples.ps1 -Dotnet dotnet
# 面向未安装 .NET 8 的用户发布，附带独立运行环境：
./tools/Build-PluginExamples.ps1 -Dotnet dotnet -SelfContained
```

输出目录 `bin/PluginExamples/<构建ID>/` 含两个 `.qdtplugin`。默认包需要用户系统安装 .NET 8；LaunchPad 自带的运行环境不等于全局 .NET 安装。使用 `-SelfContained` 可离线独立运行，但插件包会明显变大。

## 数据、更新与边界

插件程序位于 `%AppData%/LaunchPad/Plugins/Packages/<id>`，设置、最近错误与专用 files 目录位于 `Plugins/Data/<id>`。软件自动更新不会覆盖它们。卸载保留数据；再次安装同 ID 时保留设置，通过界面确认导入后启用。第一阶段更新插件需要先停用并卸载，再安装新包，没有静默插件自动更新。

插件及其数据不加入当前应用备份/主题备份格式。第一阶段不提供任意 WPF/XAML 注入、复杂自绘页面、插件商城、后台常驻服务或应用全局快捷键注册；快捷操作通过主页右键菜单调用。

## 验证

构建两个示例，再构建 `Tests/Plugins/Fixture/Fixture.csproj` 到独立目录，运行：

```powershell
dotnet run --project Tests/Plugins/PluginTests.csproj -- <示例包目录> <Fixture发布目录>
```

测试只使用临时目录，覆盖安装停用、搜索、执行、设置、权限、失败停用、取消、超时、压缩包越界和卸载数据保留。若使用私有目录中的 .NET SDK，测试子进程需可通过 `DOTNET_ROOT` 找到该运行时。

## English summary

API v1 runs trusted native EXE plugins as on-demand child processes. Use one JSON line per request/response over stdin/stdout, echo the request ID, and exit after replying. LaunchPad owns the UI: search results, context menus, quick actions and text settings. Prefix-based search is debounced and cancellable; calls time out after 8 seconds, three failures disable the plugin, and the last 20 host errors are retained. The import confirmation shows the trust notice and host permissions; confirming imports and enables the plugin in one step. This is process isolation, **not a security sandbox**; declared permissions gate host actions only. Packages and data are separate from application backups. See the two complete examples and SDK above; framework-dependent examples require a system .NET 8 runtime, while `-SelfContained` bundles one.
