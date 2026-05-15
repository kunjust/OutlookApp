using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using OutlookApp.Services;
using OutlookApp.ViewModels;

namespace OutlookApp.Api;

/// <summary>
/// 内置 HTTP API 服务器（端口 5000），供移动端调用
/// </summary>
public class HttpServer
{
    private readonly HttpListener _listener;
    private readonly DatabaseService _dbService;
    private readonly KeywordService _kwService;
    private readonly MainWindowViewModel _mainWindowVm;
    private readonly EmailSyncOnDemand _emailSync;
    private readonly string _docsUrl;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public HttpServer(int port, DatabaseService dbService, KeywordService kwService)
        : this(port, dbService, kwService, null!, false) { }

    public HttpServer(int port, DatabaseService dbService, bool showDocs)
        : this(port, dbService, new KeywordService(dbService.ConnectionString), null!, showDocs) { }

    public HttpServer(int port, DatabaseService dbService, KeywordService kwService, MainWindowViewModel mainWindowVm)
        : this(port, dbService, kwService, mainWindowVm, false) { }

    public HttpServer(int port, DatabaseService dbService, MainWindowViewModel mainWindowVm)
        : this(port, dbService, mainWindowVm, false) { }

    public HttpServer(int port, DatabaseService dbService, MainWindowViewModel mainWindowVm, bool showDocs)
        : this(port, dbService, new KeywordService(dbService.ConnectionString), mainWindowVm, showDocs) { }

    public HttpServer(int port, DatabaseService dbService, KeywordService kwService, MainWindowViewModel mainWindowVm, bool showDocs)
    {
        _dbService = dbService;
        _kwService = kwService;
        _mainWindowVm = mainWindowVm;
        _emailSync = new EmailSyncOnDemand(dbService, ImapEmailService.Create());
        _docsUrl = showDocs ? $"http://localhost:{port}/docs" : "";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://*:{port}/");
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        _listenTask = ListenLoop(_cts.Token);
        if (!string.IsNullOrEmpty(_docsUrl))
            Console.WriteLine($"📖 API 文档: {_docsUrl}");
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        if (_listenTask != null)
            await _listenTask;
        _listener.Stop();
    }

