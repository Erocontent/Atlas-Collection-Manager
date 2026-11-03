using Atlas.Core;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;

namespace Atlas.App;

public partial class PremiumWindow
{
    private readonly INexusCredentials nexusCredentials;
    private readonly Func<string,NexusApiClient> nexusFactory;
    private NexusApiClient? nexusClient;
    private NexusUser? nexusUser;
    private CancellationTokenSource? nexusSignIn;
    private bool nexusPageVisible, nexusProcessing, nexusConnecting, nexusClosed, nexusHistoryLoaded;
    private int nexusAccountGeneration;
    private readonly List<NexusJob> nexusJobs = [];
    private C.StackPanel? nexusQueuePanel;
    private static string NexusSlug => typeof(PremiumWindow).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(x=>x.Key=="NexusApplicationSlug")?.Value ?? "";
    private sealed class NexusJob(NexusLink link)
    {
        public NexusLink Link = link;
        public string State = "NexusQueued";
        public string? Error;
        public long Bytes; public long? Total;
        public NexusDownload? Download;
        public CancellationTokenSource? Cancellation;
        public C.TextBlock? Caption; public C.ProgressBar? Progress;
    }
    private sealed record NexusHistory(string GameId,long ModId,long FileId,NexusFile File,string Archive);
    public void ReceiveNxm(string value)
    {
        try { QueueNexus(NexusLink.Parse(value)); if (!busy) { if(collectionTransfer is not {Cancelled:false})ShowNexus(); if(WindowState==W.WindowState.Minimized)WindowState=W.WindowState.Normal; Activate(); } }
        catch(Exception error) { Error(error); }
    }
    private void QueueNexus(NexusLink link)
    {
        LoadNexusHistory();
        if(nexusJobs.FirstOrDefault(j=>j.Link.GameDomain==link.GameDomain&&j.Link.ModId==link.ModId&&j.Link.FileId==link.FileId&&j.State is "NexusQueued" or "NexusDownloading") is {} existing){BindCollectionDownload(existing);return;}
        if(nexusJobs.Count(j=>j.State is "NexusQueued" or "NexusDownloading")>=100)throw new InvalidOperationException("The Nexus queue is full.");
        var job=new NexusJob(link);nexusJobs.Add(job);BindCollectionDownload(job); RefreshNexusQueue(); _ = ProcessNexusQueue();
    }
    private void ShowNexus()
    {
        if(busy)return; LoadNexusHistory(); profilesPageVisible=false; nexusPageVisible=true; modList=null; shadePanel=null; status=null; NavigationBuild();
        var page=SettingsPage(true);var content=new C.StackPanel{Margin=new W.Thickness(0,18,0,0)};page.Children.Add(new C.ScrollViewer{Content=content,VerticalScrollBarVisibility=C.ScrollBarVisibility.Auto});
        var top=new C.StackPanel();top.Children.Add(SecondaryText(L.T("NexusTitle"),24,false,true));top.Children.Add(Text(L.T("NexusTesting"),12,true));
        top.Children.Add(Text(nexusConnecting?L.T("NexusConnecting"):nexusUser==null?L.T("NexusDisconnected"):L.T("NexusConnected",nexusUser.Name,L.T(nexusUser.IsPremium?"NexusPremium":"NexusFree")),15));
        var account=new C.WrapPanel();var key=Button(L.T("NexusConnect"),ConnectNexus,"NexusConnectButton");key.IsEnabled=!nexusConnecting;account.Children.Add(key);
        var sso=Button(L.T("NexusSso"),()=>ConnectNexusSso(),"NexusSsoButton");sso.IsEnabled=!nexusConnecting&&NexusSlug.Length>0;sso.ToolTip=L.T("NexusSsoPending");account.Children.Add(sso);
        var refresh=Button(L.T("NexusRefreshAccount"),()=>RefreshNexusAccount());refresh.IsEnabled=!nexusConnecting;account.Children.Add(refresh);
        account.Children.Add(Button(L.T("NexusDisconnect"),DisconnectNexus));top.Children.Add(account);
        if(NexusSlug.Length==0)top.Children.Add(Text(L.T("NexusSsoPending"),11,true));
        var protocolActions=new C.WrapPanel();protocolActions.Children.Add(Button(L.T("NexusRegister"),()=>{if(!Confirm(L.T("NexusRegisterConfirm")))return;new NxmProtocol(data,Environment.ProcessPath!).Register();W.MessageBox.Show(this,L.T("NexusRegistered"),"Atlas");}));
        protocolActions.Children.Add(Button(L.T("NexusRestore"),()=>{new NxmProtocol(data,Environment.ProcessPath!).Restore();W.MessageBox.Show(this,L.T("NexusRestored"),"Atlas");}));top.Children.Add(protocolActions);
        var input=new C.TextBox{Name="NxmLinkInput",MaxLength=8192};System.Windows.Automation.AutomationProperties.SetName(input,L.T("NexusPaste"));top.Children.Add(SearchField(input,L.T("NexusPaste")));
        top.Children.Add(Button(L.T("NexusAdd"),()=>{var value=input.Text.Trim();QueueNexus(NexusLink.Parse(value));input.Clear();},"NxmAddButton"));top.Children.Add(Text(L.T("NexusLinkHint"),12,true));content.Children.Add(SecondarySurface(top));
        nexusQueuePanel=new C.StackPanel();content.Children.Add(nexusQueuePanel);RefreshNexusQueue();
    }
    private void ConnectNexus()
    {
        var dialog=new W.Window{Owner=this,Title=L.T("NexusConnect"),Width=590,Height=330,Background=Brush("#15171B"),Foreground=SkinBrush("Text"),WindowStartupLocation=W.WindowStartupLocation.CenterOwner};dialog.Resources.MergedDictionaries.Add(Resources);
        var panel=new C.StackPanel{Margin=new W.Thickness(26)};panel.Children.Add(Text(L.T("NexusKey"),22));panel.Children.Add(Text(L.T("NexusKeyHelp"),13,true));
        var password=new C.PasswordBox{MaxLength=2500,Margin=new W.Thickness(0,12,0,18),Padding=new W.Thickness(10),Background=Brush("#24272E"),Foreground=SkinBrush("Text")};panel.Children.Add(password);
        var actions=new C.WrapPanel();actions.Children.Add(Button(L.T("NexusApiPage"),()=>Process.Start(new ProcessStartInfo("https://www.nexusmods.com/users/myaccount?tab=api") {UseShellExecute=true})));actions.Children.Add(Button(L.T("Cancel"),()=>dialog.DialogResult=false));actions.Children.Add(Button(L.T("NexusConnect"),()=>dialog.DialogResult=true,primary:true));panel.Children.Add(actions);dialog.Content=panel;
        if(dialog.ShowDialog()==true){var key=password.Password;password.Clear();_ = SetNexusAccount(key,true);}else password.Clear();
    }
    private async Task SetNexusAccount(string key,bool save)
    {
        if(nexusConnecting)return;var generation=++nexusAccountGeneration;nexusConnecting=true;ShowNexus();NexusApiClient? candidate=null;
        nexusSignIn?.Dispose();nexusSignIn=new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { candidate=nexusFactory(key);var user=await candidate.ValidateAsync(nexusSignIn.Token);if(nexusClosed||generation!=nexusAccountGeneration)return;if(save)nexusCredentials.Save(key);nexusClient?.Dispose();nexusClient=candidate;candidate=null;nexusUser=user; }
        catch(OperationCanceledException) { }
        catch(Exception error){if(!nexusClosed)Error(error);}
        finally{candidate?.Dispose();nexusConnecting=false;if(!nexusClosed){ShowNexus();_ = ProcessNexusQueue();}}
    }
    private async void RefreshNexusAccount()
    {
        try{var key=nexusCredentials.Load()??throw new InvalidOperationException(L.T("NexusNoKey"));await SetNexusAccount(key,false);}catch(Exception error){Error(error);}
    }
    private async void ConnectNexusSso()
    {
        if(nexusConnecting||NexusSlug.Length==0)return;var generation=++nexusAccountGeneration;nexusConnecting=true;nexusSignIn?.Dispose();nexusSignIn=new CancellationTokenSource();ShowNexus();
        try{var key=await NexusSso.ConnectAsync(NexusSlug,uri=>Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true}),nexusSignIn.Token);nexusConnecting=false;if(!nexusClosed&&generation==nexusAccountGeneration)await SetNexusAccount(key,true);}
        catch(OperationCanceledException){}catch(Exception error){if(!nexusClosed)Error(error);}finally{nexusConnecting=false;if(!nexusClosed)ShowNexus();}
    }
    private void DisconnectNexus()
    {
        nexusAccountGeneration++;nexusSignIn?.Cancel();foreach(var job in nexusJobs.Where(j=>j.State is "NexusQueued" or "NexusDownloading")){job.Cancellation?.Cancel();job.State="NexusCancelled";}
        nexusCredentials.Delete();nexusClient?.Dispose();nexusClient=null;nexusUser=null;ShowNexus();
    }
    private async Task ProcessNexusQueue()
    {
        if(nexusProcessing||nexusClosed||nexusConnecting)return;var startingGeneration=nexusAccountGeneration;nexusProcessing=true;
        try
        {
            if(nexusClient==null)
            {
                var key=nexusCredentials.Load();if(key==null)return;var generation=nexusAccountGeneration;
                var candidate=nexusFactory(key);try{var user=await candidate.ValidateAsync();if(nexusClosed||generation!=nexusAccountGeneration){candidate.Dispose();return;}nexusUser=user;nexusClient=candidate;if(nexusPageVisible)ShowNexus();}catch{candidate.Dispose();return;}
            }
            QueuePendingCollectionDownloads();
            while(!nexusClosed&&nexusClient!=null&&nexusJobs.FirstOrDefault(j=>j.State=="NexusQueued") is {} job)
            {
                job.State="NexusDownloading";job.Error=null;job.Cancellation=new CancellationTokenSource();RefreshNexusQueue();
                try
                {
                    var progress=new Progress<NexusProgress>(p=>{job.Bytes=p.Bytes;job.Total=p.TotalBytes;UpdateNexusProgress(job);});
                    job.Download=await nexusClient.DownloadAsync(job.Link,Path.Combine(data,"nexus-downloads"),progress,job.Cancellation.Token);job.State="NexusComplete";
                    // Signed NXM authorization is session-only and is discarded after a successful download.
                    job.Link=new(job.Link.GameDomain,job.Link.ModId,job.Link.FileId);SaveNexusHistory();
                    BindCollectionDownload(job);
                    await ProcessCollectionImports();
                }
                catch(OperationCanceledException){job.State="NexusCancelled";}
                catch(Exception error){job.State="NexusFailed";job.Error=error is InvalidOperationException?error.Message:L.T("NexusFailed");RecordCollectionDownloadFailure(job);}
                finally{job.Cancellation.Dispose();job.Cancellation=null;QueuePendingCollectionDownloads();RefreshNexusQueue();}
            }
        }
        finally{nexusProcessing=false;if(!nexusClosed&&startingGeneration!=nexusAccountGeneration&&nexusClient!=null&&nexusJobs.Any(j=>j.State=="NexusQueued"))_ = ProcessNexusQueue();}
    }
    private void UpdateNexusProgress(NexusJob job)
    {
        refreshCollectionTransfer?.Invoke();
        if(!nexusPageVisible||nexusClosed)return;
        if(job.Progress!=null){job.Progress.IsIndeterminate=job.Total==null;job.Progress.Value=job.Total>0?100d*job.Bytes/job.Total.Value:0;}
        if(job.Caption!=null)job.Caption.Text=L.T(job.State)+(job.State=="NexusDownloading"?" · "+L.T("NexusProgress",(job.Bytes/1048576d).ToString("0.0"),((job.Total??0)/1048576d).ToString("0.0")):"");
    }
    private void RefreshNexusQueue()
    {
        refreshCollectionTransfer?.Invoke();
        if(!nexusPageVisible||nexusQueuePanel==null||nexusClosed)return;nexusQueuePanel.Children.Clear();
        if(nexusJobs.Count==0){nexusQueuePanel.Children.Add(SecondaryEmpty(L.T("NexusTitle"),L.T("NexusQueueEmpty"),"\uE896"));return;}
        foreach(var job in nexusJobs.AsEnumerable().Reverse())
        {
            var card=new C.StackPanel();card.Children.Add(SecondaryText(job.Download?.File.Name??$"{GameCatalog.Get(NexusGames.GameId(job.Link.GameDomain)).Name} · #{job.Link.ModId} / {job.Link.FileId}",18,false,true));
            job.Caption=Text(L.T(job.State),12,true);card.Children.Add(job.Caption);
            job.Progress=new C.ProgressBar{Height=5,Minimum=0,Maximum=100,Margin=new W.Thickness(0,5,0,15),Foreground=(M.Brush)Resources["Accent"]};card.Children.Add(job.Progress);UpdateNexusProgress(job);
            if(job.Error!=null)card.Children.Add(Text(job.Error,12,true));
            var actions=new C.WrapPanel();
            if(job.State is "NexusQueued" or "NexusDownloading")actions.Children.Add(Button(L.T("NexusCancelQueue"),()=>{if(job.Cancellation!=null)job.Cancellation.Cancel();else job.State="NexusCancelled";RefreshNexusQueue();}));
            if(job.State is "NexusFailed" or "NexusCancelled")actions.Children.Add(Button(L.T("NexusRetry"),()=>{job.State="NexusQueued";job.Error=null;job.Bytes=0;RefreshNexusQueue();_ = ProcessNexusQueue();}));
            if(job.Download!=null){actions.Children.Add(Button(L.T("NexusImport"),()=>ImportNexusDownload(job)));actions.Children.Add(Button(L.T("NexusCopyPath"),()=>W.Clipboard.SetText(job.Download.Path)));}
            actions.Children.Add(Button(L.T("NexusOpenFilePage"),()=>OpenNexusFilePage(job.Link)));card.Children.Add(actions);
            nexusQueuePanel.Children.Add(SecondarySurface(card));
        }
    }
    private static void OpenNexusFilePage(NexusLink link)=>Process.Start(new ProcessStartInfo($"https://www.nexusmods.com/{link.GameDomain}/mods/{link.ModId}?tab=files&file_id={link.FileId}"){UseShellExecute=true});
    private void DownloadNexusItem(GameProfile profile,CollectionMod mod)
    {
        var source=mod.Nexus!;var link=new NexusLink(NexusGames.Domain(profile.Id),source.ModId,source.FileId);
        if(nexusUser?.IsPremium==true){QueueNexus(link);ShowNexus();}else OpenNexusFilePage(link);
    }
    private async void ImportNexusDownload(NexusJob job)
    {
        if(busy||job.Download==null)return;
        try
        {
            var profile=GameCatalog.Get(job.Download.GameId);if(!GameCatalog.IsAvailable(profile.Id))throw new InvalidOperationException(L.T("GameUnavailable"));var collections=new CollectionStore(LibraryRoot(profile),profile);
            if(new ModProfileStore(LibraryRoot(profile),profile).HasPending)throw new InvalidOperationException(L.T("ProfilePending"));
            var matches=collections.List().SelectMany(c=>c.Manifest.Mods.Where(m=>m.Nexus?.ModId==job.Link.ModId&&m.Nexus.FileId==job.Link.FileId).Select(m=>(Collection:c,Mod:m))).ToArray();
            (ImportedCollection Collection,CollectionMod Mod)? chosen=null;
            if(matches.Length>0)
            {
                var dialog=new W.Window{Owner=this,Title=L.T("NexusChooseMatch"),Width=560,Height=500,Background=Brush("#15171B"),Foreground=SkinBrush("Text"),WindowStartupLocation=W.WindowStartupLocation.CenterOwner};dialog.Resources.MergedDictionaries.Add(Resources);var choices=new C.StackPanel{Margin=new W.Thickness(25)};
                choices.Children.Add(Text(L.T("NexusChooseMatch"),20));foreach(var match in matches)choices.Children.Add(Button(match.Collection.Manifest.Name+" · "+match.Mod.Name,()=>{chosen=match;dialog.DialogResult=true;},"NexusMatch_"+match.Mod.Id));choices.Children.Add(Button(L.T("Cancel"),()=>dialog.DialogResult=false));dialog.Content=new C.ScrollViewer{Content=choices};if(dialog.ShowDialog()!=true)return;
            }
            if(chosen is {} selected)
            {
                busy=true;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=false;
                await Task.Run(()=>collections.AttachZip(selected.Collection.Manifest.Id,selected.Mod.Id,job.Download.Path,new ModLibrary(LibraryRoot(profile),profile)));
                busy=false;OpenGame(profile);ShowCollections(selected.Collection.Manifest.Id);
            }
            else{OpenGame(profile);await ImportZipCore(job.Download.Path,false);}
        }
        catch(Exception error){Error(error);}
        finally{busy=false;PageHost.IsEnabled=Navigation.IsEnabled=BottomNavigation.IsEnabled=true;}
    }
    private void LoadNexusHistory()
    {
        if(nexusHistoryLoaded)return;nexusHistoryLoaded=true;var path=Path.Combine(data,"nexus-history.json");
        try{if(!File.Exists(path)||new FileInfo(path).Length>1024*1024)return;foreach(var entry in (JsonSerializer.Deserialize<NexusHistory[]>(File.ReadAllText(path))??[]).Take(200))
        {if(entry?.File==null||entry.Archive==null||entry.File.FileId!=entry.FileId)continue;var domain=NexusGames.Domain(entry.GameId);if(!Guid.TryParseExact(Path.GetFileNameWithoutExtension(entry.Archive),"N",out _)||Path.GetFileName(entry.Archive)!=entry.Archive||!ModArchives.IsSupported(entry.Archive)||entry.ModId<=0||entry.FileId<=0)continue;var archive=Path.Combine(data,"nexus-downloads",entry.Archive);if(File.Exists(archive))nexusJobs.Add(new(new(domain,entry.ModId,entry.FileId)){Download=new(archive,entry.File,entry.GameId),State="NexusComplete"});}}
        catch(Exception e)when(e is IOException or JsonException or InvalidOperationException or ArgumentException){/* Invalid local history must not prevent use of the library. */}
    }
    private void SaveNexusHistory()
    {
        var records=nexusJobs.Where(j=>j.Download!=null).TakeLast(200).Select(j=>new NexusHistory(j.Download!.GameId,j.Link.ModId,j.Link.FileId,j.Download.File,Path.GetFileName(j.Download.Path))).ToArray();var path=Path.Combine(data,"nexus-history.json");
        var temp=path+".tmp";using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(stream,records);stream.Flush(true);}File.Move(temp,path,true);
    }
}
