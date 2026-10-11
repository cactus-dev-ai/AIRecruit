# 51job - 前程无忧简历下载

Windows Forms 桌面工具。主界面左侧为操作区，右侧为 WebView2 浏览器；应用本身没有用户或权限系统。

## 功能

- HR 在嵌入式浏览器中手动完成账号、密码及验证码登录；应用不读取或保存凭据。
- 自动判断 51job 企业后台的登录状态。
- 从“人才管理 → 待处理”收集当前页面可识别的候选人详情并保存 JSON。
- 职位直接使用 51job 人才搜索页面当前下拉选中的职位，桌面应用不再提供职位输入框。
- 按 `appsettings.json` 中的筛选条件执行搜索，并逐个打开可识别的人才详情保存 JSON。
- 人才搜索默认排除51job中已经查看过的候选人；请求端使用未读筛选，响应端再次跳过 `is_read=1` 的记录。
- 使用本地 SQLite 记录已下载人才 ID，再次搜索时自动跳过，防止重复下载。
- 登录状态只通过 WebView2 Cookie 判断，不再通过页面文字判断；可在配置中指定认证 Cookie 名称。
- 单次目标范围固定为 50–100，默认获取 100 个“新人才”；重复人才不占本次配额。
- 人才搜索按单轮运行：从 SQLite 中保存的职位断点开始，依次处理全部已发布职位；每批最多 100 个新人才，整轮完成后自动停止，不等待且不自动开启下一轮。
- HR 在 51job 人才搜索页完成登录后启动单轮下载；停止任务时保留当前职位断点，下次从该职位继续并由 SQLite 跳过已处理人才。
- 职位数据由 51job 异步加载，程序最多等待约 4 秒并每 300ms 重新读取，避免页面刚打开时误判为未选择职位。
- 开始人才搜索时先展开“更多筛选”，再识别当前职位并按配置顺序应用筛选；只有控件未找到时才再次尝试展开。
- “更多筛选”是启动搜索的必需控件：程序最多等待约 4 秒定位顶部按钮，找不到时停止本次启动，不会提交缺少筛选条件的搜索。
- 无法打开或解析的个别人才直接写入 `discarded_talents`，以后不再尝试。
- 不点击电话、Hi聊等会消耗点数或产生外部操作的功能。
- JSON 保存在程序目录下的 `ResumeTemp` 子目录。
- 保存JSON及上传服务器前都会清洗51job页面壳：删除导航、操作按钮、页尾动态区和连续重复企业水印；当企业名称误占首行、第二行为“某先生/某女士”时，自动删除企业名并修正 `candidate_name`；原始候选人履历章节不删除。
- “上传服务器”扫描各批次中的 `candidate_*.json`，调用通用 API 上传；服务器确认成功或确认重复后清理本地简历 JSON，上传失败的文件保留重试，批次索引不会上传或清理。
- 默认上传地址为 `https://hr.siekon.com/api/third-party-resumes`。
- 下载运行期间上传按钮不可用，必须先点击“停止当前任务”，避免上传和写入同一文件发生冲突。
- SQLite 默认位于 `%LOCALAPPDATA%\51job\51job.db`。

## 登录地址说明

“打开网站”使用 `https://ehire.51job.com/`。登录会话保存在应用自己的 WebView2 用户目录中，账号、密码和验证码仅由 HR 在网页中输入。

网站地址、单批数量和所有人才搜索筛选条件均可在程序目录旁的 `appsettings.json` 中修改。每个筛选项包含：

- `ControlName`：页面筛选控件文字。
- `ControlAliases`：控件标题会随当前值变化时使用的备用名称，例如最近活跃时间的“近1年”和“近1周”。
- `Options`：需要选择的一个或多个选项。
- `UseDialogSearch`：行业等复杂弹层是否先使用弹层搜索框。
- `OpenMoreFilters`：控件是否位于“更多筛选”中。
- `ConfirmButtonText`：选择完成后的确认按钮；无需确认时设为 `null`。

范围筛选按 `Options` 顺序点击。例如期望月薪配置为 `5千`、`5万`，程序先选择左侧下限 `5千`，等待右侧上限出现后再选择 `5万`；此类自动收起的范围控件将 `ConfirmButtonText` 设为 `null`。

