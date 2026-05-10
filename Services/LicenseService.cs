using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OutlookApp.Models;

namespace OutlookApp.Services;

/// <summary>
/// 卡密 API 调用封装。
/// 服务端统一响应格式：{"success":bool, "message":string, "data":加密Base64|null, "serverTime":long}
/// 仅 data 字段加密，外层为明文 JSON。
/// </summary>
public class LicenseService
{
    private readonly HttpClient _httpClient;
    private readonly LicenseStorageService _storage;

    public LicenseService(LicenseStorageService storage)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(ApiSecurityService.ServerBase)
        };
        _storage = storage;
    }

    public LicenseService() : this(new LicenseStorageService())
    {
    }

    #region API 调用方法

    public async Task<ActivationResult> ActivateAsync(string cardKey)
    {
        var body = new
        {
            cardKey,
            deviceId = HardwareService.GetDeviceId(),
            hardwareId = HardwareService.GetHardwareId(),
            osPlatform = HardwareService.GetOsPlatform()
        };

        var (data, serverTime) = await PostAsync("/api/v1/Auth/Activate", body);

        var expiredAt = DateTime.Parse(
            data!.Value.GetProperty("expiredAt").GetString()!,
            null, System.Globalization.DateTimeStyles.RoundtripKind);

        return new ActivationResult
        {
            ExpiryTime = expiredAt,
            ServerTime = DateTimeOffset.FromUnixTimeSeconds(serverTime).UtcDateTime,
            CardKey = cardKey,
            SessionToken = data.Value.GetProperty("sessionToken").GetString() ?? ""
        };
    }

    public async Task<VerifyResult> VerifyAsync(string cardKey)
    {
        var body = new
        {
            cardKey,
            deviceId = HardwareService.GetDeviceId(),
            hardwareId = HardwareService.GetHardwareId()
        };

        var (data, serverTime) = await PostAsync("/api/v1/Auth/Verify", body);
        if (data == null)
            return new VerifyResult { Valid = false, ServerTime = DateTime.UtcNow };

        var isValid = data.Value.GetProperty("isValid").GetBoolean();
        DateTime? expiredAt = null;
        var remainingDays = 0;

        if (data.Value.TryGetProperty("expiredAt", out var exp) && exp.ValueKind == JsonValueKind.String)
            expiredAt = DateTime.Parse(exp.GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind);
        if (data.Value.TryGetProperty("remainingDays", out var days))
            remainingDays = days.GetInt32();

        return new VerifyResult
        {
            Valid = isValid,
            ExpiryTime = expiredAt ?? DateTime.MinValue,
            ServerTime = DateTimeOffset.FromUnixTimeSeconds(serverTime).UtcDateTime
        };
    }

    public async Task<HeartbeatResult> HeartbeatAsync(string cardKey)
    {
        var body = new
        {
            cardKey,
            deviceId = HardwareService.GetDeviceId(),
            hardwareId = HardwareService.GetHardwareId()
        };

        var (data, serverTime) = await PostAsync("/api/v1/Auth/Heartbeat", body);

        return new HeartbeatResult
        {
            RemainingDays = data!.Value.GetProperty("remainingDays").GetInt32(),
            ServerTime = DateTimeOffset.FromUnixTimeSeconds(serverTime).UtcDateTime
        };
    }

    public async Task<bool> UnbindAsync(string cardKey, string? reason = null)
    {
        var body = new
        {
            cardKey,
            deviceId = HardwareService.GetDeviceId(),
            hardwareId = HardwareService.GetHardwareId(),
            reason
        };

        await PostAsync("/api/v1/Device/Unbind", body);
        return true;
    }

    public async Task<QueryResult> QueryAsync(string cardKey)
    {
        var timestamp = ApiSecurityService.GetTimestamp();
        var nonce = ApiSecurityService.GenerateNonce();
        var signature = ApiSecurityService.GenerateSignature("", timestamp);

        var url = $"/api/v1/Device/Query?cardKey={Uri.EscapeDataString(cardKey)}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Timestamp", timestamp.ToString());
        request.Headers.Add("X-Nonce", nonce);
        request.Headers.Add("X-Signature", signature);

        var (data, serverTime) = await SendAsync(request);

        DateTime? expiredAt = null;
        if (data.HasValue && data.Value.TryGetProperty("expiredAt", out var exp) && exp.ValueKind == JsonValueKind.String)
            expiredAt = DateTime.Parse(exp.GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind);

        return new QueryResult
        {
            CardKey = cardKey,
            Status = data?.GetProperty("status").GetString() ?? "",
            ExpiryTime = expiredAt ?? DateTime.MinValue,
            ServerTime = DateTimeOffset.FromUnixTimeSeconds(serverTime).UtcDateTime
        };
    }

    #endregion

    #region 内部方法

    /// <summary>
    /// POST 请求 → 返回 (解密后的 data 的 JsonElement, serverTime)
    /// </summary>
    private async Task<(JsonElement? data, long serverTime)> PostAsync(string path, object body)
    {
        var timestamp = ApiSecurityService.GetTimestamp();
        var nonce = ApiSecurityService.GenerateNonce();
        var jsonBody = JsonSerializer.Serialize(body);
        var signature = ApiSecurityService.GenerateSignature(jsonBody, timestamp);

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Timestamp", timestamp.ToString());
        request.Headers.Add("X-Nonce", nonce);
        request.Headers.Add("X-Signature", signature);

        return await SendAsync(request);
    }

    /// <summary>
    /// 发送请求 → 解析统一响应格式 → 解密 data 字段
    /// </summary>
    private async Task<(JsonElement? data, long serverTime)> SendAsync(HttpRequestMessage request)
    {
        var httpResponse = await _httpClient.SendAsync(request);
        var responseBody = await httpResponse.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        // 统一格式：{success, message, data, serverTime}
        var success = root.GetProperty("success").GetBoolean();
        var message = root.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "";
        var serverTime = root.TryGetProperty("serverTime", out var st) ? st.GetInt64() : 0;

        if (!success)
        {
            throw new LicenseException(string.IsNullOrEmpty(message) ? "请求失败" : message, (int)httpResponse.StatusCode);
        }

        // 解密 data 字段（仅 data 加密，外层明文）
        if (!root.TryGetProperty("data", out var dataProp) || dataProp.ValueKind != JsonValueKind.String)
            return (null, serverTime);

        var encryptedData = dataProp.GetString();
        if (string.IsNullOrEmpty(encryptedData))
            return (null, serverTime);

        var decrypted = ApiSecurityService.DecryptResponse(encryptedData);
        var dataDoc = JsonDocument.Parse(decrypted);

        // 处理服务端双重序列化：Encrypt(object) 会再次 Serialize 字符串
        JsonElement? result = null;
        if (dataDoc.RootElement.ValueKind == JsonValueKind.String)
        {
            var innerJson = dataDoc.RootElement.GetString();
            if (!string.IsNullOrEmpty(innerJson))
            {
                dataDoc.Dispose();
                dataDoc = JsonDocument.Parse(innerJson);
                result = dataDoc.RootElement.Clone();
            }
        }
        else
        {
            result = dataDoc.RootElement.Clone();
        }

        dataDoc.Dispose();
        doc.Dispose(); // dispose outer doc

        return (result, serverTime);
    }

    #endregion
}

#region API 返回结果类型

public class ActivationResult
{
    public DateTime ExpiryTime { get; init; }
    public DateTime ServerTime { get; init; }
    public string CardKey { get; init; } = "";
    public string SessionToken { get; init; } = "";
}

public class VerifyResult
{
    public bool Valid { get; init; }
    public DateTime ExpiryTime { get; init; }
    public DateTime ServerTime { get; init; }
}

public class HeartbeatResult
{
    public int RemainingDays { get; init; }
    public DateTime ServerTime { get; init; }
}

public class QueryResult
{
    public string CardKey { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTime ExpiryTime { get; init; }
    public DateTime ServerTime { get; init; }
}

public class LicenseException : Exception
{
    public int StatusCode { get; }
    public LicenseException(string message, int statusCode = 0) : base(message) => StatusCode = statusCode;
}

#endregion
