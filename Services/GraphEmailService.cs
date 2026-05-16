using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using OutlookApp.Models;

namespace OutlookApp.Services;

/// <summary>
/// Microsoft Graph / Outlook API Token 刷新服务
/// </summary>
public class GraphEmailService : IEmailService
{
    private readonly HttpClient _http;

    private static readonly string[] Scopes = new[]
    {
        "https://outlook.office.com/IMAP.AccessAsUser.All offline_access",
        "https://outlook.office.com/.default",
    };

    public GraphEmailService()
    {
        _http = new HttpClient();
    }

    /// <summary>
    /// 刷新 OAuth2 访问令牌。依次尝试多个作用域，直到成功为止。
    /// </summary>
    public async Task<string?> RefreshTokenAsync(EmailAccount account)
    {
        foreach (var scope in Scopes)
        {
            try
            {
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", account.ClientId),
                    new KeyValuePair<string, string>("grant_type", "refresh_token"),
                    new KeyValuePair<string, string>("refresh_token", account.Token),
                    new KeyValuePair<string, string>("scope", scope),
                });

                var response = await _http.PostAsync(
                    "https://login.microsoftonline.com/common/oauth2/v2.0/token", content);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var token = doc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
                    if (token != null)
                        return token;
                }
            }
            catch
            {
                // 尝试下一个 scope
            }
        }
        return null;
    }

    public async Task<bool> VerifyAsync(EmailAccount account)
    {
        var accessToken = await RefreshTokenAsync(account);
        return !string.IsNullOrEmpty(accessToken);
    }

    public Task<List<EmailMessage>> FetchEmailsAsync(EmailAccount account, int maxCount = 50)
    {
        return Task.FromResult(new List<EmailMessage>());
    }
}
