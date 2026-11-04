using W = System.Windows;
using C = System.Windows.Controls;

namespace Atlas.App;

public partial class PremiumWindow
{
    private C.StackPanel? collectionApplyPanel;
    private C.TextBlock? collectionApplyTitle;
    private C.TextBlock? collectionApplyMod;
    private C.ProgressBar? collectionApplyBar;
    private string? collectionApplyText, collectionApplyModName;
    private double? collectionApplyPercent;

    private void BuildCollectionApplyPanel(C.Panel host)
    {
        collectionApplyPanel = new C.StackPanel
        {
            Width = IsCinematic ? 145 : 180,
            Margin = new W.Thickness(12, 0, 12, 10)
        };
        collectionApplyTitle = SecondaryText("", 12, false, true);
        collectionApplyMod = SecondaryText("", 11, true);
        collectionApplyMod.TextWrapping = W.TextWrapping.NoWrap;
        collectionApplyMod.TextTrimming = W.TextTrimming.CharacterEllipsis;
        collectionApplyBar = new C.ProgressBar { Height = 4, Maximum = 100, Foreground = SkinBrush("Accent") };
        collectionApplyPanel.Children.Add(collectionApplyTitle);
        collectionApplyPanel.Children.Add(collectionApplyMod);
        collectionApplyPanel.Children.Add(collectionApplyBar);
        host.Children.Add(collectionApplyPanel);
        ShowCollectionApplyProgress(collectionApplyText, collectionApplyPercent, collectionApplyModName);
    }

    private void ShowCollectionApplyProgress(string? text, double? percent, string? mod = null)
    {
        collectionApplyText = text;
        collectionApplyPercent = percent;
        collectionApplyModName = mod;
        if (collectionApplyPanel == null) return;
        collectionApplyPanel.Visibility = text == null ? W.Visibility.Collapsed : W.Visibility.Visible;
        collectionApplyTitle!.Text = text ?? "";
        collectionApplyMod!.Text = mod ?? "";
        collectionApplyMod.Visibility = mod == null ? W.Visibility.Collapsed : W.Visibility.Visible;
        collectionApplyBar!.Visibility = text == Atlas.Core.L.T("OperationFailed") ? W.Visibility.Collapsed : W.Visibility.Visible;
        collectionApplyBar.IsIndeterminate = percent == null;
        collectionApplyBar.Value = percent ?? 0;
    }
}
