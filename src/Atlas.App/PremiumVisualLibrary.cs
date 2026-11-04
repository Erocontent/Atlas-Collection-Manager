using Atlas.Core;
using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;

namespace Atlas.App;
public partial class PremiumWindow
{
    private C.StackPanel ModFilterGroup(string label,C.Control control)
    {
        var group=new C.StackPanel{Margin=new W.Thickness(0,0,12,12)};
        var caption=SecondaryText(label,10,true);caption.Margin=new W.Thickness(0,0,0,5);group.Children.Add(caption);
        control.Margin=new W.Thickness(0);control.Height=40;group.Children.Add(control);
        System.Windows.Automation.AutomationProperties.SetName(control,label);return group;
    }
    private void ResetModFilters()
    {
        if(busy)return;
        refreshingMods=true;
        try
        {
            selectedCategory=null;selectedCharacterFilter=null;selectedState="all";search?.Clear();modList?.SelectedItems.Clear();
            if(stateFilter!=null)stateFilter.SelectedIndex=0;
        }
        finally{refreshingMods=false;}
        RefreshMods();
    }
    private void SelectLibraryMod(string id)
    {
        if(modList==null)return;
        modList.SelectedItem=modList.Items.Cast<C.ListBoxItem>().FirstOrDefault(i=>i.Tag is ModPackage p&&p.Id==id);ShowDetails();
    }
    private void ShowModGallery(ModPackage package,string? initial=null)
    {
        if(busy||game==null||library==null)return;
        var profile=game;var mod=library.List().Single(m=>m.Id==package.Id);var metadata=CurrentModDetails();var info=WithNexusVersion(metadata.Get(mod));
        var paths=info.Images.Where(p=>metadata.ImagePath(p)!=null).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(paths.Length==0){ShowModPage(mod);return;}
        var index=Array.FindIndex(paths,p=>p==(initial??info.Cover));if(index<0)index=0;
        var changed=false;var dialog=ProfileDialog(L.T("ModGallery"),1040,790);dialog.Name="ModGalleryDialog";
        var layout=new C.DockPanel{Margin=new W.Thickness(24)};
        var heading=new C.StackPanel();heading.Children.Add(SecondaryText(mod.Name,24,false,true));var counter=SecondaryText("",12,true);counter.Name="ModGalleryCounter";heading.Children.Add(counter);
        C.DockPanel.SetDock(heading,C.Dock.Top);layout.Children.Add(heading);
        var actions=new C.WrapPanel{HorizontalAlignment=W.HorizontalAlignment.Center,Margin=new W.Thickness(0,18,0,0)};
        C.DockPanel.SetDock(actions,C.Dock.Bottom);layout.Children.Add(actions);
        var image=new C.Image{Name="ModGalleryImage",Stretch=M.Stretch.Uniform};
        var missing=SecondaryText(L.T("ModGalleryEmpty"),15,true);missing.HorizontalAlignment=W.HorizontalAlignment.Center;missing.VerticalAlignment=W.VerticalAlignment.Center;
        var viewport=new C.Grid{Background=SkinBrush("Base")};viewport.Children.Add(image);viewport.Children.Add(missing);layout.Children.Add(viewport);
        C.Button? previous=null,next=null,cover=null;
        void Update()
        {
            image.Source=ModImage(metadata,paths[index],2200);missing.Visibility=image.Source==null?W.Visibility.Visible:W.Visibility.Collapsed;
            counter.Text=L.T("ModGalleryCount",index+1,paths.Length);
            if(previous!=null)previous.IsEnabled=index>0;if(next!=null)next.IsEnabled=index<paths.Length-1;
            if(cover!=null)cover.IsEnabled=!CurrentProfiles().HasPending&&image.Source!=null&&metadata.Get(mod).Cover!=paths[index];
        }
        previous=Button(L.T("ModPrevious"),()=>{if(index>0){index--;Update();}},"ModGalleryPreviousButton");
        next=Button(L.T("ModNext"),()=>{if(index<paths.Length-1){index++;Update();}},"ModGalleryNextButton");
        cover=Button(L.T("ModUseCover"),()=>{EnsureProfileReady();metadata.RecordImage(mod.Id,paths[index],true);changed=true;Update();},"ModGallerySetCoverButton",true);
        actions.Children.Add(previous);actions.Children.Add(next);actions.Children.Add(cover);actions.Children.Add(Button(L.T("ModClose"),()=>dialog.Close(),"ModGalleryCloseButton"));
        var root=new C.Grid();root.Children.Add(layout);dialog.Content=root;ConstrainModPreview(dialog);Update();dialog.ShowDialog();
        if(changed&&!nexusClosed&&game?.Id==profile.Id){if(modList!=null)RefreshMods();else ShowModPage(mod);}
    }
    private void AppendConflictFiles(C.Panel panel,ModConflictReport report,int maximum=200)
    {
        foreach (var group in report.Files.Take(maximum).GroupBy(entry => entry.OwnerId))
        {
            var copy = new C.StackPanel();
            copy.Children.Add(SecondaryText(group.First().OwnerName, 16, false, true));
            if (NexusPackageLabel(group.Key) is {} file) copy.Children.Add(SecondaryText(file, 13, true));
            foreach (var entry in group)
            {
                var path = SecondaryText(entry.CandidatePath, 12);
                path.Margin = new W.Thickness(0, 4, 0, 4);
                copy.Children.Add(path);
                if (!entry.CandidatePath.Equals(entry.ExistingPath, StringComparison.OrdinalIgnoreCase))
                    copy.Children.Add(SecondaryText(entry.ExistingPath, 11, true));
            }
            panel.Children.Add(SecondarySurface(copy, new W.Thickness(14)));
        }
        if(report.Truncated||report.Files.Length>maximum)panel.Children.Add(SecondaryText(L.T("ModConflictLimit",maximum),12,true));
    }
    private async void ShowModConflicts(ModPackage package,W.Window? owner=null,ModBatchPlan? batch=null)
    {
        if(busy||game==null||library==null)return;
        try
        {
            IndexCoverSources(CurrentCollections().List());
            var mod=package;var lib=library;var folder=GamePath;var packages=lib.List();var active=lib.GetActive();
            busy=true;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=false;
            var conflictFolder = string.IsNullOrWhiteSpace(folder) ? Path.GetTempPath() : folder;
            ModConflictReport report;
            try{report=await Task.Run(()=>ModConflictDetails.Explain(mod,packages,batch==null?LibraryConflictRecords(packages,conflictFolder):BatchActiveRecords(batch,packages,active,folder),conflictFolder));}
            finally{busy=false;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=true;}
            if(owner!=null&&!owner.IsVisible)return;
            var dialog=ProfileDialog(L.T("ModConflictFiles"),760,680);if(owner!=null)dialog.Owner=owner;dialog.Name="ModConflictsDialog";
            var dock=new C.DockPanel{Margin=new W.Thickness(24)};
            var close=Button(L.T("ModClose"),()=>dialog.Close(),"ModConflictsCloseButton");close.HorizontalAlignment=W.HorizontalAlignment.Right;C.DockPanel.SetDock(close,C.Dock.Bottom);dock.Children.Add(close);
            var content=new C.StackPanel();content.Children.Add(SecondaryText(mod.Name,25,false,true));if(NexusPackageLabel(mod.Id) is {} selectedFile) content.Children.Add(SecondaryText(selectedFile,14,true));content.Children.Add(SecondaryText(L.T(batch==null?"ModConflictHint":"ModConflictBatchHint"),13,true));
            if(report.Files.Length==0)content.Children.Add(SecondaryEmpty(L.T("ModConflictFiles"),L.T(string.IsNullOrWhiteSpace(folder)?"ModConflictFolderHint":"ModConflictNone"),"\uE73E"));
            else
            {
                AppendConflictFiles(content, report);
                if (batch == null && !report.Truncated && !string.IsNullOrWhiteSpace(folder))
                {
                    var options = new C.WrapPanel();
                    foreach (var candidate in report.Files.Select(file => file.OwnerId).Append(mod.Id).Distinct())
                    {
                        var choice = packages.SingleOrDefault(item => item.Id == candidate);
                        if (choice != null) options.Children.Add(Button(L.T("ModPrefer", NexusPackageLabel(choice.Id) ?? choice.Name), () =>
                        {
                            dialog.Close();
                            Guard(() => PreferConflictingMod(choice.Id));
                        }));
                    }
                    content.Children.Add(options);
                }
            }
            dock.Children.Add(new C.ScrollViewer{Content=content,VerticalScrollBarVisibility=C.ScrollBarVisibility.Auto});var root=new C.Grid();root.Children.Add(dock);dialog.Content=root;dialog.ShowDialog();
        }
        catch(Exception error){Error(error);}
    }
    private static List<ActiveMod> BatchActiveRecords(ModBatchPlan plan,IReadOnlyList<ModPackage> packages,IReadOnlyList<ActiveMod> records,string folder)
    {
        var byId=packages.ToDictionary(p=>p.Id);var active=records.Where(a=>!plan.Disable.Contains(a.Id)).ToList();
        foreach(var id in plan.Selected)if(byId.TryGetValue(id,out var p)&&!active.Any(a=>a.Id==id&&string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a.Game)),Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)),StringComparison.OrdinalIgnoreCase)))
            active.Add(new(id,folder,ImportAnalysis.Targets(p.Files,p.Destination,false).Select(path=>new ManagedFile(path,"",false)).ToArray()));
        return active;
    }
    private static Dictionary<string,ModConflictReport> BatchConflictReports(ModBatchPlan plan,ModLibrary lib,string folder)
    {
        var result=new Dictionary<string,ModConflictReport>();
        if(plan.Action!=ModBatchAction.Enable||plan.Problems.Length==0||string.IsNullOrWhiteSpace(folder))return result;
        var packages=lib.List();var byId=packages.ToDictionary(p=>p.Id);
        var active=BatchActiveRecords(plan,packages,lib.GetActive(),folder);
        foreach(var id in plan.Selected.Take(20))if(byId.TryGetValue(id,out var p))result[id]=ModConflictDetails.Explain(p,packages,active,folder,30);
        return result;
    }
}
