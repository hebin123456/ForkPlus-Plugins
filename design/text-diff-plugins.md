# 结构化数据 / 字幕 / SVG 对比插件 · 设计文档

> 状态：已实现（三个插件均为 v0.0.1）。
> 日期：2026-10-07。
> 相关：[README.md](../README.md)（插件开发规范）、[sdk/ForkPlus.Plugins.Abstractions](../sdk/ForkPlus.Plugins.Abstractions)（契约）。

## 1. 定位与范围

这三个插件服务同一类需求：**输入是纯文本，但逐字符的文本 diff 读不出重点**。

- **结构化数据**（`forkplus.structured`）：配置 / 数据文件改了一版，要看「哪个键加 / 删 / 改」。
- **字幕 / 时间轴**（`forkplus.subtitle`）：字幕改的是「某句话在某几秒」，要看文本改写与时间挪动。
- **SVG**（`forkplus.svg`）：矢量图差异在「某个图形挪了 / 换了颜色 / 多了一笔」，要看图形本身与元素结构。

共同做法：把两侧各自**解析成同一套数据模型**，按键路径 / cue / 元素路径做**语义 diff**，而不是把文本按行比。

| | 结构化数据 | 字幕 / 时间轴 | SVG |
| --- | --- | --- | --- |
| 插件 Id | `forkplus.structured` | `forkplus.subtitle` | `forkplus.svg` |
| AssemblyName | `ForkPlus.Plugins.Structured` | `ForkPlus.Plugins.Subtitle` | `ForkPlus.Plugins.Svg` |
| 扩展名 | `.json` `.jsonc` `.yaml` `.yml` `.toml` `.xml` `.ini` `.cfg` `.properties` | `.srt` `.vtt` `.ass` `.ssa` `.sub` | `.svg` |
| Priority | 100 | 100 | 100 |
| 默认模式 | 键路径 | 字幕行表 | 并排渲染 |
| 其余模式 | 结构树 | 时间轴 | 结构差异 |
| 私有依赖 | YamlDotNet（MIT）/ Tomlyn（BSD-2-Clause） | 无 | 无 |

## 2. 路由决策：只在自己绑定扩展名后生效

宿主**只对二进制差异查询插件路由**，纯文本差异固定由内置文本编辑器渲染。这三个插件的输入都是文本，
因此 **`CanHandle` 一律返回 `true`，但自动路由通常不会命中**——要使用对应视图，需在宿主
「偏好设置 → 扩展名绑定」里把扩展名绑定到插件（用户绑定优先级最高）。

之所以仍然实现，是因为这类文件的「结构级差异」用文本 diff 很难读；接受「需手动绑定」这一代价，
换取一眼看懂键 / 句 / 图形层面的变化。README 与 Pages 登记表都显式写明这一限制，不制造「声明了却
从不生效」的假象。

> demo / 截图：文本样本靠 `.gitattributes` 的 `<file> -diff` 属性让 git 按二进制上报差异，
> 从而走插件路由（见 `capture-screenshots.sh` 的 `prepare_repo_text`）。

## 3. 统一数据模型与 diff

三个插件各自定义一份统一模型，再套同一套「拍平 + 索引 + 合并」的 diff 骨架，四色语义一致：

| 插件 | 节点模型 | 差异算法 | 状态 |
| --- | --- | --- | --- |
| 结构化数据 | `DataNode`（`Scalar / Map / Seq`） | `DataFlatten.Flatten` 拍平成键路径 → 标量，`DataDiff.Compute` 合并出 `DiffSummary` | `Same / Changed / LeftOnly / RightOnly` |
| 字幕 | `SubtitleCue`（序号 / 起止毫秒 / 文本 / 附加信息） | `SubtitleText.Normalize` 归一化后由 `SubtitleDiff.Compute` 双指针 + 有限前瞻对齐 | 同上 |
| SVG | `SvgNode`（标签 / id / 属性 / 子节点 / 序号） | `SvgFlatten.Flatten` 拍平成元素路径 → 属性，`SvgDiff.Compute` 合并出 `SvgDiffResult` | 同上 |

- 结构化数据：键路径用点号连接、数组下标写成 `[i]`（如 `server.ports[0]`）。
- 字幕：先按**归一化文本**配对、再按时间配对，避免「只差一个空格」被误判成改写。
- SVG：`<text>` / `<tspan>` 的直接文本挂成伪属性 `#text`，让改字也能被 diff 报出。

## 4. 视图模式

每个插件都是单视图两模式，自建分段工具条切换（当前项浅蓝底 + 半粗），共用同一套标题 / 状态 / 内容区。

- **结构化数据**：键路径表（四列「键路径 / 旧值 / 新值 / 状态」）· 结构树（按层级递归展开、父节点聚合状态）。
- **字幕**：字幕行表（五列「旧时间 / 旧文本 / 新时间 / 新文本 / 状态」）· 时间轴（旧 / 新两条轨道按总时长等比铺开）。
- **SVG**：并排渲染（按各自 `viewBox` 等比缩放，`Viewbox` 自适应）· 结构差异（元素路径 + 属性逐项比较）。

呈现沿用仓库既有视觉语言：标题直接采用宿主注入的 `context.SrcTitleBrush` / `context.DstTitleBrush`
着色跟随主题；四色底 / 状态小标签与其它插件同口径。

## 5. 解析路线

- 结构化数据 `StructuredParser.Parse`：JSON / JSONC 走 `System.Text.Json`；YAML 用 YamlDotNet 的表示
  模型（**保序**）；TOML 用 Tomlyn；XML 用 `System.Xml.Linq`；INI / `.cfg` / `.properties` 自写解析器。
- 字幕 `SubtitleParser.Parse`：SRT / WebVTT / ASS / SSA / MicroDVD(`.sub`，兼容 SubViewer) 五种格式各一个
  解析器，统一归一化成 `SubtitleCue`；格式特有的样式标签归入 `Extra`，不参与文本比较。
- SVG `SvgParser.Parse`：`System.Xml.Linq` 解析元素树，读 `width / height / viewBox`，建立 `id → 节点`
  索引供 `url(#id)` 引用解析；`SvgRenderer` 把元素树转成 Avalonia 图形原语
  （`Rectangle / Ellipse / Line / Polyline / Polygon / Path`，`g` 递归、`transform` 级联、`fill / stroke` 继承）。

三类解析异常都不冒泡：转成 `error` 文本由状态行显示。

## 6. 阈值、线程与降级

- 单侧声明大小 > `MaxSideBytes`（300 MB）时只显示 `File too large to preview`，两侧都不解析；大小未知时放行。
- 解析 / diff / 渲染在后台线程（`Task.Run`），控件只在 UI 线程经 `Dispatcher.UIThread.Post` 构建。
- 每次刷新 / 切模式用「代次 `_generation` + `CancellationTokenSource`」取消上一轮，回投时再校验代次，
  切文件后旧任务结果不会画到本次视图。
- 渲染预算：结构化数据 / SVG 行数上限 4000、字幕 3000，单值展示上限 300 字符（截断不影响比较）。

## 7. 依赖与许可

- 结构化数据随包分发 **YamlDotNet**（MIT）与 **Tomlyn**（BSD-2-Clause），登记在
  `plugins/ForkPlus.Plugins.Structured/third-party.json`，打包时合并成
  `ForkPlus.Plugins.Structured.THIRD-PARTY-NOTICES.txt`（见根 `THIRD-PARTY-NOTICES.md`）。
- 字幕与 SVG **零第三方依赖**，随包进 `plugins/` 的只有插件自身主 DLL。
