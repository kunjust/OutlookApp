using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OutlookApp.Models;
using OutlookApp.Services;

namespace OutlookApp.ViewModels;

public partial class ImportDialogViewModel : ViewModelBase
{
    private readonly AuthDetectService _detector;

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

    [ObservableProperty]
    private ObservableCollection<DetectLog> _detectLogs = new();

    public ImportDialogViewModel()
    {
        _detector = new AuthDetectService();
    }

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasDetectedAccount => DetectedAccount != null;
    public bool HasDetectLogs => DetectLogs.Count > 0;

    [RelayCommand]
    private async Task Detect()
    {
        if (string.IsNullOrWhiteSpace(InputText))
        {
            ErrorMessage = "请输入账号信息";
            return;
        }

        ErrorMessage = null;
        DetectedAccount = null;
        CanImport = false;
        DetectLogs.Clear();

        var parts = InputText.Split("----");
        if (parts.Length < 2)
        {
            ErrorMessage = "格式错误，请使用: 邮箱----密码----clientid----token";
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
        CanImport = false;

        var result = await _detector.DetectAsync(account);

        foreach (var log in result.LogMessages)
            DetectLogs.Add(log);

        account.Status = result.Success ? "Verified" : "Failed";
        account.StatusMessage = result.StatusMessage;
        account.AuthType = result.AuthType;

        IsDetecting = false;
        CanImport = result.Success;
        DetectStatus = result.StatusMessage;
        OnPropertyChanged(nameof(DetectedAccount));
        OnPropertyChanged(nameof(HasDetectLogs));
    }

    [RelayCommand]
    private void ConfirmImport()
    {
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasErrorMessage));
    }

    partial void OnDetectedAccountChanged(EmailAccount? value)
    {
        OnPropertyChanged(nameof(HasDetectedAccount));
    }
}
