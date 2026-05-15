using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using OutlookApp.Models;

namespace OutlookApp.Services;

/// <summary>
/// IMAP 邮件获取服务，支持密码认证和 XOAUTH2 两种方式
/// </summary>
public partial class ImapEmailService : IEmailService
{
    public static ImapEmailService Create() => new();
    private const string Host = "outlook.office365.com";
    private const int Port = 993;

    public async Task<bool> VerifyAsync(EmailAccount account)
    {
        try
        {
            using var client = new ImapClient();
            await client.ConnectAsync(Host, Port, SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(account.Email, account.Password);
            await client.DisconnectAsync(true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> VerifyXoauth2Async(string email, string accessToken)
    {
        try
        {
            using var client = new ImapClient();
            await client.ConnectAsync(Host, Port, SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(new SaslMechanismOAuth2(email, accessToken));
            await client.DisconnectAsync(true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<EmailMessage>> FetchEmailsAsync(EmailAccount account, int maxCount = 50)
    {
        return await FetchByPasswordAsync(account, maxCount);
    }

    public async Task<List<EmailMessage>> FetchByPasswordAsync(EmailAccount account, int maxCount)
    {
        var emails = new List<EmailMessage>();
        try
        {
            using var client = new ImapClient();
            await client.ConnectAsync(Host, Port, SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(account.Email, account.Password);
            emails = await FetchMessagesAsync(client, maxCount);
            await client.DisconnectAsync(true);
        }
        catch { }
        return emails;
    }

    public async Task<List<EmailMessage>> FetchByXoauth2Async(string email, string accessToken, int maxCount)
    {
        var emails = new List<EmailMessage>();
        try
        {
            using var client = new ImapClient();
            await client.ConnectAsync(Host, Port, SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(new SaslMechanismOAuth2(email, accessToken));
            emails = await FetchMessagesAsync(client, maxCount);
            await client.DisconnectAsync(true);
        }
        catch { }
        return emails;
    }

    private async Task<List<EmailMessage>> FetchMessagesAsync(ImapClient client, int maxCount)
    {
        var emails = new List<EmailMessage>();
        var inbox = client.Inbox ?? throw new InvalidOperationException("Inbox not available");
        await inbox.OpenAsync(FolderAccess.ReadOnly);
        var uids = await inbox.SearchAsync(SearchQuery.All);
        var recent = uids.Skip(Math.Max(0, uids.Count - maxCount)).Take(maxCount).ToList();
        if (recent.Count == 0) return emails;

        var summaries = await inbox.FetchAsync(recent, MessageSummaryItems.Full | MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure);
        foreach (var s in summaries)
        {
            var bodyText = "";
            if (s.TextBody != null)
            {
                try
                {
                    var part = await inbox.GetBodyPartAsync(s.UniqueId, s.TextBody);
                    if (part is TextPart tp) bodyText = tp.Text;
                }
                catch { }
            }
            if (string.IsNullOrEmpty(bodyText) && s.HtmlBody != null)
            {
                try
                {
                    var part = await inbox.GetBodyPartAsync(s.UniqueId, s.HtmlBody);
                    if (part is TextPart tp) bodyText = tp.Text;
                }
                catch { }
            }

            emails.Add(new EmailMessage
            {
                Id = s.UniqueId.ToString(),
                Subject = s.Envelope?.Subject ?? "(No Subject)",
                From = s.Envelope?.From?.Mailboxes?.FirstOrDefault()?.Address ?? "",
                To = s.Envelope?.To?.Mailboxes?.FirstOrDefault()?.Address ?? "",
                Body = bodyText,
                BodyPreview = bodyText.Length > 100 ? bodyText[..100] + "..." : bodyText,
                ReceivedTime = s.Date.DateTime,
                HasAttachments = false,
                IsRead = s.Flags.HasValue && s.Flags.Value.HasFlag(MessageFlags.Seen)
            });
        }
        return emails.OrderByDescending(m => m.ReceivedTime).ToList();
    }
}
