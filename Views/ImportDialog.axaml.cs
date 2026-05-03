using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using OutlookApp.Models;
using OutlookApp.ViewModels;

namespace OutlookApp.Views;

public partial class ImportDialog : Window
{
    public ImportDialog()
    {
        InitializeComponent();
        var vm = new ImportDialogViewModel();
        vm.FilePicked += OnFilePicked;
        DataContext = vm;
    }

    private async Task<string?> OnFilePicked()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择账号文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("文本文件") { Patterns = new[] { "*.txt" } },
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*" } }
                }
            });
            if (files == null || files.Count == 0) return null;
            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return null;
            return await File.ReadAllTextAsync(path);
        }
        catch
        {
            return null;
        }
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnImportClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ImportDialogViewModel vm)
        {
            var accounts = vm.GetVerifiedAccounts();
            Close(accounts);
        }
    }
}
