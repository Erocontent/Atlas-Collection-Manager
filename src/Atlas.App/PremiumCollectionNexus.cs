using System.IO;
using System.Net.Http;
using System.Text.Json;
using Atlas.Core;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;

namespace Atlas.App;

public partial class PremiumWindow
{
    private bool nexusCollectionChecking;

    private string NexusCoverPath(GameProfile profile, long modId) =>
        Path.Combine(data, "nexus-covers", NexusGames.Domain(profile.Id), modId + ".png");

    private M.ImageSource? CachedNexusCover(GameProfile profile, CollectionMod mod)
    {
        return mod.Nexus is {} source ? CachedNexusCover(profile, source.ModId) : null;
    }

    private M.ImageSource? CachedNexusCover(GameProfile profile, long modId)
    {
        try
        {
            var path = NexusCoverPath(profile, modId);
            return File.Exists(path) && new FileInfo(path).Length <= 16 * 1024 * 1024 ? LoadImage(path) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException)
        {
            return null;
        }
    }

    private async Task CacheNexusCoverAsync(NexusApiClient client, GameProfile profile, long modId, CancellationToken token)
    {
        var path = NexusCoverPath(profile, modId);
        if (CachedNexusCover(profile, modId) != null) return;
        var bytes = await client.GetModCoverAsync(NexusGames.Domain(profile.Id), modId, token);
        if (bytes == null) return;
        var image = await Task.Run(() => NormalizeNexusCover(bytes), token);
        if (image.Length > 16 * 1024 * 1024) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, image, token);
            File.Move(temp, path, true);
        }
        finally { File.Delete(temp); }
    }

    private static byte[] NormalizeNexusCover(byte[] bytes)
    {
        using var input = new MemoryStream(bytes, writable: false);
        var image = new System.Windows.Media.Imaging.BitmapImage();
        image.BeginInit();
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 960;
        image.StreamSource = input;
        image.EndInit();
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var output = new MemoryStream();
        png.Save(output);
        return output.ToArray();
    }

    private async void SyncCollectionNexus(GameProfile profile, string id, bool showUpdates, C.Button? trigger = null)
    {
        if (busy || nexusCollectionChecking) return;
        var key = nexusCredentials.Load();
        if (key == null)
        {
            if (showUpdates) ShowNexus();
            return;
        }

        var collection = new CollectionStore(LibraryRoot(profile), profile).Find(id);
        if (collection == null) return;
        nexusCollectionChecking = true;
        if (trigger != null) { trigger.IsEnabled = false; trigger.Content = L.T("Processing"); }
        var report = new C.StackPanel();
        W.Window? reviewDialog = null;
        var checks = new Dictionary<(long Mod, long File), NexusUpdateCheck>();
        var covers = new HashSet<long>();
        using var token = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var client = nexusFactory(key);
        try
        {
            foreach (var mod in collection.Manifest.Mods)
            {
                if (nexusClosed || token.IsCancellationRequested) break;
                if (mod.Nexus is not {} source)
                {
                    if (showUpdates) report.Children.Add(SecondaryText(mod.Name + " · " + L.T("NexusCollectionUnlinked"), 13, true));
                    continue;
                }

                var link = new NexusLink(NexusGames.Domain(profile.Id), source.ModId, source.FileId);
                if (covers.Add(source.ModId))
                {
                    try { await CacheNexusCoverAsync(client, profile, source.ModId, token.Token); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or ArgumentException or NotSupportedException or JsonException or OperationCanceledException or OutOfMemoryException) { }
                }

                if (!showUpdates) continue;
                var card = new C.StackPanel();
                card.Children.Add(SecondaryText(mod.Name, 16, false, true));
                var installed = collection.Entries.FirstOrDefault(e => e.ModId == mod.Id)?.PackageVersion;
                card.Children.Add(SecondaryText(L.T("NexusCollectionVersions", installed ?? "—", mod.Version), 12, true));
                try
                {
                    if (!checks.TryGetValue((source.ModId, source.FileId), out var result))
                        checks[(source.ModId, source.FileId)] = result = await client.CheckUpdateAsync(link, token.Token);
                    if (result.Update is {} update)
                    {
                        card.Children.Add(SecondaryText(update.Name + " · " + update.Version, 14));
                        var actions = new C.WrapPanel();
                        actions.Children.Add(Button(L.T("NexusOpenFilePage"), () => OpenNexusFilePage(new NexusLink(link.GameDomain, link.ModId, update.FileId))));
                        actions.Children.Add(Button(L.T("NexusCollectionSelectUpdate"), () =>
                        {
                            EnsureProfileReady();
                            if (collectionPreparing || collectionImporting || CollectionDownloadsRunning(profile.Id, id))
                                throw new InvalidOperationException(L.T("CollectionTransferChanged"));
                            if (W.MessageBox.Show(reviewDialog ?? this, L.T("NexusCollectionConfirmUpdate", update.Name, update.Version),
                                L.T("NexusCollectionCheck"), W.MessageBoxButton.YesNo, W.MessageBoxImage.Question) != W.MessageBoxResult.Yes) return;
                            if (busy || collectionPreparing || collectionImporting || CollectionDownloadsRunning(profile.Id, id)) return;
                            new CollectionStore(LibraryRoot(profile), profile).SelectNexusUpdate(id, mod, update);
                            if (collectionTransfer is {} session && session.Profile.Id == profile.Id && session.Id == id)
                                CancelCollectionTransfer();
                            reviewDialog?.Close();
                            ShowCollections(id);
                        }));
                        card.Children.Add(actions);
                    }
                    else
                    {
                        card.Children.Add(SecondaryText(L.T(result.ReviewNeeded ? "NexusCollectionReview" : "NexusCollectionNoUpdate"), 13, true));
                        if (result.ReviewNeeded) card.Children.Add(Button(L.T("NexusOpenFilePage"), () => OpenNexusFilePage(link)));
                    }
                }
                catch (Exception error) when (error is InvalidOperationException or HttpRequestException or JsonException or KeyNotFoundException or OperationCanceledException or FormatException)
                {
                    card.Children.Add(SecondaryText(L.T("NexusFailed"), 13, true));
                }
                report.Children.Add(SecondarySurface(card));
            }

            if (nexusClosed || busy || game?.Id != profile.Id || commandSection != "collections" || visibleCollectionId != id) return;
            ShowCollections(id);
            if (!showUpdates) return;
            if (token.IsCancellationRequested) report.Children.Add(SecondaryText(L.T("NexusCollectionIncomplete"), 13, true));
            var dialog = ProfileDialog(L.T("NexusCollectionCheck"), 760, 680);
            reviewDialog = dialog;
            var layout = new C.DockPanel { Margin = new W.Thickness(20) };
            var close = Button(L.T("ModClose"), () => dialog.Close());
            C.DockPanel.SetDock(close, C.Dock.Bottom);
            layout.Children.Add(close);
            layout.Children.Add(new C.ScrollViewer { Content = report, VerticalScrollBarVisibility = C.ScrollBarVisibility.Auto });
            dialog.Content = layout;
            dialog.ShowDialog();
        }
        catch (Exception error) { if (!nexusClosed && showUpdates) Error(error); }
        finally
        {
            nexusCollectionChecking = false;
            if (trigger != null) { trigger.IsEnabled = true; trigger.Content = L.T("NexusCollectionCheck"); }
        }
    }
}
