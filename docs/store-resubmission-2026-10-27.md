# uwPDF：2026-10-26 认证报告的重新提交材料

> 实现更新：本文保留最初整理的材料。后续已选择移除权限并实现本地草稿恢复；当前状态与填写步骤以 [1.0.13.0 提交说明](store/README.md) 为准，第 4 节的权限复审草稿不用于本次提交。

整理日期：2026-10-27（Asia/Taipei）。产品 ID：`9PP3QW43QJ77`。发布者：`BenLi06`。

本文提供可复制的商店文案和权限复审草稿，尚未写入 Partner Center、发布网站或重新提交应用。下列说明依据当前源码；未完成关闭流程的本次运行测试。

## 1. 日语搜索关键词：10.1.3

从 ja-JP listing 的搜索关键词字段中删除报告点名的整句：

> 検索関連性を高めるための日本語（ja-jp）キーワードの提案です

这是一句写作说明，不是搜索关键词。也应检查其他关键词栏位，移除说明句、编号和与应用无关的词。

以下七项可逐项填入 ja-JP 的关键词字段；不要把本节标题或说明一起复制进去：

```text
PDFビューアー
PDF閲覧
PDF注釈
手書き注釈
文字検索
OCR
ハイライト
```

检查该语言最终保存的关键词总数不超过七项，且没有其他产品名称或价格宣传词。以上关键词对应当前已有的阅读、批注、文字搜索、离线 OCR 和高亮功能。

## 2. 中文产品说明：10.1.4.3

报告只写 Chinese，未指明简体或繁体，因此应检查 Partner Center 中所有已启用的中文 listing。以下两个代码块分别是可直接粘贴到对应 Description 字段的正文。

### 简体中文 Description

```text
uwPDF 是一款轻巧的 Windows PDF 阅读与批注应用，适合阅读文档、查找文字和记录笔记。

打开本地 PDF，通过连续滚动、页面缩略图和缩放浏览文档。选择并复制文字，搜索文档内容，或高亮选中的文字。你还可以使用触控笔或鼠标添加手写笔迹，调整笔迹颜色和粗细，并使用橡皮擦与撤销工具修改批注。

对于扫描件和图片页面，uwPDF 可使用 Windows 离线 OCR 识别文字，供选择和搜索。此功能需要设备安装相应的 Windows OCR 识别语言，识别结果不会作为可搜索文字层写入保存的 PDF。

保存时，手写笔迹与文字高亮会写入 PDF，便于在其他支持相应批注的阅读器中查看。应用还提供本地保存历史的预览与恢复，以及 Windows 系统分享和打印功能。

PDF 渲染、文字提取、OCR 和批注处理均在设备上完成，无需注册应用账户。文件位于云端同步文件夹时，同步由相应服务处理；主动分享和打印时，文件会交给你选择的应用或系统服务。

界面支持简体中文、繁体中文、英语和日语。uwPDF 专注于阅读和批注，不提供 PDF 正文编辑、文档拆分或合并功能。
```

### 繁體中文 Description

```text
uwPDF 是一款輕巧的 Windows PDF 閱讀與註記應用程式，適合閱讀文件、尋找文字和記錄筆記。

開啟本機 PDF，透過連續捲動、頁面縮圖和縮放瀏覽文件。選取並複製文字，搜尋文件內容，或醒目提示選取的文字。你也可以使用觸控筆或滑鼠加入手寫筆跡，調整筆跡顏色與粗細，並使用橡皮擦與復原工具修改註記。

針對掃描文件和圖片頁面，uwPDF 可使用 Windows 離線 OCR 辨識文字，供選取與搜尋。此功能需要裝置安裝對應的 Windows OCR 辨識語言，辨識結果不會作為可搜尋的文字層寫入儲存的 PDF。

儲存時，手寫筆跡與文字醒目提示會寫入 PDF，方便在其他支援對應註記的閱讀器中檢視。應用程式也提供本機儲存歷程的預覽與還原，以及 Windows 系統分享和列印功能。

PDF 繪製、文字擷取、OCR 和註記處理均在裝置上完成，無需註冊應用程式帳號。檔案位於雲端同步資料夾時，同步由對應服務處理；主動分享和列印時，檔案會交給你選擇的應用程式或系統服務。

介面支援繁體中文、簡體中文、英語和日語。uwPDF 專注於閱讀和註記，不提供 PDF 本文編輯、文件分割或合併功能。
```

