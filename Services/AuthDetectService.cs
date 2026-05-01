using System.Collections.Generic;
using System.Threading.Tasks;
using OutlookApp.Models;

namespace OutlookApp.Services;

public class AuthDetectService
{
    private readonly ImapEmailService _imapService;
    private readonly GraphEmailService _graphService;

    public AuthDetectService()
    {
        _imapService = new ImapEmailService();
        _graphService = new GraphEmailService();
    }

    public async Task<DetectionResult> DetectAsync(EmailAccount account)
    {
        var result = new DetectionResult();
        var tasks = new List<ProtocolTask>();

        if (!string.IsNullOrEmpty(account.Password))
        {
            tasks.Add(new ProtocolTask
            {
                Name = "IMAP (密码)",
                IsAvailable = true,
                TestFunc = async () =>
                {
                    try
                    {
                        var ok = await _imapService.VerifyAsync(account);
                        return (ok, ok ? "IMAP 密码验证成功" : "IMAP 密码验证失败");
                    }
                    catch { return (false, "IMAP 连接异常"); }
                }
            });
        }

        if (!string.IsNullOrEmpty(account.Token) && !string.IsNullOrEmpty(account.ClientId))
        {
            tasks.Add(new ProtocolTask
            {
                Name = "Graph API",
                IsAvailable = true,
                TestFunc = async () =>
                {
                    try
                    {
                        var ok = await _graphService.VerifyAsync(account);
                        return (ok, ok ? "Graph API 验证成功" : "Graph API 验证失败");
                    }
                    catch { return (false, "Graph API 连接异常"); }
                }
            });

            tasks.Add(new ProtocolTask
            {
                Name = "XOAUTH2 IMAP",
                IsAvailable = true,
                TestFunc = async () =>
                {
                    try
                    {
                        var ok = await _imapService.VerifyAsync(account);
                        return (ok, ok ? "XOAUTH2 IMAP 验证成功" : "XOAUTH2 IMAP 验证失败");
                    }
                    catch { return (false, "XOAUTH2 IMAP 连接异常"); }
                }
            });
        }

        foreach (var t in tasks)
        {
            result.LogMessages.Add(new DetectLog
            {
                Protocol = t.Name,
                IsTesting = true
            });

            var (success, message) = await t.TestFunc();
            result.LogMessages[^1] = new DetectLog
            {
                Protocol = t.Name,
                IsTesting = false,
                Success = success,
                Message = message
            };
        }

        var best = PickBest(result.LogMessages);
        result.Success = best != null;
        result.AuthType = best?.Protocol switch
        {
            "Graph API" => "graph",
            "XOAUTH2 IMAP" => "imap",
            "IMAP (密码)" => "imap",
            _ => ""
        };
        result.StatusMessage = result.Success
            ? $"✅ {best!.Protocol} — {best.Message}"
            : "❌ 所有协议均无法连接，请检查账号信息";

        return result;
    }

    private DetectLog? PickBest(List<DetectLog> logs)
    {
        var succeeded = logs.FindAll(l => l.Success);
        if (succeeded.Count == 0) return null;

        var graph = succeeded.Find(l => l.Protocol == "Graph API");
        if (graph != null) return graph;
        var xoauth = succeeded.Find(l => l.Protocol == "XOAUTH2 IMAP");
        if (xoauth != null) return xoauth;
        return succeeded[0];
    }
}

public class ProtocolTask
{
    public string Name { get; set; } = "";
    public bool IsAvailable { get; set; }
    public string Message { get; set; } = "";
    public System.Func<Task<(bool Success, string Message)>>? TestFunc { get; set; }
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
