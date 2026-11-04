using Atlas.Core;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;

namespace Atlas.App;

public partial class PremiumWindow
{
    private Dictionary<string, bool> libraryConflicts = [];

    private bool HasLibraryConflict(ModPackage mod) => libraryConflicts.GetValueOrDefault(mod.Id);

    private void IndexLibraryConflicts(IReadOnlyList<ModPackage> packages)
    {
        var targets = packages.SelectMany(mod => mod.Files.Select(file =>
            (Path: string.IsNullOrEmpty(mod.Destination) ? file : mod.Destination + "/" + file, mod.Id)))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
        var conflicting = new HashSet<string>();
        foreach (var (path, owners) in targets)
        {
            if (owners.Length > 1) conflicting.UnionWith(owners);
            for (var slash = path.LastIndexOf('/'); slash >= 0; slash = path.LastIndexOf('/', slash - 1))
            {
                if (!targets.TryGetValue(path[..slash], out var parents)) continue;
                if (parents.Concat(owners).Distinct().Count() < 2) continue;
                conflicting.UnionWith(parents);
                conflicting.UnionWith(owners);
            }
        }
        libraryConflicts = conflicting.ToDictionary(id => id, _ => true);
    }

    private C.Button ConflictButton(ModPackage mod, bool compact = false)
    {
        var button = Button("", () => ShowModConflicts(mod), "ModConflict_" + mod.Id);
        button.Content = IndicatorDrawing("M 8,1 L 15,14 H 1 Z M 8,5 V 9 M 8,11 V 12", Brush("#EAC47F"), compact ? 13 : 16);
        button.Width = button.Height = compact ? 22 : 28;
        button.Background = SkinBrush("Base");
        button.Margin = new W.Thickness(0);
        button.Padding = new W.Thickness(0);
        button.ToolTip = L.T("ModConflictFiles");
        System.Windows.Automation.AutomationProperties.SetName(button, L.T("ModConflictFiles"));
        return button;
    }

    private static System.Windows.Shapes.Path IndicatorDrawing(string geometry, M.Brush stroke, double size = 16) => new()
    {
        Data = M.Geometry.Parse(geometry), Width = size, Height = size, Stretch = M.Stretch.Uniform,
        Stroke = stroke, StrokeThickness = 1.4, StrokeLineJoin = M.PenLineJoin.Round,
        StrokeStartLineCap = M.PenLineCap.Round, StrokeEndLineCap = M.PenLineCap.Round,
        HorizontalAlignment = W.HorizontalAlignment.Center, VerticalAlignment = W.VerticalAlignment.Center
    };

    private C.WrapPanel ModContentIndicators(ModPackage mod, bool compact = false, bool interactive = true)
    {
        var row = new C.WrapPanel();
        void Icon(string geometry, string description)
        {
            var tile = new C.Border
            {
                Width = compact ? 22 : 28, Height = compact ? 22 : 28, CornerRadius = new W.CornerRadius(6),
                BorderBrush = SkinBrush("Stroke"), BorderThickness = new W.Thickness(1),
                Margin = new W.Thickness(0, 0, 4, 0), Background = SkinBrush("Base"),
                Child = IndicatorDrawing(geometry, SkinBrush("Muted"), compact ? 13 : 16), ToolTip = description
            };
            row.Children.Add(tile);
        }
        if (mod.Kind == "variants") Icon("M 1,4 L 8,1 L 15,4 L 8,7 Z M 1,8 L 8,11 L 15,8 M 1,12 L 8,15 L 15,12", L.T("ModVariants"));

        {
            if (mod.Files.Any(file => file.Contains(".tex.", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)))
                Icon("M 1,2 H 15 V 14 H 1 Z M 2,12 L 6,8 L 9,11 L 12,7 L 15,11 M 5,5 H 6", "Textures");
            if (mod.Files.Any(file => file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)))
                Icon("M 5,4 L 1,8 L 5,12 M 11,4 L 15,8 L 11,12 M 9,2 L 7,14", "Lua scripts");
            if (mod.Files.Any(file => file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                Icon("M 4,4 H 12 V 12 H 4 Z M 6,1 V 4 M 10,1 V 4 M 6,12 V 15 M 10,12 V 15 M 1,6 H 4 M 12,6 H 15 M 1,10 H 4 M 12,10 H 15", "Plugins / DLL");
            if (mod.Kind == "reshade") Icon("M 8,1 L 10,6 L 15,8 L 10,10 L 8,15 L 6,10 L 1,8 L 6,6 Z", "ReShade");
        }
        if (interactive && HasLibraryConflict(mod)) row.Children.Add(ConflictButton(mod, compact));
        return row;
    }

    private IReadOnlyList<ActiveMod> LibraryConflictRecords(IReadOnlyList<ModPackage> packages, string folder) =>
        packages.Select(mod => new ActiveMod(mod.Id, folder, ImportAnalysis.Targets(mod.Files, mod.Destination, false)
            .Select(file => new ManagedFile(file, "", false)).ToArray())).ToArray();

    private void PreferConflictingMod(string id)
    {
        EnsureProfileReady();
        var lib = library!;
        var folder = GamePath;
        var profiles = CurrentProfiles();
        var collections = CurrentCollections();
        var plan = profiles.PreviewReplacement(id, folder, lib, collections);
        if (PreviewProfile(plan, lib, collection: true))
            Run(() => profiles.ApplyReplacement(plan, folder, lib, collections), L.T("Ready"));
    }
}
