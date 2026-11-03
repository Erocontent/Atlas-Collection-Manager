using Atlas.Core;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;

namespace Atlas.App;

public partial class PremiumWindow
{
    private bool IsCinematic => preferences.LauncherSkin == "cinematic";

    private static M.Color ShellColor(string value) => (M.Color)M.ColorConverter.ConvertFromString(value);
    private static M.Color BlendShell(M.Color background, M.Color foreground, double amount) => M.Color.FromRgb(
        (byte)Math.Round(background.R + (foreground.R - background.R) * amount),
        (byte)Math.Round(background.G + (foreground.G - background.G) * amount),
        (byte)Math.Round(background.B + (foreground.B - background.B) * amount));
    private static M.Brush SolidShell(M.Color color) { var brush = new M.SolidColorBrush(color); brush.Freeze(); return brush; }
    private M.Brush SkinBrush(string resource) => (M.Brush)Resources[resource];

    private void ApplySkinShell()
    {
        RefreshCaptionControls();
        CinematicNavigation.SetBinding(IsEnabledProperty,new W.Data.Binding("IsEnabled"){Source=Navigation});CinematicUtilities.SetBinding(IsEnabledProperty,new W.Data.Binding("IsEnabled"){Source=BottomNavigation});
        var palette = LauncherSkins.Palette(preferences);
        if(!LauncherSkins.IsReadable(palette))palette=LauncherSkins.Defaults(preferences.LauncherSkin);
        var background = ShellColor(palette.Background);
        var text = ShellColor(palette.Text);
        var accent = ShellColor(palette.Accent);
        Resources["Base"] = SolidShell(background);
        Resources["Surface"] = SolidShell(BlendShell(background, text, IsCinematic?.045:.018));
        Resources["Elevated"] = SolidShell(BlendShell(background, text, .085));
        Resources["Text"] = SolidShell(LauncherSkins.Contrast("#FFFFFF", palette.Background) >= 4.5 ? M.Colors.White : text);
        Resources["Muted"] = SolidShell(LauncherSkins.Contrast("#28D7E8", palette.Background) >= 4.5 ? ShellColor("#28D7E8") : text);
        Resources["Stroke"] = SolidShell(BlendShell(background, text, .16));
        Resources["Accent"] = SolidShell(accent);
        Resources["AccentText"] = SolidShell(LauncherSkins.Contrast(palette.Accent,"#000000") >= LauncherSkins.Contrast(palette.Accent,"#FFFFFF") ? M.Colors.Black : M.Colors.White);
        Background = SkinBrush("Base");
        Foreground = SkinBrush("Text");
        RootShell.Background = SkinBrush("Base");
        OuterFrame.Background = SkinBrush("Base");
        RootShell.ColumnDefinitions[0].Width = new W.GridLength(IsCinematic ? 0 : 222);
        RootShell.RowDefinitions[0].Height = new W.GridLength(IsCinematic ? 64 : 104);
        SidebarSurface.Visibility = SidebarDock.Visibility = IsCinematic ? W.Visibility.Collapsed : W.Visibility.Visible;
        CinematicHeader.Visibility = IsCinematic ? W.Visibility.Visible : W.Visibility.Collapsed;
        C.Grid.SetColumn(ShellBrand, IsCinematic ? 1 : 0);
        ShellBrand.VerticalAlignment = W.VerticalAlignment.Center;
        ShellBrand.Margin = IsCinematic ? new W.Thickness(22, 0, 0, 0) : new W.Thickness(0);
        ShellBrand.HorizontalAlignment = IsCinematic ? W.HorizontalAlignment.Left : W.HorizontalAlignment.Center;
        ShellBrand.TextAlignment = W.TextAlignment.Center;
        ShellBrand.FontSize = IsCinematic?17:21;
        ShellBrand.Inlines.Clear();
        if(IsCinematic)ShellBrand.Inlines.Add(new W.Documents.Run("▲ "){Foreground=SkinBrush("Accent"),FontSize=20});
        else {var mark=new System.Windows.Shapes.Path{Data=M.Geometry.Parse("M0,42 L24,0 L48,42 L37,42 L24,19 L11,42 Z M17,42 L24,29 L31,42 Z"),Fill=SkinBrush("Accent"),Stretch=M.Stretch.Uniform};ShellBrand.Inlines.Add(new W.Documents.InlineUIContainer(new C.Viewbox{Width=40,Height=38,Child=mark,Margin=new W.Thickness(0,0,10,-4)}));}
        ShellBrand.Inlines.Add(new W.Documents.Run(IsCinematic?"ATLAS":"A T L A S"));
        if(!IsCinematic){ShellBrand.Inlines.Add(new W.Documents.LineBreak());ShellBrand.Inlines.Add(new W.Documents.InlineUIContainer(new C.Viewbox { Width = 140, Height = 19, Stretch = M.Stretch.Uniform, Child = new C.TextBlock { Text = "Collection Manager", FontSize = 12, FontWeight = W.FontWeights.Normal, Foreground = SkinBrush("Muted") } }));}
        C.Grid.SetRow(PageHost,IsCinematic?1:0);C.Grid.SetRowSpan(PageHost,IsCinematic?1:2);
        SidebarDock.Margin=IsCinematic?new W.Thickness(18,16,18,24):new W.Thickness(0,8,0,24);
        PageHost.Margin = IsCinematic ? new W.Thickness(36, 24, 36, 24) : new W.Thickness(0);
    }

    // Legacy view builders share these semantic brushes; palette parsing above never calls this resolver.
    private M.Brush ResolveSkinBrush(string legacyColor)
    {
        var key = legacyColor.ToUpperInvariant() switch
        {
            "#101114" or "#111215" => "Base",
            "#15171B" or "#1B1E23" or "#1B1E24" or "#1C1F25" => "Surface",
            "#20232B" or "#22252B" or "#24272E" or "#252830" => "Elevated",
            "#282B31" or "#282B33" or "#26292E" or "#2B2D32" or "#34383F" or "#353940" => "Stroke",
            "#F0F1F2" or "#E9EBEE" or "#C5C8CE" or "#CBD0D7" or "#C1C5CB" or "WHITE" => "Text",
            "#92979E" or "#A2A7AF" => "Muted",
            "#A78BFA" or "#84CFAE" or "#EAC47F" or "#F19A9A" => "Accent",
            _ => null
        };
        return key != null ? SkinBrush(key) : (M.Brush)new M.BrushConverter().ConvertFromString(legacyColor)!;
    }
}
