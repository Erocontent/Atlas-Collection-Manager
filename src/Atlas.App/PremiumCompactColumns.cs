using Atlas.Core;
using W = System.Windows;
using C = System.Windows.Controls;

namespace Atlas.App;

public partial class PremiumWindow
{
    private C.Grid? compactHeader;

    private C.Grid BuildCompactHeader()
    {
        var header = new C.Grid { Margin = new W.Thickness(10, 0, 17, 0), MinHeight = 38, ClipToBounds = true };
        var backdrop = new C.Border { Background = SkinBrush("Elevated"), BorderBrush = SkinBrush("Stroke"),
            BorderThickness = new W.Thickness(0, 0, 0, 1), CornerRadius = new W.CornerRadius(7, 7, 0, 0),
            Margin = new W.Thickness(-10, 0, -17, 0), IsHitTestVisible = false };
        C.Grid.SetColumnSpan(backdrop, 7);
        header.ClipToBounds = false;
        header.Children.Add(backdrop);
        var widths = preferences.CompactColumnWidths;
        var defaults = new[] { 5.0, 2.0, 1.1, 1.1 };
        if (widths is not { Length: 4 } || widths.Any(width => !double.IsFinite(width) || width <= 0)) widths = defaults;
        string[] labels = [L.T("ModColumnName"), L.T("ModColumnArchive"), L.T("ModColumnConflicts"), L.T("ModVersion")];
        for (var index = 0; index < labels.Length; index++)
        {
            if (index > 0)
            {
                var splitterColumn = header.ColumnDefinitions.Count;
                header.ColumnDefinitions.Add(new() { Width = new W.GridLength(7) });
                var splitter = new C.GridSplitter
                {
                    Width = 7, HorizontalAlignment = W.HorizontalAlignment.Stretch,
                    VerticalAlignment = W.VerticalAlignment.Stretch, Background = System.Windows.Media.Brushes.Transparent,
                    Template = (C.ControlTemplate)FindResource("CompactSplitterTemplate"),
                    Cursor = W.Input.Cursors.SizeWE,
                    ResizeDirection = C.GridResizeDirection.Columns,
                    ResizeBehavior = C.GridResizeBehavior.PreviousAndNext
                };
                splitter.DragCompleted += (_, _) =>
                {
                    preferences.CompactColumnWidths = header.ColumnDefinitions.Where((_, column) => column % 2 == 0)
                        .Select(definition => definition.Width.Value).ToArray();
                    store.Save(preferences);
                };
                header.Children.Add(splitter.AtColumn(splitterColumn));
            }
            var column = header.ColumnDefinitions.Count;
            header.ColumnDefinitions.Add(new() { Width = new W.GridLength(widths[index], W.GridUnitType.Star), MinWidth = index == 0 ? 100 : 62 });
            var title = SecondaryText(labels[index], 12, false, true);
            title.Margin = new W.Thickness(7, 0, 7, 0);
            title.VerticalAlignment = W.VerticalAlignment.Center;
            if (index is 1 or 2 or 3) title.TextAlignment = W.TextAlignment.Center;
            header.Children.Add(title.AtColumn(column));
        }
        return header;
    }

    private C.Grid CompactModRow(ModPackage mod, ModDetails details, W.FrameworkElement? nameCell = null)
    {
        var row = new C.Grid { MinHeight = 36 };
        if (compactHeader != null)
            foreach (var headerColumn in compactHeader.ColumnDefinitions)
            {
                var definition = new C.ColumnDefinition();
                definition.SetBinding(C.ColumnDefinition.WidthProperty, new W.Data.Binding(nameof(C.ColumnDefinition.Width)) { Source = headerColumn });
                definition.MinWidth = headerColumn.MinWidth;
                row.ColumnDefinitions.Add(definition);
            }

        C.TextBlock Cell(string value, bool secondary)
        {
            var text = SecondaryText(value, 13, secondary);
            text.Margin = new W.Thickness(7, 0, 7, 0);
            text.VerticalAlignment = W.VerticalAlignment.Center;
            text.TextWrapping = W.TextWrapping.NoWrap;
            text.TextTrimming = W.TextTrimming.CharacterEllipsis;
            text.ToolTip = value;
            return text;
        }
        row.Children.Add(nameCell ?? Cell(mod.Name, false));
        var archive = ModContentIndicators(mod, interactive: false, compact: true);
        archive.HorizontalAlignment = W.HorizontalAlignment.Center;
        archive.VerticalAlignment = W.VerticalAlignment.Center;
        row.Children.Add(archive.AtColumn(2));
        if (HasLibraryConflict(mod))
        {
            var conflict = ConflictButton(mod, compact: true);
            conflict.HorizontalAlignment = W.HorizontalAlignment.Center;
            conflict.VerticalAlignment = W.VerticalAlignment.Center;
            row.Children.Add(conflict.AtColumn(4));
        }
        var version = Cell(details.Version, true);
        version.TextAlignment = W.TextAlignment.Center;
        row.Children.Add(version.AtColumn(6));
        return row;
    }
}
