using System;
using System.Security.Cryptography;
using System.Text;

namespace OutlookApp.Services;

/// <summary>
/// 卡密 API 安全工具：SHA256 签名生成 + AES-CBC 响应解密。
/// 产品密钥由接口分配，代码中写死后可通过更新常量替换。
///
/// 签名算法（来自服务端 SignatureHelper）：
///   SHA256(body + "|" + apiResponseKey + "|" + timestamp)
///   注意：是 SHA256，不是 HMAC-SHA256，且使用 "|" 分隔。
/// 
/// 响应加密（来自服务端 EncryptionHelper）：
///   AES-CBC, Key=SHA256(ApiResponseKey), IV=密文前16字节, PKCS7 填充
/// </summary>
public static class ApiSecurityService
{
    /// <summary>
    /// 产品密钥（ApiResponseKey），默认值来自服务端 appsettings。
    /// 可通过环境变量 App__Security__ApiResponseKey 覆盖。
    /// </summary>
    public const string ProductKey = "ChangeThisToARandom32CharKey!!!";

    /// <summary>
    /// 卡密 API 服务端地址（正式环境）
    /// </summary>
    public const string ServerBase = "http://localhost:5001";

    /// <summary>
    /// 生成签名（与服务端 SignatureHelper 一致）。
    /// 算法：SHA256(body + "|" + key + "|" + timestamp)
    /// 输出 64 位小写 hex 字符串。
    /// </summary>
    public static string GenerateSignature(string body, long timestamp, string? productKey = null)
    {
        var key = productKey ?? ProductKey;
        // 服务端代码：$"{body}|{secretKey}|{timestamp}"
        var data = $"{body}|{key}|{timestamp}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 生成随机 Nonce（防重放攻击）
    /// </summary>
    public static string GenerateNonce() => Guid.NewGuid().ToString("D");

    /// <summary>
    /// 获取当前 Unix 时间戳（秒）
    /// </summary>
    public static long GetTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>
    /// 解密 AES-CBC 加密的 API 响应。
    /// Key = SHA256(UTF8(productKey))
    /// IV  = Base64 密文前 16 字节
    /// Padding = PKCS7
    /// </summary>
    /// <param name="encryptedBase64">服务端返回的 AES 加密 Base64 字符串</param>
    /// <param name="productKey">产品密钥（解密密钥由 productKey 派生）</param>
    /// <returns>解密后的 UTF8 文本（JSON）</returns>
    public static string DecryptResponse(string encryptedBase64, string? productKey = null)
    {
        if (string.IsNullOrEmpty(encryptedBase64))
            return string.Empty;

        var key = productKey ?? ProductKey;
        var cipherData = Convert.FromBase64String(encryptedBase64);

        // IV = 前 16 字节
        var iv = new byte[16];
        Buffer.BlockCopy(cipherData, 0, iv, 0, 16);

        // 剩余部分为密文
        var cipherBytes = new byte[cipherData.Length - 16];
        Buffer.BlockCopy(cipherData, 16, cipherBytes, 0, cipherBytes.Length);

        // Key = SHA256(UTF8(productKey))
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));

        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
