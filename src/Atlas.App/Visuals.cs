using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Atlas.Core;

namespace Atlas.App;

internal static class Visuals
{
    internal static readonly Color Background = Color.FromArgb(13, 15, 20);
    internal static readonly Color Surface = Color.FromArgb(22, 25, 33);
    internal static readonly Color Raised = Color.FromArgb(31, 35, 46);
    internal static readonly Color Muted = Color.FromArgb(157, 164, 182);
    internal static readonly Color Text = Color.FromArgb(237, 239, 247);
    internal static Image? LoadImage(string path)
    {
        try { using var image=Image.FromFile(path); return new Bitmap(image); }
        catch (Exception e) when (e is IOException or ArgumentException or OutOfMemoryException or UnauthorizedAccessException) { return null; }
    }
    internal static Image? Cover(GameProfile profile, string root, PlayerPreferences settings)
    {
        if (settings.Covers.TryGetValue(profile.Id, out var custom))
        {
            var path = AppearancePath(root, custom);
            if (path != null && LoadImage(path) is Image image) return image;
        }
        using var stream = typeof(Visuals).Assembly.GetManifestResourceStream($"Atlas.App.Assets.{profile.Id}.jpg");
        if (stream == null) return null;
        using var original = Image.FromStream(stream); return new Bitmap(original);
    }
    internal static string? AppearancePath(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative)) return null;
        try
        {
            var path = Path.GetFullPath(Path.Combine(root, relative));
            return path.StartsWith(Path.GetFullPath(Path.Combine(root,"appearance")) + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) ? path : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { return null; }
    }
    internal static string ImportImage(string file, string root)
    {
        if (new FileInfo(file).Length > 50L*1024*1024) throw new InvalidOperationException(L.T("ImageTooLarge"));
        using var source = Image.FromFile(file);
        if ((long)source.Width*source.Height > 40_000_000) throw new InvalidOperationException(L.T("ImageTooManyPixels"));
        var scale=Math.Min(1.0,Math.Min(3840.0/source.Width,2160.0/source.Height));
        using var bitmap=new Bitmap(Math.Max(1,(int)(source.Width*scale)),Math.Max(1,(int)(source.Height*scale)));
        using (var graphics=Graphics.FromImage(bitmap)) { graphics.InterpolationMode=InterpolationMode.HighQualityBicubic; graphics.DrawImage(source,0,0,bitmap.Width,bitmap.Height); }
        Directory.CreateDirectory(Path.Combine(root,"appearance"));
        var relative="appearance/"+Guid.NewGuid().ToString("N")+".png";
        bitmap.Save(Path.Combine(root,relative),ImageFormat.Png); return relative;
    }
    internal static byte[] NormalizeModImage(string file){if(new FileInfo(file).Length>50L*1024*1024)throw new InvalidOperationException(L.T("ImageTooLarge"));return NormalizeModImage(File.ReadAllBytes(file));}
    internal static byte[] NormalizeModImage(byte[] bytes)
    {
        if(bytes.Length>50L*1024*1024)throw new InvalidOperationException(L.T("ImageTooLarge"));
        using var stream=new MemoryStream(bytes,false);using var source=Image.FromStream(stream);
        if((long)source.Width*source.Height>40_000_000)throw new InvalidOperationException(L.T("ImageTooManyPixels"));
        var scale=Math.Min(1.0,Math.Min(3840.0/source.Width,2160.0/source.Height));
        using var bitmap=new Bitmap(Math.Max(1,(int)(source.Width*scale)),Math.Max(1,(int)(source.Height*scale)));
        using(var graphics=Graphics.FromImage(bitmap)){graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.DrawImage(source,0,0,bitmap.Width,bitmap.Height);}
        using var output=new MemoryStream();bitmap.Save(output,ImageFormat.Png);return output.ToArray();
    }
    internal static void DrawCover(Graphics graphics, Image image, Rectangle target)
    {
        var scale=Math.Max((float)target.Width/image.Width,(float)target.Height/image.Height);
        var width=image.Width*scale; var height=image.Height*scale;
        var saved=graphics.Save(); graphics.SetClip(target);
        graphics.DrawImage(image,target.X+(target.Width-width)/2,target.Y+(target.Height-height)/2,width,height);
        graphics.Restore(saved);
    }
    internal static Button Button(string text, Action click, Color? fill=null)
    {
        var background=fill??Raised;
        var foreground=background.R*0.299+background.G*0.587+background.B*0.114>145?Background:Text;
        var button=new Button { Text=text, AutoSize=true, Height=38, MinimumSize=new Size(95,38), FlatStyle=FlatStyle.Flat, BackColor=background, ForeColor=foreground, Padding=new Padding(12,5,12,5), Cursor=Cursors.Hand, Margin=new Padding(0,0,8,0) };
        button.FlatAppearance.BorderSize=0; button.Click+=(_,_)=>click(); return button;
    }
}
