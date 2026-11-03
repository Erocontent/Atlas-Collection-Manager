using Atlas.Core;
using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;

namespace Atlas.App;

// Shared visual language for secondary pages: display headings, body copy and semantic surfaces.
public partial class PremiumWindow
{
    private C.TextBlock SecondaryText(string value,double size=14,bool muted=false,bool display=false)
    {
        var text=Text(value,size,muted);
        text.FontFamily=new M.FontFamily(display?"Bahnschrift, Segoe UI Variable Display, Segoe UI":"Segoe UI Variable Text, Segoe UI");
        text.FontWeight=display?W.FontWeights.SemiBold:W.FontWeights.Normal;
        text.LineHeight=size*1.5;
        return text;
    }
    private C.Border SecondaryIcon(string glyph,double size=48)
    {
        var icon=new C.TextBlock{Text=glyph,FontFamily=new M.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),FontSize=size*.42,
            Foreground=SkinBrush("Accent"),HorizontalAlignment=W.HorizontalAlignment.Center,VerticalAlignment=W.VerticalAlignment.Center};
        return new C.Border{Width=size,Height=size,CornerRadius=new W.CornerRadius(12),Background=SkinBrush("Elevated"),Child=icon};
    }
    private C.Border SecondarySurface(W.UIElement content,W.Thickness? padding=null)
        =>new(){Child=content,Background=SkinBrush("Surface"),BorderBrush=SkinBrush("Stroke"),BorderThickness=new W.Thickness(1),CornerRadius=new W.CornerRadius(14),Padding=padding??new W.Thickness(22),Margin=new W.Thickness(0,0,0,18)};
    private C.Border SecondaryHeader(string title,string hint,string context,string glyph)
    {
        var grid=new C.Grid();grid.ColumnDefinitions.Add(new(){Width=W.GridLength.Auto});grid.ColumnDefinitions.Add(new());
        var icon=SecondaryIcon(glyph,54);icon.Margin=new W.Thickness(0,3,20,0);icon.VerticalAlignment=W.VerticalAlignment.Top;grid.Children.Add(icon);
        var copy=new C.StackPanel();C.Grid.SetColumn(copy,1);grid.Children.Add(copy);
        var eyebrow=SecondaryText(context.ToUpperInvariant(),11,true);eyebrow.Foreground=SkinBrush("Accent");eyebrow.FontWeight=W.FontWeights.SemiBold;eyebrow.Margin=new W.Thickness(0,0,0,5);copy.Children.Add(eyebrow);
        var heading=SecondaryText(title,30,false,true);heading.Margin=new W.Thickness(0,0,0,7);copy.Children.Add(heading);
        var help=SecondaryText(hint,13,true);help.Margin=new W.Thickness(0);help.MaxWidth=880;help.HorizontalAlignment=W.HorizontalAlignment.Left;copy.Children.Add(help);
        var surface=SecondarySurface(grid,new W.Thickness(24));surface.Name="SecondaryPageHeader";return surface;
    }
    private C.Border SecondaryEmpty(string title,string hint,string glyph)
    {
        var copy=new C.StackPanel{MaxWidth=540,HorizontalAlignment=W.HorizontalAlignment.Center,VerticalAlignment=W.VerticalAlignment.Center,Margin=new W.Thickness(18,32,18,32)};
        var icon=SecondaryIcon(glyph,64);icon.HorizontalAlignment=W.HorizontalAlignment.Center;icon.Margin=new W.Thickness(0,0,0,22);copy.Children.Add(icon);
        var heading=SecondaryText(title,24,false,true);heading.TextAlignment=W.TextAlignment.Center;copy.Children.Add(heading);
        var help=SecondaryText(hint,14,true);help.TextAlignment=W.TextAlignment.Center;copy.Children.Add(help);
        var box=SecondarySurface(copy);box.Name="SecondaryEmptyState";box.MinHeight=290;return box;
    }
    private C.Grid SecondaryListItem(string title,string subtitle,string glyph)
    {
        var grid=new C.Grid();grid.ColumnDefinitions.Add(new(){Width=new W.GridLength(54)});grid.ColumnDefinitions.Add(new());
        var icon=SecondaryIcon(glyph,40);icon.VerticalAlignment=W.VerticalAlignment.Center;grid.Children.Add(icon);
        var copy=new C.StackPanel{VerticalAlignment=W.VerticalAlignment.Center};C.Grid.SetColumn(copy,1);grid.Children.Add(copy);
        var heading=SecondaryText(title,15,false,true);heading.Margin=new W.Thickness(0,0,0,4);copy.Children.Add(heading);
        var detail=SecondaryText(subtitle,12,true);detail.Margin=new W.Thickness(0);copy.Children.Add(detail);return grid;
    }
    private C.Grid SecondaryStats(params (string Label,string Value)[] values)
    {
        var grid=new C.Grid{Margin=new W.Thickness(0,4,0,18)};
        for(var i=0;i<values.Length;i++)
        {
            grid.ColumnDefinitions.Add(new());var item=values[i];
            var copy=new C.StackPanel();var value=SecondaryText(item.Value,20,false,true);value.Foreground=SkinBrush("Accent");copy.Children.Add(value);
            var label=SecondaryText(item.Label,11,true);label.Margin=new W.Thickness(0);copy.Children.Add(label);
            var card=SecondarySurface(copy,new W.Thickness(16));card.Margin=new W.Thickness(0,0,i==values.Length-1?0:12,0);C.Grid.SetColumn(card,i);grid.Children.Add(card);
        }
        return grid;
    }
}
