using System.IO;
using Atlas.Core;
using System.Diagnostics;
using W=System.Windows;
using C=System.Windows.Controls;

namespace Atlas.App;

public partial class PremiumWindow
{
    private sealed class CollectionTransfer(CollectionDownloadItem item)
    {
        public CollectionDownloadItem Item=item;
        public string State=item.Ready?"CollectionTransferReady":"CollectionTransferWaiting";
        public string? Error;
        public string? LocalPath;
        public bool SourceOpened;
        public double? DirectPercent;
        public NexusJob? Job;
    }
    private sealed class CollectionTransferSession(GameProfile profile,string id,CollectionTransfer[] items)
    {
        public GameProfile Profile=profile;
        public string Id=id;
        public CollectionTransfer[] Items=items;
        public bool Cancelled;
        public CancellationTokenSource? DirectCancellation;
        public List<NexusJob> OwnedJobs=[];
    }
    private CollectionTransferSession? collectionTransfer;
    private bool collectionPreparing,collectionImporting;
    private string? visibleCollectionId;
    private Action? refreshCollectionTransfer;

    private static string TransferKey(CollectionMod mod) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(mod)));

    private HashSet<string> ReadTransferFailures(GameProfile profile,string id) =>
        new CollectionStore(LibraryRoot(profile),profile).ReadFailures(id);

    private void SetTransferFailure(CollectionTransferSession session,CollectionTransfer target,bool failed) =>
        new CollectionStore(LibraryRoot(session.Profile),session.Profile)
            .SetFailure(session.Id,TransferKey(target.Item.Mod),failed, target.Error);

    private void RecordCollectionDownloadFailure(NexusJob job)
    {
        if(collectionTransfer is not {} session)return;
        foreach(var target in session.Items.Where(item=>ReferenceEquals(item.Job,job)))
        {
            target.Error = L.T("NexusFailed") + " — " + job.Error;
            SetTransferFailure(session,target,true);
        }
    }

    private void DrawCollectionTransfer(C.StackPanel host,GameProfile profile,string id,Dictionary<string,C.TextBlock> progress)
    {
        host.Children.Clear();
        var session=collectionTransfer;
        if(session==null||session.Id!=id||session.Profile.Id!=profile.Id)return;
        if (session.Cancelled) host.Children.Add(Text(L.T("CollectionTransferStopped",
            session.Items.Count(t=>t.State=="CollectionTransferReady"),session.Items.Length),14,true));
        foreach(var item in session.Items)
        {
            var state=item.State;
            if(state=="CollectionTransferWaiting"&&item.Job!=null)
                state=item.Job.State;
            if(state=="CollectionTransferWaiting"&&item.LocalPath==null&&!item.Item.Mod.DirectDownload&&(item.Item.Mod.Nexus==null||nexusUser?.IsPremium!=true))state="CollectionManualNeeded";
            var suffix=state=="NexusDownloading"&&item.Job?.Total>0
                ?" · "+(100d*item.Job.Bytes/item.Job.Total.Value).ToString("0")+"%":
                state=="NexusDownloading" && item.DirectPercent is {} percent ? $" · {percent:0}%" : "";
            if(progress.TryGetValue(item.Item.Mod.Id,out var caption))
                caption.Text = state == "CollectionTransferReady" ? "" :
                    (state is "NexusFailed" or "CollectionImportFailed") && item.Error != null ? item.Error : L.T(state) + suffix;

        }
    }
    private bool CollectionDownloadsRunning(string profileId,string id) =>
        collectionTransfer is {Cancelled:false} session&&session.Profile.Id==profileId&&session.Id==id&&
        (session.DirectCancellation != null || session.Items.Any(item=>item.Job?.State is "NexusQueued" or "NexusDownloading"));

    private async void StartCollectionDownload(string id,bool failuresOnly=false)
    {
        if(busy||game==null||collectionPreparing||collectionImporting)return;
        if(failuresOnly&&CollectionDownloadsRunning(game.Id,id))return;
        collectionPreparing=true;
        try
        {
            EnsureProfileReady();
            var profile=game;
            var store=new CollectionStore(LibraryRoot(profile),profile);
            var before=store.Find(id)??throw new InvalidOperationException(L.T("CollectionTransferChanged"));
            var beforeJson=System.Text.Json.JsonSerializer.Serialize(before);
            var previous=collectionTransfer;
            CancelCollectionTransfer();
            // Resolve the saved account before choosing the premium or website path.
            await ProcessNexusQueue();
            if(nexusClosed||game?.Id!=profile.Id)return;
            if(busy)throw new InvalidOperationException(L.T("CollectionTransferChanged"));
            EnsureProfileReady();
            var collection=store.Find(id)??throw new InvalidOperationException(L.T("CollectionTransferChanged"));
            if(System.Text.Json.JsonSerializer.Serialize(collection)!=beforeJson)throw new InvalidOperationException(L.T("CollectionTransferChanged"));
            var plan=CollectionDownloads.Plan(collection,library!.List());
            if(failuresOnly){var failures=ReadTransferFailures(profile,id);plan=plan.Where(item=>!item.Ready&&failures.Contains(TransferKey(item.Mod))).ToArray();if(plan.Length==0)return;}
            foreach(var item in plan.Where(i=>!collection.Entries.Single(e=>e.ModId==i.Mod.Id).Selected))
                store.SaveSelection(id,item.Mod.Id,true);
            var session=new CollectionTransferSession(profile,id,plan.Select(p=>new CollectionTransfer(p)).ToArray());
            collectionTransfer=session;
            LoadNexusHistory();
            foreach(var target in session.Items.Where(t=>!t.Item.Ready))
            {
                if(previous?.Profile.Id==profile.Id&&previous.Id==id)
                    target.LocalPath=previous.Items.FirstOrDefault(item=>TransferKey(item.Item.Mod)==TransferKey(target.Item.Mod)&&item.LocalPath!=null&&File.Exists(item.LocalPath))?.LocalPath;
                if(target.Item.Mod.Nexus is not {} source)continue;
                var domain=NexusGames.Domain(profile.Id);
                target.Job=nexusJobs.LastOrDefault(j=>j.Link.GameDomain==domain&&j.Link.ModId==source.ModId&&j.Link.FileId==source.FileId&&((j.Download!=null&&File.Exists(j.Download.Path))||j.State is "NexusQueued" or "NexusDownloading"));

            }
            QueuePendingCollectionDownloads();
            ShowCollections(id);
            _=ProcessDirectCollectionDownloads(session);
            _=ProcessNexusQueue();
            _=ProcessCollectionImports();
        }
        catch(Exception error){Error(error);}
        finally{collectionPreparing=false;}
    }
    private async Task ProcessDirectCollectionDownloads(CollectionTransferSession session)
    {
        var items = session.Items.Where(item => item.State == "CollectionTransferWaiting" &&
            item.Item.Mod.DirectDownload && item.LocalPath == null).ToArray();
        if (items.Length == 0) return;
        using var cancellation = new CancellationTokenSource();
        session.DirectCancellation = cancellation;
        try
        {
            foreach (var item in items)
            {
                if (nexusClosed || session.Cancelled) return;
                item.State = "NexusDownloading";
                refreshCollectionTransfer?.Invoke();
                try
                {
                    var progress = new Progress<double>(percent =>
                    {
                        if (collectionTransfer != session || item.State != "NexusDownloading") return;
                        item.DirectPercent = percent;
                        refreshCollectionTransfer?.Invoke();
                    });
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                    timeout.CancelAfter(TimeSpan.FromHours(1));
                    item.LocalPath = await CollectionDirectDownloads.DownloadAsync(item.Item.Mod,
                        Path.Combine(NexusDownloadFolder, "direct"), progress, timeout.Token);
                    item.State = "CollectionTransferWaiting";
                }
                catch (OperationCanceledException) when (session.Cancelled || nexusClosed) { return; }
                catch (Exception error)
                {
                    item.State = "NexusFailed";
                    item.Error = L.T("NexusFailed") + " — " + error.Message;
                    SetTransferFailure(session, item, true);
                }
            }
        }
        finally
        {
            session.DirectCancellation = null;
            if (!nexusClosed && collectionTransfer == session)
            {
                refreshCollectionTransfer?.Invoke();
                if (!session.Cancelled) _ = ProcessCollectionImports();
            }
        }
    }
    private void QueuePendingCollectionDownloads()
    {
        if(nexusUser?.IsPremium!=true||collectionTransfer is not {Cancelled:false} session)return;
        foreach(var target in session.Items.Where(t=>t.State=="CollectionTransferWaiting"&&t.Job==null&&t.LocalPath==null))
        {
            if(target.Item.Mod.Nexus is not {} source)continue;
            var domain=NexusGames.Domain(session.Profile.Id);
            var existing=nexusJobs.LastOrDefault(j=>j.Link.GameDomain==domain&&j.Link.ModId==source.ModId&&j.Link.FileId==source.FileId&&j.State is "NexusQueued" or "NexusDownloading");
            if(existing!=null){target.Job=existing;continue;}
            if(nexusJobs.Count(j=>j.State is "NexusQueued" or "NexusDownloading")>=100)break;
            target.Job=new NexusJob(new(domain,source.ModId,source.FileId));
            nexusJobs.Add(target.Job);session.OwnedJobs.Add(target.Job);
        }
    }
    private void CancelCollectionTransfer()
    {
        if(collectionTransfer is not {} session)return;
        session.Cancelled=true;
        session.DirectCancellation?.Cancel();
        foreach(var job in session.OwnedJobs.Where(j=>j.State is "NexusQueued" or "NexusDownloading"))
        {
            if(job.Cancellation!=null)job.Cancellation.Cancel();else job.State="NexusCancelled";
        }
        RefreshNexusQueue();
    }
    private void BindCollectionDownload(NexusJob job)
    {
        if(collectionTransfer is not {Cancelled:false} session)return;
        foreach(var item in session.Items.Where(t=>t.State=="CollectionTransferWaiting"&&t.Item.Mod.Nexus is {} source&&
            job.Link.GameDomain==NexusGames.Domain(session.Profile.Id)&&job.Link.ModId==source.ModId&&job.Link.FileId==source.FileId))
            item.Job=job;
        refreshCollectionTransfer?.Invoke();
    }
    private async Task ProcessCollectionImports()
    {
        if(collectionImporting||collectionTransfer is not {Cancelled:false} session||nexusClosed)return;
        collectionImporting=true;
        try
        {
            foreach(var target in session.Items.Where(t=>t.State=="CollectionTransferWaiting"))
            {
                var path=target.LocalPath??target.Job?.Download?.Path;
                if(path==null)continue;
                while(busy&&!nexusClosed&&!session.Cancelled)await Task.Delay(150);
                if(nexusClosed||session.Cancelled||collectionTransfer!=session)return;
                try
                {
                    var store=new CollectionStore(LibraryRoot(session.Profile),session.Profile);
                    if(!CollectionDownloads.IsCurrent(target.Item,store.Find(session.Id)))throw new InvalidOperationException(L.T("CollectionTransferChanged"));
                    if(new ModProfileStore(LibraryRoot(session.Profile),session.Profile).HasPending)throw new InvalidOperationException(L.T("ProfilePending"));
                    busy=true;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=false;
                    target.State="CollectionTransferImporting";refreshCollectionTransfer?.Invoke();
                    await Task.Run(()=>store.AttachZip(session.Id,target.Item.Mod.Id,path,new ModLibrary(LibraryRoot(session.Profile),session.Profile)));
                    target.State="CollectionTransferReady";target.Error=null;SetTransferFailure(session,target,false);
                }
                catch(Exception error){target.State="CollectionImportFailed";target.Error=L.T("CollectionImportFailed") + " — " + error.Message;SetTransferFailure(session,target,true);}
                finally{busy=false;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=true;refreshCollectionTransfer?.Invoke();}
            }
        }
        finally{collectionImporting=false;
            if(!nexusClosed&&!busy&&game?.Id==session.Profile.Id&&commandSection=="collections"&&!nexusPageVisible&&visibleCollectionId==session.Id)ShowCollections(session.Id);
            if(!session.Cancelled&&!nexusClosed&&session.Items.Any(t=>t.State=="CollectionTransferWaiting"&&(t.LocalPath!=null||t.Job?.Download!=null)))_=ProcessCollectionImports();}
    }
    private static bool CanOpenCollectionSource(CollectionTransfer t) =>
        (t.Item.Mod.SourceUrl != null || t.Item.Mod.Nexus != null) && !t.SourceOpened&&t.State!="CollectionTransferReady"&&t.State is not ("CollectionTransferImporting" or "NexusDownloading")&&t.Job?.State is not ("NexusQueued" or "NexusDownloading")&&t.LocalPath==null;

    private void OpenNextCollectionSource()
    {
        if(collectionTransfer is not {Cancelled:false} session)return;
        var target=session.Items.FirstOrDefault(CanOpenCollectionSource);
        if(target==null)return;
        target.SourceOpened=true;
        if(target.Item.Mod.Nexus is {} source)OpenNexusFilePage(new(NexusGames.Domain(session.Profile.Id),source.ModId,source.FileId));
        else if(target.Item.Mod.SourceUrl is {} url)Process.Start(new ProcessStartInfo(url){UseShellExecute=true});
        refreshCollectionTransfer?.Invoke();
    }
    private void ChooseCollectionFiles(GameProfile profile, string id)
    {
        if (busy || collectionImporting || collectionPreparing) return;
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = L.T("ZipFilter"), Multiselect = true, Title = L.T("CollectionTransferLocal") };
        if (picker.ShowDialog(this) != true) return;
        if (picker.FileNames.Length > 100)
        {
            W.MessageBox.Show(this, L.T("CollectionTransferFileLimit"), "Atlas");
            return;
        }
        if (collectionTransfer is not { Cancelled: false } session || session.Id != id || session.Profile.Id != profile.Id)
        {
            if (collectionTransfer is { Cancelled: false } other && CollectionDownloadsRunning(other.Profile.Id, other.Id)) return;
            var collection = CurrentCollections().Find(id);
            if (collection == null) return;
            var plan = CollectionDownloads.Plan(collection, library!.List());
            session = new CollectionTransferSession(profile, id, plan.Select(item => new CollectionTransfer(item)).ToArray());
            collectionTransfer = session;
        }
        ReviewCollectionFiles(session, picker.FileNames);
        refreshCollectionTransfer?.Invoke();
    }
    private void ReviewCollectionFiles(CollectionTransferSession session,string[] paths)
    {
        var targets=session.Items.Where(t=>t.State is not ("CollectionTransferReady" or "CollectionTransferImporting" or "NexusDownloading")).ToArray();
        var dialog=new W.Window{Owner=this,Title=L.T("CollectionTransferReview"),Width=720,Height=540,MinWidth=540,MinHeight=400,
            Background=Brush("#15171B"),Foreground=SkinBrush("Text"),WindowStartupLocation=W.WindowStartupLocation.CenterOwner};
        dialog.Resources.MergedDictionaries.Add(Resources);
        var dock=new C.DockPanel{Margin=new W.Thickness(24)};
        var actions=new C.WrapPanel{HorizontalAlignment=W.HorizontalAlignment.Right};
        actions.Children.Add(Button(L.T("Cancel"),()=>dialog.DialogResult=false));
        var choices=new List<(string Path,C.ComboBox Choice)>();
        actions.Children.Add(Button(L.T("CollectionTransferImport"),()=>
        {
            var selected=choices.Where(p=>p.Choice.SelectedValue is CollectionTransfer).Select(p=>(p.Path,Target:(CollectionTransfer)p.Choice.SelectedValue)).ToArray();
            if(selected.Length==0||selected.Select(p=>p.Target).Distinct().Count()!=selected.Length)
            {W.MessageBox.Show(dialog,L.T("CollectionTransferUnique"),"Atlas");return;}
            foreach(var pair in selected){pair.Target.LocalPath=pair.Path;pair.Target.State="CollectionTransferWaiting";pair.Target.Error=null;}
            dialog.DialogResult=true;
        },"ConfirmCollectionFilesButton",true));
        C.DockPanel.SetDock(actions,C.Dock.Bottom);dock.Children.Add(actions);
        var rows=new C.StackPanel();rows.Children.Add(Text(L.T("CollectionTransferReviewHint"),14));
        foreach(var path in paths.Take(100))
        {
            rows.Children.Add(Text(Path.GetFileName(path),14));
            var combo=new C.ComboBox{SelectedValuePath="Tag",Margin=new W.Thickness(0,0,0,18)};
            combo.Items.Add(new C.ComboBoxItem{Content=L.T("CollectionTransferSkip")});
            foreach(var target in targets)combo.Items.Add(new C.ComboBoxItem{Content=target.Item.Mod.Name,Tag=target});
            combo.SelectedIndex=0;choices.Add((path,combo));rows.Children.Add(combo);
        }
        dock.Children.Add(new C.ScrollViewer{Content=rows,VerticalScrollBarVisibility=C.ScrollBarVisibility.Auto});
        var layout=new C.Grid();layout.Children.Add(dock);dialog.Content=layout;
        if(dialog.ShowDialog()==true)_=ProcessCollectionImports();
    }
}
