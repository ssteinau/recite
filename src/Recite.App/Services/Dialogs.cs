using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Recite.App.Services;

/// <summary>Thin wrapper over Avalonia's StorageProvider for file open/save pickers.</summary>
public static class Dialogs
{
    /// <summary>The main window; set once at startup so VMs can raise pickers.</summary>
    public static Window? Owner { get; set; }

    public static async Task<string?> OpenFileAsync(string title, params (string name, string[] patterns)[] filters)
    {
        if (Owner is null) return null;
        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters.Select(f => new FilePickerFileType(f.name) { Patterns = f.patterns }).ToList(),
        };
        var result = await Owner.StorageProvider.OpenFilePickerAsync(options);
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> SaveFileAsync(string title, string suggestedName, string extension)
    {
        if (Owner is null) return null;
        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = new List<FilePickerFileType>
            {
                new(extension.ToUpperInvariant() + " file") { Patterns = new[] { "*." + extension } },
            },
        };
        var result = await Owner.StorageProvider.SaveFilePickerAsync(options);
        return result?.TryGetLocalPath();
    }
}
