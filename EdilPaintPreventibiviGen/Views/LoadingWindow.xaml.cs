using System.Windows;

namespace EdilPaintPreventibiviGen;

public partial class LoadingWindow : Window
{
    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(LoadingWindow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsLoadingProperty =
        DependencyProperty.Register(nameof(IsLoading), typeof(bool), typeof(LoadingWindow), new PropertyMetadata(true));

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public LoadingWindow()
    {
        InitializeComponent();
        DataContext = this;
    }
}