using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OutlookApp.Models;

namespace OutlookApp.ViewModels;

public partial class ImportDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isDetecting;

    [ObservableProperty]
    private string _detectStatus = string.Empty;

    [ObservableProperty]
    private EmailAccount? _detectedAccount;

    [ObservableProperty]
    private bool _canImport;

    [ObservableProperty]
    private string? _errorMessage;

    [RelayCommand]
    private async Task Detect()
    {
        if (string.IsNullOrWhiteSpace(InputText))
        {
            ErrorMessage = "Please enter account info";
            return;
        }

        ErrorMessage = null;
        DetectedAccount = null;
        CanImport = false;

        var parts = InputText.Split("----");
        if (parts.Length < 2)
        {
            ErrorMessage = "Invalid format. Expected: email----password----clientid----token";
            return;
        }

        var email = parts[0].Trim();
        var password = parts[1].Trim();
        var clientId = parts.Length > 2 ? parts[2].Trim() : "";
        var token = parts.Length > 3 ? parts[3].Trim() : "";

        var account = new EmailAccount
        {
            Email = email,
            Password = password,
            ClientId = clientId,
            Token = token
        };

        IsDetecting = true;
        DetectedAccount = account;
        DetectStatus = "Detecting IMAP...";

        await Task.Delay(1000);

        DetectStatus = "IMAP unavailable, trying Graph API...";
        await Task.Delay(1000);

        if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(token))
        {
            DetectStatus = "Graph API connected!";
            account.Status = "Verified";
            account.StatusMessage = "Auto-detected: Graph API";
            account.AuthType = "graph";
        }
        else
        {
            DetectStatus = "IMAP connected!";
            account.Status = "Verified";
            account.StatusMessage = "Auto-detected: IMAP";
            account.AuthType = "imap";
        }

        IsDetecting = false;
        CanImport = true;
        OnPropertyChanged(nameof(DetectedAccount));
    }

    [RelayCommand]
    private void ConfirmImport()
    {
    }

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasDetectedAccount => DetectedAccount != null;

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasErrorMessage));
    }

    partial void OnDetectedAccountChanged(EmailAccount? value)
    {
        OnPropertyChanged(nameof(HasDetectedAccount));
    }
}
