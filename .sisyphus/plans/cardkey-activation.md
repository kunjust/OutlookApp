# 卡密激活系统

## TL;DR

> **Quick Summary**: 为 OutlookApp 增加卡密激活功能，应用启动时先验证卡密，未激活则弹出激活窗口，已激活则进入主窗口并定时心跳保活。所有内部 API（port 5000）均受激活状态保护。
>
> **Deliverables**:
> - 激活窗口（输入卡密 + 激活/解绑）
> - 卡密验证服务（HMAC签名 + AES解密 + 5个API调用）
> - 本地隐藏文件缓存（加密存储卡密信息）
> - 1分钟心跳保活 + 3次重试退出
> - 主窗口标题栏显示到期时间
> - 内置API网关（port 5000全部接口受控）
> 
> **Estimated Effort**: Medium
> **Parallel Execution**: YES - 3 waves
> **Critical Path**: 许可证服务 → 激活窗口 → 启动流程集成 → 心跳/API网关

---

## Context

### Original Request
现有 OutlookApp 无任何登录/鉴权机制，用户需增加卡密激活功能：只有输入合法的卡密并激活后，才能使用软件。

### Interview Summary

**Key Discussions**:
- **激活流程**：App启动 → 检测本地缓存 → 有缓存则 Verify → 无缓存则显示激活窗口 → Activate → 进入主窗口
- **心跳机制**：1分钟间隔，3次失败则退出
- **硬件指纹**：MAC地址作为 deviceId/hardwareId
- **服务器时间**：到期时间以服务器返回为准
- **通知方式**：一律用弹框(popup)，不用 toast
- **解绑功能**：提供解绑按钮，解绑后回到激活界面
- **API 网关**：HttpServer(port 5000) 所有接口增加激活校验

**Research Findings**:
- 项目已存在 `EncryptionService.cs` (AES-256-GCM)，但卡密API用的是 **AES-CBC**（不同加密方案）
- HttpServer 使用 `HttpListener` 原始路由分发，无框架
- `App.axaml.cs` 是启动入口，可直接插入激活校验逻辑

### API Documentation

| 端点 | 方法 | 功能 |
|---|---|---|
| `/api/v1/Auth/Activate` | POST | 激活卡密，绑定设备 |
| `/api/v1/Auth/Verify` | POST | 验证会话有效性 |
| `/api/v1/Auth/Heartbeat` | POST | 心跳保活 |
| `/api/v1/Device/Unbind` | POST | 解绑设备 |
| `/api/v1/Device/Query` | GET | 查询卡密信息 |

所有请求需携带 `X-Timestamp`, `X-Nonce`, `X-Signature` 三个安全头。
成功响应(200)是 AES 加密的 Base64；错误响应是明文 JSON `{"success":false,"message":"..."}`。

### Crypto Details (confirmed)

| 要素 | 值 |
|---|---|
| 产品密钥(测试) | `testkey123456789`（写死占位，后续替换） |
| HMAC-SHA256 签名 | message=`body + productKey + timestamp`, key=`productKey`, 输出小写hex |
| AES-CBC 解密 | Key=`SHA256(productKey)`, IV=`Base64密文前16字节`, Padding=`PKCS7` |

---

## Work Objectives

### Core Objective
实现卡密激活系统，用户必须通过卡密验证后才能使用 OutlookApp 的全部功能。

### Concrete Deliverables
- `Services/ApiSecurityService.cs` — HMAC 签名生成 + AES-CBC 解密
- `Services/LicenseService.cs` — Activate/Verify/Heartbeat/Unbind/Query API调用
- `Services/HardwareService.cs` — MAC 地址获取
- `Services/LicenseStorageService.cs` — 本地隐藏文件加密缓存
- `Models/LicenseInfo.cs` — 卡密信息模型
- `ViewModels/ActivationViewModel.cs` — 激活窗口 ViewModel
- `Views/ActivationWindow.axaml` + `.cs` — 激活窗口 UI
- 修改 `App.axaml.cs` — 启动时添加激活校验流程
- 修改 `MainWindowViewModel.cs` — 增加心跳定时器、标题到期时间、解绑命令
- 修改 `MainWindow.axaml` — 标题栏显示到期时间 + 解绑按钮
- 修改 `Api/HttpServer.cs` — 所有接口增加激活状态检查

