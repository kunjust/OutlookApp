using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OutlookApp.Api;
using OutlookApp.Models;
using OutlookApp.Services;
using OutlookApp.ViewModels;
using OutlookApp.Views;

namespace OutlookApp;

/// <summary>
/// Avalonia 应用程序入口，管理启动流程、激活校验和主窗口生命周期
/// </summary>
public partial class App : Application
{
    public static Window? MainWindow { get; private set; }
    public static HttpServer? HttpServer { get; private set; }
    public static DatabaseService? DatabaseService { get; private set; }
    public static KeywordService? KeywordService { get; private set; }
    public static MainWindowViewModel? MainVm { get; private set; }

    private readonly LicenseStorageService _licenseStorage = new();
    private readonly LicenseService _licenseService = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 初始化基础服务
            DatabaseService = new DatabaseService();
            KeywordService = new KeywordService(DatabaseService.ConnectionString);

            // 尝试从本地缓存加载卡密信息
            var cachedLicense = Task.Run(async () => await _licenseStorage.LoadAsync()).GetAwaiter().GetResult();

            if (cachedLicense != null)
            {
                // 有缓存 → 先尝试调服务端更新
                var serverOk = Task.Run(async () =>
                {
                    try
                    {
                        var result = await _licenseService.VerifyAsync(cachedLicense.CardKey);
                        if (result.Valid)
                        {
                            cachedLicense.UpdateServerTime(result.ServerTime);
                            cachedLicense.ExpiryTime = result.ExpiryTime;
                            cachedLicense.LastVerifiedAt = DateTime.UtcNow;
                            await _licenseStorage.SaveAsync(cachedLicense);
                            return true;
                        }
                        // 服务端返回无效（过期/撤销）
                        return false;
                    }
                    catch
                    {
                        // 服务端连不上 → 不允许进入主窗口
                        return false;
                    }
                }).GetAwaiter().GetResult();

                if (serverOk)
                {
                    EnterMainWindow(desktop, cachedLicense);
                    return;
                }

                // 服务端和本地都无效 → 清除缓存，记住卡密
                var oldCardKey = cachedLicense.CardKey;
                Task.Run(async () => await _licenseStorage.ClearAsync()).GetAwaiter().GetResult();
                ShowActivationWindow(desktop, oldCardKey);
                return;
            }

            // 无缓存 → 显示激活窗口
            ShowActivationWindow(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void EnterMainWindow(IClassicDesktopStyleApplicationLifetime desktop, LicenseInfo license)
    {
        MainVm = new MainWindowViewModel(DatabaseService!, KeywordService!);
        MainVm.InitializeLicense(license);

        MainWindow = new MainWindow
        {
            DataContext = MainVm,
        };
        desktop.MainWindow = MainWindow;

        try
        {
            HttpServer = new HttpServer(5000, DatabaseService!, KeywordService!, MainVm);
            HttpServer.Start();

            // 尝试在 Windows 上自动注册防火墙规则（管理员权限才会成功，不影响应用）
            if (WindowsNetworkHelper.IsWindows)
            {
                WindowsNetworkHelper.TryRegisterFirewallRule(5000);
            }

            // 给 UI 显示所有可用的局域网访问地址
            var urls = HttpServer.BoundUrls;
            if (urls.Count == 0)
            {
                MainVm.StatusText = "HTTP API 已启动，但未能枚举到访问地址";
            }
            else if (urls.Count == 1)
            {
                MainVm.StatusText = $"HTTP API 已启动: {urls[0]}";
            }
            else
            {
                MainVm.StatusText = $"HTTP API 已启动，共 {urls.Count} 个地址可访问: {string.Join(" | ", urls)}";
            }
            Console.WriteLine($"[HttpServer] 监听地址列表 ({urls.Count}):");
            foreach (var u in urls)
                Console.WriteLine($"  - {u}");
        }
        catch (Exception ex)
        {
            // 关键失败要让用户看到，而不是吞掉
            MainVm.StatusText = $"❌ HTTP API 启动失败: {ex.GetType().Name} - {ex.Message}。"
                              + $"请用管理员权限执行: {WindowsNetworkHelper.GetManualSetupCommands(5000)}";
            Console.WriteLine($"[HttpServer] 启动失败: {ex}");
        }

        MainWindow.Show();

        // 启动后检查更新
        await CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            var updateSvc = new UpdateService();
            var release = await updateSvc.CheckAsync();
            if (release != null && UpdateService.IsNewer(release.Version))
            {
                await MainVm!.ShowUpdateDialog(release);
            }
        }
        catch { }
    }

    private void ShowActivationWindow(IClassicDesktopStyleApplicationLifetime desktop, string? prefillCardKey = null)
    {
        var activationVm = new ActivationViewModel(_licenseService, _licenseStorage);
        if (!string.IsNullOrEmpty(prefillCardKey))
            activationVm.CardKey = prefillCardKey;
        var activationWindow = new ActivationWindow
        {
            DataContext = activationVm
        };

        activationVm.ActivationSucceeded += license =>
        {
            // 1. 先隐藏激活窗口（避免闪烁）
            activationWindow.Hide();
            // 2. 创建主窗口并设为 desktop.MainWindow（防止关闭激活窗口触发退出）
            EnterMainWindow(desktop, license);
            // 3. 最后关掉激活窗口
            activationWindow.Close();
        };

        desktop.MainWindow = activationWindow;
        activationWindow.Show();
    }

}
