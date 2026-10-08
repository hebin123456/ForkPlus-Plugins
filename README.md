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
│   │   ├── fetch-third-party.sh          # 按 RID 取三方件仓最新 Release 的原生库（校验 sha256，不锁版本）
│   │   ├── collect-third-party-notices.py# 由插件登记表 + licenses/ 全文生成第三方许可声明
│   │   ├── capture-screenshots.sh        # Pages 截图采集（装插件 → 无头启动 ForkPlus → 截图）
│   │   ├── release-notes.py              # 由 conventional commit 生成 Release 版本说明
│   │   └── build-pages.py                # Pages 站点生成
│   ├── pages/                            # Pages 模板与插件登记表（template-*.html / style.css / plugins.json）
│   └── workflows/
│       ├── build.yml                     # GitHub Actions：四平台构建 + 打包 + Release（附版本说明）
│       └── pages.yml                     # GitHub Actions：截图 + 生成站点 + 发布 Pages
├── sdk/
│   └── ForkPlus.Plugins.Abstractions/    # 插件契约工程（主仓 src/ForkPlus.Plugins.Abstractions 的源码镜像）
│       ├── IDiffViewPlugin.cs            # IDiffViewPlugin / DiffViewRequest
│       ├── IPluginMetadata.cs            # IPluginMetadata 插件元数据（名称 / 版本 / 描述，可选实现；v5.0.3 支持多语言）
│       ├── PluginLocalization.cs         # v5.0.3 多语言查表助手（插件自带译文的语言回退）
│       ├── IDiffView.cs                  # IDiffView / DiffViewMode / DiffViewContext
│       ├── IDiffViewHost.cs              # 宿主能力桥 IDiffViewHost / LfsSmudgeResult
│       ├── DiffSideContent.cs            # 单侧内容模型（路径 / 大小 / 懒加载字节 / LFS）
│       ├── LfsRef.cs                     # LFS 指针投影
│       ├── PluginEnvironment.cs          # 宿主能力桥（本地化 / 图标 / 剪贴板 / Hex 设置…）
│       ├── PluginLog.cs                  # 插件日志门面（NLog）
│       ├── PluginSizeFormat.cs           # 文件尺寸格式化
│       ├── NullAttribute.cs              # 可空性标注
│       └── ForkPlus.Plugins.Abstractions.csproj
│   └── ForkPlus.Plugins.Media/           # 音视频插件共享解码核心（只解码不编码，FFmpeg.AutoGen 动态绑定）
│       ├── MediaNative.cs                # 按 RID 动态绑定 FFmpeg 原生库；av_log 调级
│       ├── MemoryAvioContext.cs          # 内存 AVIO 回调（把 git blob 直接喂给 FFmpeg，可 seek）
│       ├── MediaProbe.cs                 # 容器 / 流 / 标签 / 内嵌封面探测
│       ├── MediaAudio.cs                 # 波形包络 + STFT 频谱分析
│       ├── MediaVideo.cs                 # 关键帧定位取帧 + 帧条
│       ├── MediaConvert.cs               # sws_scale 像素转换 / 音频重采样
│       ├── AudioDecoder.cs               # 解码循环（send_packet / receive_frame）
│       ├── Fft.cs / FfError.cs           # 基 2 FFT / FFmpeg 错误码转文本
│       ├── MediaModels.cs                # 探测 / 波形 / 频谱 / 帧模型 + 额度上限（MediaLimits）
│       └── ForkPlus.Plugins.Media.csproj # 私有依赖 FFmpeg.AutoGen（LGPL）
├── plugins/
│   ├── Directory.Build.props             # 插件公共属性：统一引用 SDK 契约（Private=false）
│   ├── ForkPlus.Plugins.Example/         # 示例插件（新插件复制本目录即可）
│   │   ├── ExampleDiffPlugin.cs
│   │   ├── ExampleDiffView.cs
│   │   ├── Localization/
│   │   │   └── ExampleStrings.cs         # 插件自带译文（8 语言；元数据 + 界面文案）
│   │   └── ForkPlus.Plugins.Example.csproj
│   ├── ForkPlus.Plugins.Pdf/             # PDF 对比插件（左右两栏逐页并排渲染旧 / 新 PDF）
│   │   ├── PdfDiffPlugin.cs
│   │   ├── PdfDiffView.cs
│   │   ├── PdfNativeLibrary.cs           # PDFium 原生库解析（从插件目录 runtimes/ 加载）
│   │   ├── Localization/
│   │   │   └── PdfStrings.cs             # 插件自带译文（8 语言）
│   │   ├── third-party.json              # 本插件分发的三方组件登记（许可统一管理的单一事实来源）
│   │   └── ForkPlus.Plugins.Pdf.csproj   # 私有依赖 Docnet.Core（MIT）
│   ├── ForkPlus.Plugins.Office/          # Office 套件对比插件（.docx/.xlsx/.pptx 正文左右并排）
│   │   ├── OfficeDiffPlugin.cs
│   │   ├── OfficeDiffView.cs             # 徽章 / 标题卡片 / 富文本段落 / 表头斑马纹表格
│   │   ├── OfficeContentExtractor.cs     # 用 Open XML SDK 提取三件套正文（含 run 字符格式）
│   │   ├── OfficeContent.cs              # 标题 / 段落（带格式 run）/ 表格内容块模型
│   │   ├── Localization/
│   │   │   └── OfficeStrings.cs          # 插件自带译文（8 语言）
│   │   ├── third-party.json              # Open XML SDK 等（MIT）登记
│   │   └── ForkPlus.Plugins.Office.csproj # 私有依赖 Open XML SDK（MIT）
│   ├── ForkPlus.Plugins.Archive/         # 压缩包对比插件（zip/7z/rar/tar 条目树左右并排 + MD5）
│   │   ├── ArchiveDiffPlugin.cs
│   │   ├── ArchiveDiffView.cs            # TreeView 条目树视图 + 整包 / 条目 MD5 + 密码输入
│   │   ├── ArchiveContentExtractor.cs    # 用 SharpCompress 把压缩包展开成条目树并算 MD5
│   │   ├── ArchiveContent.cs             # 条目节点 / 展开结果模型（含 MD5）
│   │   ├── Localization/
│   │   │   └── ArchiveStrings.cs         # 插件自带译文（8 语言）
│   │   ├── third-party.json              # SharpCompress（MIT）登记
│   │   └── ForkPlus.Plugins.Archive.csproj # 私有依赖 SharpCompress（MIT）
│   ├── ForkPlus.Plugins.Font/            # 字体对比插件（多字号样张 + 元数据 / 码位结构化 diff）
│   │   ├── FontDiffPlugin.cs
│   │   ├── FontDiffView.cs               # 三模式视图：样张位图 / 元数据卡片 / 码位占比条
│   │   ├── FontParser.cs                 # sfnt（ttf/otf/ttc/woff）表解析 + cmap 码位
│   │   ├── FontModel.cs                  # 容器格式 / 错误分类模型
│   │   ├── FontFace.cs                   # 单套字体（元数据 / 码位 / 表存在性）
│   │   ├── UnicodeBlocks.cs              # 码位 → Unicode block 归类
│   │   ├── Localization/
│   │   │   └── FontStrings.cs            # 插件自带译文（8 语言）
│   │   └── ForkPlus.Plugins.Font.csproj  # 零私有依赖（SkiaSharp 由宿主共享）
│   ├── ForkPlus.Plugins.Executable/      # 可执行文件 / 库对比插件（PE/ELF/Mach-O/wasm/ar 结构 diff）
│   │   ├── ExecutableDiffPlugin.cs
│   │   ├── ExecutableDiffView.cs         # 四模式：摘要 / 节段 / 导入导出 / 体积堆叠条
│   │   ├── ExecutableParser.cs           # PE 走共享框架，ELF / Mach-O / wasm / ar 自解析
│   │   ├── ExecutableModel.cs            # 字段 / 节 / 依赖 / 程序集引用模型
│   │   ├── Localization/
│   │   │   └── ExecutableStrings.cs      # 插件自带译文（8 语言）
│   │   └── ForkPlus.Plugins.Executable.csproj # 零第三方依赖
│   ├── ForkPlus.Plugins.Certificate/     # 证书对比插件（身份 / 有效期时间轴 / SAN 徽章 / 证书链）
│   │   ├── CertificateDiffPlugin.cs
│   │   ├── CertificateDiffView.cs        # 详情 / 证书链两模式 + 有效期时间轴 + 彩色徽章
│   │   ├── CertificateParser.cs          # DER / PKCS#12 / PKCS#7（SignedCms）/ CRL
│   │   ├── CertificateModel.cs           # 证书字段 / SAN / 有效期状态模型
│   │   ├── Localization/
│   │   │   └── CertificateStrings.cs     # 插件自带译文（8 语言）
│   │   ├── third-party.json              # System.Security.Cryptography.Pkcs（MIT）登记
│   │   └── ForkPlus.Plugins.Certificate.csproj # 私有依赖 Pkcs（MIT）
│   ├── ForkPlus.Plugins.Structured/      # 结构化数据对比插件（键路径表 / 结构树）
│   │   ├── StructuredDiffPlugin.cs
│   │   ├── StructuredDiffView.cs         # 两模式视图：键路径表 / 结构树
│   │   ├── StructuredParser.cs           # JSON / JSONC / YAML / TOML / XML / INI 解析成统一数据树
│   │   ├── StructuredData.cs             # 统一数据模型 + 拍平 + 按键路径语义 diff
│   │   ├── Localization/
│   │   │   └── StructuredStrings.cs      # 插件自带译文（8 语言）
│   │   ├── third-party.json              # YamlDotNet（MIT）/ Tomlyn（BSD-2-Clause）登记
│   │   └── ForkPlus.Plugins.Structured.csproj # 私有依赖 YamlDotNet / Tomlyn
│   ├── ForkPlus.Plugins.Dbc/             # DBC（CAN 数据库）对比插件（键路径表 / 原文对照）
│   │   ├── DbcDiffPlugin.cs
│   │   ├── DbcDiffView.cs                # 两模式视图：键路径表 / 原文对照
│   │   ├── DbcParser.cs                  # DbcParserLib 解析为统一数据树（节点 / 报文 / 信号 / 环境变量）
│   │   ├── DbcData.cs                    # 统一数据模型 + 拍平 + 按键路径语义 diff
│   │   ├── Localization/
│   │   │   └── DbcStrings.cs             # 插件自带译文（8 语言）
│   │   ├── third-party.json              # DbcParserLib（MIT）登记
│   │   └── ForkPlus.Plugins.Dbc.csproj   # 私有依赖 DbcParserLib
│   ├── ForkPlus.Plugins.Subtitle/        # 字幕 / 时间轴对比插件（字幕行表 / 等比时间轴）
│   │   ├── SubtitleDiffPlugin.cs
│   │   ├── SubtitleDiffView.cs           # 两模式视图：字幕行表 / 时间轴
│   │   ├── SubtitleParser.cs             # SRT / VTT / ASS / SSA / MicroDVD(.sub) 解析
│   │   ├── SubtitleModel.cs              # cue 模型 + 文本归一化 + 时间轴对齐
│   │   ├── Localization/
│   │   │   └── SubtitleStrings.cs        # 插件自带译文（8 语言）
│   │   └── ForkPlus.Plugins.Subtitle.csproj # 零第三方依赖
│   ├── ForkPlus.Plugins.Svg/             # SVG 矢量图对比插件（并排渲染 / 元素结构 diff）
│   │   ├── SvgDiffPlugin.cs
│   │   ├── SvgDiffView.cs                # 两模式视图：并排渲染 / 结构差异
│   │   ├── SvgParser.cs                  # System.Xml.Linq 解析为元素树（含 viewBox / 文本）
│   │   ├── SvgRenderer.cs                # 元素树 → Avalonia 图形原语（按 viewBox 缩放）
│   │   ├── SvgData.cs                    # 元素模型 + 拍平 + 元素路径/属性 diff
│   │   ├── Localization/
│   │   │   └── SvgStrings.cs             # 插件自带译文（8 语言）
│   │   └── ForkPlus.Plugins.Svg.csproj   # 零第三方依赖
│   ├── ForkPlus.Plugins.Audio/           # 音频对比插件（元数据 / 波形 / 频谱 / 内嵌封面 / 试听）
│   │   ├── AudioDiffPlugin.cs
│   │   ├── AudioDiffView.cs              # 四模式视图 + 自建模式标签栏
│   │   ├── MediaRender.cs                # 波形 / 差异带 / 声谱图位图绘制
│   │   ├── MediaImage.cs                 # BGRA 字节 → Avalonia Bitmap
│   │   ├── Localization/
│   │   │   └── AudioStrings.cs           # 插件自带译文（8 语言）
│   │   ├── third-party.json              # FFmpeg / FFmpeg.AutoGen（LGPL）登记
│   │   └── ForkPlus.Plugins.Audio.csproj # 私有依赖共享解码核心 + 按 RID 的 FFmpeg 原生件
│   └── ForkPlus.Plugins.Video/           # 视频对比插件（元数据 / 关键帧帧条 / 单帧像素差异 / 播放）
│       ├── VideoDiffPlugin.cs
│       ├── VideoDiffView.cs              # 四模式视图 + 单帧时间轴拖动条 + 播放传输条
│       ├── MediaRender.cs                # 像素差异比 / 变更像素高亮
│       ├── MediaImage.cs                 # BGRA 字节 → Avalonia Bitmap
│       ├── Localization/
│       │   └── VideoStrings.cs           # 插件自带译文（8 语言）
│       ├── third-party.json              # FFmpeg / FFmpeg.AutoGen（LGPL）登记
│       └── ForkPlus.Plugins.Video.csproj # 私有依赖共享解码核心 + 按 RID 的 FFmpeg 原生件
├── licenses/                             # 第三方许可全文仓库（按组件分目录，集中管理）
│   ├── docnet-core/LICENSE.txt           # Docnet.Core（MIT）
│   ├── open-xml-sdk/LICENSE.txt          # Open XML SDK（MIT）
│   ├── dotnet-runtime/LICENSE.txt        # System.IO.Packaging / System.Security.Cryptography.Pkcs（MIT）
│   ├── sharpcompress/LICENSE.txt         # SharpCompress（MIT）
│   ├── pdfium/LICENSE.txt                # PDFium 及其捆绑组件（BSD-3-Clause 等）
│   ├── ffmpeg/LICENSE.txt                # FFmpeg 原生件（LGPL-2.1-or-later）
│   ├── ffmpeg-autogen/LICENSE.txt        # FFmpeg.AutoGen 绑定（LGPL-3.0-or-later）
│   ├── miniaudio/LICENSE.txt             # miniaudio 音频输出后端（Unlicense OR MIT-0）
│   ├── yamldotnet/LICENSE.txt            # YamlDotNet（MIT）
│   ├── tomlyn/LICENSE.txt                # Tomlyn（BSD-2-Clause）
│   └── dbcparserlib/LICENSE.txt          # DbcParserLib（MIT）
├── third_party/                          # 三方件「件与锁」：清单入库，二进制不入库（构建前取件）
│   ├── ffmpeg/manifest.json              # FFmpeg 来源 / 许可 / 运行期库 / 各 RID 说明（版本以三方件仓为准）
│   ├── ffmpeg/<rid>/*.dll|*.so.*|*.dylib# 实际原生件（由 fetch-third-party.sh 取入）
│   └── miniaudio/manifest.json           # 音频输出后端：来源 / 许可 / 运行期库 fpp_audio / 各 RID
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
    // 名称与描述约定为**英文原文**（缺省语言）；多语言由 GetDisplayName / GetDescription 覆写提供
    public string Version => "0.1.0";
    public string DisplayName => "My Compare";
    public string Description => "One sentence on what this plugin compares.";

    public string GetDisplayName(string language) => PluginLocalization.Resolve(language, MyStrings.DisplayNames, DisplayName);
    public string GetDescription(string language) => PluginLocalization.Resolve(language, MyStrings.Descriptions, Description);

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
| `ApplyLocalization()` | 宿主切语言时广播：先 `PluginEnvironment.ApplyLocalization(root)` 重刷宿主 key，再按自带译文表重算插件文案（含内容区可译部分） |
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
- **插件自带文案**（宿主字典里没有的）由插件自己维护：在 `Localization/<Name>Strings.cs` 里按
  「英文原文 → 各语言译文」组织字典，视图用 `<Name>Strings.T(...)` / `F(...)` 取译文；
  `ApplyLocalization()` 里重算一遍静态文案并刷新内容区，宿主切语言即时生效（见「视图生命周期」）。
- 尺寸展示用 `PluginSizeFormat`，与宿主口径一致。

### 8. 插件元数据（IPluginMetadata，可选）

实现 `IPluginMetadata` 后，宿主 ForkPlus **5.0.1+** 的「偏好设置 → 插件」页会展示你声明的
**名称 / 版本号 / 描述**（未实现则回退到 `DisplayNameKey` 翻译 + 程序集版本 + 空描述）；
**5.0.3+** 起名称 / 描述支持多语言：

| 成员 | 说明 |
| --- | --- |
| `Version` | 插件**自身**版本号，与宿主版本解耦，建议语义化（如 `"0.1.0"`）；与工程 `<Version>` 保持一致。 |
| `DisplayName` | 插件显示名，约定为**英文原文**，同时作为多语言的缺省值。 |
| `Description` | 一句话描述插件能力，约定为**英文原文**，同时作为多语言的缺省值。 |
| `GetDisplayName(language)` | v5.0.3：按界面语言返回显示名；默认实现返回英文原文，无需多语言的插件不必覆写。 |
| `GetDescription(language)` | v5.0.3：按界面语言返回描述；默认实现返回英文原文，无需多语言的插件不必覆写。 |

约定：

- 版本号与工程文件的 `<Version>` 保持一致（本仓库示例、PDF、Office 与压缩包插件当前均为 **0.1.0**；
  字体、可执行文件、证书三个插件为 **0.0.1**，随首个 v1.0.3 Release 一并分发）。
- 名称 / 描述一律写成**英文原文**（作为缺省与回退值）；插件把各语言译文按 `语言 code → 文案`
  组织成字典，经 `GetDisplayName` / `GetDescription` 覆写，用 `PluginLocalization.Resolve`
  按宿主下发的 `PluginEnvironment.CurrentLanguage` 取译文，查不到逐级回退（当前语言 → 语言主标签
  → 英文 → 英文原文）。`DisplayNameKey` 仍用于宿主扩展名绑定列表的翻译 key。
- 语言 code 与宿主界面语言一致：`en` / `zh-Hans` / `zh-Hant` / `ja-JP` / `ko-KR` / `fr-FR` /
  `de-DE` / `es-ES`；`en` 不写入字典（英文原文即缺省）。

---

## 示例插件

[plugins/ForkPlus.Plugins.Example](plugins/ForkPlus.Plugins.Example) 演示了 `IDiffViewPlugin` / `IDiffView`
的最小完整实现：认领 `.example` / `.exampletxt`，命中后由 `ExampleDiffView` 用纯代码（未用 `.axaml`）
渲染左右两列只读信息面板（路径 / 声明大小 / 可读字节数 / LFS 引用）。

同时实现 `IPluginMetadata`，向宿主「偏好设置 → 插件」页暴露名称「示例对比」（英文原文
`Example Compare`，8 语言译文见 `Localization/ExampleStrings.cs`）、版本 `0.1.0` 与描述。

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

插件同样实现 `IPluginMetadata`，向宿主「偏好设置 → 插件」页暴露名称「PDF 对比」（英文原文
`PDF Compare`，8 语言译文见 `Localization/PdfStrings.cs`）、版本 `0.1.0` 与描述。

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

插件同样实现 `IPluginMetadata`，向宿主「偏好设置 → 插件」页暴露名称「Office 对比」（英文原文
`Office Compare`，8 语言译文见 `Localization/OfficeStrings.cs`）、版本 `0.1.0` 与描述。

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

插件同样实现 `IPluginMetadata`，向宿主「偏好设置 → 插件」页暴露名称「压缩包对比」（英文原文
`Archive Compare`，8 语言译文见 `Localization/ArchiveStrings.cs`）、版本 `0.1.0` 与描述。

---

## 字体对比插件

[plugins/ForkPlus.Plugins.Font](plugins/ForkPlus.Plugins.Font) 认领 `.ttf` / `.otf` / `.ttc` / `.otc` /
`.woff`：命中后把差异区换成左右两栏，用**同一套固定样张**并排渲染旧 / 新字体，同一字号落在同一行、
天然对齐；再对 sfnt 表做**结构化 diff**——元数据与 cmap 码位覆盖，逐项按「相同 / 变了 / 仅左 / 仅右」
四色标注（对比目标不是「打开字体」，而是并排看清字形 + 元数据的变化）。

三种模式（自建工具条 `Sample / Metadata / Codepoints` 切换，共用同一份解析结果）：

- **样张**：固定样张按 `12 / 18 / 28 / 48 px` 四个字号**现渲染成位图**，左右同字号同行并排，
  最适合看字形、字宽、笔画这类肉眼差异。样张不是设个 `FontFamily` 就行——字体是用户带来的文件，
  未必已安装、也不能保证按「第几套」精确选取，因此插件在后台线程用宿主共享的 **SkiaSharp**
  （`SKTypeface.FromStream` + `SKFont` + `SKCanvas`）直接排版绘制，再把 PNG 解码成 `Image`。
  样张文本刻意全用拉丁字母 / 数字 / 标点，避免无头 runner 缺 CJK 字体画出豆腐块。
- **元数据**：两栏各自**分组卡片**（Identity / Vertical metrics / Weight & width / Tables），逐行
  「名 : 值」并四色铺底，看家族名、版本、字重、单位、各表大小与 `head` 时间戳。
- **码位**：顶部两条**水平占比条**给出覆盖量，下面统计「共有 / 仅左 / 仅右」并给示例码位，再给
  **Unicode block 归类表**（拉丁 / 希腊 / CJK / 表情…），看「新字体多了哪些字符集」。

`.ttc` / `.otc` 是「一文件多套字体」的集合容器：解析出全部套，只在确有多套时于工具栏右侧显示
「第几套」下拉，切换即以该套索引重跑渲染。

> 注意：宿主只对**二进制**差异查询插件路由。字体一律含非文本字节、git 判为二进制，因此必然命中本插件。
> `.woff2` 一期**不认领**——Brotli 解压后还有 glyf / loca 表变换需还原，与其声明一个「画不出」的扩展名，
> 不如明确不做；`.woff`（zlib 封装）则解封装后照常解析 sfnt。

本插件**零私有依赖**（SkiaSharp 为宿主共享程序集，不随包分发）。插件实现 `IPluginMetadata`，向宿主
「偏好设置 → 插件」页暴露名称「字体对比」（英文原文 `Font Compare`，8 语言译文见
`Localization/FontStrings.cs`）、版本 `0.0.1` 与描述。

---

## 可执行文件对比插件

[plugins/ForkPlus.Plugins.Executable](plugins/ForkPlus.Plugins.Executable) 认领 `.exe` / `.dll` /
`.so` / `.dylib` / `.a` / `.lib` / `.wasm`：面向**发版产物对比**——两次构建出来的可执行文件 / 动态库
到底哪里变了（依赖换了、导出符号少了、体积涨了、安全位被关了），这些用 Hex 看是看不出来的。命中后把
两侧二进制各自解析成**结构模型**，左右并排逐行按「相同 / 变了 / 仅左 / 仅右」四色标注。

四种模式（自建工具条切换，两栏各自独立滚动）：

- **结构摘要**：格式徽章 + `Header` 关键字段（架构 / 位数 / 字节序 / 类型 / 子系统 / DllCharacteristics…），
  安全位展开为 `ASLR` / `DEP` / `CFG` / `No SEH` 等**徽章**；.NET PE 另列 `Assembly references`。
- **段 · 节表**：逐行列出节 / 段 / ar 成员（名称、字节数、读 / 写 / 执行权限徽章），ar 成员额外给
  偏移与 Unix 修改时间，ELF 无名节回退为 `[N]`。
- **导入导出**：分 `Dependencies` / `Imports` / `Exports` 三类，每类内部再按「共有 / 仅左 / 仅右」
  分组，符号去重后逐个列出。
- **体积构成**：一条彩色**堆叠条**（12 色循环调色板，按体积为权重）+ WrapPanel 图例，未列入的字节
  折成「其它」；下面逐节给出体积与 `F1` 百分比。

四种格式的解析：**PE / COFF** 走共享框架内置的 `System.Reflection.PortableExecutable`（`PEReader`
读节表与 `DllCharacteristics`，`MetadataReader` 读 `AssemblyRef`，导入表解析依赖与符号）；**ELF**、
**Mach-O**（含 fat / universal 多架构）、**WebAssembly**、**ar** 归档均**自解析**。

> 注意：本插件**零第三方依赖**，随包进 `plugins/` 的只有插件自身主 DLL。已知限制：版本化的
> `libfoo.so.1.2.3` 扩展名是 `.3`，路由不到本插件——这是宿主按扩展名路由的固有限制。

插件实现 `IPluginMetadata`，向宿主「偏好设置 → 插件」页暴露名称「可执行文件对比」（英文原文
`Executable Compare`，8 语言译文见 `Localization/ExecutableStrings.cs`）、版本 `0.0.1` 与描述。

---

## 证书对比插件

[plugins/ForkPlus.Plugins.Certificate](plugins/ForkPlus.Plugins.Certificate) 认领证书容器
`.der` / 二进制 `.cer` / `.p12` / `.pfx` / `.p7b` / `.p7c` / `.crl`：证书轮换、续期、换 CA 之后，
并排看清两版证书到底变了什么——有效期、签发者、SAN 列表、密钥长度、用途，而不是对着 Base64 疙瘩看。

两模式（自建工具条 `Details / Chain` 切换）：

- **详情**：只取容器里**第一张**证书做主对比，按 `Identity`（Subject / Issuer / 序列号 / 自签名）、
  `Validity`（Not before / Not after + 时间轴）、`Key & Signature`（公钥算法与位数、签名算法与 OID）、
  `Usage`（KeyUsage / EKU / BasicConstraints）、`Extensions`（SAN、CRL 分发点、OCSP）、
  `Fingerprints`（SHA-1、SHA-256）分组，每行按四色铺一层低对比底色。
- **证书链**：逐张列出容器内证书（`.p12` / `.p7b` 常含多张）的卡片，标题带序号与 Subject，两侧用
  **SHA-1 指纹**判断同一张是否共享，仅左 / 仅右按四色铺底，顶部给出共享 / 仅左 / 仅右数量统计。

呈现上尽量「像证书」而不是一坨纯文字：

- **有效期时间轴**：把 `NotBefore → NotAfter` 画成横向圆角条，按「现在」在区间里的比例落一根竖线；
  整条颜色随状态变化（**有效绿 / 距到期 ≤30 天橙 / 已过期红**），旁边给状态词与剩余 / 逾期天数。
- **SAN / 用途彩色徽章**：SAN 按类型配色（`DNS` 蓝 / `IP` 紫 / `URI` 青 / `Email` 橙），每条一个徽章；
  KeyUsage / EKU 用中性浅底徽章逐项列出。
- 加密的 `.p12` / `.pfx` 未给密码时提示 `Password required`，视图顶部提供密码输入框 +「应用」按钮。

**安全边界（明确写死）：不导入、不导出私钥，不解密任何内容。** `.p12` / `.pfx` 即使输入了密码，
也只读**证书链与别名**，完全不碰私钥材料；导入用 `X509KeyStorageFlags.EphemeralKeySet`。
`.crl` 共享框架无解析 API，降级为只读头部信息并注明 `CRL parsing is not supported`，不假装支持。

依赖基本为零：`X509Certificate2` / `X509Certificate2Collection` 是共享框架内置；仅 PKCS#7
（`.p7b` / `.p7c`）需要 `SignedCms`，位于独立包 **System.Security.Cryptography.Pkcs**（MIT）——
按仓库流程登记在插件目录的 `third-party.json`，随包分发并合并进
`ForkPlus.Plugins.Certificate.THIRD-PARTY-NOTICES.txt`（见「第三方许可管理」）。

> 注意：`.pem` 与一般 `.crt` 是 **PEM 文本**（Base64 包裹），而宿主只对**二进制**差异查询插件路由——
> 文本差异固定由内置文本编辑器渲染。因此扩展名里故意不列它们，避免「声明了却从不生效」的假象。

插件实现 `IPluginMetadata`，向宿主「偏好设置 → 插件」页暴露名称「证书对比」（英文原文
`Certificate Compare`，8 语言译文见 `Localization/CertificateStrings.cs`）、版本 `0.0.1` 与描述。

---

## 结构化数据对比插件

[plugins/ForkPlus.Plugins.Structured](plugins/ForkPlus.Plugins.Structured) 认领常见配置 / 数据格式
`.json` / `.jsonc` / `.yaml` / `.yml` / `.toml` / `.xml` / `.ini` / `.cfg` / `.properties`：
配置改了一版之后，并排看清到底**哪个键加 / 删 / 改**了——不是逐字符比文本，而是把两侧解析成
同一套数据模型后按键路径做**语义 diff**。

两种模式（自建工具条 `Key path / Structure tree` 切换）：

- **键路径**（默认）：把两侧数据各拍平成「键路径 → 标量」的列表，取并集逐行列出，每行按
  `相同 / 已变更 / 仅左 / 仅右` 四色铺底，路径用点号 + `[i]` 下标表示（如 `server.ports[0]`），
  并附旧值 / 新值两列——新增 / 删除 / 改值的键一眼可见。
- **结构树**：按对象 / 数组的层级递归展开，父节点带状态底色，逐层看到是哪个分支变了。

解析：JSON / JSONC 走框架内置 `System.Text.Json`，YAML 用 **YamlDotNet**、TOML 用 **Tomlyn**
（均按「保序」转成统一数据树），XML 用 `System.Xml.Linq`（元素 / 属性 / 文本），
INI / `.cfg` / `.properties` 自写解析器。

> **路由说明（重要）**：宿主只对**二进制**差异查询插件路由，纯文本差异固定由内置文本编辑器渲染。
> 本插件认领的九种扩展名都是文本格式，因此**自动路由通常不会命中**；要使用本视图，需在宿主
> 「偏好设置 → 扩展名绑定」里把对应扩展名绑定到本插件（用户绑定优先级最高）。之所以仍然实现，
> 是因为 JSON / YAML / TOML 这类配置的「键级差异」用文本 diff 很难读。

依赖 YamlDotNet（MIT）与 Tomlyn（BSD-2-Clause），登记在插件目录的 `third-party.json`，
打包时合并成 `ForkPlus.Plugins.Structured.THIRD-PARTY-NOTICES.txt`（见「第三方许可管理」）。

插件实现 `IPluginMetadata`，向宿主「偏好设置 → 插件」页暴露名称「结构化数据对比」（英文原文
`Structured Data Compare`，8 语言译文见 `Localization/StructuredStrings.cs`）、版本 `0.0.1` 与描述。

---

## 字幕 / 时间轴对比插件

[plugins/ForkPlus.Plugins.Subtitle](plugins/ForkPlus.Plugins.Subtitle) 认领 `.srt` / `.vtt` /
`.ass` / `.ssa` / `.sub`：字幕改的是「某句话在某几秒」——逐行文本 diff 很难看出时间轴的挪动，
本插件把两侧解析成同一套 cue 模型后先按文本对齐、再按时间配对，给出「相同 / 已变 / 仅左 / 仅右」四类。

两种模式（自建工具条 `Cue list / Timeline` 切换）：

- **字幕行表**（默认）：逐条列出 cue（序号 / 起止时间 / 文本），两侧同一条对齐成一行，按四色标注；
  文本比较先做**归一化**（去多余空白、统一换行），避免「只差一个空格」被误判成改写。
- **时间轴**：以总时长为横轴，旧 / 新两条轨道各画自己的 cue 条，按起止时间**等比**铺开——字幕
  提前 / 延后 / 拉长一眼可见。

解析：SRT / WebVTT / ASS / SSA / MicroDVD（`.sub`）五种格式各一个解析器，统一归一化成
`SubtitleCue`（序号 / 起止毫秒 / 文本 / 附加信息）。**零第三方依赖**，随包进 `plugins/` 的只有
插件自身主 DLL。

> **路由说明（重要）**：与结构化数据插件同理——字幕都是文本，宿主只对二进制差异查询插件路由，
> 因此**自动路由通常不会命中**；要使用本视图，需在宿主「偏好设置 → 扩展名绑定」里把对应扩展名
> 绑定到本插件。

插件实现 `IPluginMetadata`，向宿主「偏好设置 → 插件」页暴露名称「字幕对比」（英文原文
`Subtitle Compare`，8 语言译文见 `Localization/SubtitleStrings.cs`）、版本 `0.0.1` 与描述。

---

## SVG 矢量图对比插件

[plugins/ForkPlus.Plugins.Svg](plugins/ForkPlus.Plugins.Svg) 认领 `.svg`：SVG 的差异往往在
「某个图形挪了 / 改了颜色 / 多了一笔」，逐字符的文本 diff 几乎读不出来——本插件把两侧各自解析成
元素树，既**并排渲染**直接比图形，也按「元素路径 + 呈现属性」做**结构 diff**。

两种模式（自建工具条 `Rendered / Structure` 切换）：

- **并排渲染**（默认）：把两侧 SVG 各自渲染成图形并排显示，按各自的 `viewBox` 等比缩放到统一尺寸
  （`Viewbox` 自适应），图形挪动 / 换色 / 增删一眼可见。
- **结构差异**：把元素树拍平成「元素路径 → 属性」列表，逐条按 `相同 / 已变更 / 仅左 / 仅右`
  四色标注；支持常见图形元素（`rect` / `circle` / `ellipse` / `line` / `polyline` / `polygon` /
  `path`）与 `g` 分组、`transform` 变换、`fill` / `stroke` 等呈现属性，`<text>` / `<tspan>` 的
  文字也作为伪属性 `#text` 参与比较。

解析走 BCL 的 `System.Xml.Linq`，渲染走 Avalonia 自带图形原语（`Rectangle` / `Ellipse` / `Line` /
`Polyline` / `Polygon` / `Path`）。**零第三方依赖**，随包进 `plugins/` 的只有插件自身主 DLL。

> **路由说明（重要）**：SVG 是 XML 文本，宿主只对二进制差异查询插件路由，因此**自动路由通常不会
> 命中**；要使用本视图，需在宿主「偏好设置 → 扩展名绑定」里把 `.svg` 绑定到本插件。

插件实现 `IPluginMetadata`，向宿主「偏好设置 → 插件」页暴露名称「SVG 对比」（英文原文
`SVG Compare`，8 语言译文见 `Localization/SvgStrings.cs`）、版本 `0.0.1` 与描述。

---

## DBC（CAN 数据库）对比插件

[plugins/ForkPlus.Plugins.Dbc](plugins/ForkPlus.Plugins.Dbc) 认领 `.dbc`：CAN 数据库改了一版，
差异往往在「某个报文 ID / DLC 变了、某个信号的起始位 / 因子 / 取值范围 / 值表改了、哪个节点 /
报文 / 信号加了删了」——逐字符的文本 diff 读不出来。本插件用第三方库 **DbcParserLib** 把两侧各自
解析成同一套数据模型后按键路径做**语义 diff**，并附带一份逐字符的原文对照。

两种模式（自建工具条 `Structured / Raw text` 切换）：

- **结构化**（默认）：把两侧数据各拍平成「键路径 → 标量」的列表，取并集逐行列出，每行按
  `相同 / 已变更 / 仅左 / 仅右` 四色铺底，并附旧值 / 新值两列——报文 / 信号 / 节点的新增、删除、
  改值一眼可见。
- **原文**：左右两栏并排展示两侧原始文本（等宽字体、可滚动），便于对照上下文。

解析覆盖：**节点**（名称 / 注释 / 属性）、**报文**（ID（十进制 / 十六进制 / 扩展帧）/ DLC / 发送方 /
注释 / 属性 / 下属信号）、**信号**（起始位 / 长度 / 字节序（Motorola / Intel）/ 数值类型 /
因子 / 偏移 / 取值范围 / 单位 / 初值 / 多路复用 / 接收方 / 值表 / 属性）、**环境变量**
（类型 / 访问权限 / 单位 / 取值范围 / 初值 / 注释 / 值表 / 属性）、**全局属性**。报文按 ID、
信号按起始位排序，保证两侧行序稳定。DbcParserLib 的语法告警不冒泡，转成树顶的 `Parse warnings`
一行提示。

> **路由说明（重要）**：宿主只对**二进制**差异查询插件路由，纯文本差异固定由内置文本编辑器渲染。
> `.dbc` 是文本格式，因此**自动路由通常不会命中**；要使用本视图，需在宿主「偏好设置 → 扩展名绑定」
> 里把 `.dbc` 绑定到本插件（用户绑定优先级最高）。

依赖 DbcParserLib（MIT，纯托管、零传递依赖），登记在插件目录的 `third-party.json`，
打包时合并成 `ForkPlus.Plugins.Dbc.THIRD-PARTY-NOTICES.txt`（见「第三方许可管理」）。

插件实现 `IPluginMetadata`，向宿主「偏好设置 → 插件」页暴露名称「DBC 对比」（英文原文
`DBC Compare`，8 语言译文见 `Localization/DbcStrings.cs`）、版本 `0.0.1` 与描述。

---

## 音频对比插件

[plugins/ForkPlus.Plugins.Audio](plugins/ForkPlus.Plugins.Audio) 认领主流音频容器
`.mp3` / `.wav` / `.flac` / `.ogg` / `.oga` / `.opus` / `.m4a` / `.aac` / `.wma`：换码率、转码、
改标签之后，并排看清两版音频到底变了什么——元数据、波形包络、声谱图、内嵌封面四路对照。

四模式（自建工具条 `Metadata / Waveform / Spectrum / Cover` 切换）：

- **元数据**（默认）：按 `Container`（格式 / 时长 / 码率 / 体积 / 流数）、`Audio streams`（逐条流的
  编码器 / 采样率 / 声道 / 声道布局 / 码率）、`Tags`（两侧标签 key 并集）分组，逐行「名 : 值」并
  四色铺底 + 行尾状态小标签。
- **波形**：两侧**同一时间轴对齐**的包络图，下接一条「差异带」——两侧 RMS 逐桶求差，越暖越不同，
  「哪几秒的声音变了」一眼可见。
- **频谱**：STFT 声谱图并排（低频在下、冷→暖渐变）。
- **封面**：内嵌封面（ID3 `APIC` / mp4 `covr`）并排；无封面时明示 `No embedded cover`。
- **试听**：波形 / 频谱两模式下，栏内可直接播放旧 / 新任一侧（播放 / 暂停 + 进度定位），
  用**同一个已解码结果**，不额外重解。

呈现上沿用既有视觉语言：两栏标题由宿主注入的 `context.SrcTitleBrush` / `context.DstTitleBrush`
着色，元数据逐行按 `相同 / 已变更 / 仅左 / 仅右` 四色标注，本侧缺失的值显示 `not present`
而不是留空，避免看起来像渲染失败。

分析有额度上限（沿用 Archive 的 `HashBudget`、Office 的 `MaxSheetRows` 模式）：波形最多
`MediaLimits.MaxAudioSeconds`（600）秒、`MaxWaveBuckets`（2048）桶，频谱最多 `MaxSpectrumSeconds`
（120）秒，超出截断并在栏内注明只分析了前若干秒。单侧声明大小超过 300 MB（`MediaLimits.MaxSideBytes`）
只给提示、不渲染媒体内容（设计文档 §5 方案 A：`CanHandle` 一律放行，由视图内判阈值）。

解码走共享核心 [sdk/ForkPlus.Plugins.Media](sdk/ForkPlus.Plugins.Media)（FFmpeg.AutoGen 动态绑定，
**只解码不编码**；播放由该核心的 `MediaPlayback` 承载，音频输出走 **miniaudio**，经 C ABI 垫片
`fpp_audio` 调用、随包分发）。探测与波形 / 频谱分析都在后台线程，控件只在 UI 线程构建，每次刷新以
「代次 + `CancellationToken`」取消上一轮；无音频设备 / 输出失败时降级为传输条提示
`Audio output unavailable`，可视化照常显示——异常绝不冒泡到宿主。

插件实现 `IPluginMetadata`，向宿主「偏好设置 → 插件」页暴露名称「音频对比」（英文原文
`Audio Compare`，8 语言译文见 `Localization/AudioStrings.cs`）、版本 `0.0.1` 与描述。

---

## 视频对比插件

[plugins/ForkPlus.Plugins.Video](plugins/ForkPlus.Plugins.Video) 认领视频容器
`.mp4` / `.mkv` / `.mov` / `.webm` / `.avi` / `.m4v` / `.mpg` / `.mpeg` / `.wmv` / `.flv`：转封装、
改码率、重编码、换分辨率之后，并排看清两版视频的差异——元数据、关键帧帧条、单帧像素级差异，
以及可就地播放的整段对照。

四模式（自建工具条 `Metadata / Filmstrip / Frame compare / Playback` 切换）：

- **元数据**（默认）：按 `Container` / `Video streams` / `Audio streams` / `Subtitles` / `Tags`
  分组，逐行四色标注。
- **帧条**：两侧**按同一时间刻度**抽帧（每段中点，最多 `MediaLimits.MaxFilmstripFrames`（8）帧、
  每帧缩到 `MaxFilmstripWidth`（240）宽），上下两条横向可滚动的缩略图带，逐位置给出像素差异比，
  看画面在哪几段变了。
- **单帧对比**：定位到同一时间戳各取一帧（两侧时长都有效时取较短者、按拖动比例定位），做像素级
  差异；工具条下常驻一条「位置」拖动条（`0–100%`，仅此模式显示，拖动即重新取帧）。开启宿主
  **「高亮差异像素」**偏好（`PluginEnvironment.HighlightImageDiff`）时把变更像素在右侧帧上染色，
  尺寸不一致则只算差异比例、不染色。
- **播放**：直接解码播放旧 / 新任一侧的画面与声音（播放 / 暂停 + 进度定位），一次只播一侧、
  换侧即重建播放器。**优先硬件解码**（Windows `d3d11va` / macOS `videotoolbox` / Linux `vulkan`），
  拿不到可用设备即静默回落软解（非致命），硬解生效时传输条显示 `Hardware decoding`。

取帧是一条共用路径：`av_seek_frame(..., AVSEEK_FLAG_BACKWARD)` 先跳到目标时间前最近的关键帧，
`avcodec_flush_buffers` 清缓冲，再顺序 `av_read_frame` → `avcodec_send_packet` / `avcodec_receive_frame`
解到 `pts ≥ 目标时间` 的第一帧；解码出的 YUV 等格式经 `sws_scale`（`MediaConvert.FrameToImage`）统一
转成紧凑 BGRA，再由 `MediaImage.FromImageData` 变成 Avalonia `Bitmap`。帧条与单帧对比共用这条路径。

与音频插件同源：解码走共享核心 [sdk/ForkPlus.Plugins.Media](sdk/ForkPlus.Plugins.Media)
（FFmpeg.AutoGen 动态绑定，**只解码不编码**；播放 / 音频输出由核心的 `MediaPlayback` 与 miniaudio
承载），300 MB 阈值同取方案 A，后台线程解码 + UI 线程建控件 + 代次取消，异常降级为状态行错误文案。
帧条 / 单帧的像素差异比按 `MediaLimits.PixelDiffThreshold`（24，逐通道最大差）计。

插件实现 `IPluginMetadata`，向宿主「偏好设置 → 插件」页暴露名称「视频对比」（英文原文
`Video Compare`，8 语言译文见 `Localization/VideoStrings.cs`）、版本 `0.0.1` 与描述。

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
  压缩包插件即 `archive-modify.png` / `archive-add.png` / `archive-remove.png`，
  字体插件即 `font-*.png`，可执行文件插件即 `executable-*.png`，证书插件即 `certificate-*.png`，
  结构化数据插件即 `structured-*.png`，字幕插件即 `subtitle-*.png`，SVG 插件即 `svg-*.png`，
  音频插件即 `audio-*.png`，视频插件即 `video-*.png`）；
- 截图规格：整屏 `1920×1280`，完整软件界面，不做局部裁切；
- 缺任一场景视为截图不完整；插件新增变更形态时，同步补对应场景截图与 `plugins.json` 登记。
- demo 素材由采集脚本现场构造（PDF 用 `write_demo_pdf`，Office 用 `write_demo_office`，
  压缩包用 `write_demo_archive`），纯 Python 标准库生成最小合法样本，不依赖 ghostscript /
  python-docx / 7z 等外部工具；Office 与压缩包三张截图各用一种格式
  （Office：modify=`.docx`、add=`.xlsx`、remove=`.pptx`，覆盖 Word / Excel / PowerPoint；
  压缩包：modify=`.zip`、add=`.tar.gz`、remove=`.tar.xz`）。
- **字体 / 可执行文件 / 证书**三类 demo 素材改用系统工具现场构造：字体用系统字体
  （`write_demo_font`，dejavu / liberation）复制成 `sample.ttf`，可执行文件用 `cc` 编译 ELF 共享库
  与 `ar` 打包静态库、`write_demo_pe` 造 PE（`write_demo_elf` / `write_demo_pe` / `write_demo_ar`），
  证书用 `openssl` 造 DER / PKCS#12 / PKCS#7（`write_demo_certificate`）。三张截图各覆盖一种形态：
  可执行文件 modify=`.so`(ELF) / add=`.dll`(PE) / remove=`.a`(ar)，证书 modify=`.der` / add=`.p12` /
  remove=`.p7b`；字体三场景用同一对系统字体（旧 DejaVu Sans → 新 DejaVu Serif）。
  这些依赖见 [pages.yml](.github/workflows/pages.yml) 的 `Install headless toolchain`
  （`fonts-dejavu` / `fonts-liberation` / `gcc` / `binutils` / `openssl`）。
- **结构化数据 / DBC / 字幕 / SVG** 四类的 demo 素材都是**纯文本**，由采集脚本用纯 Python 标准库
  直接写出（`write_demo_structured` / `write_demo_dbc` / `write_demo_subtitle` / `write_demo_svg`）。
  因为宿主只对**二进制**差异查询插件路由，这四类文本格式**自动路由不会命中**，采集脚本须在 demo
  仓库里预置「把对应扩展名绑定到本插件」的设置（用户绑定优先级最高），否则截图会落到内置文本编辑器上。
  三张截图各覆盖一种格式形态：结构化数据 modify=`.yaml` / add=`.json` / remove=`.toml`（另用
  `.ini` / `.xml` 体现解析广度），DBC 三场景共用同一对 `.dbc`（旧：两报文 / 三信号带值表；新：改因子与
  取值范围、加一条信号与一条报文、删一条信号），字幕 modify=`.srt` / add=`.vtt` / remove=`.ass`（另用
  `.ssa` / MicroDVD `.sub`），SVG 三场景共用同一对 `.svg`（旧：蓝底矩形 + 灰线；新：换色 + 挪位 + 多一笔）。
- **音频 / 视频**两类 demo 素材用 **系统 `ffmpeg` CLI** 现造（`write_demo_audio` / `write_demo_video`，
  pages.yml 装 `ffmpeg`）。注意分工：造样本用系统 ffmpeg，插件解码用随包分发的 FFmpeg 原生库，
  两者互不相干。音频三场景各用一种容器（modify=`.mp3` 有损 / add=`.wav` PCM 无损 / remove=`.flac`
  无损压缩），旧 440 Hz / 3 s / 44100 Hz、新 660 Hz / 4 s / 48000 Hz 并改标签；视频三场景
  modify=`.mp4`(H.264) / add=`.mkv`(H.264) / remove=`.avi`(MPEG-4 Part 2)，旧 `testsrc` 2 s、
  新 `testsrc2` 3 s 并做 90° 色相旋转，让帧条与单帧对比里的画面明显不同。三张截图都停在默认的
  元数据模式。
- 采集脚本在构建插件前先跑 [fetch-third-party.sh](.github/scripts/fetch-third-party.sh) 从三方件仓
  最新 Release 取原生件并校验 sha256，否则音视频插件按 RID 拷不出原生库。

---

## 第三方许可管理

插件分发的第三方组件（如 PDF 插件的 Docnet.Core / PDFium、Office 插件的 Open XML SDK、
压缩包插件的 SharpCompress、证书插件的 System.Security.Cryptography.Pkcs、结构化数据插件的
YamlDotNet / Tomlyn、DBC 插件的 DbcParserLib、音视频插件的 FFmpeg / FFmpeg.AutoGen / miniaudio）
**统一登记、集中存放、按包合并**，单一事实来源是两处：

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

**原生件（如 FFmpeg）另有一层「件与锁」**：托管依赖走 NuGet 即可，但随包分发的原生二进制不在
NuGet 里。这类件统一收到**三方件仓** [ForkPlus-Plugins-Third_Party](https://github.com/hebin123456/ForkPlus-Plugins-Third_Party)：
源码按组件分目录锁版本、自建编译，tag 触发四平台出包；本仓的取件脚本
（[fetch-third-party.sh](.github/scripts/fetch-third-party.sh)）按 RID 从**最新 Release** 取件：
先读 `releases/latest/download/index.json` 拿本 RID 的资产名与 sha256，再下载该资产、校验、
解出运行期库到 `third_party/<组件>/<rid>/`，构建时由插件工程拷进输出。二进制**不入库**
（见 [.gitignore](.gitignore)），只留 `third_party/<组件>/manifest.json` 说明来源与取件方式；
`licenses/` 全文与 `third-party.json` 登记照旧。分工不重叠：`third_party/` 放**件与锁**，
`licenses/` 放**许可全文**，`third-party.json` 做**登记**。

> **不锁版本**：三方件仓发新 tag 即自动出包，本仓 CI 下次构建自动消费最新；但哈希取自同一次
> Release 的 `index.json`，取件仍强校验。要复现某次构建，给取件脚本传 `THIRD_PARTY_TAG=<tag>`。

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

音视频插件要按 RID 构建才带得到 FFmpeg 原生件，且需先取件（原生二进制不入库）：

```bash
bash .github/scripts/fetch-third-party.sh linux-x64      # 从三方件仓最新 Release 取件 + 校验 sha256
dotnet build plugins/ForkPlus.Plugins.Audio/ForkPlus.Plugins.Audio.csproj -c Release -r linux-x64
```

> 不传 `-r <rid>` 时 csproj 里的原生件 ItemGroup 不生效，输出只有托管 DLL，运行期降级为
> 「FFmpeg decoding unavailable」。三方件仓尚未收录的 RID 取件脚本会跳过，该平台同样降级。

### 通过 GitHub Actions 出包（四平台）

workflow：[.github/workflows/build.yml](.github/workflows/build.yml)

- **触发**：push `v*` 标签 → 构建四平台并发布 Release；`workflow_dispatch` 手动 → 只构建并上传 Artifacts。
- **版本说明**：Release 的正文由 [release-notes.py](.github/scripts/release-notes.py) 依据
  **conventional commit** 自动生成——取上一个 `v*` 标签到当前标签之间的提交，按 `feat` / `fix` /
  `docs` / `ci` … 分组为「新增 / 修复 / 文档 / CI…」，去掉冗余前缀并附 Full Changelog 链接。
  因此**提交信息请遵循 `type(scope): 描述` 规范**，发版时版本说明即自动带上；如需人工润色，
  可在 Release 页面直接编辑（自动化生成仅保证「不为空」）。
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
      ├── ForkPlus.Plugins.Font.dll          # 字体插件（零私有依赖）
      ├── ForkPlus.Plugins.Executable.dll    # 可执行文件插件（零私有依赖）
      ├── ForkPlus.Plugins.Certificate.dll
      ├── System.Security.Cryptography.Pkcs.dll # 证书插件私有依赖
      ├── ForkPlus.Plugins.Certificate.THIRD-PARTY-NOTICES.txt # 三方许可声明（Pkcs）
      ├── ForkPlus.Plugins.Structured.dll
      ├── YamlDotNet.dll                     # 结构化数据插件私有依赖
      ├── Tomlyn.dll                         # 结构化数据插件私有依赖
      ├── ForkPlus.Plugins.Structured.THIRD-PARTY-NOTICES.txt # 三方许可声明（YamlDotNet / Tomlyn）
      ├── ForkPlus.Plugins.Dbc.dll
      ├── DbcParserLib.dll                   # DBC 插件私有依赖
      ├── ForkPlus.Plugins.Dbc.THIRD-PARTY-NOTICES.txt # 三方许可声明（DbcParserLib）
      ├── ForkPlus.Plugins.Subtitle.dll      # 字幕插件（零私有依赖）
      ├── ForkPlus.Plugins.Svg.dll           # SVG 插件（零私有依赖）
      ├── ForkPlus.Plugins.Audio.dll
      ├── ForkPlus.Plugins.Video.dll
      ├── ForkPlus.Plugins.Media.dll         # 音视频插件共享解码核心（私有依赖）
      ├── FFmpeg.AutoGen.dll                 # 音视频绑定（私有依赖）
      ├── libavformat.so.63                  # 音视频私有原生库（按平台 / SONAME）
      ├── libavcodec.so.63 …
      ├── ForkPlus.Plugins.Audio.THIRD-PARTY-NOTICES.txt   # 三方许可声明（FFmpeg / FFmpeg.AutoGen）
      ├── ForkPlus.Plugins.Video.THIRD-PARTY-NOTICES.txt   # 同上
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