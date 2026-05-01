using System;
using System.Security.Cryptography;
using System.Text;

namespace OutlookApp.Services;

public static class EncryptionService
{
    private static readonly byte[] Key = DeriveKey();

    private static byte[] DeriveKey()
    {
        var machineName = Environment.MachineName;
        var userDomain = Environment.UserDomainName ?? "";
        var userName = Environment.UserName ?? "";
        var raw = Encoding.UTF8.GetBytes(machineName + userDomain + userName + "OutlookAppV1");
        return SHA256.HashData(raw);
    }

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(Key, 16);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        var result = new byte[12 + cipherBytes.Length + 16];
        Buffer.BlockCopy(nonce, 0, result, 0, 12);
        Buffer.BlockCopy(cipherBytes, 0, result, 12, cipherBytes.Length);
        Buffer.BlockCopy(tag, 0, result, 12 + cipherBytes.Length, 16);
        return Convert.ToBase64String(result);
    }

    public static string Decrypt(string encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText)) return string.Empty;
        var data = Convert.FromBase64String(encryptedText);
        var nonce = data[..12];
        var cipherBytes = data[12..^16];
        var tag = data[^16..];
        var plainBytes = new byte[cipherBytes.Length];
        using var aes = new AesGcm(Key, 16);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
