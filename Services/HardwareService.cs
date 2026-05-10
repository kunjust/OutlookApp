using System;
using System.Linq;
using System.Net.NetworkInformation;

namespace OutlookApp.Services;

/// <summary>
/// 硬件指纹服务。当前仅使用 MAC 地址作为设备唯一标识。
/// </summary>
public static class HardwareService
{
    private static string? _cachedMac;

    /// <summary>
    /// 获取设备唯一标识（MAC 地址，大写格式 XX-XX-XX-XX-XX-XX）
    /// </summary>
    public static string GetDeviceId()
    {
        if (_cachedMac != null)
            return _cachedMac;

        _cachedMac = GetMacAddress();
        return _cachedMac;
    }

    /// <summary>
    /// 获取硬件指纹（与 GetDeviceId 相同，均使用 MAC 地址）
    /// </summary>
    public static string GetHardwareId() => GetDeviceId();

    /// <summary>
    /// 获取操作系统平台描述
    /// </summary>
    public static string GetOsPlatform()
    {
        if (OperatingSystem.IsMacOS())
            return "macOS";
        if (OperatingSystem.IsWindows())
            return "Windows";
        if (OperatingSystem.IsLinux())
            return "Linux";
        return "Unknown";
    }

    private static string GetMacAddress()
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                         && n.OperationalStatus == OperationalStatus.Up
                         && !string.IsNullOrEmpty(n.GetPhysicalAddress().ToString()))
                .FirstOrDefault();

            if (nic != null)
            {
                var mac = nic.GetPhysicalAddress();
                if (mac != null && mac.GetAddressBytes().Length > 0)
                    return string.Join("-", mac.GetAddressBytes().Select(b => b.ToString("X2")));
            }
        }
        catch
        {
            // 如果获取失败，回退到备用标识
        }

        // 备用方案：使用 MachineName（MAC 获取失败时的 fallback）
        return Environment.MachineName.Replace(" ", "-");
    }
}
