using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using OutlookApp.Models;
using OutlookApp.Services;

namespace OutlookApp.Api;

public class VerificationExtractor
{
    private readonly string _connectionString;

    public VerificationExtractor(string connectionString)
    {
        _connectionString = connectionString;
    }

    public (string Code, DateTime ReceivedTime) ExtractLatestCode(string email)
    {
        var messages = GetInstagramMessages(email);
        if (messages.Count == 0)
            return (string.Empty, DateTime.MinValue);

        var latest = messages.First();
        var code = ExtractVerificationCode(latest.Body);
        if (string.IsNullOrEmpty(code))
            code = ExtractVerificationCode(latest.Subject);

        return (code ?? string.Empty, latest.ReceivedTime);
    }

    private List<EmailMessage> GetInstagramMessages(string email)
    {
        var messages = new List<EmailMessage>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT m.*, a.Email as AccountEmail
            FROM EmailMessages m
            JOIN EmailAccounts a ON m.AccountId = a.Id
            WHERE a.Email = $email
              AND (m.FromAddress LIKE '%instagram%' OR m.Subject LIKE '%instagram%' OR m.Body LIKE '%instagram%')
            ORDER BY m.ReceivedTime DESC
            """;
        cmd.Parameters.AddWithValue("$email", email);
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

    private static string? ExtractVerificationCode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var matches = Regex.Matches(text, @"\b(\d{6})\b");
        foreach (Match match in matches)
        {
            var code = match.Groups[1].Value;
            if (int.TryParse(code, out _) && !code.EndsWith("000000") && !code.StartsWith("000000"))
                return code;
        }

        if (matches.Count > 0)
            return matches[0].Groups[1].Value;

        return null;
    }
}
