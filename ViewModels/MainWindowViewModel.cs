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

    public MainWindowViewModel()
    {
        _db = new DatabaseService();
        _detector = new AuthDetectService();
        _imapService = new ImapEmailService();
        _graphService = new GraphEmailService();
        LoadAccounts();
    }

    public bool HasSelectedEmail => SelectedEmail != null;
    public bool HasSelectedAccount => SelectedAccount != null;
    public bool HasNoSelectedAccount => SelectedAccount == null;

    private void LoadAccounts()
    {
        Accounts.Clear();
        foreach (var acc in _db.GetAccounts())
            Accounts.Add(acc);
    }

    [RelayCommand]
    private async Task ImportAccount()
    {
        var dialog = new Views.ImportDialog();
        var result = await dialog.ShowDialog<EmailAccount?>(App.MainWindow);
        if (result == null) return;

        StatusText = $"正在检测 {result.Email} 的协议...";
        var detectResult = await _detector.DetectAsync(result);

        result.Status = detectResult.Success ? "Verified" : "Failed";
        result.StatusMessage = detectResult.StatusMessage;
        result.AuthType = detectResult.AuthType;
        result.Id = _db.SaveAccount(result);

        Accounts.Insert(0, result);
        StatusText = detectResult.Success
            ? $"{result.Email} 导入成功 ({detectResult.StatusMessage})"
            : $"{result.Email} 导入失败 ({detectResult.StatusMessage})";
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
        StatusText = $"已删除账号: {account.Email}";
    }

    [RelayCommand]
    private async Task RefreshAccount(EmailAccount account)
    {
        if (account == null) return;
        await FetchEmailsForAccount(account);
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
        SelectedEmail = null;
        OnPropertyChanged(nameof(HasSelectedAccount));
        OnPropertyChanged(nameof(HasNoSelectedAccount));
        if (value != null)
        {
            _ = FetchEmailsForAccount(value);
        }
    }

    partial void OnSelectedEmailChanged(EmailMessage? value)
    {
        OnPropertyChanged(nameof(HasSelectedEmail));
    }

    partial void OnSearchTextChanged(string value)
    {
        if (SelectedAccount == null) return;
        var allEmails = _db.GetMessages(SelectedAccount.Id);
        var filtered = string.IsNullOrWhiteSpace(value)
            ? allEmails
            : allEmails.Where(m =>
                m.Subject.Contains(value, StringComparison.OrdinalIgnoreCase) ||
                m.From.Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();
        Emails.Clear();
        foreach (var m in filtered)
            Emails.Add(m);
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
            {
                messages = await _imapService.FetchByXoauth2Async(account.Email, accessToken, 50);
            }
            else if (!string.IsNullOrEmpty(account.Password))
            {
                messages = await _imapService.FetchByPasswordAsync(account, 50);
            }
            _db.DeleteMessages(account.Id);
            _db.SaveMessages(account.Id, messages);
        }
        catch
        {
        }

        var dbMessages = _db.GetMessages(account.Id);
        Emails.Clear();
        foreach (var m in dbMessages)
            Emails.Add(m);

        IsLoading = false;
        StatusText = $"共 {Emails.Count} 封邮件";
    }
}
