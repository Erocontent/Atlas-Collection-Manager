using System.IO;
using Atlas.Core;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;

namespace Atlas.App;

public partial class PremiumWindow
{
    private readonly Dictionary<string, long> libraryNexusIds = new(StringComparer.OrdinalIgnoreCase);

    private long? LibraryNexusId(GameProfile profile, ModDetails details)
    {
        if (libraryNexusIds.TryGetValue(details.Id, out var linkedId)) return linkedId;
        if (!Uri.TryCreate(details.SourceUrl, UriKind.Absolute, out var source) ||
            source.Scheme != "https" || !source.IsDefaultPort || source.UserInfo.Length != 0 ||
            source.Host is not ("www.nexusmods.com" or "nexusmods.com")) return null;

        var parts = source.AbsolutePath.Trim('/').Split('/');
        return parts.Length == 3 &&
            parts[0].Equals(NexusGames.Domain(profile.Id), StringComparison.OrdinalIgnoreCase) &&
            parts[1].Equals("mods", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(parts[2], out var id) && id > 0 ? id : null;
    }

    private M.ImageSource? LibraryCover(ModDetailsStore metadata, ModDetails details, int width)
    {
        var local = ModImage(metadata, details.Cover, width);
        return local ?? (game is {} profile && LibraryNexusId(profile, details) is {} modId
            ? CachedNexusCover(profile, modId)
            : null);
    }

    private string? coverSummary;
    private string? coverGame;
    private string? visibleCoverCollection;
    private int coverCompleted, coverTotal;
    private bool coversDownloading;
    private event Action? CoverProgressChanged;

    private void IndexCoverSources(IReadOnlyList<ImportedCollection> collections)
    {
        libraryNexusIds.Clear();
        foreach (var collection in collections)
        {
            var sources = collection.Manifest.Mods.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in collection.Entries)
                if (entry.PackageId != null && sources.TryGetValue(entry.ModId, out var mod) && mod.Nexus is {} source)
                    libraryNexusIds.TryAdd(entry.PackageId, source.ModId);
        }
    }

    private C.Grid CoverDownloadControl(bool alignWithPicker = false)
    {
        var profile = game!;
        var control = new C.Grid();
        var button = Button(L.T("LibraryDownloadCovers"), () => DownloadLibraryCovers(), "DownloadLibraryCoversButton");
        if (alignWithPicker)
        {
            button.Height = 40;
            button.Margin = new W.Thickness(0);
        }
        control.Children.Add(button);
        var outline = new System.Windows.Shapes.Path
        {
            Stroke = SkinBrush("Accent"), StrokeThickness = 2,
            IsHitTestVisible = false, HorizontalAlignment = W.HorizontalAlignment.Left,
            VerticalAlignment = W.VerticalAlignment.Top
        };
        control.Children.Add(outline);
        void Update()
        {
            var running = coversDownloading && coverGame == profile.Id;
            var fraction = coverTotal == 0 ? 0 : (double)coverCompleted / coverTotal;
            button.IsEnabled = !coversDownloading;
            button.Content = running ? L.T("CoversProgress", coverCompleted, coverTotal, fraction) : L.T("LibraryDownloadCovers");
            button.ToolTip = coverGame == profile.Id ? coverSummary : null;
            outline.Margin = button.Margin;
            var width = Math.Max(0, button.ActualWidth - 2);
            var height = Math.Max(0, button.ActualHeight - 2);
            var geometry = new M.StreamGeometry();
            using (var path = geometry.Open())
            {
                var point = new W.Point(1, 1);
                path.BeginFigure(point, false, false);
                var remaining = (2 * (width + height)) * fraction;
                foreach (var edge in new[] { new W.Vector(width, 0), new W.Vector(0, height), new W.Vector(-width, 0), new W.Vector(0, -height) })
                {
                    if (edge.Length == 0 || remaining <= 0) continue;
                    point += edge * Math.Min(1, remaining / edge.Length);
                    path.LineTo(point, true, false);
                    remaining -= edge.Length;
                }
            }
            outline.Data = running ? geometry : null;
        }
        button.SizeChanged += (_, _) => Update();
        control.Loaded += (_, _) => { CoverProgressChanged += Update; Update(); };
        control.Unloaded += (_, _) => CoverProgressChanged -= Update;
        return control;
    }

    private void ShowCovers() => ShowCovers(null);

