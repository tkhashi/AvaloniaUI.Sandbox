using CommunityToolkit.Mvvm.ComponentModel;

namespace WebViewSample.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly string _baseUrl = @"https://www.google.com/search?q=";

    [ObservableProperty]
    private string _url = "";
    
    [ObservableProperty]
    private string _searchKeyword = "";
    
    partial void OnSearchKeywordChanged(string value)
    {
        Url =  $"{_baseUrl}{value}";
    }
}
