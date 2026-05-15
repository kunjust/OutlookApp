using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using OutlookApp.Models;

namespace OutlookApp.Services;

/// <summary>
/// 关键词/对标数据 CRUD 服务
/// </summary>
public class KeywordService
{
    private readonly string _connectionString;

    public KeywordService(string connectionString)
    {
        _connectionString = connectionString;
        Initialize();
    }

    private void Initialize()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Keywords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Content TEXT NOT NULL,
                Status TEXT DEFAULT 'Available',
                UsedAt TEXT,
                CreatedAt TEXT DEFAULT (datetime('now'))
            );
            CREATE INDEX IF NOT EXISTS idx_keywords_status ON Keywords(Status);
            """;
        cmd.ExecuteNonQuery();
    }

    public void BatchInsert(List<string> contents)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        foreach (var c in contents)
        {
            if (string.IsNullOrWhiteSpace(c))
                continue;
            var trimmed = c.Trim();
            if (trimmed.Length > 100)
                trimmed = trimmed[..100];

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Keywords (Content) VALUES ($content)";
            cmd.Parameters.AddWithValue("$content", trimmed);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public List<KeywordItem> GetAll()
    {
        var items = new List<KeywordItem>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Keywords ORDER BY CreatedAt ASC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new KeywordItem
            {
                Id = Convert.ToInt32(reader["Id"]),
                Content = reader["Content"] as string ?? "",
                Status = reader["Status"] as string ?? "Available",
                UsedAt = ParseDateTime(reader["UsedAt"] as string),
                CreatedAt = (reader["CreatedAt"] as string) ?? "",
            });
        }
        return items;
    }

    public void RestoreToAvailable(int keywordId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Keywords SET Status='Available', UsedAt=NULL WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", keywordId);
        cmd.ExecuteNonQuery();
    }

    public void BatchRestoreToAvailable(List<int> ids)
    {
        if (ids.Count == 0)
            return;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        foreach (var id in ids)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Keywords SET Status='Available', UsedAt=NULL WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void Delete(int keywordId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Keywords WHERE Id=$id";
        cmd.Parameters.AddWithValue("$id", keywordId);
        cmd.ExecuteNonQuery();
    }

    public void BatchDelete(List<int> ids)
    {
        if (ids.Count == 0)
            return;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        foreach (var id in ids)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Keywords WHERE Id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public string? AllocateOne()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = "SELECT Id, Content FROM Keywords WHERE Status='Available' LIMIT 1";
        using var reader = cmd1.ExecuteReader();
        if (!reader.Read())
        {
            tx.Rollback();
            return null;
        }
        var id = Convert.ToInt32(reader["Id"]);
        var content = reader["Content"] as string;
        reader.Close();

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "UPDATE Keywords SET Status='Used', UsedAt=datetime('now') WHERE Id=$id AND Status='Available'";
        cmd2.Parameters.AddWithValue("$id", id);
        int rows = cmd2.ExecuteNonQuery();
        if (rows == 0)
        {
            tx.Rollback();
            return null;
        }

        tx.Commit();
        return content;
    }

    public (int Available, int Used, int Total) GetCounts()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        int available = 0, used = 0, total = 0;

        using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = "SELECT COUNT(*) FROM Keywords WHERE Status='Available'";
        available = Convert.ToInt32(cmd1.ExecuteScalar());

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT COUNT(*) FROM Keywords WHERE Status='Used'";
        used = Convert.ToInt32(cmd2.ExecuteScalar());

        using var cmd3 = conn.CreateCommand();
        cmd3.CommandText = "SELECT COUNT(*) FROM Keywords";
        total = Convert.ToInt32(cmd3.ExecuteScalar());

        return (available, used, total);
    }

    private static DateTime? ParseDateTime(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        return DateTime.TryParse(value, out var dt) ? dt : null;
    }
}
