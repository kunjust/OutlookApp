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

        if (!string.IsNullOrEmpty(account.Password))
        {
            result.StatusMessage = "正在检测 IMAP...";
            var imapOk = await _imapService.VerifyAsync(account);
            if (imapOk)
            {
                result.Success = true;
                result.AuthType = "imap";
                result.StatusMessage = "IMAP 连接成功";
                return result;
            }
        }

        if (!string.IsNullOrEmpty(account.Token) && !string.IsNullOrEmpty(account.ClientId))
        {
            result.StatusMessage = "IMAP 不可用，正在检测 Graph API...";
            await Task.Delay(500);
            var graphOk = await _graphService.VerifyAsync(account);
            if (graphOk)
            {
                result.Success = true;
                result.AuthType = "graph";
                result.StatusMessage = "Graph API 连接成功";
                return result;
            }
        }

        if (!string.IsNullOrEmpty(account.Token) && !string.IsNullOrEmpty(account.Password))
        {
            result.StatusMessage = "尝试 XOAUTH2 IMAP...";
            await Task.Delay(500);
            var xoauthOk = await _imapService.VerifyAsync(account);
            if (xoauthOk)
            {
                result.Success = true;
                result.AuthType = "imap";
                result.StatusMessage = "XOAUTH2 IMAP 连接成功";
                return result;
            }
        }

        result.Success = false;
        result.StatusMessage = "所有协议均无法连接，请检查账号信息";
        return result;
    }
}

public class DetectionResult
{
    public bool Success { get; set; }
    public string AuthType { get; set; } = "";
    public string StatusMessage { get; set; } = "";
}
