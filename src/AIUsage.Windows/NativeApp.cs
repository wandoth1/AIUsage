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
            if (!args.Contains("--smoke-test")) MessageBox.Show("AIUsage no pudo continuar (" + e.Exception.GetType().Name + "). No se han enviado datos.", "AIUsage");
            app.Shutdown(1);
        };
        return app.Run();
    }
    private async Task Start(string[] args)
    {
        bool smoke = args.Contains("--smoke-test");
        bool demo = smoke || args.Contains("--demo");
        if (!smoke)
        {
            instance = new Mutex(true, @"Local\AIUsage.Windows.Instance", out bool first);
            if (!first)
            {
                try { using var signal = EventWaitHandle.OpenExisting(@"Local\AIUsage.Windows.Show"); signal.Set(); }
                catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("AIUsage ya está abierto. Busca su icono junto al reloj.", "AIUsage"); }
                Shutdown(); return;
            }
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AIUsage.Windows.Show");
        }
        Theme.Apply(false);
        panel = new MainWindow(demo, () => Shutdown());
        MainWindow = panel;
        if (smoke)
        {
            int index = Array.IndexOf(args, "--smoke-test");
            string folder = index + 1 < args.Length ? args[index + 1] : "artifacts";
            Directory.CreateDirectory(folder);
            panel.Width = 560; panel.Height = 940; panel.MaxHeight = double.PositiveInfinity;
            panel.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(panel, Path.Combine(folder, "AIUsage-dark-demo.png"));
            panel.ChangeTheme(true);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(panel, Path.Combine(folder, "AIUsage-light-demo.png"));
            File.WriteAllText(Path.Combine(folder, "ui-smoke-ok.txt"), "WPF window, dark/light layouts and synthetic dashboard rendered successfully. No credentials read.\n");
            Shutdown(); return;
        }
        icon = MakeIcon();
        tray = new Forms.NotifyIcon { Icon = icon, Text = "AIUsage · Codex", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir AIUsage", null, (_, _) => ShowPanel());
        menu.Items.Add("Actualizar", null, async (_, _) => await panel.RefreshAsync());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Dispatcher.Invoke(() => Shutdown()));
        tray.ContextMenuStrip = menu;
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(ShowPanel); };
        panel.UsageChanged += text => { if (tray is not null) tray.Text = text.Length > 63 ? text[..63] : text; };
        wait = ThreadPool.RegisterWaitForSingleObject(showEvent!, (_, _) => Dispatcher.BeginInvoke(new Action(ShowPanel)), null, Timeout.Infinite, false);
        if (!args.Contains("--tray")) ShowPanel();
        await panel.RefreshAsync();
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
        using var file = File.Create(path); encoder.Save(file);
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
        panel?.Stop();
        wait?.Unregister(null);
        showEvent?.Dispose();
        if (tray is not null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); }
        icon?.Dispose(); instance?.Dispose();
        base.OnExit(e);
    }
}
