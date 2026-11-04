using Atlas.Core;
using W = System.Windows;
using C = System.Windows.Controls;

namespace Atlas.App;

public partial class PremiumWindow
{
    private C.Button? sidebarDownloadButton;
    private C.TextBlock? sidebarDownloadTitle;
    private C.TextBlock? sidebarDownloadFile;
    private C.ProgressBar? sidebarDownloadProgress;

    private void BuildSidebarDownloads(C.Panel host)
    {
        sidebarDownloadButton = Button("", ShowNexus, "SidebarDownloadsButton");
        sidebarDownloadButton.Margin = IsCinematic ? new W.Thickness(0, 0, 8, 8) : new W.Thickness(12, 0, 12, 10);
        var content = new C.StackPanel { Width = IsCinematic ? 142 : 154 };
        sidebarDownloadTitle = SecondaryText("", 12, false, true);
        sidebarDownloadFile = SecondaryText("", 11, true);
        sidebarDownloadFile.TextWrapping = W.TextWrapping.NoWrap;
        sidebarDownloadFile.TextTrimming = W.TextTrimming.CharacterEllipsis;
        sidebarDownloadProgress = new C.ProgressBar { Height = 4, Maximum = 100, Foreground = SkinBrush("Accent") };
        content.Children.Add(sidebarDownloadTitle);
        content.Children.Add(sidebarDownloadFile);
        content.Children.Add(sidebarDownloadProgress);
        sidebarDownloadButton.Content = content;
        host.Children.Add(sidebarDownloadButton);
        RefreshSidebarDownloads();
    }

    private void RefreshSidebarDownloads()
    {
        if (sidebarDownloadButton == null || nexusClosed) return;
        var job = nexusJobs.FirstOrDefault(item => item.State == "NexusDownloading");
        var queued = nexusJobs.Count(item => item.State == "NexusQueued");
        sidebarDownloadButton.Visibility = job != null || queued > 0 ? W.Visibility.Visible : W.Visibility.Collapsed;
        sidebarDownloadTitle!.Text = L.T("SidebarDownloads", queued);
        var percent = job?.Total > 0 ? Math.Clamp(100d * job.Bytes / job.Total.Value, 0, 100) : (double?)null;
        var file = job == null ? "" : collectionTransfer?.Items.FirstOrDefault(item => ReferenceEquals(item.Job, job))?.Item.Mod.Name
            ?? $"#{job.Link.ModId} / {job.Link.FileId}";
        sidebarDownloadFile!.Text = percent is double progress ? $"{progress:0}% · {file}" : file;
        sidebarDownloadButton.ToolTip = sidebarDownloadFile.Text;
        sidebarDownloadProgress!.IsIndeterminate = percent == null;
        sidebarDownloadProgress.Value = percent ?? 0;
    }
}
