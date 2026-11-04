using System.IO;
using System.Diagnostics;
using Atlas.Core;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;
using I = System.Windows.Media.Imaging;

namespace Atlas.App;

public partial class PremiumWindow : W.Window
{
    private readonly string data;
    private readonly PlayerPreferencesStore store;
    private readonly PlayerPreferences preferences;
    private GameProfile? game;
    private ModLibrary? library;
    private C.ListBox? modList;
    private C.TextBox? search;
    private C.TextBlock? status;
    private C.TextBlock? details;
    private C.StackPanel? shadePanel;
    private bool busy;
    private string GamePath => game != null && preferences.GamePaths.TryGetValue(game.Id, out var path) ? path : "";
    public PremiumWindow(string data, INexusCredentials? nexusCredentials = null, Func<string, NexusApiClient>? nexusFactory = null)
    {
        this.nexusCredentials = nexusCredentials ?? new NexusCredentialVault(); this.nexusFactory = nexusFactory ?? NexusApiClient.Create;
        this.data = data; Directory.CreateDirectory(data);
        store = new(data); preferences = store.Load(); L.SetLanguage(preferences.Language);
        InitializeComponent();
        SourceInitialized+=(_,_)=>AttachWorkAreaBounds();
        StateChanged+=(_,_)=>RefreshCaptionControls();
        Closing += (_, e) => e.Cancel = busy;
        Closed += (_, _) => { nexusClosed = true; collectionTransfer?.DirectCancellation?.Cancel(); nexusSignIn?.Cancel(); foreach (var job in nexusJobs) job.Cancellation?.Cancel(); nexusClient?.Dispose(); };
        DragOver += (_, e) => { e.Effects = !busy && e.Data.GetDataPresent(W.DataFormats.FileDrop) ? W.DragDropEffects.Copy : W.DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => Guard(() => {
            var paths = e.Data.GetData(W.DataFormats.FileDrop) as string[];
            if (paths is not { Length: 1 }) throw new InvalidOperationException(L.T("CollectionDropOnly"));
            if (Path.GetExtension(paths[0]).Equals(".atlascollection", StringComparison.OrdinalIgnoreCase)) { ImportCollectionFile(paths[0]); return; }
            if (!ModArchives.IsSupported(paths[0])) throw new InvalidOperationException(L.T("CollectionDropOnly"));
            if (game == null) { PickGameForImport(paths[0]); return; }
            ImportZip(paths[0]);
        });
        ApplyAppearance(); Home();
    }
    private M.Brush Brush(string color) => ResolveSkinBrush(color);
    private C.TextBlock Text(string text, double size = 14, bool muted = false) => new() { Text = text, FontSize = size, Foreground = SkinBrush(muted ? "Muted" : "Text"), TextWrapping = W.TextWrapping.Wrap, Margin = new W.Thickness(0, 0, 0, 10) };
    private C.Button Button(string text, Action action, string? name = null, bool primary = false)
    {
        var b = new C.Button { Content = text, Margin = new W.Thickness(0, 0, 8, 8), HorizontalContentAlignment = W.HorizontalAlignment.Left };
        if (name != null) b.Name = name;
        if (primary) b.Style = (W.Style)FindResource("Primary");
        b.Click += (_, _) => Guard(action); return b;
    }
    private void ApplyAppearance()
    {
        PageHost.Background=null;ApplySkinShell();
    }
    private static I.BitmapImage LoadImage(string path) { using var stream = File.OpenRead(path); return LoadImage(stream); }
    private static I.BitmapImage LoadImage(Stream stream) { var image = new I.BitmapImage(); image.BeginInit(); image.CacheOption = I.BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image; }
    private M.ImageSource? Cover(GameProfile profile)
    {
        if (preferences.Covers.TryGetValue(profile.Id, out var relative)) { var path = Visuals.AppearancePath(data, relative); try { if (path != null && File.Exists(path)) return LoadImage(path); } catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException or FormatException or UnauthorizedAccessException) { } }
        using var stream = typeof(PremiumWindow).Assembly.GetManifestResourceStream($"Atlas.App.Assets.{profile.Id}.jpg");
        return stream == null ? null : LoadImage(stream);
    }
    private void NavigationBuild()
    {
        ApplySkinShell();Navigation.Children.Clear(); BottomNavigation.Children.Clear();CinematicNavigation.Children.Clear();CinematicUtilities.Children.Clear();
        var navigation=IsCinematic?CinematicNavigation:Navigation;var utilities=IsCinematic?CinematicUtilities:BottomNavigation;
        navigation.Children.Add(IsCinematic?Button(L.T("Library"), Home, "LibraryButton"):CommandNavigation("Library","\uE80F",Home,"LibraryButton",commandSection=="library"));
        if (game != null) navigation.Children.Add(IsCinematic?Button(L.T("Collections"), ShowCollections, "CollectionsButton"):CommandNavigation("Collections","\uE8F1",ShowCollections,"CollectionsButton",commandSection=="collections"));
        if (game != null) navigation.Children.Add(IsCinematic ? Button(L.T("Covers"), ShowCovers, "CoversButton") : CommandNavigation("Covers", "\uEB9F", ShowCovers, "CoversButton", commandSection == "covers"));
        if (game != null) navigation.Children.Add(IsCinematic?Button(L.T("Profiles"), ShowProfiles, "ProfilesButton"):CommandNavigation("Profiles","\uE77B",ShowProfiles,"ProfilesButton",commandSection=="profiles"));
        BuildSidebarDownloads(utilities);
        BuildCollectionApplyPanel(utilities);
        utilities.Children.Add(IsCinematic?Button(L.T("Settings"), ShowSettings, "SettingsButton"):CommandNavigation("Settings","\uE713",ShowSettings,"SettingsButton",commandSection=="settings"));

        var languages = new C.ComboBox { Name = "LanguageSelector", ItemsSource = L.Languages, SelectedItem = L.Languages.FirstOrDefault(x => x.Code == preferences.Language), Width=IsCinematic?125:double.NaN,Margin = new W.Thickness(0, 0, 0, IsCinematic?8:18) };
        languages.SelectionChanged += (_, _) => Guard(() => { if (languages.SelectedItem is LanguageOption option && option.Code != preferences.Language) { preferences.Language = option.Code; store.Save(preferences); L.SetLanguage(option.Code); if (nexusPageVisible) ShowNexus(); else if(settingsPageVisible)ShowSettings();else if (commandSection == "covers") ShowCovers(); else if (profilesPageVisible) ShowProfiles(); else if (game is {} current) OpenGame(current); else Home(); } });
        languages.Style = (W.Style)FindResource("LauncherLanguageSelector");
        if(!IsCinematic){languages.Margin=new W.Thickness(12,4,12,16);languages.Height=46;languages.FontSize=14;}
        utilities.Children.Add(languages);
        if(!IsCinematic){var footer=SecondaryText("Version - " + (typeof(PremiumWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"),13,true);footer.Name="SidebarVersion";footer.Margin=new W.Thickness(28,0,28,0);utilities.Children.Add(footer);}
    }
    private C.Grid Page()
    {
        settingsPageVisible=false;PageHost.Children.Clear(); var page = new C.Grid { Margin=IsCinematic?new W.Thickness(0):new W.Thickness(28,52,28,24) }; PageHost.Children.Add(page); return page;
    }
    private void Home()
    {
        if(!IsCinematic){CommandHome();return;}
        if (busy) return; profilesPageVisible = false; nexusPageVisible = false; game = null; library = null; modList = null; status = null; commandSection="library"; NavigationBuild();
        var page = Page(); page.RowDefinitions.Add(new() { Height = W.GridLength.Auto }); page.RowDefinitions.Add(new());
        var heading = new C.StackPanel(); heading.Children.Add(Text(L.T("LibraryBreadcrumb"), 11, true)); heading.Children.Add(CommandTitle(L.T("LibraryTitle"), 38)); heading.Children.Add(Text(L.T("LibraryHint"), 14, true));
        var filter = new C.TextBox { Name = "GameSearch", ToolTip = L.T("SearchGames"), MaxWidth = 380, MinWidth = 300 };
        System.Windows.Automation.AutomationProperties.SetName(filter, L.T("SearchGames")); heading.Children.Add(SearchField(filter,L.T("SearchGames"),380)); page.Children.Add(heading);
        var cards = new C.WrapPanel(); var scroll = new C.ScrollViewer { Content = cards, VerticalScrollBarVisibility = C.ScrollBarVisibility.Auto }; C.Grid.SetRow(scroll, 1); page.Children.Add(scroll);
        void Fill() { cards.Children.Clear(); foreach (var profile in GameCatalog.Available.Where(x => x.Name.Contains(filter.Text, StringComparison.OrdinalIgnoreCase))) {
            var width = 320; var stack = new C.StackPanel { Width = width };
            var coverImage = new C.Image { Source = GameHero(profile), Width = width, Height = 180, Stretch = M.Stretch.UniformToFill, Clip = new M.RectangleGeometry(new W.Rect(0,0,width,180),8,8) };
            stack.Children.Add(new C.Border { Child = coverImage, Margin = new W.Thickness(0, 0, 0, 16) });
            var caption=new C.StackPanel { Margin=new W.Thickness(14,0,14,8) }; caption.Children.Add(CommandTitle(profile.Name,20)); caption.Children.Add(Text(profile.Subtitle,12,true)); stack.Children.Add(caption);
            var card = Button("", () => OpenGame(profile), "Game_" + profile.Id); card.Tag = profile.Id; card.Content = stack; card.Background = SkinBrush("Surface"); card.BorderBrush=SkinBrush("Stroke"); card.BorderThickness=new W.Thickness(1); card.Padding = new W.Thickness(0); card.Margin = new W.Thickness(0, 0, 28, 24);
            var menu = new C.ContextMenu(); foreach (var reset in new[] { false, true }) { var item = new C.MenuItem { Header = L.T(reset ? "RestoreCover" : "ChangeCover") }; item.Click += (_, _) => Guard(() => ChangeCover(profile, reset)); menu.Items.Add(item); } card.ContextMenu = menu;
            var tile=new C.Grid{Width=width,Margin=new W.Thickness(0,0,28,24)};card.Margin=new W.Thickness(0);tile.Children.Add(card);
            var options=Button("···",()=>{},"CoverOptions_"+profile.Id);options.ToolTip=L.T("ChangeCover");options.Width=34;options.Height=30;options.Padding=new W.Thickness(0);options.Margin=new W.Thickness(0,8,8,0);options.HorizontalAlignment=W.HorizontalAlignment.Right;options.VerticalAlignment=W.VerticalAlignment.Top;options.Click+=(_,e)=>{e.Handled=true;OpenThemedMenu(menu,options);};tile.Children.Add(options);cards.Children.Add(tile);
        } }
        filter.TextChanged += (_, _) => Fill(); Fill();
    }
    private void OpenGame(GameProfile profile)
    {
        if(!GameCatalog.IsAvailable(profile.Id))throw new InvalidOperationException(L.T("GameUnavailable"));
        OpenCommandGame(profile);
    }
    private ModPackage? Selected => (modList?.SelectedItem as C.ListBoxItem)?.Tag as ModPackage;
    private void RefreshMods()
    {
        RefreshOrganizedMods();
    }
    private void ShowDetails() { RefreshBatchToggleButtons();if(selectionCaption!=null)selectionCaption.Text=L.T("ModSelection",SelectedMods().Length);if (details != null) details.Text = Selected is {} mod ? L.T("SelectedDetails", mod.Name, mod.Destination == "" ? L.T("RootGame") : mod.Destination) + string.Join("\n", mod.Files.Take(100)) : L.T("EmptyDetails");RefreshSkinInspector(); }
    private void RefreshShade()
    {
        if (shadePanel == null || library == null) return; shadePanel.IsEnabled=!CurrentProfiles().HasPending; shadePanel.Children.Clear(); shadePanel.Children.Add(Text(L.T("ReShadeTitle"), 21)); var exists = Directory.Exists(GamePath);
        shadePanel.Children.Add(Text(L.T(exists && ReShadePresets.HasRuntime(GamePath,game) ? "ReShadeRuntimeInstalled" : "ReShadeRuntimeMissing"), 12, true)); shadePanel.Children.Add(Button(L.T("ReShadeInstallOfficial"), InstallShade));
        shadePanel.Children.Add(Text(L.T("ReShadePresetCaption"), 12, true)); shadePanel.Children.Add(Text(L.T("ReShadeDefaultLabel"), 12));
        var presets = exists ? ReShadePresets.InstalledPresets(GamePath, library.GetActive().Where(x => string.Equals(x.Game, GamePath, StringComparison.OrdinalIgnoreCase)).SelectMany(x => x.Files).Select(x => x.Path)) : [];
        var combo = new C.ComboBox { ItemsSource = presets, Name = "PresetSelector", Margin = new W.Thickness(0, 0, 0, 12) }; var current = exists ? ReShadePresets.ReadDefault(GamePath)?.Replace('\\', '/') : null; if(current?.StartsWith("./")==true)current=current[2..]; combo.SelectedItem = presets.FirstOrDefault(x => x.Equals(current, StringComparison.OrdinalIgnoreCase)); shadePanel.Children.Add(combo);
        shadePanel.Children.Add(Text(presets.Length == 0 ? L.T("ReShadeNoPresets") : current ?? L.T("ReShadeDefaultUnset"), 11, true));
        var apply = Button(L.T("ReShadeApplyDefault"), () => { EnsureProfileReady(); library.EnsureReady(); if (combo.SelectedItem is not string preset) throw new InvalidOperationException(L.T("ReShadeInvalidPreset")); if(library.GetActive().Where(mod=>string.Equals(mod.Game,GamePath,StringComparison.OrdinalIgnoreCase)).Any(mod=>mod.Files.Any(file=>file.Path.Equals("ReShade.ini",StringComparison.OrdinalIgnoreCase))))throw new InvalidOperationException(L.T("Conflict","ReShade.ini")); ReShadePresets.SetDefault(game!, GamePath, preset, Path.Combine(data, "reshade-backups")); RefreshShade(); RefreshProfileStatus(); status!.Text = L.T("ReShadeDefaultApplied", preset); },"ApplyPresetButton");
        apply.IsEnabled=presets.Length>0;shadePanel.Children.Add(apply);
    }
    private void ChooseGame(bool exe)
    {
        EnsureProfileReady();
        string path; if (exe) { var picker = new Microsoft.Win32.OpenFileDialog { Title = L.T("PickExeTitle", game!.Executables[0]), Filter = L.T("ExeFilter") }; if (picker.ShowDialog(this) != true) return; path = Path.GetDirectoryName(picker.FileName)!; }
        else { var picker = new Microsoft.Win32.OpenFolderDialog { Title = L.T("PickFolderTitle", game!.Name, game.Executables[0]) }; if (picker.ShowDialog(this) != true) return; path = picker.FolderName; }
        preferences.GamePaths[game!.Id] = ModLibrary.ValidateGame(path, game); store.Save(preferences); OpenGame(game);
    }
    private void Import() { var picker = new Microsoft.Win32.OpenFileDialog { Filter = L.T("ZipFilter"), Title = L.T("ImportTitle", game!.Name) }; if (picker.ShowDialog(this) == true) ImportZip(picker.FileName); }
    private void PickGameForImport(string zip)
    {
        var dialog = new W.Window { Owner = this, Title = L.T("YourGames"), Width = 440, Height = 560, WindowStartupLocation = W.WindowStartupLocation.CenterOwner, Background = Brush("#15171B"), Foreground = SkinBrush("Text") }; var panel = new C.StackPanel { Margin = new W.Thickness(24) }; panel.Children.Add(Text(L.T("YourGames"), 22)); foreach (var profile in GameCatalog.Available) panel.Children.Add(Button(profile.Name, () => { dialog.DialogResult = true; OpenGame(profile); ImportZip(zip); })); dialog.Content = new C.ScrollViewer{Content=panel,VerticalScrollBarVisibility=C.ScrollBarVisibility.Auto}; dialog.ShowDialog();
    }
    private async void ImportZip(string zip) => await ImportZipCore(zip, true);
    private async Task ImportZipCore(string zip, bool activateReShade)
    {
        if(busy)return;
        try
        {
            EnsureProfileReady();
            var lib = library!; var profile = game!; var selectedFolder=GamePath; ArchiveImportAnalysis analysis;ImportTargetReview? importReview=null;var activeSnapshot=lib.GetActive();var packageSnapshot=lib.List();
            busy=true;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=false;if(status!=null)status.Text=L.T("Processing");
            try { await Task.Yield(); analysis=await Task.Run(()=>{var found=ImportAnalysis.Inspect(zip,profile);if(found.ReShade!=null)importReview=ImportAnalysis.Review(found.ReShade.Files.Select(f=>f.TargetPath).ToArray(),selectedFolder,activeSnapshot,packageSnapshot);return found;}); }
            finally { busy=false;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=true; }
            if(analysis.ReShade is {} plan)
            {
                var name=Path.GetFileNameWithoutExtension(zip);var decision=Preview(name,plan,activateReShade,importReview,analysis.WarningKeys);
                if(decision==null){if(status!=null)status.Text=L.T("Ready");return;}
                var enable=decision.Value;var folder=enable?ModLibrary.ValidateGame(selectedFolder,profile):"";
                await RunAsync(()=>{var package=lib.ImportReShade(zip,name,plan);if(enable)lib.Enable(package.Id,folder);},L.T(enable?"ReShadeImportComplete":"Imported"));return;
            }
            using var dialog = new ImportForm(zip,profile,analysis,selectedFolder,activeSnapshot,packageSnapshot); if(dialog.ShowDialog(new NativeOwner(this))!=System.Windows.Forms.DialogResult.OK)return;
            var title = dialog.ModName; var category = dialog.Category; var destination = dialog.Destination; var skin = dialog.AutoLinkSkin;var character=dialog.Character; await RunAsync(() => lib.ImportZip(zip, title, category, destination, skin,character,true), L.T("Imported"));
        }
        catch(Exception error){Error(error);}
    }
    private bool? Preview(string name, ReShadeImportPlan plan, bool activate = true,ImportTargetReview? review=null,string[]? warnings=null)
    {
        var dialog = new W.Window { Owner = this, Title = L.T("ReShadeImportRecognized"), Width = 700, Height = 620, Background = Brush("#15171B"), Foreground = SkinBrush("Text"), WindowStartupLocation = W.WindowStartupLocation.CenterOwner };
        dialog.Resources.MergedDictionaries.Add(Resources);
        var grid = new C.DockPanel { Margin = new W.Thickness(26) }; var actions = new C.WrapPanel { HorizontalAlignment = W.HorizontalAlignment.Right };bool enable=false; actions.Children.Add(Button(L.T("Cancel"), () => dialog.DialogResult = false));actions.Children.Add(Button(L.T("ImportAction"),()=>{enable=false;dialog.DialogResult=true;},"SmartReShadeImportButton",true));if(activate){var activateButton=Button(L.T("Enable"),()=>{enable=true;dialog.DialogResult=true;},"SmartReShadeEnableButton");activateButton.IsEnabled=review?.FolderConfigured==true&&review.Conflicts.Length==0&&review.UnsafePaths.Length==0;actions.Children.Add(activateButton);} C.DockPanel.SetDock(actions, C.Dock.Bottom); grid.Children.Add(actions);
        var stack = new C.StackPanel(); stack.Children.Add(Text(L.T("SmartDetected",L.T("ReShadeTitle")),18));stack.Children.Add(Text(L.T("SmartReadOnly"),12,true));foreach(var warning in warnings??[])stack.Children.Add(Text(L.T(warning),12,true));if(review!=null){stack.Children.Add(Text(review.FolderConfigured?L.T("SmartOverwrite",review.ExistingFiles.Length):L.T("SmartNoFolder"),12,true));if(review.Conflicts.Length>0)stack.Children.Add(Text(L.T("SmartConflicts",string.Join(", ",review.Conflicts)),13));if(review.UnsafePaths.Length>0)stack.Children.Add(Text(L.T("SmartUnsafePaths",string.Join(", ",review.UnsafePaths.Take(10))),13));}stack.Children.Add(Text(L.T("ReShadeImportConfirm", name, plan.Files.Length, plan.Presets.Length), 16)); stack.Children.Add(Text(L.T("ReShadeImportFiles"), 13)); stack.Children.Add(Text(string.Join("\n", plan.Files.Select(x => x.EntryName + "  →  " + x.TargetPath)), 12, true)); if (plan.IgnoredFiles.Length > 0) stack.Children.Add(Text("—\n" + string.Join("\n", plan.IgnoredFiles.Select(x => "×  " + x)), 12, true)); grid.Children.Add(new C.ScrollViewer { Content = stack, VerticalScrollBarVisibility = C.ScrollBarVisibility.Auto }); dialog.Content = grid; return dialog.ShowDialog() == true?enable:null;
    }
    private void Toggle(bool enable)
    {
        EnsureProfileReady();
        var mod = Selected ?? throw new InvalidOperationException(L.T("SelectMod")); var folder = ModLibrary.ValidateGame(GamePath, game!); var lib = library!;
        if (!Confirm(enable ? L.T("EnableConfirm", mod.Name, game!.Name, folder, mod.Destination == "" ? L.T("RootGame") : mod.Destination) : L.T("DisableConfirm", mod.Name))) return;
        Run(() => { if (enable) { CurrentCollections().EnsureCanEnable(mod.Id, lib, folder); lib.Enable(mod.Id, folder); } else lib.Disable(mod.Id, folder); }, L.T(enable ? "ModEnabled" : "ModDisabled", mod.Name));
    }
    private void Delete()
    {
        EnsureProfileReady();
        var mod = Selected ?? throw new InvalidOperationException(L.T("SelectMod")); var lib = library!; var active = lib.GetActive().FirstOrDefault(x => x.Id == mod.Id);
        if (!Confirm(active == null ? L.T("DeleteInactiveConfirm", mod.Name) : L.T("DeleteActiveConfirm", mod.Name, active.Game))) return;
        Run(() => { if (active != null) lib.Disable(mod.Id, active.Game); lib.Delete(mod.Id); }, L.T("Deleted", mod.Name));
    }
    private async void InstallShade()
    {
        if (busy) return;
        try
        {
            EnsureProfileReady();
            var lib = library!;
            lib.EnsureReady();
            var profile = game!;
            var folder = ModLibrary.ValidateGame(GamePath, profile);
            var collisions = ReShadePresets.ConflictingRuntimeFiles(folder, new[] { "d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll", "dxgi.dll", "opengl32.dll", "ddraw.dll" });
            var autoLink = collisions is [var proxy] && proxy.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase)
                ? lib.ManagedAutoLinkProxy(folder) : null;
            if (collisions.Length > 0 && autoLink == null)
                throw new InvalidOperationException(L.T("ReShadeProxyConflict", string.Join(", ", collisions)));
            if (!Confirm(L.T(autoLink == null ? "ReShadeSetupNotice" : "ReShadeAutoLinkNotice", profile.Name))) return;
            busy = true;
            PageHost.IsEnabled = Navigation.IsEnabled = BottomNavigation.IsEnabled = false;
            if (status != null) status.Text = L.T("Processing");
            if (autoLink != null)
            {
                await Task.Run(() => lib.PrepareAutoLinkForReShade(autoLink, folder));
                if (ReShadePresets.HasRuntime(folder, profile))
                {
                    RefreshShade();
                    if (status != null) status.Text = L.T("ReShadeRuntimeInstalled");
                    return;
                }
            }
            var installer = await ReShadeSetup.DownloadAsync(Path.Combine(data, "cache"), new Progress<double>(p =>
            {
                if (status != null) status.Text = L.T("Processing") + " " + (int)(p * 100) + "%";
            }));
            var setup = ReShadeSetup.Prepare(profile, folder, installer);

            if (status != null) status.Text = L.T("OfficialSetupRunning");
            using var process = Process.Start(setup) ?? throw new InvalidOperationException(L.T("ReShadeSetupFailed", ""));
            await process.WaitForExitAsync();
            RefreshShade();
            if (status != null) status.Text = L.T(ReShadePresets.HasRuntime(folder, profile) ? "ReShadeRuntimeInstalled" : "ReShadeSetupClosed");
        } catch (Exception ex) { Error(ex); } finally { busy = false; PageHost.IsEnabled = Navigation.IsEnabled = BottomNavigation.IsEnabled = true; }
    }
    private async void Run(Action action, string done) => await RunAsync(action, done);
    private async Task RunAsync(Action action, string done)
    {
        if (busy) return; busy = true; PageHost.IsEnabled = Navigation.IsEnabled = BottomNavigation.IsEnabled = false; if (status != null) status.Text = L.T("Processing");
        try { await Task.Run(action); RefreshMods(); if (status != null) status.Text = done; } catch (Exception e) { Error(e); } finally { busy = false; PageHost.IsEnabled = Navigation.IsEnabled = BottomNavigation.IsEnabled = true; }
    }
    private bool Confirm(string text) => W.MessageBox.Show(this, text, "Atlas", W.MessageBoxButton.YesNo, W.MessageBoxImage.Question, W.MessageBoxResult.No) == W.MessageBoxResult.Yes;
    private void Guard(Action action) { if (busy) return; try { action(); } catch (Exception e) { Error(e); } }
    private void Error(Exception e) { if (status != null) status.Text = L.T("OperationFailed"); W.MessageBox.Show(this, e.Message, "Atlas", W.MessageBoxButton.OK, W.MessageBoxImage.Warning); }
    private void Customize() { using var dialog = new AppearanceForm(preferences, data); if (dialog.ShowDialog(new NativeOwner(this)) != System.Windows.Forms.DialogResult.OK) return; preferences.Accent = dialog.Accent; preferences.CoverWidth = dialog.CoverWidth; preferences.Background = dialog.BackgroundFile; store.Save(preferences); PageHost.Background = null; ApplyAppearance(); if (game is {} current) OpenGame(current); else Home(); }
    private void ChangeCover(GameProfile profile,bool reset)
    {
        if(reset){SetGameCover(profile,null);return;}
        var picker=new Microsoft.Win32.OpenFileDialog{Title=L.T("PickCoverTitle",profile.Name),Filter=L.T("ImageFilter")};
        if(picker.ShowDialog(this)==true)SetGameCover(profile,picker.FileName);
    }
    private void SetGameCover(GameProfile profile,string? file)
    {
        if(file==null)preferences.Covers.Remove(profile.Id);else preferences.Covers[profile.Id]=Visuals.ImportImage(file,data);
        store.Save(preferences);Home();
    }
    private void MinimizeClick(object sender, W.RoutedEventArgs e) => WindowState = W.WindowState.Minimized;
    private void MaximizeClick(object sender, W.RoutedEventArgs e) => WindowState = WindowState == W.WindowState.Maximized ? W.WindowState.Normal : W.WindowState.Maximized;
    private void CloseClick(object sender, W.RoutedEventArgs e) => Close();
    private C.Grid SearchField(C.TextBox input,string hint,double width=double.PositiveInfinity)
    {
        var field=new C.Grid{Margin=new W.Thickness(0,8,0,18),MaxWidth=width,HorizontalAlignment=double.IsPositiveInfinity(width)?W.HorizontalAlignment.Stretch:W.HorizontalAlignment.Left};field.Children.Add(input);
        var placeholder=Text(hint,13,true);placeholder.Margin=new W.Thickness(13,0,13,0);placeholder.VerticalAlignment=W.VerticalAlignment.Center;placeholder.IsHitTestVisible=false;field.Children.Add(placeholder);
        void Update()=>placeholder.Visibility=input.Text.Length==0?W.Visibility.Visible:W.Visibility.Collapsed;
        input.TextChanged+=(_,_)=>Update();Update();return field;
    }
    private sealed class NativeOwner(W.Window window) : System.Windows.Forms.IWin32Window { public nint Handle => new System.Windows.Interop.WindowInteropHelper(window).Handle; }
}
internal static class PremiumLayout { internal static T AtColumn<T>(this T element, int column) where T : W.UIElement { C.Grid.SetColumn(element, column); return element; } }
