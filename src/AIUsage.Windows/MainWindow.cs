using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AIUsage.Core;
using Forms = System.Windows.Forms;

namespace AIUsage.Windows;

public sealed partial class MainWindow : Window
{
    private readonly bool demo;
    private readonly Action exit;
    private readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIUsage");
    private readonly CodexUsageClient client = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly DispatcherTimer timer = new();
    private AppSettings settings = new();
    private PriceCatalog prices = PriceCatalog.Load();
    private LogScanner scanner;
    private ScanResult scan = new([], [], 0, 0, DateTimeOffset.Now);
    private QuotaSnapshot? live;
    private DashboardData dashboard = new(new(), new());
    private ScrollViewer? currentScroll;
    private int lastBuiltPeriod;
    private bool lastBuiltSettings;
    private string error = "", pricingError = "", settingsError = "";
    private bool busy, settingsView, stopped;
    private int period = 1;
    private TextBlock? status;
    public event Action<string>? UsageChanged;
    private string SettingsPath => Path.Combine(dataDir, "settings.json");
    private string PricesPath => Path.Combine(dataDir, "price-overrides.json");

    public MainWindow(bool demo, Action exit)
    {
        this.demo = demo; this.exit = exit;
        Title = "AIUsage · Codex"; Width = 560; Height = 820; MinWidth = 460; MinHeight = 380;
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip; ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        if (!demo)
        {
            try { settings = AppSettings.Load(SettingsPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            { settingsError = "No se pudieron leer los ajustes. Se usan los valores por defecto; el archivo original no se ha sobrescrito."; }
        }
        scanner = new LogScanner(Path.Combine(dataDir, "cache"));
        Theme.Apply(settings.LightTheme);
        if (demo) { CreateDemo(); dashboard = DashboardData.Create(scan.Events, prices, TimeZoneInfo.Local, DateOnly.FromDateTime(DateTime.Today)); }
        Build();
        Closing += (_, e) => { if (!stopped) { e.Cancel = true; Hide(); } };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
        timer.Interval = TimeSpan.FromSeconds(settings.RefreshSeconds);
        timer.Tick += async (_, _) => await RefreshAsync();
        if (!demo) timer.Start();
    }
    public void ChangeTheme(bool light) { settings.LightTheme = light; Theme.Apply(light); Build(); }
    public void Stop()
    {
        if (stopped) return;
        stopped = true; timer.Stop(); cancellation.Cancel(); client.Dispose();
    }
    public async Task RefreshAsync()
    {
        if (busy || settingsView || stopped) return;
        if (demo) { Build(); return; }
        busy = true;
        if (status is not null) status.Text = "Leyendo registros locales…";
        try
        {
            try { prices = PriceCatalog.Load(PricesPath); pricingError = ""; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            { pricingError = "No se han aplicado los precios personalizados: revisa price-overrides.json. Se mantiene el último catálogo válido."; }
            string home = settings.ResolveHome();
            scan = await Task.Run(() => scanner.Scan(home, cancellation.Token), cancellation.Token);
            dashboard = await Task.Run(() => DashboardData.Create(scan.Events, prices, TimeZoneInfo.Local, DateOnly.FromDateTime(DateTime.Today)), cancellation.Token);
            error = "";
            if (settings.OnlineQuota && DateTimeOffset.UtcNow >= client.NextAllowedAt)
            {
                live = null; // A failed request must not keep limits from a former account.
                try { live = await client.ReadAsync(home, cancellation.Token); }
                catch (InvalidOperationException ex) { error = ex.Message; }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { error = "La consulta online ha agotado el tiempo de espera. Los datos locales siguen disponibles."; }
                catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or UnauthorizedAccessException or JsonException or FormatException)
                { error = "No se pudieron consultar los límites de la cuenta (" + ex.GetType().Name + "). No se reutilizan cuotas online anteriores; los registros locales conservan su origen y fecha."; }
            }
            if (!settings.OnlineQuota) live = null;
            var rows = dashboard.ForPeriod(1);
            string amount = rows.Any(r => r.Unpriced > 0) ? "coste parcial" : UsageSummary.Dollars(rows.Sum(r => r.KnownCost));
            UsageChanged?.Invoke(rows.Sum(r => r.Events) == 0 ? "AIUsage · Hoy: sin datos locales" : $"AIUsage · Hoy {amount} · {UsageSummary.Compact(rows.Sum(r => r.Total))} tokens");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = "No se pudo leer la carpeta de Codex. Comprueba la ruta y sus permisos en Ajustes (" + ex.GetType().Name + ")."; }
        finally { busy = false; if (!stopped && !settingsView) Build(); }
    }
    private List<UsageEvent> Selected()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var end = period == -1 ? today.AddDays(-1) : today;
        var start = period <= 1 ? end : end.AddDays(1 - period);
        return UsageSummary.Between(scan.Events, start, end, TimeZoneInfo.Local);
    }
    private void Build()
    {
        bool sameView = lastBuiltPeriod == period && lastBuiltSettings == settingsView;
        double offset = sameView ? currentScroll?.VerticalOffset ?? 0 : 0;
        string? focusedButton = sameView && Keyboard.FocusedElement is Button oldButton ? oldButton.Content as string : null;
        lastBuiltPeriod = period; lastBuiltSettings = settingsView;
        Background = Theme.Brush("Page"); Foreground = Theme.Brush("Ink");
        var grid = new Grid();
        foreach (var h in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) grid.RowDefinitions.Add(new RowDefinition { Height = h });
        var header = new DockPanel { Margin = new Thickness(22, 20, 16, 16) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        actions.Children.Add(Button(Topmost ? "Fijado" : "Fijar", () => { Topmost = !Topmost; Build(); }));
        actions.Children.Add(Button("Ajustes", () => { settingsView = !settingsView; Build(); }));
        actions.Children.Add(Button("×", Hide));
        DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        var title = new StackPanel(); title.Children.Add(Text("AIUsage", 25, true));
        title.Children.Add(Text(demo ? "DEMO · DATOS SINTÉTICOS" : "CODEX · WINDOWS", 10, false, "Accent"));
        header.Children.Add(title);
        header.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
        grid.Children.Add(header);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 0, 16, 14) };
        if (settingsView) nav.Children.Add(Button("← Volver al consumo", () => { settingsView = false; Build(); }));
        else foreach (var (label, value) in new[] { ("Hoy", 1), ("Ayer", -1), ("7 días", 7), ("30 días", 30) })
        {
            var button = Button(label, () => { period = value; Build(); });
            if (period == value) { button.Background = Theme.Brush("Tint"); button.Foreground = Theme.Brush("Accent"); }
            nav.Children.Add(button);
        }
        Grid.SetRow(nav, 1); grid.Children.Add(nav);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(22, 0, 16, 12) };
        currentScroll = scroll;
        scroll.Content = settingsView ? SettingsPanel() : Dashboard();
        Grid.SetRow(scroll, 2); grid.Children.Add(scroll);
        var footer = new StackPanel { Margin = new Thickness(22, 10, 22, 15) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(Button("Actualizar", async () => await RefreshAsync()));
        tools.Children.Add(Button("Exportar CSV", Export));
        tools.Children.Add(Button("GitHub", () => Open("https://github.com/wandoth1/AIUsage")));
        tools.Children.Add(Button("Salir", exit)); footer.Children.Add(tools);
        status = Text(demo ? "Vista de demostración. No se leen archivos ni credenciales." :
            $"{scan.At:HH:mm:ss} · {scan.Files} archivos accesibles · Solo este equipo", 10, false, "Muted");
        status.Margin = new Thickness(0, 10, 0, 0); footer.Children.Add(status);
        Grid.SetRow(footer, 3); grid.Children.Add(footer);
        Content = new Border { BorderBrush = Theme.Brush("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = grid };
        // Rebuilding the visual tree must not throw the reader back to the top on every refresh.
        scroll.Loaded += (_, _) =>
        {
            scroll.ScrollToVerticalOffset(offset);
            if (focusedButton is not null) FindButton(grid, focusedButton)?.Focus();
        };
    }
    internal async Task VerifyDemoRefreshAsync()
    {
        if (!demo) throw new InvalidOperationException("UI regression checks require synthetic demo mode.");
        double originalHeight = Height; Height = 420;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        currentScroll!.ScrollToEnd();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        double expected = currentScroll.VerticalOffset;
        if (expected <= 0) throw new InvalidOperationException("The UI test did not create scrollable content.");
        FindButton((DependencyObject)Content, "Actualizar")!.Focus();
        Build();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (Math.Abs(currentScroll!.VerticalOffset - expected) > 1)
            throw new InvalidOperationException("Refresh lost the scroll position.");
        if (Keyboard.FocusedElement is not Button b || !Equals(b.Content, "Actualizar"))
            throw new InvalidOperationException("Refresh lost keyboard focus.");
        Height = originalHeight; currentScroll.ScrollToTop();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
    private static Button? FindButton(DependencyObject parent, string text)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button button && Equals(button.Content, text)) return button;
            var nested = FindButton(child, text); if (nested is not null) return nested;
        }
        return null;
    }
    private StackPanel SettingsPanel()
    {
        var body = new StackPanel();
        body.Children.Add(Text("Configuración", 23, true));
        body.Children.Add(Text("Privado por defecto. Sin telemetría ni subida de conversaciones.", 12, false, "Muted"));
        body.Children.Add(Label("Carpeta de Codex"));
        var home = new TextBox { Text = settings.CodexHome, ToolTip = "Carpeta .codex que contiene sessions y archived_sessions. También admite una ruta WSL accesible desde Windows." };
        body.Children.Add(home);
        body.Children.Add(Text(@"Vacío = CODEX_HOME o %USERPROFILE%\.codex", 11, false, "Muted"));
        var folders = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        folders.Children.Add(Button("Elegir carpeta…", () =>
        {
            using var dialog = new Forms.FolderBrowserDialog { Description = "Selecciona .codex (o una carpeta de sesiones)", ShowNewFolderButton = false, UseDescriptionForTitle = true };
            if (dialog.ShowDialog() == Forms.DialogResult.OK) home.Text = dialog.SelectedPath;
        }));
        folders.Children.Add(Button("Datos de AIUsage", () => { if (!demo) { Directory.CreateDirectory(dataDir); Open(dataDir); } }));
        body.Children.Add(folders);
        body.Children.Add(Button("Reconstruir caché de lectura", async () =>
        {
            if (demo || busy) return;
            try { scanner.ClearCache(); settingsView = false; await RefreshAsync(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { MessageBox.Show(this, "No se pudo reconstruir la caché. Los registros originales no se han modificado.", "AIUsage"); }
        }));
        body.Children.Add(Text("Úsalo tras editar registros antiguos. Elimina solo la caché de AIUsage, no tus sesiones ni credenciales.", 10, false, "Muted"));
        body.Children.Add(Label("Actualizar cada N segundos (15–3600)"));
        var interval = new TextBox { Text = settings.RefreshSeconds.ToString(CultureInfo.InvariantCulture), MaxLength = 4 }; body.Children.Add(interval);
        var light = new CheckBox { Content = "Tema claro", IsChecked = settings.LightTheme }; body.Children.Add(light);
        var online = new CheckBox { Content = "Consultar los límites de mi cuenta online", IsChecked = settings.OnlineQuota };
        online.Checked += (_, _) =>
        {
            if (!settings.OnlineQuota && MessageBox.Show(this,
                "AIUsage leerá el access_token de auth.json y lo enviará únicamente a chatgpt.com para consultar tus límites. No copiará ni renovará las credenciales, y no enviará conversaciones. Es un endpoint interno que puede cambiar. ¿Activar?",
                "Consulta online opcional", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) online.IsChecked = false;
        };
        body.Children.Add(online);
        body.Children.Add(Text("Como máximo una consulta por minuto. No modifica auth.json, no reclama reinicios y no realiza peticiones al modelo. Si tu sesión caduca, renuévala desde Codex. El almacén de credenciales de Windows no se lee en esta versión.", 11, false, "Muted"));
        body.Children.Add(Label("Tarifas y modelos nuevos"));
        body.Children.Add(Text("Los modelos desconocidos mantienen sus tokens visibles. Puedes añadir o corregir tarifas locales en USD por millón, sin recompilar.", 12, false, "Muted"));
        var editPrices = Button("Editar precios personalizados", () =>
        {
            if (demo) return;
            try
            {
                Directory.CreateDirectory(dataDir);
                if (!File.Exists(PricesPath)) File.WriteAllText(PricesPath, "{}\n");
                var p = new ProcessStartInfo("notepad.exe") { UseShellExecute = false }; p.ArgumentList.Add(PricesPath); Process.Start(p);
            }
            catch (Exception ex) { MessageBox.Show(this, "No se pudo abrir el editor (" + ex.GetType().Name + ").", "AIUsage"); }
        }); editPrices.Margin = new Thickness(0, 10, 0, 10); body.Children.Add(editPrices);
        body.Children.Add(Text("Ejemplo: {\"mi-modelo\":{\"Input\":2,\"Cached\":0.1,\"Output\":10,\"LongThreshold\":272000,\"FastMultiplier\":2}}", 11, false, "Muted"));
        var save = Button("Guardar y actualizar", async () =>
        {
            try
            {
                if (busy) throw new InvalidOperationException("Espera a que termine la actualización actual.");
                if (!int.TryParse(interval.Text, out int seconds) || seconds is < 15 or > 3600) throw new InvalidOperationException("El intervalo debe estar entre 15 y 3600 segundos.");
                var next = new AppSettings { CodexHome = home.Text.Trim(), RefreshSeconds = seconds, LightTheme = light.IsChecked == true, OnlineQuota = online.IsChecked == true };
                string resolved = next.ResolveHome();
                if (next.CodexHome.Length > 0 && !Directory.Exists(resolved)) throw new InvalidOperationException("La carpeta indicada no existe o no es accesible.");
                if (!demo) AtomicJson.Write(SettingsPath, next);
                settings = next; live = null; settingsError = "";
                scan = new([], [], 0, 0, DateTimeOffset.Now); dashboard = new(new(), new());
                if (demo) { CreateDemo(); dashboard = DashboardData.Create(scan.Events, prices, TimeZoneInfo.Local, DateOnly.FromDateTime(DateTime.Today)); }
                scanner = new LogScanner(Path.Combine(dataDir, "cache"));
                timer.Interval = TimeSpan.FromSeconds(seconds); settingsView = false;
                ChangeTheme(settings.LightTheme); await RefreshAsync();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se han aplicado los ajustes", MessageBoxButton.OK, MessageBoxImage.Warning); }
        });
        save.Background = Theme.Brush("Tint"); save.Foreground = Theme.Brush("Accent"); save.Margin = new Thickness(0, 20, 0, 14); body.Children.Add(save);
        body.Children.Add(Text("AIUsage 0.1.1 · Port funcional parcial de OpenUsage v0.7.12, MIT. Esta versión integra Codex; no incluye los demás proveedores, pi ni OpenCode. Lee el historial accesible de la carpeta seleccionada, no todo el consumo cloud ni de otros equipos. No se añade al inicio de Windows.", 11, false, "Muted"));
        return body;
    }
    private void Export()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "AIUsage-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv", Filter = "CSV (*.csv)|*.csv", AddExtension = true };
            if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, CsvExport.Build(Selected(), prices, TimeZoneInfo.Local), new UTF8Encoding(true));
        }
        catch (Exception ex) { MessageBox.Show(this, "No se pudo exportar (" + ex.GetType().Name + ").", "AIUsage"); }
    }
    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("No se pudo abrir el destino (" + ex.GetType().Name + ").", "AIUsage"); }
    }
    private static TextBlock Text(string text, double size = 13, bool bold = false, string color = "Ink") => new()
    { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Theme.Brush(color), TextWrapping = TextWrapping.Wrap };
    private static TextBlock Label(string text) { var t = Text(text, 12, true); t.Margin = new Thickness(0, 18, 0, 8); return t; }
    private static Button Button(string text, Action action)
    {
        var b = new Button { Content = text }; b.Click += (_, _) => action(); return b;
    }
    private static Border Card(UIElement child) => new()
    { Child = child, Background = Theme.Brush("Surface"), BorderBrush = Theme.Brush("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 12) };
    private static Border Notice(string message) => new()
    { Child = Text(message, 12, false, "Warning"), Background = Theme.Brush("Surface"), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12) };
    private static DockPanel Heading(string left, string right)
    {
        var line = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var r = Text(right, 11, false, "Muted"); r.Margin = new Thickness(12, 0, 0, 0); DockPanel.SetDock(r, Dock.Right); line.Children.Add(r);
        line.Children.Add(Text(left, 11, false, "Muted")); return line;
    }
    private static Border Progress(double percent, string color)
    {
        double fraction = Math.Clamp(percent, 0, 100);
        var grid = new Grid { Height = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - fraction, GridUnitType.Star) });
        grid.Children.Add(new Border { Background = Theme.Brush(color), CornerRadius = new CornerRadius(3) });
        return new Border { Child = grid, Background = Theme.Brush("Raised"), CornerRadius = new CornerRadius(3) };
    }
    private void CreateDemo()
    {
        var events = new List<UsageEvent>();
        for (int day = 0; day < 7; day++)
            for (int i = 0; i < 32 + day * 3; i++)
            {
                var at = new DateTimeOffset(DateTime.Today.AddDays(-day).AddHours(10).AddMinutes(i));
                events.Add(new(at, i % 9 == 0 ? "gpt-6-astra" : "gpt-6.1-sol", new Tokens(96000, 84000, 2500, 1800, 98500)));
            }
        live = new(DateTimeOffset.Now, "DEMO", "Pro", [new("Codex · Sesión", 34, DateTimeOffset.Now.AddHours(2.5), 18000), new("Codex · Semanal", 62, DateTimeOffset.Now.AddDays(3), 604800)]);
        scan = new(events, [], 38, 0, DateTimeOffset.Now);
    }
}
