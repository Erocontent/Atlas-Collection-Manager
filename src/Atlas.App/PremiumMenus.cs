using W=System.Windows;
using C=System.Windows.Controls;
using P=System.Windows.Controls.Primitives;

namespace Atlas.App;
public partial class PremiumWindow
{
    private void OpenThemedMenu(C.ContextMenu menu,W.FrameworkElement anchor)
    {
        menu.Style=(W.Style)FindResource(typeof(C.ContextMenu));
        if(!menu.Resources.MergedDictionaries.Contains(Resources))menu.Resources.MergedDictionaries.Add(Resources);
        foreach(var item in menu.Items.OfType<C.MenuItem>())item.Style=(W.Style)FindResource(typeof(C.MenuItem));
        menu.PlacementTarget=anchor;menu.Placement=P.PlacementMode.Bottom;menu.HorizontalOffset=0;menu.VerticalOffset=6;
        anchor.ContextMenu=menu;menu.IsOpen=true;
    }
}
