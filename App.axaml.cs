using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using OutlookApp.Api;
using OutlookApp.Services;
using OutlookApp.ViewModels;
using OutlookApp.Views;

namespace OutlookApp;

public partial class App : Application
{
    public static Window MainWindow { get; private set; } = null!;
    public static HttpServer? HttpServer { get; private set; }
    public static DatabaseService? DatabaseService { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DatabaseService = new DatabaseService();
            var vm = new MainWindowViewModel(DatabaseService);
            MainWindow = new MainWindow
            {
                DataContext = vm,
            };
            desktop.MainWindow = MainWindow;

            try
            {
                HttpServer = new HttpServer(5000, DatabaseService, vm);
                HttpServer.Start();
                vm.StatusText = $"HTTP API 已启动: http://{GetLocalIPAddress()}:5000";
            }
            catch (Exception ex)
            {
                vm.StatusText = $"HTTP API 启动失败: {ex.Message}";
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string GetLocalIPAddress()
    {
        var host = System.Net.Dns.GetHostName().Replace(".local", "");
        try
        {
            var addresses = System.Net.Dns.GetHostAddresses(host);
            foreach (var addr in addresses)
            {
                if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return addr.ToString();
                }
            }
        }
        catch { }
        return "127.0.0.1";
    }
}