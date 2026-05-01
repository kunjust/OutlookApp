using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using OutlookApp.Models;

namespace OutlookApp.Services;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService()
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OutlookApp.db");
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

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
    }

    public int SaveAccount(EmailAccount account)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO EmailAccounts (Email, PasswordEncrypted, ClientId, TokenEncrypted, AuthType, Status, StatusMessage)
            VALUES ($email, $pass, $clientId, $token, $auth, $status, $msg);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$email", account.Email);
        cmd.Parameters.AddWithValue("$pass", string.IsNullOrEmpty(account.Password) ? "" : EncryptionService.Encrypt(account.Password));
        cmd.Parameters.AddWithValue("$clientId", account.ClientId ?? "");
        cmd.Parameters.AddWithValue("$token", string.IsNullOrEmpty(account.Token) ? "" : EncryptionService.Encrypt(account.Token));
        cmd.Parameters.AddWithValue("$auth", account.AuthType ?? "");
        cmd.Parameters.AddWithValue("$status", account.Status);
        cmd.Parameters.AddWithValue("$msg", account.StatusMessage);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

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

    public List<EmailAccount> GetAccounts()
    {
        var accounts = new List<EmailAccount>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM EmailAccounts ORDER BY CreatedAt DESC";
        using var reader = cmd.ExecuteReader();
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
                StatusMessage = reader["StatusMessage"] as string ?? ""
            });
        }
        return accounts;
    }

    public void DeleteAccount(int accountId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = "PRAGMA foreign_keys = ON; DELETE FROM EmailMessages WHERE AccountId=$id; DELETE FROM EmailAccounts WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", accountId);
        cmd.ExecuteNonQuery();
    }

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

    public List<EmailMessage> GetMessages(int accountId)
    {
        var messages = new List<EmailMessage>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM EmailMessages WHERE AccountId=$id ORDER BY ReceivedTime DESC";
        cmd.Parameters.AddWithValue("$id", accountId);
        using var reader = cmd.ExecuteReader();
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