### Definition of Done
- [ ] 首次启动无缓存 → 显示激活窗口
- [ ] 输入测试卡密 → Activate成功 → 进入主窗口
- [ ] 标题栏显示"剩余 X 天"
- [ ] 心跳 1分钟间隔，3次失败退出
- [ ] 重启应用 → 本地缓存 Valid → Verify成功 → 直接进入主窗口
- [ ] 解绑 → 退出主窗口 → 回到激活窗口
- [ ] port 5000 API 在未激活时返回 403

### Must Have
- 激活窗口 UI（输入框 + 激活按钮 + 状态提示）
- LicenseService 完整实现 5个 API 调用
- HMAC 签名 + AES-CBC 解密的正确实现
- 本地隐藏文件存储
- 心跳定时器（1分钟）
- 标题栏到期时间显示（服务器时间为准）
- 解绑功能
- API 网关激活检查

### Must NOT Have (Guardrails)
- 不要实现用户注册/密码找回功能
- 不要改变现有的邮件收发逻辑
- 不要修改 DatabaseService 已有的表结构
- 不要影响现有的 HttpServer 路由结构（只增加中间件）
- 不要用 toast 通知 — 一律用弹框

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** - ALL verification is agent-executed.

### Test Decision
- **Infrastructure exists**: YES (项目已有 .NET 编译能力)
- **Automated tests**: None (Agent QA only)
- **Framework**: N/A

### QA Policy
Every task MUST include agent-executed QA scenarios (see TODO template).
Evidence saved to `.sisyphus/evidence/task-{N}-{scenario-slug}.{ext}`.

- **Backend/Crypto**: Bash (dotnet run / curl) — Verify HMAC produces correct hash, AES decrypts correctly
- **UI**: The app is Avalonia desktop — QA via building and running, checking console output
- **API Gateway**: curl to port 5000 — verify 403 before activation, 200 after

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Foundation — start immediately, MAX PARALLEL):
├── Task 1: ApiSecurityService — HMAC签名 + AES-CBC解密 [quick]
├── Task 2: HardwareService — MAC地址获取 [quick]
├── Task 3: LicenseInfo 模型 [quick]
├── Task 4: LicenseStorageService — 本地隐藏文件缓存 [quick]
└── Task 5: LicenseService — 5个API调用封装 [quick]

Wave 2 (UI + Integration — after Wave 1):
├── Task 6: ActivationWindow + ActivationViewModel [visual-engineering]
├── Task 7: App.axaml.cs — 启动流程激活校验 [unspecified-high]
├── Task 8: MainWindow — 标题栏到期时间 [visual-engineering]
└── Task 9: MainWindowViewModel — 心跳定时器 + 解绑命令 [unspecified-high]

Wave 3 (API Gate + Final):
├── Task 10: HttpServer — API激活网关 [unspecified-high]
└── Task F1-F4: Final Verification Wave

