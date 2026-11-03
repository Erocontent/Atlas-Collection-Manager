using Atlas.Core;
using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;

namespace Atlas.App;
public partial class PremiumWindow
{
    private C.StackPanel? inspectorPanel;
    private C.ComboBox? categoryDropdown;
    private M.ImageSource? GameHero(GameProfile profile)
    {
        if(preferences.Covers.ContainsKey(profile.Id))return Cover(profile);
        using var stream=typeof(PremiumWindow).Assembly.GetManifestResourceStream($"Atlas.App.Assets.Hero.{profile.Id}.jpg");return stream==null?Cover(profile):LoadImage(stream);
    }
    private void RefreshSkinInspector()
    {
        if(inspectorPanel==null||library==null)return;inspectorPanel.Children.Clear();var mod=Selected;
        if(mod==null){inspectorPanel.Children.Add(Text(L.T("SkinInspectHint"),16,true));return;}
        var active=library.GetActive().Any(a=>a.Id==mod.Id&&string.Equals(a.Game,GamePath,StringComparison.OrdinalIgnoreCase));
        var name = SecondaryText(mod.Name, 25, false, true);
        name.LineHeight = 31;
        name.Margin = new W.Thickness(0, 0, 0, 8);
        inspectorPanel.Children.Add(name);
        var badge = CollectionBadge(SecondaryText(L.T(active ? "Enabled" : "Disabled"), 11), active);
        badge.HorizontalAlignment = W.HorizontalAlignment.Left;
        badge.Margin = new W.Thickness(0, 0, 0, 14);
        inspectorPanel.Children.Add(badge);
        var metadata = CurrentModDetails();
        var info = metadata.Get(mod);
        var image = LibraryCover(metadata, info, 900);
        if (image != null)
        {
            var cover = new C.Image { Source = image, MaxHeight = 220, Stretch = M.Stretch.Uniform, Margin = new W.Thickness(0, 0, 0, 12) };
            inspectorPanel.Children.Add(cover);
        }
        var galleryActions=new C.WrapPanel();
        var galleryButton=Button(L.T("ModPreview"),()=>ShowModGallery(mod),"InspectorGalleryButton");galleryButton.IsEnabled=info.Images.Length>0;galleryActions.Children.Add(galleryButton);
        var changeCover=Button(L.T("ModCover"),()=>AddModImage(mod,true,true),"InspectorCoverButton");changeCover.IsEnabled=!CurrentProfiles().HasPending;galleryActions.Children.Add(changeCover);inspectorPanel.Children.Add(galleryActions);
        var thumbs=new C.WrapPanel{Margin=new W.Thickness(0,0,0,16)};
        foreach(var relative in info.Images.Take(4))
        {
            var thumb=ModImage(metadata,relative,150);if(thumb==null)continue;
            var button=Button("",()=>ShowModGallery(mod,relative));button.Content=new C.Image{Source=thumb,Width=60,Height=48,Stretch=M.Stretch.UniformToFill};button.Padding=new W.Thickness(2);button.Margin=new W.Thickness(0,0,8,0);button.ToolTip=L.T("ModPreview");thumbs.Children.Add(button);
        }
        if(thumbs.Children.Count>0)inspectorPanel.Children.Add(thumbs);
        var facts = new C.StackPanel();
        void Row(string label,string value) {
            var row=new C.Grid{Margin=new W.Thickness(0,0,0,6)};row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=W.GridLength.Auto});
            var key=SecondaryText(label.ToUpperInvariant(),10,true);key.Margin=new W.Thickness(0);row.Children.Add(key);
            var detail=SecondaryText(value,13);detail.Foreground=SkinBrush("Accent");detail.FontWeight=W.FontWeights.SemiBold;detail.Margin=new W.Thickness(12,0,0,0);detail.TextAlignment=W.TextAlignment.Right;detail.MaxWidth=170;row.Children.Add(detail.AtColumn(1));facts.Children.Add(row);
        }
        if(game?.Id=="doa5"&&AutoLinkCharacters.ForPackage(mod) is {Length:>0} characters)Row(L.T("CharacterLabel"),string.Join(", ",characters));
        Row(L.T("CategoryColumn"),info.Category);Row(L.T("ModVersion"),info.Version.Length>0?info.Version:L.T("ModUnknown"));
        inspectorPanel.Children.Add(SecondarySurface(facts, new W.Thickness(12, 10, 12, 4)));
        if (info.Description.Length > 0)
        {
            var description = new C.StackPanel();
            description.Children.Add(SecondaryText(L.T("ModDescription"), 15, false, true));
            description.Children.Add(SecondaryText(info.Description, 13));
            inspectorPanel.Children.Add(SecondarySurface(description, new W.Thickness(12)));
        }
        if(info.Dependencies.Length>0)inspectorPanel.Children.Add(SecondaryText(L.T("ModDependencies")+": "+string.Join(", ",info.Dependencies),12,true));
        var visual=modVisualStatusSnapshot?.GetValueOrDefault(mod.Id);AddModIndicators(inspectorPanel,visual);
        inspectorPanel.Children.Add(new C.Border { Height = 1, Background = SkinBrush("Stroke"), Margin = new W.Thickness(0, 8, 0, 10) });
        var conflicts=Button(L.T("ModShowConflicts"),()=>ShowModConflicts(mod),"InspectorConflictsButton");conflicts.Margin=new W.Thickness(0,10,0,4);inspectorPanel.Children.Add(conflicts);

        var toggle=Button((active?"⊘  ":"✓  ")+L.T(active?"Disable":"Enable"),()=>ReviewBatch(active?ModBatchAction.Disable:ModBatchAction.Enable),"InspectorToggleButton",!active);
        toggle.Margin=new W.Thickness(0,18,0,10);toggle.Padding=new W.Thickness(16,14,16,14);toggle.IsEnabled=!CurrentProfiles().HasPending;inspectorPanel.Children.Add(toggle);
        var more=Button("↗  "+L.T("ModPage"),()=>ShowModPage(mod),"InspectorDetailsButton");more.BorderBrush=SkinBrush("Stroke");more.BorderThickness=new W.Thickness(1);more.Background=M.Brushes.Transparent;more.Margin=new W.Thickness(0);inspectorPanel.Children.Add(more);
    }
    private void ShowSkinReShade()
    {
        var dialog=ProfileDialog(L.T("SkinReShade"),520,620);var previous=shadePanel;var panel=new C.StackPanel{Margin=new W.Thickness(26)};shadePanel=panel;RefreshShade();dialog.Content=new C.ScrollViewer{Content=panel,VerticalScrollBarVisibility=C.ScrollBarVisibility.Auto};try{dialog.ShowDialog();}finally{shadePanel=previous;}
    }
}
