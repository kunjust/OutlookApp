# OutlookApp

Outlook 邮件管理桌面应用。紧凑窗口设计，不干扰其他软件使用。

内置 HTTP API 服务器（端口 5000），供移动端调用获取邮箱和验证码。

## 技术栈

- **.NET 8** + **Avalonia UI**（跨平台 XAML 框架，API 与 WPF 高度一致）
- **SQLite** (Microsoft.Data.Sqlite) + **AES-256-GCM** 字段级加密
- **IMAP** + **XOAUTH2** 双协议；Microsoft Graph API（仅 Token 刷新）
- **卡密激活** — 依赖远程 API 服务（`http://km.frp.ikechen.com:8099`）
- **MVVM** 架构 (CommunityToolkit.Mvvm)

## 项目结构

```
OutlookApp/
├── Models/
│   ├── EmailAccount.cs       # 邮箱账号模型 (Email, Password, ClientId, Token, Status, IsUsed, Allocated, LastCode, LastSyncTime)
│   ├── EmailMessage.cs       # 邮件模型 (Subject, From, Body, HasAttachments, IsRead, etc.)
│   ├── LicenseInfo.cs        # 卡密激活信息 (CardKey, DeviceId, ExpiryTime, ServerTime)
│   └── KeywordItem.cs        # 对标条目模型 (Content, Status, UsedAt)
├── ViewModels/
│   ├── ViewModelBase.cs      # ObservableObject 基类
│   ├── MainWindowViewModel.cs  # 主窗口 VM (账号管理+邮件管理+卡密心跳+对标面板切换)
│   ├── ActivationViewModel.cs  # 激活窗口 VM (卡密输入→Activate API)
│   ├── ImportDialogViewModel.cs # 导入对话框 VM (解析 ---- 分隔的文本)
│   └── KeywordViewModel.cs   # 对标管理面板 VM (CRUD/批量恢复/分配)
├── Views/
│   ├── MainWindow.axaml/cs   # 主窗口 (三栏布局: 账号/邮件列表/详情 + 对标面板)
│   ├── ActivationWindow.axaml/cs # 卡密激活窗口
│   ├── ImportDialog.axaml/cs  # 导入对话框 (粘贴+文件选择)
│   └── KeywordPanel.axaml/cs  # 对标管理面板
├── Services/
│   ├── DatabaseService.cs    # SQLite CRUD + 外键级联删除 + 分页 + 原子分配
│   ├── EncryptionService.cs  # AES-256-GCM 加解密 (密钥=MachineName+UserName)
│   ├── IEmailService.cs      # 邮件服务接口
│   ├── ImapEmailService.cs   # IMAP 密码认证 + XOAUTH2 + 邮件获取
│   ├── GraphEmailService.cs  # OAuth2 Token 刷新 (获取邮件返回空列表)
│   ├── AuthDetectService.cs  # 协议自动检测 (密码优先→XOAUTH2备选)
│   ├── LicenseService.cs     # 卡密 API 调用 (Activate/Verify/Heartbeat/Unbind/Query)
│   ├── LicenseStorageService.cs # 本地加密缓存 ~/.outlookapp/license.dat
│   ├── HardwareService.cs    # 硬件指纹 (MAC 地址)
│   ├── ApiSecurityService.cs # API 签名 (SHA256) + 响应解密 (AES-CBC)
│   ├── UpdateService.cs      # 版本更新检查 (GitHub Releases / 降级 API)
│   ├── KeywordService.cs     # 对标 CRUD + 原子分配 + 统计
│   └── EmailSyncOnDemand.cs  # 按需同步邮件并提取验证码
├── Api/
│   ├── HttpServer.cs         # 内置 HTTP API (端口 5000, 需卡密激活)
│   └── VerificationExtractor.cs # 从邮件正文提取 6 位 Instagram 验证码
├── App.axaml / App.axaml.cs  # 应用入口，管理激活流程和主窗口生命周期
├── Program.cs                # 入口 (正常启动 / --test-signature 签名诊断)
├── ViewLocator.cs            # VM → View 自动映射
├── ServerTest.cs             # 卡密 API 签名诊断工具
└── AGENTS.md
```

## 当前状态

- **卡密激活系统**完整：本地缓存 + 远程 API 验证 + 5分钟心跳保活 + 解绑
- **数据库层**完整：SQLite CRUD + AES-256-GCM 字段级加密 + 原子账号分配
- **邮件服务**：IMAP 密码认证 + XOAUTH2 双协议已完整实现
- **协议检测**：密码认证优先，XOAUTH2 备选 (AuthDetectService)
- **Token 刷新**：通过 raw HttpClient 实现 OAuth2 Refresh Token → Access Token
- **UI 完整**：GitHub 暗色主题，三栏布局，支持 GridSplitter 调整宽度
- **批量导入**：支持 `邮箱----密码----clientid----token` 及 `邮箱----密码` 格式
- **搜索过滤**：按主题/发件人实时搜索（仅邮件，账号列表不支持搜索）
- **分页加载**：邮件 PageSize=20，账号 PageSize=30，均支持翻页
- **批量操作**：全选/取消全选、批量删除账号、批量恢复/删除对标
- **级联删除**：删账号自动删除关联邮件
- **HTTP API**：内置 HttpListener 服务器，提供邮箱分配/验证码获取/对标管理接口
- **验证码提取**：从 Instagram 邮件中提取 6 位数字验证码
- **对标管理**：支持添加/删除/恢复/原子分配对标条目，带统计面板
- **版本更新**：检查 GitHub Releases，弹出更新对话框

