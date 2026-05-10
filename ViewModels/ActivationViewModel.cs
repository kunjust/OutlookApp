using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OutlookApp.Models;
using OutlookApp.Services;
using OutlookApp.Views;

namespace OutlookApp.ViewModels;

/// <summary>
/// 激活窗口 ViewModel。用户输入卡密，调 Activate API 激活。
/// 激活成功后触发事件，由 App.axaml.cs 接管进入主窗口。
/// </summary>
public partial class ActivationViewModel : ViewModelBase
{
    private readonly LicenseService _licenseService;
    private readonly LicenseStorageService _storage;

    [ObservableProperty]
    private string _cardKey = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _hasMessage;

    /// <summary>
    /// 激活成功时触发，传递 LicenseInfo
    /// </summary>
    public event Action<LicenseInfo>? ActivationSucceeded;

    public ActivationViewModel()
        : this(new LicenseService(), new LicenseStorageService())
    {
    }

    public ActivationViewModel(LicenseService licenseService, LicenseStorageService storage)
    {
        _licenseService = licenseService;
        _storage = storage;
    }

    /// <summary>
    /// 是否可以激活（卡密不为空且未在加载）
    /// </summary>
    public bool CanActivate => !string.IsNullOrWhiteSpace(CardKey) && !IsLoading;

    partial void OnCardKeyChanged(string value)
    {
        OnPropertyChanged(nameof(CanActivate));
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanActivate));
    }

    /// <summary>
    /// 消息颜色
    /// </summary>
    public IBrush MessageColor => IsError
        ? new SolidColorBrush(Color.Parse("#F85149"))
        : new SolidColorBrush(Color.Parse("#3FB950"));

    [RelayCommand]
    private async Task Activate()
    {
        var key = CardKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            ShowError("请输入卡密");
            return;
        }

        IsLoading = true;
        HasMessage = false;
        StatusMessage = string.Empty;

        try
        {
            var result = await _licenseService.ActivateAsync(key);

            var license = new LicenseInfo
            {
                CardKey = key,
                DeviceId = HardwareService.GetDeviceId(),
                HardwareId = HardwareService.GetHardwareId(),
                ExpiryTime = result.ExpiryTime,
                ServerTime = result.ServerTime,
                ActivatedAt = result.ServerTime,
                LastVerifiedAt = DateTime.UtcNow
            };

            // 保存到本地缓存
            await _storage.SaveAsync(license);

            IsLoading = false;

            // 通知 App 进入主窗口
            ActivationSucceeded?.Invoke(license);
        }
        catch (LicenseException ex)
        {
            IsLoading = false;
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            IsLoading = false;
            Console.WriteLine($"[Activation ERROR] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[Activation STACK] {ex.StackTrace}");
            if (ex.InnerException != null)
                Console.WriteLine($"[Activation INNER] {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            ShowError($"网络错误: {ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        IsError = true;
        StatusMessage = message;
        HasMessage = true;
    }

    private void ShowSuccess(string message)
    {
        IsError = false;
        StatusMessage = message;
        HasMessage = true;
    }
}
