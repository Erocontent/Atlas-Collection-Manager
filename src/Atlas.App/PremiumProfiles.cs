using Atlas.Core;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;

namespace Atlas.App;

public partial class PremiumWindow
{
    private bool profilesPageVisible;
    private C.TextBlock? profileStatus;
    private ModProfileStore CurrentProfiles() => new(LibraryRoot(game!), game!);
    private void EnsureProfileReady() { if(game!=null&&CurrentProfiles().HasPending)throw new InvalidOperationException(L.T("ProfilePending")); }
    private string ProfileStatusText()
    {
        var profiles=CurrentProfiles();if(profiles.HasPending)return L.T("ProfilePending");
        if(string.IsNullOrWhiteSpace(GamePath))return L.T("ProfileNone");
        var current=profiles.Current(GamePath);return current==null?L.T("ProfileNone"):L.T(profiles.IsCurrentModified(GamePath,library!)?"ProfileModified":"ProfileCurrent",current.Name);
    }
    private void RefreshProfileStatus() { if(game!=null&&library!=null&&profileStatus!=null)profileStatus.Text=ProfileStatusText(); }
    private void ShowProfiles() => ShowProfiles(null);
    private void ShowProfiles(string? selectedId)
    {
        if(busy||game==null)return;profilesPageVisible=true;nexusPageVisible=false;modList=null;shadePanel=null;status=null;profileStatus=null;commandSection="profiles";NavigationBuild();
        var profile=game;var profiles=CurrentProfiles();var page=Page();page.RowDefinitions.Add(new(){Height=W.GridLength.Auto});page.RowDefinitions.Add(new());
        var top=new C.StackPanel();
        top.Children.Add(SecondaryHeader(L.T("ProfilesTitle"),L.T("ProfilesHint"),profile.Name,"\uE77B"));
        var state=SecondaryText(ProfileStatusText(),13);state.Foreground=SkinBrush("Accent");state.Margin=new W.Thickness(0,0,0,16);top.Children.Add(state);
        C.Button Mutation(string text,Action action,string? name=null,bool primary=false){var button=Button(text,action,name,primary);button.IsEnabled=!profiles.HasPending;return button;}
        var actions=new C.WrapPanel{Margin=new W.Thickness(0,0,0,16)};
        actions.Children.Add(Mutation(L.T("ProfileCreate"),()=>EditProfile(null),"CreateProfileButton",true));
        actions.Children.Add(Mutation(L.T("ProfileCapture"),CaptureProfile,"CaptureProfileButton"));
        if(profiles.HasPending)actions.Children.Add(Button(L.T("ProfileRecover"),RecoverProfile,"RecoverProfileButton"));
        top.Children.Add(actions);page.Children.Add(top);
        var body=new C.Grid();body.ColumnDefinitions.Add(new(){Width=new W.GridLength(300)});body.ColumnDefinitions.Add(new());C.Grid.SetRow(body,1);page.Children.Add(body);
        var list=new C.ListBox{Name="ProfilesList",Margin=new W.Thickness(0,0,22,0)};body.Children.Add(list);
        var detail=new C.StackPanel();var scroll=new C.ScrollViewer{Content=detail,VerticalScrollBarVisibility=C.ScrollBarVisibility.Auto};C.Grid.SetColumn(scroll,1);body.Children.Add(scroll);
        foreach(var item in profiles.List())list.Items.Add(new C.ListBoxItem{Tag=item.Id,Content=SecondaryListItem(item.Name,L.T("ProfileCount",item.ModIds.Length),"\uE77B"),Padding=new W.Thickness(14),Margin=new W.Thickness(0,0,0,12)});
        if(list.Items.Count==0)
        {
            list.Visibility=W.Visibility.Collapsed;C.Grid.SetColumn(scroll,0);C.Grid.SetColumnSpan(scroll,2);
            detail.Children.Add(SecondaryEmpty(L.T("ProfilesTitle"),L.T("ProfileEmpty"),"\uE77B"));
        }
        void Fill()
        {
            if((list.SelectedItem as C.ListBoxItem)?.Tag is not string id)return;
            var item=profiles.List().Single(p=>p.Id==id);detail.Children.Clear();
            var hero=new C.StackPanel();hero.Children.Add(SecondaryText(item.Name,26,false,true));
            var applied=!string.IsNullOrWhiteSpace(GamePath)&&profiles.Current(GamePath)?.Id==item.Id;
            var tag=SecondaryText(applied?ProfileStatusText():L.T("ProfileCount",item.ModIds.Length),12,true);tag.Foreground=SkinBrush("Accent");hero.Children.Add(tag);
            var buttons=new C.WrapPanel{Margin=new W.Thickness(0,10,0,0)};
            buttons.Children.Add(Mutation(L.T("ProfileApply"),()=>ReviewProfile(id),"ApplyProfileButton",true));buttons.Children.Add(Mutation(L.T("ProfileEdit"),()=>EditProfile(item),"EditProfileButton"));
            buttons.Children.Add(Mutation(L.T("ProfileDuplicate"),()=>{var name=ProfileNameDialog(L.T("ProfileDuplicate"),L.T("ProfileDuplicateName",item.Name));if(name!=null)ShowProfiles(profiles.Duplicate(id,name).Id);},"DuplicateProfileButton"));
            buttons.Children.Add(Mutation(L.T("ProfileRename"),()=>{var name=ProfileNameDialog(L.T("ProfileRenameTitle"),item.Name);if(name!=null){profiles.Update(id,name,item.ModIds,item.Preset);ShowProfiles(id);}},"RenameProfileButton"));
            buttons.Children.Add(Mutation(L.T("ProfileDelete"),()=>{if(Confirm(L.T("ProfileDeleteConfirm",item.Name))){profiles.Delete(id);ShowProfiles();}},"DeleteProfileButton"));hero.Children.Add(buttons);
            detail.Children.Add(SecondarySurface(hero));
            detail.Children.Add(SecondaryStats((L.T("ProfileMods"),item.ModIds.Length.ToString()),(L.T("ProfilePreset"),item.Preset??L.T("ProfileNoPreset"))));
            if(item.ModIds.Length==0)detail.Children.Add(SecondaryEmpty(L.T("ProfileNoModsTitle"),L.T("ProfileVanillaHint"),"\uE7FC"));
            else detail.Children.Add(SecondaryText(L.T("ProfileMods"),18,false,true));
            var mods=library!.List().ToDictionary(m=>m.Id);var metadata=CurrentModDetails();
            foreach(var modId in item.ModIds)
            {
                if(!mods.TryGetValue(modId,out var mod)){detail.Children.Add(SecondarySurface(SecondaryText(L.T("ProfileMissing",modId),14,true),new W.Thickness(16)));continue;}
                var info=metadata.Get(mod);var image=ModImage(metadata,info.Cover,140);
                var row=new C.Grid();row.ColumnDefinitions.Add(new(){Width=new W.GridLength(72)});row.ColumnDefinitions.Add(new());
                W.UIElement thumb=image==null?SecondaryIcon("\uE8F1",52):new C.Image{Source=image,Width=52,Height=60,Stretch=M.Stretch.UniformToFill};
                row.Children.Add(thumb);
                var copy=new C.StackPanel{VerticalAlignment=W.VerticalAlignment.Center};copy.Children.Add(SecondaryText(mod.Name,16,false,true));
                var category=SecondaryText(info.Category??mod.Category,12,true);category.Margin=new W.Thickness(0);copy.Children.Add(category);C.Grid.SetColumn(copy,1);row.Children.Add(copy);
                detail.Children.Add(SecondarySurface(row,new W.Thickness(16)));
            }
        }
        list.SelectionChanged+=(_,_)=>Fill();var current=string.IsNullOrWhiteSpace(GamePath)?null:profiles.Current(GamePath)?.Id;
        list.SelectedItem=list.Items.Cast<C.ListBoxItem>().FirstOrDefault(i=>Equals(i.Tag,selectedId??current))??list.Items.Cast<C.ListBoxItem>().FirstOrDefault();
    }
    private string? ProfileNameDialog(string title,string initial)
    {
        var dialog=ProfileDialog(title,510,260);var panel=new C.StackPanel{Margin=new W.Thickness(25)};panel.Children.Add(Text(L.T("ProfileName"),18));var name=new C.TextBox{Name="ProfileNameInput",Text=initial,MaxLength=100,Margin=new W.Thickness(0,8,0,22)};panel.Children.Add(name);var actions=new C.WrapPanel();actions.Children.Add(Button(L.T("Cancel"),()=>dialog.DialogResult=false));var save=Button(L.T("ProfileSave"),()=>{if(!string.IsNullOrWhiteSpace(name.Text))dialog.DialogResult=true;},"SaveProfileButton",true);actions.Children.Add(save);panel.Children.Add(actions);dialog.Content=panel;return dialog.ShowDialog()==true?name.Text.Trim():null;
    }
    private W.Window ProfileDialog(string title,double width,double height)
    {
        var dialog=new W.Window{Owner=this,Title=title,Width=width,Height=height,MinWidth=460,MinHeight=240,Background=Brush("#15171B"),Foreground=SkinBrush("Text"),WindowStartupLocation=W.WindowStartupLocation.CenterOwner};dialog.Resources.MergedDictionaries.Add(Resources);return dialog;
    }
    private void CaptureProfile()
    {
        EnsureProfileReady();var folder=ModLibrary.ValidateGame(GamePath,game!);var name=ProfileNameDialog(L.T("ProfileCapture"),L.T("ProfileCaptureName"));if(name==null)return;var item=CurrentProfiles().Capture(name,folder,library!);ShowProfiles(item.Id);
    }
    private void EditProfile(ModProfile? existing)
    {
        EnsureProfileReady();var profiles=CurrentProfiles();var lib=library!;var mods=lib.List();var dialog=ProfileDialog(L.T("ProfileEditor"),760,700);
        var dock=new C.DockPanel{Margin=new W.Thickness(26)};var top=new C.StackPanel();top.Children.Add(Text(L.T("ProfileName"),15));var name=new C.TextBox{Name="ProfileNameInput",Text=existing?.Name??L.T("ProfileCreate"),MaxLength=100,Margin=new W.Thickness(0,0,0,18)};top.Children.Add(name);top.Children.Add(Text(L.T("ProfilePreset"),15));
        var presets=new C.ComboBox{Name="ProfilePresetSelector",Margin=new W.Thickness(0,0,0,16)};top.Children.Add(presets);top.Children.Add(Text(L.T("ProfileMissingHint"),12,true));C.DockPanel.SetDock(top,C.Dock.Top);dock.Children.Add(top);
        var footer=new C.WrapPanel{HorizontalAlignment=W.HorizontalAlignment.Right};footer.Children.Add(Button(L.T("Cancel"),()=>dialog.DialogResult=false));var save=Button(L.T("ProfileSave"),()=>{if(!string.IsNullOrWhiteSpace(name.Text))dialog.DialogResult=true;},"SaveProfileButton",true);footer.Children.Add(save);C.DockPanel.SetDock(footer,C.Dock.Bottom);dock.Children.Add(footer);
        var rows=new C.StackPanel();var choices=new Dictionary<string,C.CheckBox>();
        foreach(var mod in mods){var check=new C.CheckBox{Tag=mod.Id,Content=mod.Name+" · "+mod.Category,IsChecked=existing?.ModIds.Contains(mod.Id)==true,Foreground=SkinBrush("Text"),Margin=new W.Thickness(0,8,0,8)};choices.Add(mod.Id,check);rows.Children.Add(check);}
        foreach(var missing in existing?.ModIds.Where(id=>!choices.ContainsKey(id))??[]){var check=new C.CheckBox{Tag=missing,Content=L.T("ProfileMissing",missing),IsChecked=true,Foreground=SkinBrush("Accent"),Margin=new W.Thickness(0,8,0,8)};choices.Add(missing,check);rows.Children.Add(check);}
        dock.Children.Add(new C.ScrollViewer{Content=rows,VerticalScrollBarVisibility=C.ScrollBarVisibility.Auto});dialog.Content=dock;
        var generation=0;var closed=false;var initialized=false;dialog.Closed+=(_,_)=>closed=true;
        async void UpdatePresets()
        {
            var current=++generation;save.IsEnabled=false;var previous=initialized?(presets.SelectedItem as C.ComboBoxItem)?.Tag as string:existing?.Preset;var selected=choices.Where(p=>p.Value.IsChecked==true).Select(p=>p.Key).ToArray();
            try{var available=await Task.Run(()=>profiles.AvailablePresets(selected.Where(id=>mods.Any(m=>m.Id==id)).ToArray(),lib));if(closed||current!=generation)return;presets.Items.Clear();presets.Items.Add(new C.ComboBoxItem{Content=L.T("ProfileNoPreset")});foreach(var preset in available)presets.Items.Add(new C.ComboBoxItem{Content=preset,Tag=preset});presets.SelectedItem=presets.Items.Cast<C.ComboBoxItem>().FirstOrDefault(i=>Equals(i.Tag,previous))??presets.Items[0];initialized=true;save.IsEnabled=true;}
            catch(Exception error){if(!closed&&current==generation)Error(error);}
        }
        foreach(var check in choices.Values){check.Checked+=(_,_)=>UpdatePresets();check.Unchecked+=(_,_)=>UpdatePresets();}dialog.Loaded+=(_,_)=>UpdatePresets();
        if(dialog.ShowDialog()!=true)return;var ids=choices.Where(p=>p.Value.IsChecked==true).Select(p=>p.Key).ToArray();var preset=(presets.SelectedItem as C.ComboBoxItem)?.Tag as string;
        var saved=existing==null?profiles.Create(name.Text.Trim(),ids,preset):profiles.Update(existing.Id,name.Text.Trim(),ids,preset);ShowProfiles(saved.Id);
    }
    private async void ReviewProfile(string id)
    {
        if(busy)return;try
        {
            EnsureProfileReady();var profiles=CurrentProfiles();var folder=ModLibrary.ValidateGame(GamePath,game!);var lib=library!;var collections=CurrentCollections();
            busy=true;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=false;ProfilePlan plan;
            try{plan=await Task.Run(()=>profiles.Preview(id,folder,lib,collections));}finally{busy=false;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=true;}
            if(!PreviewProfile(plan,lib))return;busy=true;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=false;
            try{await Task.Run(()=>profiles.Apply(plan,folder,lib,collections));}finally{busy=false;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=true;}
            ShowProfiles(id);
        }
        catch(Exception error){Error(error);if(!busy)ShowProfiles(id);}
    }
    private bool PreviewProfile(ProfilePlan plan,ModLibrary lib,bool collection=false, bool deactivate=false)
    {
        var dialog=ProfileDialog(L.T(collection?(deactivate?"CollectionDeactivate":"CollectionActivate"):"ProfilePreview"),700,650);var dock=new C.DockPanel{Margin=new W.Thickness(26)};var actions=new C.WrapPanel{HorizontalAlignment=W.HorizontalAlignment.Right};actions.Children.Add(Button(L.T("Cancel"),()=>dialog.DialogResult=false));actions.Children.Add(Button(L.T("ProfileConfirm"),()=>dialog.DialogResult=true,"ConfirmProfileApplyButton",true));C.DockPanel.SetDock(actions,C.Dock.Bottom);dock.Children.Add(actions);
        var panel=new C.StackPanel();panel.Children.Add(Text(plan.Profile.Name,26));if(!collection)panel.Children.Add(Text(L.T("ProfilePreviewHint"),13,true));var mods=lib.List().ToDictionary(m=>m.Id);
        foreach(var group in new[]{("ProfileDisable",plan.Disable),("ProfileEnable",plan.Enable),("ProfileKeep",plan.Keep)}){panel.Children.Add(Text(L.T(group.Item1,group.Item2.Length),17));foreach(var id in group.Item2)panel.Children.Add(Text(mods.TryGetValue(id,out var mod)?mod.Name:id,13,true));}
        if(!collection)panel.Children.Add(Text(L.T("ProfilePreset")+": "+(plan.Preset??L.T("ProfileClearPreset")),14));dock.Children.Add(new C.ScrollViewer{Content=panel,VerticalScrollBarVisibility=C.ScrollBarVisibility.Auto});dialog.Content=dock;return dialog.ShowDialog()==true;
    }
    private async void RecoverProfile()
    {
        if(busy)return;try{var profiles=CurrentProfiles();var lib=library!;var collections=CurrentCollections();var folder=GamePath;busy=true;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=false;await Task.Run(()=>{if(profiles.HasPending)profiles.Recover(folder,lib,collections);else lib.Recover(folder);});busy=false;OpenGame(game!);}catch(Exception error){Error(error);}finally{busy=false;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=true;}
    }
}
