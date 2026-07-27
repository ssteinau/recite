using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Recite.App.Services;
using Recite.App.ViewModels;
using Recite.App.Views;

namespace Recite.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var root = Environment.GetEnvironmentVariable("RECITE_LIBRARY");
            var services = new AppServices(string.IsNullOrWhiteSpace(root) ? null : root);
            var mainVm = new MainWindowViewModel(services);
            desktop.MainWindow = new MainWindow { DataContext = mainVm };
            mainVm.Initialize();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
