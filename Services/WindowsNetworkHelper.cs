using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace OutlookApp.Services;

/// <summary>
/// 网络相关工具：
///   - 枚举所有可用的局域网 IPv4 地址（多网卡场景下不再只取第一个）
///   - 在 Windows 上尝试注册 HttpListener URL ACL + 防火墙入站规则
///
/// 所有 Windows 专属操作在非 Windows 平台都是 no-op。
/// </summary>
public static class WindowsNetworkHelper
{
    /// <summary>
    /// 枚举本机所有"活动且非回环"的 IPv4 地址。
    /// 多网卡（物理 + VMnet + Hyper-V + 雷电虚拟网卡）场景下会全部返回，由调用方决定如何展示。
    /// </summary>
    public static List<string> GetAllLanIPv4()
    {
        var ips = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var props = nic.GetIPProperties();
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;
                    if (IPAddress.IsLoopback(addr.Address))
                        continue;

                    var ip = addr.Address.ToString();
                    if (ip.StartsWith("169.254."))
                        continue; // APIPA 自动分配地址，没有实际意义

                    ips.Add(ip);
                }
            }
        }
        catch
        {
            // 任何枚举失败都不影响应用启动
        }

        return ips.Distinct().ToList();
    }

    /// <summary>
    /// 把"看起来最像主要局域网"的 IP 排在前面（192.168.* 优先，10.* 其次，其它最后）。
    /// </summary>
    public static List<string> GetSortedLanIPv4()
    {
        return GetAllLanIPv4()
            .OrderBy(ip => ip.StartsWith("192.168.") ? 0
                         : ip.StartsWith("10.") ? 1
                         : ip.StartsWith("172.") ? 2
                         : 3)
            .ThenBy(ip => ip)
            .ToList();
    }

    /// <summary>
    /// 当前是否运行在 Windows 上。
    /// </summary>
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// 在 Windows 上尝试为指定端口注册防火墙入站规则。
    /// 调用 netsh，需要管理员权限；不是管理员时静默失败（不影响应用启动）。
    /// </summary>
    /// <returns>是否成功注册（或规则已存在）</returns>
    public static bool TryRegisterFirewallRule(int port, string ruleName = "OutlookApp HTTP API")
    {
        if (!IsWindows) return false;

        try
        {
            // 先查规则是否已存在
            var checkResult = RunNetsh($"advfirewall firewall show rule name=\"{ruleName}\"");
            if (checkResult.ExitCode == 0 && checkResult.Output.Contains(ruleName))
                return true;

            // 不存在 → 尝试添加
            var addCmd = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={port}";
            var addResult = RunNetsh(addCmd);
            return addResult.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 生成"用户手动配置防火墙/URL ACL"的命令行提示。
    /// 用于在 StatusText 或日志中告诉用户：如果局域网仍然访问不到，请用管理员权限运行下面这两条命令。
    /// </summary>
    public static string GetManualSetupCommands(int port)
    {
        return $"netsh http add urlacl url=http://*:{port}/ user=Everyone && "
             + $"netsh advfirewall firewall add rule name=\"OutlookApp HTTP API\" dir=in action=allow protocol=TCP localport={port}";
    }

    private static (int ExitCode, string Output) RunNetsh(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return (-1, "");
            var output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
            proc.WaitForExit(5000);
            return (proc.ExitCode, output);
        }
        catch
        {
            return (-1, "");
        }
    }
}
