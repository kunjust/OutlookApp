using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using OutlookApp.Models;

namespace OutlookApp.Services;

public class GraphEmailService : IEmailService
{
    private readonly HttpClient _http;

    public GraphEmailService()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri("https://graph.microsoft.com/v1.0/")
        };
    }

    public async Task<bool> VerifyAsync(EmailAccount account)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
            var response = await _http.SendAsync(request);
            return response.IsSuccessStatusCode;
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
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"me/messages?$top={maxCount}&$select=id,subject,from,toRecipients,bodyPreview,body,receivedDateTime,hasAttachments,isRead&$orderby=receivedDateTime DESC");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.Token);
            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return emails;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("value", out var valueArray)) return emails;

            foreach (var item in valueArray.EnumerateArray())
            {
                var msg = new EmailMessage
                {
                    Id = item.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Subject = item.TryGetProperty("subject", out var subj) ? subj.GetString() ?? "" : "",
                    From = item.TryGetProperty("from", out var from)
                        ? (from.TryGetProperty("emailAddress", out var ea)
                            ? (ea.TryGetProperty("address", out var addr) ? addr.GetString() ?? "" : "")
                            : "")
                        : "",
                    BodyPreview = item.TryGetProperty("bodyPreview", out var prev) ? prev.GetString() ?? "" : "",
                    ReceivedTime = item.TryGetProperty("receivedDateTime", out var dt)
                        ? (DateTime.TryParse(dt.GetString(), out var parsed) ? parsed : DateTime.MinValue)
                        : DateTime.MinValue,
                    HasAttachments = item.TryGetProperty("hasAttachments", out var attach) && attach.GetBoolean(),
                    IsRead = item.TryGetProperty("isRead", out var read) && read.GetBoolean()
                };

                if (item.TryGetProperty("toRecipients", out var toRecipients) && toRecipients.GetArrayLength() > 0)
                {
                    var first = toRecipients[0];
                    if (first.TryGetProperty("emailAddress", out var toEa) && toEa.TryGetProperty("address", out var toAddr))
                        msg.To = toAddr.GetString() ?? "";
                }

                if (item.TryGetProperty("body", out var body))
                {
                    msg.Body = body.TryGetProperty("content", out var content)
                        ? content.GetString() ?? ""
                        : msg.BodyPreview;
                }

                emails.Add(msg);
            }
        }
        catch
        {
        }

        return emails;
    }
}
