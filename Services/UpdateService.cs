using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace OutlookApp.Services;

public class ReleaseInfo
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public bool ForceUpdate { get; set; }
}

/// <summary>
/// 版本更新检查。
/// 首选 GitHub Releases API，失败时可降级到卡密服务端 /api/v1/AppVersion。
/// </summary>
public class UpdateService
{
    private readonly HttpClient _http;

    // 当前版本（硬编码，发布时更新）
    public static readonly Version CurrentVersion = new(1, 0, 0);

    // GitHub 仓库
    private const string GitHubApi = "https://api.github.com/repos/ikechen/OutlookApp/releases/latest";
    private const string GitHubUserAgent = "IKC-Updater/1.0";

    // 降级地址（卡密服务端可配置）
    private readonly string? _fallbackUrl;

    public UpdateService(string? fallbackUrl = null)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.Add("User-Agent", GitHubUserAgent);
        _fallbackUrl = fallbackUrl;
    }

    /// <summary>
    /// 检查是否有新版本。返回 null 表示检查失败（网络问题等）。
    /// </summary>
    public async Task<ReleaseInfo?> CheckAsync()
    {
        // 1. 尝试 GitHub
        try
        {
            return await CheckGitHubAsync();
        }
        catch
        {
            // GitHub 失败，继续尝试降级
        }

        // 2. 尝试降级地址（卡密服务端 AppVersion 接口）
        if (!string.IsNullOrEmpty(_fallbackUrl))
        {
            try
            {
                return await CheckFallbackAsync();
            }
            catch { }
        }

        return null;
    }

    private async Task<ReleaseInfo?> CheckGitHubAsync()
    {
        var resp = await _http.GetStringAsync(GitHubApi);
        using var doc = JsonDocument.Parse(resp);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "0.0.0";
        var version = tag.TrimStart('v', 'V');
        var body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

        // 取第一个 assets 的下载链接
        var assets = root.GetProperty("assets");
        var downloadUrl = "";
        if (assets.GetArrayLength() > 0)
            downloadUrl = assets[0].GetProperty("browser_download_url").GetString() ?? "";

        return new ReleaseInfo
        {
            Version = version,
            DownloadUrl = downloadUrl,
            ReleaseNotes = body,
            ForceUpdate = false
        };
    }

    private async Task<ReleaseInfo?> CheckFallbackAsync()
    {
        var resp = await _http.GetStringAsync(_fallbackUrl);
        return JsonSerializer.Deserialize<ReleaseInfo>(resp);
    }

    /// <summary>
    /// 比对版本号。
    /// </summary>
    public static bool IsNewer(string latestVersion)
    {
        if (Version.TryParse(latestVersion, out var v))
            return v > CurrentVersion;
        return false;
    }
}
