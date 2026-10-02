# ForkPlus-Plugins

ForkPlus 的插件仓。每个插件是一个**独立进程**，通过 stdin/stdout 与主程序交换 JSON，
因此插件可以自由使用 GPL 依赖、原生库或与主程序冲突的库版本——崩溃和依赖冲突都被关在
自己的进程里，主程序（MIT）不链接、不加载任何插件程序集。

本仓同时提供插件开发所需的 SDK 源码、示例插件，以及随 ForkPlus 分发的官方插件。

---

## 目录结构

```
ForkPlus-Plugins/
├── sdk/ForkPlus.PluginSdk/        插件 SDK 源码（清单模型 / 线协议 / 主循环 / 分帧 / PNG 编码）
├── plugins/
│   ├── Directory.Build.props      插件公共属性：OutputType=Exe、net10.0、引用 SDK 源码
│   ├── Directory.Build.targets    把 plugin.json 拷到 build / publish 输出
│   ├── ForkPlus.Plugin.ExampleSwatch/   最小示例插件（不入发行包，供本地参考）
│   ├── ForkPlus.Plugin.ImageCompare/    官方：图片对比（随 ForkPlus 分发）
│   └── ForkPlus.Plugin.BinaryPreview/   官方：二进制结构兜底视图（随 ForkPlus 分发）
├── ForkPlus.Plugins.sln
└── .github/workflows/release.yml  打 v* 标签时构建并发布发行包
```

> **SDK 同步约定**：`sdk/ForkPlus.PluginSdk` 与主仓 `ForkPlus/src/ForkPlus.PluginSdk`
> 必须逐字节一致。主仓 SDK 有改动时，需把改动同步到本仓（或反过来）后再提交；否则插件按旧
> 清单模型编译、宿主按新规则校验，会出现"本地能跑、发布即拒载"的漂移。

---

## 快速开始

```bash
# 构建全部插件
dotnet build ForkPlus.Plugins.sln

# 发布单个插件（产物目录即"一个插件目录"）
dotnet publish plugins/ForkPlus.Plugin.ExampleSwatch/ForkPlus.Plugin.ExampleSwatch.csproj \
    -c Release -o publish/ForkPlus.Plugin.ExampleSwatch
```

安装：把发布目录整体拷进 ForkPlus 应用目录下的 `plugins/<插件名>/`，使
`plugins/<插件名>/plugin.json` 与宿主 dll 同目录。开发期可用环境变量指向一个临时目录：

```bash
FORKPLUS_PLUGINS_DIR=/path/to/publish ForkPlus
```

宿主启动时扫描该目录下的一级子目录，逐个读取 `plugin.json`；坏插件只记日志跳过，不影响整批。

---

## 插件的最小完整形态

一个插件 = **一份清单 `plugin.json`** + **一个可执行宿主进程** + **引用 SDK**。

### 1. `plugin.json`

```json
{
  "id": "com.example.my-viewer",
  "name": "My Viewer",
  "version": "1.0.0",
  "apiVersion": 1,
  "license": "MIT",
  "homepage": "https://github.com/you/my-viewer",
  "host": { "executable": "MyViewer.dll" },
  "viewers": [
    {
      "id": "main",
      "displayName": "My View",
      "priority": 150,
      "extensions": [".foo", ".bar"]
    }
  ]
}
```

字段说明：

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `id` | 是 | 全局唯一插件 id，建议反向域名（如 `com.example.font-tools`） |
| `name` / `version` / `homepage` / `license` | 否 | 仅用于展示，主程序不做法律判断 |
| `apiVersion` | 是 | 必须等于 `PluginProtocol.Version`（当前为 `1`），不符直接拒载 |
| `host.executable` | 是 | 宿主可执行文件；相对路径按插件目录解析，也可写 PATH 上的命令名 |
| `host.arguments` | 否 | 附加启动参数 |
| `viewers[]` | 是 | 至少一个视图；每个视图对应主程序里的一个查看器 |

