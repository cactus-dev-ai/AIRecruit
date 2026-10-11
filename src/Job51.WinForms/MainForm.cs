using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Job51.WinForms;

internal sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly JsonExportService _exportService = new();
    private readonly DownloadHistoryRepository _downloadHistory = new();
    private readonly DiagnosticLogService _diagnostics = new();
    private readonly WebView2 _webView = new();
    private readonly WebView2 _detailWebView = new();
    private NetworkCaptureService? _networkCapture;
    private int _talentJobCount = 1;
    private IReadOnlyList<TalentJobApiData> _publishedTalentJobs = [];
    private TalentJobApiData? _activeTalentJob;
    private int _activeTalentJobIndex = -1;
    private int _activeTalentPageIndex = 1;
    private TalentSearchApiSnapshot? _activeTalentSearchSnapshot;
    private readonly Label _statusLabel = new();
    private readonly Label _addressLabel = new();
    private readonly Label _filterSummaryLabel = new();
    private readonly NumericUpDown _limitInput = new();
    private readonly TextBox _logTextBox = new();
    private readonly Button _openWebsiteButton = new();
    private readonly Button _managementButton = new();
    private readonly Button _searchButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _uploadButton = new();
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 3000 };
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private CancellationTokenSource? _operationCancellation;
    private bool _browserReady;
    private bool _detailBrowserReady;
    private bool _busy;

    public MainForm()
    {
        _settings = LoadSettings();
        Text = "51job - 前程无忧简历下载";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 760);
        Size = new Size(1440, 900);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(245, 247, 250);

        BuildLayout();
        Shown += MainForm_Shown;
        FormClosing += MainForm_FormClosing;
        _statusTimer.Tick += StatusTimer_Tick;
    }

    private void BuildLayout()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            FixedPanel = FixedPanel.Panel1,
            BackColor = Color.FromArgb(224, 228, 235)
        };
        Controls.Add(split);
        split.Panel1MinSize = 340;
        split.Panel2MinSize = 600;
        split.SplitterDistance = Math.Clamp(ClientSize.Width / 3, 340, ClientSize.Width - 606);

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            ColumnCount = 1,
            RowCount = 13,
            BackColor = Color.White
        };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        left.RowStyles[7] = new RowStyle(SizeType.Absolute, 0);
        split.Panel1.Controls.Add(left);

        left.Controls.Add(new Label
        {
            Text = "前程无忧简历下载",
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 41, 59),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        var statusPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 250, 252) };
        _statusLabel.Text = "● 浏览器初始化中";
        _statusLabel.ForeColor = Color.FromArgb(100, 116, 139);
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Padding = new Padding(12, 0, 0, 0);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusPanel.Controls.Add(_statusLabel);
        left.Controls.Add(statusPanel, 0, 1);

        ConfigureButton(_openWebsiteButton, "打开网站", Color.FromArgb(37, 99, 235));
        _openWebsiteButton.Click += async (_, _) => await NavigateAsync(_settings.LoginUrl, CancellationToken.None);
        left.Controls.Add(_openWebsiteButton, 0, 2);

        _addressLabel.Text = "登录入口：https://ehire.51job.com/；账号、密码和验证码均由 HR 在右侧手动输入。";
        _addressLabel.Dock = DockStyle.Fill;
        _addressLabel.ForeColor = Color.FromArgb(180, 83, 9);
        _addressLabel.Font = new Font(Font, FontStyle.Italic);
        left.Controls.Add(_addressLabel, 0, 3);

        ConfigureButton(_managementButton, "人才管理 · 下载当前页NEW", Color.FromArgb(15, 118, 110));
        _managementButton.Click += async (_, _) => await RunOperationAsync(DownloadPendingCandidatesAsync);

        left.Controls.Add(new Label
        {
            Text = "人才搜索配置",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = Color.FromArgb(71, 85, 105)
        }, 0, 5);

        _filterSummaryLabel.Text = "职位使用 51job 页面当前下拉选择；筛选条件读取 appsettings.json。";
        _filterSummaryLabel.Dock = DockStyle.Fill;
        _filterSummaryLabel.ForeColor = Color.FromArgb(71, 85, 105);
        _filterSummaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        left.Controls.Add(_filterSummaryLabel, 0, 6);

        ConfigureButton(_searchButton, "人才搜索 · 单轮下载", Color.FromArgb(124, 58, 237));
        _searchButton.Click += async (_, _) => await RunOperationAsync(SearchAndDownloadAsync);
        left.Controls.Add(_searchButton, 0, 4);

        var limitPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        limitPanel.Controls.Add(new Label { Text = "每批下载数量：", AutoSize = true, Margin = new Padding(0, 10, 8, 0) });
        _limitInput.Minimum = Math.Clamp(_settings.Download.MinimumPerRun, 50, 100);
        _limitInput.Maximum = Math.Clamp(_settings.Download.MaximumPerRun, (int)_limitInput.Minimum, 100);
        _limitInput.Value = Math.Clamp(_settings.Download.DefaultPerRun, (int)_limitInput.Minimum, (int)_limitInput.Maximum);
        _limitInput.Width = 80;
        _limitInput.Margin = new Padding(0, 6, 0, 0);
        limitPanel.Controls.Add(_limitInput);
        left.Controls.Add(limitPanel, 0, 8);

        ConfigureButton(_stopButton, "停止当前任务", Color.FromArgb(220, 38, 38));
        _stopButton.Enabled = false;
        _stopButton.Click += (_, _) => _operationCancellation?.Cancel();
        left.Controls.Add(_stopButton, 0, 9);

        left.Controls.Add(new Label
        {
            Text = "运行记录",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = Color.FromArgb(71, 85, 105)
        }, 0, 10);

        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Multiline = true;
        _logTextBox.ReadOnly = true;
        _logTextBox.ScrollBars = ScrollBars.Vertical;
        _logTextBox.BackColor = Color.FromArgb(248, 250, 252);
        _logTextBox.BorderStyle = BorderStyle.FixedSingle;
        left.Controls.Add(_logTextBox, 0, 11);

        _uploadButton.Text = "上传服务器";
        _uploadButton.Dock = DockStyle.Fill;
        _uploadButton.FlatStyle = FlatStyle.Flat;
        _uploadButton.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        _uploadButton.Click += async (_, _) => await RunOperationAsync(UploadJsonResumesAsync, requireBrowser: false);
        left.Controls.Add(_uploadButton, 0, 12);

        _webView.Dock = DockStyle.Fill;
        _webView.BackColor = Color.White;
        split.Panel2.Controls.Add(_webView);

        // Keep a live, shared-session WebView2 controller off-screen. It loads resume
        // details without moving the HR-visible search page in the right panel.
        _detailWebView.Size = new Size(2, 2);
        _detailWebView.Location = new Point(-10, -10);
        _detailWebView.TabStop = false;
        split.Panel2.Controls.Add(_detailWebView);
        _detailWebView.SendToBack();
    }

    private static void ConfigureButton(Button button, string text, Color color)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 6, 0, 6);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        try
        {
            _diagnostics.Write("application_started", new
            {
                filter_delay_ms = _settings.Search.InteractionDelayMilliseconds,
                batch_size = (int)_limitInput.Value
            });
            AppendLog($"诊断日志：{_diagnostics.CurrentLogPath}");
            await _downloadHistory.InitializeAsync();
            AppendLog($"下载记录数据库：{_downloadHistory.DatabasePath}");
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "51job",
                "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await _webView.EnsureCoreWebView2Async(environment);
            await _detailWebView.EnsureCoreWebView2Async(environment);
            _detailWebView.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (!string.IsNullOrWhiteSpace(args.Uri))
                    _detailWebView.CoreWebView2.Navigate(args.Uri);
            };
            _detailBrowserReady = true;
            _detailWebView.CoreWebView2.Navigate("about:blank");
            if (_settings.NetworkCapture.Enabled)
            {
                _networkCapture = new NetworkCaptureService(
                    _settings.NetworkCapture,
                    _diagnostics,
                    _settings.Search.ExcludeViewedCandidates);
                await _networkCapture.StartAsync(_webView.CoreWebView2);
                AppendLog($"网络监控已启用：{_networkCapture.CaptureDirectory}");
                AppendLog("请手动完成一次人才搜索并打开一份简历，程序将保存脱敏后的接口诊断 JSON。");
            }
            _webView.CoreWebView2.NavigationStarting += (_, args) =>
            {
                SetLoginStatus(false, "登录中 / 页面加载中");
                AppendLog($"打开：{args.Uri}");
            };
            _webView.CoreWebView2.NavigationCompleted += async (_, args) =>
            {
                _diagnostics.Write("navigation_completed", new
                {
                    success = args.IsSuccess,
                    route = _webView.Source?.AbsolutePath ?? string.Empty
                });
                AppendLog(args.IsSuccess ? "页面加载完成" : $"页面加载失败：{args.WebErrorStatus}");
                await RefreshLoginStatusAsync();
            };
            _webView.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (!string.IsNullOrWhiteSpace(args.Uri))
                    _webView.CoreWebView2.Navigate(args.Uri);
            };
            _browserReady = true;
            _statusTimer.Start();
            ShowWelcomePage();
        }
        catch (Exception ex)
        {
            _diagnostics.Write("browser_initialization_failed", new { error_type = ex.GetType().Name });
            SetLoginStatus(false, "浏览器初始化失败");
            AppendLog($"WebView2 初始化失败：{ex.Message}");
            MessageBox.Show(this, "无法初始化嵌入式浏览器。请确认已安装 Microsoft Edge WebView2 Runtime。\n\n" + ex.Message,
                "51job", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowWelcomePage()
    {
        _webView.NavigateToString("""
            <!doctype html><html lang="zh-CN"><head><meta charset="utf-8">
            <style>body{font-family:'Microsoft YaHei UI',sans-serif;background:#f8fafc;color:#334155;display:grid;place-items:center;height:100vh;margin:0}.card{background:#fff;padding:48px;border-radius:16px;box-shadow:0 12px 40px #0f172a18;max-width:620px}h1{color:#1e293b}li{margin:12px 0;line-height:1.6}</style></head>
            <body><div class="card"><h1>前程无忧简历下载</h1><ol><li>点击左侧“打开网站”，由 HR 手动输入账号、密码和验证码。</li><li>在 51job 人才搜索页使用网站自带下拉框选择职位。</li><li>应用读取 appsettings.json 中的筛选条件，搜索后逐个保存尚未下载的人才。</li></ol><p>人才 ID 记录在本地 SQLite 中；应用不会保存登录凭据，也不会自动发起聊天或电话沟通。</p></div></body></html>
            """);
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation, bool requireBrowser = true)
    {
        if (_busy) return;
        if (requireBrowser && !_browserReady)
        {
            MessageBox.Show(this, "浏览器尚未准备好。", "51job", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _busy = true;
        _operationCancellation = new CancellationTokenSource();
        SetOperationButtons(false);
        try
        {
            await operation(_operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            _diagnostics.Write("operation_stopped_by_user");
            AppendLog("任务已停止。");
        }
        catch (Exception ex)
        {
            _diagnostics.Write("operation_failed", new { error_type = ex.GetType().Name, safe_message = SanitizeDiagnosticMessage(ex.Message) });
            AppendLog("任务失败：" + ex.Message);
            MessageBox.Show(this, ex.Message, "任务失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _operationCancellation.Dispose();
            _operationCancellation = null;
            _busy = false;
            SetOperationButtons(true);
            await RefreshLoginStatusAsync();
        }
    }

    private async Task DownloadPendingCandidatesAsync(CancellationToken cancellationToken)
    {
        if (_networkCapture is null)
            throw new InvalidOperationException("人才管理接口模式需要启用 NetworkCapture。");

        await Ensure51JobPageAsync(cancellationToken);
        await Require51JobLoginAsync(cancellationToken);
        var capturedAfter = DateTimeOffset.Now;
        var currentPath = _webView.Source?.AbsolutePath ?? string.Empty;
        var targetUrl = currentPath.Contains("/Revision/talent/management", StringComparison.OrdinalIgnoreCase)
            ? _webView.Source!.ToString()
            : _settings.TalentManagementUrl;
        AppendLog("正在刷新人才管理当前页面，等待列表接口返回50条数据…");
        await NavigateAsync(targetUrl, cancellationToken);

        var snapshot = await _networkCapture.WaitForManagementSnapshotAsync(
            capturedAfter, TimeSpan.FromSeconds(15), cancellationToken);
        if (snapshot is null)
            throw new InvalidOperationException("15秒内未捕获人才管理列表接口，请确认当前页面已显示人才列表后重试。");

        var folder = _exportService.CreateBatchDirectory("人才管理_当前页NEW");
        var files = new List<string>();
        var skipped = 0;
        AppendLog($"人才管理接口返回 {snapshot.CurrentPageCount} 条，识别到 NEW {snapshot.NewResumes.Count} 条。");
        foreach (var candidate in snapshot.NewResumes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _downloadHistory.IsHandledAsync(candidate.TalentId, cancellationToken))
            {
                skipped++;
                AppendLog($"跳过已下载 NEW 人才 ID：{candidate.TalentId}");
                continue;
            }

            var path = await _exportService.SaveManagementApiResumeAsync(
                folder, candidate, files.Count + 1, cancellationToken);
            files.Add(Path.GetFileName(path));
            await _downloadHistory.RecordAsync(new DownloadRecord
            {
                TalentId = candidate.TalentId,
                SourceRoute = candidate.SourceRoute,
                CandidateName = candidate.CandidateName,
                JsonPath = path,
                BatchType = "人才管理_当前页NEW",
                DownloadedAt = candidate.DownloadedAt
            }, cancellationToken);
        }

        var index = new BatchIndex
        {
            BatchType = "人才管理_当前页NEW",
            SourceRoute = "/resume/administer/talent_pool_resume",
            DatabasePath = _downloadHistory.DatabasePath,
            DiscoveredCount = snapshot.CurrentPageCount,
            DownloadedCount = files.Count,
            SkippedDuplicateCount = skipped,
            DiscardedCount = 0,
            Files = files,
            Notes = [$"当前页NEW数量：{snapshot.NewResumes.Count}；仅保存 is_read=0 的接口记录。"]
        };
        await _exportService.SaveIndexAsync(folder, index, cancellationToken);
        _diagnostics.Write("management_api_download_finished", new
        {
            current_page_count = snapshot.CurrentPageCount,
            new_count = snapshot.NewResumes.Count,
            downloaded = files.Count,
            skipped
        });
        AppendLog($"人才管理接口下载完成：NEW {snapshot.NewResumes.Count}，新下载 {files.Count}，重复跳过 {skipped}。JSON：{folder}");
        MessageBox.Show(this,
            $"人才管理当前页处理完成。\n\n当前页：{snapshot.CurrentPageCount}\nNEW：{snapshot.NewResumes.Count}\n新下载：{files.Count}\n重复跳过：{skipped}",
            "51job", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task SearchAndDownloadAsync(CancellationToken cancellationToken)
    {
        SearchExecution? search = null;
        var batchNumber = 0;
        var completedJobs = 0;
        var jobsInThisRound = 0;
        while (jobsInThisRound == 0 || completedJobs < jobsInThisRound)
        {
            cancellationToken.ThrowIfCancellationRequested();
            search ??= await PrepareTalentSearchAsync(cancellationToken);
            if (jobsInThisRound == 0)
            {
                jobsInThisRound = Math.Max(1, _talentJobCount);
                AppendLog($"开始单轮下载：本轮共 {jobsInThisRound} 个已发布职位，完成后自动停止。");
            }

            batchNumber++;
            AppendLog($"开始本轮第 {batchNumber} 批，本批目标 {(int)_limitInput.Value} 个新人才。");
            var result = await DownloadCandidatesFromCurrentPageAsync("人才搜索", search, cancellationToken);
            AppendLog($"第 {batchNumber} 批结束：新下载 {result.DownloadedCount}，跳过 {result.SkippedDuplicateCount}，丢弃 {result.DiscardedCount}。");

            if (result.CanContinueImmediately)
                continue;

            await AdvanceTalentSearchProgressAsync(cancellationToken);
            completedJobs++;
            search = null;
            AppendLog($"当前职位处理结束，本轮进度 {completedJobs}/{jobsInThisRound}。");
        }

        AppendLog($"人才搜索单轮下载完成：已处理 {completedJobs} 个职位。任务停止，不等待、不自动开始下一轮。");
    }

    private async Task<SearchExecution> PrepareTalentSearchAsync(CancellationToken cancellationToken)
    {
        var currentHost = _webView.Source?.Host ?? string.Empty;
        var currentPath = _webView.Source?.AbsolutePath ?? string.Empty;
        var alreadyOnTalentSearch = currentHost.EndsWith("51job.com", StringComparison.OrdinalIgnoreCase) &&
                                    currentPath.Contains("/Revision/talent/search", StringComparison.OrdinalIgnoreCase);
        var navigationStartedAt = DateTimeOffset.Now;
        if (!alreadyOnTalentSearch)
            await NavigateAsync(_settings.TalentSearchUrl, cancellationToken);
        await Require51JobLoginAsync(cancellationToken);

        if (_networkCapture is null)
            throw new InvalidOperationException("接口模式需要启用 NetworkCapture。");

        AppendLog("进入人才搜索页，主动请求完整“我的职位”列表；只接受 status=24 且状态为“发布中”的职位。");
        var fullJobListRequestedAt = DateTimeOffset.Now;
        ActionResult? loadJobs = null;
        var jobApiDeadline = DateTimeOffset.Now.AddSeconds(10);
        do
        {
            loadJobs = await ExecuteJsonAsync<ActionResult>(
                BrowserScripts.LoadPublishedTalentJobsViaPageApi, cancellationToken);
            if (loadJobs?.Ok == true) break;
            await Task.Delay(300, cancellationToken);
        } while (DateTimeOffset.Now < jobApiDeadline);

        var jobsSnapshot = loadJobs?.Ok == true
            ? await _networkCapture.WaitForTalentJobSnapshotAsync(
                fullJobListRequestedAt,
                TimeSpan.FromSeconds(15),
                cancellationToken,
                "/resume/search/get_combine_joblist")
            : null;
        jobsSnapshot ??= await _networkCapture.WaitForTalentJobSnapshotAsync(
            alreadyOnTalentSearch ? DateTimeOffset.MinValue : navigationStartedAt,
            TimeSpan.FromSeconds(3),
            cancellationToken);
        if (jobsSnapshot is null && alreadyOnTalentSearch)
        {
            AppendLog("尚未收到职位列表接口，刷新人才搜索页后继续等待。");
            navigationStartedAt = DateTimeOffset.Now;
            await NavigateAsync(_settings.TalentSearchUrl, cancellationToken);
            await Require51JobLoginAsync(cancellationToken);
            jobsSnapshot = await _networkCapture.WaitForTalentJobSnapshotAsync(
                navigationStartedAt,
                TimeSpan.FromSeconds(15),
                cancellationToken);
        }
        if (jobsSnapshot is null || jobsSnapshot.Jobs.Count == 0)
            throw new InvalidOperationException("15秒内未收到职位列表接口数据，请确认账号至少有一个可用职位。");

        _publishedTalentJobs = jobsSnapshot.Jobs;
        _talentJobCount = _publishedTalentJobs.Count;
        var progress = await _downloadHistory.GetTalentSearchProgressAsync(cancellationToken);
        _activeTalentJobIndex = progress is null
            ? 0
            : FindTalentJobIndex(_publishedTalentJobs, progress.ResumeJobId);
        if (_activeTalentJobIndex < 0) _activeTalentJobIndex = 0;
        var job = _publishedTalentJobs[_activeTalentJobIndex];
        _activeTalentJob = job;
        _activeTalentPageIndex = 1;
        await _downloadHistory.SaveTalentSearchProgressAsync(job.JobId, job.JobName, cancellationToken);
        if (progress is not null && string.Equals(progress.ResumeJobId, job.JobId, StringComparison.OrdinalIgnoreCase))
            AppendLog($"继续上次职位：{job.JobName}（ID {job.JobId}）；重新从第1页请求，已下载人才由SQLite跳过。");
        AppendLog($"已发布职位接口加载 {_talentJobCount} 个职位，本轮选择：{job.JobName}（ID {job.JobId}）。");
        AppendLog($"通过网页内部服务提交接口筛选：取消期望工作地；求职状态=离职-周内到岗；从事行业=汽车研发/制造、汽车零部件；最近活跃=近1周；5年内跳槽次数<3次；{(_settings.Search.ExcludeViewedCandidates ? "排除已看" : "包含已看")}。");

        var searchStartedAt = DateTimeOffset.Now;
        ActionResult? apiStart = null;
        var componentDeadline = DateTimeOffset.Now.AddSeconds(15);
        do
        {
            searchStartedAt = DateTimeOffset.Now;
            apiStart = await ExecuteJsonAsync<ActionResult>(
                BrowserScripts.StartTalentSearchViaPageApi(
                    job,
                    excludeViewedCandidates: _settings.Search.ExcludeViewedCandidates), cancellationToken);
            if (apiStart?.Ok == true) break;
            await Task.Delay(300, cancellationToken);
        } while (DateTimeOffset.Now < componentDeadline);
        if (apiStart?.Ok != true)
            throw new InvalidOperationException(apiStart?.Message ?? "人才搜索接口组件在15秒内未加载。");

        var apiResult = await _networkCapture.WaitForTalentSearchSnapshotAsync(
            searchStartedAt,
            TimeSpan.FromSeconds(20),
            cancellationToken);
        if (apiResult is null)
            throw new InvalidOperationException("人才搜索已提交，但20秒内没有收到结果接口；请检查登录状态或网站提示。");
        _activeTalentSearchSnapshot = apiResult;
        AppendLog($"人才搜索接口返回：当前批 {apiResult.CurrentPageCount} 条，总计约 {apiResult.TotalCount} 条。页面筛选控件的显示状态不参与判断。");

        var search = new SearchExecution
        {
            ExternalJobId = job.JobId,
            SelectedPosition = job.JobName,
            KeywordExpression = string.Empty,
            ConfiguredFilters = _settings.Search.Filters
        };
        return search;
    }

    private static int FindTalentJobIndex(IReadOnlyList<TalentJobApiData> jobs, string jobId)
    {
        for (var index = 0; index < jobs.Count; index++)
        {
            if (string.Equals(jobs[index].JobId, jobId, StringComparison.OrdinalIgnoreCase))
                return index;
        }
        return -1;
    }

    private async Task AdvanceTalentSearchProgressAsync(CancellationToken cancellationToken)
    {
        if (_publishedTalentJobs.Count == 0 || _activeTalentJobIndex < 0) return;
        var nextIndex = (_activeTalentJobIndex + 1) % _publishedTalentJobs.Count;
        var next = _publishedTalentJobs[nextIndex];
        await _downloadHistory.SaveTalentSearchProgressAsync(next.JobId, next.JobName, cancellationToken);
        AppendLog($"职位断点已推进到：{next.JobName}（ID {next.JobId}）。");
        _activeTalentJob = null;
        _activeTalentSearchSnapshot = null;
        _activeTalentJobIndex = nextIndex;
        _activeTalentPageIndex = 1;
    }

    private async Task<TalentSearchApiSnapshot> RequestTalentSearchPageAsync(
        TalentJobApiData job,
        int pageIndex,
        string requestId,
        CancellationToken cancellationToken)
    {
        if (_networkCapture is null)
            throw new InvalidOperationException("接口模式需要启用NetworkCapture。");

        var requestedAt = DateTimeOffset.Now;
        var apiStart = await ExecuteJsonAsync<ActionResult>(
            BrowserScripts.StartTalentSearchViaPageApi(
                job,
                pageIndex,
                requestId,
                _settings.Search.ExcludeViewedCandidates), cancellationToken);
        if (apiStart?.Ok != true)
            throw new InvalidOperationException(apiStart?.Message ?? "人才搜索接口组件尚未加载。");

        var result = await _networkCapture.WaitForTalentSearchSnapshotAsync(
            requestedAt, TimeSpan.FromSeconds(20), cancellationToken);
        if (result is null)
            throw new InvalidOperationException($"人才搜索第{pageIndex}页已提交，但20秒内没有收到结果接口。");
        _activeTalentSearchSnapshot = result;
        _activeTalentPageIndex = pageIndex;
        return result;
    }

    private async Task<SearchPageContext> WaitForSelectedPositionAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(4);
        var context = new SearchPageContext();
        var attempts = 0;
        do
        {
            attempts++;
            cancellationToken.ThrowIfCancellationRequested();
            context = await ExecuteJsonAsync<SearchPageContext>(BrowserScripts.GetSearchPageContext, cancellationToken)
                      ?? new SearchPageContext();
            if (!string.IsNullOrWhiteSpace(context.SelectedPosition))
            {
                _diagnostics.Write("position_detected", new { attempts, detection_source = context.DetectionSource });
                return context;
            }
            await Task.Delay(300, cancellationToken);
        } while (DateTime.UtcNow < deadline);

        _diagnostics.Write("position_detection_timeout", new { attempts });
        return context;
    }

    private async Task<bool> SelectFirstAvailablePositionWithWaitAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(6);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ExecuteJsonAsync<ActionResult>(BrowserScripts.SelectFirstAvailablePosition, cancellationToken);
            if (result?.Ok == true && result.Message.StartsWith("已选择", StringComparison.Ordinal))
            {
                AppendLog(result.Message);
                return true;
            }
            await Task.Delay(600, cancellationToken);
        } while (DateTime.UtcNow < deadline);
        return false;
    }

    private async Task<bool> ClickMoreFiltersWithWaitAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(4);
        var attempts = 0;
        do
        {
            attempts++;
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ExecuteJsonAsync<ActionResult>(BrowserScripts.ClickMoreFilters, cancellationToken);
            if (result?.Ok == true)
            {
                AppendLog(result.Message);
                _diagnostics.Write("more_filters_clicked", new { attempts });
                return true;
            }
            await Task.Delay(300, cancellationToken);
        } while (DateTime.UtcNow < deadline);
        _diagnostics.Write("more_filters_not_found", new { attempts });
        return false;
    }

    private async Task ApplyConfiguredFilterAsync(SearchFilterSetting filter, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filter.ControlName) || filter.Options.Length == 0) return;
        var interactionDelay = Math.Clamp(_settings.Search.InteractionDelayMilliseconds, 1000, 5000);
        _diagnostics.Write("filter_started", new
        {
            control = filter.ControlName,
            option_count = filter.Options.Length,
            dialog_search = filter.UseDialogSearch,
            delay_ms = interactionDelay
        });
        AppendLog($"应用筛选：{filter.ControlName} = {string.Join("、", filter.Options)}");
        var controlNames = new[] { filter.ControlName }
            .Concat(filter.ControlAliases)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var opened = false;
        foreach (var controlName in controlNames)
        {
            opened = await ExecuteActionAsync(BrowserScripts.OpenFilter(controlName), cancellationToken);
            if (opened) break;
        }
        if (!opened && filter.OpenMoreFilters)
        {
            if (!await ClickMoreFiltersWithWaitAsync(cancellationToken))
                throw new InvalidOperationException("需要使用更多筛选，但未找到顶部“更多筛选”按钮。");
            await Task.Delay(interactionDelay, cancellationToken);
            foreach (var controlName in controlNames)
            {
                opened = await ExecuteActionAsync(BrowserScripts.OpenFilter(controlName), cancellationToken);
                if (opened) break;
            }
        }
        if (!opened)
            throw new InvalidOperationException($"无法打开筛选控件“{filter.ControlName}”，请检查 appsettings.json 或网站页面结构。");
        _diagnostics.Write("filter_opened", new { control = filter.ControlName });

        await Task.Delay(interactionDelay, cancellationToken);
        foreach (var option in filter.Options)
        {
            if (filter.UseDialogSearch)
            {
                await ExecuteActionAsync(BrowserScripts.SetDialogSearch(option), cancellationToken);
                _diagnostics.Write("dialog_search_entered", new { control = filter.ControlName, option });
                await Task.Delay(interactionDelay, cancellationToken);
                if (!await ClickDialogSearchResultWithWaitAsync(option, cancellationToken))
                {
                    _diagnostics.Write("dialog_search_fallback_started", new { control = filter.ControlName, option });
                    await ExecuteActionAsync(BrowserScripts.SetDialogSearch(string.Empty), cancellationToken);
                    await Task.Delay(interactionDelay, cancellationToken);
                    if (!await ExecuteActionAsync(BrowserScripts.SelectDialogOptionFallback(option), cancellationToken))
                        throw new InvalidOperationException($"筛选“{filter.ControlName}”的搜索结果和行业分类列表中均未找到选项“{option}”。");
                    _diagnostics.Write("dialog_search_fallback_succeeded", new { control = filter.ControlName, option });
                }
                await Task.Delay(interactionDelay, cancellationToken);
                _diagnostics.Write("dialog_search_result_selected", new { control = filter.ControlName, option });
                continue;
            }

            var optionSelected = await ExecuteActionAsync(BrowserScripts.SelectFilterOption(option), cancellationToken);
            if (!optionSelected)
                optionSelected = await ClickTextAsync(option, false, cancellationToken);
            if (!optionSelected)
                throw new InvalidOperationException($"筛选“{filter.ControlName}”中未找到选项“{option}”。");
            await Task.Delay(interactionDelay, cancellationToken);
            _diagnostics.Write("filter_option_selected", new { control = filter.ControlName, option });
        }

        if (!string.IsNullOrWhiteSpace(filter.ConfirmButtonText))
            await ClickTextAsync(filter.ConfirmButtonText, false, cancellationToken);
        await Task.Delay(interactionDelay, cancellationToken);
        _diagnostics.Write("filter_finished", new { control = filter.ControlName });
    }

    private async Task<bool> ClickDialogSearchResultWithWaitAsync(string option, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        var attempts = 0;
        do
        {
            attempts++;
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ExecuteJsonAsync<ActionResult>(BrowserScripts.ClickDialogSearchResult(option), cancellationToken);
            if (result?.Ok == true)
            {
                AppendLog(result.Message);
                _diagnostics.Write("dialog_search_result_clicked", new { option, attempts });
                return true;
            }
            await Task.Delay(250, cancellationToken);
        } while (DateTime.UtcNow < deadline);
        _diagnostics.Write("dialog_search_result_timeout", new { option, attempts });
        return false;
    }

    private async Task<BatchDownloadResult> DownloadCandidatesFromCurrentPageAsync(
        string batchType,
        SearchExecution? search,
        CancellationToken cancellationToken)
    {
        var sourceUrl = _webView.Source?.ToString() ?? string.Empty;
        var sourceRoute = Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri) ? sourceUri.AbsolutePath : string.Empty;
        var requestedNewCount = (int)_limitInput.Value;
        var folder = _exportService.CreateBatchDirectory(batchType);
        var files = new List<string>();
        var notes = new List<string>();
        var seenThisRun = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedDuplicates = 0;
        var discardedCount = 0;
        var discoveredCount = 0;
        var pagesVisited = 0;
        var canContinueImmediately = false;

        while (files.Count < requestedNewCount && pagesVisited < _settings.Download.MaxPagesPerRun)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pagesVisited++;
            var usingApiTargets = batchType.Equals("人才搜索", StringComparison.OrdinalIgnoreCase) &&
                                  _activeTalentSearchSnapshot is not null &&
                                  _activeTalentJob is not null;
            var apiPage = usingApiTargets ? _activeTalentSearchSnapshot : null;
            var targets = usingApiTargets
                ? CreateTalentSearchApiTargets(apiPage!)
                : await ExecuteJsonAsync<List<CandidateTarget>>(
                    BrowserScripts.DiscoverCandidateTargets, cancellationToken) ?? [];
            targets = targets
                .Where(target => !string.IsNullOrWhiteSpace(target.TalentId) && seenThisRun.Add(target.TalentId))
                .GroupBy(target => target.TalentId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            discoveredCount += targets.Count;
            AppendLog($"第 {pagesVisited} 个加载段发现 {targets.Count} 个新人才，网页每次追加约 {_settings.Download.LoadMoreBatchSize} 条，已获取 {files.Count}/{requestedNewCount}。");

            if (targets.Count == 0 && pagesVisited == 1)
            {
                notes.Add("未识别到人才卡片；未保存列表页 JSON。");
                AppendLog("未识别到人才卡片，不保存人才列表页 JSON。");
                break;
            }

            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (files.Count >= requestedNewCount) break;
                if (await _downloadHistory.IsHandledAsync(target.TalentId, search?.ExternalJobId ?? _activeTalentJob?.JobId ?? "", cancellationToken))
                {
                    skippedDuplicates++;
                    AppendLog($"快速跳过已处理人才 ID：{target.TalentId}");
                    continue;
                }

                AppendLog($"获取新人才 {files.Count + 1}/{requestedNewCount}：{target.Label}（ID {target.TalentId}）");
                var listUrl = _webView.Source?.ToString() ?? sourceUrl;
                var useBackgroundDetailBrowser = false;
                try
                {
                    if (Uri.TryCreate(target.Url, UriKind.Absolute, out var detailUri) &&
                        detailUri.Host.EndsWith("51job.com", StringComparison.OrdinalIgnoreCase))
                    {
                        useBackgroundDetailBrowser = true;
                        AppendLog($"后台加载人才详情：ID {target.TalentId}（右侧搜索页保持不动）。");
                        await NavigateAsync(target.Url, cancellationToken, _detailWebView);
                    }
                    else
                    {
                        if (!await ExecuteActionAsync(BrowserScripts.ClickCandidateTarget(target.DomIndex), cancellationToken))
                            throw new InvalidOperationException("无法打开人才卡片");
                        await WaitForDetailOpenAsync(_settings.Download.DetailOpenDelayMilliseconds, cancellationToken);
                    }

                    if (batchType.StartsWith("人才管理", StringComparison.OrdinalIgnoreCase))
                    {
                        var applicantInfoClicked = await ClickApplicantInformationWithWaitAsync(cancellationToken);
                        if (applicantInfoClicked)
                            await Task.Delay(1000, cancellationToken);
                    }
                    var detailReady = await WaitForCandidateDetailAsync(
                        cancellationToken,
                        useBackgroundDetailBrowser ? _detailWebView : null);
                    if (!detailReady)
                        throw new InvalidOperationException("候选人详情未载入，拒绝把人才列表页保存为简历 JSON");

                    var candidate = await ExtractCurrentPageAsync(
                        cancellationToken,
                        useBackgroundDetailBrowser ? _detailWebView : null);
                    if (search is not null && !string.IsNullOrWhiteSpace(search.ExternalJobId))
                    {
                        candidate.SourceJob = new SourceJobData
                        {
                            ExternalJobId = search.ExternalJobId,
                            ExternalJobName = search.SelectedPosition,
                            DownloadBatch = Path.GetFileName(folder),
                        };
                    }
                    if (string.IsNullOrWhiteSpace(candidate.TalentId)) candidate.TalentId = target.TalentId;
                    if (await _downloadHistory.IsHandledAsync(candidate.TalentId, search?.ExternalJobId ?? _activeTalentJob?.JobId ?? "", cancellationToken))
                    {
                        skippedDuplicates++;
                        AppendLog($"详情页人才 ID {candidate.TalentId} 已处理，跳过。");
                    }
                    else
                    {
                        var path = await _exportService.SaveCandidateAsync(folder, candidate, files.Count + 1, cancellationToken);
                        files.Add(Path.GetFileName(path));
                        await _downloadHistory.RecordAsync(new DownloadRecord
                        {
                            TalentId = candidate.TalentId,
                            SourceRoute = candidate.SourceRoute,
                            CandidateName = candidate.CandidateName,
                            JsonPath = path,
                            BatchType = batchType,
                            SourceJobId = search?.ExternalJobId ?? _activeTalentJob?.JobId,
                            SourceJobName = search?.SelectedPosition ?? _activeTalentJob?.JobName,
                            DownloadedAt = candidate.DownloadedAt
                        }, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    discardedCount++;
                    var reason = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
                    notes.Add($"丢弃人才 {target.TalentId}：{reason}");
                    AppendLog($"丢弃无法处理的人才 ID {target.TalentId}：{reason}");
                    if (_settings.Download.DiscardFailures)
                    {
                        await _downloadHistory.RecordDiscardedAsync(new DiscardRecord
                        {
                            TalentId = target.TalentId,
                            Reason = reason,
                            PositionName = search?.SelectedPosition
                        }, cancellationToken);
                    }
                }
                finally
                {
                    if (!useBackgroundDetailBrowser)
                        await RestoreResultListAsync(listUrl, cancellationToken);
                }
            }

            if (files.Count >= requestedNewCount)
            {
                canContinueImmediately = true;
                break;
            }
            if (usingApiTargets)
            {
                var hasMoreApiPages = apiPage!.CurrentPageCount > 0 &&
                                      _activeTalentPageIndex * 50 < apiPage.TotalCount;
                if (!hasMoreApiPages)
                {
                    canContinueImmediately = false;
                    break;
                }

                var nextPage = _activeTalentPageIndex + 1;
                AppendLog($"接口继续加载第 {nextPage} 页（每页50条）。");
                await RequestTalentSearchPageAsync(
                    _activeTalentJob!, nextPage, apiPage.RequestId, cancellationToken);
                canContinueImmediately = true;
                continue;
            }
            if (!await ScrollForMoreCandidatesAsync(cancellationToken))
            {
                canContinueImmediately = false;
                break;
            }
            canContinueImmediately = true;
        }

        var indexData = new BatchIndex
        {
            BatchType = batchType,
            Search = search,
            SourceRoute = sourceRoute,
            DatabasePath = _downloadHistory.DatabasePath,
            DiscoveredCount = discoveredCount,
            DownloadedCount = files.Count(name => name.StartsWith("candidate_", StringComparison.OrdinalIgnoreCase)),
            SkippedDuplicateCount = skippedDuplicates,
            DiscardedCount = discardedCount,
            Files = files,
            Notes = notes
        };
        await _exportService.SaveIndexAsync(folder, indexData, cancellationToken);
        _diagnostics.Write("batch_finished", new
        {
            batch_type = batchType,
            downloaded = indexData.DownloadedCount,
            skipped = skippedDuplicates,
            discarded = discardedCount,
            pages = pagesVisited,
            can_continue = canContinueImmediately
        });
        AppendLog($"任务完成：新下载 {indexData.DownloadedCount}，跳过 {skippedDuplicates}，丢弃 {discardedCount}。JSON：{folder}");
        return new BatchDownloadResult(
            indexData.DownloadedCount,
            skippedDuplicates,
            discardedCount,
            canContinueImmediately,
            folder);
    }

    private static List<CandidateTarget> CreateTalentSearchApiTargets(TalentSearchApiSnapshot snapshot)
    {
        var requestId = Uri.EscapeDataString(snapshot.RequestId);
        var jobId = Uri.EscapeDataString(snapshot.JobId);
        return snapshot.Candidates.Select(candidate =>
        {
            var talentId = Uri.EscapeDataString(candidate.TalentId);
            var url = $"https://ehire.51job.com/Revision/talent/resume/detail?contentType=1" +
                      $"&resumeId={talentId}&requestId={requestId}" +
                      $"&fromModule=foundTalentSerachCommon&recommendJobId={jobId}&jobId={jobId}" +
                      $"&rt={DateTimeOffset.Now.ToUnixTimeMilliseconds()}";
            return new CandidateTarget
            {
                TalentId = candidate.TalentId,
                Label = string.IsNullOrWhiteSpace(candidate.CandidateName) ? candidate.TalentId : candidate.CandidateName,
                Url = url,
                DomIndex = -1
            };
        }).ToList();
    }

    private async Task<bool> ScrollForMoreCandidatesAsync(CancellationToken cancellationToken)
    {
        var before = (await ExecuteJsonAsync<List<CandidateTarget>>(
            BrowserScripts.DiscoverCandidateTargets, cancellationToken) ?? [])
            .Select(target => target.TalentId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (!await ExecuteActionAsync(BrowserScripts.ScrollToLoadMoreCandidates, cancellationToken))
            return false;

        var delay = Math.Clamp(_settings.Download.LoadMoreDelayMilliseconds, 800, 5000);
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(delay * 3, 5000));
        do
        {
            await Task.Delay(delay, cancellationToken);
            var after = (await ExecuteJsonAsync<List<CandidateTarget>>(
                BrowserScripts.DiscoverCandidateTargets, cancellationToken) ?? [])
                .Select(target => target.TalentId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (after > before)
            {
                AppendLog($"向下滚动后人才列表由 {before} 条增加到 {after} 条。");
                _diagnostics.Write("candidate_list_loaded_more", new { before, after });
                return true;
            }
        } while (DateTime.UtcNow < deadline);

        AppendLog("向下滚动后未出现新的50条人才，当前搜索结果可能已加载完。 ");
        _diagnostics.Write("candidate_list_load_more_exhausted", new { before });
        return false;
    }

    private async Task WaitForDetailOpenAsync(int minimumDelayMilliseconds, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(4);
        do
        {
            await Task.Delay(Math.Clamp(minimumDelayMilliseconds / 3, 120, 350), cancellationToken);
            var path = _webView.Source?.AbsolutePath ?? string.Empty;
            if (path.Contains("/resume/detail", StringComparison.OrdinalIgnoreCase)) return;
        } while (DateTime.UtcNow < deadline);
    }

    private async Task<bool> ClickApplicantInformationWithWaitAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(4);
        var attempts = 0;
        do
        {
            attempts++;
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ExecuteJsonAsync<ActionResult>(BrowserScripts.ClickApplicantInformation, cancellationToken);
            if (result?.Ok == true)
            {
                AppendLog(result.Message);
                _diagnostics.Write("applicant_information_clicked", new { attempts });
                return true;
            }
            await Task.Delay(300, cancellationToken);
        } while (DateTime.UtcNow < deadline);
        AppendLog("候选人详情未显示“应聘者信息”入口，将继续执行详情内容校验。");
        _diagnostics.Write("applicant_information_not_found", new { attempts });
        return false;
    }

    private async Task<bool> WaitForCandidateDetailAsync(
        CancellationToken cancellationToken,
        WebView2? browser = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var attempts = 0;
        do
        {
            attempts++;
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ExecuteJsonAsync<ActionResult>(
                BrowserScripts.VerifyCandidateDetail, cancellationToken, browser);
            if (result?.Ok == true)
            {
                AppendLog(result.Message);
                _diagnostics.Write("candidate_detail_verified", new { attempts });
                return true;
            }
            await Task.Delay(300, cancellationToken);
        } while (DateTime.UtcNow < deadline);
        _diagnostics.Write("candidate_detail_timeout", new { attempts });
        return false;
    }

    private async Task RestoreResultListAsync(string listUrl, CancellationToken cancellationToken)
    {
        var currentUrl = _webView.Source?.ToString() ?? string.Empty;
        if (!string.Equals(currentUrl, listUrl, StringComparison.OrdinalIgnoreCase))
        {
            if (_webView.CoreWebView2.CanGoBack)
                _webView.CoreWebView2.GoBack();
            else
                _webView.CoreWebView2.Navigate(listUrl);
            await Task.Delay(_settings.Download.ListRestoreDelayMilliseconds, cancellationToken);
        }
        else
        {
            await ExecuteActionAsync(BrowserScripts.CloseCandidateOverlay, cancellationToken);
        }
    }

    private async Task<CandidatePageData> ExtractCurrentPageAsync(
        CancellationToken cancellationToken,
        WebView2? browser = null)
    {
        var data = await ExecuteJsonAsync<CandidatePageData>(
                BrowserScripts.ExtractCandidatePage, cancellationToken, browser)
            ?? throw new InvalidOperationException("无法读取当前页面信息。");
        data.DownloadedAt = DateTimeOffset.Now;
        return data;
    }

    private async Task Ensure51JobPageAsync(CancellationToken cancellationToken)
    {
        var host = _webView.Source?.Host ?? string.Empty;
        if (!host.EndsWith("51job.com", StringComparison.OrdinalIgnoreCase))
            await NavigateAsync(_settings.TalentSearchUrl, cancellationToken);
        await Require51JobLoginAsync(cancellationToken);
    }

    private async Task Require51JobLoginAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = await DetectCookieLoginAsync();
        if (state.LoggedIn) return;
        throw new InvalidOperationException("未检测到有效的 51job 登录 Cookie。请在右侧完成登录；如仍无法识别，请在 appsettings.json 配置 RequiredCookieNames。");
    }

    private async Task NavigateAsync(
        string url,
        CancellationToken cancellationToken,
        WebView2? browser = null)
    {
        var targetBrowser = browser ?? _webView;
        if (ReferenceEquals(targetBrowser, _detailWebView))
        {
            if (!_detailBrowserReady) throw new InvalidOperationException("后台详情浏览器尚未准备好。");
        }
        else if (!_browserReady) return;
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
        handler = (_, _) => completion.TrySetResult();
        targetBrowser.CoreWebView2.NavigationCompleted += handler;
        try
        {
            targetBrowser.CoreWebView2.Navigate(url);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(45), cancellationToken);
        }
        finally
        {
            targetBrowser.CoreWebView2.NavigationCompleted -= handler;
        }
    }

    private async Task<bool> ClickTextAsync(string text, bool contains, CancellationToken cancellationToken)
        => await ExecuteActionAsync(BrowserScripts.ClickText(text, contains), cancellationToken);

    private async Task<bool> ExecuteActionAsync(string script, CancellationToken cancellationToken)
    {
        var result = await ExecuteJsonAsync<ActionResult>(script, cancellationToken);
        if (!string.IsNullOrWhiteSpace(result?.Message)) AppendLog(result.Message);
        return result?.Ok == true;
    }

    private async Task<T?> ExecuteJsonAsync<T>(
        string script,
        CancellationToken cancellationToken,
        WebView2? browser = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var targetBrowser = browser ?? _webView;
        var raw = await targetBrowser.CoreWebView2.ExecuteScriptAsync(script).WaitAsync(cancellationToken);
        var innerJson = JsonSerializer.Deserialize<string>(raw, _jsonOptions) ?? raw;
        return JsonSerializer.Deserialize<T>(innerJson, _jsonOptions);
    }

    private async void StatusTimer_Tick(object? sender, EventArgs e)
    {
        if (!_browserReady || _busy) return;
        await RefreshLoginStatusAsync();
    }

    private async Task RefreshLoginStatusAsync()
    {
        if (!_browserReady) return;
        try
        {
            var state = await DetectCookieLoginAsync();
            if (state.LoggedIn)
                SetLoginStatus(true, $"已登录 · Cookie {state.MatchedCookieCount}");
            else if ((_webView.Source?.Host ?? string.Empty).EndsWith("51job.com", StringComparison.OrdinalIgnoreCase))
                SetLoginStatus(false, "未登录 · 未检测到认证Cookie");
            else
                SetLoginStatus(false, "未打开招聘网站");
        }
        catch
        {
            SetLoginStatus(false, "状态检测中");
        }
    }

    private async Task<CookieLoginResult> DetectCookieLoginAsync()
    {
        if (!_browserReady || _webView.CoreWebView2 is null) return new CookieLoginResult(false, 0);
        var cookies = await _webView.CoreWebView2.CookieManager.GetCookiesAsync(_settings.Authentication.CookieUrl);
        var usable = cookies
            .Where(cookie => cookie.Domain.EndsWith("51job.com", StringComparison.OrdinalIgnoreCase) &&
                             !string.IsNullOrWhiteSpace(cookie.Value))
            .ToList();

        bool hasAuthenticationCookie;
        int matchedCount;
        if (_settings.Authentication.RequiredCookieNames.Length > 0)
        {
            var matchedNames = _settings.Authentication.RequiredCookieNames
                .Count(required => usable.Any(cookie => cookie.Name.Equals(required, StringComparison.OrdinalIgnoreCase)));
            matchedCount = matchedNames;
            hasAuthenticationCookie = matchedNames == _settings.Authentication.RequiredCookieNames.Length;
        }
        else
        {
            var hints = _settings.Authentication.CookieNameHints;
            matchedCount = usable.Count(cookie => hints.Any(hint =>
                cookie.Name.Contains(hint, StringComparison.OrdinalIgnoreCase)));
            hasAuthenticationCookie = matchedCount > 0;
        }

        var path = _webView.Source?.AbsolutePath ?? string.Empty;
        var onLoginPage = path.Contains("/login", StringComparison.OrdinalIgnoreCase) ||
                          path.Contains("/sso/jump", StringComparison.OrdinalIgnoreCase);
        return new CookieLoginResult(hasAuthenticationCookie && !onLoginPage, matchedCount);
    }

    private void SetLoginStatus(bool loggedIn, string text)
    {
        _statusLabel.Text = "● " + text;
        _statusLabel.ForeColor = loggedIn ? Color.FromArgb(22, 163, 74) : Color.FromArgb(234, 88, 12);
    }

    private void SetOperationButtons(bool enabled)
    {
        _openWebsiteButton.Enabled = enabled;
        _managementButton.Enabled = enabled;
        _searchButton.Enabled = enabled;
        _uploadButton.Enabled = enabled;
        _limitInput.Enabled = enabled;
        _stopButton.Enabled = !enabled;
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(message));
            return;
        }
        _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private static string SanitizeDiagnosticMessage(string message)
    {
        var sanitized = Regex.Replace(message, @"https?://[^\s]+", "[url]");
        sanitized = Regex.Replace(sanitized, @"\b\d{6,}\b", "[id]");
        return sanitized.Length > 300 ? sanitized[..300] : sanitized;
    }

    private async Task UploadJsonResumesAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_exportService.RootDirectory);
        AppendLog($"开始上传 JSON：{_exportService.RootDirectory}");
        var uploader = new ResumeUploadService(_settings.ServerUpload);
        var result = await uploader.UploadDirectoryAsync(_exportService.RootDirectory, AppendLog, cancellationToken);
        var removedBatchDirectories = ClearEmptyResumeBatchDirectories();
        var removedCaptureFiles = 0;
        if (_settings.NetworkCapture.ClearAfterSuccessfulUpload && result.Total > 0 && result.Failed == 0 && _networkCapture is not null)
            removedCaptureFiles = await _networkCapture.ClearStoredFilesAsync(cancellationToken);
        if (result.Total == 0)
        {
            removedCaptureFiles = _networkCapture is null
                ? 0
                : await _networkCapture.ClearStoredFilesAsync(cancellationToken);
            AppendLog($"没有可上传的简历；已清理 {removedBatchDirectories} 个临时批次目录、{removedCaptureFiles} 个网络诊断文件。");
        }
        else if (removedBatchDirectories > 0 || removedCaptureFiles > 0)
            AppendLog($"上传后清理：{removedBatchDirectories} 个空批次目录、{removedCaptureFiles} 个网络诊断文件。");
        AppendLog($"上传完成：成功 {result.Uploaded}，服务器已存在 {result.Duplicates}，上传失败 {result.Failed}，清理失败 {result.CleanupFailed}。服务端扫描：{result.ScanMessage}");
        MessageBox.Show(this,
            $"上传完成。\n\n扫描：{result.Total}\n成功：{result.Uploaded}\n服务器已存在：{result.Duplicates}\n上传失败：{result.Failed}\n本地清理失败：{result.CleanupFailed}\n服务端扫描：{result.ScanMessage}",
            "51job", MessageBoxButtons.OK,
            result.Failed == 0 && result.CleanupFailed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private int ClearEmptyResumeBatchDirectories()
    {
        var root = Path.GetFullPath(_exportService.RootDirectory);
        if (!Directory.Exists(root)) return 0;

        var deleted = 0;
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly).ToArray())
        {
            var resolved = Path.GetFullPath(directory);
            if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                if (Directory.EnumerateFileSystemEntries(resolved).Any()) continue;
                Directory.Delete(resolved);
                deleted++;
            }
            catch (Exception ex)
            {
                AppendLog($"临时批次目录清理失败：{Path.GetFileName(resolved)}；{ex.Message}");
            }
        }
        return deleted;
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _statusTimer.Stop();
        _operationCancellation?.Cancel();
        _networkCapture?.Dispose();
        _detailWebView.Dispose();
    }

    private static AppSettings LoadSettings()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    private sealed class ActionResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    private sealed record CookieLoginResult(bool LoggedIn, int MatchedCookieCount);
}
