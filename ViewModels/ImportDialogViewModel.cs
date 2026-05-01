using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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

    public event Func<Task<string?>>? FilePicked;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isDetecting;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private ObservableCollection<AccountDetectResult> _accountResults = new();

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _successCount;

    [ObservableProperty]
    private int _currentIndex;

    public ImportDialogViewModel()
    {
        _detector = new AuthDetectService();
    }

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    [RelayCommand]
    private async Task SelectFile()
    {
        if (FilePicked == null) return;
        var text = await FilePicked.Invoke();
        if (!string.IsNullOrEmpty(text))
            InputText = text;
    }

    [RelayCommand]
    private async Task BatchDetect()
    {
        if (string.IsNullOrWhiteSpace(InputText))
        {
            ErrorMessage = "请粘贴账号信息或选择文件";
            return;
        }

        ErrorMessage = null;
        AccountResults.Clear();
        HasResults = false;

        var lines = InputText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var accounts = new List<EmailAccount>();

        foreach (var line in lines)
        {
            var parts = line.Split("----");
            if (parts.Length < 2) continue;
            accounts.Add(new EmailAccount
            {
                Email = parts[0].Trim(),
                Password = parts[1].Trim(),
                ClientId = parts.Length > 2 ? parts[2].Trim() : "",
                Token = parts.Length > 3 ? parts[3].Trim() : ""
            });
        }

        if (accounts.Count == 0)
        {
            ErrorMessage = "未解析到有效账号，每行格式: 邮箱----密码----clientid----token";
            return;
        }

        TotalCount = accounts.Count;
        IsDetecting = true;
        var success = 0;

        for (int i = 0; i < accounts.Count; i++)
        {
            CurrentIndex = i + 1;
            var acct = accounts[i];

            var result = new AccountDetectResult
            {
                Email = acct.Email,
                Account = acct,
                Status = "检测中…"
            };
            AccountResults.Add(result);

            var detection = await _detector.DetectAsync(acct);
            acct.Status = detection.Success ? "Verified" : "Failed";
            acct.StatusMessage = detection.StatusMessage;
            acct.AuthType = detection.AuthType;

            result.Status = detection.Success ? "✅ 通过" : "❌ 失败";
            result.StatusMessage = detection.StatusMessage;
            result.Success = detection.Success;
            result.Account = acct;

            if (detection.Success) success++;
        }

        SuccessCount = success;
        HasResults = true;
        IsDetecting = false;
    }

    [RelayCommand]
    private void ConfirmImport()
    {
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasErrorMessage));
    }
}

public class AccountDetectResult
{
    public string Email { get; set; } = "";
    public EmailAccount? Account { get; set; }
    public bool Success { get; set; }
    public bool IsFailed => !Success;
    public string Status { get; set; } = "待检测";
    public string StatusMessage { get; set; } = "";
    public string Icon => Success ? "✓" : "✗";
    public string IconColor => Success ? "#3FB950" : "#F85149";
}
