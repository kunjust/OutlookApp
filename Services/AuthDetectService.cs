using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OutlookApp.Models;

namespace OutlookApp.Services;

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

        // 1. IMAP + 密码
        if (!string.IsNullOrEmpty(account.Password))
        {
            logs.Add(new DetectLog { Protocol = "IMAP (密码)", IsTesting = true });
            await Task.Delay(300);
            try
            {
                var ok = await _imap.VerifyAsync(account);
                logs[^1] = new DetectLog { Protocol = "IMAP (密码)", IsTesting = false, Success = ok, Message = ok ? "连接成功" : "密码错误或 IMAP 被禁用" };
            }
            catch (Exception ex)
            {
                logs[^1] = new DetectLog { Protocol = "IMAP (密码)", IsTesting = false, Success = false, Message = $"异常: {ex.Message}" };
            }
        }

        // 2. Token 刷新 → IMAP XOAUTH2
        if (!string.IsNullOrEmpty(account.Token) && !string.IsNullOrEmpty(account.ClientId))
        {
            logs.Add(new DetectLog { Protocol = "刷新 Token", IsTesting = true });
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
                logs.Add(new DetectLog { Protocol = "IMAP (XOAUTH2)", IsTesting = true });
                await Task.Delay(300);
                try
                {
                    var xoauthOk = await _imap.VerifyXoauth2Async(account.Email, accessToken);
                    logs[^1] = new DetectLog { Protocol = "IMAP (XOAUTH2)", IsTesting = false, Success = xoauthOk, Message = xoauthOk ? "连接成功" : "XOAUTH2 认证失败" };
                }
                catch (Exception ex)
                {
                    logs[^1] = new DetectLog { Protocol = "IMAP (XOAUTH2)", IsTesting = false, Success = false, Message = $"异常: {ex.Message}" };
                }
            }

            // 3. 如果 XOAUTH2 失败，试试 Outlook REST API
            {
                logs.Add(new DetectLog { Protocol = "Outlook REST API", IsTesting = true });
                await Task.Delay(300);
                try
                {
                    var apiOk = accessToken != null && await _graph.VerifyAsync(account);
                    logs[^1] = new DetectLog { Protocol = "Outlook REST API", IsTesting = false, Success = apiOk, Message = apiOk ? "连接成功" : "API 调用失败" };
                }
                catch (Exception ex)
                {
                    logs[^1] = new DetectLog { Protocol = "Outlook REST API", IsTesting = false, Success = false, Message = $"异常: {ex.Message}" };
                }
            }
        }

        result.LogMessages = logs;

        var best = PickBest(logs);
        result.Success = best != null;
        result.AuthType = best?.Protocol switch
        {
            "IMAP (XOAUTH2)" => "imap",
            "IMAP (密码)" => "imap",
            "Outlook REST API" => "graph",
            _ => ""
        };
        result.StatusMessage = best != null
            ? $"✅ {best.Protocol} — {best.Message}"
            : "❌ 所有协议均无法连接，请检查账号信息";

        return result;
    }

    private DetectLog? PickBest(List<DetectLog> logs)
    {
        var ok = logs.FindAll(l => l.Success);
        if (ok.Count == 0) return null;

        var pref = ok.Find(l => l.Protocol == "IMAP (XOAUTH2)");
        if (pref != null) return pref;
        pref = ok.Find(l => l.Protocol == "Outlook REST API");
        if (pref != null) return pref;
        pref = ok.Find(l => l.Protocol == "IMAP (密码)");
        if (pref != null) return pref;
        return ok[0];
    }
}

public class DetectLog
{
    public string Protocol { get; set; } = "";
    public bool IsTesting { get; set; }
    public bool IsTestingDone => !IsTesting;
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

public class DetectionResult
{
    public bool Success { get; set; }
    public string AuthType { get; set; } = "";
    public string StatusMessage { get; set; } = "";
    public List<DetectLog> LogMessages { get; set; } = new();
}
