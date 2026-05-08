using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OutlookApp.Models;
using OutlookApp.Services;

namespace OutlookApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly DatabaseService _db;
    private readonly AuthDetectService _detector;
    private readonly ImapEmailService _imapService;
    private readonly GraphEmailService _graphService;
    private List<EmailMessage> _allEmails = new();
    private const int PageSize = 20;

    [ObservableProperty]
    private ObservableCollection<EmailAccount> _accounts = new();

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

    public bool HasSelectedEmail => SelectedEmail != null;
    public bool HasSelectedAccount => SelectedAccount != null;
    public bool HasNoSelectedAccount => SelectedAccount == null;
    public bool HasSelectedAny => Accounts.Any(a => a.IsSelected);

    private void LoadAccounts()
    {
        Accounts.Clear();
        var list = _db.GetAccounts();
        for (int i = 0; i < list.Count; i++)
        {
            list[i].Index = i + 1;
            Accounts.Add(list[i]);
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
                acc.Index = Accounts.Count + 1;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Accounts.Insert(0, acc));
                successCount++;
            }
            else
            {
                _db.DeleteAccount(acc.Id);
                failCount++;
            }
        }

        UpdateIndices();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsImporting = false;
            StatusText = $"检测完成：成功 {successCount} 个，失败 {failCount} 个";
        });
    }

    private void UpdateIndices()
    {
        for (int i = 0; i < Accounts.Count; i++)
            Accounts[i].Index = i + 1;
    }

    [RelayCommand]
    private void MarkAsUsed(EmailAccount account)
    {
        if (account == null) return;
        account.IsUsed = true;
        _db.MarkAccountAsUsed(account.Id);
        Accounts.Remove(account);
        if (SelectedAccount == account)
        {
            SelectedAccount = null;
            Emails.Clear();
        }
        UpdateIndices();
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
        Accounts.Remove(account);
        if (SelectedAccount == account)
        {
            SelectedAccount = null;
            Emails.Clear();
        }
        UpdateIndices();
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
            Accounts.Remove(acc);
        }
        if (toRemove.Contains(SelectedAccount))
        {
            SelectedAccount = null;
            Emails.Clear();
        }
        UpdateIndices();
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
        var remaining = _allEmails.Skip(LoadedEmailCount).Take(PageSize).ToList();
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
        var page = source.Take(PageSize).ToList();
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
}
