# 音视频对比插件 · 设计文档

> 状态：已实现（插件 v0.0.1；解码、可视化与**播放 / 音频输出**均已落地，硬件解码见 §7.1、§8、§12）。
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

> 最终落地的模式以上表为准（音频四模式、视频四模式，视频含「播放」；音频的波形 / 频谱两模式就地试听，
> 音频输出见 §8、视频播放与硬解见 §7.1）。

扩展名无重叠（`.m4a` 音频 / `.m4v` 视频）。`.webm` 可能只含音轨，归视频插件，此时帧条 / 单帧对比
取不到视频流，自然退化成只有元数据（波形 / 频谱是音频插件的模式）。

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

**帧条 / 单帧对比不做帧率控制**，只按需取有限几帧，管线很轻；**播放**模式另走连续解码（见 §7.1）：

- 后台线程 `av_read_frame` → `avcodec_send_packet` / `avcodec_receive_frame`。
- 帧格式（`YUV420P` 等）经 `sws_scale`（`MediaConvert.FrameToImage`）转紧凑 BGRA → Avalonia `WriteableBitmap` → `Image`。
- 取帧定位：`av_seek_frame(..., AVSEEK_FLAG_BACKWARD)` 跳到目标时间前最近的关键帧，`avcodec_flush_buffers`
  清缓冲，再从该关键帧顺序解到 `pts ≥ 目标时间` 的第一帧；每次取帧只 seek 一次，长视频不必从头解到尾。
- 帧条：在时间轴上均匀取每段中点、最多 `MediaLimits.MaxFilmstripFrames`（8）帧，每帧缩到
  `MaxFilmstripWidth`（240）宽。单帧对比：缩到不超过 `MaxFrameCompareWidth`（720）× `MaxFrameCompareHeight`（480）。
- 像素差异：按 `MediaLimits.PixelDiffThreshold`（24，逐通道最大差）算变更像素比例；「高亮差异像素」
  偏好开启时把变更像素染到右侧帧上。

### 7.1 播放模式与硬件解码

播放是本期的正式能力（`ViewMode.Playback`），由共享核心 `MediaPlayback` 承载：同一份字节起**视频 / 音频两路独立解码**，
画面经 `FrameReady` 回投 `Image`，声音见 §8。

- **只播一侧**：一次只播旧或新（`试听` 切换），两侧字节不同、不共用播放器，换侧即重建。
- **硬件解码**：`MediaHwDecode` 按平台只试一种设备类型，与三方件仓交付件里启用的 hwaccel 对应——
  Windows `d3d11va`（`dxva2` 同开）、macOS `videotoolbox`、Linux `vulkan`；用「设 `hw_device_ctx`、
  由默认 `get_format` 自选硬解像素格式」这条路，拿不到可用格式即**静默回落软解，非致命**。硬解帧带
  `hw_frames_ctx`，先 `av_hwframe_transfer_data` 拷回系统内存再转 BGRA。硬解**只在连续播放启用**：
  单帧 / 帧条每次只解一两帧，设备创建开销反而更贵。
- **时钟**：以**音频已播帧数为 master**，视频按 pts 跟随，音频欠载时暂停视频等待；暂停冻结时钟，seek 两路同步。

## 8. 音频输出（已实现）

音频输出已落地，波形 / 频谱两个「有声音」模式下可就地对旧 / 新两侧试听：

- `swr_convert` 统一成设备采样格式。
- 环形缓冲 + 音频设备回调驱动；播放 / 暂停 / seek 用原子标志，`PositionChanged` 回写进度。
- 音画同步以**音频时钟为 master**（视频按 pts 丢弃或等待），够用且实现简单。
- 后端采用 **miniaudio**（public domain / MIT-0，单文件跨平台），省掉 WASAPI / CoreAudio / ALSA 三套原生代码；
  经 C ABI 垫片 `fpp_audio.h` 调用，[third_party/miniaudio](../third_party/miniaudio/manifest.json) 已随包分发。
- **降级**：无音频设备 / 输出失败时不让异常冒泡，传输条显示 `音频输出不可用: …`，可视化照常显示。

## 9. 差异呈现

**共同骨架**：左右两栏 = 元数据表 + 各自的专属可视化；变更逐行标注 `相同 · 变了 · 仅左 · 仅右`，视觉语言与现有三个插件保持一致。

- **音频**
  - 元数据表：时长 / 容器 / 码率 / 采样率 / 声道 / 编码器 tag / 标签与年份。
  - 波形：两侧共用同一时间轴对齐，按窗口能量差**高亮差异段**——「哪几秒的声音变了」一眼可见。
  - 频谱：STFT 声谱图并排（低频在下、冷→暖渐变）。
  - 封面：`ID3 APIC` / mp4 `covr` 内嵌图并排。
  - 试听：波形 / 频谱两模式下就地播放旧 / 新（播放 / 暂停 + 进度 seek），见 §8。
