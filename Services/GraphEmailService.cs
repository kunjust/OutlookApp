using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using OutlookApp.Models;

namespace OutlookApp.Services;

public class GraphEmailService : IEmailService
{
    private readonly HttpClient _http;

    public GraphEmailService()
    {
        _http = new HttpClient();
    }

    public async Task<string?> RefreshTokenAsync(EmailAccount account)
    {
        try
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", account.ClientId),
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", account.Token),
                new KeyValuePair<string, string>("scope", "https://outlook.office.com/.default")
            });

            var response = await _http.PostAsync("https://login.microsoftonline.com/common/oauth2/v2.0/token", content);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> VerifyAsync(EmailAccount account)
    {
        try
        {
            var accessToken = await RefreshTokenAsync(account);
            if (string.IsNullOrEmpty(accessToken)) return false;

            var request = new HttpRequestMessage(HttpMethod.Get,
                "https://outlook.office.com/api/v2.0/me/messages?$top=1&$select=Subject");
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

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
            var accessToken = await RefreshTokenAsync(account);
            if (string.IsNullOrEmpty(accessToken)) return emails;

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://outlook.office.com/api/v2.0/me/mailfolders/inbox/messages?$top={maxCount}&$select=Subject,From,ReceivedDateTime,BodyPreview,HasAttachments,IsRead");
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return emails;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("value", out var valueArray))
                return emails;

            foreach (var item in valueArray.EnumerateArray())
            {
                var msg = new EmailMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    Subject = item.TryGetProperty("Subject", out var s) ? s.GetString() ?? "" : "",
                    From = item.TryGetProperty("From", out var f)
                        ? (f.TryGetProperty("EmailAddress", out var ea)
                            ? (ea.TryGetProperty("Address", out var a) ? a.GetString() ?? "" : "")
                            : "")
                        : "",
                    BodyPreview = item.TryGetProperty("BodyPreview", out var p) ? p.GetString() ?? "" : "",
                    ReceivedTime = item.TryGetProperty("ReceivedDateTime", out var dt)
                        ? (DateTime.TryParse(dt.GetString(), out var parsed) ? parsed : DateTime.MinValue)
                        : DateTime.MinValue,
                    HasAttachments = item.TryGetProperty("HasAttachments", out var att) && att.GetBoolean(),
                    IsRead = item.TryGetProperty("IsRead", out var r) && r.GetBoolean(),
                    Body = item.TryGetProperty("BodyPreview", out var bp) ? bp.GetString() ?? "" : ""
                };
                emails.Add(msg);
            }
        }
        catch { }

        return emails;
    }
}
