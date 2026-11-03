using Atlas.Core;
namespace Atlas.App;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var incoming = args.FirstOrDefault(a => a.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase));
        var smoke = args.Contains("--smoke-test");
        using var mutex = new Mutex(true, smoke ? null : "Local\\AtlasCollectionManager", out bool first);
        if (!first) { if (incoming != null) { try { NxmInbox.SendAsync(incoming).GetAwaiter().GetResult(); } catch { MessageBox.Show("Could not deliver the NXM link to Atlas."); } } else MessageBox.Show(L.T("AlreadyOpen")); return; }
        ApplicationConfiguration.Initialize();
        var data = smoke ? Path.Combine(Path.GetTempPath(), "AtlasSmoke", Guid.NewGuid().ToString("N")) :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtlasCollectionManager");
        try
        {
            var app = new System.Windows.Application();
            var window = new PremiumWindow(data);
            using var inbox = smoke ? null : new NxmInbox(link => app.Dispatcher.BeginInvoke(() => window.ReceiveNxm(link)));
            if (incoming != null && !smoke) window.Loaded += (_, _) => window.ReceiveNxm(incoming);
            if (smoke)
            {
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
                window.Loaded += (_, _) => timer.Start();
            }
            app.Run(window);
        }
        catch (Exception e) { if (smoke) Environment.ExitCode = 1; else MessageBox.Show(e.Message, L.T("OpenFailed")); }
        finally { if (smoke && Directory.Exists(data)) Directory.Delete(data, true); }
    }
}
