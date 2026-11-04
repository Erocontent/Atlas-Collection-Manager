using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;
using S = System.Windows.Shapes;

namespace Atlas.App;

public partial class PremiumWindow
{
    private W.FrameworkElement BuildAtlasBrand()
    {
        var cyan = Brush("#28D7E8");
        var white = M.Brushes.White;
        var mountain = new C.Canvas { Width = 100, Height = 55 };
        void Peak(string geometry, M.Brush fill) => mountain.Children.Add(new S.Path { Data = M.Geometry.Parse(geometry), Fill = fill });
        Peak("M 0,55 L 28,21 L 26,38 L 49,0 L 55,21 L 42,33 L 40,26 L 21,48 L 25,34 Z", white);
        Peak("M 0,55 L 28,21 L 26,38 Z M 49,0 L 72,33 L 70,21 L 100,55 L 75,40 L 78,45 L 54,21 Z", cyan);
        var mark = new C.Viewbox
        {
            Width = IsCinematic ? 32 : 67, Height = IsCinematic ? 21 : 37, Child = mountain,
            HorizontalAlignment = W.HorizontalAlignment.Center
        };
        var wordmark = new C.Viewbox
        {
            Width = IsCinematic ? 61 : 115, Height = IsCinematic ? 16 : 24,
            HorizontalAlignment = W.HorizontalAlignment.Center,
            Child = new S.Path
            {
                Fill = white,
                Data = M.Geometry.Parse("M0,24 L14,0 H22 L36,24 H27 L18,7 L9,24 Z M30,0 H60 L56,6 H48 V24 H40 V6 H26 Z M62,0 H70 V18 H88 L85,24 H62 Z M87,24 L101,0 H109 L123,24 H114 L105,7 L96,24 Z M145,0 L141,6 H130 Q128,6 128,8 Q128,9 130,9 H138 Q146,9 146,16 Q146,24 138,24 H122 L126,18 H137 Q139,18 139,16 Q139,15 137,15 H129 Q121,15 121,8 Q121,0 130,0 Z")
            }
        };
        var brand = new C.StackPanel
        {
            Orientation = IsCinematic ? C.Orientation.Horizontal : C.Orientation.Vertical,
            HorizontalAlignment = W.HorizontalAlignment.Center
        };
        mark.Margin = IsCinematic ? new W.Thickness(0, 0, 7, 0) : new W.Thickness(0, 0, 0, 4);
        brand.Children.Add(mark);
        brand.Children.Add(wordmark);
        if (!IsCinematic)
            brand.Children.Add(new C.TextBlock
            {
                Text = "Collection Manager", Foreground = cyan, FontSize = 12,
                FontWeight = W.FontWeights.SemiBold, TextAlignment = W.TextAlignment.Center,
                Margin = new W.Thickness(0, 4, 0, 0)
            });
        return brand;
    }
}
