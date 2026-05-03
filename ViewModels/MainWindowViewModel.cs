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
    private bool _hasSelectedAccounts;

    public MainWindowViewModel()
    {
        _db = new DatabaseService();
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
        foreach (var acc in _db.GetAccounts())
            Accounts.Add(acc);
    }

    [RelayCommand]
    private void ImportAccount()
    {
        var dialog = new Views.ImportDialog();
        if (dialog.DataContext is ImportDialogViewModel vm)
        {
            vm.AccountVerified += OnAccountVerified;
            vm.ProgressUpdated += (current, total) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ImportCurrent = current;
                    ImportTotal = total;
                    IsImporting = true;
                });
            };
            dialog.Closed += (_, _) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => IsImporting = false);
            };
        }
        dialog.Show(App.MainWindow);
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
        StatusText = $"已删除 {toRemove.Count} 个账号";
    }

    private void OnAccountVerified(EmailAccount account)
    {
        account.Id = _db.SaveAccount(account);
        Accounts.Insert(0, account);
        account.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EmailAccount.IsSelected))
                OnPropertyChanged(nameof(HasSelectedAny));
        };
        OnPropertyChanged(nameof(HasSelectedAny));
        StatusText = $"已导入: {account.Email}";
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
        FilterEmails();
    }

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
