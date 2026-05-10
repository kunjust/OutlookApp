using Avalonia;
using System;
using System.Threading.Tasks;

namespace OutlookApp;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--test-signature")
        {
            var cardKey = args.Length > 1 ? args[1] : "BBAFB1544A417F05";
            Task.Run(async () => await SignatureTest.RunAsync(cardKey))
                .GetAwaiter().GetResult();
            return;
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
