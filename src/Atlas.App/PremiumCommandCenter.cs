using System.Diagnostics;
using Atlas.Core;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;

namespace Atlas.App;

// Command Center is a complete composition; Cinematic retains the previous view.
public partial class PremiumWindow
{
    private C.WrapPanel? cinematicFilters;
    private void ToggleCinematicFilters()
    {
        if (cinematicFilters != null) cinematicFilters.Visibility = cinematicFilters.Visibility == W.Visibility.Visible ? W.Visibility.Collapsed : W.Visibility.Visible;
    }
    private double commandCardWidth = 340;
    private double commandArtworkHeight = 260;
    private string commandSection="library";
    private C.Button CommandNavigation(string key, string glyph, Action action, string name, bool active = false)
    {
        var content=new C.Grid();content.ColumnDefinitions.Add(new(){Width=new W.GridLength(34)});content.ColumnDefinitions.Add(new());
        content.Children.Add(new C.TextBlock{Text=glyph,FontFamily=new M.FontFamily("Segoe Fluent Icons"),FontSize=19,Width=22,HorizontalAlignment=W.HorizontalAlignment.Left,VerticalAlignment=W.VerticalAlignment.Center});
        var label=new C.TextBlock{Text=L.T(key),FontSize=14,FontWeight=active?W.FontWeights.SemiBold:W.FontWeights.Normal,VerticalAlignment=W.VerticalAlignment.Center,TextTrimming=W.TextTrimming.CharacterEllipsis};content.Children.Add(label.AtColumn(1));
        var button=Button("",action,name);button.Content=content;button.Style=(W.Style)FindResource("CommandNavigation");
        button.Background=active?new M.SolidColorBrush(BlendShell(((M.SolidColorBrush)SkinBrush("Base")).Color,((M.SolidColorBrush)SkinBrush("Accent")).Color,.10)):M.Brushes.Transparent;
        button.Foreground=SkinBrush(active?"Accent":"Text");button.BorderBrush=active?SkinBrush("Stroke"):M.Brushes.Transparent;
        button.BorderThickness=new W.Thickness(1);button.Padding=new W.Thickness(16,0,12,0);button.Margin=new W.Thickness(12,3,12,3);button.Height=50;button.ToolTip=L.T(key);
        return button;
    }
    private M.Brush CommandFade(bool vertical = false)
    {
        var color = ((M.SolidColorBrush)SkinBrush("Base")).Color;
        if(vertical)return new M.LinearGradientBrush(new M.GradientStopCollection { new(color,0), new(M.Color.FromArgb(235,color.R,color.G,color.B),.18), new(M.Color.FromArgb(35,color.R,color.G,color.B),.58), new(M.Color.FromArgb(0,color.R,color.G,color.B),1) },new W.Point(0,1),new W.Point(0,0));
        return new M.LinearGradientBrush(new M.GradientStopCollection {
            new(color,0), new(M.Color.FromArgb(230,color.R,color.G,color.B),.32), new(M.Color.FromArgb(20,color.R,color.G,color.B),1)
        }, vertical ? new W.Point(0,1) : new W.Point(0,0), vertical ? new W.Point(0,0) : new W.Point(1,0));
    }
    private C.Grid CommandArtwork(GameProfile profile, double height, string name)
    {
        var hero = new C.Grid { Name = name, Height = height, ClipToBounds = true, Background = SkinBrush("Surface") };
        hero.Children.Add(new C.Border { Background = new M.ImageBrush(GameHero(profile)) { Stretch = M.Stretch.UniformToFill, AlignmentX = M.AlignmentX.Right, AlignmentY = M.AlignmentY.Center }, Opacity = .85 });
        hero.Children.Add(new C.Border { Background = CommandFade() });
        return hero;
    }
    private C.TextBlock CommandTitle(string value, double size)
    {
        var title = Text(value, size); title.FontFamily = new M.FontFamily("Bahnschrift SemiBold, Segoe UI"); title.FontWeight = W.FontWeights.SemiBold; return title;
    }
    private void CommandHome()
    {
        if (busy) return;
        profilesPageVisible = nexusPageVisible = false; game = null; library = null; modList = null; inspectorPanel = null; shadePanel = null; status = null;
        commandSection="library"; NavigationBuild(); var page = Page(); page.Margin = new W.Thickness(0);
        page.RowDefinitions.Add(new() { Height = W.GridLength.Auto }); page.RowDefinitions.Add(new() { Height = W.GridLength.Auto }); page.RowDefinitions.Add(new());
        var feature = GameCatalog.Available.FirstOrDefault(p => preferences.GamePaths.ContainsKey(p.Id)) ?? GameCatalog.Available[0];
        var spotlight = CommandArtwork(feature, 278, "LibrarySpotlight");
        var intro = new C.StackPanel { Margin = new W.Thickness(32, 48, 28, 24), VerticalAlignment = W.VerticalAlignment.Center, MaxWidth = 620, HorizontalAlignment = W.HorizontalAlignment.Left };
        var eyebrow = Text("ATLAS  /  " + L.T("Library").ToUpperInvariant(), 11, true); intro.Children.Add(eyebrow);
        intro.Children.Add(CommandTitle(feature.Name.ToUpperInvariant(), 38)); intro.Children.Add(Text(feature.Subtitle, 15, true));
        var enter = Button("→  " + L.T("CollectionGameLibrary"), () => OpenGame(feature), "SpotlightOpenButton", true); enter.HorizontalAlignment = W.HorizontalAlignment.Left; enter.Padding = new W.Thickness(22, 12, 22, 12); intro.Children.Add(enter); spotlight.Children.Add(intro);
        page.Children.Add(new C.Border { Child = spotlight, BorderBrush = SkinBrush("Stroke"), BorderThickness = new W.Thickness(0, 0, 0, 1) });
        var heading = new C.Grid { Margin = new W.Thickness(28, 24, 28, 18) }; heading.ColumnDefinitions.Add(new()); heading.ColumnDefinitions.Add(new() { Width = new W.GridLength(300) });
        var label = new C.StackPanel(); label.Children.Add(CommandTitle(L.T("LibraryTitle"), 26)); label.Children.Add(Text(L.T("GamesCount", GameCatalog.Available.Count), 12, true)); heading.Children.Add(label);
        var filter = new C.TextBox { Name = "GameSearch", ToolTip = L.T("SearchGames") }; System.Windows.Automation.AutomationProperties.SetName(filter, L.T("SearchGames"));
        var field = SearchField(filter, L.T("SearchGames")); field.Margin = new W.Thickness(0); field.VerticalAlignment = W.VerticalAlignment.Center; heading.Children.Add(field.AtColumn(1)); C.Grid.SetRow(heading, 1); page.Children.Add(heading);
        var cards = new C.WrapPanel { Margin = new W.Thickness(28, 0, 12, 20) }; var scroll = new C.ScrollViewer { Content = cards, VerticalScrollBarVisibility = C.ScrollBarVisibility.Auto }; C.Grid.SetRow(scroll, 2); page.Children.Add(scroll);
        void Fill()
        {
            cards.Children.Clear();
            foreach (var profile in GameCatalog.Available.Where(x => x.Name.Contains(filter.Text, StringComparison.OrdinalIgnoreCase)))
            {
                const double width = 174.4; var content = new C.Grid { Width = width, Height = width * 1.45, Clip = new M.RectangleGeometry(new W.Rect(0, 0, width, width * 1.45), 5, 5) };
                content.Children.Add(new C.Image { Source = Cover(profile), Width = width, Height = width * 1.45, Stretch = M.Stretch.UniformToFill });
                content.Children.Add(new C.Border { Background = CommandFade(true) });
                var caption = new C.StackPanel { VerticalAlignment = W.VerticalAlignment.Bottom, Margin = new W.Thickness(13) }; var title = Text(profile.Name, 15); title.FontWeight = W.FontWeights.SemiBold; title.Margin = new W.Thickness(0, 0, 0, 5); caption.Children.Add(title); var sub = Text(profile.Subtitle, 11, true); sub.Margin = new W.Thickness(0); caption.Children.Add(sub); content.Children.Add(caption);
                var card = Button("", () => OpenGame(profile), "Game_" + profile.Id); card.Tag = profile.Id; card.Content = content; card.Background = M.Brushes.Transparent; card.BorderBrush = SkinBrush("Stroke"); card.BorderThickness = new W.Thickness(1); card.Padding = new W.Thickness(0); card.Margin = new W.Thickness(0, 0, 18, 18);
                var menu = new C.ContextMenu(); foreach (var reset in new[] { false, true }) { var item = new C.MenuItem { Header = L.T(reset ? "RestoreCover" : "ChangeCover") }; item.Click += (_, _) => Guard(() => ChangeCover(profile, reset)); menu.Items.Add(item); } card.ContextMenu = menu;
                var tile=new C.Grid{Width=width+2,Margin=new W.Thickness(0,0,18,18)};card.Margin=new W.Thickness(0);tile.Children.Add(card);
                var options=Button("···",()=>{},"CoverOptions_"+profile.Id);options.ToolTip=L.T("ChangeCover");options.Width=34;options.Height=30;options.FontSize=19;options.Padding=new W.Thickness(0);options.Margin=new W.Thickness(0,8,8,0);options.HorizontalAlignment=W.HorizontalAlignment.Right;options.VerticalAlignment=W.VerticalAlignment.Top;options.Background=SkinBrush("Surface");options.Click+=(_,e)=>{e.Handled=true;OpenThemedMenu(menu,options);};tile.Children.Add(options);cards.Children.Add(tile);
            }
            if (cards.Children.Count == 0) cards.Children.Add(Text(L.T("NoSearchResults"), 14, true));
        }
        filter.TextChanged += (_, _) => Fill(); Fill();
    }
    private void OpenCommandGame(GameProfile profile)
    {
        if (busy) return;
        profilesPageVisible = nexusPageVisible = false; game = profile; library = new(profile.Id == "doa5" ? data : Path.Combine(data, "games", profile.Id), profile); commandSection="library"; NavigationBuild();
        var page = Page(); page.Margin = new W.Thickness(0); page.RowDefinitions.Add(new() { Height = W.GridLength.Auto }); page.RowDefinitions.Add(new()); page.RowDefinitions.Add(new() { Height = W.GridLength.Auto });
        var hero = CommandArtwork(profile, IsCinematic ? 210 : 168, IsCinematic ? "CinematicHero" : "CommandHero");
        var heading = new C.StackPanel { Margin = new W.Thickness(28, 22, 170, 14), VerticalAlignment = W.VerticalAlignment.Center };
        var crumb = Text("←  " + L.T("CollectionGameLibrary"), 12, true); heading.Children.Add(crumb); heading.Children.Add(CommandTitle(profile.Name.ToUpperInvariant(), IsCinematic ? 39 : 31));
        var tools = new C.WrapPanel(); var play = Button("▶  " + L.T("Play"), () => { Process.Start(GameLauncher.Prepare(profile, GamePath)); if (status != null) status.Text = L.T("GameStarted", profile.Name); }, "PlayButton", true);
        play.IsEnabled = !CurrentProfiles().HasPending && !string.IsNullOrWhiteSpace(GamePath) && profile.Executables.Any(e => File.Exists(Path.Combine(GamePath, e))); tools.Children.Add(play);
        var canConfigure = !CurrentProfiles().HasPending;
        var import = Button("＋  " + L.T("ImportZip"), Import, "ImportButton");
        import.IsEnabled = canConfigure;
        tools.Children.Add(import);

        tools.Children.Add(CoverDownloadControl());
        tools.Children.Add(Button("ReShade", ShowSkinReShade, "GameReShadeButton"));

        var gameFolder = Button(L.T("GameFolder"), () => ChooseGame(false), "GameFolderButton");
        gameFolder.IsEnabled = canConfigure;
        tools.Children.Add(gameFolder);
        var selectExe = Button(L.T("SelectExe"), () => ChooseGame(true), "SelectExeButton");
        selectExe.IsEnabled = canConfigure;
        tools.Children.Add(selectExe);

        var openFolder = Button(L.T("OpenFolder"), () =>
        {
            var path = ModLibrary.ValidateGame(GamePath, profile);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }, "OpenGameFolderButton");
        openFolder.IsEnabled = Directory.Exists(GamePath);
        tools.Children.Add(openFolder);
        tools.Children.Add(Button(L.T("Recover"), RecoverProfile, "RecoverGameButton"));
        heading.Children.Add(tools);
        profileStatus = Text("", 11, true); profileStatus.Margin = new W.Thickness(0); heading.Children.Add(profileStatus); RefreshProfileStatus(); hero.Children.Add(heading);
        page.Children.Add(new C.Border { Child = hero, BorderBrush = SkinBrush("Stroke"), BorderThickness = new W.Thickness(0, 0, 0, 1) });
        var body = new C.Grid(); body.ColumnDefinitions.Add(new()); body.ColumnDefinitions.Add(new() { Width = new W.GridLength(0) }); C.Grid.SetRow(body, 1); page.Children.Add(body);
        var center = new C.Grid { Margin = new W.Thickness(24, 18, 20, 0) }; center.RowDefinitions.Add(new() { Height = W.GridLength.Auto }); center.RowDefinitions.Add(new()); body.Children.Add(center);
        var filters = new C.Grid(); filters.ColumnDefinitions.Add(new()); filters.ColumnDefinitions.Add(new() { Width = W.GridLength.Auto }); filters.ColumnDefinitions.Add(new() { Width = W.GridLength.Auto }); filters.RowDefinitions.Add(new() { Height = W.GridLength.Auto }); filters.RowDefinitions.Add(new() { Height = W.GridLength.Auto });
        search = new C.TextBox { Name = "ModSearch", ToolTip = L.T("SearchMods") }; search.TextChanged += (_, _) => { modList?.SelectedItems.Clear(); RefreshMods(); }; var searchField = SearchField(search, L.T("SearchMods")); searchField.Margin = new W.Thickness(0, 0, 12, 10); filters.Children.Add(searchField);
        var reset=Button(L.T(IsCinematic ? "CinematicFilters" : "ModResetFilters"),()=>{if(IsCinematic)ToggleCinematicFilters();else ResetModFilters();},"ResetModFiltersButton");reset.Height=44;reset.Margin=new W.Thickness(0,0,12,10);filters.Children.Add(reset.AtColumn(1));
        var views = new C.StackPanel { Orientation = C.Orientation.Horizontal }; modGridButton = Button("⊞", () => SetModView("grid"), "ModGridButton"); modListButton = Button("☷", () => SetModView("list"), "ModListButton"); modCompactButton = Button("≡", () => SetModView("compact"), "ModCompactButton"); foreach (var b in new[] { modGridButton, modListButton, modCompactButton }) { b.Width = 44; b.Height = 44; b.Padding = new W.Thickness(0); b.FontSize = 23; b.Margin = new W.Thickness(0, 0, 0, 10); } modGridButton.ToolTip = L.T("ModGrid"); modListButton.ToolTip = L.T("ModListView"); views.Children.Add(modGridButton); views.Children.Add(modListButton); modCompactButton.ToolTip = L.T("ModCompactView"); System.Windows.Automation.AutomationProperties.SetName(modCompactButton,L.T("ModCompactView")); views.Children.Add(modCompactButton); filters.Children.Add(views.AtColumn(2));
        var secondary=new C.WrapPanel();
        cinematicFilters = IsCinematic ? secondary : null;
        if (IsCinematic) secondary.Visibility = W.Visibility.Collapsed;
        categoryDropdown=new C.ComboBox{Name="ModCategoryFilter",Width=180,Height=40};categoryDropdown.SelectionChanged+=(_,_)=>{if(refreshingMods)return;selectedCategory=(categoryDropdown.SelectedItem as C.ComboBoxItem)?.Tag as string;modList?.SelectedItems.Clear();RefreshMods();};
        secondary.Children.Add(ModFilterGroup(L.T("CategoryColumn"),categoryDropdown));
        stateFilter=LibraryCombo("ModStateFilter",new[]{("all","AllStates"),("enabled","Enabled"),("disabled","Disabled"),("other","OtherFolder")},selectedState,v=>{selectedState=v;modList?.SelectedItems.Clear();RefreshMods();});
        secondary.Children.Add(ModFilterGroup(L.T("ModStateLabel"),stateFilter));
        var characterHost=new C.StackPanel();AddCharacterFilter(characterHost);if(characterFilter!=null){characterHost.Children.Remove(characterFilter);secondary.Children.Add(ModFilterGroup(L.T("CharacterLabel"),characterFilter));}
        sortSelector=LibraryCombo("ModSortSelector",new[]{("name","SortName"),("category","SortCategory"),("state","SortState"),("date","SortImported")},selectedSort,v=>{selectedSort=v;RefreshMods();});
        secondary.Children.Add(ModFilterGroup(L.T("ModSortLabel"),sortSelector));
        modResultsCaption=SecondaryText("",11,true);modResultsCaption.Name="ModResultsCaption";modResultsCaption.Margin=new W.Thickness(0,0,0,10);
        if (IsCinematic) secondary.Children.Add(Button(L.T("ModResetFilters"), ResetModFilters));
        categoryList = new C.ListBox { Name = "CategoryList", Width = 0, Height = 0 }; categoryList.SelectionChanged += (_, _) => { if (refreshingMods) return; selectedCategory = (categoryList.SelectedItem as C.ListBoxItem)?.Tag as string; modList?.SelectedItems.Clear(); RefreshMods(); }; secondary.Children.Add(categoryList); C.Grid.SetRow(secondary, 1); C.Grid.SetColumnSpan(secondary, 3); filters.Children.Add(secondary);var filterArea=new C.StackPanel();filterArea.Children.Add(filters);filterArea.Children.Add(modResultsCaption);center.Children.Add(filterArea);
        modList = new C.ListBox { Name = "ModList", SelectionMode = C.SelectionMode.Extended }; modList.SelectionChanged += (_, _) => { if (!refreshingMods) ShowDetails(); }; C.Grid.SetRow(modList, 1); center.Children.Add(modList);
        modList.SizeChanged += (_, _) => { if (modList == null) return; var available = modList.ActualWidth - 10; var columns = IsCinematic ? Math.Max(1,(int)(available/310)) : available >= 590 ? 2 : 1; var width = Math.Min(IsCinematic ? 470 : 380,Math.Max(220, (available - columns * 14) / columns)); var artworkHeight=Math.Max(110,Math.Min(width*(IsCinematic?.56:.8),modList.ActualHeight-150)); if (Math.Abs(width - commandCardWidth) > 2||Math.Abs(artworkHeight-commandArtworkHeight)>2) { commandCardWidth = width;commandArtworkHeight=artworkHeight; RefreshMods(); } };
        var inspector = new C.StackPanel { Margin = new W.Thickness(0, 0, 12, 0) };
        var inspectorScroll = new C.ScrollViewer
        {
            Content = inspector,
            VerticalScrollBarVisibility = C.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = C.ScrollBarVisibility.Disabled
        };
        var drawer = new C.Border
        {
            Background = SkinBrush("Surface"),
            BorderBrush = SkinBrush("Stroke"),
            BorderThickness = new W.Thickness(1, 0, 0, 0),
            Padding = new W.Thickness(16),
            Child = inspectorScroll,
            Visibility = W.Visibility.Collapsed
        };
        C.Grid.SetColumn(drawer, 1);
        body.Children.Add(drawer);
        var close = Button("×", () =>
        {
            drawer.Visibility = W.Visibility.Collapsed;
            body.ColumnDefinitions[1].Width = new W.GridLength(0);
        }, "CloseModInspectorButton");
        close.HorizontalAlignment = W.HorizontalAlignment.Right;
        close.ToolTip = L.T("WindowClose");
        System.Windows.Automation.AutomationProperties.SetName(close, L.T("WindowClose"));
        inspector.Children.Add(close);
        inspectorPanel = new C.StackPanel { Name = "ModInspector" };
        inspector.Children.Add(inspectorPanel);
        shadePanel = null;

        var libraryList = modList;
        libraryList.PreviewMouseLeftButtonDown += (_, args) =>
        {
            if (busy || args.ClickCount != 2 || args.OriginalSource is not W.DependencyObject source) return;
            if (C.ItemsControl.ContainerFromElement(libraryList, source) is not C.ListBoxItem { Tag: ModPackage } row) return;
            for (var element = source; element is M.Visual && element != row; element = M.VisualTreeHelper.GetParent(element))
            {
                if (element is C.Primitives.ButtonBase button && !button.Name.StartsWith("OpenMod_", StringComparison.Ordinal))
                    return;
            }

            args.Handled = true;
            libraryList.SelectedItem = row;
            body.ColumnDefinitions[1].Width = new W.GridLength(360);
            drawer.Visibility = W.Visibility.Visible;
            inspectorScroll.ScrollToTop();
            ShowDetails();
        };
        var footer = new C.Grid { Margin = new W.Thickness(24, 12, 24, 12) }; footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = W.GridLength.Auto });
        var summary = new C.StackPanel { VerticalAlignment = W.VerticalAlignment.Center }; selectionCaption = Text("", 13); selectionCaption.FontWeight = W.FontWeights.SemiBold; selectionCaption.Margin = new W.Thickness(0, 0, 0, 4); var selectionRow=new C.StackPanel{Orientation=C.Orientation.Horizontal};
        var selectionMenu=Button("☑",()=>{var menu=new C.ContextMenu();var select=new C.MenuItem{Header=L.T("ModSelectAll")};select.Click+=(_,_)=>{modList.SelectAll();ShowDetails();};var clear=new C.MenuItem{Header=L.T("ModClearSelection")};clear.Click+=(_,_)=>modList.SelectedItems.Clear();menu.Items.Add(select);menu.Items.Add(clear);menu.IsOpen=true;},"CommandSelectionMenu");selectionMenu.ToolTip=L.T("ModSelectAll");selectionMenu.Padding=new W.Thickness(0,0,8,0);selectionMenu.Margin=new W.Thickness(0);selectionMenu.Background=M.Brushes.Transparent;selectionRow.Children.Add(selectionMenu);selectionRow.Children.Add(selectionCaption);summary.Children.Add(selectionRow); status = Text("", 10, true); status.Margin = new W.Thickness(0); summary.Children.Add(status); footer.Children.Add(summary);
        var actions = new C.WrapPanel { HorizontalAlignment = W.HorizontalAlignment.Right }; actions.Children.Add(Button("✓  " + L.T("Enable"), () => ReviewBatch(ModBatchAction.Enable), "BatchEnableButton", !IsCinematic)); actions.Children.Add(Button("⊘  " + L.T("Disable"), () => ReviewBatch(ModBatchAction.Disable), "BatchDisableButton")); actions.Children.Add(Button(L.T("ModPage"), () => ShowModPage(Selected ?? throw new InvalidOperationException(L.T("SelectMod"))), "ModPageButton")); actions.Children.Add(Button(L.T("Delete"), () => ReviewBatch(ModBatchAction.Delete), "BatchDeleteButton")); actions.IsEnabled = !CurrentProfiles().HasPending; footer.Children.Add(actions.AtColumn(1));
        details = Text("", 11, true); modLegacyDetailsHost = null; modDropHint = null;
        var footerBorder = new C.Border { Name = "CommandBatchBar", Background = SkinBrush("Surface"), BorderBrush = SkinBrush("Stroke"), BorderThickness = new W.Thickness(0, 1, 0, 0), Child = footer }; C.Grid.SetRow(footerBorder, 2); page.Children.Add(footerBorder); RefreshMods(); if(modList.Items.Count>0&&modList.Items[0] is C.ListBoxItem first&&first.Tag is ModPackage)first.IsSelected=true;LoadEmbeddedArtwork();
    }
}
