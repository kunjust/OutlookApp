using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OutlookApp.Models;
using OutlookApp.Services;
using Timer = System.Timers.Timer;

namespace OutlookApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly DatabaseService _db;
    private readonly AuthDetectService _detector;
    private readonly ImapEmailService _imapService;
    private readonly GraphEmailService _graphService;
    private List<EmailMessage> _allEmails = new();
    private List<EmailAccount> _allAccounts = new();
    private const int EmailPageSize = 20;
    private const int AccountPageSize = 30;
    private int _accountPage = 1;

    // ═══ 卡密激活相关 ═══
    private LicenseInfo? _currentLicense;
    private Timer? _heartbeatTimer;
    private int _heartbeatRetryCount;
    private int _heartbeatTickCount;   // 心跳 tick 计数，每 12 tick（1小时）调一次服务端
    private const int HeartbeatServerInterval = 12; // 每 12 次本地 tick（5min×12=1h）调一次服务端
    private const int HeartbeatLocalMs = 300_000;   // 5 分钟本地检查
    private readonly LicenseService _licenseService = new();
    private readonly LicenseStorageService _licenseStorage = new();

    [ObservableProperty]
    private bool _isActivated;

    [ObservableProperty]
    private string _windowTitle = "IKC";

    [ObservableProperty]
    private ObservableCollection<EmailAccount> _accounts = new();

    [ObservableProperty]
    private int _accountPageNum;

    [ObservableProperty]
    private int _accountTotalPages;

    [ObservableProperty]
    private bool _hasPrevPage;

    [ObservableProperty]
    private bool _hasNextPage;

    [ObservableProperty]
    private int _accountTotal;

    [ObservableProperty]
    private EmailAccount? _selectedAccount;

    [ObservableProperty]
    private ObservableCollection<EmailMessage> _emails = new();

    [ObservableProperty]
    private EmailMessage? _selectedEmail;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private int _importCurrent;

    [ObservableProperty]
    private int _importTotal;

    [ObservableProperty]
    private int _loadedEmailCount;

    [ObservableProperty]
    private bool _hasMoreEmails;

    [ObservableProperty]
    private bool _isAllSelected;

    [ObservableProperty]
    private KeywordViewModel? _keywordViewModel;

    [ObservableProperty]
    private bool _showKeywordPanel;

    public MainWindowViewModel()
    {
        _db = new DatabaseService();
        _detector = new AuthDetectService();
        _imapService = new ImapEmailService();
        _graphService = new GraphEmailService();
        LoadAccounts();
    }

    public MainWindowViewModel(DatabaseService dbService)
    {
        _db = dbService;
        _detector = new AuthDetectService();
        _imapService = new ImapEmailService();
        _graphService = new GraphEmailService();
        LoadAccounts();
    }

    public MainWindowViewModel(DatabaseService dbService, KeywordService kwService)
        : this(dbService)
    {
        KeywordViewModel = new KeywordViewModel(kwService);
    }

    public bool HasSelectedEmail => SelectedEmail != null;
    public bool HasSelectedAccount => SelectedAccount != null;
    public bool HasNoSelectedAccount => SelectedAccount == null;
    public bool HasSelectedAny => Accounts.Any(a => a.IsSelected);
    public bool ShowEmailPanel => !ShowKeywordPanel;

    [RelayCommand]
    private void TogglePanel()
    {
        ShowKeywordPanel = !ShowKeywordPanel;
        StatusText = ShowKeywordPanel ? "对标管理" : "邮件管理";
    }

    private void LoadAccounts(int page = 1)
    {
        _allAccounts.Clear();
        _allAccounts = _db.GetAccounts();
        for (int i = 0; i < _allAccounts.Count; i++)
        {
            _allAccounts[i].Index = i + 1;
        }
        _accountPage = page;
        LoadAccountPage();
    }

    private void LoadAccountPage()
    {
        Accounts.Clear();
        _accountTotalPages = (_allAccounts.Count + AccountPageSize - 1) / AccountPageSize;
        if (_accountTotalPages < 1) _accountTotalPages = 1;
        _accountPageNum = _accountPage;
        HasPrevPage = _accountPage > 1;
        HasNextPage = _accountPage < _accountTotalPages;
        AccountTotal = _allAccounts.Count;

        var startIdx = (_accountPage - 1) * AccountPageSize;
        var endIdx = Math.Min(startIdx + AccountPageSize, _allAccounts.Count);
        for (int i = startIdx; i < endIdx; i++)
        {
            Accounts.Add(_allAccounts[i]);
        }
    }

    [RelayCommand]
    private void PrevAccountPage()
    {
        if (_accountPage > 1)
        {
            _accountPage--;
            LoadAccountPage();
        }
    }

    [RelayCommand]
    private void NextAccountPage()
    {
        if (_accountPage < _accountTotalPages)
        {
            _accountPage++;
            LoadAccountPage();
        }
    }

    partial void OnIsAllSelectedChanged(bool value)
    {
        foreach (var acc in Accounts)
            acc.IsSelected = value;
        OnPropertyChanged(nameof(HasSelectedAny));
    }

    [RelayCommand]
    private async Task ImportAccount()
    {
        var dialog = new Views.ImportDialog();
        var result = await dialog.ShowDialog<List<EmailAccount>?>(App.MainWindow);
        if (result == null || result.Count == 0) return;

        ImportTotal = result.Count;
        IsImporting = true;
        var successCount = 0;
        var failCount = 0;

        for (int i = 0; i < result.Count; i++)
        {
            var acc = result[i];
            ImportCurrent = i + 1;
            StatusText = $"正在检测 ({i + 1}/{result.Count}) {acc.Email}...";

            var detection = await _detector.DetectAsync(acc);

            if (detection.Success)
            {
                acc.Status = "Verified";
                acc.StatusMessage = detection.StatusMessage;
                acc.AuthType = detection.AuthType;
                _db.UpdateAccountStatus(acc.Id, acc.Status, acc.StatusMessage, acc.AuthType);
                _allAccounts.Add(acc);
                successCount++;
            }
            else
            {
                // 输出失败详情到控制台，方便排查
                var errorMsg = $"❌ {acc.Email} 检测失败";
                foreach (var log in detection.LogMessages)
                    errorMsg += $" | {log.Protocol}: {log.Message}";
                Console.WriteLine(errorMsg);
                StatusText = $"失败: {acc.Email} → {detection.StatusMessage}";
                _db.DeleteAccount(acc.Id);
                failCount++;
            }
        }

        UpdateIndices();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoadAccountPage();
            IsImporting = false;
            StatusText = $"检测完成：成功 {successCount} 个，失败 {failCount} 个";
        });
    }

    private void UpdateIndices()
    {
        for (int i = 0; i < _allAccounts.Count; i++)
            _allAccounts[i].Index = i + 1;
    }

    [RelayCommand]
    private void MarkAsUsed(EmailAccount account)
    {
        if (account == null) return;
        var existing = _allAccounts.FirstOrDefault(a => a.Id == account.Id);
        if (existing != null)
        {
            existing.IsUsed = true;
            _db.MarkAccountAsUsed(account.Id);
            _allAccounts.Remove(existing);
        }
        if (SelectedAccount != null && SelectedAccount.Id == account.Id)
        {
            SelectedAccount = null;
            Emails.Clear();
        }
        UpdateIndices();
        LoadAccountPage();
        StatusText = $"已标记使用: {account.Email}";
    }

    [RelayCommand]
    private void CopyEmail(EmailAccount account)
    {
        if (account == null) return;
        StatusText = $"已复制: {account.Email}";
    }

    [RelayCommand]
    private void DeleteAccount(EmailAccount account)
    {
        if (account == null) return;
        _db.DeleteAccount(account.Id);
        var existing = _allAccounts.FirstOrDefault(a => a.Id == account.Id);
        if (existing != null) _allAccounts.Remove(existing);
        if (SelectedAccount != null && SelectedAccount.Id == account.Id)
        {
            SelectedAccount = null;
            Emails.Clear();
        }
        UpdateIndices();
        LoadAccountPage();
        StatusText = $"已删除: {account.Email}";
    }

    [RelayCommand]
    private void DeleteSelectedAccounts()
    {
        var toRemove = Accounts.Where(a => a.IsSelected).ToList();
        if (toRemove.Count == 0) return;
        foreach (var acc in toRemove)
        {
            _db.DeleteAccount(acc.Id);
            _allAccounts.Remove(acc);
        }
        if (toRemove.Contains(SelectedAccount))
        {
            SelectedAccount = null;
            Emails.Clear();
        }
        UpdateIndices();
        LoadAccountPage();
        StatusText = $"已删除 {toRemove.Count} 个账号";
    }

    [RelayCommand]
    private async Task RefreshAccount(EmailAccount account)
    {
        if (account == null) return;
        StatusText = $"正在刷新 {account.Email}...";
        if (SelectedAccount == account)
            await FetchEmailsForAccount(account);
        StatusText = "刷新完成";
    }

    [RelayCommand]
    private async Task RefreshAll()
    {
        StatusText = "正在刷新全部账号...";
        IsLoading = true;
        foreach (var acc in Accounts.ToList())
        {
            if (acc.Status != "Verified") continue;
            if (SelectedAccount == acc)
                await FetchEmailsForAccount(acc);
        }
        IsLoading = false;
        StatusText = "全部刷新完成";
    }

    partial void OnSelectedAccountChanged(EmailAccount? value)
    {
        Emails.Clear();
        _allEmails.Clear();
        SelectedEmail = null;
        LoadedEmailCount = 0;
        HasMoreEmails = false;
        OnPropertyChanged(nameof(HasSelectedAccount));
        OnPropertyChanged(nameof(HasNoSelectedAccount));
        if (value != null && value.Status == "Verified")
            _ = FetchEmailsForAccount(value);
    }

    partial void OnSelectedEmailChanged(EmailMessage? value)
    {
        OnPropertyChanged(nameof(HasSelectedEmail));
    }

    partial void OnSearchTextChanged(string value) => FilterEmails();

    [RelayCommand]
    private void LoadMoreEmails()
    {
        var remaining = _allEmails.Skip(LoadedEmailCount).Take(EmailPageSize).ToList();
        foreach (var m in remaining)
            Emails.Add(m);
        LoadedEmailCount += remaining.Count;
        HasMoreEmails = LoadedEmailCount < _allEmails.Count;
    }

    private void FilterEmails()
    {
        var source = string.IsNullOrWhiteSpace(SearchText)
            ? _allEmails
            : _allEmails.Where(m =>
                m.Subject.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                m.From.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

        Emails.Clear();
        LoadedEmailCount = 0;
        var page = source.Take(EmailPageSize).ToList();
        foreach (var m in page)
            Emails.Add(m);
        LoadedEmailCount = page.Count;
        HasMoreEmails = LoadedEmailCount < source.Count;
    }

    private async Task FetchEmailsForAccount(EmailAccount account)
    {
        StatusText = $"正在获取 {account.Email} 的邮件...";
        IsLoading = true;
        try
        {
            List<EmailMessage> messages = new();
            var accessToken = await _graphService.RefreshTokenAsync(account);
            if (!string.IsNullOrEmpty(accessToken))
                messages = await _imapService.FetchByXoauth2Async(account.Email, accessToken, 5);
            else if (!string.IsNullOrEmpty(account.Password))
                messages = await _imapService.FetchByPasswordAsync(account, 5);
            _db.DeleteMessages(account.Id);
            _db.SaveMessages(account.Id, messages);
        }
        catch { }
        _allEmails = _db.GetMessages(account.Id);
        FilterEmails();
        IsLoading = false;
        StatusText = $"共 {_allEmails.Count} 封邮件";
    }

    // ════════════════════════════════════════════
    // 卡密激活：初始化 / 心跳 / 解绑
    // ════════════════════════════════════════════

    /// <summary>
    /// 初始化卡密信息，更新标题，启动心跳定时器。
    /// App.axaml.cs 激活成功后调用。
    /// </summary>
    public void InitializeLicense(LicenseInfo license)
    {
        _currentLicense = license;
        IsActivated = true;
        UpdateWindowTitle();
        StartHeartbeat();
    }

    private void UpdateWindowTitle()
    {
        if (_currentLicense == null)
        {
            WindowTitle = "IKC";
            return;
        }

        if (_currentLicense.IsActive)
        {
            WindowTitle = $"IKC — 剩余{_currentLicense.TimeRemainingText}";
        }
        else
        {
            WindowTitle = "IKC — 卡密已过期";
        }
    }

    private void StartHeartbeat()
    {
        _heartbeatRetryCount = 0;
        _heartbeatTickCount = 0;
        _heartbeatTimer = new Timer(HeartbeatLocalMs); // 5 分钟
        _heartbeatTimer.Elapsed += OnHeartbeatTick;
        _heartbeatTimer.AutoReset = true;
        _heartbeatTimer.Start();
    }

    private async void OnHeartbeatTick(object? sender, ElapsedEventArgs e)
    {
        if (_currentLicense == null) return;

        _heartbeatTickCount++;

        // 每 5 分钟：本地检查到期时间
        var now = DateTime.UtcNow;
        if (now >= _currentLicense.ExpiryTime)
        {
            // 本地检测到已过期
            _currentLicense.UpdateServerTime(now);
            await _licenseStorage.SaveAsync(_currentLicense);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateWindowTitle());
            return;
        }

        // 每小时（12 tick）：调服务端心跳
        if (_heartbeatTickCount >= HeartbeatServerInterval)
        {
            _heartbeatTickCount = 0;

            try
            {
                var result = await _licenseService.HeartbeatAsync(_currentLicense.CardKey);
                _heartbeatRetryCount = 0;

                // 用服务端返回的 remainingDays 更新到期时间
                var newExpiry = now.AddDays(result.RemainingDays);
                _currentLicense.ExpiryTime = newExpiry;
                _currentLicense.UpdateServerTime(now);
                await _licenseStorage.SaveAsync(_currentLicense);
            }
            catch
            {
                _heartbeatRetryCount++;
                if (_heartbeatRetryCount >= 3)
                {
                    _heartbeatTimer?.Stop();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        ShowExitDialog("服务端心跳验证连续失败3次，即将退出程序。"));
                    return;
                }
            }
        }

        // 每 5 分钟：更新标题（显示剩余时间）
        Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateWindowTitle());
    }

    [RelayCommand]
    private async Task Unbind()
    {
        if (_currentLicense == null) return;

        // 确认弹框
        var confirm = new Avalonia.Controls.Window
        {
            Title = "确认解绑",
            Width = 400,
            Height = 180,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0D1117"))
        };

        var panel = new Avalonia.Controls.StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 16 };
        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "确定要解绑卡密吗？此操作将释放当前设备绑定。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E6EDF3"))
        });

        var btnPanel = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };

        var cancelBtn = new Avalonia.Controls.Button
        {
            Content = "取消",
            Width = 80,
            Height = 32
        };
        var okBtn = new Avalonia.Controls.Button
        {
            Content = "确定解绑",
            Width = 80,
            Height = 32,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F85149")),
            Foreground = Avalonia.Media.Brushes.White,
            BorderThickness = new Avalonia.Thickness(0)
        };

        var result = false;
        cancelBtn.Click += (_, _) => confirm.Close();
        okBtn.Click += (_, _) => { result = true; confirm.Close(); };

        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(okBtn);
        panel.Children.Add(btnPanel);
        confirm.Content = panel;

        var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (mainWindow != null)
            await confirm.ShowDialog(mainWindow);
        else
            return;

        if (!result) return;

        try
        {
            IsActivated = false;
            await _licenseService.UnbindAsync(_currentLicense.CardKey, "用户手动解绑");
        }
        catch { /* 即使解绑 API 失败也清除本地缓存 */ }

        // 清除本地缓存
        await _licenseStorage.ClearAsync();
        _heartbeatTimer?.Stop();

        // 退出到激活窗口
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop2)
        {
            var activationVm = new ActivationViewModel();
            var activationWindow = new Views.ActivationWindow
            {
                DataContext = activationVm
            };

            activationVm.ActivationSucceeded += newLicense =>
            {
                activationWindow.Close();
                InitializeLicense(newLicense);
                desktop2.MainWindow = mainWindow;
                mainWindow?.Show();
            };

            desktop2.MainWindow = activationWindow;
            mainWindow?.Hide();
            activationWindow.Show();
        }
    }

    /// <summary>
    /// 停止心跳（应用退出时调用）
    /// </summary>
    public void StopHeartbeat()
    {
        _heartbeatTimer?.Stop();
        _heartbeatTimer?.Dispose();
    }

    private async void ShowExitDialog(string message)
    {
        var dialog = new Avalonia.Controls.Window
        {
            Title = "卡密验证失败",
            Content = new Avalonia.Controls.TextBlock
            {
                Text = message,
                Margin = new Avalonia.Thickness(20),
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E6EDF3"))
            },
            Width = 360,
            Height = 160,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0D1117"))
        };

        await dialog.ShowDialog(Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow!
            : null!);

        Environment.Exit(1);
    }

    /// <summary>
    /// 弹出更新提示对话框
    /// </summary>
    public async Task ShowUpdateDialog(ReleaseInfo release)
    {
        var downloadUrl = release.DownloadUrl;
        var notes = string.IsNullOrEmpty(release.ReleaseNotes) ? "暂无更新说明" : release.ReleaseNotes;

        var dialog = new Avalonia.Controls.Window
        {
            Title = "发现新版本 v" + release.Version,
            Width = 480,
            Height = 360,
            WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0D1117"))
        };

        var panel = new Avalonia.Controls.StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 14 };

        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = $"发现新版本 v{release.Version}",
            FontSize = 18,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E6EDF3"))
        });

        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "当前版本: v" + UpdateService.CurrentVersion,
            FontSize = 12,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8B949E"))
        });

        panel.Children.Add(new Avalonia.Controls.Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#161B22")),
            Padding = new Avalonia.Thickness(12),
            Child = new Avalonia.Controls.TextBlock
            {
                Text = notes,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontSize = 12,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E6EDF3"))
            }
        });

        var btnPanel = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8
        };

        var skipBtn = new Avalonia.Controls.Button { Content = "暂不更新", Width = 90, Height = 32 };
        var downloadBtn = new Avalonia.Controls.Button
        {
            Content = "下载更新",
            Width = 90,
            Height = 32,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#58A6FF")),
            Foreground = Avalonia.Media.Brushes.White,
            BorderThickness = new Avalonia.Thickness(0)
        };

        skipBtn.Click += (_, _) => dialog.Close();
        downloadBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(downloadUrl))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(downloadUrl) { UseShellExecute = true }); }
                catch { }
            }
            dialog.Close();
        };

        btnPanel.Children.Add(skipBtn);
        btnPanel.Children.Add(downloadBtn);
        panel.Children.Add(btnPanel);
        dialog.Content = panel;

        var mainWin = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
        if (mainWin != null)
            await dialog.ShowDialog(mainWin);
    }

    [RelayCommand]
    private async Task CheckUpdate()
    {
        var updateSvc = new UpdateService();
        var release = await updateSvc.CheckAsync();
        if (release != null && UpdateService.IsNewer(release.Version))
        {
            await ShowUpdateDialog(release);
        }
        else
        {
            StatusText = "已是最新版本";
            // 3秒后恢复状态
            _ = Task.Delay(3000).ContinueWith(_ =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusText = "就绪"));
        }
    }
}
