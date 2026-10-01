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

public sealed partial class MainWindow
{
    private StackPanel Dashboard()
    {
        var body = new StackPanel();
        var rows = dashboard.ForPeriod(period); int records = rows.Sum(r => r.Events);
        decimal cost = rows.Sum(r => r.KnownCost);
        int unpriced = rows.Sum(r => r.Unpriced);
        long total = rows.Sum(r => r.Total), input = rows.Sum(r => r.Input), cached = rows.Sum(r => r.Cached);
        string label = period switch { -1 => "AYER", 7 => "ÚLTIMOS 7 DÍAS", 30 => "ÚLTIMOS 30 DÍAS", _ => "HOY" };
        var hero = new StackPanel();
        hero.Children.Add(Text(label + " · COSTE API ESTIMADO", 10, false, "Muted"));
        hero.Children.Add(Text(records == 0 ? "Sin datos" : (unpriced > 0 ? "≥ " : "") + UsageSummary.Dollars(cost), 40, true));
        hero.Children.Add(Text(records == 0 ? "Abre Codex y realiza una petición, o revisa la carpeta en Ajustes." :
            $"{UsageSummary.Compact(total)} tokens  ·  {records:N0} registros de uso", 13, false, "Muted"));
        if (input > 0)
        {
            var cache = Text($"{100d * cached / input:0.#}% de la entrada reutilizada desde caché", 12, false, "Accent");
            cache.Margin = new Thickness(0, 12, 0, 0); hero.Children.Add(cache);
        }
        var note = Text("No es un cargo de tu suscripción ni una factura de OpenAI.", 11, false, "Muted"); note.Margin = new Thickness(0, 12, 0, 0); hero.Children.Add(note);
        body.Children.Add(Card(hero));
        if (unpriced > 0) body.Children.Add(Notice($"Coste parcial: {unpriced} registros sin tarifa conocida. Sus tokens sí están incluidos; no se les asigna un precio inventado."));
        if (scan.Warnings > 0) body.Children.Add(Notice($"Lectura posiblemente incompleta: {scan.Warnings} incidencias en archivos, registros o carpetas. Los archivos con contabilidad ambigua pueden quedar excluidos para no duplicar consumo."));
        if (rows.Any(r => r.Qualified > 0 && r.KnownCost > 0)) body.Children.Add(Notice("Algunas estimaciones tienen condiciones, equivalencias o tarifas personalizadas. Consulta la nota bajo cada modelo y el CSV."));
        if (settingsError.Length > 0) body.Children.Add(Notice(settingsError));
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
            item.Children.Add(bar);
            if (row.PricingNotes.Length > 0) item.Children.Add(Text(row.PricingNotes, 10, false, "Warning"));
            models.Children.Add(item);
        }
        body.Children.Add(Card(models));
        body.Children.Add(Trend());
        body.Children.Add(Text($"Tarifas: {PriceCatalog.SnapshotDate}{(prices.HasOverrides ? " + personalizadas" : "")}. Costes teóricos con ese catálogo; no reconstruyen promociones, cargos por herramientas ni recargos regionales. Días en {TimeZoneInfo.Local.DisplayName}.", 10, false, "Muted"));
        return body;
    }
    private Border QuotaCard()
    {
        var content = new StackPanel();
        content.Children.Add(Heading("LÍMITES DE CODEX", live?.Plan ?? (settings.OnlineQuota ? "Registros · cuenta no actualizada" : "Registros locales")));
        var windows = QuotaSelection.Select(scan.Quotas, live);
        if (live is null && scan.Quotas.Count > 0) content.Children.Add(Text("Límites históricos de los registros, no de una cuenta online verificada. Las identidades desconocidas no pueden separarse con certeza.", 10, false, "Muted"));
        if (settings.OnlineQuota && DateTimeOffset.UtcNow < client.NextAllowedAt)
            content.Children.Add(Text($"Consulta online no antes de {client.NextAllowedAt.ToLocalTime():HH:mm:ss}.", 10, false, "Muted"));
        if (windows.Count == 0)
        {
            content.Children.Add(Text("Sin información de límites todavía.", 13, false, "Muted"));
            content.Children.Add(Text("Los logs pueden incluirlos. La consulta online se activa por separado en Ajustes.", 11, false, "Muted"));
        }
        foreach (var item in windows.Take(8))
        {
            var w = item.Window; var q = item.Snapshot;
            var row = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            row.Children.Add(Heading((live is null ? AccountIdentity.Label(q.AccountKey) + " · " : "") + w.Name, $"{w.UsedPercent:0.#}% usado"));
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
        long[] counts = days.Select(d => dashboard.DailyTokens.GetValueOrDefault(d)).ToArray();
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
}
