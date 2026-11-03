using Atlas.Core;

namespace Atlas.App;

internal sealed class ImportForm : Form
{
    private readonly TextBox name=new(){Dock=DockStyle.Fill,Name="ModName"};
    private readonly TextBox category=new(){Dock=DockStyle.Fill};
    private readonly TextBox destination=new(){Dock=DockStyle.Fill,Name="Destination"};
    private readonly ComboBox mode=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,Name="InstallType"};
    private readonly TextBox help=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Name="ImportRecognition"};
    private readonly TextBox preview=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Name="ImportTargetPreview"};
    private readonly CheckBox manual=new(){Dock=DockStyle.Fill,Text=L.T("SmartManualAck"),Name="ManualImportAcknowledgment"};
    private readonly ComboBox character=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,Name="CharacterSelector"};
    private readonly Panel characterPanel=new(){Dock=DockStyle.Fill};
    public string? Character=>character.SelectedItem is string value&&AutoLinkCharacters.Names.Contains(value)?value:null;
    private readonly bool isDoa;
    private readonly string gameFolder;
    private readonly IReadOnlyList<ActiveMod> active;
    private readonly IReadOnlyList<ModPackage> packages;
    private ArchiveImportAnalysis? analysis;
    private Button? confirm;
    private int revision;
    private bool previewReady;
    public string ModName=>name.Text;
    public string Category=>category.Text;
    public string Destination=>destination.Text;
    public bool AutoLinkSkin=>isDoa&&mode.SelectedIndex==0;
    private bool ManualRouting=>analysis?.Kind==ArchiveImportKind.Unknown||(analysis!=null&&((analysis.Kind==ArchiveImportKind.AutoLinkSkin)!=AutoLinkSkin||!string.Equals(Destination.Trim().Replace('\\','/').TrimEnd('/'),analysis.Destination,StringComparison.OrdinalIgnoreCase)));

    public ImportForm(string zip,GameProfile? profile=null,ArchiveImportAnalysis? analysis=null,string gameFolder="",IReadOnlyList<ActiveMod>? active=null,IReadOnlyList<ModPackage>? packages=null)
    {
        profile??=GameCatalog.Get("doa5");isDoa=profile.Id=="doa5";this.analysis=analysis;this.gameFolder=gameFolder;this.active=active??[];this.packages=packages??[];
        Text=L.T("ImportHeading");Size=new(840,920);MinimumSize=new(800,760);StartPosition=FormStartPosition.CenterParent;
        Font=new Font("Segoe UI",10);BackColor=Visuals.Surface;ForeColor=Visuals.Text;
        var grid=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=14};
        foreach(var height in new[]{26,36,26,36,26,36,26,36,62,142,44,26})grid.RowStyles.Add(new(SizeType.Absolute,height));
        grid.RowStyles.Add(new(SizeType.Percent,100));grid.RowStyles.Add(new(SizeType.Absolute,48));
        name.Text=Path.GetFileNameWithoutExtension(zip);category.Text=L.T("NoCategory");
        foreach(var box in new[]{name,category,destination,preview,help}){box.BackColor=Visuals.Raised;box.ForeColor=Visuals.Text;box.BorderStyle=BorderStyle.FixedSingle;}
        mode.BackColor=Visuals.Raised;mode.ForeColor=Visuals.Text;
        if(isDoa)mode.Items.AddRange([L.T("AutoLinkSkin"),L.T("GameFilesMode")]);else mode.Items.Add(L.T("GameFilesMode"));
        void Field(string key,Control control,int row){grid.Controls.Add(new Label{Text=L.T(key),AutoSize=true},0,row);grid.Controls.Add(control,0,row+1);}
        Field("Name",name,0);Field("InstallType",mode,2);Field("UniqueCategory",category,4);Field("DestinationRelative",destination,6);
        var characterLabel=new Label{Text=L.T("CharacterLabel"),Dock=DockStyle.Top,Height=25};characterPanel.Controls.Add(character);characterPanel.Controls.Add(characterLabel);character.Dock=DockStyle.Bottom;character.Height=30;characterPanel.Visible=false;character.BackColor=Visuals.Raised;character.ForeColor=Visuals.Text;grid.Controls.Add(characterPanel,0,8);
        grid.Controls.Add(help,0,9);grid.Controls.Add(manual,0,10);grid.Controls.Add(new Label{Text=L.T("ArchivePreview"),AutoSize=true},0,11);grid.Controls.Add(preview,0,12);
        mode.SelectedIndexChanged+=(_,_)=>{destination.ReadOnly=AutoLinkSkin;destination.Text=AutoLinkSkin?"AutoLink":"";manual.Checked=false;SetupCharacters();UpdatePreview();};
        character.SelectedIndexChanged+=(_,_)=>{manual.Checked=false;UpdatePreview();};
        destination.TextChanged+=(_,_)=>{manual.Checked=false;UpdatePreview();};manual.CheckedChanged+=(_,_)=>UpdateConfirmation();
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,6,0,0)};
        confirm=Visuals.Button(L.T("ImportAction"),()=>{if(string.IsNullOrWhiteSpace(name.Text)){MessageBox.Show(this,L.T("EnterModName"));return;}if(previewReady&&(!ManualRouting||manual.Checked))DialogResult=DialogResult.OK;});confirm.Name="SmartImportConfirm";confirm.Enabled=false;
        var cancel=Visuals.Button(L.T("Cancel"),()=>DialogResult=DialogResult.Cancel);buttons.Controls.Add(confirm);buttons.Controls.Add(cancel);grid.Controls.Add(buttons,0,13);CancelButton=cancel;Controls.Add(grid);
        Shown+=async(_,_)=>{
            try{this.analysis??=await Task.Run(()=>ImportAnalysis.Inspect(zip,profile));if(IsDisposed)return;mode.SelectedIndex=isDoa&&this.analysis.Kind==ArchiveImportKind.AutoLinkSkin?0:isDoa?1:0;SetupCharacters();UpdatePreview();}
            catch(Exception error){if(IsDisposed)return;MessageBox.Show(this,error.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error);DialogResult=DialogResult.Cancel;}
        };
    }
    private void SetupCharacters()
    {
        characterPanel.Visible=AutoLinkSkin;character.Items.Clear();if(!AutoLinkSkin||analysis==null)return;
        try{var plan=AutoLinkCharacters.Plan(analysis.Files);character.Enabled=plan.CanChooseCharacter;
            if(plan.CanChooseCharacter){character.Items.Add(L.T("CharacterChoose"));foreach(var name in AutoLinkCharacters.Names.Order())character.Items.Add(name);character.SelectedItem=plan.Characters.Length==1?plan.Characters[0]:L.T("CharacterChoose");}
            else{character.Items.Add(L.T("CharacterMultiple"));character.SelectedIndex=0;}
        }catch{character.Items.Add(L.T("CharacterLayoutUnknown"));character.SelectedIndex=0;character.Enabled=false;}
    }
    private void UpdateConfirmation(){if(confirm!=null)confirm.Enabled=previewReady&&(!ManualRouting||manual.Checked);}
    private async void UpdatePreview()
    {
        if(analysis==null||IsDisposed)return;
        var version=++revision;previewReady=false;UpdateConfirmation();manual.Enabled=ManualRouting;manual.Visible=ManualRouting;
        var skin=AutoLinkSkin;var prefix=Destination;var files=analysis.Files;var selectedCharacter=Character;
        try
        {
            var result=await Task.Run(()=>{var targets=ImportAnalysis.Targets(files,prefix,skin,selectedCharacter,true);return(Targets:targets,Review:ImportAnalysis.Review(targets,gameFolder,active,packages));});
            if(IsDisposed||version!=revision)return;
            var type=analysis.Kind switch{ArchiveImportKind.AutoLinkSkin=>"AutoLinkSkin",ArchiveImportKind.DirectFiles=>"GameFilesMode",ArchiveImportKind.ReShade=>"ReShadeTitle",_=>"SmartUnknown"};
            var lines=new List<string>{L.T("SmartDetected",L.T(type)),L.T("SmartReadOnly"),L.T(skin?"ImportSkinHelp":"ImportFilesHelp")};
            lines.AddRange(analysis.WarningKeys.Select(key=>L.T(key)));if(skin)lines.Add(L.T("CharacterHint"));
            lines.Add(result.Review.FolderConfigured?L.T("SmartOverwrite",result.Review.ExistingFiles.Length):L.T("SmartNoFolder"));
            if(result.Review.Conflicts.Length>0)lines.Add(L.T("SmartConflicts",string.Join(", ",result.Review.Conflicts)));
            if(result.Review.UnsafePaths.Length>0)lines.Add(L.T("SmartUnsafePaths",string.Join(", ",result.Review.UnsafePaths.Take(10))));
            help.Text=string.Join("\r\n",lines);preview.Text=string.Join("\r\n",files.Zip(result.Targets).Take(2000).Select(p=>p.First+"  →  "+p.Second));
            if(files.Length>2000)preview.AppendText("\r\n"+L.T("SmartPreviewLimit",files.Length));
            previewReady=true;UpdateConfirmation();
        }
        catch(Exception error){if(IsDisposed||version!=revision)return;help.Text=error.Message;preview.Text="";UpdateConfirmation();}
    }
}
