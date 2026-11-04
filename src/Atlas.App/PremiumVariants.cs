using Atlas.Core;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;

namespace Atlas.App;

public partial class PremiumWindow
{
    private void ChooseModVariant(ModPackage mod)
    {
        EnsureProfileReady();
        var lib = library!;
        var choices = lib.Variants(mod.Id);
        var selected = lib.SelectedVariant(mod.Id);
        var dialog = ProfileDialog(L.T("ModVariants"), 540, 500);
        var panel = new C.StackPanel { Margin = new W.Thickness(24) };
        panel.Children.Add(SecondaryText(mod.Name, 21, false, true));
        var picker = new C.ComboBox { ItemsSource = choices.Select(choice => choice.Name), SelectedIndex = selected };
        panel.Children.Add(picker);
        var preview = new C.Image { Height = 260, Stretch = M.Stretch.Uniform, Margin = new W.Thickness(0, 12, 0, 12) };
        panel.Children.Add(preview);
        void RefreshPreview()
        {
            preview.Source = null;
            if (picker.SelectedIndex < 0) return;
            try
            {
                if (lib.VariantPreview(mod.Id, picker.SelectedIndex) is {} bytes)
                {
                    using var stream = new MemoryStream(bytes, writable: false);
                    preview.Source = LoadImage(stream);
                }
            }
            catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException or FormatException) { }
        }
        picker.SelectionChanged += (_, _) => RefreshPreview();
        RefreshPreview();
        panel.Children.Add(Button(L.T("ModVariants"), () =>
        {
            if (picker.SelectedIndex < 0) return;
            dialog.DialogResult = true;
        }, primary: true));
        dialog.Content = panel;
        if (dialog.ShowDialog() != true || selected == picker.SelectedIndex) return;
        var index = picker.SelectedIndex;
        var folder = GamePath;
        if (lib.GetActive().Any(item => item.Id == mod.Id) && !Confirm(L.T("ModVariantConfirm", choices[index].Name))) return;
        Run(() => lib.SelectVariant(mod.Id, index, folder), choices[index].Name);
    }
}
