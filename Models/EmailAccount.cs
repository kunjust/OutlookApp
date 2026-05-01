using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OutlookApp.Models;

public partial class EmailAccount : ObservableObject
{
    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private string _token = string.Empty;

    [ObservableProperty]
    private string _authType = string.Empty;

    [ObservableProperty]
    private string _status = "Pending";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _createdAt = string.Empty;

    public string MaskedEmail => Email.Length > 3
        ? Email[..3] + "****" + Email[Email.IndexOf('@')..]
        : Email;

    public string DisplayStatus => Status switch
    {
        "Verified" => "✓",
        "Failed" => "✗",
        _ => "⋯"
    };
}
