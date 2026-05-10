using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OutlookApp.Models;

/// <summary>
/// 卡密激活信息模型。记录当前设备的卡密状态和到期时间。
/// 所有时间以服务器时间为准。
/// </summary>
public partial class LicenseInfo : ObservableObject
{
    /// <summary>卡密字符串</summary>
    [ObservableProperty]
    private string _cardKey = string.Empty;

    /// <summary>设备 ID（MAC 地址）</summary>
    [ObservableProperty]
    private string _deviceId = string.Empty;

    /// <summary>硬件 ID（MAC 地址）</summary>
    [ObservableProperty]
    private string _hardwareId = string.Empty;

    /// <summary>过期时间（服务器时间）</summary>
    [ObservableProperty]
    private DateTime _expiryTime;

    /// <summary>服务器当前时间（最后一次同步）</summary>
    [ObservableProperty]
    private DateTime _serverTime;

    /// <summary>激活时间（服务器时间）</summary>
    [ObservableProperty]
    private DateTime _activatedAt;

    /// <summary>最后验证时间（本地）</summary>
    [ObservableProperty]
    private DateTime _lastVerifiedAt;

    /// <summary>
    /// 是否处于激活有效期（根据服务器时间判断）
    /// </summary>
    [JsonIgnore]
    public bool IsActive => ServerTime < ExpiryTime;

    /// <summary>
    /// 剩余时间的可读文本，如 "15天 3小时"
    /// </summary>
    [JsonIgnore]
    public string TimeRemainingText
    {
        get
        {
            if (!IsActive)
                return "已过期";

            var remaining = ExpiryTime - ServerTime;
            if (remaining.TotalDays >= 1)
                return $"{(int)remaining.TotalDays}天 {remaining.Hours}小时";
            if (remaining.TotalHours >= 1)
                return $"{(int)remaining.TotalHours}小时 {remaining.Minutes}分";
            return $"{(int)Math.Max(remaining.TotalMinutes, 0)}分钟";
        }
    }

    /// <summary>
    /// 序列化为 JSON
    /// 注意：使用反射序列化而非源生成器（JsonContext），
    /// 因为 [ObservableProperty] 生成的属性在源生成器编译顺序中不可见。
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this);
    }

    /// <summary>
    /// 从 JSON 反序列化
    /// </summary>
    public static LicenseInfo? FromJson(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<LicenseInfo>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 更新服务器时间，并触发相关属性的通知
    /// </summary>
    public void UpdateServerTime(DateTime serverTime)
    {
        ServerTime = serverTime;
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(TimeRemainingText));
    }
}


