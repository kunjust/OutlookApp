using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OutlookApp.Models;
using OutlookApp.Services;

namespace OutlookApp.ViewModels;

public partial class KeywordViewModel : ViewModelBase
{
    private readonly KeywordService _kwService;

    [ObservableProperty]
    private ObservableCollection<KeywordItem> _keywords = new();

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isAllSelected;

    [ObservableProperty]
    private int _availableCount;

    [ObservableProperty]
    private int _usedCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private string _statusText = "就绪";

    [ObservableProperty]
    private string _searchText = string.Empty;

    public KeywordViewModel(KeywordService kwService)
    {
        _kwService = kwService;
        LoadKeywords();
        UpdateCounts();
    }

    private void LoadKeywords()
    {
        Keywords.Clear();
        var all = _kwService.GetAll();
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            all = all.Where(k => k.Content.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        foreach (var item in all)
        {
            Keywords.Add(item);
        }
    }

    partial void OnSearchTextChanged(string value) => LoadKeywords();

    private void UpdateCounts()
    {
        var counts = _kwService.GetCounts();
        AvailableCount = counts.Available;
        UsedCount = counts.Used;
        TotalCount = counts.Total;
    }

    partial void OnIsAllSelectedChanged(bool value)
    {
        foreach (var kw in Keywords)
            kw.IsSelected = value;
    }

    public bool HasSelected => Keywords.Any(k => k.IsSelected);

    [RelayCommand]
    private void SaveKeywords()
    {
        if (string.IsNullOrWhiteSpace(InputText))
            return;

        var lines = InputText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var validLines = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && trimmed.Length <= 100)
            {
                validLines.Add(trimmed);
            }
        }

        if (validLines.Count == 0)
        {
            StatusText = "没有有效的对标内容";
            return;
        }

        _kwService.BatchInsert(validLines);
        InputText = string.Empty;
        LoadKeywords();
        UpdateCounts();
        StatusText = $"已添加 {validLines.Count} 条对标";
    }

    [RelayCommand]
    private void RestoreKeyword(KeywordItem item)
    {
        if (item == null || item.Status == "Available")
            return;
        _kwService.RestoreToAvailable(item.Id);
        item.Status = "Available";
        item.UsedAt = null;
        UpdateCounts();
        StatusText = $"已恢复: {item.Content}";
    }

    [RelayCommand]
    private void DeleteKeyword(KeywordItem item)
    {
        if (item == null)
            return;
        _kwService.Delete(item.Id);
        Keywords.Remove(item);
        UpdateCounts();
        StatusText = $"已删除: {item.Content}";
    }

    [RelayCommand]
    private void RestoreSelected()
    {
        var selected = Keywords.Where(k => k.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "请先选择要对标";
            return;
        }
        var ids = selected.Select(k => k.Id).ToList();
        _kwService.BatchRestoreToAvailable(ids);
        foreach (var kw in selected)
        {
            kw.Status = "Available";
            kw.UsedAt = null;
            kw.IsSelected = false;
        }
        IsAllSelected = false;
        UpdateCounts();
        StatusText = $"已恢复 {selected.Count} 条对标";
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        var selected = Keywords.Where(k => k.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText = "请先选择要对标";
            return;
        }
        var ids = selected.Select(k => k.Id).ToList();
        _kwService.BatchDelete(ids);
        foreach (var kw in selected)
            Keywords.Remove(kw);
        IsAllSelected = false;
        UpdateCounts();
        StatusText = $"已删除 {selected.Count} 条对标";
    }

    [RelayCommand]
    private async Task RefreshAll()
    {
        await Task.Run(() =>
        {
            LoadKeywords();
            UpdateCounts();
        });
        StatusText = "已刷新";
    }
}
