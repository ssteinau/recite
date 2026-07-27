using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Recite.App.ViewModels;

/// <summary>Small value converters used across views.</summary>
public static class UiConverters
{
    /// <summary>true → SemiBold, false → Normal (used to emphasise facet roots in the tree).</summary>
    public static readonly IValueConverter BoolToBold =
        new FuncValueConverter<bool, FontWeight>(b => b ? FontWeight.SemiBold : FontWeight.Normal);

    /// <summary>int → true when the value is zero (for "empty list" placeholders).</summary>
    public static readonly IValueConverter IsZero =
        new FuncValueConverter<int, bool>(n => n == 0);
}
