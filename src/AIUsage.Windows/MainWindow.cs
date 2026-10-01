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

public sealed class MainWindow : Window
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
    private DateTimeOffset lastOnlineAttempt = DateTimeOffset.MinValue;
    private string error = "", pricingError = "";
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
            { error = "No se pudieron leer los ajustes. Se usan los valores por defecto; el archivo original no se ha sobrescrito."; }
        }
        scanner = new LogScanner(Path.Combine(dataDir, "cache"));
        Theme.Apply(settings.LightTheme);
        if (demo) CreateDemo();
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
            error = "";
            if (settings.OnlineQuota && DateTimeOffset.Now - lastOnlineAttempt >= TimeSpan.FromSeconds(60))
            {
                lastOnlineAttempt = DateTimeOffset.Now;
                try { live = await client.ReadAsync(home, cancellation.Token); }
                catch (InvalidOperationException ex) { error = ex.Message; }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { error = "La consulta online ha agotado el tiempo de espera. Los datos locales siguen disponibles."; }
                catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or UnauthorizedAccessException or JsonException)
                { error = "No se pudieron consultar los límites de la cuenta (" + ex.GetType().Name + "). Se conservan los datos anteriores con su fecha."; }
            }
            var today = UsageSummary.Between(scan.Events, DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today), TimeZoneInfo.Local);
            var rows = UsageSummary.Group(today, prices);
            string amount = rows.Any(r => r.Unpriced > 0) ? "coste parcial" : UsageSummary.Dollars(rows.Sum(r => r.KnownCost));
            UsageChanged?.Invoke(today.Count == 0 ? "AIUsage · Hoy: sin datos locales" : $"AIUsage · Hoy {amount} · {UsageSummary.Compact(rows.Sum(r => r.Total))} tokens");
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
    }
    private StackPanel Dashboard()
    {
        var body = new StackPanel();
        var selected = Selected(); var rows = UsageSummary.Group(selected, prices);
        decimal cost = rows.Sum(r => r.KnownCost);
        int unpriced = rows.Sum(r => r.Unpriced);
        long total = rows.Sum(r => r.Total), input = rows.Sum(r => r.Input), cached = rows.Sum(r => r.Cached);
        string label = period switch { -1 => "AYER", 7 => "ÚLTIMOS 7 DÍAS", 30 => "ÚLTIMOS 30 DÍAS", _ => "HOY" };
        var hero = new StackPanel();
        hero.Children.Add(Text(label + " · COSTE API ESTIMADO", 10, false, "Muted"));
        hero.Children.Add(Text(selected.Count == 0 ? "Sin datos" : (unpriced > 0 ? "≥ " : "") + UsageSummary.Dollars(cost), 40, true));
        hero.Children.Add(Text(selected.Count == 0 ? "Abre Codex y realiza una petición, o revisa la carpeta en Ajustes." :
            $"{UsageSummary.Compact(total)} tokens  ·  {selected.Count:N0} registros de uso", 13, false, "Muted"));
        if (input > 0)
        {
            var cache = Text($"{100d * cached / input:0.#}% de la entrada reutilizada desde caché", 12, false, "Accent");
            cache.Margin = new Thickness(0, 12, 0, 0); hero.Children.Add(cache);
        }
        var note = Text("No es un cargo de tu suscripción ni una factura de OpenAI.", 11, false, "Muted"); note.Margin = new Thickness(0, 12, 0, 0); hero.Children.Add(note);
        body.Children.Add(Card(hero));
        if (unpriced > 0) body.Children.Add(Notice($"Coste parcial: {unpriced} registros sin tarifa conocida. Sus tokens sí están incluidos; no se les asigna un precio inventado."));
        if (scan.Warnings > 0) body.Children.Add(Notice($"Lectura posiblemente incompleta: {scan.Warnings} incidencias en archivos, registros o carpetas. No se han sustituido por consumo cero."));
        if (error.Length > 0) body.Children.Add(Notice(error));
        if (pricingError.Length > 0) body.Children.Add(Notice(pricingError));
        body.Children.Add(QuotaCard());
        var models = new StackPanel();
        bool byCost = unpriced == 0 && cost > 0;
        models.Children.Add(Heading("POR MODELO", byCost ? "% del coste estimado" : "% de tokens"));
        if (rows.Count == 0) models.Children.Add(Text("Todavía no hay registros en este periodo.", 12, false, "Muted"));
        foreach (var row in rows)
        {
            var item = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            var line = new DockPanel();
            var amount = Text(row.Unpriced > 0 ? (row.KnownCost > 0 ? "≥ " + UsageSummary.Dollars(row.KnownCost) : "Sin tarifa") : UsageSummary.Dollars(row.KnownCost), 15, true);
            DockPanel.SetDock(amount, Dock.Right); line.Children.Add(amount);
            var model = Text(row.Model, 15, true); model.TextTrimming = TextTrimming.CharacterEllipsis; model.TextWrapping = TextWrapping.NoWrap;
            model.ToolTip = row.Model; model.Margin = new Thickness(0, 0, 12, 0); line.Children.Add(model); item.Children.Add(line);
            double share = byCost ? (double)(row.KnownCost / cost * 100) : total > 0 ? (double)row.Total / total * 100 : 0;
            item.Children.Add(Heading($"{share:0.#}%", $"{UsageSummary.Compact(row.Total)} tokens"));
            var bar = Progress(share, "Accent");
            bar.ToolTip = $"Entrada: {row.Input:N0}\nCaché (incluida en entrada): {row.Cached:N0}\nSalida: {row.Output:N0}\nRegistros: {row.Events:N0}";
            item.Children.Add(bar); models.Children.Add(item);
        }
        body.Children.Add(Card(models));
        body.Children.Add(Trend());
        body.Children.Add(Text($"Tarifas: {PriceCatalog.SnapshotDate}{(prices.HasOverrides ? " + personalizadas" : "")}. Costes teóricos con ese catálogo; no reconstruyen promociones, cargos por herramientas ni recargos regionales. Días en {TimeZoneInfo.Local.DisplayName}.", 10, false, "Muted"));
        return body;
    }
    private Border QuotaCard()
    {
        var content = new StackPanel();
        content.Children.Add(Heading("LÍMITES DE CODEX", live?.Plan ?? (settings.OnlineQuota ? "Cuenta + registros" : "Registros locales")));
        var snapshots = scan.Quotas.ToList(); if (live is not null) snapshots.Add(live);
        var windows = snapshots.SelectMany(q => q.Windows.Select(w => (Snapshot: q, Window: w)))
            .GroupBy(x => x.Window.Name).Select(g => g.OrderByDescending(x => x.Snapshot.At).First()).OrderBy(x => x.Window.Name).ToList();
        if (windows.Count == 0)
        {
            content.Children.Add(Text("Sin información de límites todavía.", 13, false, "Muted"));
            content.Children.Add(Text("Los logs pueden incluirlos. La consulta online se activa por separado en Ajustes.", 11, false, "Muted"));
        }
        foreach (var item in windows.Take(8))
        {
            var w = item.Window; var q = item.Snapshot;
            var row = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            row.Children.Add(Heading(w.Name, $"{w.UsedPercent:0.#}% usado"));
            row.Children.Add(Progress(w.UsedPercent, w.UsedPercent >= 85 ? "Warning" : "Accent"));
            string reset;
            if (w.ResetAt is null) reset = "Reinicio no informado";
            else if (w.ResetAt <= DateTimeOffset.Now) reset = "Reinicio vencido · pendiente de actualizar";
            else
            {
                var remaining = w.ResetAt.Value - DateTimeOffset.Now;
                reset = remaining.TotalDays >= 1 ? $"Reinicio en {(int)remaining.TotalDays} d {remaining.Hours} h" : $"Reinicio en {(int)remaining.TotalHours} h {remaining.Minutes} min";
            }
            var stamp = Text($"{reset}  ·  {q.Source}, {q.At.ToLocalTime():dd/MM HH:mm}", 10, false, "Muted");
            stamp.ToolTip = w.ResetAt?.ToLocalTime().ToString("F"); stamp.Margin = new Thickness(0, 5, 0, 0); row.Children.Add(stamp);
            content.Children.Add(row);
        }
        if (live?.Credits is not null) content.Children.Add(Text("Créditos informados: " + live.Credits, 11, false, "Muted"));
        if (live?.ResetCredits is not null) content.Children.Add(Text("Reinicios disponibles: " + live.ResetCredits, 11, false, "Muted"));
        return Card(content);
    }
    private Border Trend()
    {
        var panel = new StackPanel(); panel.Children.Add(Heading("ACTIVIDAD · 7 DÍAS", "tokens"));
        var bars = new UniformGrid { Columns = 7, Margin = new Thickness(0, 10, 0, 0) };
        var days = Enumerable.Range(0, 7).Select(i => DateOnly.FromDateTime(DateTime.Today).AddDays(i - 6)).ToArray();
        long[] counts = days.Select(d => scan.Events.Where(e => UsageSummary.Day(e.At, TimeZoneInfo.Local) == d).Sum(e => e.Tokens.Total)).ToArray();
        long max = Math.Max(1, counts.Max());
        for (int i = 0; i < 7; i++)
        {
            var slot = new StackPanel { Margin = new Thickness(3, 0, 3, 0) };
            var frame = new Grid { Height = 55 };
            frame.Children.Add(new Border { Height = Math.Max(2, (double)counts[i] / max * 55), Background = Theme.Brush(i == 6 ? "Accent" : "Tint"), CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Bottom, ToolTip = $"{days[i]:dd/MM}: {UsageSummary.Compact(counts[i])} tokens" });
            slot.Children.Add(frame);
            var day = Text(days[i].ToString("ddd", CultureInfo.GetCultureInfo("es-ES")), 10, false, "Muted"); day.HorizontalAlignment = HorizontalAlignment.Center;
            slot.Children.Add(day); bars.Children.Add(slot);
        }
        panel.Children.Add(bars); return Card(panel);
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
                settings = next; live = null; lastOnlineAttempt = DateTimeOffset.MinValue;
                scanner = new LogScanner(Path.Combine(dataDir, "cache"));
                timer.Interval = TimeSpan.FromSeconds(seconds); settingsView = false;
                ChangeTheme(settings.LightTheme); await RefreshAsync();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se han aplicado los ajustes", MessageBoxButton.OK, MessageBoxImage.Warning); }
        });
        save.Background = Theme.Brush("Tint"); save.Foreground = Theme.Brush("Accent"); save.Margin = new Thickness(0, 20, 0, 14); body.Children.Add(save);
        body.Children.Add(Text("AIUsage 0.1.0 · Port funcional parcial de OpenUsage v0.7.12, MIT. Esta versión integra Codex; no incluye los demás proveedores, pi ni OpenCode. Lee el historial accesible de la carpeta seleccionada, no todo el consumo cloud ni de otros equipos. No se añade al inicio de Windows.", 11, false, "Muted"));
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
