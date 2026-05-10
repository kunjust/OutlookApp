using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using OutlookApp.Models;

namespace OutlookApp.Services;

/// <summary>
/// 卡密本地缓存服务。将 LicenseInfo 加密后存储到隐藏文件，
/// 下次启动时优先读取本地缓存，再通过远程 Verify 校验。
/// </summary>
public class LicenseStorageService
{
    private static readonly string StorageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".outlookapp");

    private static readonly string StorageFile = Path.Combine(StorageDir, "license.dat");

    /// <summary>
    /// 将卡密信息加密保存到本地隐藏文件
    /// </summary>
    public async Task SaveAsync(LicenseInfo license)
    {
        if (license == null)
            throw new ArgumentNullException(nameof(license));

        Directory.CreateDirectory(StorageDir);

        var json = license.ToJson();
        var encrypted = EncryptionService.Encrypt(json);
        await File.WriteAllTextAsync(StorageFile, encrypted);

        // macOS/Linux：确保目录和文件隐藏（以 . 开头即为隐藏）
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            // 目录已经以 . 开头，macOS Finder 中自动隐藏
        }
    }

    /// <summary>
    /// 从本地隐藏文件读取并解密卡密信息。文件不存在或解密失败时返回 null。
    /// </summary>
    public async Task<LicenseInfo?> LoadAsync()
    {
        if (!File.Exists(StorageFile))
            return null;

        try
        {
            var encrypted = await File.ReadAllTextAsync(StorageFile);
            if (string.IsNullOrEmpty(encrypted))
                return null;

            var json = EncryptionService.Decrypt(encrypted);
            return LicenseInfo.FromJson(json);
        }
        catch
        {
            // 解密失败或数据损坏，清除文件
            await ClearAsync();
            return null;
        }
    }

    /// <summary>
    /// 删除本地缓存文件
    /// </summary>
    public Task ClearAsync()
    {
        if (File.Exists(StorageFile))
            File.Delete(StorageFile);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 检查本地缓存是否存在
    /// </summary>
    public bool Exists()
    {
        return File.Exists(StorageFile);
    }
}