- **视频**
  - 元数据表：时长 / 容器 / 码率 / 分辨率 / 帧率 / 像素格式 / 色彩空间 / HDR 元数据 / 编码器 tag / 音轨 / 字幕轨 / 章节。
  - 帧条：两侧按同一时间刻度抽关键帧并排，看画面在哪几段变了。
  - 单帧对比：定位到同一时间戳取帧，做像素级差异高亮——直接复用宿主已接线的 `PluginEnvironment.HighlightImageDiff` 与 `ImageDiffHighlightPixelsChanged`（偏好设置里的「高亮差异像素」实时生效）。
  - 播放：直接解码播放旧 / 新任一侧的画面与声音（播放 / 暂停 + 进度 seek，视频优先硬解），见 §7.1。

## 10. 线程、生命周期与复用

- **控件必须在 UI 线程构建**（本仓库已有先例：后台线程构建的 Avalonia 控件不渲染）。分工：解码在后台线程，控件构建与更新经 `Dispatcher.UIThread.Post` 回 UI 线程。
- `SetContent` **每次刷新对比都会调用、实例会复用** → 进入时必须取消并等待上一次的解码任务，不能假设是首次。
- `Activate` / `Deactivate`：失活即停播——音频插件暂停当前播放器、视频插件切走播放模式时 `DisposePlayback`，避免在别的视图前继续出声 / 耗电。
- `Release`：停线程、退订 `ImageDiffHighlightPixelsChanged`、释放位图；解码上下文随 `using` / 收尾释放。
- 每轮渲染一份 `CancellationTokenSource` + 递增的**代次**（generation），随 `SetContent` / 切模式 / `Release` 取消上一轮；回投 UI 线程时校验代次，旧任务结果不会画到本次视图。

## 11. 额度与安全

- 沿用仓库既有模式（Archive 的 `HashBudget`（条目数 + 字节双上限）、Office 的 `MaxSheetRows`）：设**最多解码帧数 / 最多分析秒数**上限，超出即停并在视图里注明 `analyzed first N of M`。
- 恶意媒体比恶意压缩包更容易打崩解析器（FFmpeg 历史 CVE 不少），因此：解码一律在后台线程、**全局 try/catch 包住**，出错降级为「元数据 + 错误提示」，绝不让异常冒泡到宿主。

## 12. 依赖与许可

