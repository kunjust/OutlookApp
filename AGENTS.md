# OutlookApp

Outlook 邮件管理桌面应用。紧凑窗口设计，不干扰其他软件使用。

## 技术栈

- **.NET 8** + **Avalonia UI**（跨平台 XAML 框架，API 与 WPF 高度一致）
- **SQLite** (Microsoft.Data.Sqlite) + **AES-256-GCM** 字段级加密（已实现）
- **IMAP** + **XOAUTH2** 双协议（已实现）；Microsoft Graph API（部分实现）
- **MVVM** 架构 (CommunityToolkit.Mvvm)

## 项目结构

```
OutlookApp/
├── Models/
│   ├── EmailAccount.cs       # 邮箱账号模型 (Email, Password, ClientId, Token, Status)
│   └── EmailMessage.cs       # 邮件模型 (Subject, From, Body, Attachments, etc.)
├── ViewModels/
│   ├── ViewModelBase.cs      # ObservableObject 基类
│   ├── MainWindowViewModel.cs  # 主窗口 VM (账号列表 + 邮件列表 + 完整 CRUD)
│   └── ImportDialogViewModel.cs # 导入对话框 VM
├── Views/
│   ├── MainWindow.axaml      # 主窗口 (三栏布局: 账号 / 邮件列表 / 详情)
│   ├── MainWindow.axaml.cs
│   ├── ImportDialog.axaml    # 导入对话框 (粘贴 + 文件选择)
│   └── ImportDialog.axaml.cs
├── Services/
│   ├── DatabaseService.cs    # SQLite CRUD + 外键级联删除
│   ├── AuthDetectService.cs  # 协议自动检测 (Token → IMAP XOAUTH2 → 密码)
│   ├── IEmailService.cs      # 邮件服务接口
│   ├── ImapEmailService.cs   # IMAP 认证 + XOAUTH2 + 邮件获取
│   ├── GraphEmailService.cs  # OAuth2 Token 刷新 (邮件获取空实现)
│   └── EncryptionService.cs  # AES-256-GCM 加解密
├── App.axaml / App.axaml.cs
├── Program.cs
├── ViewLocator.cs            # VM → View 自动映射
└── AGENTS.md
```

## 当前状态

- 数据库层完整：SQLite CRUD + AES-256-GCM 字段级加密
- 邮件服务：IMAP 密码认证 + XOAUTH2 双协议已完整实现
- 协议检测：自动检测可用协议 (AuthDetectService)
- Token 刷新：通过 raw HttpClient 实现 OAuth2 Token 刷新
- UI 完整：GitHub 暗色主题，三栏布局，支持 GridSplitter 调整宽度
- 批量导入：支持 `邮箱----密码----clientid----token` 及 `邮箱----密码` 格式
- 搜索过滤：按主题/发件人实时搜索
- 分页加载：PageSize=20，按"加载更多"按钮追加
- 级联删除：删账号自动删除关联邮件
- 批量操作：全选/取消全选、批量删除、全部刷新

## 关键约定

- **导入格式**：`邮箱----密码----clientid----token`（支持 2 项或 4 项）
- **自动检测**：IMAP XOAUTH2 → IMAP 密码认证依次尝试，自动选用可用协议
- **UI 风格**：紧凑窗口，每行账号右侧提供操作按钮（不用右键菜单）
- **删除策略**：删账号时级联删除所有邮件（外键 ON DELETE CASCADE + 代码双重保障）
- **安全**：密码和 Token 使用 AES-256-GCM 加密存储（已实现）
- **Token 过期**：Graph API 401 时标记过期（未实现）

## 开发命令

```bash
dotnet build              # 编译
dotnet run                # 运行
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

待添加：`Microsoft.Graph`（Graph API SDK 集成）

## 待实现功能

1. **Microsoft.Graph SDK** -- 安装包并通过 Graph REST API 获取邮件
2. **Token 过期标记** -- Graph API 401 时自动标记账号过期
3. **Attachments 提取** -- 当前 `HasAttachments` 硬编码为 false
4. **系统剪贴板集成** -- `CopyEmailCommand` 仅更新状态，未实际复制

## 注意事项

- macOS 上开发，使用 Avalonia（非 WPF，WPF 仅限 Windows）
- `$parent[Window]` 用于子控件绑定到窗口 DataContext
- ViewModel 方法使用 `[RelayCommand]`，异步方法需返回 `Task`
- 密钥派生基于 MachineName + UserDomainName + UserName，换机器后无法解密
- 修改 UI 前读此文件了解架构
