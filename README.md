# ForkPlus Plugins

ForkPlus 对比视图插件仓库。

ForkPlus 的「文件对比视图」是插件化的：宿主启动时扫描可执行文件旁的 `plugins/` 目录，
加载其中的 DLL 并发现 `IDiffViewPlugin` 实现，再按文件扩展名把对应文件的对比视图路由给插件。
主仓 ForkPlus 只内置图片（`forkplus.image`）与二进制 / Hex（`forkplus.hex`）两个视图插件，
本仓库用于承载官方与第三方扩展的对比视图插件。

- **插件形态**：进程内 DLL（与宿主同一进程，非独立进程、不走 JSON 协议）
- **加载目录**：ForkPlus 安装目录下的 `plugins/`（扫描其中的 `*.dll`，一个 DLL 一个插件）
- **契约程序集**：`ForkPlus.Plugins.Abstractions`（运行期由宿主提供，插件不随包分发）
- **目标平台**：与 ForkPlus 四平台一致 —— Windows x64 / Linux x64 / Linux ARM64 / macOS ARM64

---

## 目录结构

```
ForkPlus-Plugins/
├── .github/
│   └── workflows/
│       └── build.yml                     # GitHub Actions：四平台构建 + 打包 + Release
├── sdk/
│   └── ForkPlus.Plugins.Abstractions/    # 插件契约工程（主仓 src/ForkPlus.Plugins.Abstractions 的源码镜像）
│       ├── IDiffViewPlugin.cs            # IDiffViewPlugin / DiffViewRequest
│       ├── IDiffView.cs                  # IDiffView / DiffViewMode / DiffViewContext
│       ├── IDiffViewHost.cs              # 宿主能力桥 IDiffViewHost / LfsSmudgeResult
│       ├── DiffSideContent.cs            # 单侧内容模型（路径 / 大小 / 懒加载字节 / LFS）
│       ├── LfsRef.cs                     # LFS 指针投影
│       ├── PluginEnvironment.cs          # 宿主能力桥（本地化 / 图标 / 剪贴板 / Hex 设置…）
│       ├── PluginLog.cs                  # 插件日志门面（NLog）
│       ├── PluginSizeFormat.cs           # 文件尺寸格式化
│       ├── NullAttribute.cs              # 可空性标注
│       └── ForkPlus.Plugins.Abstractions.csproj
├── plugins/
│   ├── Directory.Build.props             # 插件公共属性：统一引用 SDK 契约（Private=false）
│   └── ForkPlus.Plugins.Example/         # 示例插件（新插件复制本目录即可）
│       ├── ExampleDiffPlugin.cs
│       ├── ExampleDiffView.cs
│       └── ForkPlus.Plugins.Example.csproj
├── Directory.Build.props                 # 仓库级公共构建属性（net10.0 / AvaloniaVersion）
├── ForkPlus.Plugins.slnx                 # 解决方案（新增插件在此登记）
└── README.md
```

**新增插件**：在 `plugins/` 下新建 `ForkPlus.Plugins.<Name>/` 目录，复制示例插件的结构，
并在 `ForkPlus.Plugins.slnx` 中登记新工程即可。CI 会自动发现 `plugins/*/*.csproj`，无需改 workflow。

---

## 插件开发规范

### 1. 最小实现

一个插件 = **一个无参构造的 public 类** 实现 `IDiffViewPlugin`，并由 `CreateView()` 返回 `IDiffView`。

```csharp
public sealed class MyDiffPlugin : IDiffViewPlugin
{
    public string Id => "com.example.myplugin";                 // 唯一 Id
    public string DisplayNameKey => "My Plugin";                // 显示名（宿主扩展名绑定列表用）
    public int Priority => 100;                                  // 同扩展名竞争，大者优先
    public IReadOnlyList<string> FileExtensions => new[] { ".mine" };

    public bool CanHandle(DiffViewRequest request) => true;      // 扩展名命中后的二次判定
    public IDiffView CreateView() => new MyDiffView();
}
```

实现要点：

