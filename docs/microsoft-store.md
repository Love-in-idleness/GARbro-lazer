# Microsoft Store delivery

The registered identity is maintained in `tools/installers/store-identity.json`.
The Publisher hash is checked against the supplied PFN before packaging.

- Name: `Love-in-idleness.GARbro`
- Publisher: `CN=B903DD43-CDD5-4EFE-9B2A-1AF2F3C210A8`
- Publisher display name: `Love-in-idleness`
- PFN: `Love-in-idleness.GARbro_j546ht2g1e7f4`
- Store ID: `9NP39X1TRVNM`

Run `python tools/package-store.py --version 1.0.6` on Windows with
.NET 10 SDK, Python and the Windows SDK installed. The script publishes GUI,
worker and CLI in one `app/` directory with a single shared local runtime, builds x64/ARM64 packages, and creates
an unsigned MSIX bundle and MSIX upload for Partner Center signing. Do not upload
the side-loaded preview package under this identity.

Store versions use `release/store-version.txt` and a zero fourth component
reserved by Store requirements. Increment this independently for every new Store
submission, including repeated application previews; never reuse a submitted
package version.

The `Microsoft Store packages` workflow runs through Release or a manual dispatch, avoiding a second build for the same release push. It prepares upload artifacts. Submission
still requires an authorized Partner Center account, listing screenshots,
privacy-policy URL, age-rating questionnaire and certification answers. The
Store page/deep link should be advertised after the product is live.

The side-loaded preview keeps its previous package family and certificate
instructions. The registered Store app has a different identity and can coexist;
it is not an in-place upgrade of the preview package.

Debug symbols, debugger-only helpers and XML API documentation are omitted. File lists use lossless gzip compression. The complete FFmpeg source is retained once in losslessly recompressed XZ form with checksums. `store-size-win-*.json` reports each architecture’s download and installed sizes separately against the 100 MB target. The multi-architecture upload contains both architectures; each device installs its matching package.

The build-only F# `tools/RuntimeCompactor` removes ReadyToRun native code from selected framework libraries with the pinned .NET 10.0.14 ILLink tool. All their IL members remain rooted; linker optimizations and substitutions are disabled. Original runtime configurations, application/format assemblies and native codecs remain unchanged. Frequently used startup libraries retain ReadyToRun code. Assembly identity, member/parameter metadata and embedded resources are checked before any replacement; mismatches stop packaging. This is not `PublishTrimmed`, and does not turn off reflection, COM or serialization features. Compaction reports accompany the upload artifacts.

Package sizes and validation: [1.0.5 baseline](package-size-1.0.5.md), [1.0.6 runtime compaction](package-size-1.0.6.md).

## 中文

商店注册信息集中保存在 `tools/installers/store-identity.json`。打包前会通过 Publisher 的哈希核对 PFN。
商店包将 GUI、解码工作进程和 CLI 放在同一个 `app/` 目录，共用一份 .NET 运行时、格式数据库和媒体工具。用户无需另行安装运行时；安装后可通过 `garbro` 命令调用 CLI。

1.0.6 继续精简部分运行时库中可由 JIT 重新生成的预编译代码，保留所有成员和资源，以及常用启动库的预编译代码。格式后端、本机编解码器和运行时功能配置保持原样。构建核对成员、参数和内嵌资源后才替换文件；核对不通过则停止打包。每个架构的精简记录随上传文件提供。

在装有 .NET 10 SDK、Python 和 Windows SDK 的 Windows 上运行上述命令，会生成未签名的 x64/ARM64 MSIX bundle 和 MSIX upload，供 Partner Center 签名。
不要把侧载预览包作为该商店身份上传。

商店版本由 `release/store-version.txt` 管理，第四段固定为零。每次新的商店提交都应递增，包括相同应用版本的再次预览提交，不得复用已提交的包版本。

工作流会准备上传资产；实际提交仍需要有权限的 Partner Center 账号、商店截图、隐私政策链接、年龄分级问卷和认证信息。
产品上线后再提供商店页面和深层链接。

侧载预览版保持原有包系列和证书安装方式，商店版使用独立身份，二者可以并存；商店版不会直接覆盖升级旧的侧载预览版。