    private async Task ListenLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();
                _ = HandleRequestAsync(context);
            }
        }
        catch (ObjectDisposedException) { }
        catch (HttpListenerException) { }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";

            // API 激活网关：所有 /api/ 路径需要卡密激活
            if (path.StartsWith("/api/") && path != "/docs")
            {
                if (_mainWindowVm == null || !_mainWindowVm.IsActivated)
                {
                    SendResponse(response, 403, new
                    {
                        error = "unauthorized",
                        message = "请先激活卡密"
                    });
                    return;
                }
            }

            if (path == "/api/email" && context.Request.HttpMethod == "GET")
                HandleAllocateEmail(response);
            else if (path == "/api/accounts" && context.Request.HttpMethod == "GET")
                HandleListAccounts(context.Request, response);
            else if (path == "/api/target/allocate" && context.Request.HttpMethod == "GET")
                HandleTargetAllocate(response);
            else if (path == "/api/target/count" && context.Request.HttpMethod == "GET")
                HandleTargetCount(response);
            else if (path == "/api/code" && context.Request.HttpMethod == "GET")
                await HandleGetCode(context.Request, response);
            else if (path == "/api/status" && context.Request.HttpMethod == "GET")
                HandleStatus(context.Request, response);
            else if (path == "/docs")
                HandleDocs(response);
            else
                SendResponse(response, 404, new { error = "Not Found" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HTTP Error [{ex.GetType().Name}]: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            try
            {
                SendResponse(response, 500, new { error = ex.GetType().Name, detail = ex.Message });
            }
            catch
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes($"{{\"error\":\"{ex.GetType().Name}\",\"detail\":\"{ex.Message.Replace("\"", "\\\"")}\"}}");
                response.StatusCode = 500;
                response.ContentType = "application/json";
                response.ContentLength64 = bytes.Length;
                response.OutputStream.Write(bytes, 0, bytes.Length);
            }
        }
        finally
        {
            response.OutputStream.Close();
        }
    }

    private void HandleDocs(HttpListenerResponse response)
    {
        var html = GenerateDocsHtml();
        var bytes = System.Text.Encoding.UTF8.GetBytes(html);
        response.StatusCode = 200;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    private static string GenerateDocsHtml()
    {
        return @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>OutlookApp API 文档</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #0d1117; color: #c9d1d9; line-height: 1.6; padding: 24px; max-width: 900px; margin: 0 auto; }
        h1 { font-size: 24px; color: #58a6ff; margin-bottom: 8px; }
        .subtitle { color: #8b949e; margin-bottom: 32px; }
        .endpoint { background: #161b22; border: 1px solid #30363d; border-radius: 8px; padding: 20px; margin-bottom: 20px; }
        .endpoint:hover { border-color: #58a6ff; }
        .method { display: inline-block; background: #238636; color: #fff; padding: 2px 10px; border-radius: 4px; font-size: 13px; font-weight: 600; margin-right: 10px; }
        .path { font-family: 'SF Mono', 'Consolas', monospace; font-size: 15px; color: #f0f6fc; }
        .desc { color: #8b949e; margin: 12px 0 8px; }
        .params { margin: 8px 0; }
        .params th { text-align: left; color: #58a6ff; padding: 6px 16px 6px 0; font-weight: 600; font-size: 13px; }
        .params td { padding: 6px 16px 6px 0; font-size: 13px; }
        .params code { background: #21262d; padding: 2px 6px; border-radius: 4px; font-size: 12px; color: #ff7b72; }
        pre { background: #0d1117; border: 1px solid #30363d; border-radius: 6px; padding: 12px; overflow-x: auto; margin: 8px 0; }
        pre code { font-family: 'SF Mono', 'Consolas', monospace; font-size: 13px; color: #7ee787; }
        .resp-label { font-size: 12px; color: #8b949e; margin: 12px 0 4px; font-weight: 600; }
        .try-btn { background: #21262d; color: #58a6ff; border: 1px solid #30363d; padding: 6px 14px; border-radius: 6px; cursor: pointer; font-size: 13px; margin-top: 8px; }
        .try-btn:hover { background: #30363d; border-color: #58a6ff; }
        .result { display: none; background: #0d1117; border: 1px solid #30363d; border-radius: 6px; padding: 12px; margin-top: 8px; }
        .result.active { display: block; }
        .result pre { border: none; margin: 0; padding: 0; }
        .section { font-size: 18px; color: #58a6ff; font-weight: 600; margin: 32px 0 16px; border-bottom: 1px solid #30363d; padding-bottom: 8px; }
    </style>
</head>
<body>
    <h1>📧 OutlookApp API 文档</h1>
    <p class=""subtitle"">邮件验证码获取接口 &middot; 开发环境专用</p>

    <div class=""section"">📬 邮箱接口</div>

    <div class=""endpoint"">
        <span class=""method"">GET</span> <span class=""path"">/api/email</span>
        <p class=""desc"">分配一个可用邮箱</p>
        <p class=""resp-label"">成功响应：</p>
        <pre><code>{ ""success"": true, ""email"": ""user@outlook.com"" }</code></pre>
        <p class=""resp-label"">邮箱用尽：</p>
        <pre><code>{ ""success"": false, ""message"": ""邮箱已全部使用"" }</code></pre>
        <button class=""try-btn"" onclick=""tryApi('email', this)"">▶ 在线测试</button>
        <div class=""result"" id=""result-email""></div>
    </div>

    <div class=""endpoint"">
        <span class=""method"">GET</span> <span class=""path"">/api/accounts</span>
        <p class=""desc"">分页查询所有可用邮箱列表</p>
        <table class=""params"">
            <tr><th>参数</th><th>类型</th><th>默认</th><th>说明</th></tr>
            <tr><td><code>page</code></td><td>int</td><td>1</td><td>页码，从 1 开始</td></tr>
            <tr><td><code>pageSize</code></td><td>int</td><td>30</td><td>每页数量（最大 100）</td></tr>
        </table>
        <p class=""resp-label"">响应：</p>
        <pre><code>{
  ""success"": true,
  ""page"": 1,
  ""pageSize"": 30,
  ""total"": 150,
  ""totalPages"": 5,
  ""items"": [{ ""email"": ""..."", ""status"": ""Verified"", ""allocated"": false }]
}</code></pre>
        <button class=""try-btn"" onclick=""tryApi('accounts', this)"">▶ 在线测试</button>
        <div class=""result"" id=""result-accounts""></div>
    </div>

    <div class=""endpoint"">
        <span class=""method"">GET</span> <span class=""path"">/api/code</span>
        <p class=""desc"">获取指定邮箱的 Instagram 验证码（6 位数字）</p>
        <table class=""params"">
            <tr><th>参数</th><th>类型</th><th>必填</th><th>说明</th></tr>
            <tr><td><code>email</code></td><td>string</td><td>是</td><td>邮箱地址</td></tr>
            <tr><td><code>retry</code></td><td>0/1</td><td>否</td><td>未找到时是否触发 IMAP 刷新</td></tr>
        </table>
        <p class=""resp-label"">成功响应：</p>
        <pre><code>{ ""success"": true, ""code"": ""123456"", ""time"": ""2026-05-06 10:30:00"" }</code></pre>
        <p class=""resp-label"">暂无验证码：</p>
        <pre><code>{ ""success"": false, ""message"": ""暂无验证码"" }</code></pre>
    </div>

    <div class=""endpoint"">
        <span class=""method"">GET</span> <span class=""path"">/api/status</span>
        <p class=""desc"">查询邮箱的分配状态</p>
        <table class=""params"">
            <tr><th>参数</th><th>类型</th><th>必填</th><th>说明</th></tr>
            <tr><td><code>email</code></td><td>string</td><td>是</td><td>邮箱地址</td></tr>
        </table>
        <p class=""resp-label"">响应：</p>
        <pre><code>{ ""success"": true, ""email"": ""user@outlook.com"", ""allocated"": true, ""lastCode"": """", ""lastSyncTime"": """" }</code></pre>
    </div>

    <div class=""section"">🎯 对标接口</div>

    <div class=""endpoint"">
        <span class=""method"">GET</span> <span class=""path"">/api/target/allocate</span>
        <p class=""desc"">分配一个可用对标。原子操作防止并发冲突，返回后标记为已用，不可重复分配。</p>
        <p class=""resp-label"">成功响应：</p>
        <pre><code>{ ""success"": true, ""content"": ""keyword123"" }</code></pre>
        <p class=""resp-label"">无可用对标：</p>
        <pre><code>{ ""success"": false, ""message"": ""无可用对标"" }</code></pre>
        <button class=""try-btn"" onclick=""tryApi('target/allocate', this)"">▶ 在线测试</button>
        <div class=""result"" id=""result-target/allocate""></div>
    </div>

    <div class=""endpoint"">
        <span class=""method"">GET</span> <span class=""path"">/api/target/count</span>
        <p class=""desc"">查询对标的可用、已用和总数量。</p>
        <p class=""resp-label"">响应：</p>
        <pre><code>{ ""success"": true, ""available"": 150, ""used"": 1520, ""total"": 1670 }</code></pre>
        <button class=""try-btn"" onclick=""tryApi('target/count', this)"">▶ 在线测试</button>
        <div class=""result"" id=""result-target/count""></div>
    </div>

    <script>
        function tryApi(endpoint, btn) {
            var result = document.getElementById('result-' + endpoint);
            btn.textContent = '⏳ 请求中...';
            btn.disabled = true;
            fetch('/api/' + endpoint)
                .then(r => r.json())
                .then(d => {
                    result.classList.add('active');
                    result.innerHTML = '<pre><code>' + JSON.stringify(d, null, 2) + '</code></pre>';
                    btn.textContent = '▶ 重新测试';
                    btn.disabled = false;
                })
                .catch(function(err) {
                    result.classList.add('active');
                    result.innerHTML = '<pre><code style=""color:#f85149"">请求失败: ' + err.message + '</code></pre>';
                    btn.textContent = '▶ 重试';
                    btn.disabled = false;
                });
        }
    </script>
</body>
</html>";
    }

    private void HandleAllocateEmail(HttpListenerResponse response)
    {
        if (_dbService.TryAllocateAccount(out var email))
        {
            SendResponse(response, 200, new { success = true, email });
        }
        else
        {
            try
            {
                if (_mainWindowVm != null)
                    _mainWindowVm.StatusText = "⚠️ 所有邮箱已分配完毕，请导入更多账号。";
            }
            catch { }
            SendResponse(response, 200, new { success = false, message = "邮箱已全部使用，请导入更多账户。" });
        }
    }

    private void HandleListAccounts(HttpListenerRequest request, HttpListenerResponse response)
    {
        int.TryParse(request.QueryString["page"] ?? "1", out var page);
        int.TryParse(request.QueryString["pageSize"] ?? "30", out var pageSize);
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 30;

        var accounts = _dbService.GetAccountsPaged(page, pageSize);
        var total = _dbService.GetAccountsCount();
        var totalPages = (total + pageSize - 1) / pageSize;

        var items = accounts.Select(a => new
        {
            email = a.Email,
            status = a.Status,
            allocated = a.Allocated,
            isUsed = a.IsUsed,
            lastCode = a.LastCode,
            lastSyncTime = a.LastSyncTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
        });

        SendResponse(response, 200, new
        {
            success = true,
            page,
            pageSize,
            total,
            totalPages,
            items,
        });
    }

    private void HandleTargetAllocate(HttpListenerResponse response)
    {
        var content = _kwService.AllocateOne();
        if (content != null)
            SendResponse(response, 200, new { success = true, content });
        else
            SendResponse(response, 200, new { success = false, message = "无可用对标" });
    }

    private void HandleTargetCount(HttpListenerResponse response)
    {
        var counts = _kwService.GetCounts();
        SendResponse(response, 200, new { success = true, available = counts.Available, used = counts.Used, total = counts.Total });
    }

    private async Task HandleGetCode(HttpListenerRequest request, HttpListenerResponse response)
    {
        var email = request.QueryString["email"] ?? "";
        if (string.IsNullOrEmpty(email))
        {
            SendResponse(response, 400, new { success = false, message = "缺少 email 参数" });
            return;
        }

        var account = _dbService.GetAccountByEmail(email);
        if (account == null)
        {
            SendResponse(response, 404, new { success = false, message = "未找到该邮箱账户" });
            return;
        }

        var (success, code, receivedTime) = await _emailSync.FetchVerificationCodeAsync(account);
        if (success)
            SendResponse(response, 200, new { success = true, code, time = receivedTime.ToString("yyyy-MM-dd HH:mm:ss") });
        else
            SendResponse(response, 200, new { success = false, message = "暂无验证码" });
    }

    private void HandleStatus(HttpListenerRequest request, HttpListenerResponse response)
    {
        var email = request.QueryString["email"] ?? "";
        if (string.IsNullOrEmpty(email))
        {
            SendResponse(response, 400, new { success = false, message = "缺少 email 参数" });
            return;
        }

        var account = _dbService.GetAccountByEmail(email);
        if (account == null)
        {
            SendResponse(response, 404, new { success = false, message = "未找到该邮箱账户" });
            return;
        }

        SendResponse(response, 200, new
        {
            success = true,
            email = account.Email,
            allocated = account.Allocated,
            lastCode = account.LastCode,
            lastSyncTime = account.LastSyncTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""
        });
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private void SendResponse(HttpListenerResponse response, int statusCode, object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOpts);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }
}