- 类必须 `public`、非 `abstract`、**带无参构造函数**（加载器经 `Activator.CreateInstance` 实例化）。
  缺少无参构造的类会被跳过并记入日志。
- `FileExtensions` 用小写并含点（如 `".png"`）；`"*"` 表示通配兜底。
- `CanHandle` 用于扩展名之外的判定（例如校验文件头魔数）；返回 `false` 时宿主继续询问下一个候选插件。
- 每次对比调用一次 `CreateView()` 创建独立视图实例。

### 2. 视图生命周期

`IDiffView` 的方法由宿主按以下顺序调度，实例会被复用：

```
CreateView → SetContent →（SetMode / Activate / Deactivate / ApplyLocalization 循环）→ Release
```

| 成员 | 职责 |
| --- | --- |
| `View` | 视图根控件，宿主直接挂进自己的容器 |
| `Modes` | 供宿主渲染「视图模式」切换工具条的模式列表；自带工具条的视图返回空数组 |
| `SetContent(context, host)` | 下发两侧内容 + 宿主能力桥；**每次刷新对比都会重新调用**（实例复用） |
| `SetMode(modeId)` | 切换视图模式；不支持的模式直接忽略 |
| `Activate()` / `Deactivate()` | 切入显示 / 切走失活（如恢复、暂停动图播放，省电防闪） |
| `ApplyLocalization()` | 宿主切语言时广播，视图重建文案 |
| `Release()` | 释放：停后台任务、退订事件、释放位图；之后实例不再复用 |
| `HighlightPixelsAvailableChanged` | 「高亮差异像素」能力变化事件；无此能力可显式空实现（`add {} remove {}`） |

### 3. 路由规则

宿主按优先级依次裁决，命中即用：

1. **用户绑定**（用户在设置里把扩展名绑定到某插件）—— 最高，覆盖一切自动路由；
2. **精确扩展名**：声明了该扩展名的插件按 `Priority` 降序，逐个询问 `CanHandle`；
3. **通配 `"*"`**：兜底插件按 `Priority` 降序。

约定：内置 Hex 插件为通配兜底且 `Priority = 0`；普通专用插件应使用更高优先级（示例取 100）。

### 4. 依赖与共享程序集（重要）

插件与宿主之间存在两个**静态共享**程序集，运行期由宿主提供，**插件不得随包分发**：

- `ForkPlus.Plugins.Abstractions`（契约）
- `ForkPlus.Plugins.Ui`（共享视图组件，如需复用）

约束：

- **禁止引用 ForkPlus 主工程类型**。一切宿主能力经 `IDiffViewHost` / `PluginEnvironment` 获取。
- 契约经 `ProjectReference` 引用且 `Private=false`（`plugins/Directory.Build.props` 已统一配置，无需逐插件重复）。
- 不要把 `ForkPlus.Plugins.Abstractions.dll`、`Avalonia*.dll`、`NLog*.dll` 等宿主已加载的程序集放进 `plugins/`。
  同名程序集以先加载者为准，重复分发会造成类型身份漂移（宿主与插件各持一份 `Type`，`is/as` 判定全部失效）。
- **Avalonia 版本必须与宿主严格一致**（当前 `12.1.1`，见根 `Directory.Build.props` 的 `AvaloniaVersion`）。
  版本漂移会在运行期出现 XAML IL 加载失败 / `MissingMethodException` 类崩溃。
- 若确有私有依赖，与插件 DLL 放在 `plugins/` 同目录即可 —— 加载器的 `Resolving` 钩子会兜底解析。
  但不支持同名程序集多版本并存。

### 5. 宿主能力桥

插件的副作用（LFS、图片解码、保存对话框、错误弹窗、本地化、图标、剪贴板…）一律走宿主，插件保持纯 UI 逻辑。

- **`IDiffViewHost`**（随 `SetContent` 下发）：
  `PrepareImageStream` / `GetCachedLfsData` / `RunLfsSmudge` / `SaveFileAs` / `ShowError`。
