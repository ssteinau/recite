using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Recite.App.Services;

namespace Recite.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Dialogs.Owner = this;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
