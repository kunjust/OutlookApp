# OutlookApp

Outlook 邮件管理桌面应用。紧凑窗口设计，不干扰其他软件使用。

## 技术栈

- **.NET 8** + **Avalonia UI**（跨平台 XAML 框架，API 与 WPF 高度一致）
- **SQLite** (Microsoft.Data.Sqlite) + **AES-256-GCM** 字段级加密（待实现）
- **Microsoft Graph API** + **MailKit** (IMAP, XOAUTH2) 双协议（待实现）
- **MVVM** 架构 (CommunityToolkit.Mvvm)

## 项目结构

```
OutlookApp/
├── Models/
│   ├── EmailAccount.cs       # 邮箱账号模型 (Email, Password, ClientId, Token, Status)
│   └── EmailMessage.cs       # 邮件模型 (Subject, From, Body, Attachments, etc.)
├── ViewModels/
│   ├── ViewModelBase.cs      # ObservableObject 基类
│   ├── MainWindowViewModel.cs  # 主窗口 VM (账号列表 + 邮件列表 + 操作命令)
│   └── ImportDialogViewModel.cs # 导入对话框 VM
├── Views/
│   ├── MainWindow.axaml      # 主窗口 (左账号列表 / 右邮件列表 + 详情)
│   ├── MainWindow.axaml.cs
│   ├── ImportDialog.axaml    # 导入对话框 (粘贴 + 协议检测)
│   └── ImportDialog.axaml.cs
├── Services/                 # TODO: 数据库、邮件服务
├── App.axaml / App.axaml.cs
├── Program.cs
└── AGENTS.md
```

## 当前状态

- 已搭建 UI 框架，使用示例数据展示布局
- 导入对话框支持 `邮箱----密码----clientid----token` 格式粘贴 + 模拟协议检测
- 账号列表显示状态标识 ✓/✗，每行有 Copy/Del/Ref 按钮
- 点击账号加载示例邮件列表，选中邮件显示详情
- 搜索框、分页、级联删除等功能待实现

## 关键约定

- **导入格式**：`邮箱----密码----clientid----token`
- **自动检测**：IMAP → XOAUTH2 → Graph API 依次尝试，自动选用可用协议
- **UI 风格**：紧凑窗口，每行账号右侧提供操作按钮（不用右键菜单）
- **删除策略**：删除账号时级联删除所有邮件
- **安全**：密码和 token 需 AES-256-GCM 加密存储（待实现）
- **Token 过期**：Graph API 401 时标记过期（待实现）

## 开发命令

```bash
dotnet build              # 编译
dotnet run                # 运行
dotnet add package <name> # 安装 NuGet 包
```

## NuGet 依赖

- `Avalonia` / `Avalonia.Desktop` / `Avalonia.Themes.Fluent` (12.0.2)
- `CommunityToolkit.Mvvm` (8.4.1)

待添加：`Microsoft.Data.Sqlite`、`MailKit`、`Microsoft.Graph`

## 注意事项

- macOS 上开发，使用 Avalonia（非 WPF，WPF 仅限 Windows）
- `$parent[Window]` 用于子控件绑定到窗口 DataContext
- ViewModel 方法使用 `[RelayCommand]`，异步方法需返回 `Task`
- 修改 UI 前读此文件了解架构
