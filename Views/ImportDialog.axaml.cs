using Avalonia.Controls;
using OutlookApp.Models;
using OutlookApp.ViewModels;

namespace OutlookApp.Views;

public partial class ImportDialog : Window
{
    public ImportDialog()
    {
        InitializeComponent();
        DataContext = new ImportDialogViewModel();
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnImportClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ImportDialogViewModel vm && vm.DetectedAccount != null)
        {
            Close(vm.DetectedAccount);
        }
    }
}
