using Atlas.Core;
using W=System.Windows;
using C=System.Windows.Controls;
namespace Atlas.App;
public partial class PremiumWindow
{
    private C.ComboBox? characterFilter;
    private string? selectedCharacterFilter;
    private readonly HashSet<string> artworkChecked=[];
    private void AddCharacterFilter(C.Panel panel)
    {
        characterFilter=null;if(game?.Id is not ("doa5" or "re4"))return;
        characterFilter=new C.ComboBox{Name="CharacterFilter",Width=185,Margin=new W.Thickness(0,0,8,12)};
        characterFilter.SelectionChanged+=(_,_)=>{if(refreshingMods)return;selectedCharacterFilter=(characterFilter.SelectedItem as C.ComboBoxItem)?.Tag as string;modList?.SelectedItems.Clear();RefreshMods();};panel.Children.Add(characterFilter);
    }
    private void RefreshCharacterFilter(IReadOnlyList<ModPackage> packages)
    {
        if(characterFilter==null)return;var names=game?.Id == "re4" ? Re4CharacterNames : packages.SelectMany(LibraryCharacters).Distinct().Order().ToArray();
        if(selectedCharacterFilter!=null&&selectedCharacterFilter!="unassigned"&&!names.Contains(selectedCharacterFilter))selectedCharacterFilter=null;
        characterFilter.Items.Clear();characterFilter.Items.Add(new C.ComboBoxItem{Content=L.T("CharacterAll"),Tag=null});
        foreach(var name in names)characterFilter.Items.Add(new C.ComboBoxItem{Content=name,Tag=name});
        characterFilter.Items.Add(new C.ComboBoxItem{Content=L.T("CharacterUnassigned"),Tag="unassigned"});
        characterFilter.SelectedItem=characterFilter.Items.Cast<C.ComboBoxItem>().First(i=>Equals(i.Tag,selectedCharacterFilter));
    }
    private static readonly string[] Re4CharacterNames = ["Ada", "Ashley", "Leon"];

    private string[] LibraryCharacters(ModPackage mod)
    {
        if (game?.Id != "re4") return AutoLinkCharacters.ForPackage(mod);
        if (preferences.Re4Characters?.TryGetValue(mod.Id, out var chosen) == true) return chosen;
        var names = Re4CharacterNames.Where(name => System.Text.RegularExpressions.Regex.IsMatch(mod.Name,
            @"\b" + name + @"\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
        if (names.Count == 0 && mod.Files.Any(path => path.Contains("/cha2/cha200/", StringComparison.OrdinalIgnoreCase)))
            names.Add("Ada");
        return names.ToArray();
    }

    private bool MatchesCharacter(ModPackage mod)
    {
        if (selectedCharacterFilter == null) return true;
        var names = LibraryCharacters(mod);
        return selectedCharacterFilter == "unassigned" ? names.Length == 0 : names.Contains(selectedCharacterFilter);
    }

    private void ChooseRe4Characters(ModPackage mod)
    {
        var dialog = ProfileDialog(L.T("CharacterLabel"), 420, 300);
        var panel = new C.StackPanel { Margin = new W.Thickness(24) };
        panel.Children.Add(Text(mod.Name, 16));
        var current = LibraryCharacters(mod);
        var choices = Re4CharacterNames.Select(name => new C.CheckBox
        {
            Content = name, IsChecked = current.Contains(name), Foreground = SkinBrush("Text"), Margin = new W.Thickness(0, 0, 0, 8)
        }).ToArray();
        foreach (var choice in choices) panel.Children.Add(choice);
        panel.Children.Add(Button(L.T("Save"), () => dialog.DialogResult = true, primary: true));
        dialog.Content = panel;
        if (dialog.ShowDialog() != true) return;
        (preferences.Re4Characters ??= [])[mod.Id] = choices.Where(choice => choice.IsChecked == true).Select(choice => (string)choice.Content).ToArray();
        store.Save(preferences);
        RefreshMods();
    }

    private async void LoadEmbeddedArtwork()
    {
        if(library==null||game==null)return;var lib=library;var profile=game;var root=LibraryRoot(profile);
        var pending=lib.List().Where(p=>artworkChecked.Add(root+"/"+p.Id)).ToArray();if(pending.Length==0)return;
        var changed=await Task.Run(()=>{
            bool any=false;foreach(var mod in pending)try{
                var store=new ModDetailsStore(root,profile);var current=store.Get(mod);if(current.Cover!=null||current.Images.Length>0)continue;
                bool cover=true;foreach(var art in lib.ReadArtwork(mod.Id))try{var png=Visuals.NormalizeModImage(art.Data);if(cover){if(!store.TryAddEmbeddedCover(mod.Id,png))break;}else store.AddImage(mod.Id,png,false);cover=false;any=true;}catch(Exception error)when(error is IOException or ArgumentException or InvalidOperationException or OutOfMemoryException or UnauthorizedAccessException){ }
            }catch(Exception error)when(error is IOException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException){ }
            return any;
        });
        if(changed&&!nexusClosed&&ReferenceEquals(library,lib)){if(modList!=null)RefreshMods();else if(!busy&&!nexusPageVisible&&commandSection=="collections")ShowCollections(visibleCollectionId);}
    }
}
