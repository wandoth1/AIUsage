using static AIUsage.Core.L10n;
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
    public event Action? LanguageChanged;
    private string SettingsPath => Path.Combine(dataDir, "settings.json");
    private string PricesPath => Path.Combine(dataDir, "price-overrides.json");

    public MainWindow(bool demo, Action exit, string? languageOverride = null)
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
            { settingsError = T("SettingsReadFailed"); }
        }
        settings.Language = L10n.NormalizeSetting(languageOverride ?? settings.Language);
        ApplyLanguage();
        if (settingsError.Length > 0) settingsError = T("SettingsReadFailed");
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
    private void ApplyLanguage()
    {
        L10n.SetLanguage(settings.Language);
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(L10n.Culture.Name);
        LanguageChanged?.Invoke();
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
        if (status is not null) status.Text = T("ReadingLogs");
        try
        {
            try { prices = PriceCatalog.Load(PricesPath); pricingError = ""; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            { pricingError = T("CustomPricesFailed"); }
            string home = settings.ResolveHome();
            scan = await Task.Run(() => scanner.Scan(home, cancellation.Token), cancellation.Token);
            dashboard = await Task.Run(() => DashboardData.Create(scan.Events, prices, TimeZoneInfo.Local, DateOnly.FromDateTime(DateTime.Today)), cancellation.Token);
            error = "";
            if (settings.OnlineQuota && DateTimeOffset.UtcNow >= client.NextAllowedAt)
            {
                live = null; // A failed request must not keep limits from a former account.
                try { live = await client.ReadAsync(home, cancellation.Token); }
                catch (InvalidOperationException ex) { error = ex.Message; }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { error = T("OnlineTimeout"); }
                catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException or UnauthorizedAccessException or JsonException or FormatException)
                { error = F("OnlineFailed", ex.GetType().Name); }
            }
            if (!settings.OnlineQuota) live = null;
            var rows = dashboard.ForPeriod(1);
            string amount = rows.Any(r => r.Unpriced > 0) ? T("PartialCost") : UsageSummary.Dollars(rows.Sum(r => r.KnownCost), L10n.Culture);
            UsageChanged?.Invoke(rows.Sum(r => r.Events) == 0 ? T("TrayNoData") : F("TrayUsage", amount, UsageSummary.Compact(rows.Sum(r => r.Total), L10n.Culture)));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = F("ReadFolderFailed", ex.GetType().Name); }
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
        actions.Children.Add(Button(Topmost ? T("Pinned") : T("Pin"), () => { Topmost = !Topmost; Build(); }));
        actions.Children.Add(Button(T("Settings"), () => { settingsView = !settingsView; Build(); }));
        actions.Children.Add(Button("×", Hide));
        DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        var title = new StackPanel(); title.Children.Add(Text("AIUsage", 25, true));
        title.Children.Add(Text(demo ? T("DemoBadge") : "CODEX · WINDOWS", 10, false, "Accent"));
        header.Children.Add(title);
        header.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
        grid.Children.Add(header);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 0, 16, 14) };
        if (settingsView) nav.Children.Add(Button(T("BackToUsage"), () => { settingsView = false; Build(); }));
        else foreach (var (label, value) in new[] { (T("Today"), 1), (T("Yesterday"), -1), (T("Days7"), 7), (T("Days30"), 30) })
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
        tools.Children.Add(Button(T("Refresh"), async () => await RefreshAsync()));
        tools.Children.Add(Button(T("ExportCsv"), Export));
        tools.Children.Add(Button("GitHub", () => Open("https://github.com/wandoth1/AIUsage")));
        tools.Children.Add(Button(T("Exit"), exit)); footer.Children.Add(tools);
        status = Text(demo ? T("DemoStatus") :
            F("LocalStatus", scan.At, scan.Files), 10, false, "Muted");
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
        FindButton((DependencyObject)Content, T("Refresh"))!.Focus();
        Build();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (Math.Abs(currentScroll!.VerticalOffset - expected) > 1)
            throw new InvalidOperationException("Refresh lost the scroll position.");
        if (Keyboard.FocusedElement is not Button b || !Equals(b.Content, T("Refresh")))
            throw new InvalidOperationException("Refresh lost keyboard focus.");
        Height = originalHeight; currentScroll.ScrollToTop();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
    internal async Task VerifyDemoLanguageAsync(string code)
    {
        if (!demo) throw new InvalidOperationException("Language smoke tests require demo mode.");
        long before = scan.Events.Sum(e => e.Tokens.Total);
        settingsView = true; Build();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var radio = Descendants((DependencyObject)Content).OfType<RadioButton>().Single(r => Equals(r.Tag, code));
        radio.IsChecked = true;
        FindButton((DependencyObject)Content, T("SaveRefresh"))!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (settings.Language != code || L10n.Code != code || settingsView || scan.Events.Sum(e => e.Tokens.Total) != before)
            throw new InvalidOperationException("Language selection changed accounting or failed to apply.");
        string expected = code == "es" ? "Ajustes" : "Settings";
        if (FindButton((DependencyObject)Content, expected) is null)
            throw new InvalidOperationException("The dashboard was not translated.");
    }
    internal void ShowDemoSettings(bool show)
    {
        if (!demo) throw new InvalidOperationException("Synthetic mode required.");
        settingsView = show; Build();
    }
    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
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
        body.Children.Add(Text(T("Configuration"), 23, true));
        body.Children.Add(Text(T("PrivacyIntro"), 12, false, "Muted"));
        body.Children.Add(Label(T("Language")));
        var languages = new WrapPanel();
        RadioButton? chosenLanguage = null;
        foreach (var (code, name) in new[] { ("auto", T("LanguageSystem")), ("en", "English"), ("es", "Español") })
        {
            var option = new RadioButton { Content = name, Tag = code, GroupName = "UiLanguage", IsChecked = settings.Language == code,
                Foreground = Theme.Brush("Ink"), Margin = new Thickness(0, 0, 18, 8), Padding = new Thickness(4, 0, 0, 0) };
            if (option.IsChecked == true) chosenLanguage = option;
            option.Checked += (_, _) => chosenLanguage = option;
            languages.Children.Add(option);
        }
        body.Children.Add(languages);
        body.Children.Add(Text(T("LanguageHelp"), 11, false, "Muted"));
        body.Children.Add(Label(T("CodexFolder")));
        var home = new TextBox { Text = settings.CodexHome, ToolTip = T("FolderHelp") };
        body.Children.Add(home);
        body.Children.Add(Text(T("DefaultFolder"), 11, false, "Muted"));
        var folders = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        folders.Children.Add(Button(T("ChooseFolder"), () =>
        {
            using var dialog = new Forms.FolderBrowserDialog { Description = T("ChooseFolderTitle"), ShowNewFolderButton = false, UseDescriptionForTitle = true };
            if (dialog.ShowDialog() == Forms.DialogResult.OK) home.Text = dialog.SelectedPath;
        }));
        folders.Children.Add(Button(T("AppData"), () => { if (!demo) { Directory.CreateDirectory(dataDir); Open(dataDir); } }));
        body.Children.Add(folders);
        body.Children.Add(Button(T("RebuildCache"), async () =>
        {
            if (demo || busy) return;
            try { scanner.ClearCache(); settingsView = false; await RefreshAsync(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { MessageBox.Show(this, T("RebuildFailed"), "AIUsage"); }
        }));
        body.Children.Add(Text(T("RebuildHelp"), 10, false, "Muted"));
        body.Children.Add(Label(T("RefreshInterval")));
        var interval = new TextBox { Text = settings.RefreshSeconds.ToString(CultureInfo.InvariantCulture), MaxLength = 4 }; body.Children.Add(interval);
        var light = new CheckBox { Content = T("LightTheme"), IsChecked = settings.LightTheme }; body.Children.Add(light);
        var online = new CheckBox { Content = T("OnlineOption"), IsChecked = settings.OnlineQuota };
        online.Checked += (_, _) =>
        {
            if (!settings.OnlineQuota && MessageBox.Show(this,
                T("OnlineConsent"),
                T("OnlineTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) online.IsChecked = false;
        };
        body.Children.Add(online);
        body.Children.Add(Text(T("OnlineHelp"), 11, false, "Muted"));
        body.Children.Add(Label(T("NewPrices")));
        body.Children.Add(Text(T("NewPricesHelp"), 12, false, "Muted"));
        var editPrices = Button(T("EditPrices"), () =>
        {
            if (demo) return;
            try
            {
                Directory.CreateDirectory(dataDir);
                if (!File.Exists(PricesPath)) File.WriteAllText(PricesPath, "{}\n");
                var p = new ProcessStartInfo("notepad.exe") { UseShellExecute = false }; p.ArgumentList.Add(PricesPath); Process.Start(p);
            }
            catch (Exception ex) { MessageBox.Show(this, F("EditorFailed", ex.GetType().Name), "AIUsage"); }
        }); editPrices.Margin = new Thickness(0, 10, 0, 10); body.Children.Add(editPrices);
        body.Children.Add(Text(T("PriceExample"), 11, false, "Muted"));
        var save = Button(T("SaveRefresh"), async () =>
        {
            try
            {
                if (busy) throw new InvalidOperationException(T("WaitForRefresh"));
                if (!int.TryParse(interval.Text, out int seconds) || seconds is < 15 or > 3600) throw new InvalidOperationException(T("InvalidInterval"));
                var next = new AppSettings { CodexHome = home.Text.Trim(), RefreshSeconds = seconds, LightTheme = light.IsChecked == true, OnlineQuota = online.IsChecked == true, Language = L10n.NormalizeSetting(chosenLanguage?.Tag as string) };
                string resolved = next.ResolveHome();
                if (next.CodexHome.Length > 0 && !Directory.Exists(resolved)) throw new InvalidOperationException(T("MissingFolder"));
                if (!demo) AtomicJson.Write(SettingsPath, next);
                settings = next; live = null; settingsError = ""; pricingError = ""; error = "";
                ApplyLanguage();
                scan = new([], [], 0, 0, DateTimeOffset.Now); dashboard = new(new(), new());
                if (demo) { CreateDemo(); dashboard = DashboardData.Create(scan.Events, prices, TimeZoneInfo.Local, DateOnly.FromDateTime(DateTime.Today)); }
                scanner = new LogScanner(Path.Combine(dataDir, "cache"));
                timer.Interval = TimeSpan.FromSeconds(seconds); settingsView = false;
                ChangeTheme(settings.LightTheme); await RefreshAsync();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, T("SettingsNotApplied"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        });
        save.Background = Theme.Brush("Tint"); save.Foreground = Theme.Brush("Accent"); save.Margin = new Thickness(0, 20, 0, 14); body.Children.Add(save);
        body.Children.Add(Text(F("About", AppVersion.Value), 11, false, "Muted"));
        return body;
    }
    private void Export()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "AIUsage-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv", Filter = "CSV (*.csv)|*.csv", AddExtension = true };
            if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, CsvExport.Build(Selected(), prices, TimeZoneInfo.Local), new UTF8Encoding(true));
        }
        catch (Exception ex) { MessageBox.Show(this, F("ExportFailed", ex.GetType().Name), "AIUsage"); }
    }
    private static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(F("OpenFailed", ex.GetType().Name), "AIUsage"); }
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
        live = new(DateTimeOffset.Now, "DEMO", "Pro", [new("Codex · Session", 34, DateTimeOffset.Now.AddHours(2.5), 18000), new("Codex · Weekly", 62, DateTimeOffset.Now.AddDays(3), 604800)]);
        scan = new(events, [], 38, 0, DateTimeOffset.Now);
    }
}
