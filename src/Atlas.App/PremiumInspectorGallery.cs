using Atlas.Core;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;
using I = System.Windows.Media.Imaging;

namespace Atlas.App;

public partial class PremiumWindow
{
    private void AddInspectorGallery(ModDetails info, ModDetailsStore metadata)
    {
        var images = new List<M.ImageSource>();
        var keys = new HashSet<string>();
        void Add(M.ImageSource? source)
        {
            if (source is not I.BitmapSource bitmap || bitmap.PixelWidth > 4096 || bitmap.PixelHeight > 4096) return;
            var scaled = new I.TransformedBitmap(bitmap, new M.ScaleTransform(32d / bitmap.PixelWidth, 32d / bitmap.PixelHeight));
            var pixels = new I.FormatConvertedBitmap(scaled, M.PixelFormats.Bgra32, null, 0);
            var bytes = new byte[pixels.PixelWidth * pixels.PixelHeight * 4];
            pixels.CopyPixels(bytes, pixels.PixelWidth * 4, 0);
            if (keys.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)))) images.Add(source);
        }

        if (game is {} profile && LibraryNexusId(profile, info) is {} modId) Add(CachedNexusCover(profile, modId));
        Add(ModImage(metadata, info.Cover, 900));
        foreach (var path in info.Images) Add(ModImage(metadata, path, 900));
        if (images.Count == 0) return;

        var frame = new C.Border
        {
            Height = 192, Background = SkinBrush("Base"), CornerRadius = new W.CornerRadius(9),
            BorderBrush = SkinBrush("Stroke"), BorderThickness = new W.Thickness(1),
            Margin = new W.Thickness(0, 0, 0, 16)
        };
        var artwork = new C.Grid();
        artwork.SizeChanged += (_, _) => artwork.Clip = new M.RectangleGeometry(
            new W.Rect(0, 0, artwork.ActualWidth, artwork.ActualHeight), 8, 8);
        frame.Child = artwork;
        inspectorPanel!.Children.Add(frame);

        var current = 0;
        var count = SecondaryText("", 11, false, true);
        count.Foreground = M.Brushes.White;
        count.Margin = new W.Thickness(0);
        void Render()
        {
            artwork.Background = new M.ImageBrush(images[current]) { Stretch = M.Stretch.UniformToFill };
            count.Text = $"{current + 1} / {images.Count}";
        }
        Render();
        if (images.Count == 1) return;

        C.Button Arrow(bool backwards)
        {
            var button = Button("", () =>
            {
                current = (current + (backwards ? images.Count - 1 : 1)) % images.Count;
                Render();
            });
            button.Width = button.Height = 32;
            button.Padding = new W.Thickness(0);
            button.Margin = new W.Thickness(8, 0, 8, 0);
            button.HorizontalAlignment = backwards ? W.HorizontalAlignment.Left : W.HorizontalAlignment.Right;
            button.VerticalAlignment = W.VerticalAlignment.Center;
            button.Background = Brush("#DC11151B");
            button.BorderBrush = Brush("#6695A4B6");
            button.BorderThickness = new W.Thickness(1);
            button.Content = IndicatorDrawing(backwards ? "M 10,2 L 4,8 L 10,14" : "M 5,2 L 11,8 L 5,14", M.Brushes.White, 11);
            button.ToolTip = L.T(backwards ? "ModPrevious" : "ModNext");
            System.Windows.Automation.AutomationProperties.SetName(button, (string)button.ToolTip);
            return button;
        }
        artwork.Children.Add(Arrow(true));
        artwork.Children.Add(Arrow(false));
        artwork.Children.Add(new C.Border
        {
            Child = count, Background = Brush("#DC11151B"), CornerRadius = new W.CornerRadius(8),
            HorizontalAlignment = W.HorizontalAlignment.Center, VerticalAlignment = W.VerticalAlignment.Bottom,
            Padding = new W.Thickness(9, 4, 9, 4), Margin = new W.Thickness(0, 0, 0, 9)
        });
    }
}
