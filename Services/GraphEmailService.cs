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

            var response = await _http.PostAsync(
                "https://login.microsoftonline.com/common/oauth2/v2.0/token", content);
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
        var accessToken = await RefreshTokenAsync(account);
        return !string.IsNullOrEmpty(accessToken);
    }

    public Task<List<EmailMessage>> FetchEmailsAsync(EmailAccount account, int maxCount = 50)
    {
        // REST API doesn't work with these tokens
        // Email fetching is done via IMAP XOAUTH2 in ImapEmailService
        return Task.FromResult(new List<EmailMessage>());
    }
}