## 关键约定

- **导入格式**：`邮箱----密码----clientid----token`（支持 2 项或 4 项）
- **自动检测**：IMAP 密码认证优先 → XOAUTH2 备选，自动选用可用协议
- **UI 风格**：紧凑窗口，每行账号右侧提供操作按钮（不用右键菜单）
- **删除策略**：删账号时级联删除所有邮件（外键 ON DELETE CASCADE + 代码双重保障）
- **安全**：密码和 Token 使用 AES-256-GCM 加密存储；API 通信使用 SHA256 签名
- **卡密**：启动时读本地缓存 → 调服务端 Verify → 启动心跳 (5min本地+1h服务端)
- **API 激活网关**：所有 `/api/` 路径需卡密激活后才可用
- **Token 过期**：Graph API 401 时标记过期（未实现）

## 开发命令

```bash
dotnet build              # 编译
dotnet run                # 运行
dotnet run -- --test-signature [卡密]  # 签名诊断
dotnet add package <name> # 安装 NuGet 包
```

## NuGet 依赖

| 包 | 版本 | 用途 |
|---|---|---|
| `Avalonia` | 12.0.2 | UI 框架 |
| `Avalonia.Desktop` | 12.0.2 | 桌面支持 |
| `Avalonia.Themes.Fluent` | 12.0.2 | Fluent 主题 |
| `Avalonia.Fonts.Inter` | 12.0.2 | Inter 字体 |
| `AvaloniaUI.DiagnosticsSupport` | 2.2.1 | 调试支持 |
| `CommunityToolkit.Mvvm` | 8.4.1 | MVVM 工具箱 |
| `MailKit` | 4.16.0 | IMAP/XOAUTH2 邮件协议 |
| `Microsoft.Data.Sqlite` | 10.0.7 | SQLite 数据库 |

## 已知问题（待修复）

1. **AuthDetectService.cs** — `_imap` 和 `_graph` 字段未初始化，运行时调用会 NullReferenceException
2. **Attachments 提取** — `HasAttachments` 硬编码为 false
3. **系统剪贴板集成** — `CopyEmailCommand` 仅更新状态，未实际复制
4. **Token 过期标记** — Graph API 401 时未自动标记账号过期
5. **账号搜索** — 不支持按邮箱关键字搜索账号列表

## HTTP API（5000 端口，局域网可访问）

| 接口 | 说明 |
|---|---|
| `GET /api/email` | 分配一个未使用邮箱 |
| `GET /api/accounts?page=&pageSize=` | 分页列出邮箱 |
| `GET /api/code?email=&keyword=&minutes=` | 取最新验证码（默认 30 分钟内，可按 from/subject/body 关键词过滤） |
| `GET /api/status?email=` | 查邮箱分配状态 |
| `GET /api/target/allocate` | 原子分配一条对标关键词 |
| `GET /api/target/count` | 关键词可用/已用/总数 |
| `GET /docs` | 在线接口文档 |

### 监听绑定策略（`HttpServer.ConfigurePrefixes`）

1. 先 probe 通配符 `http://*:5000/`，能绑定则直接用；
2. 不能（Windows URL ACL 限制）→ 退化为枚举 `WindowsNetworkHelper.GetSortedLanIPv4()`，给每个 IP 单独绑定 `http://192.168.x.x:5000/`，loopback 也一起；
3. 单 IP 绑定无需管理员权限，绕开 URL ACL。
4. 启动后 `App.axaml.cs` 调 `WindowsNetworkHelper.TryRegisterFirewallRule(5000)` 尝试自动注册 Windows 防火墙入站规则（非管理员静默失败）；
5. UI 状态栏会显示所有实际可访问 URL 列表。

如果用户仍然在局域网访问不到，提示用管理员 CMD 跑：
```
netsh http add urlacl url=http://*:5000/ user=Everyone
netsh advfirewall firewall add rule name="OutlookApp HTTP API" dir=in action=allow protocol=TCP localport=5000
```

## 验证码提取（`Api/VerificationExtractor.cs` + `Services/EmailSyncOnDemand.cs`）

- **不再硬编码 instagram**：`/api/code` 通过 `keyword` 查询参数按需过滤；不传则匹配所有发件人
- **时间窗口**：默认只看最近 30 分钟内的邮件，可用 `minutes` 参数调整
- **正则优先级**：`NNN-NNN / NNN NNN / NNN.NNN` → 单独 6 位 → 单独 4~8 位
- **过滤伪验证码**：排除全重复（000000）和年份（1900~2099）
- **认证链路**：`EmailSyncOnDemand` 内部按 密码 → `GraphEmailService.RefreshTokenAsync` → IMAP XOAUTH2 顺序尝试；ImapEmailService 不再吞异常，由 EmailSyncOnDemand 决定 fallback

## 注意事项

- macOS 上开发，使用 Avalonia（非 WPF，WPF 仅限 Windows）
- `$parent[Window]` 用于子控件绑定到窗口 DataContext
- ViewModel 方法使用 `[RelayCommand]`，异步方法需返回 `Task`
- 密钥派生基于 MachineName + UserDomainName + UserName，换机器后无法解密
- `ApiSecurityService.ProductKey` 为占位符，部署前需替换为真实密钥
- 卡密 API 服务端地址配置在 `ApiSecurityService.ServerBase`
- 修改 UI 前读此文件了解架构