`viewers[]` 每项：

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `id` | 是 | 视图 id，插件内唯一（宿主用 `插件id:视图id` 做全局标识） |
| `displayName` | 否 | 展示名 |
| `priority` | 否 | 判定优先级，与内置查看器同一标尺（见下） |
| `extensions` | 是 | 感兴趣的扩展名，形如 `".png"`（**必须带点**）；或 `"*"` 表示通配兜底 |

> 清单在装载时即校验：缺 `id` / `host.executable` / `viewers`、`apiVersion` 不符、
> 扩展名没带点（如写成 `png`）、或写法非 `".xxx"` / `"*"`，都会被拒载并给出原因。

### 2. 宿主进程（`Program.cs`）

协议主循环（`hello` / `render` / `shutdown`、分帧、异常转错误响应）已收敛到 SDK 的
`PluginHost.Run`。插件作者只需实现"处理一个请求、返回一个结果对象"：

```csharp
using System;
using System.Collections.Generic;
using ForkPlus.Plugins;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private const string PluginId = "com.example.my-viewer";

    private static int Main(string[] args) => PluginHost.Run(Handle);

    private static object Handle(PluginRequest request)
    {
        switch (request.Method)
        {
        case PluginProtocol.MethodHello:
            return new HelloResult
            {
                ProtocolVersion = PluginProtocol.Version,
                PluginId = PluginId,
                Name = "My Viewer",
                Version = "1.0.0"
            };
        case PluginProtocol.MethodRender:
            return Render(request.Params?.ToObject<RenderParams>());
        default:
            throw new InvalidOperationException("Unsupported method '" + request.Method + "'.");
        }
    }

    private static RenderResult Render(RenderParams parameters)
    {
        byte[] data = parameters?.Data ?? Array.Empty<byte>();
        // 把这一侧内容画成位图，返回 PNG 帧
        return new RenderResult
        {
            Frames = new List<byte[]> { MyRenderer.Encode(data, parameters?.Theme) },
            FrameDelayMs = 0,
            StatusLabel = data.Length + " bytes"
        };
    }
}
```

> **重要**：插件的 `stdout` 只承载协议报文。任何裸打印（`Console.WriteLine` 调试输出）都会
> 污染协议流。日志请走 `Console.Error`，它归插件侧，宿主不会解析。
> 完整可运行示例见 [ExampleSwatch](plugins/ForkPlus.Plugin.ExampleSwatch/Program.cs)。

### 3. 工程文件

`plugins/Directory.Build.props` 已为所有插件配好 `OutputType=Exe`、`net10.0` 并引用 SDK 源码，
插件自身的 `.csproj` 只需声明程序集名：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>MyViewer</AssemblyName>
    <RootNamespace>MyViewer</RootNamespace>
  </PropertyGroup>