Critical Path: Task 1 → Task 5 → Task 6,7 → Task 9 → Task 10 → Final Verification
```

---

## TODOs

- [ ] 1. ApiSecurityService — HMAC签名 + AES-CBC解密

  **What to do**:
  - 创建 `Services/ApiSecurityService.cs` 静态类
  - 实现 `GenerateSignature(string body, long timestamp, string productKey) → string`
    - 拼接: body + productKey + timestamp
    - 使用 `HMACSHA256` 计算，key = UTF8(productKey)
    - 输出小写 hex 字符串
  - 实现 `DecryptResponse(string encryptedBase64, string productKey) → string`
    - Base64 解码
    - 取前 16 字节作为 IV
    - 剩余部分作为密文
    - Key = SHA256(UTF8(productKey))
    - AES-CBC + PKCS7 解密
    - 返回 UTF8 字符串
  - 实现 `GenerateNonce() → string` (GUID)
  - 实现 `GetTimestamp() → long` (Unix秒)

  **Must NOT do**:
  - 不要修改已有的 EncryptionService
  - 不要涉及网络调用，只做纯计算

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: None needed — pure .NET crypto code, straightforward implementation

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 2, 3, 4)
  - **Blocks**: Task 5 (LicenseService)
  - **Blocked By**: None (foundation task)

  **References**:
  - `Services/EncryptionService.cs` — 参考现有的 AES-256-GCM 实现风格，但注意卡密API用的是 AES-CBC
  - API文档: HMAC-SHA256 → `System.Security.Cryptography.HMACSHA256`
  - API文档: AES-CBC → `System.Security.Cryptography.Aes.Create()`

  **Acceptance Criteria**:

  **QA Scenarios**:

  ```
  Scenario: HMAC-SHA256 签名验证
    Tool: Bash (dotnet script / C# interactive)
    Preconditions: 已知产品密钥 "testkey123456789"
    Steps:
      1. 调用 GenerateSignature(body="", timestamp=1778328614, key="testkey123456789")
      2. 与 Python HMAC-SHA256("testkey123456789", "") 结果对比
    Expected Result: 签名生成无异常，格式为 64位小写hex
    Evidence: .sisyphus/evidence/task-1-hmac-valid.txt

  Scenario: AES-CBC 解密
    Tool: Bash (dotnet script)
    Preconditions: 已知产品密钥 "testkey123456789"，已知加密响应
    Steps:
      1. 模拟一个加密响应（用 C# AES-CBC 加密一段已知 JSON）
      2. 调用 DecryptResponse 解密
      3. 确认解密结果与原文一致
    Expected Result: 解密正确，返回原文 JSON
    Evidence: .sisyphus/evidence/task-1-aes-decrypt.txt
  ```

  **Evidence to Capture**:
  - [ ] 签名算法测试输出
  - [ ] AES 解密测试输出

  **Commit**: YES
  - Message: `feat: add ApiSecurityService with HMAC-SHA256 signing and AES-CBC decryption`
  - Files: `Services/ApiSecurityService.cs`

---

- [ ] 2. HardwareService — MAC 地址获取

  **What to do**:
  - 创建 `Services/HardwareService.cs` 静态类
  - 实现 `GetDeviceId() → string` — 返回 MAC 地址
  - 实现 `GetHardwareId() → string` — 与 GetDeviceId 相同（用户确认两个都用 MAC）
  - MAC地址获取方式：使用 `System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()`
    - 过滤掉 Loopback、非以太网接口
    - 取第一个非空 MAC 地址
    - 格式化为 `XX-XX-XX-XX-XX-XX`（大写）
  - 缓存结果避免重复查询

  **Must NOT do**:
  - 不要采集用户隐私信息（仅MAC地址）
  - 不要做网络请求

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: None

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 3, 4)
  - **Blocks**: Task 5 (LicenseService needs it)
  - **Blocked By**: None

  **Acceptance Criteria**:

  **QA Scenarios**:

  ```
  Scenario: MAC 地址获取
    Tool: Bash (dotnet run 测试)
    Preconditions: macOS 网络接口正常
    Steps:
      1. 调用 HardwareService.GetDeviceId()
      2. 输出结果
    Expected Result: 返回格式如 "00-1A-2B-3C-4D-5E" 的非空字符串
    Evidence: .sisyphus/evidence/task-2-mac-address.txt
  ```

  **Evidence to Capture**:
  - [ ] MAC 地址输出

  **Commit**: YES (groups with Task 1)
  - Message: `feat: add HardwareService for MAC address retrieval`
  - Files: `Services/HardwareService.cs`

---

- [ ] 3. LicenseInfo 模型

  **What to do**:
  - 创建 `Models/LicenseInfo.cs`
  - 继承 `ObservableObject`
  - 使用 `[ObservableProperty]` 源生成器
  - 字段：
    - `CardKey` — 卡密字符串
    - `DeviceId` — 设备ID(MAC)
    - `HardwareId` — 硬件ID(MAC)
    - `ExpiryTime` — 过期时间(DateTime)
    - `ServerTime` — 服务器当前时间(DateTime)
    - `ActivatedAt` — 激活时间(DateTime)
    - `LastVerifiedAt` — 最后验证时间(DateTime)
    - `IsActive` — bool，根据当前服务器时间判断是否过期
  - 添加 `TimeRemaining` 计算属性（格式化为"X天X小时"）
  - 添加 JSON 序列化/反序列化方法（用于本地缓存读写）

  **Must NOT do**:
  - 不要添加与卡密无关的字段
  - 不要依赖数据库

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: None

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2, 4)
  - **Blocks**: Task 4, Task 5, Task 6
  - **Blocked By**: None

  **References**:
  - `Models/EmailAccount.cs` — 参照现有 ObservableObject 模型
  - `Models/KeywordItem.cs` — 参照现有 ObservableObject 模型

  **Acceptance Criteria**:

  **QA Scenarios**:

  ```
  Scenario: LicenseInfo 模型创建和 JSON 序列化
    Tool: Bash (dotnet script)
    Steps:
      1. 创建 LicenseInfo 实例，设置各字段
      2. 序列化为 JSON
      3. 从 JSON 反序列化
    Expected Result: 反序列化结果与原始对象字段一致
    Evidence: .sisyphus/evidence/task-3-model-serialize.txt
  ```

  **Evidence to Capture**:
  - [ ] 序列化测试输出

  **Commit**: YES (groups with Task 1, 2)
  - Message: `feat: add LicenseInfo model for card key license data`
  - Files: `Models/LicenseInfo.cs`

---

- [ ] 4. LicenseStorageService — 本地隐藏文件加密缓存

  **What to do**:
  - 创建 `Services/LicenseStorageService.cs`
  - 存储路径：`~/.outlookapp/license.dat`（隐藏文件，使用 `~/.outlookapp/` 目录）
  - 存储格式：`LicenseInfo` JSON 序列化 → AES-256-GCM 加密 → Base64 写入文件
    - 复用现有的 `EncryptionService`（AES-256-GCM 但使用产品密钥派生）或直接用 `ApiSecurityService` 的 AES-CBC
    - **注意**：用户数据在本地，用现有 `EncryptionService` 的机器绑定加密更合适
  - 方法：
    - `SaveAsync(LicenseInfo license)` — 写入隐藏文件
    - `LoadAsync() → LicenseInfo?` — 读取隐藏文件，解密失败返回 null
    - `ClearAsync()` — 删除隐藏文件
    - `ExistsAsync() → bool` — 判断缓存是否存在
  - 确保目录 `~/.outlookapp/` 在首次写入时自动创建，并设置隐藏属性（macOS 上以 `.` 开头即为隐藏）

  **Must NOT do**:
  - 不要涉及 API 调用，只做本地 IO
  - 不要用明文存储卡密信息

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: None

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2, 3)
  - **Blocks**: Task 5, Task 7
  - **Blocked By**: Task 3 (needs LicenseInfo model)

  **References**:
  - `Services/EncryptionService.cs:20-47` — 参考 AES-256-GCM 加密模式

  **Acceptance Criteria**:

  **QA Scenarios**:

  ```
  Scenario: 本地缓存写入和读取
    Tool: Bash (dotnet script)
    Steps:
      1. 创建 LicenseInfo 实例
      2. 调用 SaveAsync 保存
      3. 调用 LoadAsync 加载
      4. 比较加载结果与原始数据
    Expected Result: 读写一致，文件存储在 ~/.outlookapp/license.dat
    Evidence: .sisyphus/evidence/task-4-storage-roundtrip.txt

  Scenario: 缓存不存在时返回 null
    Tool: Bash (dotnet script)
    Steps:
      1. 调用 ClearAsync 清除缓存
      2. 调用 LoadAsync
    Expected Result: 返回 null
    Evidence: .sisyphus/evidence/task-4-storage-clear.txt
  ```

  **Evidence to Capture**:
  - [ ] 加密读写测试输出
  - [ ] 文件路径确认

  **Commit**: YES (groups with Tasks 1-3)
  - Message: `feat: add LicenseStorageService for local hidden file cache`
  - Files: `Services/LicenseStorageService.cs`

---

- [ ] 5. LicenseService — 卡密 API 调用封装

  **What to do**:
  - 创建 `Services/LicenseService.cs`
  - 引用 `ApiSecurityService`、`HardwareService`、`LicenseStorageService`
  - 产品密钥常量：`private const string ProductKey = "testkey123456789";`（后续替换）
  - 服务端地址常量：`private const string ServerBase = "http://localhost:5001";`
  - 每个方法自动生成 `X-Timestamp`、`X-Nonce`、`X-Signature`
  - 每个方法自动处理响应：解密 AES → 解析 JSON → 返回强类型结果（或抛出异常）
  - 方法：
    - `ActivateAsync(string cardKey) → ActivationResult`
      - POST `/api/v1/Auth/Activate`
      - Body: `{cardKey, deviceId, hardwareId, osPlatform}`
      - 返回过期时间和服务器时间
    - `VerifyAsync(string cardKey) → VerifyResult`
      - POST `/api/v1/Auth/Verify`
      - Body: `{cardKey, deviceId, hardwareId}`
      - 返回有效状态 + 过期时间 + 服务器时间
    - `HeartbeatAsync(string cardKey) → HeartbeatResult`
      - POST `/api/v1/Auth/Heartbeat`
      - Body: `{cardKey, deviceId, hardwareId}`
      - 返回服务器时间
    - `UnbindAsync(string cardKey, string? reason) → bool`
      - POST `/api/v1/Device/Unbind`
      - Body: `{cardKey, deviceId, hardwareId, reason}`
    - `QueryAsync(string cardKey) → QueryResult`
      - GET `/api/v1/Device/Query?cardKey=xxx`
      - 返回卡密详情
  - 每个方法的返回类型定义为 `record` 或简单 DTO
  - 使用 `HttpClient` 单例
  - 错误处理：API返回 `{"success":false,"message":"..."}` 时抛出 `LicenseException`

  **Must NOT do**:
  - 不要实现心跳定时器（那是 ViewModel 的职责）
  - 不要直接操作 UI

  **Recommended Agent Profile**:
  - **Category**: `quick`
  - **Skills**: None

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1
  - **Blocks**: Task 6, Task 7, Task 9 (all depend on this)
  - **Blocked By**: Task 1 (ApiSecurityService), Task 2 (HardwareService), Task 3 (LicenseInfo), optionally Task 4 (LicenseStorageService)

  **References**:
  - `Services/GraphEmailService.cs` — 参考现有的 HttpClient 使用模式
  - `Services/ApiSecurityService.cs` (Task 1) — 签名和加密
  - `Services/HardwareService.cs` (Task 2) — 设备信息

  **Acceptance Criteria**:

  **QA Scenarios**:

  ```
  Scenario: Activate API 调用
    Tool: Bash (dotnet run test)
    Preconditions: 卡密服务运行在 localhost:5001
    Steps:
      1. 调用 LicenseService.ActivateAsync("TEST-CARD-001")
      2. 观察返回结果或异常
    Expected Result: API调用正常，返回不为null（可能因密钥无效而抛出 LicenseException，但验证流程正确）
    Evidence: .sisyphus/evidence/task-5-activate-call.txt

  Scenario: Verify API 调用
    Tool: Bash (dotnet run test)
    Preconditions: 同上
    Steps:
      1. 调用 LicenseService.VerifyAsync("TEST-CARD-001")
    Expected Result: API 调用流程正确
    Evidence: .sisyphus/evidence/task-5-verify-call.txt
  ```

  **Evidence to Capture**:
  - [ ] API 调用测试输出

  **Commit**: YES
  - Message: `feat: add LicenseService for card key API integration`
  - Files: `Services/LicenseService.cs`

---

- [ ] 6. ActivationWindow + ActivationViewModel

  **What to do**:
  - 创建 `ViewModels/ActivationViewModel.cs`
    - 继承 `ViewModelBase`
    - 属性：`CardKey`(输入绑定), `StatusMessage`(状态提示), `IsLoading`(加载中), `IsError`(错误状态)
    - 命令：`ActivateCommand` → 调 `LicenseService.ActivateAsync` → 成功则触发事件 `ActivationSucceeded`
    - 输入验证：卡密不能为空
    - 错误显示：API 返回的错误信息展示在 StatusMessage
  - 创建 `Views/ActivationWindow.axaml` + `.cs`
    - 窗口标题："激活 — OutlookApp"
    - 延续现有 GitHub 暗色主题（#0D1117 背景）
    - 居中布局，紧凑窗口（400x300）
    - UI 元素：
      - 标题文本："请输入卡密激活"
      - TextBox：卡密输入（密码模式，PasswordChar）
      - 激活按钮（主色 #58A6FF）
      - 状态文本：显示加载中/错误信息（红色#F85149 / 绿色#3FB950）
    - 窗口关闭行为：禁止直接关闭（必须激活成功）

  **Must NOT do**:
  - 不要在主窗口启动后才弹出激活窗口
  - 不要依赖数据库

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
  - **Skills**: None

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2 (with Tasks 7, 8)
  - **Blocks**: Task 7 (App startup needs this window)
  - **Blocked By**: Task 5 (LicenseService)

  **References**:
  - `Views/MainWindow.axaml` — 参考现有 GitHub 暗色主题风格
  - `Views/MainWindow.axaml.cs` — 窗口代码后端模式
  - `ViewModels/ImportDialogViewModel.cs` — 对话框 VM 模式
  - `Views/ImportDialog.axaml` — 对话框 UI 模式

  **Acceptance Criteria**:

  **QA Scenarios**:

  ```
  Scenario: 激活窗口渲染
    Tool: Bash (dotnet build)
    Preconditions: 代码编译通过
    Steps:
      1. dotnet build
    Expected Result: 编译成功，无错误
    Evidence: .sisyphus/evidence/task-6-build.txt
  ```

  **Evidence to Capture**:
  - [ ] 编译成功

  **Commit**: YES
  - Message: `feat: add ActivationWindow and ActivationViewModel`
  - Files: `Views/ActivationWindow.axaml`, `Views/ActivationWindow.axaml.cs`, `ViewModels/ActivationViewModel.cs`

---

- [ ] 7. App.axaml.cs — 启动流程激活校验

  **What to do**:
  - 修改 `App.axaml.cs` 的 `OnFrameworkInitializationCompleted` 方法
  - 新启动流程：
    1. 初始化 DatabaseService, KeywordService
    2. **不直接创建 MainWindow**
    3. 创建 ActivationViewModel
    4. 显示 ActivationWindow（ShowDialog）
    5. 激活成功（ActivationSucceeded 事件）→ **关闭 ActivationWindow**
    6. **然后** 创建 MainWindow + MainWindowViewModel + HttpServer
    7. 如果已有本地缓存 → 验证通过 → 跳过激活窗口
  - 异步等待激活完成后再启动主窗口
  - 使用 `ShowDialog` 模态方式确保激活窗口是唯一交互窗口
  - 激活成功后立即启动心跳定时器

  **Must NOT do**:
  - 不要删掉现有的 DatabaseService/KeywordService 初始化
  - 不要修改 Program.cs

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: None

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2
  - **Blocks**: Nothing (but depends on Tasks 5, 6)
  - **Blocked By**: Task 5 (LicenseService), Task 6 (ActivationWindow)

  **References**:
  - `App.axaml.cs` — 完整重构启动流程

  **Acceptance Criteria**:

  **QA Scenarios**:

  ```
  Scenario: 启动流程验证
    Tool: Bash (dotnet build)
    Preconditions: 代码编译通过
    Steps:
      1. dotnet build
      2. dotnet run（验证启动时先弹出激活窗口）
    Expected Result: 编译成功，启动后先显示激活窗口而非主窗口
    Evidence: .sisyphus/evidence/task-7-startup-flow.txt
  ```

  **Evidence to Capture**:
  - [ ] 编译通过

  **Commit**: YES (groups with Task 9)
  - Message: `feat: add activation gate to app startup flow`
  - Files: `App.axaml.cs`

---

- [ ] 8. MainWindow — 标题栏到期时间

  **What to do**:
  - 修改 `MainWindow.axaml` 窗口标题绑定
  - Window.Title 绑定到 ViewModel 的 `WindowTitle` 属性
  - 格式：`OutlookApp — 剩余 X 天 X 小时`（卡密有效时）
  - 卡密过期时：`OutlookApp — 卡密已过期`
  - 添加解绑按钮到顶部工具栏
    - 按钮文字："解绑"
    - 颜色：红色 #F85149
    - 点击触发 `UnbindCommand`
    - 位置：工具栏右侧，状态文字旁边
  - 解绑按钮点击后弹出确认弹框："确定要解绑卡密吗？此操作将释放当前设备绑定。"
  - 解绑成功后：关闭主窗口，重新显示激活窗口

  **Must NOT do**:
  - 不要改动现有的三栏布局
  - 不要删除现有的功能按钮

  **Recommended Agent Profile**:
  - **Category**: `visual-engineering`
  - **Skills**: None

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2
  - **Blocks**: Nothing
  - **Blocked By**: Nothing directly

  **References**:
  - `Views/MainWindow.axaml` — 当前UI文件，在此基础上修改
  - `MainWindowViewModel.cs` — 属性绑定

  **Acceptance Criteria**:

  **QA Scenarios**:

  ```
  Scenario: 标题栏更新
    Tool: Bash (dotnet build)
    Preconditions: 代码编译通过
    Steps:
      1. dotnet build
    Expected Result: 编译成功
    Evidence: .sisyphus/evidence/task-8-build.txt
  ```

  **Evidence to Capture**:
  - [ ] 编译通过

  **Commit**: YES (groups with Task 9)
  - Message: `feat(ui): add unbind button and expiry time to title bar`
  - Files: `Views/MainWindow.axaml`

---

- [ ] 9. MainWindowViewModel — 心跳定时器 + 解绑命令

  **What to do**:
  - 修改 `MainWindowViewModel.cs`
  - 新增属性：
    - `WindowTitle` — 绑定到窗口标题（更新到期时间时自动更新）
    - `LicenseInfo` — 当前卡密信息
  - 新增命令：
    - `UnbindCommand` — 调 LicenseService.UnbindAsync → 清本地缓存 → 触发重新激活流程
  - 心跳定时器：
    - 使用 `System.Timers.Timer`，间隔 60000ms（1分钟）
    - 每次触发：调用 `LicenseService.HeartbeatAsync`
    - 成功 → 更新本地缓存 + 更新标题到期时间
    - 失败 → 重试计数，3次失败 → 弹框提示"卡密验证失败，即将退出" → Application.Current.Shutdown()
  - 服务器时间同步：心跳返回的 serverTime 作为基准时间，`LicenseInfo.ServerTime` 据此更新
  - 退出时停止定时器

  **Must NOT do**:
  - 不要改动现有的邮件、账号相关逻辑
  - 心跳失败不要用 toast，用弹框

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: None

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 2
  - **Blocks**: Task 10
  - **Blocked By**: Task 5 (LicenseService), Task 8 (title binding)

  **References**:
  - `ViewModels/MainWindowViewModel.cs` — 参考现有命令和属性模式

  **Acceptance Criteria**:

  **QA Scenarios**:

  ```
  Scenario: 编译通过
    Tool: Bash (dotnet build)
    Steps:
      1. dotnet build
    Expected Result: 编译成功
    Evidence: .sisyphus/evidence/task-9-build.txt
  ```

  **Evidence to Capture**:
  - [ ] 编译通过

  **Commit**: YES (groups with Tasks 7, 8)
  - Message: `feat: add heartbeat timer, unbind command, and title binding`
  - Files: `ViewModels/MainWindowViewModel.cs`

---

- [ ] 10. HttpServer — API 激活网关

  **What to do**:
  - 修改 `Api/HttpServer.cs`
  - 在 `HandleRequestAsync` 方法开头添加激活检查（在路由之前）
  - 排除健康检查类接口（不需要激活的路径）：
    - 如果有 `/health` 或类似路径可放行
    - 目前所有接口都需要保护，无白名单
  - 激活检查逻辑：
    - 从 LicenseStorageService 加载本地缓存
    - 如果缓存不存在 → 返回 403 `{"error":"unauthorized","message":"请先激活卡密"}`
    - 如果缓存存在但已过期 → 返回 403 `{"error":"license_expired","message":"卡密已过期"}`
    - 如果缓存有效 → 放行请求
  - 使用 `LicenseStorageService` 的单例或静态实例
  - 因为 `HttpServer` 已经有多个构造函数，要注意兼容

  **Must NOT do**:
  - 不要改动现有的路由处理逻辑
  - 不要在 API 网关中发起网络请求（只用本地缓存判断）
  - 不要影响 nowin 模式的 docs 页面

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
  - **Skills**: None

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Wave 2)
  - **Parallel Group**: Wave 3
  - **Blocks**: Nothing
  - **Blocked By**: Task 4 (LicenseStorageService), Task 7 (startup integration)

  **References**:
  - `Api/HttpServer.cs` — 参考现有的路由分发模式

  **Acceptance Criteria**:

  **QA Scenarios**:

  ```
  Scenario: API 网关拒绝未激活请求
    Tool: Bash (curl)
    Preconditions: 应用启动但未激活，或本地缓存不存在
    Steps:
      1. curl http://localhost:5000/api/email
    Expected Result: 返回 403 {"error":"unauthorized","message":"请先激活卡密"}
    Evidence: .sisyphus/evidence/task-10-api-403.txt

  Scenario: 已激活状态 API 放行
    Tool: Bash (curl)
    Preconditions: 应用已激活，本地缓存有效
    Steps:
      1. curl http://localhost:5000/api/email
    Expected Result: 正常返回 API 响应（非403）
    Evidence: .sisyphus/evidence/task-10-api-200.txt
  ```

  **Evidence to Capture**:
  - [ ] 未激活时 403 响应
  - [ ] 激活后正常响应

  **Commit**: YES
  - Message: `feat: add activation gate to HttpServer API endpoints`
  - Files: `Api/HttpServer.cs`

---

## Final Verification Wave

- [ ] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. For each "Must Have": verify implementation exists. For each "Must NOT Have": search codebase for forbidden patterns. Check evidence files exist in `.sisyphus/evidence/`.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT`

- [ ] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build`. Review all changed files for: empty catches, console.log in prod, commented-out code, unused imports.
  Output: `Build [PASS/FAIL] | Files [N clean/N issues] | VERDICT`

- [ ] F3. **Real Manual QA** — `unspecified-high`
  Start from clean state. Execute end-to-end flow: start app → see activation window → verify API 403 → test crypto. Save to `.sisyphus/evidence/final-qa/`.
  Output: `Scenarios [N/N pass] | Integration [N/N] | VERDICT`

- [ ] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", read actual diff. Verify 1:1 coverage. Detect cross-task contamination.
  Output: `Tasks [N/N compliant] | VERDICT`

---

## Commit Strategy

| Tasks | Message | Files |
|---|---|---|
| 1-4 | `feat: add core activation services (crypto, hardware, model, storage)` | `Services/ApiSecurityService.cs`, `Services/HardwareService.cs`, `Models/LicenseInfo.cs`, `Services/LicenseStorageService.cs` |
| 5 | `feat: add LicenseService for card key API integration` | `Services/LicenseService.cs` |
| 6 | `feat: add ActivationWindow and ActivationViewModel` | `Views/ActivationWindow.axaml`, `Views/ActivationWindow.axaml.cs`, `ViewModels/ActivationViewModel.cs` |
| 7-9 | `feat: integrate activation into app startup, title bar, and heartbeat` | `App.axaml.cs`, `Views/MainWindow.axaml`, `ViewModels/MainWindowViewModel.cs` |
| 10 | `feat: add activation gate to HttpServer API endpoints` | `Api/HttpServer.cs` |

---

## Success Criteria

### Verification Commands
```bash
dotnet build    # Expected: Build succeeded
dotnet run      # Expected: Activation window shown first
curl http://localhost:5000/api/email    # Expected: 403 before activation
```

### Final Checklist
- [ ] 首次启动 → 激活窗口 → 输入卡密 → 进入主窗口
- [ ] 标题栏显示"剩余 X 天 X 小时"
- [ ] 心跳每 1 分钟执行，失败 3 次退出
- [ ] 重启应用 → 跳过激活窗口 → 直接进主窗口
- [ ] 解绑按钮 → 确认 → 退出到激活窗口
- [ ] 未激活时 port 5000 API 全部返回 403
- [ ] 本地缓存文件存储在 `~/.outlookapp/license.dat`（隐藏文件）
