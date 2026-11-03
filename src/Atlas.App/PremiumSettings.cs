using Atlas.Core;
using W = System.Windows;
using C = System.Windows.Controls;
using M = System.Windows.Media;

namespace Atlas.App;

public partial class PremiumWindow
{
    private bool settingsPageVisible;
    private void ShowSettings()
    {
        if(busy)return;profilesPageVisible=nexusPageVisible=false;modList=null;shadePanel=null;status=null;commandSection="settings";NavigationBuild();SettingsPage(false);
    }
    private C.Grid SettingsPage(bool nexus)
    {
        var root=Page();settingsPageVisible=true;root.RowDefinitions.Add(new(){Height=W.GridLength.Auto});root.RowDefinitions.Add(new());root.Children.Add(SecondarySurface(SecondaryText(L.T("Settings"),27,false,true),new W.Thickness(20,12,20,6)));
        var tabs=new C.TabControl{Name="SettingsTabs",Background=M.Brushes.Transparent,BorderThickness=new W.Thickness(0),Foreground=SkinBrush("Text"),Padding=new W.Thickness(0,20,0,0)};
        var tabStyle=new W.Style(typeof(C.TabItem));tabStyle.Setters.Add(new W.Setter(C.Control.BackgroundProperty,Brush("#24272E")));tabStyle.Setters.Add(new W.Setter(C.Control.ForegroundProperty,Brush("#C5C8CE")));tabStyle.Setters.Add(new W.Setter(C.Control.PaddingProperty,new W.Thickness(20,12,20,12)));tabStyle.Setters.Add(new W.Setter(C.Control.BorderThicknessProperty,new W.Thickness(0)));
        var template=new C.ControlTemplate(typeof(C.TabItem));var border=new W.FrameworkElementFactory(typeof(C.Border));border.SetBinding(C.Border.BackgroundProperty,new W.Data.Binding("Background"){RelativeSource=new W.Data.RelativeSource(W.Data.RelativeSourceMode.TemplatedParent)});border.SetValue(C.Border.CornerRadiusProperty,new W.CornerRadius(8));var header=new W.FrameworkElementFactory(typeof(C.ContentPresenter));header.SetValue(C.ContentPresenter.ContentSourceProperty,"Header");header.SetValue(C.ContentPresenter.MarginProperty,new W.Thickness(20,12,20,12));border.AppendChild(header);template.VisualTree=border;tabStyle.Setters.Add(new W.Setter(C.Control.TemplateProperty,template));var selected=new W.Trigger{Property=C.TabItem.IsSelectedProperty,Value=true};selected.Setters.Add(new W.Setter(C.Control.BackgroundProperty,Resources["Accent"]));selected.Setters.Add(new W.Setter(C.Control.ForegroundProperty,Resources["AccentText"]));tabStyle.Triggers.Add(selected);tabs.ItemContainerStyle=tabStyle;
        var personalization=new C.TabItem{Name="PersonalizationTab",Header=L.T("Personalization"),Content=PersonalizationPanel()};var nexusContent=new C.Grid();var integration=new C.TabItem{Name="NexusSettingsTab",Header=L.T("Nexus"),Content=nexusContent};tabs.Items.Add(personalization);tabs.Items.Add(integration);tabs.SelectedItem=nexus?integration:personalization;
        tabs.SelectionChanged+=(_,e)=>{if(e.Source!=tabs)return;if(tabs.SelectedItem==integration&&!nexusPageVisible)ShowNexus();else if(tabs.SelectedItem==personalization)nexusPageVisible=false;};C.Grid.SetRow(tabs,1);root.Children.Add(tabs);return nexusContent;
    }
    private C.ScrollViewer PersonalizationPanel()
    {
        var panel = new C.StackPanel();
        var palettePanel = new C.StackPanel();
        panel.Children.Add(SecondaryText(L.T("LauncherSkin"),22,false,true));panel.Children.Add(SecondaryText(L.T("SkinHint"),13,true));
        var drafts=preferences.SkinPalettes.ToDictionary(p=>p.Key,p=>p.Value);var selected=preferences.LauncherSkin;var updating=false;
        var previews=new C.Primitives.UniformGrid { Columns=2 };panel.Children.Add(previews);
        C.Button? applyAppearance = null;
        var edits=new Dictionary<string,C.TextBox>();var swatches=new Dictionary<string,C.Button>();
        M.Brush Color(string hex)=>(M.Brush)new M.BrushConverter().ConvertFromString(hex)!;
        LauncherPalette Read()=>new(edits["Background"].Text.Trim(),edits["Text"].Text.Trim(),edits["Accent"].Text.Trim());
        void UpdateApplyState()
        {
            if (applyAppearance != null) applyAppearance.IsEnabled = selected != preferences.LauncherSkin || drafts.Any(d => !Equals(d.Value, preferences.SkinPalettes.GetValueOrDefault(d.Key))) || !Equals(Read(), drafts[selected]);
        }
        void Preview()
        {
            previews.Children.Clear();foreach(var id in LauncherSkins.Ids)
            {
                using var screenshot = typeof(PremiumWindow).Assembly.GetManifestResourceStream($"Atlas.App.Assets.preview-{id}.jpg");
                var stack = new C.StackPanel();
                if (screenshot != null)
                    stack.Children.Add(new C.Image { Source = LoadImage(screenshot), Width = 260, MaxHeight = 170, Stretch = M.Stretch.Uniform });
                stack.Children.Add(SecondaryText(L.T(id == "cinematic" ? "Cinematic" : "CommandCenter"), 15, false, true));
                stack.Children.Add(SecondaryText(L.T(id == "cinematic" ? "CinematicPreviewHint" : "CommandPreviewHint"), 12, true));
                var choice=Button("",()=>SelectSkin(id),"SkinChoice_"+id.Replace('-','_'));choice.Content=stack;choice.Padding=new W.Thickness(10);choice.Margin=new W.Thickness(0,0,8,0);choice.BorderThickness=new W.Thickness(1);choice.BorderBrush=SkinBrush(selected==id?"Accent":"Stroke");choice.Background=selected==id?SkinBrush("Elevated"):SkinBrush("Surface");previews.Children.Add(choice);
            }
        }
        void Fill()
        {
            updating=true;var palette=drafts[selected];edits["Background"].Text=palette.Background;edits["Text"].Text=palette.Text;edits["Accent"].Text=palette.Accent;foreach(var key in edits.Keys){swatches[key].Background=Color(edits[key].Text);var c=(M.Color)M.ColorConverter.ConvertFromString(edits[key].Text);swatches[key].Foreground=Color(LauncherSkins.Contrast(edits[key].Text,"#000000")>=LauncherSkins.Contrast(edits[key].Text,"#FFFFFF")?"#000000":"#FFFFFF");}updating=false;Preview();UpdateApplyState();
        }
        palettePanel.Children.Add(SecondaryText(L.T("SkinColors"),21,false,true));foreach(var field in new[]{("Background","SkinBackground"),("Text","SkinText"),("Accent","SkinAccent")})
        {
            var row=new C.WrapPanel { Margin=new W.Thickness(0,0,0,12) }; var label=Text(L.T(field.Item2),13); label.Width=125; label.VerticalAlignment=W.VerticalAlignment.Center; label.Margin=new W.Thickness(0,0,8,0); row.Children.Add(label);var input=new C.TextBox{Name="SkinColor"+field.Item1,MaxLength=7,Width=105,Margin=new W.Thickness(0,0,12,0)};edits[field.Item1]=input;row.Children.Add(input);
            var pick=Button(L.T("ChooseColor"),()=>{var valid=LauncherSkins.IsValid(Read())?Read():drafts[selected];var hex=field.Item1 switch{"Background"=>valid.Background,"Text"=>valid.Text,_=>valid.Accent};using var dialog=new System.Windows.Forms.ColorDialog{Color=System.Drawing.ColorTranslator.FromHtml(hex),FullOpen=true};if(dialog.ShowDialog(new NativeOwner(this))==System.Windows.Forms.DialogResult.OK)input.Text=$"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";});pick.MinWidth=100;pick.Margin=new W.Thickness(0);swatches[field.Item1]=pick;row.Children.Add(pick);palettePanel.Children.Add(row);
            input.TextChanged+=(_,_)=>{if(updating||edits.Count!=3)return;UpdateApplyState();var palette=Read();if(!LauncherSkins.IsValid(palette))return;drafts[selected]=palette;foreach(var key in edits.Keys){swatches[key].Background=Color(edits[key].Text);var c=(M.Color)M.ColorConverter.ConvertFromString(edits[key].Text);swatches[key].Foreground=Color(LauncherSkins.Contrast(edits[key].Text,"#000000")>=LauncherSkins.Contrast(edits[key].Text,"#FFFFFF")?"#000000":"#FFFFFF");}Preview();};
        }
        void SelectSkin(string id)
        {
            if (selected == id) return;
            var current = Read();
            if (LauncherSkins.IsValid(current)) drafts[selected] = current;
            selected = id;
            Fill();
        }
        var actions=new C.WrapPanel{Margin=new W.Thickness(0,8,0,0)};actions.Children.Add(Button(L.T("SkinReset"),()=>{drafts[selected]=LauncherSkins.Defaults(selected);Fill();},"ResetSkinColorsButton"));applyAppearance=Button(L.T("SkinSave"),()=>{var palette=Read();if(!LauncherSkins.IsReadable(palette))throw new InvalidOperationException(L.T("SkinContrast"));drafts[selected]=palette;preferences.LauncherSkin=selected;preferences.SkinPalettes=drafts;store.Save(preferences);ApplyAppearance();ShowSettings();},"SaveSettingsButton",true);actions.Children.Add(applyAppearance);palettePanel.Children.Add(new C.Border { Height = 1, Background = SkinBrush("Stroke"), Margin = new W.Thickness(0, 2, 0, 12) });
        palettePanel.Children.Add(actions);

        var appearanceCard = SecondarySurface(panel);
        var paletteCard = SecondarySurface(palettePanel);
        var layout = new C.Grid
        {
            MaxWidth = 1240,
            HorizontalAlignment = W.HorizontalAlignment.Stretch,
            Margin = new W.Thickness(0, 18, 12, 0)
        };
        layout.ColumnDefinitions.Add(new C.ColumnDefinition());
        layout.ColumnDefinitions.Add(new C.ColumnDefinition());
        layout.RowDefinitions.Add(new C.RowDefinition { Height = W.GridLength.Auto });
        layout.RowDefinitions.Add(new C.RowDefinition { Height = W.GridLength.Auto });
        layout.Children.Add(appearanceCard);
        layout.Children.Add(paletteCard);

        var scroll = new C.ScrollViewer
        {
            Content = layout,
            VerticalScrollBarVisibility = C.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = C.ScrollBarVisibility.Disabled
        };
        void ArrangePanels()
        {
            var sideBySide = scroll.ViewportWidth >= 820;
            layout.ColumnDefinitions[0].Width = new W.GridLength(sideBySide ? 1.55 : 1, W.GridUnitType.Star);
            layout.ColumnDefinitions[1].Width = sideBySide ? new W.GridLength(1, W.GridUnitType.Star) : new W.GridLength(0);
            appearanceCard.Margin = new W.Thickness(0, 0, sideBySide ? 16 : 0, 14);
            C.Grid.SetColumn(paletteCard, sideBySide ? 1 : 0);
            C.Grid.SetRow(paletteCard, sideBySide ? 0 : 1);
        }
        scroll.Loaded += (_, _) => ArrangePanels();
        scroll.ScrollChanged += (_, args) => { if (args.ViewportWidthChange != 0) ArrangePanels(); };
        Fill();
        return scroll;
    }
}
