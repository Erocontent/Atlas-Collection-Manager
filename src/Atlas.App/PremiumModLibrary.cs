using Atlas.Core;
using System.Diagnostics;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;
using I = System.Windows.Media.Imaging;

namespace Atlas.App;

public partial class PremiumWindow
{
    private C.ListBox? categoryList;
    private C.ComboBox? stateFilter,sortSelector;
    private C.TextBlock? selectionCaption;
    private C.TextBlock? modResultsCaption;
    private IReadOnlyDictionary<string,ModStatus>? modVisualStatusSnapshot;
    private string? selectedCategory;
    private string selectedState="all",selectedSort="name";
    private bool refreshingMods;
    private C.Button? modGridButton,modListButton,modCompactButton;
    private C.ScrollViewer? modLegacyDetailsHost;
    private C.TextBlock? modDropHint;
    private int modSelectionAnchor;
    private void SetModView(string mode){if(mode is not ("grid" or "list" or "compact"))return;preferences.ModLibraryView=mode;store.Save(preferences);RefreshMods();}
    private ModDetailsStore CurrentModDetails()=>new(LibraryRoot(game!),game!);
    private ModPackage[] SelectedMods()=>modList?.SelectedItems.Cast<C.ListBoxItem>().Select(i=>i.Tag).OfType<ModPackage>().ToArray()??[];
    private C.ComboBox LibraryCombo(string name,(string Id,string Key)[] choices,string selected,Action<string> changed)
    {
        var combo=new C.ComboBox{Name=name,Width=145,Margin=new W.Thickness(0,0,8,12)};foreach(var choice in choices)combo.Items.Add(new C.ComboBoxItem{Tag=choice.Id,Content=L.T(choice.Key)});combo.SelectedItem=combo.Items.Cast<C.ComboBoxItem>().First(i=>Equals(i.Tag,selected));combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is C.ComboBoxItem item)changed((string)item.Tag);};return combo;
    }
    private I.BitmapImage? ModImage(ModDetailsStore metadata,string? relative,int width)
    {
        try{var path=metadata.ImagePath(relative);if(path==null)return null;using var stream=File.OpenRead(path);var image=new I.BitmapImage();image.BeginInit();image.CacheOption=I.BitmapCacheOption.OnLoad;image.DecodePixelWidth=width;image.StreamSource=stream;image.EndInit();image.Freeze();return image;}
        catch(Exception e)when(e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException){return null;}
    }
    private void RefreshOrganizedMods()
    {
        if(library==null||modList==null||refreshingMods)return;refreshingMods=true;
        try
        {

            var selection=SelectedMods().Select(m=>m.Id).ToHashSet();var packages=library.List();var active=library.GetActive();var metadata=CurrentModDetails();
            IndexLibraryConflicts(packages);
            libraryNexusIds.Clear();
            IReadOnlyDictionary<string,ModStatus>? visualStatus=null;
            try
            {
                var collections = CurrentCollections().List();
                IndexCoverSources(collections);
                visualStatus=ModVisualStatus.Evaluate(packages,active,collections,GamePath);
            }catch(Exception error)when(error is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException){ }
            var records = packages.ToDictionary(mod => mod.Id, mod => WithNexusVersion(metadata.Get(mod)));
            modVisualStatusSnapshot=visualStatus;
            if (compactHeader != null) compactHeader.Visibility = preferences.ModLibraryView != "grid" ? W.Visibility.Visible : W.Visibility.Collapsed;
            var gridMode=preferences.ModLibraryView=="grid";if(modLegacyDetailsHost!=null)modLegacyDetailsHost.Visibility=gridMode?W.Visibility.Collapsed:W.Visibility.Visible;if(modDropHint!=null)modDropHint.Visibility=gridMode?W.Visibility.Collapsed:W.Visibility.Visible;var panel=new W.FrameworkElementFactory(gridMode?typeof(C.WrapPanel):typeof(C.VirtualizingStackPanel));modList.ItemsPanel=new C.ItemsPanelTemplate(panel);C.ScrollViewer.SetCanContentScroll(modList,!gridMode);
            C.ScrollViewer.SetVerticalScrollBarVisibility(modList, gridMode ? C.ScrollBarVisibility.Auto : C.ScrollBarVisibility.Visible);
            if(modGridButton!=null)modGridButton.Style=(W.Style)FindResource(gridMode?"Primary":(object)typeof(C.Button));if(modListButton!=null)modListButton.Style=(W.Style)FindResource(preferences.ModLibraryView=="list"?"Primary":(object)typeof(C.Button));
            if(modCompactButton!=null)modCompactButton.Style=(W.Style)FindResource(preferences.ModLibraryView=="compact"?"Primary":(object)typeof(C.Button));
            string Category(ModPackage m)=>string.IsNullOrWhiteSpace(records[m.Id].Category)?L.T("Uncategorized"):records[m.Id].Category;
            string State(ModPackage m){var installed=active.FirstOrDefault(a=>a.Id==m.Id);return installed==null?"disabled":string.Equals(installed.Game,GamePath,StringComparison.OrdinalIgnoreCase)?"enabled":"other";}
            if(categoryList!=null)
            {
                var categories=packages.GroupBy(Category,StringComparer.OrdinalIgnoreCase).OrderBy(g=>g.Key,StringComparer.CurrentCultureIgnoreCase).ToArray();if(selectedCategory!=null&&!categories.Any(g=>g.Key.Equals(selectedCategory,StringComparison.OrdinalIgnoreCase)))selectedCategory=null;
                categoryList.Items.Clear();categoryList.Items.Add(new C.ListBoxItem{Content=L.T("ModCategoryCount",L.T("AllCategories"),packages.Count),Tag=null});
                foreach(var group in categories)categoryList.Items.Add(new C.ListBoxItem{Content=L.T("ModCategoryCount",group.Key,group.Count()),Tag=group.Key});
                categoryList.SelectedItem=categoryList.Items.Cast<C.ListBoxItem>().First(i=>i.Tag==null&&selectedCategory==null||(i.Tag as string)?.Equals(selectedCategory,StringComparison.OrdinalIgnoreCase)==true);if(categoryDropdown!=null){categoryDropdown.Items.Clear();foreach(var option in categoryList.Items.Cast<C.ListBoxItem>())categoryDropdown.Items.Add(new C.ComboBoxItem{Content=option.Content,Tag=option.Tag});categoryDropdown.SelectedItem=categoryDropdown.Items.Cast<C.ComboBoxItem>().First(i=>i.Tag==null&&selectedCategory==null||(i.Tag as string)?.Equals(selectedCategory,StringComparison.OrdinalIgnoreCase)==true);}
            }
            RefreshCharacterFilter(packages);
            var query=packages.Where(m=>MatchesCharacter(m)&&(selectedCategory==null||Category(m).Equals(selectedCategory,StringComparison.OrdinalIgnoreCase))&&(selectedState=="all"||State(m)==selectedState)&&$"{m.Name} {Category(m)} {records[m.Id].Description} {records[m.Id].Version}".Contains(search?.Text??"",StringComparison.OrdinalIgnoreCase));
            query=selectedSort switch{"category"=>query.OrderBy(Category,StringComparer.CurrentCultureIgnoreCase).ThenBy(m=>m.Name,StringComparer.CurrentCultureIgnoreCase),"state"=>query.OrderBy(State).ThenBy(m=>m.Name,StringComparer.CurrentCultureIgnoreCase),"date"=>query.OrderByDescending(m=>records[m.Id].ImportedAt).ThenBy(m=>m.Name,StringComparer.CurrentCultureIgnoreCase),_=>query.OrderBy(m=>m.Name,StringComparer.CurrentCultureIgnoreCase)};
            modList.Items.Clear();foreach(var mod in query)
            {
                if(gridMode){var card=ModCard(mod,records[mod.Id],metadata,Category(mod),State(mod),visualStatus?.GetValueOrDefault(mod.Id));modList.Items.Add(card);if(selection.Contains(mod.Id))card.IsSelected=true;continue;}
                if (preferences.ModLibraryView == "list")
                {
                    var info = records[mod.Id];
                    var row = new C.Grid();
                    row.ColumnDefinitions.Add(new() { Width = W.GridLength.Auto });
                    row.ColumnDefinitions.Add(new());
                    var source = LibraryCover(metadata, info, 144);
                    row.Children.Add(new C.Border
                    {
                        Width = 72, Height = 64, CornerRadius = new W.CornerRadius(8),
                        VerticalAlignment = W.VerticalAlignment.Center,
                        Margin = new W.Thickness(0, 0, 14, 0),
                        Background = source == null ? SkinBrush("Surface") : new M.ImageBrush(source) { Stretch = M.Stretch.UniformToFill }
                    });
                    var text = new C.StackPanel { VerticalAlignment = W.VerticalAlignment.Center };
                    var title = SecondaryText(mod.Name, 17, false, true);
                    title.Margin = new W.Thickness(0, 0, 0, 6);
                    text.Children.Add(title);
                    var badges = new C.WrapPanel();
                    C.Border Badge(string value, bool secondary = false, bool active = false)
                    {
                        var label = SecondaryText(value, 12, secondary);
                        var badge = CollectionBadge(label);
                        badge.Background = SkinBrush(active ? "Accent" : "Base");
                        badge.BorderBrush = SkinBrush(secondary ? "Muted" : "Stroke");
                        badge.BorderThickness = new W.Thickness(1);
                        if (active) label.Foreground = SkinBrush("AccentText");
                        return badge;
                    }
                    badges.Children.Add(Badge(Category(mod), secondary: true));

                    var state = State(mod);
                    badges.Children.Add(Badge(L.T(state switch
                    {
                        "enabled" => "Enabled", "other" => "OtherFolder", _ => "Disabled"
                    }), active: state == "enabled"));
                    text.Children.Add(badges);

                    row.Children.Add(text.AtColumn(1));
                    AddModIndicators(text,visualStatus?.GetValueOrDefault(mod.Id));
                    var item = new C.ListBoxItem { Content = CompactModRow(mod, info, row), Tag = mod, Style = (W.Style)FindResource("CompactModItem"), Padding = new W.Thickness(10, 6, 10, 6) };modList.Items.Add(item);if(selection.Contains(mod.Id))item.IsSelected=true;
                    continue;
                }
                var compactRow = CompactModRow(mod, records[mod.Id]);
                var compactItem = new C.ListBoxItem
                {
                    Content = compactRow,
                    Tag = mod,
                    ToolTip = mod.Name,
                    Style = (W.Style)FindResource("CompactModItem")
                };
                modList.Items.Add(compactItem);if(selection.Contains(mod.Id))compactItem.IsSelected=true;
            }
                        if (modResultsCaption != null)
            {
                modResultsCaption.Text = L.T("ModResults", modList.Items.Count, packages.Count);
                if (IsCinematic)
                {
                    var activeFilters = new List<string>();
                    if (selectedCategory != null) activeFilters.Add(selectedCategory);
                    if (selectedState != "all" && stateFilter?.SelectedItem is C.ComboBoxItem stateItem) activeFilters.Add(stateItem.Content.ToString()!);
                    if (characterFilter?.SelectedItem is C.ComboBoxItem { Tag: not null } characterItem) activeFilters.Add(characterItem.Content.ToString()!);
                    if (activeFilters.Count > 0) modResultsCaption.Text += "  ·  " + string.Join("  ·  ", activeFilters);
                }
            }
            if(modList.Items.Count==0)modList.Items.Add(new C.ListBoxItem{Content=Text(L.T(packages.Count==0?"NoMods":"NoSearchResults"),14,true),IsEnabled=false});
            if(status!=null)status.Text=L.T("ActiveModCount",active.Count(a=>string.Equals(a.Game,GamePath,StringComparison.OrdinalIgnoreCase)))+(visualStatus==null?" · "+L.T("ModStatusUnavailable"):"");
        }
        finally{refreshingMods=false;}
        ShowDetails();RefreshShade();RefreshProfileStatus();LoadEmbeddedArtwork();
    }
    private void AddModIndicators(C.Panel panel,ModStatus? status)
    {
        if(status==null){var unavailable=Text("● "+L.T("ModStatusUnavailable"),11,true);unavailable.Margin=new W.Thickness(0,2,0,2);panel.Children.Add(unavailable);return;}
        if(status.HasMissingDependencies){var badge=Text("● "+L.T("ModMissingDependencyBadge"),11);badge.Foreground=Brush("#F19A9A");badge.Margin=new W.Thickness(0,2,0,2);badge.ToolTip=string.Join("\n",status.MissingDependencies);panel.Children.Add(badge);}
    }
    private C.ListBoxItem ModCard(ModPackage mod,ModDetails info,ModDetailsStore metadata,string category,string state,ModStatus? status)
    {
        return CommandModCard(mod,info,metadata,category,state,status);
    }
    private void SelectCard(C.ListBoxItem item,W.Input.ModifierKeys keys)
    {
        if(modList==null)return;var index=modList.Items.IndexOf(item);if((keys&W.Input.ModifierKeys.Shift)!=0){var anchor=modList.SelectedItems.Count==0?index:Math.Clamp(modSelectionAnchor,0,Math.Max(0,modList.Items.Count-1));if((keys&W.Input.ModifierKeys.Control)==0)modList.SelectedItems.Clear();for(var i=Math.Min(index,anchor);i<=Math.Max(index,anchor);i++)if(modList.Items[i] is C.ListBoxItem row&&row.Tag is ModPackage)row.IsSelected=true;}else{item.IsSelected=!item.IsSelected;modSelectionAnchor=index;}ShowDetails();
    }
    private void ShowModPage(ModPackage package)
    {
        if(busy)return;var mod=library!.List().Single(m=>m.Id==package.Id);var metadata=CurrentModDetails();var info=WithNexusVersion(metadata.Get(mod));var profile=game!;
        profilesPageVisible=nexusPageVisible=false;modList=null;shadePanel=null;status=null;NavigationBuild();var page=Page();var root=new C.StackPanel();page.Children.Add(new C.ScrollViewer{Content=root,Padding=new W.Thickness(0,0,12,0),VerticalScrollBarVisibility=C.ScrollBarVisibility.Auto});
        root.Children.Add(Button(L.T("ModBack"),()=>OpenGame(profile),"ModBackButton"));root.Children.Add(SecondaryHeader(mod.Name,L.T("ModDescription"),profile.Name,"\uE8F1"));root.Children.Add(Text(info.Category+" · "+L.T("ModVersion")+": "+(info.Version.Length==0?L.T("ModUnknown"):info.Version),14,true));root.Children.Add(Text(L.T("ModImported",info.ImportedAt.ToLocalTime().ToString("g")),12,true));
        var actions=new C.WrapPanel();actions.Children.Add(Button(L.T("ModPreview"),()=>ShowModGallery(mod),"ModPageGalleryButton",true));actions.Children.Add(Button(L.T("ModMetadata"),()=>EditModDetails(mod),"EditModDetailsButton"));actions.Children.Add(Button(L.T("ModCover"),()=>AddModImage(mod,true),"AddModCoverButton"));actions.Children.Add(Button(L.T("ModPreviewImage"),()=>AddModImage(mod,false),"AddModPreviewButton"));actions.IsEnabled=!CurrentProfiles().HasPending;root.Children.Add(actions);
        var hero=new C.Grid();hero.ColumnDefinitions.Add(new(){Width=new W.GridLength(340)});hero.ColumnDefinitions.Add(new());var cover=LibraryCover(metadata,info,600);hero.Children.Add(new C.Border{Width=310,Height=350,CornerRadius=new W.CornerRadius(10),Background=Brush("#1C1F25"),Child=cover==null?Text(L.T("ModGalleryEmpty"),13,true):new C.Image{Source=cover,Stretch=M.Stretch.UniformToFill},Margin=new W.Thickness(0,8,25,20)});
        var description=new C.StackPanel{Margin=new W.Thickness(0,8,0,16)};description.Children.Add(Text(L.T("ModDescription"),19));description.Children.Add(Text(info.Description.Length==0?L.T("ModUnknown"):info.Description,14));description.Children.Add(Text(L.T("ModDependencies"),19));description.Children.Add(Text(info.Dependencies.Length==0?L.T("ModNoDependencies"):string.Join("\n",info.Dependencies),13,true));
        if(info.SourceUrl!=null){description.Children.Add(Text(info.SourceUrl,12,true));description.Children.Add(Button(L.T("ModSourceOpen"),()=>Process.Start(new ProcessStartInfo(info.SourceUrl){UseShellExecute=true}),"ModSourceButton"));}hero.Children.Add(description.AtColumn(1));root.Children.Add(hero);
        root.Children.Add(Text(L.T("ModGallery"),21));var gallery=new C.WrapPanel{Name="ModGallery"};
        foreach(var relative in info.Images)
        {
            var image=ModImage(metadata,relative,420);if(image==null)continue;var card=new C.StackPanel{Margin=new W.Thickness(0,0,18,18),Width=220};var thumbnail=Button("",()=>ShowModGallery(mod,relative));thumbnail.Content=new C.Image{Source=image,Height=145,Stretch=M.Stretch.Uniform};card.Children.Add(thumbnail);var remove=Button(L.T("ModRemoveImage"),()=>{EnsureProfileReady();if(Confirm(L.T("ModImageRemoveConfirm"))){metadata.RemoveImage(mod.Id,relative);ShowModPage(mod);}});remove.IsEnabled=!CurrentProfiles().HasPending;card.Children.Add(remove);gallery.Children.Add(card);
        }
        if(gallery.Children.Count==0)root.Children.Add(Text(L.T("ModGalleryEmpty"),13,true));root.Children.Add(gallery);root.Children.Add(Text(L.T("ModFiles"),21));root.Children.Add(Text(L.T("SelectedDetails",mod.Name,mod.Destination.Length==0?L.T("RootGame"):mod.Destination)+string.Join("\n",mod.Files.Take(2000)),12,true));
    }
    private async void EditModDetails(ModPackage mod)
    {
        try
        {
            EnsureProfileReady();var metadata=CurrentModDetails();var info=metadata.Get(mod);var dialog=ProfileDialog(L.T("ModMetadata"),700,650);var panel=new C.StackPanel{Margin=new W.Thickness(26)};panel.Children.Add(Text(L.T("ModMetadataHint"),13,true));
            C.TextBox Field(string key,string value,int max,bool multiline=false){panel.Children.Add(Text(L.T(key),14));var box=new C.TextBox{Text=value,MaxLength=max,AcceptsReturn=multiline,TextWrapping=multiline?W.TextWrapping.Wrap:W.TextWrapping.NoWrap,Height=multiline?145:double.NaN,VerticalScrollBarVisibility=multiline?C.ScrollBarVisibility.Auto:C.ScrollBarVisibility.Hidden,Margin=new W.Thickness(0,0,0,14)};panel.Children.Add(box);return box;}
            var category=Field("UniqueCategory",info.Category,100);category.Name="ModCategoryInput";var version=Field("ModVersion",info.Version,80);version.Name="ModVersionInput";var source=Field("ModSource",info.SourceUrl??"",2048);source.Name="ModSourceInput";var description=Field("ModDescription",info.Description,16000,true);description.Name="ModDescriptionInput";
            var actions=new C.WrapPanel();actions.Children.Add(Button(L.T("Cancel"),()=>dialog.DialogResult=false));actions.Children.Add(Button(L.T("ModDetailsSave"),()=>dialog.DialogResult=true,"SaveModDetailsButton",true));panel.Children.Add(actions);dialog.Content=new C.ScrollViewer{Content=panel,VerticalScrollBarVisibility=C.ScrollBarVisibility.Auto};if(dialog.ShowDialog()!=true)return;
            busy=true;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=false;var c=category.Text;var d=description.Text;var v=version.Text;var s=source.Text;await Task.Run(()=>metadata.Save(mod.Id,c,d,v,s));busy=false;ShowModPage(mod);
        }
        catch(Exception error){Error(error);}finally{busy=false;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=true;}
    }
    private async void AddModImage(ModPackage mod,bool cover,bool returnToLibrary=false, Action? afterSave=null)
    {
        if(busy)return;try
        {
            EnsureProfileReady();var picker=new Microsoft.Win32.OpenFileDialog{Title=L.T(cover?"ModCover":"ModPreviewImage"),Filter=L.T("ImageFilter")};if(picker.ShowDialog(this)!=true)return;var profile=game!;var root=LibraryRoot(profile);var metadata=CurrentModDetails();var file=picker.FileName;
            busy=true;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=false;await Task.Run(()=>metadata.AddImage(mod.Id,Visuals.NormalizeModImage(file),cover));busy=false;if(afterSave!=null)afterSave();else if(returnToLibrary){RefreshMods();SelectLibraryMod(mod.Id);}else ShowModPage(mod);
        }catch(Exception error){Error(error);}finally{busy=false;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=true;}
    }
    private C.Button? batchEnable, batchDisable;

    private void RefreshBatchToggleButtons()
    {
        if (batchEnable == null || batchDisable == null || library == null) return;
        var selected = SelectedMods();
        var active = selected.Length == 1 ? library.GetActive().FirstOrDefault(item => item.Id == selected[0].Id) : null;
        batchEnable.Visibility = selected.Length == 1 && active != null ? W.Visibility.Collapsed : W.Visibility.Visible;
        batchDisable.Visibility = selected.Length == 1 && (active == null || !string.Equals(active.Game, GamePath, StringComparison.OrdinalIgnoreCase))
            ? W.Visibility.Collapsed : W.Visibility.Visible;
    }

    private void ReviewBatch(ModBatchAction action)=>ReviewBatchPackages(action,SelectedMods());
    private async void ReviewBatchPackages(ModBatchAction action,ModPackage[] selected,string? collectionId=null)
    {
        if(busy)return;try
        {
            EnsureProfileReady();if(selected.Length==0)throw new InvalidOperationException(L.T("ModNoSelection"));var profile=game!;var lib=library!;var folder=GamePath;var collections=CurrentCollections();var batches=new ModBatchService(LibraryRoot(profile),profile);
            busy=true;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=false;ModBatchPlan plan;Dictionary<string,ModConflictReport> reports;try{plan=await Task.Run(()=>batches.Preview(action,selected.Select(m=>m.Id).ToArray(),folder,lib,collections));reports=await Task.Run(()=>BatchConflictReports(plan,lib,folder));}finally{busy=false;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=true;}
            var directToggle = selected.Length == 1 && collectionId == null && action != ModBatchAction.Delete && plan.Problems.Length == 0;
            if ((!directToggle && !PreviewBatch(plan,lib,reports)) || busy) return;busy=true;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=false;var result=await Task.Run(()=>batches.Apply(plan,folder,lib,collections));busy=false;if(collectionId!=null)ShowCollections(collectionId);else RefreshMods();if(status!=null)status.Text=result.Error==null?L.T("ModBatchDone",result.Completed.Length):L.T("ModBatchPartial",result.Completed.Length,result.Remaining.Length);if(result.Error!=null){var summary=L.T("ModBatchPartial",result.Completed.Length,result.Remaining.Length);var names=selected.ToDictionary(m=>m.Id,m=>m.Name);var outcomes=result.Completed.Select(id=>"✓ "+names.GetValueOrDefault(id,id)).Concat(result.Remaining.Select(id=>"… "+names.GetValueOrDefault(id,id)));Error(new InvalidOperationException(summary+"\n\n"+result.Error+"\n\n"+string.Join("\n",outcomes)));if(status!=null)status.Text=summary;}
        }
        catch(Exception error){Error(error);}finally{busy=false;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=true;}
    }
    private bool PreviewBatch(ModBatchPlan plan,ModLibrary lib,IReadOnlyDictionary<string,ModConflictReport>? reports=null)
    {
        var dialog=ProfileDialog(L.T("ModBatchPreview"),740,660);var dock=new C.DockPanel{Margin=new W.Thickness(26)};var actions=new C.WrapPanel{HorizontalAlignment=W.HorizontalAlignment.Right};actions.Children.Add(Button(L.T("Cancel"),()=>dialog.DialogResult=false));var apply=Button(L.T(plan.Action==ModBatchAction.Delete?"ModBatchConfirmDelete":"ModBatchApply"),()=>dialog.DialogResult=true,"ConfirmBatchButton",true);apply.IsEnabled=plan.Problems.Length==0;actions.Children.Add(apply);C.DockPanel.SetDock(actions,C.Dock.Bottom);dock.Children.Add(actions);
        var panel=new C.StackPanel();panel.Children.Add(Text(L.T("ModBatchPreview"),25));if(plan.Action==ModBatchAction.Delete)panel.Children.Add(Text(L.T("ModBatchDeleteNotice"),14));panel.Children.Add(Text(L.T("ModBatchKeep"),12,true));var packages=lib.List().ToDictionary(m=>m.Id);
        if(plan.Problems.Length>0){panel.Children.Add(Text(L.T("ModBatchProblems"),18));foreach(var problem in plan.Problems)panel.Children.Add(Text(problem,14));}
        if(plan.Action==ModBatchAction.Enable&&plan.Problems.Length>0&&reports!=null&&reports.Count>0&&reports.Count<plan.Selected.Length)
        {
            panel.Children.Add(SecondaryText(L.T("ModConflictSelectionLimit",reports.Count),13,true));
            var inspect=new C.WrapPanel();var picker=new C.ComboBox{Name="BatchConflictModSelector",Width=320,Margin=new W.Thickness(0,0,12,10)};
            foreach(var id in plan.Selected)if(packages.TryGetValue(id,out var item))picker.Items.Add(new C.ComboBoxItem{Content=item.Name,Tag=item});
            picker.SelectedIndex=Math.Min(reports.Count,Math.Max(0,picker.Items.Count-1));inspect.Children.Add(picker);
            inspect.Children.Add(Button(L.T("ModShowConflicts"),()=>{if(picker.SelectedItem is C.ComboBoxItem {Tag:ModPackage item})ShowModConflicts(item,dialog,plan);},"BatchInspectConflictButton"));panel.Children.Add(inspect);
        }
        if(reports!=null)foreach(var report in reports.Where(p=>p.Value.Files.Length>0)){panel.Children.Add(SecondaryText(L.T("ModConflictFiles")+" · "+(packages.TryGetValue(report.Key,out var item)?item.Name:report.Key),18,false,true));AppendConflictFiles(panel,report.Value,30);}
        foreach(var group in new[]{("ProfileDisable",plan.Disable),("ProfileEnable",plan.Enable),("ModBatchDeleteCount",plan.Delete)})if(group.Item2.Length>0){panel.Children.Add(Text(L.T(group.Item1,group.Item2.Length),18));foreach(var id in group.Item2)panel.Children.Add(Text(packages.TryGetValue(id,out var mod)?mod.Name:id,13,true));}
        if(plan.Enable.Length+plan.Disable.Length+plan.Delete.Length==0&&plan.Problems.Length==0)panel.Children.Add(Text(L.T("ModBatchNone"),15,true));dock.Children.Add(new C.ScrollViewer{Content=panel,VerticalScrollBarVisibility=C.ScrollBarVisibility.Auto});dialog.Content=dock;return dialog.ShowDialog()==true;
    }
}
