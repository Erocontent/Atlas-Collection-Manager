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
        characterFilter=null;if(game?.Id!="doa5")return;
        characterFilter=new C.ComboBox{Name="CharacterFilter",Width=185,Margin=new W.Thickness(0,0,8,12)};
        characterFilter.SelectionChanged+=(_,_)=>{if(refreshingMods)return;selectedCharacterFilter=(characterFilter.SelectedItem as C.ComboBoxItem)?.Tag as string;modList?.SelectedItems.Clear();RefreshMods();};panel.Children.Add(characterFilter);
    }
    private void RefreshCharacterFilter(IReadOnlyList<ModPackage> packages)
    {
        if(characterFilter==null)return;var names=packages.SelectMany(AutoLinkCharacters.ForPackage).Distinct().Order().ToArray();
        if(selectedCharacterFilter!=null&&selectedCharacterFilter!="unassigned"&&!names.Contains(selectedCharacterFilter))selectedCharacterFilter=null;
        characterFilter.Items.Clear();characterFilter.Items.Add(new C.ComboBoxItem{Content=L.T("CharacterAll"),Tag=null});
        foreach(var name in names)characterFilter.Items.Add(new C.ComboBoxItem{Content=name,Tag=name});
        characterFilter.Items.Add(new C.ComboBoxItem{Content=L.T("CharacterUnassigned"),Tag="unassigned"});
        characterFilter.SelectedItem=characterFilter.Items.Cast<C.ComboBoxItem>().First(i=>Equals(i.Tag,selectedCharacterFilter));
    }
    private bool MatchesCharacter(ModPackage package)=>game?.Id!="doa5"||selectedCharacterFilter==null||(selectedCharacterFilter=="unassigned"?AutoLinkCharacters.ForPackage(package).Length==0:AutoLinkCharacters.ForPackage(package).Contains(selectedCharacterFilter));
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
