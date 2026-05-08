using System;
using System.Linq;
using OutlookApp.Api;
using OutlookApp.Services;

namespace OutlookApp;

public class ServerTest
{
    public static void Run(int port = 5000)
    {
        Console.WriteLine("Starting HTTP server on port " + port + "...");
        var db = new DatabaseService();
        Console.WriteLine("Database initialized.");

        var accounts = db.GetAccounts();
        Console.WriteLine($"Found {accounts.Count} accounts.");
        Console.WriteLine($"Verified accounts: {accounts.Count(a => a.Status == "Verified")}");

        var server = new HttpServer(port, db, true);
        server.Start();

        Console.WriteLine($"Server running: http://127.0.0.1:{port}");
        Console.WriteLine();
        Console.WriteLine("Endpoints:");
        Console.WriteLine("  GET /api/email        - Allocate an email");
        Console.WriteLine("  GET /api/code?email=  - Get verification code");
        Console.WriteLine("  GET /api/status?email= - Check email status");
        Console.WriteLine();
        Console.WriteLine("Press Ctrl+C to stop.");

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            server.StopAsync().Wait();
            Console.WriteLine("\nServer stopped.");
            Environment.Exit(0);
        };

        while (true)
        {
            Console.ReadLine();
        }
    }
}
