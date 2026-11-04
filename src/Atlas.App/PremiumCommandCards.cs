using Atlas.Core;
using W=System.Windows;
using C=System.Windows.Controls;
using M=System.Windows.Media;

namespace Atlas.App;
public partial class PremiumWindow
{
    private C.ListBoxItem CommandModCard(ModPackage mod,ModDetails info,ModDetailsStore metadata,string category,string state,ModStatus? visualStatus)
    {
        var width = commandCardWidth * .5625;
        var imageHeight = commandArtworkHeight * .5625;
        var item=new C.ListBoxItem{Tag=mod,Width=width,Padding=new W.Thickness(0),Margin=new W.Thickness(0,0,12,14),VerticalContentAlignment=W.VerticalAlignment.Top};
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
        var caption=new C.StackPanel{Margin=new W.Thickness(11,9,11,8)};
        var title=SecondaryText(mod.Name,15,false,true);title.Height=38;title.LineHeight=19;title.LineStackingStrategy=W.LineStackingStrategy.BlockLineHeight;title.TextTrimming=W.TextTrimming.CharacterEllipsis;title.Margin=new W.Thickness(0,0,0,8);caption.Children.Add(title);
        var categoryLabel=SecondaryText(category,11,true);categoryLabel.TextWrapping=W.TextWrapping.NoWrap;categoryLabel.TextTrimming=W.TextTrimming.CharacterEllipsis;categoryLabel.Margin=new W.Thickness(0,0,0,7);var metadataRow = new C.WrapPanel { Height = 24, ClipToBounds = true };
        metadataRow.Children.Add(CollectionBadge(categoryLabel));
        caption.Children.Add(metadataRow);
        var characters=LibraryCharacters(mod).Where(character=>!character.Equals(category,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(characters.Length>0)
        {
            var character=SecondaryText(string.Join(" · ",characters),11);character.Name="ModCardCharacter_"+mod.Id;character.Foreground=SkinBrush("Accent");metadataRow.Children.Add(CollectionBadge(character));
        }
        var indicators = ModContentIndicators(mod, compact: true, interactive: false);
        indicators.Height = 25;
        indicators.ClipToBounds = true;
        if (visualStatus == null || visualStatus.HasMissingDependencies)
        {
            var warning = Text("!", 12, true);
            warning.Margin = new W.Thickness(4, 2, 0, 0);
            warning.ToolTip = visualStatus == null ? L.T("ModStatusUnavailable") : string.Join(Environment.NewLine, visualStatus.MissingDependencies);
            indicators.Children.Add(warning);
        }
        caption.Children.Add(indicators);
        var content=new C.StackPanel{VerticalAlignment=W.VerticalAlignment.Top};content.Children.Add(image);content.Children.Add(caption);
        var open=Button("",()=>{var keys=W.Input.Keyboard.Modifiers;if((keys&(W.Input.ModifierKeys.Control|W.Input.ModifierKeys.Shift))!=0)SelectCard(item,keys);else{modList?.SelectedItems.Clear();item.IsSelected=true;modSelectionAnchor=modList?.Items.IndexOf(item)??0;ShowDetails();}},"OpenMod_"+mod.Id);
        open.Padding=new W.Thickness(0);open.Margin=new W.Thickness(0);open.Background=M.Brushes.Transparent;open.VerticalAlignment=W.VerticalAlignment.Stretch;open.ToolTip=mod.Name;
        var surface = new C.Grid();
        surface.RowDefinitions.Add(new() { Height = W.GridLength.Auto });
        surface.RowDefinitions.Add(new() { Height = W.GridLength.Auto });
        surface.Children.Add(content);
        surface.Children.Add(open);
        var details = Button(L.T("ModPage"), () => ShowModPage(mod), "ModCardDetails_" + mod.Id);
        details.Background = SkinBrush("Elevated");
        details.Padding = new W.Thickness(8, 0, 8, 0);
        details.Height = 29;
        details.Margin = new W.Thickness(0);
        details.FontSize = 11;
        var footer = new C.Border
        {
            Child = details, Background = SkinBrush("Base"), BorderBrush = SkinBrush("Stroke"),
            BorderThickness = new W.Thickness(0, 1, 0, 0), Padding = new W.Thickness(10, 6, 10, 6)
        };
        C.Grid.SetRow(footer, 1);
        surface.Children.Add(footer);
        var check=new C.CheckBox{Tag=mod.Id,ToolTip=L.T("ModCardSelect"),Style=(W.Style)FindResource("CommandCheck"),Margin=new W.Thickness(13),HorizontalAlignment=W.HorizontalAlignment.Left,VerticalAlignment=W.VerticalAlignment.Top};
        check.SetBinding(C.CheckBox.IsCheckedProperty,new W.Data.Binding("IsSelected"){Source=item,Mode=W.Data.BindingMode.TwoWay});check.Click+=(_,e)=>{modSelectionAnchor=modList?.Items.IndexOf(item)??0;e.Handled=true;};System.Windows.Automation.AutomationProperties.SetName(check,L.T("ModCardSelect")+" · "+mod.Name);surface.Children.Add(check);
        var more=Button("···",()=>{},"ModCardOptions_"+mod.Id);more.Width=34;more.Height=32;more.Padding=new W.Thickness(0);more.Margin=new W.Thickness(0,12,12,0);more.HorizontalAlignment=W.HorizontalAlignment.Right;more.VerticalAlignment=W.VerticalAlignment.Top;more.ToolTip=L.T("MoreActions");
        var menu=new C.ContextMenu();
        void Action(string key,Action action,bool enabled=true){var option=new C.MenuItem{Header=L.T(key),IsEnabled=enabled};option.Click+=(_,_)=>Guard(action);menu.Items.Add(option);}
        if (mod.Kind == "variants") Action("ModVariants", () => ChooseModVariant(mod));
        Action("ModPreview",()=>ShowModGallery(mod),info.Images.Length>0);Action("ModCover",()=>AddModImage(mod,true,true),!CurrentProfiles().HasPending);Action("ModPage",()=>ShowModPage(mod));
        more.Click+=(_,e)=>{e.Handled=true;OpenThemedMenu(menu,more);};surface.Children.Add(more);
        if (HasLibraryConflict(mod))
        {
            var warning = ConflictButton(mod);
            warning.HorizontalAlignment = W.HorizontalAlignment.Right;
            warning.VerticalAlignment = W.VerticalAlignment.Top;
            warning.Margin = new W.Thickness(0, 12, 52, 0);
            surface.Children.Add(warning);
        }
        var outline=new C.Border{Child=surface,Background=SkinBrush("Surface"),BorderBrush=SkinBrush("Stroke"),BorderThickness=new W.Thickness(1),CornerRadius=new W.CornerRadius(10),ClipToBounds=true};
        item.Selected+=(_,_)=>outline.BorderBrush=SkinBrush("Accent");item.Unselected+=(_,_)=>outline.BorderBrush=SkinBrush("Stroke");item.Content=outline;return item;
    }
}
