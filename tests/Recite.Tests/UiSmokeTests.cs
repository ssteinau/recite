using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Recite.App;
using Recite.App.Services;
using Recite.App.ViewModels;
using Recite.App.Views;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Recite.Tests.TestAppBuilder))]

namespace Recite.Tests;

public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<global::Recite.App.App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

public class UiSmokeTests
{
    /// <summary>
    /// Boots the real Application, seeds a temp library, shows the main window and visits every
    /// section — which forces the ViewLocator to load each view's XAML. Catches runtime binding
    /// errors the XAML compiler can't (cross-DataContext commands, missing converters, etc.).
    /// </summary>
    [AvaloniaFact]
    public void App_launches_and_renders_every_section()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "recite-ui-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var services = new AppServices(root);
            var vm = new MainWindowViewModel(services);
            vm.Initialize();

            var window = new MainWindow { DataContext = vm };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.ItemCount > 0, "sample data should have seeded");
            Assert.Equal(6, vm.Sections.Count);

            foreach (var section in vm.Sections)
            {
                vm.SelectedSection = section;
                Dispatcher.UIThread.RunJobs();
            }

            // Exercise a live query through the UI stack: search narrows the library list.
            vm.SelectedSection = vm.Sections[0];
            Dispatcher.UIThread.RunJobs();
            var library = (LibraryViewModel)vm.Sections[0].Content;
            library.SearchText = "relational";
            Dispatcher.UIThread.RunJobs();
            Assert.True(library.Items.Count >= 1);

            window.Close();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); } catch { }
        }
    }
}