    private void ShowCovers(string? selectedId)
    {
        if (busy || game == null || library == null) return;
        var profile = game;
        var collections = CurrentCollections().List();
        IndexCoverSources(collections);
        var metadata = CurrentModDetails();
        var packages = library.List().ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        commandSection = "covers";
        profilesPageVisible = nexusPageVisible = false;
        modList = null;
        shadePanel = null;
        NavigationBuild();
        var page = Page();
        page.RowDefinitions.Add(new() { Height = W.GridLength.Auto });
        page.RowDefinitions.Add(new());
        var header = new C.StackPanel();
        header.Children.Add(SecondaryHeader(L.T("Covers"), L.T("CoversHint"), profile.Name, "\uEB9F"));
        var actions = new C.WrapPanel { Margin = new W.Thickness(14, 10, 14, 8) };

        var picker = new C.ComboBox { MinWidth = 220, Height = 40, Margin = new W.Thickness(0, 0, 12, 0), VerticalAlignment = W.VerticalAlignment.Center };
        picker.Items.Add(new C.ComboBoxItem { Content = L.T("CoversAll") });
        foreach (var collection in collections)
            picker.Items.Add(new C.ComboBoxItem { Content = collection.Manifest.Name, Tag = collection.Manifest.Id });
        actions.Children.Add(picker);
        var download = CoverDownloadControl(alignWithPicker: true);
        download.VerticalAlignment = W.VerticalAlignment.Center;
        actions.Children.Add(download);
        header.Children.Add(new C.Border { Child = actions, Background = SkinBrush("Surface"), CornerRadius = new W.CornerRadius(10), Margin = new W.Thickness(0, 0, 0, 10) });
        status = null;
        var report = SecondaryText("", 12, true);
        report.TextWrapping = W.TextWrapping.Wrap;
        var summary = SecondaryText("", 13, false, true);
        summary.TextWrapping = W.TextWrapping.Wrap;
        var resultBody = new C.StackPanel { Margin = new W.Thickness(14, 10, 14, 10) };
        resultBody.Children.Add(summary);
        resultBody.Children.Add(report);
        var details = new C.Border { Child = resultBody, Background = SkinBrush("Surface"), BorderBrush = SkinBrush("Stroke"), BorderThickness = new W.Thickness(1), CornerRadius = new W.CornerRadius(8) };
        header.Children.Add(details);
        page.Children.Add(header);
        var items = new C.WrapPanel();
        var scroll = new C.ScrollViewer { Content = items, Padding = new W.Thickness(0, 12, 14, 0), VerticalScrollBarVisibility = C.ScrollBarVisibility.Auto };
        C.Grid.SetRow(scroll, 1);
        page.Children.Add(scroll);

        void Render()
        {
            var result = coverGame == profile.Id ? coverSummary : null;
            var lines = result?.Split('\n', 2);
            details.Visibility = lines == null ? W.Visibility.Collapsed : W.Visibility.Visible;
            summary.Text = lines?[0] ?? "";
            report.Visibility = lines?.Length == 2 ? W.Visibility.Visible : W.Visibility.Collapsed;
            report.Text = lines?.Length == 2 ? lines[1] : "";
            items.Children.Clear();
            var id = (picker.SelectedItem as C.ComboBoxItem)?.Tag as string;
            visibleCoverCollection = id;
            var collection = collections.FirstOrDefault(item => item.Manifest.Id == id);
            var mods = collection == null
                ? packages.Values.Select(mod => (mod.Name, Package: (ModPackage?)mod, Source: (long?)null))
                : collection.Manifest.Mods.Select(mod => (mod.Name, Package: packages.GetValueOrDefault(collection.Entries.FirstOrDefault(entry => entry.ModId == mod.Id)?.PackageId ?? ""), Source: mod.Nexus?.ModId));
            foreach (var (name, mod, source) in mods)
            {
                var record = mod == null ? null : metadata.Get(mod);
                var nexusId = record == null ? source : LibraryNexusId(profile, record) ?? source;
                var nexusImage = nexusId is {} modId ? CachedNexusCover(profile, modId) : null;
                var image = record == null ? nexusImage : ModImage(metadata, record.Cover, 260) ?? nexusImage;
                var card = new C.StackPanel { Width = 264 };
                var artwork = new C.Grid { Height = 148, Background = SkinBrush("Raised"), ClipToBounds = true };
                if (image != null)
                    artwork.Children.Add(new C.Image { Source = image, Stretch = M.Stretch.UniformToFill });
                else
                {
                    var placeholder = SecondaryText(L.T("CoversMissing"), 13, true);
                    placeholder.HorizontalAlignment = W.HorizontalAlignment.Center;
                    placeholder.VerticalAlignment = W.VerticalAlignment.Center;
                    artwork.Children.Add(placeholder);
                }
                artwork.SizeChanged += (_, _) => artwork.Clip = new M.RectangleGeometry(
                    new W.Rect(0, 0, artwork.ActualWidth, artwork.ActualHeight), 9, 9);
                card.Children.Add(artwork);
                var body = new C.StackPanel { Margin = new W.Thickness(14) };
                var title = SecondaryText(name, 16, false, true);
                title.Height = 50;
                title.TextWrapping = W.TextWrapping.Wrap;
                title.TextTrimming = W.TextTrimming.CharacterEllipsis;
                title.ToolTip = name;
                body.Children.Add(title);
                var buttons = new C.WrapPanel();
                if (mod != null)
                {
                    buttons.Children.Add(Button(L.T("ModCover"), () => AddModImage(mod, true, afterSave: () => ShowCovers(id))));
                    if (nexusImage != null && nexusId is {} downloadedId && ModImage(metadata, record?.Cover, 96) != null)
                        buttons.Children.Add(Button(L.T("CoversUseNexus"), () =>
                        {
                            EnsureProfileReady();
                            metadata.AddImage(mod.Id, File.ReadAllBytes(NexusCoverPath(profile, downloadedId)), true);
                            ShowCovers(id);
                        }));
                }
                body.Children.Add(buttons);
                card.Children.Add(body);
                items.Children.Add(new C.Border { Child = card, Background = SkinBrush("Surface"), BorderBrush = SkinBrush("Stroke"), BorderThickness = new W.Thickness(1), Margin = new W.Thickness(0, 0, 14, 14), CornerRadius = new W.CornerRadius(10), ClipToBounds = true });
            }
        }
        picker.SelectionChanged += (_, _) => Render();
        picker.SelectedItem = picker.Items.Cast<C.ComboBoxItem>().FirstOrDefault(item => Equals(item.Tag, selectedId)) ?? picker.Items[0];
    }

