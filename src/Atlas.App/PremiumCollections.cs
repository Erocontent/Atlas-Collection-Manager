using System.IO;
using System.Diagnostics;
using Atlas.Core;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;

namespace Atlas.App;

public partial class PremiumWindow
{
    private string LibraryRoot(GameProfile profile) => profile.Id == "doa5" ? data : Path.Combine(data, "games", profile.Id);
    private CollectionStore CurrentCollections() => new(LibraryRoot(game!), game!);
    private void ImportCollection()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = L.T("CollectionFilter"), Title = L.T("ImportCollection") };
        if (picker.ShowDialog(this) == true) ImportCollectionFile(picker.FileName);
    }
    private void ImportCollectionFile(string path)
    {
        var manifest = CollectionReader.Read(path);
        var profile = GameCatalog.Get(manifest.GameId);
        if(!GameCatalog.IsAvailable(profile.Id))throw new InvalidOperationException(L.T("GameUnavailable"));
        if(new ModProfileStore(LibraryRoot(profile),profile).HasPending)throw new InvalidOperationException(L.T("ProfilePending"));
        var collections = new CollectionStore(LibraryRoot(profile), profile);
        var existing = collections.Find(manifest.Id);
        var preview = PreviewCollection(manifest, existing);
        if (preview == null) return;
        collections.Import(manifest, preview.Value.Categories, preview.Value.Selected);
        OpenGame(profile); ShowCollections(manifest.Id); SyncCollectionNexus(profile,manifest.Id,false);
    }
    private (bool Categories, string[] Selected)? PreviewCollection(CollectionManifest manifest, ImportedCollection? existing)
    {
        var dialog = ProfileDialog(L.T("CollectionPreview"), 760, 690);
        var dock = new C.DockPanel { Margin = new W.Thickness(24) };
        var heading = new C.StackPanel();
        heading.Children.Add(SecondaryText(manifest.Name, 22, false, true));
        heading.Children.Add(SecondaryText($"{GameCatalog.Get(manifest.GameId).Name}  ·  {manifest.Author}  ·  v{manifest.Version}", 13, true));
        heading.Children.Add(SecondaryText(L.T("CollectionNotice"), 12, true));
        var header = SecondarySurface(heading, new W.Thickness(12, 8, 12, 8));
        header.Margin = new W.Thickness(0, 0, 0, 12);
        C.DockPanel.SetDock(header, C.Dock.Top);
        dock.Children.Add(header);

        var footer = new C.StackPanel { Margin = new W.Thickness(0, 10, 0, 0) };
        footer.Children.Add(new C.Border { Height = 1, Background = SkinBrush("Stroke"), Margin = new W.Thickness(0, 0, 0, 10) });
        var selected = SecondaryText("", 13, true);
        var options = new C.StackPanel();
        options.Children.Add(selected);
        var categories = new C.CheckBox { Name = "ImportCategoriesCheck", Content = L.T("CollectionCategories"), IsChecked = true, Foreground = SkinBrush("Text") };
        options.Children.Add(categories);
        var footerRow = new C.DockPanel();
        var actions = new C.WrapPanel { HorizontalAlignment = W.HorizontalAlignment.Right };
        actions.Children.Add(Button(L.T("Cancel"), () => dialog.DialogResult = false));
        actions.Children.Add(Button(L.T("CollectionImportSelected"), () => dialog.DialogResult = true, "ConfirmCollectionImportButton", true));
        C.DockPanel.SetDock(actions, C.Dock.Right);
        footerRow.Children.Add(actions);
        footerRow.Children.Add(options);
        footer.Children.Add(footerRow);
        C.DockPanel.SetDock(footer, C.Dock.Bottom);
        dock.Children.Add(footer);

        var rows = new C.StackPanel { Margin = new W.Thickness(0, 0, 14, 0) };
        var choices = new Dictionary<string, C.CheckBox>(StringComparer.OrdinalIgnoreCase);
        void UpdateSelected() => selected.Text = L.T("CollectionPreviewSelected", choices.Values.Count(check => check.IsChecked == true), choices.Count);
        foreach (var mod in manifest.Mods)
        {
            var row = new C.DockPanel { LastChildFill = true };
            if (mod.SourceUrl is {} url)
            {
                var source = Button("↗", () => Guard(() => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })));
                source.ToolTip = L.T("CollectionOpenSource");
                System.Windows.Automation.AutomationProperties.SetName(source, L.T("CollectionOpenSource"));
                source.Padding = new W.Thickness(9, 3, 9, 3);
                source.VerticalAlignment = W.VerticalAlignment.Center;
                C.DockPanel.SetDock(source, C.Dock.Right);
                row.Children.Add(source);
            }
            var content = new C.StackPanel();
            content.Children.Add(SecondaryText(mod.Name, 15, false, true));
            var metadata = new[] { mod.FileName ?? (mod.Nexus is {} nexus ? "#" + nexus.FileId : null),
                mod.Version.Equals("unverified", StringComparison.OrdinalIgnoreCase) ? null : "v" + mod.Version,
                mod.Category, mod.Recommended ? L.T("CollectionRecommended") : null };
            var caption = string.Join("  ·  ", metadata.Where(value => !string.IsNullOrWhiteSpace(value)));
            if (caption.Length > 0) content.Children.Add(SecondaryText(caption, 12, true));
            if (mod.Dependencies.Length > 0) content.Children.Add(SecondaryText(L.T("CollectionDependency", string.Join(", ", mod.Dependencies)), 11, true));
            var previous = existing?.Entries.FirstOrDefault(entry => entry.ModId.Equals(mod.Id, StringComparison.OrdinalIgnoreCase));
            var check = new C.CheckBox { Content = content, IsChecked = previous?.Selected ?? true, Foreground = SkinBrush("Text"), VerticalContentAlignment = W.VerticalAlignment.Center };
            choices.Add(mod.Id, check);
            check.Checked += (_, _) => UpdateSelected();
            check.Unchecked += (_, _) => UpdateSelected();
            row.Children.Add(check);
            var card = new C.Border { Child = row, Padding = new W.Thickness(8, 2, 0, 2), BorderBrush = SkinBrush("Stroke"), BorderThickness = new W.Thickness(0, 0, 0, 1) };
            rows.Children.Add(card);
        }
        UpdateSelected();
        if (choices.Count == 0) rows.Children.Add(SecondaryText(L.T("CollectionNoItems"), 14, true));
        dock.Children.Add(new C.ScrollViewer { Content = rows, VerticalScrollBarVisibility = C.ScrollBarVisibility.Auto });
        dialog.Content = dock;
        return dialog.ShowDialog() == true ? (categories.IsChecked == true, choices.Where(pair => pair.Value.IsChecked == true).Select(pair => pair.Key).ToArray()) : null;
    }
    private void ShowCollections() => ShowCollections(null);
    private void ShowCollections(string? selectedId)
    {
        if (busy || game == null) return;
        nexusPageVisible = false;
        profilesPageVisible = false;
        var profile = game; var collections = CurrentCollections(); var imported = collections.List();
        modList = null; shadePanel = null;status=null; commandSection="collections"; NavigationBuild();
        var page = Page(); page.RowDefinitions.Add(new() { Height = W.GridLength.Auto }); page.RowDefinitions.Add(new());
        var header = new C.StackPanel(); header.Children.Add(SecondaryHeader(L.T("Collections"),L.T("CollectionImportHint"),profile.Name,"\uE8F1"));
        var actions = new C.WrapPanel(); actions.Children.Add(Button(L.T("CollectionGameLibrary"), () => OpenGame(profile))); actions.Children.Add(Button(L.T("ImportCollection"), ImportCollection,"ImportCollectionButton",true)); header.Children.Add(actions);
        actions.Margin=new W.Thickness(0,0,0,12); page.Children.Add(header);
        var body = new C.Grid(); body.ColumnDefinitions.Add(new() { Width = new W.GridLength(300) }); body.ColumnDefinitions.Add(new()); C.Grid.SetRow(body, 1); page.Children.Add(body);
        var list = new C.ListBox { Name = "CollectionList", Background = SkinBrush("Base"), Template = (C.ControlTemplate)FindResource("CollectionListTemplate"), Margin = new W.Thickness(0, 0, 24, 0) }; list.Resources[W.SystemColors.ControlBrushKey] = SkinBrush("Base");
        list.Resources[W.SystemColors.WindowBrushKey] = SkinBrush("Base");
        body.Children.Add(list);
        var panel = new C.StackPanel { Margin = new W.Thickness(0, 0, 12, 0) }; var scroll = new C.ScrollViewer { Content = panel, VerticalScrollBarVisibility = C.ScrollBarVisibility.Auto }; C.Grid.SetColumn(scroll, 1); body.Children.Add(scroll);
        var installedPackages = library!.List();
        var activeIds = library.GetActive().Where(item => string.Equals(item.Game, GamePath, StringComparison.OrdinalIgnoreCase)).Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var linkedIds = imported.SelectMany(item => item.Entries).Select(entry => entry.PackageId).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool IsActive(ImportedCollection item)
        {
            var plan = CollectionDownloads.Plan(item, installedPackages);
            return plan.Length > 0 && plan.All(mod => mod.Ready) && activeIds.Where(linkedIds.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(plan.Select(mod => mod.PackageId!));
        }
        foreach (var item in imported)
        {
            var enabled = IsActive(item);
            var anyActive = item.Entries.Any(entry => entry.PackageId != null && activeIds.Contains(entry.PackageId));
            var tile = new C.StackPanel();
            tile.Children.Add(SecondaryListItem(item.Manifest.Name,
                item.Manifest.Version + " · " + L.T("ProfileCount", item.Manifest.Mods.Length), "\uE8F1"));
            var tileActions = new C.WrapPanel { Margin = new W.Thickness(54, 8, 0, 0) };
            if (enabled)
            {
                var badge = CollectionEnabledBadge();
                badge.Height = 28;
                badge.Margin = new W.Thickness(0, 0, 8, 0);
                ((C.TextBlock)badge.Child).VerticalAlignment = W.VerticalAlignment.Center;
                tileActions.Children.Add(badge);
            }
            else if (anyActive) tileActions.Children.Add(CollectionBadge(SecondaryText(L.T("CollectionPartiallyActive"), 11), true));
            if (anyActive && !enabled)
            {
                var deactivate = Button(L.T("CollectionDeactivate"), () => ReviewCollectionEnable(item.Manifest.Id, true));
                deactivate.IsEnabled = !CurrentProfiles().HasPending;
                tileActions.Children.Add(deactivate);
            }
            var toggle = Button(L.T(enabled ? "CollectionDeactivate" : "CollectionActivate"),
                () => ReviewCollectionEnable(item.Manifest.Id, enabled), "ToggleCollectionButton", !enabled);
            toggle.IsEnabled = !CurrentProfiles().HasPending && (enabled || CollectionDownloads.Plan(item, installedPackages) is { Length: > 0 } plan && plan.All(mod => mod.Ready));
            toggle.Margin = new W.Thickness(0);
            toggle.Padding = new W.Thickness(10, 0, 10, 0);
            toggle.Height = 28;
            toggle.HorizontalAlignment = W.HorizontalAlignment.Left;
            tileActions.Children.Add(toggle);
            tile.Children.Add(tileActions);
            list.Items.Add(new C.ListBoxItem { Content = tile, Tag = item.Manifest.Id });
        }
        if (imported.Count == 0){list.Visibility=W.Visibility.Collapsed;C.Grid.SetColumn(scroll,0);C.Grid.SetColumnSpan(scroll,2);panel.Children.Add(SecondaryEmpty(L.T("Collections"),L.T("CollectionEmpty"),"\uE8F1"));}
        void Fill()
        {
            if ((list.SelectedItem as C.ListBoxItem)?.Tag is not string id) return;
            visibleCollectionId=id;
            var collection = collections.Find(id)!; var manifest = collection.Manifest; panel.Children.Clear();
            LoadNexusHistory();
            var transferActions = new C.WrapPanel();
            var managementActions = new C.WrapPanel();
            var download = Button(L.T("CollectionTransferStart"), () => StartCollectionDownload(id), "DownloadCollectionButton", true);
            var nextSource = Button(L.T("CollectionTransferOpenNext"), OpenNextCollectionSource, "CollectionNextSourceButton");
            var localFiles = Button(L.T("CollectionTransferLocal"), () => ChooseCollectionFiles(profile, id), "CollectionLocalFilesButton");
            var cancel = Button(L.T("CollectionTransferCancel"), CancelCollectionTransfer, "CancelCollectionDownloadButton");
            var retry = Button(L.T("CollectionRetryFailed"), () => StartCollectionDownload(id, true), "CollectionRetryFailedButton");
            foreach (var action in new[] { download, nextSource, localFiles, cancel, retry }) transferActions.Children.Add(action);
            managementActions.Children.Add(Button(L.T("NexusTitle"), ShowNexus, "CollectionNexusSettingsButton"));
            if (manifest.Mods.Any(mod => mod.Nexus != null))
            {
                C.Button? checkUpdates = null;
                checkUpdates = Button(L.T("NexusCollectionCheck"), () => SyncCollectionNexus(profile, id, true, checkUpdates), "CheckCollectionNexusButton");
                managementActions.Children.Add(checkUpdates);
            }
            var remove = Button(L.T("CollectionDelete"), () => DeleteCollection(id), "DeleteCollectionButton");
            remove.IsEnabled = !CurrentProfiles().HasPending;
            managementActions.Children.Add(remove);

            var transferStatus = new C.StackPanel { Name = "CollectionTransferStatus" };
            var packages = library!.List().ToDictionary(x => x.Id);
            var progress = new Dictionary<string, C.TextBlock>(StringComparer.OrdinalIgnoreCase);
            var summary = new C.StackPanel();
            var readyLabel = SecondaryText("", 16, false, true);
            var readyBar = new C.ProgressBar { Height = 5, Maximum = 100, Foreground = SkinBrush("Accent"), Background = SkinBrush("Elevated"), Margin = new W.Thickness(0, 0, 0, 12) };
            var stats = new C.WrapPanel();

            var activate = Button(L.T("CollectionReviewEnable"), () => ReviewCollectionEnable(id), "CollectionReviewEnableButton", true);
            transferActions.Children.Insert(0, activate);
            summary.Children.Add(readyLabel);
            summary.Children.Add(readyBar);
            summary.Children.Add(stats);
            transferActions.Margin = new W.Thickness(0, 5, 0, 0);

            summary.Children.Add(transferActions);
            summary.Children.Add(transferStatus);
            summary.Children.Add(new C.Border { Height = 1, Background = SkinBrush("Stroke"), Margin = new W.Thickness(0, 8, 0, 10) });
            summary.Children.Add(managementActions);
            var summarySurface = SecondarySurface(summary, new W.Thickness(16));
            summarySurface.Margin = new W.Thickness(0, 0, 0, 14);
            panel.Children.Add(summarySurface);
            var metadata=CurrentModDetails();var active=library.GetActive();
            foreach (var mod in manifest.Mods)
            {
                var state = collection.Entries.Single(x => x.ModId.Equals(mod.Id, StringComparison.OrdinalIgnoreCase));
                var card = new C.StackPanel();
                var heading = SecondaryText(mod.Name, 19, false, true);
                heading.Margin = new W.Thickness(0, 0, 0, 5);
                card.Children.Add(heading);
                if (mod.Nexus is {} fileSource) card.Children.Add(SecondaryText(mod.FileName ?? $"Nexus File ID {fileSource.FileId}", 12, true));
                var badges = new C.WrapPanel { Margin = new W.Thickness(0, 0, 0, 5) };
                badges.Children.Add(CollectionBadge(SecondaryText("v" + mod.Version, 11)));
                card.Children.Add(badges);
                badges.Children.Add(CollectionBadge(SecondaryText((state.Category == "Uncategorized" ? L.T("Uncategorized") : state.Category) + "  ·  " + L.T(mod.Recommended ? "CollectionRecommended" : "CollectionOptional"), 11, true)));
                var include = new C.CheckBox { Content = L.T("CollectionSelected"), IsChecked = state.Selected, Foreground = SkinBrush("Text"), Margin = new W.Thickness(0, 2, 0, 6), Tag = mod.Id };
                include.IsEnabled=!CurrentProfiles().HasPending;
                include.Checked += (_, _) => Guard(() => {collections.SaveSelection(id, mod.Id, true);UpdateSummary();refreshCollectionTransfer?.Invoke();}); include.Unchecked += (_, _) => Guard(() => {collections.SaveSelection(id, mod.Id, false);UpdateSummary();refreshCollectionTransfer?.Invoke();}); card.Children.Add(include);
                if (mod.Dependencies.Length > 0) card.Children.Add(Text(L.T("CollectionDependency", string.Join(", ", mod.Dependencies.Select(dep=>manifest.Mods.First(m=>m.Id.Equals(dep,StringComparison.OrdinalIgnoreCase)).Name))), 12, true));
                var linked = state.PackageId != null && packages.ContainsKey(state.PackageId);
                if (!linked || state.PackageVersion != mod.Version)
                    badges.Children.Add(CollectionBadge(SecondaryText(state.PackageId == null ? L.T("CollectionMissingZip") : !linked ? L.T("CollectionUnavailable") : L.T("CollectionUpdateAvailable", mod.Version, state.PackageVersion), 11, true)));
                var buttons = new C.WrapPanel();
                if (mod.SourceUrl != null) buttons.Children.Add(Button(L.T("CollectionOpenSource"), () => Process.Start(new ProcessStartInfo(mod.SourceUrl) { UseShellExecute = true })));
                if (mod.Nexus != null) buttons.Children.Add(Button(L.T(nexusUser?.IsPremium == true ? "NexusDownload" : "NexusOpenFilePage"), () => DownloadNexusItem(profile, mod)));
                buttons.Children.Add(Button(L.T("CollectionLocalZip"), () => AttachCollectionZip(id, mod.Id)));
                var indicator = SecondaryText("", 11);
                progress.Add(mod.Id, indicator);
                var progressBadge = CollectionBadge(indicator, true);
                var badgeStyle = new W.Style(typeof(C.Border));
                var empty = new W.DataTrigger { Binding = new W.Data.Binding("Text") { Source = indicator }, Value = "" };
                empty.Setters.Add(new W.Setter(W.UIElement.VisibilityProperty, W.Visibility.Collapsed));
                badgeStyle.Triggers.Add(empty);
                progressBadge.Style = badgeStyle;
                badges.Children.Add(progressBadge);
                var package=linked?packages[state.PackageId!]:null;
                var characters=package!=null?LibraryCharacters(package):mod.Installation.Character is {} character?new[]{character}:Array.Empty<string>();
                if(characters.Length>0)badges.Children.Add(CollectionBadge(SecondaryText(string.Join(" · ",characters),11)));
                if(package!=null&&active.Any(a=>a.Id==package.Id&&string.Equals(a.Game,GamePath,StringComparison.OrdinalIgnoreCase)))badges.Children.Add(CollectionEnabledBadge());
                var artwork=(package!=null?ModImage(metadata,metadata.Get(package).Cover,340):null)??CachedNexusCover(profile,mod);
                foreach(var label in card.Children.OfType<C.TextBlock>())
                    label.Margin=new W.Thickness(0,0,0,4);
                foreach(var action in buttons.Children.OfType<C.Button>())
                {
                    action.Height = 38;
                    action.Padding = new W.Thickness(14, 0, 14, 0);
                    action.VerticalAlignment = W.VerticalAlignment.Top;
                    action.VerticalContentAlignment = W.VerticalAlignment.Center;
                    action.Margin = new W.Thickness(0, 0, 6, 4);
                }
                var layout = new C.StackPanel();
                layout.Children.Add(artwork == null ? (W.UIElement)card : CollectionArtwork(card, artwork, 104, stretch: true));
                layout.Children.Add(new C.Border { Height = 1, Background = SkinBrush("Stroke"), Margin = new W.Thickness(0, 7, 0, 8) });
                layout.Children.Add(buttons);
                var surface=SecondarySurface(layout,new W.Thickness(14,10,14,6));
                surface.Margin=new W.Thickness(0,0,0,10);
                panel.Children.Add(surface);
            }
            CollectionDownloadItem[] pending = [];
            var previouslyLinked = false;
            void UpdateSummary()
            {
                var current=collections.Find(id);if(current==null)return;
                var plan=CollectionDownloads.Plan(current,packages.Values);
                var selected = current.Entries.Count(entry => entry.Selected);
                pending = plan.Where(item => !item.Ready).ToArray();
                previouslyLinked = current.Entries.Any(entry => entry.PackageId != null);
                var missing = pending.Length;
                var linked = current.Entries.Count(entry => entry.PackageId != null && packages.ContainsKey(entry.PackageId));
                readyLabel.Text = L.T("CollectionTransferSummary", plan.Length - missing, plan.Length);
                readyBar.Value = plan.Length == 0 ? 0 : 100d * (plan.Length - missing) / plan.Length;
                stats.Children.Clear();
                void Stat(string key, int value, bool highlight = false)
                {
                    var label = SecondaryText(L.T(key) + "  " + value, 12, !highlight);
                    label.Margin = new W.Thickness(0);
                    stats.Children.Add(new C.Border { Child = label, BorderBrush = SkinBrush("Stroke"), BorderThickness = new W.Thickness(0, 0, 1, 0), Padding = new W.Thickness(0, 2, 12, 2), Margin = new W.Thickness(0, 0, 12, 4) });
                }
                Stat("ProfileMods", manifest.Mods.Length);
                Stat("CollectionReadyCount", linked);
                Stat("CollectionStatSelected", selected);
                Stat("CollectionStatDependencies", Math.Max(0, plan.Length - selected));
                if (missing > 0) Stat("CollectionStatMissing", missing, true);
                var failures = collections.ReadFailureDetails(id);
                foreach(var item in plan)
                {
                    var cached=item.Mod.Nexus is {} source&&nexusJobs.Any(job=>job.Link.GameDomain==NexusGames.Domain(profile.Id)&&job.Link.ModId==source.ModId&&job.Link.FileId==source.FileId&&job.Download!=null&&File.Exists(job.Download.Path));
                    progress[item.Mod.Id].Text=item.Ready ? "" : failures.GetValueOrDefault(TransferKey(item.Mod)) is { Length: > 0 } detail ? detail : L.T(failures.ContainsKey(TransferKey(item.Mod))?"NexusFailed":cached?"NexusComplete":!item.Mod.DirectDownload&&(item.Mod.Nexus==null||nexusUser?.IsPremium!=true)?"CollectionManualNeeded":"CollectionTransferWaiting");
                }
                activate.IsEnabled=plan.Length>0&&plan.All(x=>x.Ready)&&!CurrentProfiles().HasPending;
            }
            refreshCollectionTransfer = () =>
            {
                DrawCollectionTransfer(transferStatus, profile, id, progress);
                var session = collectionTransfer is { Cancelled: false } current &&
                    current.Profile.Id == profile.Id && current.Id == id ? current : null;
                var running = CollectionDownloadsRunning(profile.Id, id);
                var failures = collections.ReadFailureDetails(id);
                void Show(C.Button button, bool visible) => button.Visibility = visible ? W.Visibility.Visible : W.Visibility.Collapsed;
                Show(activate, activate.IsEnabled && !running);
                Show(download, !running && pending.Length > 0);
                download.Content = L.T(previouslyLinked || session != null || failures.Count > 0 ? "CollectionResume" : "CollectionTransferStart");
                Show(nextSource, session?.Items.Any(CanOpenCollectionSource) == true);
                Show(localFiles, pending.Length > 0 && session?.Items.All(item => item.State is "CollectionTransferReady" or "CollectionTransferImporting") != true);
                Show(cancel, running);
                Show(retry, !running && pending.Any(item => failures.ContainsKey(TransferKey(item.Mod))));

            };
            UpdateSummary();
            refreshCollectionTransfer();
            if (manifest.Mods.Length == 0) panel.Children.Add(Text(L.T("CollectionNoItems"), 14, true));
        }
        list.SelectionChanged += (_, _) => Fill();
        list.SelectedItem = list.Items.Cast<C.ListBoxItem>().FirstOrDefault(x => Equals(x.Tag, selectedId)) ?? list.Items.Cast<C.ListBoxItem>().FirstOrDefault();
        LoadEmbeddedArtwork();
    }
    private C.Border CollectionEnabledBadge()
    {
        var label = SecondaryText("✓ " + L.T("Enabled"), 12, false, true);
        label.Foreground = SkinBrush("AccentText");
        var badge = CollectionBadge(label);
        badge.Background = SkinBrush("Accent");
        badge.HorizontalAlignment = W.HorizontalAlignment.Left;
        return badge;
    }

    private C.Border CollectionBadge(C.TextBlock label, bool accent = false)
    {
        label.Margin = new W.Thickness(0);
        if (accent) label.Foreground = SkinBrush("Accent");
        return new C.Border
        {
            Child = label,
            Background = SkinBrush("Elevated"),
            CornerRadius = new W.CornerRadius(5),
            Padding = new W.Thickness(8, 2, 8, 2),
            Margin = new W.Thickness(0, 0, 6, 3),
            MaxWidth = 360
        };
    }

    private C.Grid CollectionArtwork(C.StackPanel copy,M.ImageSource? image,double width, bool stretch = false)
    {
        var grid=new C.Grid();grid.ColumnDefinitions.Add(new(){Width=new W.GridLength(width+16)});grid.ColumnDefinitions.Add(new());
        var art = new C.Border
        {
            Width = width, MinHeight = width * .85, CornerRadius = new W.CornerRadius(7),
            VerticalAlignment = stretch ? W.VerticalAlignment.Stretch : W.VerticalAlignment.Top,
            Background = image == null ? SkinBrush("Elevated") : new M.ImageBrush(image) { Stretch = M.Stretch.UniformToFill },
            Child = image == null ? SecondaryIcon("\uE8F1", 52) : null
        };
        grid.Children.Add(art);
        grid.Children.Add(copy.AtColumn(1));return grid;
    }
    private async void ReviewCollectionEnable(string id, bool deactivate = false)
    {
        if (busy) return;
        var retry = false;
        try
        {
            EnsureProfileReady();
            var profiles = CurrentProfiles();
            var collections = CurrentCollections();
            var lib = library!;
            var folder = GamePath;
            ShowCollectionApplyProgress(L.T("CollectionChecking"), null);
            busy = true;
            ProfilePlan plan;
            try
            {
                plan = await Task.Run(() => profiles.PreviewCollection(id, folder, lib, collections, deactivate));
            }
            finally { busy = false; }
            ShowCollectionApplyProgress(null, null);
            if (!PreviewProfile(plan, lib, collection: true, deactivate: deactivate)) return;
            busy = true;
            PageHost.IsEnabled = Navigation.IsEnabled = BottomNavigation.IsEnabled = false;
            var names = lib.List().ToDictionary(mod => mod.Id, mod => mod.Name);
            var updating = true;
            var progress = new Progress<CollectionApplyProgress>(step =>
            {
                if (updating) ShowCollectionApplyProgress(L.T("CollectionApplyingCount", step.Completed, step.Total),
                    step.Total == 0 ? null : 100d * step.Completed / step.Total, names.GetValueOrDefault(step.PackageId, step.PackageId));
            });
            ShowCollectionApplyProgress(L.T("CollectionChecking"), null);
            try
            {
                await Task.Run(() => profiles.ApplyCollection(id, plan, folder, lib, collections, deactivate, progress));
                ShowCollectionApplyProgress(L.T("CollectionApplied"), 100);
            }
            finally { updating = false; }
        }
        catch (ProfileFileConflict conflict) when (!deactivate)
        {
            busy = false;
            ShowCollectionApplyProgress(null, null);
            try { retry = ReviewCollectionConflict(id, conflict); }
            catch (Exception error) { Error(error); }
        }
        catch (Exception error) { ShowCollectionApplyProgress(L.T("OperationFailed"), null); Error(error); }
        finally
        {
            busy = false;
            PageHost.IsEnabled = Navigation.IsEnabled = BottomNavigation.IsEnabled = true;
            ShowCollections(id);
        }
        if (retry) ReviewCollectionEnable(id);
    }
    private bool ReviewCollectionConflict(string id, ProfileFileConflict conflict)
    {
        var store = CurrentCollections();
        var collection = store.Find(id);
        if (collection == null) return false;
        var packages = library!.List();
        IndexCoverSources(store.List());
        var dialog = ProfileDialog(L.T("ModConflictFiles"), 650, 390);
        var panel = new C.StackPanel { Margin = new W.Thickness(24) };
        panel.Children.Add(Text(L.T("ModConflictFiles"), 22));
        panel.Children.Add(Text(conflict.FirstPath, 13, true));
        if (!string.Equals(conflict.FirstPath, conflict.SecondPath, StringComparison.OrdinalIgnoreCase))
            panel.Children.Add(Text(conflict.SecondPath, 13, true));
        foreach (var mod in new[] { conflict.First, conflict.Second })
        {
            var details = new[] { mod.Name, NexusPackageLabel(mod.Id), string.Join(", ", LibraryCharacters(mod)) };
            panel.Children.Add(Text(string.Join(" · ", details.Where(value => !string.IsNullOrWhiteSpace(value))), 15));
        }

        var actions = new C.WrapPanel();
        actions.Children.Add(Button(L.T("Cancel"), () => dialog.Close()));
        var lib = library!;
        var activeIds = lib.GetActive().Select(mod => mod.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var messageChoices = new[] { (conflict.First, conflict.Second), (conflict.Second, conflict.First) }
            .Where(pair => !activeIds.Contains(pair.Item2.Id))
            .Select(pair => (Winner: pair.Item1, Loser: pair.Item2, Paths: lib.SharedMessageOverrides(pair.Item1, pair.Item2)))
            .Where(choice => choice.Paths.Length > 0).ToArray();
        if (messageChoices.Length > 0)
        {
            panel.Children.Add(Text(L.T("Dmc5CollectionMessages"), 13, true));
            foreach (var path in messageChoices[0].Paths.Skip(1)) panel.Children.Add(Text(path, 12, true));
            foreach (var choice in messageChoices)
                actions.Children.Add(Button(L.T("Dmc5UseMessages", NexusPackageLabel(choice.Winner.Id) ?? choice.Winner.Name), () =>
                {
                    Guard(() =>
                    {
                        lib.PreferCollectionMessages(choice.Winner.Id, choice.Loser.Id);
                        dialog.DialogResult = true;
                    });
                }, primary: true));
            panel.Children.Add(actions);
            dialog.Content = new C.ScrollViewer { Content = panel, VerticalScrollBarVisibility = C.ScrollBarVisibility.Auto };
            return dialog.ShowDialog() == true;
        }
        panel.Children.Add(Text(L.T("CollectionConflictChoice"), 13, true));
        foreach (var (winner, loser) in new[] { (conflict.First, conflict.Second), (conflict.Second, conflict.First) })
        {
            var losingEntries = collection.Entries.Where(entry => entry.PackageId == loser.Id && entry.Selected).ToArray();
            if (losingEntries.Length != 1 || !collection.Entries.Any(entry => entry.PackageId == winner.Id)) continue;
            var entry = losingEntries[0];
            var reviewed = collection with { Entries = collection.Entries.Select(item =>
                item.ModId == entry.ModId ? item with { Selected = false } : item).ToArray() };
            var desired = CollectionDownloads.Plan(reviewed, packages);
            if (!desired.Any(item => item.PackageId == winner.Id) || desired.Any(item => item.PackageId == loser.Id)) continue;
            actions.Children.Add(Button(L.T("ModPrefer", NexusPackageLabel(winner.Id) ?? winner.Name), () =>
            {
                store.SaveSelection(id, entry.ModId, false);
                dialog.DialogResult = true;
            }));
        }
        panel.Children.Add(actions);
        dialog.Content = new C.ScrollViewer { Content = panel, VerticalScrollBarVisibility = C.ScrollBarVisibility.Auto };
        return dialog.ShowDialog() == true;
    }

    private async void DeleteCollection(string id)
    {
        if(busy||game==null)return;
        EnsureProfileReady();
        var profile=game;var collections=CurrentCollections();var item=collections.Find(id);if(item==null)return;
        var lib = library!;
        var folder = GamePath;
        string[] RemovablePackages(ImportedCollection collection)
        {
            var shared = collections.List().Where(other => other.Manifest.Id != id)
                .SelectMany(other => other.Entries).Select(entry => entry.PackageId)
                .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var installed = lib.List().Select(mod => mod.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return collection.Entries.Select(entry => entry.PackageId).OfType<string>()
                .Where(package => installed.Contains(package) && !shared.Contains(package))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        var removable = RemovablePackages(item);
        var dialog=ProfileDialog(L.T("CollectionDelete"),580,370);
        var body=new C.StackPanel{Margin=new W.Thickness(26)};
        body.Children.Add(SecondaryText(L.T("CollectionDelete"),25,false,true));
        body.Children.Add(SecondaryText(L.T("CollectionDeleteConfirm",item.Manifest.Name),14,true));
        var removeMods = new C.CheckBox
        {
            Content = SecondaryText(L.T("CollectionDeleteMods", removable.Length), 14),
            Margin = new W.Thickness(0, 8, 0, 0),
            Visibility = removable.Length > 0 ? W.Visibility.Visible : W.Visibility.Collapsed
        };
        body.Children.Add(removeMods);
        var actions=new C.WrapPanel{Margin=new W.Thickness(0,18,0,0)};
        actions.Children.Add(Button(L.T("Cancel"),()=>dialog.DialogResult=false,"CancelDeleteCollectionButton"));
        actions.Children.Add(Button(L.T("CollectionDelete"),()=>dialog.DialogResult=true,"ConfirmDeleteCollectionButton",true));body.Children.Add(actions);
        var layout=new C.Grid();layout.Children.Add(body);dialog.Content=layout;
        if(dialog.ShowDialog()!=true)return;
        try
        {
            if (busy || collectionImporting) throw new InvalidOperationException(L.T("CollectionDeleteBusy"));
            EnsureProfileReady();
            if (collectionTransfer is {} transfer && transfer.Profile.Id == profile.Id && transfer.Id == id)
                CancelCollectionTransfer();
            var deletePackages = removeMods.IsChecked == true;
            var libraryRoot = LibraryRoot(profile);
            ShowCollectionApplyProgress(L.T("CollectionDelete"), null);
            busy = true;
            PageHost.IsEnabled = Navigation.IsEnabled = BottomNavigation.IsEnabled = false;
            try
            {
                await Task.Run(() =>
                {
                    if (deletePackages)
                    {
                        var latest = collections.Find(id) ?? throw new InvalidOperationException(L.T("CollectionDeleteBusy"));
                        if (!removable.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(RemovablePackages(latest)))
                            throw new InvalidOperationException(L.T("CollectionDeleteBusy"));
                        var batches = new ModBatchService(libraryRoot, profile);
                        var plan = batches.Preview(ModBatchAction.Delete, removable, folder, lib, collections);
                        if (plan.Problems.Length > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, plan.Problems));
                        var result = batches.Apply(plan, folder, lib, collections);
                        if (result.Error != null) throw new InvalidOperationException(result.Error);
                    }
                    collections.Delete(id);
                });
            }
            finally
            {
                ShowCollectionApplyProgress(null, null);
                busy = false;
                PageHost.IsEnabled = Navigation.IsEnabled = BottomNavigation.IsEnabled = true;
            }
            if (game?.Id == profile.Id) ShowCollections();
        }
        catch (Exception error) { Error(error); }
    }
    private async void AttachCollectionZip(string collectionId, string modId)
    {
        if (busy) return;
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = L.T("ZipFilter"), Title = L.T("CollectionLocalZip") };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            EnsureProfileReady();
            var collections = CurrentCollections(); var lib = library!;
            busy = true; PageHost.IsEnabled = Navigation.IsEnabled = BottomNavigation.IsEnabled = false;
            await Task.Run(() => collections.AttachZip(collectionId, modId, picker.FileName, lib));
            busy = false; ShowCollections(collectionId);
        }
        catch (Exception error) { Error(error); }
        finally { busy = false; PageHost.IsEnabled = Navigation.IsEnabled = BottomNavigation.IsEnabled = true; }
    }
}
