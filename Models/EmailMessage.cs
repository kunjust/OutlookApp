using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OutlookApp.Models;

public partial class EmailMessage : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _subject = string.Empty;

    [ObservableProperty]
    private string _from = string.Empty;

    [ObservableProperty]
    private string _to = string.Empty;

    [ObservableProperty]
    private string _body = string.Empty;

    [ObservableProperty]
    private string _bodyPreview = string.Empty;

    [ObservableProperty]
    private DateTime _receivedTime;

    [ObservableProperty]
    private bool _hasAttachments;

    [ObservableProperty]
    private bool _isRead;

    public string DisplayDate => ReceivedTime.ToString("yyyy-MM-dd HH:mm");
    public bool IsUnread => !IsRead;

    public string DisplayPreview => string.IsNullOrEmpty(BodyPreview)
        ? "(No preview)"
        : (BodyPreview.Length > 80 ? BodyPreview[..80] + "..." : BodyPreview);
}
