# ForkPlus Plugins

[![build passing](https://github.com/hebin123456/ForkPlus-Plugins/actions/workflows/build.yml/badge.svg?branch=master)](https://github.com/hebin123456/ForkPlus-Plugins/actions/workflows/build.yml)
[![release](https://img.shields.io/github/v/release/hebin123456/ForkPlus-Plugins?label=release&color=blue)](https://github.com/hebin123456/ForkPlus-Plugins/releases)

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
│   ├── scripts/
│   │   ├── install-plugin-artifacts.sh   # 单插件产物安装（主 DLL + 私有依赖 + 原生库，排除宿主共享程序集）
│   │   ├── collect-third-party-notices.py# 由插件登记表 + licenses/ 全文生成第三方许可声明
│   │   ├── capture-screenshots.sh        # Pages 截图采集（装插件 → 无头启动 ForkPlus → 截图）
│   │   └── build-pages.py                # Pages 站点生成
│   ├── pages/                            # Pages 模板与插件登记表（template-*.html / style.css / plugins.json）
│   └── workflows/
│       ├── build.yml                     # GitHub Actions：四平台构建 + 打包 + Release
│       └── pages.yml                     # GitHub Actions：截图 + 生成站点 + 发布 Pages
├── sdk/
│   └── ForkPlus.Plugins.Abstractions/    # 插件契约工程（主仓 src/ForkPlus.Plugins.Abstractions 的源码镜像）
│       ├── IDiffViewPlugin.cs            # IDiffViewPlugin / DiffViewRequest
│       ├── IPluginMetadata.cs            # IPluginMetadata 插件元数据（名称 / 版本 / 描述，可选实现）
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
│   ├── ForkPlus.Plugins.Example/         # 示例插件（新插件复制本目录即可）
│   │   ├── ExampleDiffPlugin.cs
│   │   ├── ExampleDiffView.cs
│   │   └── ForkPlus.Plugins.Example.csproj
│   ├── ForkPlus.Plugins.Pdf/             # PDF 对比插件（左右两栏逐页并排渲染旧 / 新 PDF）
│   │   ├── PdfDiffPlugin.cs
│   │   ├── PdfDiffView.cs
│   │   ├── PdfNativeLibrary.cs           # PDFium 原生库解析（从插件目录 runtimes/ 加载）
│   │   ├── third-party.json              # 本插件分发的三方组件登记（许可统一管理的单一事实来源）
│   │   └── ForkPlus.Plugins.Pdf.csproj   # 私有依赖 Docnet.Core（MIT）
│   ├── ForkPlus.Plugins.Office/          # Office 套件对比插件（.docx/.xlsx/.pptx 正文左右并排）
│   │   ├── OfficeDiffPlugin.cs
│   │   ├── OfficeDiffView.cs             # 徽章 / 标题卡片 / 富文本段落 / 表头斑马纹表格
│   │   ├── OfficeContentExtractor.cs     # 用 Open XML SDK 提取三件套正文（含 run 字符格式）
│   │   ├── OfficeContent.cs              # 标题 / 段落（带格式 run）/ 表格内容块模型
│   │   ├── third-party.json              # Open XML SDK 等（MIT）登记
│   │   └── ForkPlus.Plugins.Office.csproj # 私有依赖 Open XML SDK（MIT）
│   └── ForkPlus.Plugins.Archive/         # 压缩包对比插件（zip/7z/rar/tar 条目树左右并排 + MD5）
│       ├── ArchiveDiffPlugin.cs
│       ├── ArchiveDiffView.cs            # TreeView 条目树视图 + 整包 / 条目 MD5 + 密码输入
│       ├── ArchiveContentExtractor.cs    # 用 SharpCompress 把压缩包展开成条目树并算 MD5
│       ├── ArchiveContent.cs             # 条目节点 / 展开结果模型（含 MD5）
│       ├── third-party.json              # SharpCompress（MIT）登记
│       └── ForkPlus.Plugins.Archive.csproj # 私有依赖 SharpCompress（MIT）
├── licenses/                             # 第三方许可全文仓库（按组件分目录，集中管理）
│   ├── docnet-core/LICENSE.txt           # Docnet.Core（MIT）
│   ├── open-xml-sdk/LICENSE.txt          # Open XML SDK（MIT）
│   ├── dotnet-runtime/LICENSE.txt        # System.IO.Packaging（MIT）
│   ├── sharpcompress/LICENSE.txt         # SharpCompress（MIT）
│   └── pdfium/LICENSE.txt                # PDFium 及其捆绑组件（BSD-3-Clause 等）
├── Directory.Build.props                 # 仓库级公共构建属性（net10.0 / AvaloniaVersion）
├── ForkPlus.Plugins.slnx                 # 解决方案（新增插件在此登记）
├── THIRD-PARTY-NOTICES.md                # 第三方许可总览（由脚本生成，勿手改）
├── pages/                                # 生成产物：Pages 站点（index + 各插件子页 + 截图）
└── README.md
```

**新增插件**：在 `plugins/` 下新建 `ForkPlus.Plugins.<Name>/` 目录，复制示例插件的结构，
并在 `ForkPlus.Plugins.slnx` 中登记新工程即可。CI 会自动发现 `plugins/*/*.csproj`，无需改 workflow。

---

## 插件开发规范

### 1. 最小实现

一个插件 = **一个无参构造的 public 类** 实现 `IDiffViewPlugin`，并由 `CreateView()` 返回 `IDiffView`。

```csharp
public sealed class MyDiffPlugin : IDiffViewPlugin, IPluginMetadata
{
    public string Id => "com.example.myplugin";                 // 唯一 Id
    public string DisplayNameKey => "My Plugin";                // 显示名（宿主扩展名绑定列表用）
    public int Priority => 100;                                  // 同扩展名竞争，大者优先
    public IReadOnlyList<string> FileExtensions => new[] { ".mine" };

    // IPluginMetadata（可选）：宿主「偏好设置 → 插件」页展示的名称 / 版本 / 描述
    public string Version => "0.0.1";
    public string DisplayName => "我的对比插件";
    public string Description => "一句话说明这个插件能对比什么文件、长什么样。";

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
- `IPluginMetadata` **可选**：实现后宿主「偏好设置 → 插件」页展示你声明的名称 / 版本 / 描述；
  不实现则回退到 `DisplayNameKey` 的翻译结果 + 程序集版本 + 空描述（见下方「插件元数据」）。

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
- **原生库**（`.so` / `.dll` / `.dylib`）不在上述钩子覆盖范围内，需插件自行注册
  `DllImportResolver` 从插件目录解析（参见 PDF 插件 `PdfNativeLibrary`）。

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

### 8. 插件元数据（IPluginMetadata，可选）

实现 `IPluginMetadata` 后，宿主 ForkPlus **5.0.1+** 的「偏好设置 → 插件」页会展示你声明的
**名称 / 版本号 / 描述**（未实现则回退到 `DisplayNameKey` 翻译 + 程序集版本 + 空描述）：

| 成员 | 说明 |
| --- | --- |
| `Version` | 插件**自身**版本号，与宿主版本解耦，建议语义化（如 `"0.0.1"`）；与工程 `<Version>` 保持一致。 |
| `DisplayName` | 插件显示名，展示在插件列表与扩展名绑定 UI（当前版本固定中文，元数据国际化留待后续）。 |
| `Description` | 一句话描述插件能力，展示在插件名下方。 |

约定：

- 版本号与工程文件的 `<Version>` 保持一致（本仓库示例、PDF、Office 与压缩包插件当前均为 **0.0.1**）。
- 名称 / 描述在 5.0.1 固定为中文；后续版本才会引入多语言元数据（`DisplayNameKey` 仍用于翻译 key）。

---

## 示例插件

[plugins/ForkPlus.Plugins.Example](plugins/ForkPlus.Plugins.Example) 演示了 `IDiffViewPlugin` / `IDiffView`
的最小完整实现：认领 `.example` / `.exampletxt`，命中后由 `ExampleDiffView` 用纯代码（未用 `.axaml`）
渲染左右两列只读信息面板（路径 / 声明大小 / 可读字节数 / LFS 引用）。

同时实现 `IPluginMetadata`，向宿主「偏好设置 → 插件」页暴露名称「示例对比」、版本 `0.0.1` 与描述。

写新插件的最快路径：**复制示例目录 → 改工程名、命名空间、`Id`、扩展名**。

---

## PDF 对比插件

[plugins/ForkPlus.Plugins.Pdf](plugins/ForkPlus.Plugins.Pdf) 认领 `.pdf`：命中后把差异区替换为
左右两栏，按页号逐页并排渲染旧 / 新 PDF —— 两侧同页号顶对齐，纵向滚动天然同步；两侧页数不一致时，
按较大页数铺行，多出的一侧留空，新增 / 删除页面一眼可见。

渲染用 MIT 许可的 **Docnet.Core**（底层为 BSD-3-Clause 的 **PDFium** 原生库）：

- `Docnet.Core` 为私有托管依赖，与插件 DLL 同放 `plugins/`；
- 原生库按 RID 随插件包分发，运行期由插件内的 `PdfNativeLibrary`（`DllImportResolver`）
  从插件目录递归解析 `pdfium.*` / `libpdfium.*`，不依赖宿主探测；
- 第三方许可（Docnet.Core / PDFium）登记在插件目录的 `third-party.json`，
  打包时合并成 `ForkPlus.Plugins.Pdf.THIRD-PARTY-NOTICES.txt`（见「第三方许可管理」）。

> 注意：宿主只对**二进制**差异查询插件路由。PDF 一般含非文本字节、会被判为二进制；demo 截图用的
> `sample.pdf` 特意夹带 NUL 字节以确保这一点。

插件同样实现 `IPluginMetadata`，向宿主「偏好设置 → 插件」页暴露名称「PDF 对比」、版本 `0.0.1` 与描述。

---

## Office 对比插件

[plugins/ForkPlus.Plugins.Office](plugins/ForkPlus.Plugins.Office) 认领 Office 现代三件套
`.docx` / `.xlsx` / `.pptx`：命中后用 **Open XML SDK** 提取正文内容，把差异区替换为左右两栏，
各自渲染旧 / 新文档的**提取结果**（不是把文件当压缩包看字节，而是显示 Office 的内容）：

- **Word（.docx）**：按文档顺序抽段落与表格，标题样式段落升级为标题块（级别取样式名末尾数字）；
  段落里保留 run 级的加粗 / 斜体 / 下划线 / 删除线；
- **Excel（.xlsx）**：每张工作表一个标题块 + 一张单元格网格表（单表最多 400 行 × 64 列），
  网格补上列字母与行号，读起来更像表格软件；
- **PowerPoint（.pptx）**：每张幻灯片一个 `Slide N` 标题块 + 幻灯片内各文本框的文字。

呈现上尽量「像文档」而不是一坨纯文字：每栏顶部一个类型徽章（Word / Excel / PowerPoint）与
块数 / 字数，标题做成左缘强调色条的卡片（一级标题再垫浅底），表格首行做表头、其余行隔行浅底、
单元格只画右 / 下细线。

两栏内容长度往往不同，因此各自独立滚动（与 PDF 插件按页号强制顶对齐不同）。

解析用 MIT 许可的 **Open XML SDK**（`DocumentFormat.OpenXml`，微软官方、纯托管）：

- `DocumentFormat.OpenXml` / `DocumentFormat.OpenXml.Framework` / `System.IO.Packaging`
  均为私有托管依赖，与插件 DLL 同放 `plugins/`；
- 第三方许可登记在插件目录的 `third-party.json`，打包时合并成
  `ForkPlus.Plugins.Office.THIRD-PARTY-NOTICES.txt`（见「第三方许可管理」）。

> **为什么只做现代格式**：老格式 `.doc` / `.ppt` / `.xls` 在宽松许可下没有可用的 .NET 解析库
> （NPOI 仅覆盖 xls/xlsx/docx，其余可选项为商业库或引入二进制维护费 EULA），故本插件只认领 OOXML
> 三件套，不认领 `.doc` / `.ppt` / `.xls`。

> 注意：宿主只对**二进制**差异查询插件路由。Office 文档本质是 ZIP 包（OOXML），git 一律判为二进制，
> 因此必然命中本插件而非 Hex 兜底。

插件同样实现 `IPluginMetadata`，向宿主「偏好设置 → 插件」页暴露名称「Office 对比」、版本 `0.0.1` 与描述。

---

## 压缩包对比插件

[plugins/ForkPlus.Plugins.Archive](plugins/ForkPlus.Plugins.Archive) 认领主流压缩包，命中后把两侧
压缩包各自**展开成条目树**左右并排对比——用成熟的 `TreeView` 呈现层级（自带展开 / 折叠、缩进与
滚动），每条给出目录 / 文件 / 大小 / 是否加密，并计算**整包 MD5** 与**每个文件条目的内容 MD5**
（等宽字体展示），便于核对两侧内容是否一致。带密码的压缩包（如做了头部加密的 7z / rar）可在视图里
输入密码。

认领的扩展名（19 种）：

- **压缩包本体**：`.zip` / `.7z` / `.rar` / `.tar`；
- **流式压缩**（含与 tar 的组合，如 `.tar.gz` / `.tgz`）：`.gz` / `.tgz` / `.taz` / `.bz2` /
  `.tbz` / `.tbz2` / `.xz` / `.txz` / `.zst` / `.tzst`；
- **zip 系容器**：`.jar` / `.war` / `.apk` / `.nupkg` / `.whl`。

展开分两路：

- **压缩包本体**：交给 SharpCompress 的 `ArchiveFactory` 按类型识别，逐条取 `Key` / `IsDirectory` /
  `Size` / `CompressedSize` / `IsEncrypted`，再按 `/` 拆段还原成目录树（压缩包常常只登记文件、
  不登记目录，插件自动补出中间目录），按「目录在前 + 名称排序」的树序遍历展平后渲染。
- **流式压缩**：先解压少量字节嗅探——命中 tar 的 `ustar` 魔数、或文件名形如 `.tar.gz` / `.tgz`，
  就整体解压后按 tar 建树；否则视为「单文件压缩流」给出唯一一条条目。

条目数超过 20000 条会截断并在视图里说明；流式解压总量超过 512 MB 直接判失败，防压缩炸弹。

**MD5**：整包 MD5 直接对压缩包原始字节计算，稳定且无需解压；条目内容 MD5 需要逐条解压，故设额度
控制——最多 `2000` 条、单条不超过 `16 MB`、累计不超过 `64 MB`，超出即停止计算其余条目并在视图里
注明 `Entry MD5 computed for N / M files.`。加密条目不解密算哈希，其内容 MD5 留空；
zip 中央目录未加密，条目名 / 大小 / 整包 MD5 无需密码即可给出。

密码：视图顶部有密码输入框 +「应用」按钮。未给密码但压缩包已加密（头部加密的 7z / rar）提示
「需要密码」，密码错误提示「密码不正确」，点「应用」重新展开。zip 是例外——中央目录未加密，
条目名 / 大小无需密码即可列出，加密条目标注 `[encrypted]`。

解析用 MIT 许可的 **SharpCompress 1.0.0**（纯托管，`net10.0` 下无额外依赖）：

- `SharpCompress.dll` 为私有托管依赖，与插件 DLL 同放 `plugins/`；
- 第三方许可登记在插件目录的 `third-party.json`，打包时合并成
  `ForkPlus.Plugins.Archive.THIRD-PARTY-NOTICES.txt`（见「第三方许可管理」）。

> 注意：宿主只对**二进制**差异查询插件路由。压缩包一律含非文本字节、会被判为二进制，因此必然
> 命中本插件而非 Hex 兜底。

插件同样实现 `IPluginMetadata`，向宿主「偏好设置 → 插件」页暴露名称「压缩包对比」、版本 `0.0.1` 与描述。

---

## Pages 截图约定

插件对比视图的截图由 CI 在真实 ForkPlus 中现场采集（无头 X + 整屏截图），并随 Pages 一起发布上线；
站点与截图登记在 [.github/pages/plugins.json](.github/pages/plugins.json)，
采集脚本为 [.github/scripts/capture-screenshots.sh](.github/scripts/capture-screenshots.sh)。

**约定：每个插件的截图必须覆盖「增 / 删 / 改」三种变更场景**，一场景一张，全部随 Pages 发布上线：

| 场景 | 差异两侧 | 截图要点 |
| --- | --- | --- |
| 修改 | `Src` / `Dst` 都在 | 左右两栏各渲染各自内容，逐页对齐 |
| 新增 | 只有新侧（`Src == null`） | 旧侧显式标注 missing，新侧渲染全部内容 |
| 删除 | 只有旧侧（`Dst == null`） | 旧侧渲染被删内容，新侧显式标注 missing |

- 文件命名：`pages/assets/<插件>-<场景>.png`（PDF 插件即 `pdf-modify.png` / `pdf-add.png` / `pdf-remove.png`，
  Office 插件即 `office-modify.png` / `office-add.png` / `office-remove.png`，
  压缩包插件即 `archive-modify.png` / `archive-add.png` / `archive-remove.png`）；
- 截图规格：整屏 `1920×1280`，完整软件界面，不做局部裁切；
- 缺任一场景视为截图不完整；插件新增变更形态时，同步补对应场景截图与 `plugins.json` 登记。
- demo 素材由采集脚本现场构造（PDF 用 `write_demo_pdf`，Office 用 `write_demo_office`，
  压缩包用 `write_demo_archive`），纯 Python 标准库生成最小合法样本，不依赖 ghostscript /
  python-docx / 7z 等外部工具；Office 与压缩包三张截图各用一种格式
  （Office：modify=`.docx`、add=`.xlsx`、remove=`.pptx`，覆盖 Word / Excel / PowerPoint；
  压缩包：modify=`.zip`、add=`.tar.gz`、remove=`.tar.xz`）。

---

## 第三方许可管理

插件分发的第三方组件（如 PDF 插件的 Docnet.Core / PDFium、Office 插件的 Open XML SDK、
压缩包插件的 SharpCompress）**统一登记、集中存放、按包合并**，单一事实来源是两处：

1. **`licenses/`** —— 各组件许可全文的中央仓库，按组件分目录（`licenses/<组件>/LICENSE.txt`）。
   全文原样落库（含三方文件自身的编码），不依赖构建时从 NuGet 缓存临时抓取。
2. **`plugins/<插件>/third-party.json`** —— 该插件随包分发的组件登记表：

   ```json
   {
     "components": [
       {
         "name": "Docnet.Core",
         "version": "2.6.0",
         "license": "MIT",
         "copyright": "Copyright (c) 2018 Modestas Petravicius",
         "homepage": "https://github.com/GowenGit/docnet",
         "licenseFile": "licenses/docnet-core/LICENSE.txt"
       }
     ]
   }
   ```

**由登记表自动派生两份产物**（脚本：[.github/scripts/collect-third-party-notices.py](.github/scripts/collect-third-party-notices.py)）：

- **包内声明** `<Assembly>.THIRD-PARTY-NOTICES.txt`：头部列出该插件分发的组件元信息，
  后附各组件许可全文；由 `install-plugin-artifacts.sh` 在打包时生成，随 zip 分发。
- **仓库总览** [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)：汇总全仓库组件（跨插件去重）
  的表格，含版本 / 许可 / 版权 / 分发插件 / 全文链接。

**接入新许可（无需改脚本、无需改 CI）**：

1. 把许可全文放到 `licenses/<组件>/LICENSE.txt`；
2. 在使用它的插件 `third-party.json` 的 `components` 里追加一条（指向 `licenseFile`）；
3. 重新生成总览：`python3 .github/scripts/collect-third-party-notices.py repo THIRD-PARTY-NOTICES.md`。

> `THIRD-PARTY-NOTICES.md` 由脚本生成，**请勿手改**；[build.yml](.github/workflows/build.yml)
> 的 `notices` 作业会用 `--check` 校验它与登记表一致，改了登记表却忘了重新生成会直接失败。

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

- **出包**：每个平台构建一遍全部插件，用 `install-plugin-artifacts.sh` 把每个插件的
  **主 DLL + 私有依赖（托管程序集 / 原生库）+ 第三方许可声明** 打进一个包；
  宿主共享程序集（契约 / Avalonia / SkiaSharp / NLog…）由脚本排除，不随插件分发：

  ```
  ForkPlus-Plugins-<版本>-<平台>.zip
  └── plugins/
      ├── ForkPlus.Plugins.Example.dll
      ├── ForkPlus.Plugins.Pdf.dll
      ├── Docnet.Core.dll                    # PDF 插件私有依赖
      ├── pdfium.so                          # PDF 插件私有原生库（按平台）
      ├── ForkPlus.Plugins.Pdf.THIRD-PARTY-NOTICES.txt   # 三方许可声明（Docnet.Core / PDFium）
      ├── ForkPlus.Plugins.Office.dll
      ├── DocumentFormat.OpenXml.dll         # Office 插件私有依赖
      ├── DocumentFormat.OpenXml.Framework.dll
      ├── System.IO.Packaging.dll
      ├── ForkPlus.Plugins.Office.THIRD-PARTY-NOTICES.txt # 三方许可声明（Open XML SDK 等）
      ├── ForkPlus.Plugins.Archive.dll
      ├── SharpCompress.dll                  # 压缩包插件私有依赖
      ├── ForkPlus.Plugins.Archive.THIRD-PARTY-NOTICES.txt # 三方许可声明（SharpCompress）
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