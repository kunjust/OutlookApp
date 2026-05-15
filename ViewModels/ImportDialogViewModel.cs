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

/// <summary>
/// 导入对话框 ViewModel，处理批量导入和格式解析
/// </summary>
public partial class ImportDialogViewModel : ViewModelBase
{
    public event Func<Task<string?>>? FilePicked;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    public List<EmailAccount> ParseAccounts()
    {
        var accounts = new List<EmailAccount>();
        if (string.IsNullOrWhiteSpace(InputText)) return accounts;

        var lines = InputText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
        return accounts;
    }

    [RelayCommand]
    private async Task SelectFile()
    {
        if (FilePicked == null) return;
        var text = await FilePicked.Invoke();
        if (!string.IsNullOrEmpty(text))
            InputText = text;
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasErrorMessage));
    }
}
