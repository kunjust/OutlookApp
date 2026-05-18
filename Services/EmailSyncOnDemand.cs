using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OutlookApp.Api;
using OutlookApp.Models;

namespace OutlookApp.Services;

/// <summary>
/// 按需同步邮件服务，用于获取验证码邮件。
///
/// 流程：
///   1) 先用 VerificationExtractor 从本地 SQLite 提取（时间窗口内的最新一封）
///   2) 命中则更新 LastCode/LastSyncTime 并返回
///   3) 未命中则按账号凭证依次尝试 IMAP 拉新邮件：
///        - 有密码 → IMAP 密码认证
///        - 有 ClientId + refresh_token → GraphEmailService 刷新出 access_token → IMAP XOAUTH2
///      任一条路径成功则把新邮件写入 SQLite
///   4) 再次提取，命中返回，否则返回失败
/// </summary>
public class EmailSyncOnDemand
{
    private readonly DatabaseService _dbService;
    private readonly ImapEmailService _imapService;
    private readonly GraphEmailService _graphService;
    private readonly VerificationExtractor _extractor;

    public EmailSyncOnDemand(DatabaseService dbService, ImapEmailService imapService)
        : this(dbService, imapService, new GraphEmailService())
    {
    }

    public EmailSyncOnDemand(DatabaseService dbService, ImapEmailService imapService, GraphEmailService graphService)
    {
        _dbService = dbService;
        _imapService = imapService;
        _graphService = graphService;
        _extractor = new VerificationExtractor(dbService.ConnectionString);
    }

    /// <summary>
    /// 获取指定邮箱最新的验证码。
    /// </summary>
    /// <param name="account">邮箱账号（含密码 / Token / ClientId）</param>
    /// <param name="keyword">可选关键词过滤（按主题/发件人/正文模糊匹配），null/空 = 不过滤</param>
    /// <param name="withinMinutes">只看最近多少分钟内的邮件，默认 30 分钟</param>
    public async Task<(bool Success, string Code, DateTime ReceivedTime)> FetchVerificationCodeAsync(
        EmailAccount account,
        string? keyword = null,
        int withinMinutes = 30)
    {
        // 1) 先看本地是否已有
        var (code, receivedTime) = _extractor.ExtractLatestCode(account.Email, keyword, withinMinutes);
        if (!string.IsNullOrEmpty(code))
        {
            _dbService.UpdateAccountCodeAndSyncTime(account.Email, code, DateTime.Now);
            return (true, code, receivedTime);
        }

        // 2) 本地没有 → 触发一次 IMAP 拉取
        var messages = await SyncFromImapAsync(account);
        if (messages.Count > 0)
        {
            _dbService.SaveMessages(account.Id, messages);
        }

        // 3) 再提取一次
        (code, receivedTime) = _extractor.ExtractLatestCode(account.Email, keyword, withinMinutes);
        if (!string.IsNullOrEmpty(code))
        {
            _dbService.UpdateAccountCodeAndSyncTime(account.Email, code, DateTime.Now);
            return (true, code, receivedTime);
        }

        return (false, string.Empty, DateTime.MinValue);
    }

    /// <summary>
    /// 按账号凭证依次尝试 IMAP 拉取，返回拉到的新邮件列表。
    /// 任一路径成功就返回；全部失败返回空列表（不抛异常给上层）。
    /// </summary>
    private async Task<List<EmailMessage>> SyncFromImapAsync(EmailAccount account)
    {
        Exception? lastError = null;

        // 路径 A：密码认证（有密码就先试）
        if (!string.IsNullOrEmpty(account.Password))
        {
            try
            {
                return await _imapService.FetchByPasswordAsync(account, 20);
            }
            catch (Exception ex)
            {
                lastError = ex;
                Console.WriteLine($"[EmailSync] {account.Email} 密码认证失败: {ex.Message}");
            }
        }

        // 路径 B：refresh_token → access_token → XOAUTH2
        if (!string.IsNullOrEmpty(account.Token) && !string.IsNullOrEmpty(account.ClientId))
        {
            try
            {
                var accessToken = await _graphService.RefreshTokenAsync(account);
                if (!string.IsNullOrEmpty(accessToken))
                {
                    return await _imapService.FetchByXoauth2Async(account.Email, accessToken, 20);
                }
                Console.WriteLine($"[EmailSync] {account.Email} Token 刷新返回空");
            }
            catch (Exception ex)
            {
                lastError = ex;
                Console.WriteLine($"[EmailSync] {account.Email} XOAUTH2 失败: {ex.Message}");
            }
        }

        if (lastError != null)
            Console.WriteLine($"[EmailSync] {account.Email} 所有认证路径均失败");

        return new List<EmailMessage>();
    }
}
