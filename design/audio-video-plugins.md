# 音视频对比插件 · 设计文档

> 状态：设计已定，未开工。
> 日期：2026-10-06。
> 相关：[README.md](../README.md)（插件开发规范）、[sdk/ForkPlus.Plugins.Abstractions](../sdk/ForkPlus.Plugins.Abstractions)（契约）、[third_party/ffmpeg/manifest.json](../third_party/ffmpeg/manifest.json)（原生件锁定）。

## 1. 决策摘要

| 议题 | 结论 |
| --- | --- |
| 插件拆分 | **两个独立插件**：`forkplus.audio`（音频）/ `forkplus.video`（视频） |
| 解码路线 | **FFmpeg.AutoGen 自绘**（不引 VLC / LibVLCSharp） |
| 能力边界 | 硬约束：**只解码，不编码** |
| 体积阈值 | 单侧声明大小 **> 300 MB 不预览**（不渲染媒体内容，只给提示） |
| 三方件 | 新建 `third_party/` 目录，锁定 FFmpeg 原生件的版本与来源 |

## 2. 为什么自绘

相对 VLC / LibVLCSharp 路线，自绘有两个决定性优势：

- **headless 可截屏**：`sws_scale` + `WriteableBitmap` 全程 CPU，不依赖 GL/显示服务，CI 的 Xvfb 里能稳定出图。VLC 的视频输出在无头环境下很脆。
- **只带最小库、许可干净**：只需要 `avformat / avcodec / avutil / swscale / swresample`，避开 VLC 那套「核心 LGPL 但插件目录混有 GPL 组件」的选件麻烦。

代价是渲染、音画同步、音频输出都得自己写——尤其 FFmpeg **只解码不出声**，音频输出需要另找后端（见 §8）。

## 3. 插件定义

两个插件都按仓库既有惯例：`Priority => 100`（与 PDF / Office / Archive 同档，高于内置 Hex 通配兜底 0），`Version` 从 `0.0.1` 起，工程内 `CopyLocalLockFileAssemblies=true`（要带私有依赖）。

| | 音频插件 | 视频插件 |
| --- | --- | --- |
| 插件 Id | `forkplus.audio` | `forkplus.video` |
| AssemblyName | `ForkPlus.Plugins.Audio` | `ForkPlus.Plugins.Video` |
| 扩展名 | `.mp3` `.wav` `.flac` `.ogg` `.oga` `.opus` `.m4a` `.aac` `.wma` | `.mp4` `.mkv` `.mov` `.webm` `.avi` `.m4v` `.mpg` `.mpeg` `.wmv` `.flv` |
| 默认模式 | 元数据 | 元数据 |
| 其余模式 | 波形 · 频谱 · 封面 | 帧条 · 单帧对比 · 播放 |

扩展名无重叠（`.m4a` 音频 / `.m4v` 视频）。`.webm` 可能只含音轨，归视频插件，此时自然退化成波形 + 元数据。

## 4. 共享解码核心的落位

两个插件的解码、AVIO、像素转换、音频重采样逻辑完全相同，抽一个共享工程：

- **落位：`sdk/ForkPlus.Plugins.Media/`，不要放 `plugins/`**。原因：CI 的插件发现是 `plugins/*/*.csproj` **全量构建并逐个打包**（见 [build.yml](../.github/workflows/build.yml) 与 [install-plugin-artifacts.sh](../.github/scripts/install-plugin-artifacts.sh)），共享库放进 `plugins/` 会被当成一个「插件」打包出多余产物。放 `sdk/` 则只作为两个插件的 `ProjectReference` 被传递构建。
- 核心 DLL 会作为**私有依赖**随两个插件各自拷出一份（打包脚本只排除宿主共享程序集清单：契约 / Ui / Avalonia / SkiaSharp / HarfBuzzSharp / NLog / MicroCom）。与 PDF 插件带 `Docnet.Core.dll` 同理。
- 两个插件互不通信，各自加载一份同名程序集不存在跨插件类型身份问题。

## 5. 路由与 300 MB 阈值

- `FileExtensions` 精确命中，`Priority 100`。
- 时点在 `CanHandle(DiffViewRequest)`——**此阶段字节尚未加载**，但请求里带了**声明大小** `SrcSize` / `DstSize`（可空）。

闸法二选一，默认取 A：

- **A（默认）**：`CanHandle` 返回 `true`，视图内检测到任一侧 > 300 MB 时**不渲染媒体内容**，只显示一行提示。
- **B**：`CanHandle` 返回 `false` → 宿主继续问下一个候选 → 最终落到通配 Hex 兜底，用户改看十六进制。

> 用户口径是「不显示」，故取 A。若希望「超大文件改看 Hex」，改 B 即可，是一行的事。

大小未知（`SrcSize`/`DstSize` 为 null）时放行；进入视图后若 `DiffSideContent.Data` 仍为 null（宿主侧超阈值 / 无缓存），走同一个提示分支。

## 6. 输入源：自定义 AVIOContext（最容易翻车的地方）

