# Microsoft Store submission

Product: **9NP39X1TRVNM**. Identity and Publisher are taken from
`tools/installers/store-identity.json`; Store version is `release/store-version.txt`.
Do not replace them with the side-loaded test identity.

1. In Partner Center, open the existing GARbro product and create a submission.
2. Upload `garbro-1.0.3-store.msixupload`. It contains self-contained x64/ARM64 packages;
   it needs no user-installed .NET runtime and Microsoft applies the Store signature.
3. Use the English, Simplified Chinese, Russian and Japanese listings in this folder and the supplied
   1440×810 screenshots. Review the public privacy URL, support URL and license.
4. Explain `runFullTrust`: local file/archive processing, isolated decoder helpers,
   local FFmpeg video processes and the included CLI. Network access is used only
   for explicit update actions and browser links. No services or drivers are installed.
5. Complete the account's distribution, pricing, age-rating and certification declarations
   in Partner Center. These account/legal declarations cannot be inferred by the build.
6. Certification notes: open a folder or a game archive; select media and click Play.
   No login, account or payment is needed. Synthetic sample resources are in the source
   repository's `tests/fixtures/generated` directory. Some game variants require their own
   keys, which are not included. Verify both architectures before submitting.

Only upload `garbro-1.0.3-store.msixupload` (or its `store.msixbundle` counterpart).
It already includes x64 and ARM64. The `win-x64.msix` and `win-arm64.msix` downloads
use the side-load identity and version scheme and must not be submitted to Store.
The Store version is `1.0.3.0`; its reserved fourth component is zero.

`runFullTrust` is required for this desktop application and needs Partner Center
approval. If requested, explain: "GARbro is a desktop resource browser. Full trust
is used to read user-selected local game resources, extract and convert files, and
run local decoder and FFmpeg helper processes. The app runs as the signed-in user
and does not install services or drivers or request administrator elevation."

The supplied `.msixbundle` is unsigned for Store signing, not the side-load download.
The repository's side-load MSIX has a separate identity and test certificate.
The package and listing ZIP being generated does **not** mean the Store has accepted
or published the app. The current automation has no Partner Center credentials.

微软商店后台：打开已有产品，上传正式版的 `store.msixupload`，填写本目录中英俄日说明，
添加截图和隐私链接，完成账户要求的定价、地区、分级及法律声明后提交认证。
构建产物可直接用于提交；没有后台访问凭据时，自动化不能代替账户持有人提交。

只上传 **`garbro-1.0.3-store.msixupload`**，一个文件包含 x64 和 ARM64。
`win-x64.msix`、`win-arm64.msix` 是本地侧载包，不能提交商店。
商店版本为 `1.0.3.0`。`runFullTrust` 仍需按后台提示申报并获批；可说明：
“GARbro 是桌面游戏资源浏览器，需要读取用户选择的本地资源、提取和转换文件，
并启动本地解码器和 FFmpeg 辅助进程。程序以当前用户身份运行，不安装服务或驱动，
不请求管理员提权。”