检查最终 Description 和其他商店文案中没有“以下是……”“可以使用以下描述”等写作说明。不要宣称已经过 Windows 10 Mobile 实机认证，或提供尚不存在的功能。

## 3. 隐私政策和支持联系方式：10.5.1

仓库已有四种语言的政策正文，位于 `website/src/i18n/privacy.json`；站点默认域名配置为 `https://uwpdf.benli06.site/`。

预期的政策地址：

| 语言 | URL |
| --- | --- |
| 简体中文 | https://uwpdf.benli06.site/privacy/ |
| 繁体中文 | https://uwpdf.benli06.site/zh-tw/privacy/ |
| 日语 | https://uwpdf.benli06.site/ja/privacy/ |
| 英语 | https://uwpdf.benli06.site/en/privacy/ |

**这些政策 URL 尚未通过本次可访问性验证。** 2026-10-27 使用网页工具访问失败，使用本机 PowerShell 和 curl 访问时也出现 TLS 握手失败（unexpected EOF / schannel handshake failure）。这不能单独证明所有外部用户均无法访问，但目前不能把这些地址标为可用。

下一步应确认正式域名、部署和 HTTPS 配置，并从无需登录的外部浏览器实际打开政策页，确认页面是 uwPDF 的完整隐私政策，不是首页、404 页面或验证页面。通过后，才将对应正式 URL 填入 Partner Center 的 Properties → Privacy Policy URL 字段；以当前后台实际字段名称为准。仅在官网页脚提供入口不等于已填写该字段。

PDF 可能含有个人信息。政策应如实说明本地文档访问、可编辑笔迹、文字高亮、可能含完整 PDF 的历史快照、临时文件、OCR、磁贴信息、用户发起的分享和打印，以及数据清理与联系方法。不要将“没有自动上传给开发者”写成“不会访问任何个人信息”。现有政策已涵盖上述主要处理场景，仍需与最终提交版本一致。

可填写的支持联系页面（本次网页工具成功读取公开页面）：

```text
https://github.com/A-BenLi06/uwPDF/issues
```

公开项目主页（本次网页工具成功读取）：

```text
https://github.com/A-BenLi06/uwPDF
```

官网地址只有在确认公开可访问后才填写。无需编造支持邮箱；支持字段可以使用支持页面 URL。GitHub Issues 通常公开，提交问题时不应附带私人 PDF 或敏感资料。

## 4. confirmAppClose 复审：10.6.3

这次报告明确表示现有申请被拒绝，并要求复审提供新增或扩展的 business justification，以及有效支持联系方式或开发者网站。仅重新粘贴上次用途说明不足以回应本次报告。

### 保留现有功能时的英文复审草稿

以下代码块可用于 Submission options 中对应权限的 business justification。它包含新增的支持 URL、具体使用场景、用户控制方式、替代方案限制和测试步骤。它是基于源码的说明，不代表本次已完成运行验证，也不保证获批。