**两侧都是 git blob 内容（旧 / 新版本），不是磁盘上的文件。** `DiffSideContent.Path` 只是仓库路径（供扩展名 / 图标 / 保存建议名），字节得走 `LoadData` / `Data`，且超阈值会返回 null。所以：

- **不能** `avformat_open_input(路径)`，必须 `avio_alloc_context` + `read_packet` / `seek` 回调，从 `MemoryStream` 喂给 FFmpeg。
- 实现要点：
  1. **回调委托必须用静态字段 / 长生命周期对象持有**。委托被 GC 回收后原生层回调踩空指针，这是 AutoGen 最经典的崩法。
  2. 缓冲用 `av_malloc` 分配（FFmpeg 会自己 `free`）。
  3. `seek` 要处理 `SEEK_SET` / `SEEK_CUR` / `SEEK_END`，并支持 `AVSEEK_SIZE` 查询。
  4. 两侧各一套 `AVFormatContext` + `AVIOContext`，**完全独立、可并行分析**（两侧分别进各自的解密码上下文）。
  5. 收尾顺序与分配相反：`avio_context_free` / `avformat_close_input`，别漏。
- **好处**：`MemoryStream` 可 seek，所以**进度条拖动是真的能 seek 的**，不需要落临时文件。
- 内存代价：媒体文件会整份进内存。与宿主 `LoadData` 阈值对齐即可，不必自行再设一套。

## 7. 视频自绘管线

- 后台线程：`av_read_frame` → `avcodec_send_packet` / `avcodec_receive_frame`。
- 帧格式（`YUV420P` 等）经 `sws_scale` 转 BGRA → Avalonia `WriteableBitmap` → `Image`。
- 性能：1080p BGRA 每帧约 8 MB。预览要先 `sws_scale` 缩到预览尺寸并控制在 ~15 fps；全尺寸只在「单帧对比」模式给。
- 帧条取帧：用 `AVFrame.key_frame` / `AV_PKT_FLAG_KEY` 挑关键帧，配合 `av_seek_frame` 在时间轴上均匀采样。

## 8. 音频输出

- `swr_convert` 统一成设备采样格式（如 S16 交错 48 kHz 立体声）。
- 环形缓冲 + 音频设备回调驱动；播放 / 暂停 / seek 用原子标志。
- 音画同步以**音频时钟为 master**（视频按 pts 丢弃或等待），够用且实现简单。
- 后端建议 **miniaudio**（public domain / MIT-0，单文件跨平台），省掉 WASAPI / CoreAudio / ALSA 三套原生代码。备选是平台原生三套，工作量大且不好维护。

## 9. 差异呈现

**共同骨架**：左右两栏 = 元数据表 + 各自的专属可视化；变更逐行标注 `相同 · 变了 · 仅左 · 仅右`，视觉语言与现有三个插件保持一致。

- **音频**
  - 元数据表：时长 / 容器 / 码率 / 采样率 / 声道 / 编码器 tag / 标签与年份。
  - 波形：两侧共用同一时间轴对齐，按窗口能量差**高亮差异段**——「哪几秒的声音变了」一眼可见。
  - 频谱：STFT 声谱图并排（可延后到二期）。
  - 封面：`ID3 APIC` / mp4 `covr` 内嵌图并排。
- **视频**
  - 元数据表：时长 / 容器 / 码率 / 分辨率 / 帧率 / 像素格式 / 色彩空间 / HDR 元数据 / 编码器 tag / 音轨 / 字幕轨 / 章节。
  - 帧条：两侧按同一时间刻度抽关键帧并排，看画面在哪几段变了。
  - 单帧对比：定位到同一时间戳取帧，做像素级差异高亮——直接复用宿主已接线的 `PluginEnvironment.HighlightImageDiff` 与 `ImageDiffHighlightPixelsChanged`（偏好设置里的「高亮差异像素」实时生效）。
  - 播放：可选模式，不是默认。

## 10. 线程、生命周期与复用

- **控件必须在 UI 线程构建**（本仓库已有先例：后台线程构建的 Avalonia 控件不渲染）。分工：解码在后台线程，控件构建与更新经 `Dispatcher.UIThread.Post` 回 UI 线程。
- `SetContent` **每次刷新对比都会调用、实例会复用** → 进入时必须取消并等待上一次的解码任务，不能假设是首次。
- `Activate` / `Deactivate`：恢复 / 暂停播放，失活时停解码线程，省电防闪。
- `Release`：停线程、释放位图、释放 codec / sws / format / AVIO 上下文。
- 每个内容一份 `CancellationTokenSource`，随 `SetContent` / `Release` 取消。

## 11. 额度与安全

- 沿用仓库既有模式（Archive 的 `HashBudget`（条目数 + 字节双上限）、Office 的 `MaxSheetRows`）：设**最多解码帧数 / 最多分析秒数**上限，超出即停并在视图里注明 `analyzed first N of M`。
- 恶意媒体比恶意压缩包更容易打崩解析器（FFmpeg 历史 CVE 不少），因此：解码一律在后台线程、**全局 try/catch 包住**，出错降级为「元数据 + 错误提示」，绝不让异常冒泡到宿主。

