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
    private readonly ImapEmailService _imap;
    private readonly GraphEmailService _graph;

    /// <summary>
    /// 初始化检测服务，创建 IMAP 和 Graph API 客户端实例
    /// </summary>
    public AuthDetectService()
    {
        _imap = new ImapEmailService();
        _graph = new GraphEmailService();
    }

    /// <summary>
    /// 自动检测邮箱可用协议，依次尝试 XOAUTH2 → 密码认证
    /// </summary>
    /// <param name="account">邮箱账号信息</param>
    /// <returns>检测结果，包含成功状态和检测日志</returns>
    public async Task<DetectionResult> DetectAsync(EmailAccount account)
    {
        var result = new DetectionResult();
        var logs = new List<DetectLog>();

        // 1. Refresh Token  →  IMAP XOAUTH2（主路径，大部分账号走这个）
        if (!string.IsNullOrEmpty(account.Token) && !string.IsNullOrEmpty(account.ClientId))
        {
            logs.Add(new DetectLog { Protocol = "刷新 Token", IsTesting = true });
            await Task.Delay(30);
            string? accessToken = null;
            // 尝试刷新 Token
            try
            {
                accessToken = await _graph.RefreshTokenAsync(account);
                logs[^1] = new DetectLog
                {
                    Protocol = "刷新 Token",
                    IsTesting = false,
                    Success = accessToken != null,
                    Message = accessToken != null ? "Token 刷新成功" : "Token 刷新失败（可能已过期）"
                };
            }
            // 捕获 Token 刷新异常
            catch (Exception ex)
            {
                logs[^1] = new DetectLog { Protocol = "刷新 Token", IsTesting = false, Success = false, Message = $"异常: {ex.Message}" };
            }

            if (accessToken != null)
            {
                logs.Add(new DetectLog { Protocol = "IMAP XOAUTH2", IsTesting = true });
                await Task.Delay(300);
                // 尝试 XOAUTH2 验证
                try
                {
                    var ok = await _imap.VerifyXoauth2Async(account.Email, accessToken);
                    logs[^1] = new DetectLog
                    {
                        Protocol = "IMAP XOAUTH2",
                        IsTesting = false,
                        Success = ok,
                        Message = ok ? "连接成功" : "XOAUTH2 认证失败"
                    };
                    if (ok)
                    {
                        result.LogMessages = logs;
                        result.Success = true;
                        result.AuthType = "imap";
                        result.StatusMessage = "✅ IMAP XOAUTH2 — 连接成功";
                        return result;
                    }
                }
                // 捕获 XOAUTH2 验证异常
                catch (Exception ex)
                {
                    logs[^1] = new DetectLog { Protocol = "IMAP XOAUTH2", IsTesting = false, Success = false, Message = $"异常: {ex.Message}" };
                }
            }
        }

        // 2. IMAP + 密码（备选，部分老账号可用）
        if (!string.IsNullOrEmpty(account.Password))
        {
            logs.Add(new DetectLog { Protocol = "IMAP (密码)", IsTesting = true });
            await Task.Delay(300);
            // 尝试密码认证
            try
            {
                var ok = await _imap.VerifyAsync(account);
                logs[^1] = new DetectLog
                {
                    Protocol = "IMAP (密码)",
                    IsTesting = false,
                    Success = ok,
                    Message = ok ? "连接成功" : "密码错误或 IMAP 被禁用"
                };
                if (ok)
                {
                    result.LogMessages = logs;
                    result.Success = true;
                    result.AuthType = "imap";
                    result.StatusMessage = "✅ IMAP (密码) — 连接成功";
                    return result;
                }
            }
            // 捕获密码认证异常
            catch (Exception ex)
            {
                logs[^1] = new DetectLog { Protocol = "IMAP (密码)", IsTesting = false, Success = false, Message = $"异常: {ex.Message}" };
            }
        }

        result.LogMessages = logs;
        result.Success = false;
        result.StatusMessage = "❌ 所有协议均无法连接，请检查账号信息";
        return result;
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
