using Atlas.Core;
using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;

namespace Atlas.App;
public partial class PremiumWindow
{
    private C.ListBoxItem CommandModCard(ModPackage mod,ModDetails info,ModDetailsStore metadata,string category,string state,ModStatus? visualStatus)
    {
        var width=commandCardWidth;var imageHeight=commandArtworkHeight;
        var item=new C.ListBoxItem{Tag=mod,Width=width,Padding=new W.Thickness(0),Margin=new W.Thickness(0,0,14,18),VerticalContentAlignment=W.VerticalAlignment.Top};
        var image=new C.Grid{Height=imageHeight,ClipToBounds=true,Background=SkinBrush("Elevated")};
        var source=LibraryCover(metadata,info,(int)width*2);
        if (source != null) image.Background = new M.ImageBrush(source) { Stretch = M.Stretch.UniformToFill };
        else
        {
            var placeholder=new C.StackPanel{VerticalAlignment=W.VerticalAlignment.Center};
            var symbol=SecondaryText("◇",52,false,true);symbol.Foreground=SkinBrush("Accent");symbol.TextAlignment=W.TextAlignment.Center;placeholder.Children.Add(symbol);
            var hint=SecondaryText(L.T("ModNoCover"),12,true);hint.TextAlignment=W.TextAlignment.Center;placeholder.Children.Add(hint);image.Children.Add(placeholder);
        }
        image.SizeChanged += (_, _) => image.Clip = new M.RectangleGeometry(new W.Rect(0, 0, image.ActualWidth, image.ActualHeight + 9), 9, 9);
        var stateLabel=Text((state=="enabled"?"✓  ":"⊘  ")+L.T(state switch{"enabled"=>"Enabled","other"=>"OtherFolder",_=>"Disabled"}),11);
        stateLabel.Margin=new W.Thickness(0);stateLabel.Foreground=SkinBrush(state=="enabled"&&!IsCinematic?"AccentText":"Text");
        image.Children.Add(new C.Border{Child=stateLabel,Background=SkinBrush(state=="enabled"&&!IsCinematic?"Accent":"Elevated"),Padding=new W.Thickness(10,7,10,7),CornerRadius=new W.CornerRadius(6),HorizontalAlignment=W.HorizontalAlignment.Right,VerticalAlignment=W.VerticalAlignment.Bottom,Margin=new W.Thickness(12)});
        var caption=new C.StackPanel{Margin=new W.Thickness(16,14,16,15)};
        var title=SecondaryText(mod.Name,18,false,true);title.MinHeight=24;title.MaxHeight=54;title.TextTrimming=W.TextTrimming.CharacterEllipsis;title.Margin=new W.Thickness(0,0,0,8);caption.Children.Add(title);
        var categoryLabel=SecondaryText(category,12,true);categoryLabel.TextWrapping=W.TextWrapping.NoWrap;categoryLabel.TextTrimming=W.TextTrimming.CharacterEllipsis;categoryLabel.Margin=new W.Thickness(0,0,0,7);caption.Children.Add(categoryLabel);
        var characters=AutoLinkCharacters.ForPackage(mod).Where(character=>!character.Equals(category,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(game?.Id=="doa5"&&characters.Length>0)
        {
            var character=SecondaryText(string.Join(" · ",characters),11);character.Name="ModCardCharacter_"+mod.Id;character.Foreground=SkinBrush("Accent");character.Margin=new W.Thickness(0,0,0,4);caption.Children.Add(character);
        }
        if(visualStatus==null||visualStatus.HasConflict||visualStatus.HasMissingDependencies)AddModIndicators(caption,visualStatus);
        var content=new C.StackPanel{VerticalAlignment=W.VerticalAlignment.Top};content.Children.Add(image);content.Children.Add(caption);
        var open=Button("",()=>{var keys=W.Input.Keyboard.Modifiers;if((keys&(W.Input.ModifierKeys.Control|W.Input.ModifierKeys.Shift))!=0)SelectCard(item,keys);else{modList?.SelectedItems.Clear();item.IsSelected=true;modSelectionAnchor=modList?.Items.IndexOf(item)??0;ShowDetails();}},"OpenMod_"+mod.Id);
        open.Padding=new W.Thickness(0);open.Margin=new W.Thickness(0);open.Background=M.Brushes.Transparent;open.VerticalAlignment=W.VerticalAlignment.Stretch;open.ToolTip=mod.Name;
        var surface=new C.Grid();surface.Children.Add(content);surface.Children.Add(open);
        var check=new C.CheckBox{Tag=mod.Id,ToolTip=L.T("ModCardSelect"),Style=(W.Style)FindResource("CommandCheck"),Margin=new W.Thickness(13),HorizontalAlignment=W.HorizontalAlignment.Left,VerticalAlignment=W.VerticalAlignment.Top};
        check.SetBinding(C.CheckBox.IsCheckedProperty,new W.Data.Binding("IsSelected"){Source=item,Mode=W.Data.BindingMode.TwoWay});check.Click+=(_,e)=>{modSelectionAnchor=modList?.Items.IndexOf(item)??0;e.Handled=true;};System.Windows.Automation.AutomationProperties.SetName(check,L.T("ModCardSelect")+" · "+mod.Name);surface.Children.Add(check);
        var more=Button("···",()=>{},"ModCardOptions_"+mod.Id);more.Width=34;more.Height=32;more.Padding=new W.Thickness(0);more.Margin=new W.Thickness(0,12,12,0);more.HorizontalAlignment=W.HorizontalAlignment.Right;more.VerticalAlignment=W.VerticalAlignment.Top;more.ToolTip=L.T("MoreActions");
        var menu=new C.ContextMenu();
        void Action(string key,Action action,bool enabled=true){var option=new C.MenuItem{Header=L.T(key),IsEnabled=enabled};option.Click+=(_,_)=>Guard(action);menu.Items.Add(option);}
        Action("ModPreview",()=>ShowModGallery(mod),info.Images.Length>0);Action("ModCover",()=>AddModImage(mod,true,true),!CurrentProfiles().HasPending);Action("ModPage",()=>ShowModPage(mod));
        more.Click+=(_,e)=>{e.Handled=true;OpenThemedMenu(menu,more);};surface.Children.Add(more);
        var outline=new C.Border{Child=surface,Background=SkinBrush("Surface"),BorderBrush=SkinBrush("Stroke"),BorderThickness=new W.Thickness(1),CornerRadius=new W.CornerRadius(10),ClipToBounds=true};
        item.Selected+=(_,_)=>outline.BorderBrush=SkinBrush("Accent");item.Unselected+=(_,_)=>outline.BorderBrush=SkinBrush("Stroke");item.Content=outline;return item;
    }
}