- **`PluginEnvironment`**（静态门面）：
  `Translate` / `Format` / `ApplyLocalization`、`HighlightImageDiff`、`GetFileIcon`、
  `KeyboardModifiers`、`SetClipboardText(Data)`、Hex 视图设置等。
  宿主未接线的能力有安全回退（原文返回 / no-op / null），插件可独立运行与测试。

### 6. 命名与标识

- 程序集名与工程名：`ForkPlus.Plugins.<Name>`，放在 `plugins/ForkPlus.Plugins.<Name>/`。
- `Id`：官方插件用 `forkplus.<name>`（内置为 `forkplus.image` / `forkplus.hex`）；
  第三方插件建议使用自有反向前缀（如 `com.yourorg.<name>`）避免与其他插件冲突。
  注册表按 `Id` 去重，同 `Id` 后注册者会替换先注册者。
- 显示名：`DisplayNameKey` 为翻译 key；宿主未接线时按原文显示，可直接填英文原文。

### 7. 日志与本地化

- 日志统一用 `PluginLog`（`Debug/Info/Warn/Error`）。宿主已初始化 NLog，插件日志汇入同一输出。
- 文案用 `PluginEnvironment.Translate(key)` / `Format(key, args)`；控件子树可在构造期与
  `ApplyLocalization` 时调用 `PluginEnvironment.ApplyLocalization(root)` 完成整体翻译。
- 尺寸展示用 `PluginSizeFormat`，与宿主口径一致。

---

## 示例插件

[plugins/ForkPlus.Plugins.Example](plugins/ForkPlus.Plugins.Example) 演示了 `IDiffViewPlugin` / `IDiffView`
的最小完整实现：认领 `.example` / `.exampletxt`，命中后由 `ExampleDiffView` 用纯代码（未用 `.axaml`）
渲染左右两列只读信息面板（路径 / 声明大小 / 可读字节数 / LFS 引用）。

写新插件的最快路径：**复制示例目录 → 改工程名、命名空间、`Id`、扩展名**。

---

## 构建与打包

### 本地构建

需要 .NET 10 SDK：

```bash
dotnet build ForkPlus.Plugins.slnx -c Release
```

> 插件是库工程，构建产物为 `plugins/<插件>/bin/Release/net10.0/<AssemblyName>.dll`。

### 通过 GitHub Actions 出包（四平台）

workflow：[.github/workflows/build.yml](.github/workflows/build.yml)

- **触发**：push `v*` 标签 → 构建四平台并发布 Release；`workflow_dispatch` 手动 → 只构建并上传 Artifacts。
- **四平台矩阵**（与 ForkPlus 宿主一致）：

  | 平台 | Runner | RID |
  | --- | --- | --- |
  | windows-x64 | `windows-latest` | `win-x64` |
  | linux-x64 | `ubuntu-latest` | `linux-x64` |
  | linux-arm64 | `ubuntu-22.04-arm` | `linux-arm64` |
  | macos-arm64 | `macos-latest` | `osx-arm64` |

- **出包**：每个平台构建一遍全部插件，把每个插件**自身的主 DLL** 打进一个包：

  ```
  ForkPlus-Plugins-<版本>-<平台>.zip
  └── plugins/
      ├── ForkPlus.Plugins.Example.dll
      └── …（其余插件）
  ```

  版本号取自标签（`v1.2.3` → `1.2.3`）；手动触发时为 `0.0.0-dev-<短 SHA>`。

### 安装

解压包，把 `plugins/` 目录下的 DLL 拷入 ForkPlus 安装目录的 `plugins/`（已存在则覆盖），重启 ForkPlus 生效。

---

## SDK 同步约定

`sdk/ForkPlus.Plugins.Abstractions/` 是主仓 `src/ForkPlus.Plugins.Abstractions/` 的**源码镜像**，
目的是让第三方插件工程自包含编译，无需拉取主仓。

主仓契约变更时需同步本目录；契约的公开面（`IDiffViewPlugin` / `IDiffView` / `IDiffViewHost` /
`DiffViewContext` / `DiffSideContent` / `PluginEnvironment` …）应保持向后兼容，破坏性变更需在发行说明中注明。