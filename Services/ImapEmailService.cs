using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using OutlookApp.Models;

namespace OutlookApp.Services;

public class ImapEmailService : IEmailService
{
    private const string ImapHost = "outlook.office365.com";
    private const int ImapPort = 993;

    public async Task<bool> VerifyAsync(EmailAccount account)
    {
        try
        {
            using var client = new ImapClient();
            await client.ConnectAsync(ImapHost, ImapPort, SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(account.Email, account.Password);
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
        var emails = new List<EmailMessage>();
        try
        {
            using var client = new ImapClient();
            await client.ConnectAsync(ImapHost, ImapPort, SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(account.Email, account.Password);
            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly);
            var total = inbox.Count;
            var start = Math.Max(0, total - maxCount);
            var uids = await inbox.SearchAsync(SearchQuery.All);
            var recentUids = uids.Skip(Math.Max(0, uids.Count - maxCount)).Take(maxCount).ToList();

            var summaries = await inbox.FetchAsync(recentUids, MessageSummaryItems.Full | MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure);
            foreach (var summary in summaries.OrderByDescending(m => m.Date))
            {
                var subject = summary.Envelope.Subject ?? "(No Subject)";
                var from = summary.Envelope.From?.Mailboxes?.FirstOrDefault()?.Address ?? "";
                var to = summary.Envelope.To?.Mailboxes?.FirstOrDefault()?.Address ?? "";

                var bodyPreview = summary.TextBody != null
                    ? await inbox.GetBodyPartAsync(summary.UniqueId, summary.TextBody)
                    : null;
                var bodyText = "";
                if (bodyPreview is MimeKit.TextPart textPart)
                    bodyText = textPart.Text;
                else if (summary.HtmlBody != null)
                {
                    var htmlPart = await inbox.GetBodyPartAsync(summary.UniqueId, summary.HtmlBody);
                    if (htmlPart is MimeKit.TextPart htmlText)
                        bodyText = htmlText.Text;
                }

                if (string.IsNullOrEmpty(bodyText))
                    bodyText = subject;

                var msg = new EmailMessage
                {
                    Id = summary.UniqueId.ToString(),
                    Subject = subject,
                    From = from,
                    To = to,
                    Body = bodyText,
                    BodyPreview = bodyText.Length > 100 ? bodyText[..100] + "..." : bodyText,
                    ReceivedTime = summary.Date.DateTime,
                    HasAttachments = summary.Attachments != null && summary.Attachments.Any(),
                    IsRead = summary.Flags?.HasFlag(MessageFlags.Seen) ?? false
                };
                emails.Add(msg);
            }
            await client.DisconnectAsync(true);
        }
        catch
        {
        }
        return emails;
    }
}