## 12. 依赖与许可

| 组件 | 版本 | 许可 | 用途 |
| --- | --- | --- | --- |
| FFmpeg 原生件 | **9.0.2**（2026-09-18，9.0 分支最新稳定） | LGPL-2.1-or-later | 解码 / 格式解析 / 缩放 / 重采样 |
| FFmpeg.AutoGen | **9.0.2** | LGPL-3.0（待核实后登记） | P/Invoke 绑定 |
| miniaudio | 待锁定（见 [manifest](../third_party/miniaudio/manifest.json)） | public domain / MIT-0 | 跨平台音频输出 |

**两条硬约束**：

1. **只解码不编码**（本次决策）。构建 / 选用 FFmpeg 时**绝不能带 `--enable-gpl`**，否则整个插件被迫 GPL；同时不启用任何编码器。
2. **绑定与原生库大版本必须一致**。`FFmpeg.AutoGen` 的 NuGet 版本号与 FFmpeg 发行版**同步**（`9.0.x` ↔ FFmpeg `9.0.x`），锁定 9.0.x 即可保证 ABI 匹配——版本错配是崩溃级问题。

另需注意：编解码专利（H.264 / AAC 等）与开源许可无关，只解码也无法完全规避，需自行评估。

登记沿用既有流程：`licenses/<component>/LICENSE.txt` 全文 + 两个插件的 `third-party.json` 登记，CI 的 `collect-third-party-notices.py --check` 会拦住「登记了但缺全文」的漂移。

## 13. 三方件锁定与 `third_party/` 布局

```
third_party/ffmpeg/manifest.json                     锁定：版本 / 许可 / 来源 / 每 RID 资产 + sha256
third_party/ffmpeg/9.0.2/<rid>/*.dll|*.so*|*.dylib   实际二进制
third_party/miniaudio/manifest.json                  同上（音频输出后端）
third_party/miniaudio/<version>/<rid>/               实际二进制
licenses/ffmpeg/LICENSE.txt                          许可全文（沿用现有 licenses/ 约定）
licenses/miniaudio/LICENSE.txt                       同上
plugins/ForkPlus.Plugins.{Audio,Video}/third-party.json  登记（沿用现有约定）
```

与现有文件的**分工**，避免看起来像重复：

- `third_party/` 只放**件与锁**（二进制 + 版本 / 来源 / 哈希）；
- `licenses/` 放**许可全文**；
- `third-party.json` 做**登记**（供 CI 校验与生成 `THIRD-PARTY-NOTICES.md`）。

各 RID 来源：

| RID | 来源 | 状态 |
| --- | --- | --- |
| `win-x64` | BtbN/FFmpeg-Builds 的 **lgpl-shared** 变体 | 有现成构建 |
| `linux-x64` | 同上 | 有现成构建 |
| `linux-arm64` | 同上 | 有现成构建 |
| `osx-arm64` | **无现成 LGPL 共享构建**（ffmpeg.org 列的 macOS 构建多为静态 / GPL，或已停更） | **待定**：按锁定的 configure 自建，或核实第三方 LGPL 构建后再登记 |

- 二进制的 `sha256` 在 vendoring（实际放入仓库）时回填，manifest 中现留空占位。
- 现有 CI 只校验「登记 vs 许可全文」，**不校验二进制哈希**。建议增强 `--check` 去核对 `third_party` 里的 sha256（可选，防止件被换过而无人知）。

## 14. 打包与 CI 改动清单

1. **[install-plugin-artifacts.sh](../.github/scripts/install-plugin-artifacts.sh)**：现在只平铺拷贝 `*.dll` / `*.so` / `*.dylib`。FFmpeg 的 lgpl-shared 产物是平铺的，但 miniaudio 或个别构建可能带子目录 → **需确认，必要时扩展为支持目录树**。
2. 新增一步：从 `third_party/ffmpeg/<version>/<rid>/` 把原生件拷进插件输出（或在 csproj 里直接引用该路径），四个 RID 都要能找到对应件。
3. **[capture-screenshots.sh](../.github/scripts/capture-screenshots.sh)**：新增 demo 媒体（用 ffmpeg 生成体积极小的 sample），并给「视图已渲染」这个握手一个**稳定的日志行**；截图走静态默认模式（元数据 / 波形 / 帧条）。
4. **[pages/plugins.json](../.github/pages/plugins.json)**：新增两个插件条目与截图。
5. **[build.yml](../.github/workflows/build.yml)**：无需改流程（`plugins/*/*.csproj` 自动发现），但需保证四平台的第三方件齐备，否则打包会缺件。

设计文档本身不需要接入任何 workflow。

## 15. 未决项

1. 300 MB 闸法取 A 还是 B（§5）。
2. macOS（`osx-arm64`）原生件：自建还是用第三方 LGPL 构建。
3. 频谱图是否一期就做，还是延后。
4. 音频后端确认用 miniaudio。
5. 是否把 sha256 校验接进 CI 的 `--check`（§13）。