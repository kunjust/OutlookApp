using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using OutlookApp.Models;

namespace OutlookApp.Api;

/// <summary>
/// 从邮件中提取验证码。
///
/// 提取策略（按优先级）：
///   1) NNN-NNN / NNN NNN / NNN.NNN 三位+分隔+三位（Apple、部分 EU 服务）
///   2) 单独 6 位数字（最常见：Microsoft、Google、Instagram、TikTok、银行 OTP）
///   3) 单独 4~8 位数字（兜底）
///   每一步都会自动过滤"明显不是验证码"的数字（年份、电话、纯重复数字等）。
///
/// 查询范围：
///   - 默认仅取最近 30 分钟内收到的邮件
///   - 可通过 keyword 参数对发件人/主题/正文做模糊匹配（不传则不过滤）
/// </summary>
public class VerificationExtractor
{
    private readonly string _connectionString;

    public VerificationExtractor(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// 取该邮箱最新可用验证码。
    /// </summary>
    /// <param name="email">邮箱地址</param>
    /// <param name="keyword">可选关键词（按 FromAddress/Subject/Body 模糊匹配），null/空 = 不过滤</param>
    /// <param name="withinMinutes">只看最近 N 分钟内的邮件，&lt;=0 表示不限</param>
    public (string Code, DateTime ReceivedTime) ExtractLatestCode(
        string email,
        string? keyword = null,
        int withinMinutes = 30)
    {
        var messages = GetRecentMessages(email, keyword, withinMinutes);
        foreach (var msg in messages)
        {
            // 主题里的验证码通常最干净，先看主题再看正文
            var code = ExtractVerificationCode(msg.Subject) ?? ExtractVerificationCode(msg.Body);
            if (!string.IsNullOrEmpty(code))
                return (code, msg.ReceivedTime);
        }
        return (string.Empty, DateTime.MinValue);
    }

    private List<EmailMessage> GetRecentMessages(string email, string? keyword, int withinMinutes)
    {
        var messages = new List<EmailMessage>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();

        var sql = @"
            SELECT m.Id, m.Subject, m.FromAddress, m.ToAddress, m.Body, m.BodyPreview,
                   m.ReceivedTime, m.HasAttachments, m.IsRead
            FROM EmailMessages m
            JOIN EmailAccounts a ON m.AccountId = a.Id
            WHERE a.Email = $email";

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            sql += @" AND (m.FromAddress LIKE $kw
                       OR m.Subject LIKE $kw
                       OR m.Body LIKE $kw)";
            cmd.Parameters.AddWithValue("$kw", $"%{keyword}%");
        }

        if (withinMinutes > 0)
        {
            // SQLite 的 datetime('now') 是 UTC，而我们存的是本地时间字符串。
            // 这里把"截止时间"用本地时间格式化后传进去做字符串比较即可。
            var cutoff = DateTime.Now.AddMinutes(-withinMinutes).ToString("yyyy-MM-dd HH:mm:ss");
            sql += " AND m.ReceivedTime >= $cutoff";
            cmd.Parameters.AddWithValue("$cutoff", cutoff);
        }

        sql += " ORDER BY m.ReceivedTime DESC LIMIT 20";
        cmd.CommandText = sql;
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
                HasAttachments = Convert.ToInt32(reader["HasAttachments"]) == 1,
                IsRead = Convert.ToInt32(reader["IsRead"]) == 1,
            });
        }
        return messages;
    }

    // 正则规则：
    // 1. (?<!\d) / (?!\d) 保证两边没有数字，避免把电话号 / 长串数字里的子串误识别
    // 2. NNN[separator]NNN 风格优先（很多 6 位码会被排版分隔）
    private static readonly Regex DashedSixDigits =
        new(@"(?<!\d)(\d{3})[\s\-\.]?(\d{3})(?!\d)", RegexOptions.Compiled);

    private static readonly Regex SixDigits =
        new(@"(?<!\d)(\d{6})(?!\d)", RegexOptions.Compiled);

    private static readonly Regex FourToEightDigits =
        new(@"(?<!\d)(\d{4,8})(?!\d)", RegexOptions.Compiled);

    internal static string? ExtractVerificationCode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        // 1) NNN-NNN / NNN NNN / NNN.NNN
        foreach (Match m in DashedSixDigits.Matches(text))
        {
            var code = m.Groups[1].Value + m.Groups[2].Value;
            if (IsPlausibleCode(code))
                return code;
        }

        // 2) 单独 6 位
        foreach (Match m in SixDigits.Matches(text))
        {
            var code = m.Groups[1].Value;
            if (IsPlausibleCode(code))
                return code;
        }

        // 3) 兜底：4~8 位
        foreach (Match m in FourToEightDigits.Matches(text))
        {
            var code = m.Groups[1].Value;
            if (IsPlausibleCode(code))
                return code;
        }

        return null;
    }

    /// <summary>
    /// 简单合理性检查：排除 000000 / 111111 这种全重复，以及看起来像年份的 4 位（19xx/20xx）。
    /// </summary>
    private static bool IsPlausibleCode(string code)
    {
        if (string.IsNullOrEmpty(code))
            return false;

        // 全部是同一个数字 → 多半是占位符
        if (code.Distinct().Count() == 1)
            return false;

        // 4 位且像年份（1900~2099）→ 多半是文案里的年份
        if (code.Length == 4 && int.TryParse(code, out var n) && n >= 1900 && n <= 2099)
            return false;

        return true;
    }
}
