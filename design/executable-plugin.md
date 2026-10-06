# 可执行文件 / 库对比插件 · 设计文档

> 状态：已实现（插件 v0.0.1，随 v1.0.3 发布）。
> 日期：2026-10-06。
> 相关：[README.md](../README.md)、[sdk/ForkPlus.Plugins.Abstractions](../sdk/ForkPlus.Plugins.Abstractions)。

## 1. 定位与范围

面向**发版产物对比**：两次构建出来的可执行文件 / 动态库，到底哪里变了——依赖换了、导出符号少了、体积涨了、安全位被关了。这些用 Hex 看是看不出来的。

| | |
| --- | --- |
| 插件 Id | `forkplus.executable` |
| AssemblyName | `ForkPlus.Plugins.Executable` |
| 扩展名 | `.exe` `.dll` `.so` `.dylib` `.a` `.lib` `.wasm` |
| Priority | 100 |
| 默认模式 | 结构摘要（两侧并排） |
| 其余模式 | 段 / 节表 · 导入导出 · 依赖 · 体积构成 |

同一个插件承载四种格式：**PE / ELF / Mach-O / WebAssembly**（外加 ar 归档）。

## 2. 呈现与对比项

每栏 = 格式徽章 + 一屏可分块的结构表。四种格式的对比项：

| 格式 | 对比项 |
| --- | --- |
| **PE**（`.exe` `.dll`） | COFF 头（机器、时间戳、特征位）、节表（名 / 大小 / 权限）、可选头（子系统、`DllCharacteristics`——ASLR / DEP 这些安全位）、数据目录、**导入表**（依赖的 DLL + 函数）、**导出表**（序号 / 名）；若是 .NET 程序集（COR20 目录），再列 `AssemblyRef`（引用了哪些程序集及版本）、类型 / 方法数、目标框架 |
| **ELF**（`.so`） | class / endian / 机器 / 类型、程序头、节表、`.dynamic` 的 `NEEDED`（依赖库）/ `SONAME`、符号表（导出 / 未定义）、`build-id` |
| **Mach-O**（`.dylib`） | CPU / 类型、load commands、`LC_LOAD_DYLIB`（依赖）、`LC_UUID`、`LC_BUILD_VERSION`、是否 fat（多架构） |
| **WebAssembly**（`.wasm`） | type / import / export / code / data 各段的条目与**体积占比**、导入导出签名 |
| **ar**（`.a` / `.lib`） | 成员列表 + 大小 + 偏移（与 Archive 插件的呈现同构） |

变更逐行标注 `相同 · 变了 · 仅左 · 仅右`，与现有插件视觉语言一致。

## 3. 依赖与许可

**零第三方依赖。**

- **PE 用 .NET 共享框架内置的 `System.Reflection.Metadata`**（命名空间 `System.Reflection.PortableExecutable`，核心类 `PEReader`）。已确认：面向 .NET Core / .NET 5+ 时**无需 `PackageReference`**，该库随共享框架提供。本仓库目标框架是 net10.0，直接可用。
- ELF / Mach-O / WASM / ar **自解析**——这些格式的头与表结构都不复杂，引入第三方解析库反而增加许可与维护面。

## 4. 坑

- **`PEReader` 对不可信输入不安全**。官方文档明确警告：格式不正确或恶意的 PE 文件可能导致越界内存访问、崩溃或挂起，**只应与受信任的程序集一起使用**。而我们的输入正是「仓库里任意二进制」，所以必须：后台线程解析 + 全局 try/catch + 出错降级为「只显示文件头/体积」，绝不让异常冒泡到宿主。语言侧的 ELF / Mach-O / WASM 自解析同理，所有偏移与长度都要做边界校验。
- **`.dll` / `.exe` 不能靠扩展名判断是不是 .NET**：可能是原生，也可能是托管（含单文件 / 自包含发布）。必须靠解析结果分支，两种形态给出不同的对比项。
- **版本化 `.so` 命中不了**：`libfoo.so.1.2.3` 的扩展名是 `.3` 而不是 `.so`，宿主按扩展名路由，插件拿不到。这是**已知限制**，除非宿主侧支持扩展名以外的路由，否则无法在插件内解决。
- Mach-O 的 fat / universal 二进制一文件多架构 → 视图里要能切架构（与 `.ttc` 的多字体同构）。
- `.wasm` 也可用文本格式 `.wat`，但那是文本，**路由不到插件**（`.wat` 不在认领列表里）。

## 5. 打包与 CI

- **无原生件、无第三方件**，`install-plugin-artifacts.sh` 无需改动，也不需要 `third_party/` 条目。
- demo 文件：CI runner 上 `dotnet build` 出一个托管 `.dll`、`cc` 编译一个最小 `.so` 即可；两侧的差异用不同编译选项制造（例如去掉一个导出符号、改一个依赖库）。
- 截图：静态模式在 Xvfb 下无压力。

## 6. 未决项

1. Authenticode / 代码签名是否纳入（需要 `System.Security.Cryptography.Pkcs`，与证书记划共用依赖，见 [certificate-plugin.md](certificate-plugin.md)）。
2. 一期是否四种格式全做，还是先 PE + ELF（覆盖面最大）再补 Mach-O / WASM。
3. 版本化 `.so` 的路由限制是否值得向宿主侧提需求。