using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Recite.App.ViewModels;

namespace Recite.App;

/// <summary>Maps a *ViewModel to its *View by naming convention (Recite.App.ViewModels.XVm → Recite.App.Views.X).</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null) return new TextBlock { Text = "null" };
        var vmName = data.GetType().FullName!;
        var viewName = vmName
            .Replace("ViewModels", "Views")
            .Replace("ViewModel", "View");
        var type = Type.GetType(viewName);
        if (type is not null && Activator.CreateInstance(type) is Control control)
        {
            control.DataContext = data;
            return control;
        }
        return new TextBlock { Text = "View not found: " + viewName };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
