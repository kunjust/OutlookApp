using CommunityToolkit.Mvvm.ComponentModel;

/// <summary>
/// ViewModel 基类，所有 ViewModel 继承此类。
/// 继承 ObservableObject 提供属性变更通知。
/// </summary>
namespace OutlookApp.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
}