```text
Product: uwPDF - Lite PDF Viewer
Product ID: 9PP3QW43QJ77
Publisher: BenLi06
Restricted capability: confirmAppClose

We request reconsideration of the capability decision in the certification report dated October 26, 2026. This expanded justification adds a public support contact, source references, the document-editing use case, and reproduction steps.

Support contact: https://github.com/A-BenLi06/uwPDF/issues
Public project website and source: https://github.com/A-BenLi06/uwPDF

uwPDF is a local PDF reader and annotation editor. Users can create handwritten ink and text highlights that have not yet been saved to the PDF. The confirmAppClose capability is used with SystemNavigationManagerPreview.CloseRequested to protect these user-created changes when the user closes the app window.

If there are no pending annotation changes, the handler allows closing without a save prompt. If changes are pending, the app presents Save, Don't Save, and Cancel. Save completes the annotation save before allowing the close; a failed save keeps the document open and displays an error. Don't Save allows the close without saving the pending edits. Cancel keeps the document open. The close-event deferral is completed in a finally block after the decision and any save operation.

The app provides an explicit Save command. Its save-history feature contains saved versions and is not an automatic recovery mechanism for every unsaved edit. Automatically overwriting the original PDF after each edit would change the user's explicit Save/Don't Save workflow. A suspension callback does not provide the same interactive decision before a user-requested window close. This request is specifically to preserve that document-editing workflow and prevent accidental loss of unsaved annotations.

Reproduction steps for the submitted build on a supported Windows desktop device:
1. Open a writable local PDF in uwPDF.
2. Open the annotation tools and add an ink stroke, or select text and highlight it. Do not save.
3. Close the app using the window close button. The unsaved-changes dialog should appear.
4. Choose Cancel and confirm that the document remains open with the pending annotations.
5. Close again and choose Save. Reopen the PDF and verify that the annotations were saved.
6. Repeat with another unsaved change, close, and choose Don't Save; the app should close without saving that change.
7. Open an unchanged document and close it; no save prompt should appear.

Source references for the current implementation:
https://github.com/A-BenLi06/uwPDF/blob/main/LitePdfViewer/MainPage.xaml.cs
https://github.com/A-BenLi06/uwPDF/blob/main/LitePdfViewer/MainPage.Save.cs

Please reconsider approval of confirmAppClose for this user-controlled PDF annotation save-confirmation scenario. The public support page above is maintained by the publisher for questions about the app.
```

提交前应在实际待提交的包上完成上述测试，尤其确认 Release/.NET Native 中的反射注册能够工作；源码中的反射注册会捕获异常，仅编译通过不能证明对话框会出现。确认公开源码链接与提交实现一致。不要将未验证步骤描述为“已通过”。

### 如果选择移除权限

两份源 manifest 都声明了此权限：`LitePdfViewer/Package.appxmanifest` 和 `LitePdfViewer/Package.ARM64.appxmanifest`。移除权限需要重新生成并检查最终包，而非只修改 Partner Center 的申请说明。

同时需要调整关闭事件注册，并明确解决未保存内容的处理方式。当前 `App.OnSuspending` 只释放退休的渲染资源，不保存未提交笔迹，因此直接删除 capability 会让关闭前的保存保护失效。可另行实现本地草稿恢复，并验证退出、挂起、保存失败与重新打开行为。

本文没有修改权限或应用行为。保留权限的复审也不会解决 14393 的安装兼容性限制；若继续声明支持 14393，需要另行处理包的兼容策略。

## 提交状态

| 项目 | 当前状态 |
| --- | --- |
| 日语关键词替换文案 | 已准备；未保存到 Partner Center |
| 简体、繁体产品说明 | 已准备；未保存到 Partner Center |
| 隐私政策正文 | 本地站点已存在；正式 URL 可访问性尚未确认 |
| 支持联系方式 | 公开 GitHub Issues 页面可读取；未保存到 Partner Center |
| confirmAppClose 扩展复审说明 | 已准备；未提交，实际包关闭流程待验证 |
| 应用源码与安装包 | 本次未修改、未构建、未上传 |

## 参考资料

- [Microsoft Store Policies：10.1.3、10.5、10.6](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies)
- [Privacy policy and support info for MSIX app](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/support-info)
- [Restricted capabilities](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/app-capability-declarations#restricted-capabilities)
