using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OutlookApp.Models;

namespace OutlookApp.Services;

/// <summary>
/// 邮箱协议自动检测服务，依次尝试 XOAUTH2 和密码认证
/// </summary>
public class AuthDetectService
{
    private readonly ImapEmailService _imap = new();
    private readonly GraphEmailService _graph = new();

    public AuthDetectService()
    {
        _imap = ImapEmailService.Create();
        _graph = new GraphEmailService();
    }

    /// <summary>
    /// 自动检测邮箱可用协议，依次尝试 密码认证 → Token刷新 → XOAUTH2
    /// 只要任何一个步骤成功即判定为可用
    /// </summary>
    public async Task<DetectionResult> DetectAsync(EmailAccount account)
    {
        var result = new DetectionResult();
        var logs = new List<DetectLog>();

        // ========== 1. IMAP + 密码 ==========
        if (!string.IsNullOrEmpty(account.Password))
        {
            logs.Add(new DetectLog { Protocol = "IMAP (密码)", IsTesting = true });
            await Task.Delay(300);
            try
            {
                var ok = await _imap.VerifyAsync(account);
                logs[^1] = new DetectLog { Protocol = "IMAP (密码)", Success = ok, Message = ok ? "连接成功" : "密码认证失败" };
                if (ok) return MakeResult(result, logs, "imap", "✅ IMAP (密码)");
            }
            catch (Exception ex)
            {
                logs[^1] = new DetectLog { Protocol = "IMAP (密码)", Success = false, Message = $"异常: {ex.Message}" };
            }
        }

        // ========== 2. Token 刷新（刷新成功即可视为账号有效）==========
        if (!string.IsNullOrEmpty(account.Token) && !string.IsNullOrEmpty(account.ClientId))
        {
            logs.Add(new DetectLog { Protocol = "刷新 Token", IsTesting = true });
            await Task.Delay(30);
            string? accessToken = null;
            try
            {
                accessToken = await _graph.RefreshTokenAsync(account);
                logs[^1] = new DetectLog { Protocol = "刷新 Token", Success = accessToken != null, Message = accessToken != null ? "Token 刷新成功" : "Token 刷新失败" };
            }
            catch (Exception ex)
            {
                logs[^1] = new DetectLog { Protocol = "刷新 Token", Success = false, Message = $"异常: {ex.Message}" };
            }

            // Token 刷新成功 = 账号有效，直接通过
            if (accessToken != null)
            {
                // 尝试 XOAUTH2（如果失败也不影响检测结果）
                logs.Add(new DetectLog { Protocol = "IMAP XOAUTH2", IsTesting = true });
                await Task.Delay(300);
                try
                {
                    var ok = await _imap.VerifyXoauth2Async(account.Email, accessToken);
                    logs[^1] = new DetectLog { Protocol = "IMAP XOAUTH2", Success = ok, Message = ok ? "连接成功" : "跳过（不影响检测结果）" };
                }
                catch
                {
                    logs[^1] = new DetectLog { Protocol = "IMAP XOAUTH2", Success = false, Message = "跳过（不影响检测结果）" };
                }
                return MakeResult(result, logs, "imap", "✅ Token 刷新 — 账号有效");
            }
        }

        result.LogMessages = logs;
        result.Success = false;
        result.StatusMessage = "❌ 所有协议均无法连接";
        return result;
    }

    private DetectionResult MakeResult(DetectionResult r, List<DetectLog> logs, string authType, string msg)
    {
        r.LogMessages = logs;
        r.Success = true;
        r.AuthType = authType;
        r.StatusMessage = msg;
        return r;
    }
}

/// <summary>
/// 检测日志条目，记录每个协议的检测过程和结果
/// </summary>
public class DetectLog
{
    public string Protocol { get; set; } = "";
    public bool IsTesting { get; set; }
    public bool IsTestingDone => !IsTesting;
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>
/// 协议检测结果，包含成功状态、选中协议和完整日志
/// </summary>
public class DetectionResult
{
    public bool Success { get; set; }
    public string AuthType { get; set; } = "";
    public string StatusMessage { get; set; } = "";
    public List<DetectLog> LogMessages { get; set; } = new();
}
