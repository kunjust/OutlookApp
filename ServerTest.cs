// 卡密 API 签名诊断工具
// 编译后运行：dotnet run
// 会输出多种签名算法结果并依次测试 API，找到能用的

using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OutlookApp.Services;
using OutlookApp.Models;

namespace OutlookApp;

public static class SignatureTest
{
    public static async Task RunAsync(string cardKey)
    {
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine("  卡密 API 签名诊断工具");
        Console.WriteLine($"  卡密: {cardKey}");
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine();

        var deviceId = HardwareService.GetDeviceId();
        var bodyObj = new { cardKey, deviceId, hardwareId = deviceId, osPlatform = HardwareService.GetOsPlatform() };
        var body = JsonSerializer.Serialize(bodyObj);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Console.WriteLine($"Body:     {body}");
        Console.WriteLine($"TS:       {ts}");
        Console.WriteLine($"DeviceId: {deviceId}");
        Console.WriteLine();

        var productKey = ApiSecurityService.ProductKey;
        Console.WriteLine($"── 产品密钥: \"{productKey}\" ──");

        // 正确算法：SHA256(body|key|ts)
        var correctSig = HashHelper.Sha256($"{body}|{productKey}|{ts}");
        Console.WriteLine($"  SHA256(body|key|ts): {correctSig}");
        Console.WriteLine();
        Console.WriteLine("═══ API 测试 ═══");
        Console.WriteLine();

        using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5001") };
        var nonce = Guid.NewGuid().ToString("D");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/Auth/Activate")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Timestamp", ts.ToString());
        request.Headers.Add("X-Nonce", nonce);
        request.Headers.Add("X-Signature", correctSig);

        try
        {
            var resp = await client.SendAsync(request);
            var respBody = await resp.Content.ReadAsStringAsync();
            var status = resp.IsSuccessStatusCode ? "✅" : "❌";

            if (resp.IsSuccessStatusCode)
            {
                var decrypted = ApiSecurityService.DecryptResponse(respBody);
                Console.WriteLine($"{status} HTTP {(int)resp.StatusCode}: {decrypted}");
            }
            else
            {
                Console.WriteLine($"{status} HTTP {(int)resp.StatusCode}: {respBody}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 ERROR: {ex.Message}");
        }
    }
}

internal static class HashHelper
{
    public static string Sha256(string message)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(message))).ToLowerInvariant();
    }
}
