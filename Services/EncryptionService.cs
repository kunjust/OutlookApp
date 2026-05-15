using System;
using System.Security.Cryptography;
using System.Text;

namespace OutlookApp.Services;

/// <summary>
/// AES-256-GCM 加密/解密服务，用于本地缓存文件加密。
/// 密钥基于当前机器和用户信息派生，换机器后无法解密。
/// </summary>
public static class EncryptionService
{
    // 派生密钥：MachineName + UserDomainName + UserName
    private static readonly byte[] Key = DeriveKey();

    /// <summary>从机器名和用户名派生 AES 密钥</summary>
    private static byte[] DeriveKey()
    {
        var machineName = Environment.MachineName;
        var userDomain = Environment.UserDomainName ?? "";
        var userName = Environment.UserName ?? "";
        var raw = Encoding.UTF8.GetBytes(machineName + userDomain + userName + "OutlookAppV1");
        return SHA256.HashData(raw);
    }

    /// <summary>
    /// AES-256-GCM 加密
    /// 输出格式：Base64(nonce 12字节 + 密文 + tag 16字节)
    /// </summary>
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(12);    // 随机 nonce
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];                            // 认证标签
        using var aes = new AesGcm(Key, 16);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        // 拼接 nonce + 密文 + tag
        var result = new byte[12 + cipherBytes.Length + 16];
        Buffer.BlockCopy(nonce, 0, result, 0, 12);
        Buffer.BlockCopy(cipherBytes, 0, result, 12, cipherBytes.Length);
        Buffer.BlockCopy(tag, 0, result, 12 + cipherBytes.Length, 16);
        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// AES-256-GCM 解密
    /// 输入格式：Encrypt 输出的 Base64 字符串
    /// </summary>
    public static string Decrypt(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText)) return string.Empty;
        var data = Convert.FromBase64String(encryptedText);
        var nonce = data[..12];       // 前 12 字节 = nonce
        var cipherBytes = data[12..^16]; // 中间 = 密文
        var tag = data[^16..];        // 后 16 字节 = tag
        var plainBytes = new byte[cipherBytes.Length];
        using var aes = new AesGcm(Key, 16);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
