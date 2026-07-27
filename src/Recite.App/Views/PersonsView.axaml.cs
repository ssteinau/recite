using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Recite.App.Views;

public partial class PersonsView : UserControl
{
    public PersonsView() => AvaloniaXamlLoader.Load(this);
}
