# uwPDF 1.0.13.0 重新提交

对应产品 `9PP3QW43QJ77`，处理 2026-10-26 认证报告。本目录是人工填写 Partner Center 的材料源，不是 Partner Center 的自动导入格式。

## 应用修改

- 两份 manifest 已删除 `confirmAppClose`，不再拦截系统关闭，也不申请其他 capability。
- 未保存的笔迹和文字高亮写入独立的本地草稿。重新打开未变化的同一 PDF 时，恢复完整草稿并保持待保存状态；原 PDF 只在用户保存或主动分享等原有保存流程中更新。
- 界面显示草稿写入、已保存、恢复、失败及冲突状态。异步写入尚未完成时强制退出，仍可能丢失最新修改，不能宣称每次修改都已落盘。
- 草稿按完整代发布，写入中断不会覆盖上一代；损坏时尝试较早的完整代。删除所有笔迹或高亮也会作为草稿记录。
- 本地可编辑数据改用稳定的路径散列定位，迁移可找到的旧目录；应用保存导致创建时间变化时不会丢失批注关联。文件版本在应用外发生变化时，旧数据归档保留，避免套用到不同内容上。
- 更多菜单新增四种语言的离线隐私政策和支持入口。

这次不再使用旧的权限复审申请草稿；必须上传不声明该权限的新包。包格式、语言和权限检查不等于 Store 已认证通过。

## 商店字段

`listings.json` 包含 `ja-jp`、`zh-cn`、`zh-tw`、`en-us` 的 `description` 和 `keywords`。每种语言均为七个关键词。复制字段内容即可，不要把 JSON 键名、说明文字、代码块标记或旧稿中的提示语粘进商店字段。

1. 替换日语 listing 的关键词，确认报告点名的整句建议文字已删除。
2. 替换简体、繁体 Description，并检查所有中文 listing。正文仅说明实际功能及限制。
3. Properties → Support contact info 填写 `https://github.com/A-BenLi06/uwPDF/issues`。
4. 发布根目录 `PRIVACY.md` 后，可将 `https://github.com/A-BenLi06/uwPDF/blob/main/PRIVACY.md` 填入 Privacy Policy URL。**此地址在文件推送并确认无需登录可打开之前，不算已修复。**
5. 官网的四语言政策也已同步。`https://uwpdf.benli06.site/privacy/` 等地址在本次检查中遇到 TLS 连接错误，尚未确认公开可用；不能仅凭本地构建通过填写它。
6. 上传新版本包，移除提交中已被替换的旧包，检查后台识别出的权限为空，然后提交审核。

公开隐私链接仍是需要完成的发布步骤。应用内政策和本地 Markdown 不会自动更新 Partner Center 字段。本次源代码修改不表示已发布网站、已推送仓库或已重新送审。

## 可复制到 Notes for certification 的说明

```text
Product ID: 9PP3QW43QJ77
Version: 1.0.13.0

The updated packages remove the confirmAppClose restricted capability. The app no longer intercepts system window closing. Unsaved ink and highlights are instead written as separate local drafts; reopening the same unchanged PDF attempts to restore a completed draft as pending edits. Explicit Save writes annotations to the PDF. Draft status and storage errors are shown in the app, and abrupt termination before a write completes can leave the latest changes unsaved.

The app now includes a localized, offline privacy policy under the More menu and a support link to https://github.com/A-BenLi06/uwPDF/issues.
```

填写并保存商店字段、发布隐私政策并验证 URL 后，才在审核说明中另行陈述关键词、中文描述、Privacy Policy URL 和支持字段已更新。不要把未完成的后台操作写成已经完成。

## 维护和验证

`privacy.json` 是政策正文源。修改后运行：

```powershell
python scripts/Sync-StorePrivacy.py
python tests/ValidateLocalization.py
python tests/ValidateStoreSubmission.py
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-DraftRecovery.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-StoreSubmission.ps1
```

同步脚本更新四语言 RESW、根目录 `PRIVACY.md` 和存在的官网政策 JSON。草稿 QA 使用独立测试 PDF、真实 UWP 存储与 Windows Ink，并在测试进程退出后启动新进程读取；结束后恢复生产源码并重新构建。

Store 构建脚本将结果写入 `artifacts/store-submission/1.0.13.0/`，包括四架构上传容器和哈希验证记录。供提交选择的四个上传文件集中在其中的 `PartnerCenter/`，旁边的 `SHA256SUMS.txt` 用于核对文件；只选择上传容器，不选择校验文本。`.appxupload` / `.msixupload` 只保留在本地用于 Partner Center，不可上传 GitHub Releases。这些 Store 身份的未签名包也不是侧载安装程序。

ARM/ARM64、旧 Windows 和 Windows 10 Mobile 的实机运行不在本次 x64 桌面 QA 的验证范围内。