    private async void DownloadLibraryCovers()
    {
        if (busy || nexusCollectionChecking || game == null || library == null) return;
        var profile = game;
        var currentLibrary = library;
        var errors = new List<string>();
        coversDownloading = nexusCollectionChecking = true;
        coverGame = profile.Id;
        coverSummary = null;
        coverCompleted = coverTotal = 0;
        try
        {
            IndexCoverSources(CurrentCollections().List());
            var metadata = CurrentModDetails();
            var existing = 0;
            var unlinked = 0;
            var missing = new Dictionary<long, string>();
            foreach (var package in currentLibrary.List())
            {
                var details = metadata.Get(package);
                if (LibraryCover(metadata, details, 96) != null) existing++;
                else if (LibraryNexusId(profile, details) is {} id) missing.TryAdd(id, package.Name);
                else unlinked++;
            }
            coverTotal = missing.Count;
            CoverProgressChanged?.Invoke();
            var downloaded = 0;
            if (missing.Count > 0)
            {
                var key = nexusCredentials.Load() ?? throw new InvalidOperationException(L.T("NexusNoKey"));
                using var client = nexusFactory(key);
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                foreach (var (id, name) in missing)
                {
                    if (nexusClosed) break;
                    try
                    {
                        await CacheNexusCoverAsync(client, profile, id, timeout.Token);
                        if (CachedNexusCover(profile, id) != null) downloaded++;
                        else errors.Add(name + " — " + L.T("CoversUnavailable"));
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or
                        System.Net.Http.HttpRequestException or ArgumentException or NotSupportedException or
                        System.Text.Json.JsonException or OperationCanceledException or FormatException)
                    {
                        errors.Add(name + " — " + error.Message);
                    }
                    coverCompleted++;
                    CoverProgressChanged?.Invoke();
                }
            }
            coverSummary = L.T("CoversSummary", downloaded, errors.Count, existing, unlinked);
            if (errors.Count != 0) coverSummary += "\n" + string.Join("\n", errors);
        }
        catch (Exception error) { coverSummary = error.Message; if (!nexusClosed) Error(error); }
        finally
        {
            coversDownloading = nexusCollectionChecking = false;
            CoverProgressChanged?.Invoke();
            if (!nexusClosed && game == profile)
            {
                if (!busy && commandSection == "covers") ShowCovers(visibleCoverCollection);
                else if (modList != null && !busy) RefreshMods();
                if (status != null && commandSection is "covers" or "library") status.Text = coverSummary ?? "";
            }
        }
    }
}
