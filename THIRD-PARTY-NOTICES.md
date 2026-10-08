# 第三方许可 / Third-Party Notices

本仓库自身以 MIT 许可发布（见 [LICENSE](LICENSE)）。插件在运行期还会随包分发下列第三方组件，
这些组件以各自的许可条款为准，完整许可全文集中存放在 [`licenses/`](licenses/)。

## 组件总览

| 组件 | 版本 | 许可 | 版权 | 分发的插件 | 全文 |
| --- | --- | --- | --- | --- | --- |
| DbcParserLib | 1.8.0 | MIT | Copyright (c) 2021 EFeru | [ForkPlus.Plugins.Dbc](plugins/ForkPlus.Plugins.Dbc) | [LICENSE.txt](licenses/dbcparserlib/LICENSE.txt) |
| Docnet.Core | 2.6.0 | MIT | Copyright (c) 2018 Modestas Petravicius | [ForkPlus.Plugins.Pdf](plugins/ForkPlus.Plugins.Pdf) | [LICENSE.txt](licenses/docnet-core/LICENSE.txt) |
| DocumentFormat.OpenXml | 3.3.0 | MIT | Copyright (c) .NET Foundation and Contributors | [ForkPlus.Plugins.Office](plugins/ForkPlus.Plugins.Office) | [LICENSE.txt](licenses/open-xml-sdk/LICENSE.txt) |
| DocumentFormat.OpenXml.Framework | 3.3.0 | MIT | Copyright (c) .NET Foundation and Contributors | [ForkPlus.Plugins.Office](plugins/ForkPlus.Plugins.Office) | [LICENSE.txt](licenses/open-xml-sdk/LICENSE.txt) |
| FFmpeg | 9.0.2 | LGPL-2.1-or-later | Copyright (c) 2000-2026 the FFmpeg developers | [ForkPlus.Plugins.Audio](plugins/ForkPlus.Plugins.Audio)、[ForkPlus.Plugins.Video](plugins/ForkPlus.Plugins.Video) | [LICENSE.txt](licenses/ffmpeg/LICENSE.txt) |
| FFmpeg.AutoGen | 9.0.1.1 | LGPL-3.0-or-later | Copyright (c) Ruslan Balanukhin | [ForkPlus.Plugins.Audio](plugins/ForkPlus.Plugins.Audio)、[ForkPlus.Plugins.Video](plugins/ForkPlus.Plugins.Video) | [LICENSE.txt](licenses/ffmpeg-autogen/LICENSE.txt) |
| FFmpeg.AutoGen.Abstractions | 9.0.1.1 | LGPL-3.0-or-later | Copyright (c) Ruslan Balanukhin | [ForkPlus.Plugins.Audio](plugins/ForkPlus.Plugins.Audio)、[ForkPlus.Plugins.Video](plugins/ForkPlus.Plugins.Video) | [LICENSE.txt](licenses/ffmpeg-autogen/LICENSE.txt) |
| FFmpeg.AutoGen.Bindings.DynamicallyLoaded | 9.0.1.1 | LGPL-3.0-or-later | Copyright (c) Ruslan Balanukhin | [ForkPlus.Plugins.Audio](plugins/ForkPlus.Plugins.Audio)、[ForkPlus.Plugins.Video](plugins/ForkPlus.Plugins.Video) | [LICENSE.txt](licenses/ffmpeg-autogen/LICENSE.txt) |
| PDFium | 随 Docnet.Core 2.6.0 的原生运行时分发 | BSD-3-Clause（PDFium 本体；同文件另含其捆绑组件各自许可） | Copyright 2014 The PDFium Authors | [ForkPlus.Plugins.Pdf](plugins/ForkPlus.Plugins.Pdf) | [LICENSE.txt](licenses/pdfium/LICENSE.txt) |
| SharpCompress | 1.0.0 | MIT | Copyright (c) 2014 Adam Hathcock | [ForkPlus.Plugins.Archive](plugins/ForkPlus.Plugins.Archive) | [LICENSE.txt](licenses/sharpcompress/LICENSE.txt) |
| System.IO.Packaging | 8.0.1 | MIT | Copyright (c) .NET Foundation and Contributors | [ForkPlus.Plugins.Office](plugins/ForkPlus.Plugins.Office) | [LICENSE.txt](licenses/dotnet-runtime/LICENSE.txt) |
| System.Security.Cryptography.Pkcs | 10.0.0 | MIT | Copyright (c) .NET Foundation and Contributors | [ForkPlus.Plugins.Certificate](plugins/ForkPlus.Plugins.Certificate) | [LICENSE.txt](licenses/dotnet-runtime/LICENSE.txt) |
| Tomlyn | 0.19.0 | BSD-2-Clause | Copyright (c) 2019-2026, Alexandre Mutel | [ForkPlus.Plugins.Structured](plugins/ForkPlus.Plugins.Structured) | [LICENSE.txt](licenses/tomlyn/LICENSE.txt) |
| YamlDotNet | 16.3.0 | MIT | Copyright (c) 2008-2014 Antoine Aubry and contributors | [ForkPlus.Plugins.Structured](plugins/ForkPlus.Plugins.Structured) | [LICENSE.txt](licenses/yamldotnet/LICENSE.txt) |
| miniaudio | 0.11.25 | Unlicense OR MIT-0 | Copyright 2025 David Reid | [ForkPlus.Plugins.Audio](plugins/ForkPlus.Plugins.Audio)、[ForkPlus.Plugins.Video](plugins/ForkPlus.Plugins.Video) | [LICENSE.txt](licenses/miniaudio/LICENSE.txt) |

## 分发形态

每个插件包内附带一份合并声明 `<Assembly>.THIRD-PARTY-NOTICES.txt`，由
[.github/scripts/collect-third-party-notices.py](.github/scripts/collect-third-party-notices.py)
依据各插件的 `third-party.json` 与 `licenses/` 下的全文自动生成。

新增依赖：把许可全文落到 `licenses/<组件>/`，并在对应插件的 `third-party.json` 追加条目——脚本与 CI 均无需改动。

> 本文件由脚本生成，请勿手改；修改请编辑 `third-party.json` 后执行
> `python3 .github/scripts/collect-third-party-notices.py repo THIRD-PARTY-NOTICES.md`。
