using Atlas.Core;
namespace Atlas.App;

internal sealed class AppearanceForm : Form
{
    internal string Accent { get; private set; }
    internal int CoverWidth => sizes.SelectedIndex switch {0=>160,2=>220,_=>190};
    internal string BackgroundFile { get; private set; }
    private readonly ComboBox sizes=new(){DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill};
    private readonly Label backgroundLabel=new(){AutoSize=true,ForeColor=Visuals.Muted};
    private readonly Button color;
    internal AppearanceForm(PlayerPreferences settings,string root)
    {
        Accent=settings.Accent; BackgroundFile=settings.Background;
        Text=L.T("AppearanceTitle"); Size=new Size(680,500); FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; MinimizeBox=false; StartPosition=FormStartPosition.CenterParent;
        BackColor=Visuals.Surface; ForeColor=Visuals.Text; Font=new Font("Segoe UI",10);
        var grid=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(25),ColumnCount=1,RowCount=9};
        foreach(var height in new[]{42,32,45,30,38,46,35,62,45})grid.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        grid.Controls.Add(new Label{Text=L.T("AppearanceHeading"),AutoSize=true,Font=new Font("Segoe UI",17,FontStyle.Bold)},0,0);
        grid.Controls.Add(new Label{Text=L.T("AccentColor"),AutoSize=true},0,1);
        color=Visuals.Button(L.T("ChooseColor"),()=>{
            using var picker=new ColorDialog{Color=ColorTranslator.FromHtml(Accent),FullOpen=true};
            if(picker.ShowDialog(this)==DialogResult.OK){Accent=ColorTranslator.ToHtml(picker.Color);if(!Accent.StartsWith('#'))Accent=$"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}"; color!.BackColor=picker.Color;}
        },ColorTranslator.FromHtml(Accent)); grid.Controls.Add(color,0,2);
        grid.Controls.Add(new Label{Text=L.T("CoverSize"),AutoSize=true},0,3);
        sizes.Items.AddRange([L.T("Compact"),L.T("Medium"),L.T("Large")]); sizes.SelectedIndex=settings.CoverWidth switch{160=>0,220=>2,_=>1};grid.Controls.Add(sizes,0,4);
        var backgroundButtons=new FlowLayoutPanel{Dock=DockStyle.Fill};
        backgroundButtons.Controls.Add(Visuals.Button(L.T("ChooseBackground"),()=>{
            using var picker=new OpenFileDialog{Filter=L.T("ImageFilter")};
            if(picker.ShowDialog(this)!=DialogResult.OK)return;
            try{BackgroundFile=Visuals.ImportImage(picker.FileName,root);UpdateBackground();}catch(Exception e){MessageBox.Show(this,e.Message,L.T("ImageNotImported"));}
        }));
        backgroundButtons.Controls.Add(Visuals.Button(L.T("RemoveBackground"),()=>{BackgroundFile="";UpdateBackground();}));grid.Controls.Add(backgroundButtons,0,5);
        grid.Controls.Add(backgroundLabel,0,6);UpdateBackground();
        grid.Controls.Add(new Label{Text=L.T("AppearanceHint"),Dock=DockStyle.Fill,ForeColor=Visuals.Muted},0,7);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};
        buttons.Controls.Add(Visuals.Button(L.T("Save"),()=>{DialogResult=DialogResult.OK;}));
        buttons.Controls.Add(Visuals.Button(L.T("Cancel"),()=>{DialogResult=DialogResult.Cancel;}));
        buttons.Controls.Add(Visuals.Button(L.T("ResetDefaults"),()=>{Accent="#A78BFA";sizes.SelectedIndex=1;BackgroundFile="";color.BackColor=ColorTranslator.FromHtml(Accent);UpdateBackground();}));
        grid.Controls.Add(buttons,0,8);Controls.Add(grid);
    }
    private void UpdateBackground()=>backgroundLabel.Text=L.T(BackgroundFile==""?"DefaultBackground":"PersonalBackground");
}
