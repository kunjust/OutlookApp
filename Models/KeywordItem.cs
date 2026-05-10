using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OutlookApp.Models;

public partial class KeywordItem : ObservableObject
{
    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private string _status = "Available";

    [ObservableProperty]
    private DateTime? _usedAt;

    [ObservableProperty]
    private string _createdAt = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    public string DisplayStatus => Status == "Available" ? "可用" : "已用";
    public string DisplayUsedAt => UsedAt?.ToString("MM-dd HH:mm") ?? "—";
}
