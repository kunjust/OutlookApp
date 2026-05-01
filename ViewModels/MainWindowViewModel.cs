using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OutlookApp.Models;

namespace OutlookApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
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
    private string _statusText = "Ready";

    public MainWindowViewModel()
    {
        LoadSampleData();
    }

    public bool HasSelectedEmail => SelectedEmail != null;
    public bool HasSelectedAccount => SelectedAccount != null;
    public bool HasNoSelectedAccount => SelectedAccount == null;

    private void LoadSampleData()
    {
        Accounts.Add(new EmailAccount
        {
            Id = 1,
            Email = "user1@outlook.com",
            Status = "Verified",
            StatusMessage = "IMAP connected",
            AuthType = "imap"
        });
        Accounts.Add(new EmailAccount
        {
            Id = 2,
            Email = "user2@outlook.com",
            Status = "Verified",
            StatusMessage = "Graph API connected",
            AuthType = "graph"
        });
        Accounts.Add(new EmailAccount
        {
            Id = 3,
            Email = "user3@outlook.com",
            Status = "Failed",
            StatusMessage = "Token expired",
            AuthType = "graph"
        });
    }

    [RelayCommand]
    private async Task ImportAccount()
    {
        var dialog = new Views.ImportDialog();
        var result = await dialog.ShowDialog<EmailAccount?>(App.MainWindow);
        if (result != null)
        {
            Accounts.Add(result);
            StatusText = $"Account {result.Email} imported, detecting protocol...";
            await Task.Delay(1500);
            result.Status = "Verified";
            result.StatusMessage = "Auto-detected: IMAP";
            result.AuthType = "imap";
            OnPropertyChanged(nameof(result.Status));
            OnPropertyChanged(nameof(result.DisplayStatus));
            StatusText = "Ready";
        }
    }

    [RelayCommand]
    private void CopyEmail(EmailAccount account)
    {
        if (account == null) return;
        StatusText = $"Email copied: {account.Email}";
    }

    [RelayCommand]
    private void DeleteAccount(EmailAccount account)
    {
        if (account == null) return;
        Accounts.Remove(account);
        if (SelectedAccount == account)
        {
            SelectedAccount = null;
            Emails.Clear();
        }
        StatusText = $"Account {account.Email} deleted";
    }

    [RelayCommand]
    private async Task RefreshAccount(EmailAccount account)
    {
        if (account == null) return;
        StatusText = $"Refreshing {account.Email}...";
        IsLoading = true;
        await Task.Delay(2000);
        if (SelectedAccount == account)
        {
            LoadSampleEmails();
        }
        IsLoading = false;
        StatusText = "Ready";
    }

    [RelayCommand]
    private async Task RefreshAll()
    {
        StatusText = "Refreshing all accounts...";
        IsLoading = true;
        await Task.Delay(3000);
        if (SelectedAccount != null)
        {
            LoadSampleEmails();
        }
        IsLoading = false;
        StatusText = "Ready";
    }

    partial void OnSelectedAccountChanged(EmailAccount? value)
    {
        Emails.Clear();
        SelectedEmail = null;
        OnPropertyChanged(nameof(HasSelectedAccount));
        OnPropertyChanged(nameof(HasNoSelectedAccount));
        if (value != null)
        {
            StatusText = $"Loading emails for {value.Email}...";
            LoadSampleEmails();
            StatusText = "Ready";
        }
    }

    partial void OnSelectedEmailChanged(EmailMessage? value)
    {
        OnPropertyChanged(nameof(HasSelectedEmail));
    }

    partial void OnSearchTextChanged(string value)
    {
    }

    private void LoadSampleEmails()
    {
        Emails.Add(new EmailMessage
        {
            Id = "1",
            Subject = "Welcome to OutlookApp",
            From = "support@outlook.com",
            To = SelectedAccount?.Email ?? "",
            Body = "Hello,\n\nThank you for using OutlookApp. This is a sample email to demonstrate the UI layout.\n\nBest regards,\nOutlookApp Team",
            BodyPreview = "Thank you for using OutlookApp. This is a sample email...",
            ReceivedTime = DateTime.Now.AddHours(-1),
            HasAttachments = false,
            IsRead = false
        });
        Emails.Add(new EmailMessage
        {
            Id = "2",
            Subject = "Meeting: Project Review",
            From = "manager@company.com",
            To = SelectedAccount?.Email ?? "",
            Body = "Hi team,\n\nWe have a project review meeting scheduled for tomorrow at 10:00 AM.\n\nPlease come prepared with your updates.\n\nRegards,\nManager",
            BodyPreview = "We have a project review meeting scheduled for tomorrow...",
            ReceivedTime = DateTime.Now.AddHours(-3),
            HasAttachments = true,
            IsRead = true
        });
        Emails.Add(new EmailMessage
        {
            Id = "3",
            Subject = "Quarterly Report",
            From = "reports@company.com",
            To = SelectedAccount?.Email ?? "",
            Body = "Attached is the quarterly report for Q1 2026.\n\nPlease review and provide feedback by end of week.",
            BodyPreview = "Attached is the quarterly report for Q1 2026...",
            ReceivedTime = DateTime.Now.AddDays(-1),
            HasAttachments = true,
            IsRead = false
        });
    }
}
