# Microsoft Store submission

Product: **9NP39X1TRVNM**. Identity and Publisher are taken from
`tools/installers/store-identity.json`; Store version is `release/store-version.txt`.
Do not replace them with the side-loaded test identity.

1. In Partner Center, open the existing GARbro product and create a submission.
2. Upload `garbro-1.0.0-store.msixupload`. It contains self-contained x64/ARM64 packages;
   it needs no user-installed .NET runtime and Microsoft applies the Store signature.
3. Use the English and Simplified Chinese listings in this folder and the supplied
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

The supplied `.msixbundle` is unsigned for Store signing, not the side-load download.
The repository's side-load MSIX has a separate identity and test certificate.
The package and listing ZIP being generated does **not** mean the Store has accepted
or published the app. The current automation has no Partner Center credentials.

微软商店后台：打开已有产品，上传正式版的 `store.msixupload`，填写本目录中英说明，
添加截图和隐私链接，完成账户要求的定价、地区、分级及法律声明后提交认证。
构建产物可直接用于提交；没有后台访问凭据时，自动化不能代替账户持有人提交。