当前人才搜索不再依赖筛选控件或 Vue 组件的 DOM 状态。程序通过页面已经加载的 Webpack API 模块 `5377` 主动调用 `getTalentSearchCombineJobList`，以网站要求的 `page_size=20` 获取完整“我的职位”，并只保留 `status=24`、`publish_status=发布中` 的职位；等待阶段明确要求响应来自 `/resume/search/get_combine_joblist`，不会被首屏历史职位接口抢先覆盖。首屏 `/jobj/list/get_topfive_list` 仅作为接口失败后的降级来源，同样会过滤到期下线、暂停、结束等状态。随后程序轮换已发布职位，调用 `talentHuntResumeList`，由 51job 自身补齐动态签名，并提交固定接口筛选：取消期望工作地、求职状态 `1`（离职-周内到岗）、行业 `186,187`（汽车研发/制造、汽车零部件）、活跃时间 `1`（近1周）、低跳槽标记 `1`、未读简历标记 `chk_unread_resume=1`（排除已看）。返回的每页50个人才 ID 直接用于打开详情，不要求右侧列表完成渲染。该行为可通过 `Search.ExcludeViewedCandidates` 配置，默认开启。

人才管理使用接口模式：刷新当前人才管理页面并监听 `/resume/administer/talent_pool_resume` 的50条列表响应，仅保存 `is_read = "0"`（页面NEW标记）的记录。稳定人才ID优先使用 `real_userid`，其次使用 `userid` 和 `cvlogid`，保存前仍通过SQLite去重。该操作不翻页、不滚动，也不解析人才管理列表DOM。

人才去重数据库保存为程序运行目录下的 `51job.db`。如果检测到旧版 `%LocalAppData%\51job\51job.db`，启动时会把其中已下载和已丢弃的人才ID合并到本地数据库，Cookie使用的WebView2目录不会改变。

## 网络监控调试模式

`appsettings.json` 的 `NetworkCapture.Enabled` 默认开启。程序启动 WebView2 后，会监听前程无忧域名下与人才、简历、搜索和职位相关的 JSON 请求与响应，并保存到程序目录的：

`NetworkCapture/yyyyMMdd_HHmmss/`

- `requests.jsonl`：请求方法、脱敏 URL 和请求体。
- `0001_*.json`：每个匹配响应的状态码、内容类型及响应 JSON。
- Cookie、Authorization、token、session、password 等字段会被替换为 `[REDACTED]`。
- URL 查询参数只保留参数名称，不保存值。
- 可通过 `MaximumBodyBytes` 限制单个响应的最大读取量。

使用时由 HR 手动完成一次人才搜索并打开一份简历，再检查该目录即可确定真实接口。确认接口后可将 `Enabled` 改为 `false`。

行业等启用 `UseDialogSearch` 的筛选会先输入选项，再点击搜索框下方出现的同名结果。结果点击限定在搜索框附近，不会误点页面底层的同名行业分类。

所有筛选交互至少等待 `Search.InteractionDelayMilliseconds`，默认 1000ms。行业输入后还会在最多 3 秒内轮询搜索结果，确认结果已经渲染后才点击。

如果行业搜索结果仍未出现，程序会清空搜索框并从行业分类列表中查找同名选项；选项已经出现在“已选”标签中时直接视为成功，不会重复点击取消选择。

`Authentication.RequiredCookieNames` 建议填写实际认证 Cookie 名称。配置为空时程序按 `CookieNameHints` 查找认证特征 Cookie，并且登录页路径始终判定为未登录。Cookie 值不会写入日志、JSON 或 SQLite。

下载采用海选模式：逐页扫描、优先用 51job 人才 ID 全局去重，直到获取配置数量的新人才或达到 `MaxPagesPerRun`。失败记录不会重试。输出 JSON 只保存稳定的人才 ID 和页面路由，不保存带 `requestId`、`rt`、`time` 的临时 URL。

人才管理必须进入候选人详情并载入“应聘者信息”后才保存 JSON。无法确认详情已加载时丢弃该条；程序不再用人才列表页生成 `current_page.json`。

上传地址、API Key 和超时可在 `ServerUpload` 中配置。WEB 接口为 `POST /api/third-party-resumes`，请求体是 `application/json`，请求头携带 `X-API-Key`；服务端按 SHA-256 去重并保存到 `C:\Zhaopin\ResumeTemp\ThirdPartyImport`。

## 运行

```powershell
dotnet run --project C:\Zhaopin\src\Job51.WinForms\Job51.WinForms.csproj
```

运行电脑需要 Microsoft Edge WebView2 Runtime。Windows 10/11 和新版 Edge 通常已经包含它。

## 网站兼容性

2026-08-09 复查页面时确认：职位显示在 `.talent_search_head_left`，行业选择使用“选择行业”对话框，候选人列表并不保证提供普通超链接。因此程序同时支持详情链接和人才卡片点击两种方式。招聘网站属于动态第三方页面，其 DOM、路由和风控策略可能调整；所有页面定位规则集中在 `BrowserScripts.cs`。如果日志显示“未识别到人才卡片”，程序会保存当前页面可见文本作为回退结果，但不会把回退文件写入人才下载历史。