</Project>
```

---

## 渲染契约（`render`）

宿主把**单侧**内容交给插件，插件回位图。设计取舍：

- 插件只能回 **0..n 帧 PNG + 一行状态文案**，不能回 UI 控件。
- 对比交互（并排 / 滑动 / 洋葱皮 / 像素高亮 / 缩放平移 / 动图播放条）全部由宿主统一提供。
- 好处是插件与主程序之间只有字节流，既拿到进程隔离，又不必自己实现一套对比容器。

`RenderParams`（请求）：

| 字段 | 说明 |
| --- | --- |
| `viewerId` | 要调用的视图 id |
| `path` | 仓库内路径（可为 null） |
| `isLfs` | 是否为 LFS 内容 |
| `data` | 该侧字节；无内容时为 null（新增/删除、LFS 未 smudge、大文件未预载） |
| `width` / `height` | 宿主建议的渲染尺寸（可参考，不强制） |
| `theme` | `"light"` / `"dark"` |

`RenderResult`（响应）：

| 字段 | 说明 |
| --- | --- |
| `frames` | PNG 帧列表；空表示认领了但渲染不出内容，宿主按失败处理 |
| `frameDelayMs` | 多帧时的帧间隔（毫秒）；0 或单帧为静态图 |
| `statusLabel` | 附加到文件名后的一行说明（如 `"3 字重 · 512px"`） |
| `warning` | 非致命说明（渲染降级等），宿主记日志用 |

帧多于一帧时，宿主复用内置动图播放机制自动播放。

---

## 路由与优先级（重点）

宿主在查看器注册表里按 `priority` 从高到低挑第一个能处理的查看器。内置查看器：

| 查看器 | 优先级 | 处理 |
| --- | --- | --- |
| 动图 | 200 | GIF / 动态 WebP / APNG |
| 静态图 | 100 | 其他可解码位图 |
| 文件卡片 | `int.MinValue` | 无字节时的兜底 |

插件桥接查看器的优先级 = **所有插件视图中的最高者**。因此插件声明 `priority >= 250`
即可排到内置动图之前，接管 GIF 这类会被内置高优先级挡住的格式；未装插件时桥接不参与判定。

**具体扩展名永远优先于通配 `"*"`**（与优先级无关）：

- 图片插件声明了 `".png"`、二进制兜底插件声明了 `"*"`，两者都"命中" `.png` 时，
  **图片插件赢**——显式声明更具体。
- `"*"` 只作为**兜底**：只处理宿主尚未按图片处理、且没有查看器显式认领的文件
  （含无扩展名的 `Makefile` / `LICENSE`）。
- `"*"` 不会顶掉内置图片查看器：像 `.ico` / `.bmp` 这类没被任何插件显式声明的图片，
  仍由内置查看器渲染，而不是被二进制结构视图接管。
- `"*"` 只有在该文件**已确认是二进制且字节已预载**时才生效；拿不到字节时退回文件卡片，
  因为插件没有内容可渲染，交给它只会得到空白。

> 结论：一个"支持所有二进制"的兜底插件与图片插件**可以共存**，不会互相抢文件。

---

## 线协议细节

- **传输**：宿主以 `dotnet <插件.dll>`（或清单指定的命令）拉起插件进程，走其 stdin/stdout。
- **分帧**：LSP 风格——`Content-Length: <n>\r\n\r\n` + n 字节 UTF-8 JSON。
- **语义**：请求-响应。宿主只发 `hello` / `render` / `shutdown`，不期望插件的主动通知。
- **版本**：`PluginProtocol.Version` 是硬闸门；新增可选字段不算破坏性变更，不递增版本号。
- **超时**：握手 15s、单次渲染 20s；超时即杀进程并按失败处理，下次访问重新拉起。

---

## 测试

- **清单 / 发现 / 线协议 / 路由** 的静态契约在**主仓**测试：
  `ForkPlus.Tests` 里的 `PluginSdkTests`、`PluginViewerRoutingTests`。
- **本地手动验证**：把发布目录设到 `FORKPLUS_PLUGINS_DIR`，用目标格式的文件做一次 diff，
  确认视图被插件接管且渲染正确；再故意渲染一个损坏文件，确认宿主降级而非崩溃。

---

## 发布

`.github/workflows/release.yml` 在推送 `v*` 标签时：

1. 用 .NET 10 SDK 发布循环列表里的每个插件（`dotnet publish -c Release`）；
2. 校验产物内含 `plugin.json` 与宿主 dll；
3. 打成 `tar.gz`（归档根 = 插件目录内容，资产名不带版本号，便于稳定 URL 引用）；
4. 创建 GitHub Release 并上传资产。

要让某个插件随 ForkPlus 分发，把它加进 workflow 中 `Publish plugins` 的循环列表即可
（示例插件仅供本地参考，不入包）。

---

## 许可

各插件以自己的 `plugin.json` 中 `license` 字段为准。插件与主程序进程外通信，
因此插件可选用 GPL 等与主程序不同的许可证。