| 组件 | 版本 | 许可 | 用途 |
| --- | --- | --- | --- |
| FFmpeg 原生件 | **9.0.2**（三方件仓 [ForkPlus-Plugins-Third_Party](https://github.com/hebin123456/ForkPlus-Plugins-Third_Party) 按 `n9.0.2` 源码自建，四个 RID 一套 configure） | LGPL-2.1-or-later | 解码 / 格式解析 / 缩放 / 重采样 |
| FFmpeg.AutoGen | **9.0.1.1**（自包含动态绑定，自带 `DynamicallyLoadedBindings` 与各平台解析器） | LGPL-3.0-or-later | P/Invoke 绑定 |
| miniaudio | **0.11.25**（三方件仓自建，经 C ABI 垫片 `fpp_audio.h`，运行期库 `fpp_audio`，随包分发） | public domain / MIT-0 | 跨平台音频输出（见 §8） |

**两条硬约束**：

1. **只解码不编码**（本次决策）。构建 / 选用 FFmpeg 时**绝不能带 `--enable-gpl`**，否则整个插件被迫 GPL；同时不启用任何编码器。
2. **绑定与原生库大版本必须一致**。`FFmpeg.AutoGen` 的 NuGet 版本号与 FFmpeg 发行版**同步**（`9.0.x` ↔ FFmpeg `9.0.x`），锁定 9.0.x 即可保证 ABI 匹配——版本错配是崩溃级问题。

另需注意：编解码专利（H.264 / AAC 等）与开源许可无关，只解码也无法完全规避，需自行评估。

登记沿用既有流程：`licenses/<component>/LICENSE.txt` 全文 + 两个插件的 `third-party.json` 登记，CI 的 `collect-third-party-notices.py --check` 会拦住「登记了但缺全文」的漂移。

## 13. 三方件锁定与 `third_party/` 布局

```
三方件仓 ForkPlus-Plugins-Third_Party/ffmpeg/            源码自建的构建配方（manifest.json + build.sh，启用平台对应 hwaccel）
三方件仓 ForkPlus-Plugins-Third_Party/miniaudio/        源码自建的构建配方（manifest.json + build.sh + fpp_audio 垫片）
third_party/ffmpeg/manifest.json                        取件说明：来源 / 许可 / 运行期库 / 各 RID（版本以三方件仓为准）
third_party/ffmpeg/<rid>/*.dll|*.so.*|*.dylib           实际二进制（由 fetch-third-party.sh 取件，不入库）
third_party/miniaudio/manifest.json                     取件说明：来源 / 许可 / 运行期库 fpp_audio / 各 RID
third_party/miniaudio/<rid>/libfpp_audio.*              实际二进制（由 fetch-third-party.sh 取件，不入库）
licenses/ffmpeg/LICENSE.txt                             许可全文（沿用现有 licenses/ 约定）
licenses/ffmpeg-autogen/LICENSE.txt                     绑定许可全文
licenses/miniaudio/LICENSE.txt                          miniaudio 许可全文（MIT-0）
plugins/ForkPlus.Plugins.{Audio,Video}/third-party.json  登记（沿用现有约定）
.github/scripts/fetch-third-party.sh                    按 RID 取三方件仓最新 Release + 校验 sha256 + 解出运行期库
```

与现有文件的**分工**，避免看起来像重复：

- `third_party/` 只放**件与锁**（二进制 + 版本 / 来源 / 哈希）；
- `licenses/` 放**许可全文**；
- `third-party.json` 做**登记**（供 CI 校验与生成 `THIRD-PARTY-NOTICES.md`）。

各 RID 来源：

| RID | 构建环境 | 状态 |
| --- | --- | --- |
| `win-x64` | 三方件仓自建（MSYS2 MINGW64） | 出包 |
| `linux-x64` | 三方件仓自建（ubuntu-latest） | 出包 |
| `linux-arm64` | 三方件仓自建（ubuntu-22.04-arm） | 出包 |
| `osx-arm64` | 三方件仓自建（macos-latest） | 出包；dylib 的 install_name 设为 `@loader_path`，同目录依赖可解析 |

- 四个 RID 都有交付件，**不再有平台降级**。`sha256` 由三方件仓出包时按实际产物写进 Release 的 `index.json`，取件脚本据此强校验——**不锁版本但锁哈希**。
- Linux 产物按 SONAME（`libavformat.so.63`）命名（打包脚本已把软链目标落成实体文件），Windows 按 `<name>-<major>.dll`，macOS 按 `lib<name>.<major>.dylib`，故 `install-plugin-artifacts.sh` 须匹配 `*.so` / `*.so.*` / `*.dylib`（已改）。
- 现有 CI 只校验「登记 vs 许可全文」，**不校验二进制哈希**；二进制的完整性由取件脚本按 Release 的 `index.json` 强校验（同一次 Release 内自洽，等价保护）。若要防「件被换过」再进 `--check` 亦可（可选）。

## 14. 打包与 CI 改动清单（均已落地）

1. **[install-plugin-artifacts.sh](../.github/scripts/install-plugin-artifacts.sh)**：平铺拷贝 `*.dll` / `*.so` / `*.so.*` / `*.dylib`，并排除宿主共享程序集；补上 `*.so.*` 以收录 SONAME 命名的 FFmpeg 库。
2. **[fetch-third-party.sh](../.github/scripts/fetch-third-party.sh)**：按 manifest 的 asset / sha256 取件并校验，解出运行期库到 `third_party/<component>/<rid>/`；构建时由插件的 csproj 按 RID 拷进输出（缺件不报错，降级）。已同时支持 `ffmpeg` 与 `miniaudio` 两个组件。
3. **[capture-screenshots.sh](../.github/scripts/capture-screenshots.sh)**：新增 demo 音频 / 视频素材（系统 `ffmpeg` 现造，音频 mp3 内嵌随版本变色的封面）；构建插件前先 `fetch-third-party.sh`；三个场景走默认元数据模式，另经 `FORKPLUS_PLUGIN_VIEW_MODE` 环境变量把音频切到波形 / 频谱 / 封面、视频切到帧条 / 单帧对比 / 播放各截一张。
4. **[pages/plugins.json](../.github/pages/plugins.json)**：新增两个插件条目；音频 6 张（3 场景 + 波形 / 频谱 / 封面）、视频 6 张（3 场景 + 帧条 / 单帧对比 / 播放）截图，并补充播放 / 硬解 / miniaudio 说明与章节。
5. **[build.yml](../.github/workflows/build.yml)**：构建前新增 `Fetch FFmpeg native libraries` 一步（按矩阵 RID 取件）；**[pages.yml](../.github/workflows/pages.yml)** 的 headless 工具链补装 `ffmpeg`。
6. **[.gitignore](../.gitignore)**：忽略 `third_party/ffmpeg/*/`（二进制不入库，只锁锁文件）。

设计文档本身不需要接入任何 workflow。

## 15. 遗留项

已定：300 MB 取方案 A（§5）；频谱一期就做；**播放 / 音频输出已落地**（§7.1、§8），音频后端采用 miniaudio。

仍未决：

1. 视频硬解在无 GPU / 无对应驱动的环境（含 CI 的 Xvfb）会自动回落软解——是否要在传输条上把「已回落软解」也显式提示（当前仅在硬解生效时显示 `硬件解码`）。
2. 是否把二进制 sha256 校验再并入 CI 的 `--check`（§13）。
3. 播放期间视频帧到 Avalonia `WriteableBitmap` 的零拷贝路径（当前每帧 `sws_scale` 转 BGRA 后整帧上传），高分辨率下可再优化。