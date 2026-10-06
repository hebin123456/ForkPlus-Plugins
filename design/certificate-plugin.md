# 证书对比插件 · 设计文档

> 状态：设计已定，未开工。
> 日期：2026-10-06。
> 相关：[README.md](../README.md)、[sdk/ForkPlus.Plugins.Abstractions](../sdk/ForkPlus.Plugins.Abstractions)。

## 1. 定位与范围

证书轮换、续期、换 CA 之后，并排看清两版证书到底变了什么——有效期、签发者、SAN 列表、密钥长度、用途，而不是对着 Base64 疙瘩看。

| | |
| --- | --- |
| 插件 Id | `forkplus.certificate` |
| AssemblyName | `ForkPlus.Plugins.Certificate` |
| 扩展名 | `.der` `.cer` `.p12` `.pfx` `.p7b` `.p7c` `.crl` |
| Priority | 100 |
| 默认模式 | 证书详情并排 |
| 其余模式 | 证书链 · 原始字节 |

## 2. 必须写明的路由限制

**`.pem` / 一般 `.crt` 命中不了本插件。** 它们是 **PEM 文本**（Base64 包裹），而宿主**只对二进制差异查询插件路由**——文本差异由内置文本编辑器固定渲染。所以：

- 扩展名里**故意不列** `.pem` / `.crt`，避免造成「声明了但从不生效」的假象；
- 想覆盖它们，需要**宿主侧**支持「用户绑定可强制把文本差异交给插件」，或用户自行用 `.gitattributes` 把该类型标为 `binary`。这是宿主侧的事，插件内无解。
- 稳妥命中的是**二进制**封装：`.der` / 二进制 `.cer` / `.p12` / `.pfx` / `.p7b` / `.crl`。

## 3. 呈现与对比项

| 分组 | 对比项 |
| --- | --- |
| 身份 | subject DN、issuer DN、序列号、自签名与否 |
| 有效期 | `notBefore` / `notAfter`，并**相对当前时间**标出「已过期 / 即将过期 / 有效」 |
| 密钥 | 公钥算法与长度、签名算法（如 SHA-256 with RSA） |
| 用途 | KeyUsage、ExtendedKeyUsage、BasicConstraints（是否 CA / pathLen） |
| 扩展 | SAN（DNS / IP / URI / 邮箱）、CRL 与 OCSP 分发点 |
| 指纹 | SHA-1、SHA-256 |
| 链 | `.p12` / `.p7b` 里的证书数量与顺序，逐张列出 |

有效期与 SAN 是日常最常看的两项，建议放在最上面。

## 4. 安全边界（明确写死）

- **不导入、不导出私钥，不解密任何内容**。`.p12` / `.pfx` 即使输入了密码，也只读**证书链与别名**，不碰私钥材料——避免插件变成密钥提取工具。
- 导入用 `X509KeyStorageFlags.EphemeralKeySet`，避免把密钥写进磁盘 / 钥匙串。
- 解析一律 try/catch：畸形 DER 要降级成「无法解析」，不能让异常冒泡到宿主。

## 5. 依赖与许可

**基本零第三方依赖。**

- `X509Certificate2` / `X509Certificate2Collection`：**共享框架内置**（`System.Security.Cryptography.X509Certificates`）。
- `.p12` / `.pfx` 需要密码 → 视图里给密码输入框。注意 .NET 在 Linux / macOS 上走 OpenSSL 解析 PKCS#12，行为与 Windows 略有差异，要在三平台都实测。
- `.p7b` / `.p7c`（PKCS#7）需要 `SignedCms`，位于 `System.Security.Cryptography.Pkcs`——**它是否在共享框架内需先确认**；若不在，就加一个 MIT 许可的私有依赖，并按仓库流程登记（`third-party.json` + `licenses/`）。这条与 [executable-plugin.md](executable-plugin.md) 里 Authenticode 的需求是同一个依赖，两处共用。

## 6. 打包与 CI

- 无原生件；若 `Pkcs` 确认为独立包，则走一次常规的私有依赖登记流程，`install-plugin-artifacts.sh` 现有逻辑即可覆盖（平铺拷 `*.dll`）。
- demo 文件：CI runner 自带 `openssl`，生成一对自签证书（改 subject / SAN / 有效期 / 密钥长度）即可造出可看的差异。
- 截图：静态模式在 Xvfb 下无压力。

## 7. 未决项

1. `System.Security.Cryptography.Pkcs` 是否在共享框架内（决定要不要引私有依赖）。
2. 是否向宿主侧提「用户绑定可强制把文本差异交给插件」的需求，以覆盖 `.pem` / `.crt`。
3. 是否支持 `.jks`（Java KeyStore，需第三方库，且许可要挑）。
4. `.crl`（吊销列表）是否值得单独做，还是一期只做证书。