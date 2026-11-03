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
        var dialog = new W.Window { Owner = this, Title = L.T("CollectionPreview"), Width = 760, Height = 690, MinWidth = 560, MinHeight = 480, Background = Brush("#15171B"), Foreground = SkinBrush("Text"), WindowStartupLocation = W.WindowStartupLocation.CenterOwner };
        dialog.Resources.MergedDictionaries.Add(Resources);
        var dock = new C.DockPanel { Margin = new W.Thickness(28) };
        var footer = new C.StackPanel();
        var categories = new C.CheckBox { Name = "ImportCategoriesCheck", Content = L.T("CollectionCategories"), IsChecked = true, Margin = new W.Thickness(0, 15, 0, 15), Foreground = SkinBrush("Text") };
        footer.Children.Add(categories);
        var actions = new C.WrapPanel { HorizontalAlignment = W.HorizontalAlignment.Right };
        actions.Children.Add(Button(L.T("Cancel"), () => dialog.DialogResult = false));
        actions.Children.Add(Button(L.T("CollectionImportSelected"), () => dialog.DialogResult = true, "ConfirmCollectionImportButton", true)); footer.Children.Add(actions);
        C.DockPanel.SetDock(footer, C.Dock.Bottom); dock.Children.Add(footer);
        var heading = new C.StackPanel(); heading.Children.Add(Text(manifest.Name, 27)); heading.Children.Add(Text(GameCatalog.Get(manifest.GameId).Name, 17));
        heading.Children.Add(Text(L.T("CollectionBy", manifest.Id, manifest.Author, manifest.Version), 12, true)); heading.Children.Add(Text(L.T("CollectionNotice"), 14));
        if (!string.IsNullOrWhiteSpace(manifest.Description)) heading.Children.Add(Text(manifest.Description, 13, true));
        C.DockPanel.SetDock(heading, C.Dock.Top); dock.Children.Add(heading);
        var rows = new C.StackPanel(); var choices = new Dictionary<string, C.CheckBox>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in manifest.Mods)
        {
            var content = new C.StackPanel(); content.Children.Add(Text(mod.Name + "  ·  " + mod.Version, 15));
            content.Children.Add(Text((mod.Category ?? L.T("Uncategorized")) + "  ·  " + L.T(mod.Recommended ? "CollectionRecommended" : "CollectionOptional"), 11, true));
            if (mod.SourceUrl != null) content.Children.Add(Text(mod.SourceUrl, 11, true));
            if (mod.Dependencies.Length > 0) content.Children.Add(Text(L.T("CollectionDependency", string.Join(", ", mod.Dependencies)), 11, true));
            var previous = existing?.Entries.FirstOrDefault(x => x.ModId.Equals(mod.Id, StringComparison.OrdinalIgnoreCase));
            var check = new C.CheckBox { Content = content, IsChecked = previous?.Selected ?? true, Margin = new W.Thickness(0, 8, 0, 8), Tag = mod.Id, VerticalContentAlignment = W.VerticalAlignment.Top };
            choices.Add(mod.Id, check); rows.Children.Add(check);
        }
        if (choices.Count == 0) rows.Children.Add(Text(L.T("CollectionNoItems"), 14, true));
        dock.Children.Add(new C.ScrollViewer { Content = rows, VerticalScrollBarVisibility = C.ScrollBarVisibility.Auto }); dialog.Content = dock;
        return dialog.ShowDialog() == true ? (categories.IsChecked == true, choices.Where(x => x.Value.IsChecked == true).Select(x => x.Key).ToArray()) : null;
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
        var list = new C.ListBox { Name = "CollectionList", Margin = new W.Thickness(0, 0, 24, 0) }; body.Children.Add(list);
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
            var toggle = Button(L.T(enabled ? "CollectionDeactivate" : "CollectionActivate"),
                () => ReviewCollectionEnable(item.Manifest.Id, enabled), "ToggleCollectionButton", !enabled);
            toggle.IsEnabled = !CurrentProfiles().HasPending && CollectionDownloads.Plan(item, installedPackages) is { Length: > 0 } plan && plan.All(mod => mod.Ready);
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
            var transferActions=new C.WrapPanel();
            transferActions.Children.Add(Button(L.T("NexusTitle"),ShowNexus,"CollectionNexusSettingsButton"));
            transferActions.Children.Add(Button(L.T("CollectionResume"),()=>StartCollectionDownload(id),"DownloadCollectionButton",true));
            var currentSession=collectionTransfer;
            if(currentSession!=null&&currentSession.Id==id&&currentSession.Profile.Id==profile.Id&&!currentSession.Cancelled)
            {
                transferActions.Children.Add(Button(L.T("CollectionTransferOpenNext"),OpenNextCollectionSource,"CollectionNextSourceButton"));
                transferActions.Children.Add(Button(L.T("CollectionTransferLocal"),ChooseCollectionFiles,"CollectionLocalFilesButton"));
                transferActions.Children.Add(Button(L.T("CollectionTransferCancel"),CancelCollectionTransfer,"CancelCollectionDownloadButton"));
            }
            var remove=Button(L.T("CollectionDelete"),()=>DeleteCollection(id),"DeleteCollectionButton");remove.IsEnabled=!CurrentProfiles().HasPending;transferActions.Children.Add(remove);
            var retry=Button(L.T("CollectionRetryFailed"),()=>StartCollectionDownload(id,true),"CollectionRetryFailedButton");transferActions.Children.Add(retry);
            C.Button? checkUpdates = null;
            checkUpdates = Button(L.T("NexusCollectionCheck"), () => SyncCollectionNexus(profile, id, true, checkUpdates), "CheckCollectionNexusButton");
            transferActions.Children.Add(checkUpdates);
            panel.Children.Add(transferActions);
            var transferStatus=new C.StackPanel{Name="CollectionTransferStatus",Margin=new W.Thickness(0,6,0,15)};
            panel.Children.Add(transferStatus);

            var packages = library!.List().ToDictionary(x => x.Id);
            panel.Children.Add(SecondaryStats((L.T("ProfileMods"),manifest.Mods.Length.ToString()),(L.T("CollectionReadyCount"),collection.Entries.Count(e=>e.PackageId!=null&&packages.ContainsKey(e.PackageId)).ToString())));
            var progress=new Dictionary<string,C.TextBlock>(StringComparer.OrdinalIgnoreCase);
            var summary=SecondaryText("",13,true);panel.Children.Add(summary);
            var activate=Button(L.T("CollectionReviewEnable"),()=>ReviewCollectionEnable(id),"CollectionReviewEnableButton",true);
            panel.Children.Add(activate);
            var metadata=CurrentModDetails();var active=library.GetActive();
            foreach (var mod in manifest.Mods)
            {
                var state = collection.Entries.Single(x => x.ModId.Equals(mod.Id, StringComparison.OrdinalIgnoreCase));
                var card = new C.StackPanel();
                var heading = SecondaryText(mod.Name, 19, false, true);
                heading.Margin = new W.Thickness(0, 0, 0, 5);
                card.Children.Add(heading);
                var badges = new C.WrapPanel { Margin = new W.Thickness(0, 0, 0, 5) };
                badges.Children.Add(CollectionBadge(SecondaryText("v" + mod.Version, 11)));
                card.Children.Add(badges);
                badges.Children.Add(CollectionBadge(SecondaryText((state.Category == "Uncategorized" ? L.T("Uncategorized") : state.Category) + "  ·  " + L.T(mod.Recommended ? "CollectionRecommended" : "CollectionOptional"), 11, true)));
                var include = new C.CheckBox { Content = L.T("CollectionSelected"), IsChecked = state.Selected, Foreground = SkinBrush("Text"), Margin = new W.Thickness(0, 2, 0, 6), Tag = mod.Id };
                include.IsEnabled=!CurrentProfiles().HasPending;
                include.Checked += (_, _) => Guard(() => {collections.SaveSelection(id, mod.Id, true);UpdateSummary();}); include.Unchecked += (_, _) => Guard(() => {collections.SaveSelection(id, mod.Id, false);UpdateSummary();}); card.Children.Add(include);
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
                var characters=package!=null?AutoLinkCharacters.ForPackage(package):mod.Installation.Character is {} character?new[]{character}:Array.Empty<string>();
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
            void UpdateSummary()
            {
                var current=collections.Find(id);if(current==null)return;
                var plan=CollectionDownloads.Plan(current,packages.Values);
                summary.Text=L.T("CollectionInstallSummary",current.Entries.Count(e=>e.Selected),plan.Length-current.Entries.Count(e=>e.Selected),plan.Count(x=>!x.Ready));
                var failures=ReadTransferFailures(profile,id);
                retry.IsEnabled=plan.Any(item=>!item.Ready&&failures.Contains(TransferKey(item.Mod)));
                foreach(var item in plan)
                {
                    var cached=item.Mod.Nexus is {} source&&nexusJobs.Any(job=>job.Link.GameDomain==NexusGames.Domain(profile.Id)&&job.Link.ModId==source.ModId&&job.Link.FileId==source.FileId&&job.Download!=null&&File.Exists(job.Download.Path));
                    progress[item.Mod.Id].Text=item.Ready ? "" : L.T(failures.Contains(TransferKey(item.Mod))?"NexusFailed":cached?"NexusComplete":item.Mod.Nexus==null||nexusUser?.IsPremium!=true?"CollectionManualNeeded":"CollectionTransferWaiting");
                }
                activate.IsEnabled=plan.Length>0&&plan.All(x=>x.Ready)&&!CurrentProfiles().HasPending;
            }
                        refreshCollectionTransfer=()=>
            {
                DrawCollectionTransfer(transferStatus,profile,id,progress);
                retry.IsEnabled=!CollectionDownloadsRunning(profile.Id,id)&&progress.Values.Any(c=>c.Text.StartsWith(L.T("NexusFailed"),StringComparison.Ordinal)||c.Text.StartsWith(L.T("CollectionImportFailed"),StringComparison.Ordinal));
            };
            UpdateSummary();refreshCollectionTransfer();

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
        try
        {
            EnsureProfileReady();
            var profiles = CurrentProfiles();
            var collections = CurrentCollections();
            var lib = library!;
            var folder = GamePath;
            var plan = profiles.PreviewCollection(id, folder, lib, collections, deactivate);
            if (!PreviewProfile(plan, lib, collection: true, deactivate: deactivate)) return;
            busy = true;
            PageHost.IsEnabled = Navigation.IsEnabled = BottomNavigation.IsEnabled = false;
            await Task.Run(() => profiles.ApplyCollection(id, plan, folder, lib, collections, deactivate));
        }
        catch (Exception error) { Error(error); }
        finally
        {
            busy = false;
            PageHost.IsEnabled = Navigation.IsEnabled = BottomNavigation.IsEnabled = true;
            ShowCollections(id);
        }
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
            if (removeMods.IsChecked == true)
            {
                var latest = collections.Find(id) ?? throw new InvalidOperationException(L.T("CollectionDeleteBusy"));
                if (!removable.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(RemovablePackages(latest)))
                    throw new InvalidOperationException(L.T("CollectionDeleteBusy"));
                var batches = new ModBatchService(LibraryRoot(profile), profile);
                busy = true;
                PageHost.IsEnabled = Navigation.IsEnabled = BottomNavigation.IsEnabled = false;
                try
                {
                    await Task.Run(() =>
                    {
                        var plan = batches.Preview(ModBatchAction.Delete, removable, folder, lib, collections);
                        if (plan.Problems.Length > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, plan.Problems));
                        var result = batches.Apply(plan, folder, lib, collections);
                        if (result.Error != null) throw new InvalidOperationException(result.Error);
                        collections.Delete(id);
                    });
                }
                finally
                {
                    busy = false;
                    PageHost.IsEnabled = Navigation.IsEnabled = BottomNavigation.IsEnabled = true;
                }
            }
            else collections.Delete(id);
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
