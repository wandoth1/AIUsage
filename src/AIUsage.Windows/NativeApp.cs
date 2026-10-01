using AIUsage.Core;
using static AIUsage.Core.L10n;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace AIUsage.Windows;

public sealed class NativeApp : Application
{
    private Forms.NotifyIcon? tray;
    private Mutex? instance;
    private EventWaitHandle? showEvent;
    private RegisteredWaitHandle? wait;
    private MainWindow? panel;
    private Icon? icon;

    [STAThread]
    public static int Main(string[] args)
    {
        var app = new NativeApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) => await app.Start(args);
        app.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            if (!args.Contains("--smoke-test")) MessageBox.Show(F("AppFailed", e.Exception.GetType().Name), "AIUsage");
            else
            {
                int index = Array.IndexOf(args, "--smoke-test");
                string folder = LocalPaths.Require(Path.GetFullPath(index + 1 < args.Length ? args[index + 1] : "artifacts"));
                try { Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "ui-smoke-error.txt"), e.Exception.ToString()); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            app.Shutdown(1);
        };
        return app.Run();
    }
    private async Task Start(string[] args)
    {
        bool smoke = args.Contains("--smoke-test");
        bool demo = smoke || args.Contains("--demo");
        int languageIndex = Array.IndexOf(args, "--language");
        string? language = languageIndex >= 0 && languageIndex + 1 < args.Length ? args[languageIndex + 1] : null;
        L10n.SetLanguage(language);
        string instanceName = InstancePolicy.InstanceName(demo);
        string eventName = InstancePolicy.EventName(demo);
        if (!smoke)
        {
            if (InstancePolicy.LegacyRunning(demo))
            {
                MessageBox.Show(T("EarlierVersionRunning"), "AIUsage " + AppVersion.Value, MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown(2); return;
            }
            instance = new Mutex(true, instanceName, out bool first);
            if (!first)
            {
                try { using var signal = EventWaitHandle.OpenExisting(eventName); signal.Set(); }
                catch (WaitHandleCannotBeOpenedException) { MessageBox.Show(T("AlreadyRunning"), "AIUsage"); }
                Shutdown(); return;
            }
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        }
        Theme.Apply(false);
        panel = new MainWindow(demo, () => Shutdown(), language);
        MainWindow = panel;
        if (smoke)
        {
            int index = Array.IndexOf(args, "--smoke-test");
            string folder = LocalPaths.Require(Path.GetFullPath(index + 1 < args.Length ? args[index + 1] : "artifacts"));
            Directory.CreateDirectory(folder);
            int fixtureIndex = Array.IndexOf(args, "--local-fixture");
            if (fixtureIndex >= 0)
            {
                if (fixtureIndex + 1 >= args.Length) throw new ArgumentException("Missing synthetic fixture path.");
                var local = new MainWindow(false, () => { }, "en", args[fixtureIndex + 1]);
                try { local.Show(); await local.VerifyLocalOnlyAsync(); }
                finally { local.Stop(); local.Close(); }
                File.WriteAllText(LocalPaths.Require(Path.Combine(folder, "local-smoke-ok.txt")), "Normal-mode local totals and settings migration passed with inaccessible synthetic credentials.\n".Replace("\n", Environment.NewLine));
            }
            panel.Width = 560; panel.Height = 940; panel.MaxHeight = double.PositiveInfinity;
            panel.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            foreach (string code in new[] { "en", "es" })
            {
                await panel.VerifyDemoLanguageAsync(code);
                await panel.VerifyDemoRefreshAsync();
                using var localizedMenu = CreateTrayMenu();
                if (localizedMenu.Items[0].Text != (code == "es" ? "Abrir AIUsage" : "Open AIUsage") || localizedMenu.Items[3].Text != (code == "es" ? "Salir" : "Exit")) throw new InvalidOperationException("Tray menu localization failed.");
                foreach (bool light in new[] { false, true })
                {
                    panel.ChangeTheme(light);
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    string theme = light ? "light" : "dark";
                    Capture(panel, Path.Combine(folder, $"AIUsage-{code}-{theme}-demo.png"));
                    panel.ShowDemoSettings(true);
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Capture(panel, Path.Combine(folder, $"AIUsage-{code}-{theme}-settings.png"));
                    panel.ShowDemoSettings(false);
                }
            }
            File.WriteAllText(Path.Combine(folder, "ui-smoke-ok.txt"), "English/Spanish settings selection, unchanged synthetic totals, localized tray menus, dark/light dashboards and settings, and scroll/focus checks passed. No real logs or credentials read.\n");
            Shutdown(); return;
        }
        icon = MakeIcon();
        tray = new Forms.NotifyIcon { Icon = icon, Text = "AIUsage · Codex", Visible = true };
        tray.ContextMenuStrip = CreateTrayMenu();
        panel.LanguageChanged += () =>
        {
            if (tray is null) return;
            var previous = tray.ContextMenuStrip;
            tray.ContextMenuStrip = CreateTrayMenu();
            tray.Text = "AIUsage · Codex";
            previous?.Dispose();
        };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(ShowPanel); };
        panel.UsageChanged += text => { if (tray is not null) tray.Text = text.Length > 63 ? text[..63] : text; };
        wait = ThreadPool.RegisterWaitForSingleObject(showEvent!, (_, _) => Dispatcher.BeginInvoke(new Action(ShowPanel)), null, Timeout.Infinite, false);
        if (!args.Contains("--tray")) ShowPanel();
        await panel.RefreshAsync();
    }
    private Forms.ContextMenuStrip CreateTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(T("OpenApp"), null, (_, _) => ShowPanel());
        menu.Items.Add(T("Refresh"), null, async (_, _) => { if (panel is not null) await panel.RefreshAsync(); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(T("Exit"), null, (_, _) => Dispatcher.Invoke(() => Shutdown()));
        return menu;
    }
    private void ShowPanel()
    {
        if (panel is null) return;
        if (!panel.IsVisible)
        {
            var area = SystemParameters.WorkArea;
            panel.Height = Math.Min(820, area.Height - 24);
            panel.Left = Math.Max(area.Left + 8, area.Right - panel.Width - 12);
            panel.Top = Math.Max(area.Top + 8, area.Bottom - panel.Height - 12);
            panel.Show();
        }
        panel.Activate();
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(LocalPaths.Require(path)); encoder.Save(file);
    }
    private static Icon MakeIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(System.Drawing.Color.FromArgb(18, 24, 39));
            using var b = new SolidBrush(System.Drawing.Color.FromArgb(97, 151, 255));
            g.FillRectangle(b, 5, 18, 5, 9); g.FillRectangle(b, 13, 11, 5, 16); g.FillRectangle(b, 21, 5, 5, 22);
        }
        IntPtr handle = bitmap.GetHicon();
        try { using var temporary = Icon.FromHandle(handle); return (Icon)temporary.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
    protected override void OnExit(ExitEventArgs e)
    {
        panel?.Stop(); wait?.Unregister(null); showEvent?.Dispose();
        if (tray is not null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); }
        icon?.Dispose(); instance?.Dispose(); base.OnExit(e);
    }
}
