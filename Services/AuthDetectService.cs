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

    public AuthDetectService()
    {
        _imap = new ImapEmailService();
        _graph = new GraphEmailService();
    }

    public async Task<DetectionResult> DetectAsync(EmailAccount account)
    {
        var result = new DetectionResult();
        var logs = new List<DetectLog>();

        // 1. Refresh Token  →  IMAP XOAUTH2（主路径，大部分账号走这个）
        if (!string.IsNullOrEmpty(account.Token) && !string.IsNullOrEmpty(account.ClientId))
        {
            logs.Add(new DetectLog { Protocol = "刷新 Token", IsTesting = true });
            await Task.Delay(300);
            string? accessToken = null;
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
            catch (Exception ex)
            {
                logs[^1] = new DetectLog { Protocol = "刷新 Token", IsTesting = false, Success = false, Message = $"异常: {ex.Message}" };
            }

            if (accessToken != null)
            {
                logs.Add(new DetectLog { Protocol = "IMAP XOAUTH2", IsTesting = true });
                await Task.Delay(300);
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
/// 邮箱协议自动检测服务，依次尝试 XOAUTH2 和密码认证
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
/// 邮箱协议自动检测服务，依次尝试 XOAUTH2 和密码认证
/// </summary>
public class DetectionResult
{
    public bool Success { get; set; }
    public string AuthType { get; set; } = "";
    public string StatusMessage { get; set; } = "";
    public List<DetectLog> LogMessages { get; set; } = new();
}
