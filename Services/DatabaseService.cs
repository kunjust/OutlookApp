using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using OutlookApp.Models;

namespace OutlookApp.Services;

/// <summary>
/// SQLite 数据库服务，管理邮箱账号和邮件数据的增删改查
/// </summary>
public class DatabaseService
{
    private readonly string _connectionString;

    /// <summary>
    /// 初始化数据库服务，自动创建或连接本地 SQLite 数据库
    /// </summary>
    public DatabaseService()
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OutlookApp.db");
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    /// <summary>
    /// 初始化数据库表结构，自动创建 EmailAccounts 和 EmailMessages 表
    /// </summary>
    private void Initialize()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS EmailAccounts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Email TEXT NOT NULL,
                PasswordEncrypted TEXT,
                ClientId TEXT,
                TokenEncrypted TEXT,
                AuthType TEXT,
                Status TEXT DEFAULT 'Pending',
                StatusMessage TEXT DEFAULT '',
                CreatedAt TEXT DEFAULT (datetime('now'))
            );
            CREATE TABLE IF NOT EXISTS EmailMessages (
                Id TEXT PRIMARY KEY,
                AccountId INTEGER NOT NULL,
                Subject TEXT,
                FromAddress TEXT,
                ToAddress TEXT,
                Body TEXT,
                BodyPreview TEXT,
                ReceivedTime TEXT,
                HasAttachments INTEGER DEFAULT 0,
                IsRead INTEGER DEFAULT 0,
                FOREIGN KEY (AccountId) REFERENCES EmailAccounts(Id) ON DELETE CASCADE
            );
            PRAGMA foreign_keys = ON;
            """;
        cmd.ExecuteNonQuery();

        // 兼容旧表结构，增量添加新列
        foreach (var col in new[] {
            ("Allocated", "INTEGER DEFAULT 0"),
            ("LastCode", "TEXT DEFAULT ''"),
            ("LastSyncTime", "TEXT"),
            ("IsUsed", "INTEGER DEFAULT 0"),
        })
        {
            try
            {
                using var c = conn.CreateCommand();
                c.CommandText = $"ALTER TABLE EmailAccounts ADD COLUMN {col.Item1} {col.Item2}";
                c.ExecuteNonQuery();
            }
            // 列已存在时忽略 ALTER 错误
            catch { }
        }
    }

    /// <summary>
    /// 保存邮箱账号到数据库
    /// </summary>
    /// <param name="account">邮箱账号信息（密码和 Token 会自动加密）</param>
    /// <returns>新插入记录的自增 ID</returns>
    public int SaveAccount(EmailAccount account)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO EmailAccounts (Email, PasswordEncrypted, ClientId, TokenEncrypted, AuthType, Status, StatusMessage, Allocated, IsUsed)
            VALUES ($email, $pass, $clientId, $token, $auth, $status, $msg, $allocated, $isUsed);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$email", account.Email);
        cmd.Parameters.AddWithValue("$pass", string.IsNullOrEmpty(account.Password) ? "" : EncryptionService.Encrypt(account.Password));
        cmd.Parameters.AddWithValue("$clientId", account.ClientId ?? "");
        cmd.Parameters.AddWithValue("$token", string.IsNullOrEmpty(account.Token) ? "" : EncryptionService.Encrypt(account.Token));
        cmd.Parameters.AddWithValue("$auth", account.AuthType ?? "");
        cmd.Parameters.AddWithValue("$status", account.Status);
        cmd.Parameters.AddWithValue("$msg", account.StatusMessage);
        cmd.Parameters.AddWithValue("$allocated", account.Allocated ? 1 : 0);
        cmd.Parameters.AddWithValue("$isUsed", account.IsUsed ? 1 : 0);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// 更新邮箱账号的状态信息
    /// </summary>
    /// <param name="accountId">账号 ID</param>
    /// <param name="status">新状态</param>
    /// <param name="message">状态描述信息</param>
    /// <param name="authType">认证协议类型</param>
    public void UpdateAccountStatus(int accountId, string status, string message, string authType)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE EmailAccounts SET Status=$s, StatusMessage=$m, AuthType=$a WHERE Id=$id";
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$m", message);
        cmd.Parameters.AddWithValue("$a", authType);
        cmd.Parameters.AddWithValue("$id", accountId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 获取所有未使用的邮箱账号列表
    /// </summary>
    /// <returns>邮箱账号列表（密码和 Token 已解密）</returns>
    public List<EmailAccount> GetAccounts()
    {
        var accounts = new List<EmailAccount>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM EmailAccounts WHERE (IsUsed=0 OR IsUsed IS NULL) AND (Allocated=0 OR Allocated IS NULL) AND Status='Verified' ORDER BY Email ASC";
        using var reader = cmd.ExecuteReader();
        // 遍历查询结果，逐行构建账号对象
        while (reader.Read())
        {
            var encryptedPass = reader["PasswordEncrypted"] as string;
            var encryptedToken = reader["TokenEncrypted"] as string;
            accounts.Add(new EmailAccount
            {
                Id = Convert.ToInt32(reader["Id"]),
                Email = reader["Email"] as string ?? "",
                Password = string.IsNullOrEmpty(encryptedPass) ? "" : EncryptionService.Decrypt(encryptedPass),
                ClientId = reader["ClientId"] as string ?? "",
                Token = string.IsNullOrEmpty(encryptedToken) ? "" : EncryptionService.Decrypt(encryptedToken),
                AuthType = reader["AuthType"] as string ?? "",
                Status = reader["Status"] as string ?? "Pending",
                StatusMessage = reader["StatusMessage"] as string ?? "",
                Allocated = (reader["Allocated"] as int? ?? 0) == 1,
                LastCode = reader["LastCode"] as string ?? "",
                LastSyncTime = ParseDateTime(reader["LastSyncTime"] as string),
                IsUsed = (reader["IsUsed"] as int? ?? 0) == 1,
            });
        }
        return accounts;
    }

    /// <summary>
    /// 获取数据库连接字符串（供外部使用）
    /// </summary>
    public string ConnectionString => _connectionString;

    /// <summary>
    /// 尝试分配一个可用邮箱账号（原子操作，事务保护）
    /// </summary>
    /// <param name="email">输出的邮箱地址</param>
    /// <returns>是否成功分配到账号</returns>
    public bool TryAllocateAccount(out string email)
    {
        email = string.Empty;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = "SELECT Id, Email FROM EmailAccounts WHERE Allocated=0 AND Status='Verified' ORDER BY Email ASC LIMIT 1";
        using var reader = cmd1.ExecuteReader();
        if (!reader.Read())
        {
            tx.Rollback();
            return false;
        }
        var id = Convert.ToInt32(reader["Id"]);
        email = reader["Email"] as string ?? string.Empty;
        reader.Close();

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "UPDATE EmailAccounts SET Allocated=1 WHERE Id=$id";
        cmd2.Parameters.AddWithValue("$id", id);
        cmd2.ExecuteNonQuery();

        tx.Commit();
        return true;
    }

    /// <summary>
    /// 根据邮箱地址查询账号
    /// </summary>
    /// <param name="email">邮箱地址</param>
    /// <returns>匹配的账号对象，未找到则返回 null</returns>
    public EmailAccount? GetAccountByEmail(string email)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM EmailAccounts WHERE Email=$email";
        cmd.Parameters.AddWithValue("$email", email);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        var encryptedPass = reader["PasswordEncrypted"] as string;
        var encryptedToken = reader["TokenEncrypted"] as string;
        return new EmailAccount
        {
            Id = Convert.ToInt32(reader["Id"]),
            Email = reader["Email"] as string ?? "",
            Password = string.IsNullOrEmpty(encryptedPass) ? "" : EncryptionService.Decrypt(encryptedPass),
            ClientId = reader["ClientId"] as string ?? "",
            Token = string.IsNullOrEmpty(encryptedToken) ? "" : EncryptionService.Decrypt(encryptedToken),
            AuthType = reader["AuthType"] as string ?? "",
            Status = reader["Status"] as string ?? "Pending",
            StatusMessage = reader["StatusMessage"] as string ?? "",
            Allocated = (reader["Allocated"] as int? ?? 0) == 1,
            LastCode = reader["LastCode"] as string ?? "",
            LastSyncTime = ParseDateTime(reader["LastSyncTime"] as string),
        };
    }

    /// <summary>
    /// 更新账号的最近验证码和同步时间
    /// </summary>
    /// <param name="email">邮箱地址</param>
    /// <param name="code">最新验证码</param>
    /// <param name="syncTime">同步时间</param>
    public void UpdateAccountCodeAndSyncTime(string email, string code, DateTime syncTime)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE EmailAccounts SET LastCode=$code, LastSyncTime=$sync WHERE Email=$email";
        cmd.Parameters.AddWithValue("$code", code);
        cmd.Parameters.AddWithValue("$sync", syncTime.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("$email", email);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 检查是否所有已验证账号都已分配
    /// </summary>
    /// <returns>是否无可用账号剩余</returns>
    public bool AreAllAccountsAllocated()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM EmailAccounts WHERE Allocated=0 AND Status='Verified'";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        return count == 0;
    }

    /// <summary>
    /// 解析日期时间字符串
    /// </summary>
    /// <param name="value">日期时间字符串</param>
    /// <returns>解析后的 DateTime，解析失败返回 null</returns>
    private static DateTime? ParseDateTime(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        return DateTime.TryParse(value, out var dt) ? dt : null;
    }

    /// <summary>
    /// 获取未使用邮箱账号的总数
    /// </summary>
    /// <returns>账号总数</returns>
    public int GetAccountsCount()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM EmailAccounts WHERE IsUsed=0 AND Allocated=0 AND Status='Verified'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// 分页获取未使用的邮箱账号列表
    /// </summary>
    /// <param name="page">页码（从 1 开始）</param>
    /// <param name="pageSize">每页数量</param>
    /// <returns>邮箱账号列表</returns>
    public List<EmailAccount> GetAccountsPaged(int page, int pageSize)
    {
        var accounts = new List<EmailAccount>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var offset = (page - 1) * pageSize;
        cmd.CommandText = @"SELECT * FROM EmailAccounts WHERE IsUsed=0 AND Allocated=0 AND Status='Verified' ORDER BY Email ASC LIMIT $limit OFFSET $offset";
        cmd.Parameters.AddWithValue("$limit", pageSize);
        cmd.Parameters.AddWithValue("$offset", offset);
        using var reader = cmd.ExecuteReader();
        // 遍历分页结果
        while (reader.Read())
        {
            var encryptedPass = reader["PasswordEncrypted"] as string;
            var encryptedToken = reader["TokenEncrypted"] as string;
            accounts.Add(new EmailAccount
            {
                Id = Convert.ToInt32(reader["Id"]),
                Email = reader["Email"] as string ?? "",
                Password = string.IsNullOrEmpty(encryptedPass) ? "" : EncryptionService.Decrypt(encryptedPass),
                ClientId = reader["ClientId"] as string ?? "",
                Token = string.IsNullOrEmpty(encryptedToken) ? "" : EncryptionService.Decrypt(encryptedToken),
                AuthType = reader["AuthType"] as string ?? "",
                Status = reader["Status"] as string ?? "Pending",
                StatusMessage = reader["StatusMessage"] as string ?? "",
                Allocated = (reader["Allocated"] as int? ?? 0) == 1,
                LastCode = reader["LastCode"] as string ?? "",
                LastSyncTime = ParseDateTime(reader["LastSyncTime"] as string),
                IsUsed = (reader["IsUsed"] as int? ?? 0) == 1,
            });
        }
        return accounts;
    }

    /// <summary>
    /// 将账号标记为已使用
    /// </summary>
    /// <param name="accountId">账号 ID</param>
    public void MarkAccountAsUsed(int accountId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE EmailAccounts SET IsUsed=1 WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", accountId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 删除指定账号及其所有关联邮件
    /// </summary>
    /// <param name="accountId">账号 ID</param>
    public void DeleteAccount(int accountId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = "PRAGMA foreign_keys = ON; DELETE FROM EmailMessages WHERE AccountId=$id; DELETE FROM EmailAccounts WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", accountId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 批量保存邮件列表到数据库
    /// </summary>
    /// <param name="accountId">所属账号 ID</param>
    /// <param name="messages">邮件列表</param>
    public void SaveMessages(int accountId, List<EmailMessage> messages)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        foreach (var msg in messages)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO EmailMessages
                (Id, AccountId, Subject, FromAddress, ToAddress, Body, BodyPreview, ReceivedTime, HasAttachments, IsRead)
                VALUES ($id, $accId, $subj, $from, $to, $body, $preview, $time, $attach, $read)
                """;
            cmd.Parameters.AddWithValue("$id", msg.Id);
            cmd.Parameters.AddWithValue("$accId", accountId);
            cmd.Parameters.AddWithValue("$subj", msg.Subject ?? "");
            cmd.Parameters.AddWithValue("$from", msg.From ?? "");
            cmd.Parameters.AddWithValue("$to", msg.To ?? "");
            cmd.Parameters.AddWithValue("$body", msg.Body ?? "");
            cmd.Parameters.AddWithValue("$preview", msg.BodyPreview ?? "");
            cmd.Parameters.AddWithValue("$time", msg.ReceivedTime.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("$attach", msg.HasAttachments ? 1 : 0);
            cmd.Parameters.AddWithValue("$read", msg.IsRead ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// 获取指定账号的所有邮件
    /// </summary>
    /// <param name="accountId">账号 ID</param>
    /// <returns>邮件列表（按接收时间倒序）</returns>
    public List<EmailMessage> GetMessages(int accountId)
    {
        var messages = new List<EmailMessage>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM EmailMessages WHERE AccountId=$id ORDER BY ReceivedTime DESC";
        cmd.Parameters.AddWithValue("$id", accountId);
        using var reader = cmd.ExecuteReader();
        // 遍历查询结果，构建邮件对象
        while (reader.Read())
        {
            messages.Add(new EmailMessage
            {
                Id = reader["Id"] as string ?? "",
                Subject = reader["Subject"] as string ?? "",
                From = reader["FromAddress"] as string ?? "",
                To = reader["ToAddress"] as string ?? "",
                Body = reader["Body"] as string ?? "",
                BodyPreview = reader["BodyPreview"] as string ?? "",
                ReceivedTime = DateTime.TryParse(reader["ReceivedTime"] as string, out var dt) ? dt : DateTime.MinValue,
                HasAttachments = (reader["HasAttachments"] as int? ?? 0) == 1,
                IsRead = (reader["IsRead"] as int? ?? 0) == 1
            });
        }
        return messages;
    }

    /// <summary>
    /// 删除指定账号的所有邮件
    /// </summary>
    /// <param name="accountId">账号 ID</param>
    public void DeleteMessages(int accountId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM EmailMessages WHERE AccountId=$id";
        cmd.Parameters.AddWithValue("$id", accountId);
        cmd.ExecuteNonQuery();
    }
}
