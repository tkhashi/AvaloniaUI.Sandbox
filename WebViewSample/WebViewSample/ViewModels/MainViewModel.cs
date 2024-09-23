using CommunityToolkit.Mvvm.ComponentModel;

namespace WebViewSample.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _greeting = "Welcome to Avalonia!";
    
    [ObservableProperty]
    private string _url = "";
}
