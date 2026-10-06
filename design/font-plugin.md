# 字体对比插件 · 设计文档

> 状态：设计已定，未开工。
> 日期：2026-10-06。
> 相关：[README.md](../README.md)、[sdk/ForkPlus.Plugins.Abstractions](../sdk/ForkPlus.Plugins.Abstractions)。

## 1. 定位与范围

字体文件必然是二进制，路由没问题。目标不是「打开字体」，而是**并排看清两版字体的差异**——字形变化 + 元数据变化。

| | |
| --- | --- |
| 插件 Id | `forkplus.font` |
| AssemblyName | `ForkPlus.Plugins.Font` |
| 扩展名 | `.ttf` `.otf` `.ttc` `.otc` `.woff` |
| Priority | 100（与 PDF / Office / Archive 同档） |
| 默认模式 | 样张并排 |
| 其余模式 | 元数据 · 码位差异 · 单字体（`.ttc` 内切换） |

`.woff2` **一期不做**，原因见 §6。

## 2. 渲染形态

每栏 = 顶部元数据摘要 + 字体样张。

- **样张要固定**，两边渲染同一句话才能对比。建议中文 + 拉丁 + 数字 + 标点各覆盖一段，例如：
  `Hamburger 0123 ABCabc` +「汉字标点，。！？」。
- 同一句在多字号并排（如 12 / 18 / 28 / 48 px），字重/字形变化一眼可见。
- `.ttc` / `.otc` 是**字体集合**（一个文件多套字体），视图里要能选「第几套」两侧对比。

## 3. 结构化 diff（这才是差异视图的价值）

| 表 | 对比项 |
| --- | --- |
| `name` | family / subfamily / 唯一名 / 版本 / 版权 / 商标 |
| `head` | unitsPerEm、创建与修改时间、macStyle 位 |
| `OS/2` | 字重 `usWeightClass`、宽度、typo / win 升降部、italic / oblique 位 |
| `maxp` | 字形数 |
| `hhea` | ascender / descender / lineGap |
| `cmap` | **码位覆盖差异**——谁多认了哪些码位（按 Unicode block 归类呈现） |
| 布局表 | 有无 kerning（`kern` / GPOS）、OpenType 特性开关（GSUB 的 `vert` / `locl` 等） |

其中 **cmap 覆盖差异**最实用（「这版新增了哪些字符」），建议单独给一个模式，并在样张里高亮新增 / 消失的码位。

## 4. 渲染路线：先保底，再增强

Avalonia 的 `EmbeddedFontCollection` 是把 `fonts:<key>` 映射到**资源目录 URI**（`avares://...`）的，而插件的字体来自**内存里的 git blob**，没有 `avares` 路径。所以有两条路：

- **路线 B（保底，建议先做）**：用 **SkiaSharp 直接排版绘制**——`SKTypeface.FromStream` → `SKFont` → 画进 `SKBitmap` → 转 Avalonia 位图。
  - `SkiaSharp` 与 `libSkiaSharp.*`、`HarfBuzzSharp` 都是**宿主共享程序集**（见 [install-plugin-artifacts.sh](../.github/scripts/install-plugin-artifacts.sh) 的 `is_shared` 清单），插件可直接用，**不需随包分发**。
  - 代价：样张是画出来的位图，**文字不可选中 / 复制**。
- **路线 A（增强，需先验证）**：注册进 Avalonia 字体栈——自定义 `IFontCollection` 实现 + `FontManager.Current.AddFontCollection(...)`，让样张用普通 `SelectableTextBlock` 渲染，文字可选中、可跟随系统排版。
  - **可行性待验证**：`IFontCollection` 的实现细节、以及「从内存字节建 `IGlyphTypeface`」是否有公开入口，需要先做一个小 spike 再定。**在验证通过前不要把 A 写进实施计划。**

## 5. 依赖与许可

**零第三方依赖，全部自解析 sfnt 表。**

明确**不要用 SixLabors.Fonts**：它 2.x 起采用 Six Labors Split License，且 **3.0 起对直接依赖有构建期许可强制**（需要 `sixlabors.lic`），会直接打断 CI。即便退回 1.x（Apache-2.0），收益也不足以承担这个许可风险。

字体表解析本身不复杂（`head` / `name` / `OS/2` / `maxp` / `hhea` / `cmap` 都是定长或简单偏移结构），自己读即可。

## 6. 坑

- **`.woff2`**：Brotli 解压后还有 **`glyf` / `loca` 表的变换**要重建，.NET 内置 `BrotliStream` 只解决解压那一半。一期不支持，元数据里明确说明。
- **`.woff`**：只是 sfnt 的 zlib 封装（.NET 内置 `DeflateStream` 可解），可以做。
- **CJK 字体体积大**（几十 MB），`cmap` 覆盖可能上万码位 → 码位差异列表要**分块 + 设上限**（沿用 Archive 插件 `HashBudget` / Office `MaxSheetRows` 的「额度 + 截断提示」模式），否则视图会卡死。
- 控件必须在 **UI 线程**构建；表解析可以放后台线程。
- 字体文件是**不可信输入**，畸形表会导致越界读 → 解析一律 try/catch，出错降级为「只显示能读出来的部分」。

## 7. 打包与 CI

最省事的一个：**无原生依赖、无第三方件**，`install-plugin-artifacts.sh` 无需改动。

- demo 文件：CI runner 自带 DejaVu 字体，用 **fontTools**（MIT）衍生一个「改过 name / 去掉部分码位」的变体当 new 侧，即可造出真实可看的差异。
- 截图：静态模式（样张并排）在 Xvfb 下毫无压力。

## 8. 未决项

1. 渲染路线 A（注册进 Avalonia 字体栈）是否可行——需要先 spike。
2. `.woff2` 是否值得二期做。
3. 码位差异的分块粒度与上限取值。