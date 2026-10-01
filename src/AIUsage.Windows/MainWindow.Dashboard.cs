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

public sealed partial class MainWindow
{
    private StackPanel Dashboard()
    {
        var body = new StackPanel();
        var rows = dashboard.ForPeriod(period); int records = rows.Sum(r => r.Events);
        decimal cost = rows.Sum(r => r.KnownCost);
        int unpriced = rows.Sum(r => r.Unpriced);
        long total = rows.Sum(r => r.Total), input = rows.Sum(r => r.Input), cached = rows.Sum(r => r.Cached);
        string label = period switch { -1 => T("PeriodYesterday"), 7 => T("Period7"), 30 => T("Period30"), _ => T("PeriodToday") };
        var hero = new StackPanel();
        hero.Children.Add(Text(label + T("EstimatedSuffix"), 10, false, "Muted"));
        hero.Children.Add(Text(records == 0 ? T("NoData") : (unpriced > 0 ? "≥ " : "") + UsageSummary.Dollars(cost, L10n.Culture), 40, true));
        hero.Children.Add(Text(records == 0 ? T("NoDataHelp") :
            F("UsageRecords", UsageSummary.Compact(total, L10n.Culture), records), 13, false, "Muted"));
        if (input > 0)
        {
            var cache = Text(F("CacheReuse", 100d * cached / input), 12, false, "Accent");
            cache.Margin = new Thickness(0, 12, 0, 0); hero.Children.Add(cache);
        }
        var note = Text(T("NotBill"), 11, false, "Muted"); note.Margin = new Thickness(0, 12, 0, 0); hero.Children.Add(note);
        body.Children.Add(Card(hero));
        if (unpriced > 0) body.Children.Add(Notice(F("PartialCostNotice", unpriced)));
        if (scan.Warnings > 0) body.Children.Add(Notice(F("ScanWarning", scan.Warnings)));
        if (rows.Any(r => r.Qualified > 0 && r.KnownCost > 0)) body.Children.Add(Notice(T("QualifiedNotice")));
        if (settingsError.Length > 0) body.Children.Add(Notice(settingsError));
        if (error.Length > 0) body.Children.Add(Notice(error));
        if (pricingError.Length > 0) body.Children.Add(Notice(pricingError));
        body.Children.Add(QuotaCard());
        var models = new StackPanel();
        bool byCost = unpriced == 0 && cost > 0;
        models.Children.Add(Heading(T("ByModel"), byCost ? T("ShareCost") : T("ShareTokens")));
        if (rows.Count == 0) models.Children.Add(Text(T("NoPeriodRecords"), 12, false, "Muted"));
        foreach (var row in rows)
        {
            var item = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            var line = new DockPanel();
            var amount = Text(row.Unpriced > 0 ? (row.KnownCost > 0 ? "≥ " + UsageSummary.Dollars(row.KnownCost, L10n.Culture) : T("NoPrice")) : UsageSummary.Dollars(row.KnownCost, L10n.Culture), 15, true);
            DockPanel.SetDock(amount, Dock.Right); line.Children.Add(amount);
            var model = Text(row.Model, 15, true); model.TextTrimming = TextTrimming.CharacterEllipsis; model.TextWrapping = TextWrapping.NoWrap;
            model.ToolTip = row.Model; model.Margin = new Thickness(0, 0, 12, 0); line.Children.Add(model); item.Children.Add(line);
            double share = byCost ? (double)(row.KnownCost / cost * 100) : total > 0 ? (double)row.Total / total * 100 : 0;
            item.Children.Add(Heading(share.ToString("0.#", L10n.Culture) + "%", $"{UsageSummary.Compact(row.Total, L10n.Culture)} tokens"));
            var bar = Progress(share, "Accent");
            bar.ToolTip = F("TokenDetails", row.Input, row.Cached, row.Output, row.Events);
            item.Children.Add(bar);
            if (row.PricingNotes.Length > 0) item.Children.Add(Text(row.PricingNotes, 10, false, "Warning"));
            models.Children.Add(item);
        }
        body.Children.Add(Card(models));
        body.Children.Add(Trend());
        body.Children.Add(Text(F("PricingFooter", PriceCatalog.SnapshotDate, prices.HasOverrides ? T("CustomSuffix") : "", TimeZoneInfo.Local.Id), 10, false, "Muted"));
        return body;
    }
    private Border QuotaCard()
    {
        var content = new StackPanel();
        content.Children.Add(Heading(T("LimitsTitle"), live?.Plan ?? (settings.OnlineQuota ? T("AccountNotUpdated") : T("LocalLogs"))));
        var windows = QuotaSelection.Select(scan.Quotas, live);
        if (live is null && scan.Quotas.Count > 0) content.Children.Add(Text(T("HistoricalLimits"), 10, false, "Muted"));
        if (settings.OnlineQuota && DateTimeOffset.UtcNow < client.NextAllowedAt)
            content.Children.Add(Text(F("OnlineNotBefore", client.NextAllowedAt.ToLocalTime()), 10, false, "Muted"));
        if (windows.Count == 0)
        {
            content.Children.Add(Text(T("NoLimits"), 13, false, "Muted"));
            content.Children.Add(Text(T("NoLimitsHelp"), 11, false, "Muted"));
        }
        foreach (var item in windows.Take(8))
        {
            var w = item.Window; var q = item.Snapshot;
            var row = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            row.Children.Add(Heading((live is null ? AccountIdentity.Label(q.AccountKey) + " · " : "") + L10n.WindowName(w), F("PercentUsed", w.UsedPercent)));
            row.Children.Add(Progress(w.UsedPercent, w.UsedPercent >= 85 ? "Warning" : "Accent"));
            string reset;
            if (w.ResetAt is null) reset = T("ResetUnknown");
            else if (w.ResetAt <= DateTimeOffset.Now) reset = T("ResetExpired");
            else
            {
                var remaining = w.ResetAt.Value - DateTimeOffset.Now;
                reset = remaining.TotalDays >= 1 ? F("ResetDays", (int)remaining.TotalDays, remaining.Hours) : F("ResetHours", (int)remaining.TotalHours, remaining.Minutes);
            }
            var stamp = Text(reset + "  ·  " + L10n.SourceName(q.Source) + ", " + q.At.ToLocalTime().ToString("g", L10n.Culture), 10, false, "Muted");
            stamp.ToolTip = w.ResetAt?.ToLocalTime().ToString("F", L10n.Culture); stamp.Margin = new Thickness(0, 5, 0, 0); row.Children.Add(stamp);
            content.Children.Add(row);
        }
        if (live?.Credits is not null) content.Children.Add(Text(T("CreditsReported") + L10n.CreditValue(live.Credits), 11, false, "Muted"));
        if (live?.ResetCredits is not null) content.Children.Add(Text(T("ResetsAvailable") + live.ResetCredits.Value.ToString("N0", L10n.Culture), 11, false, "Muted"));
        return Card(content);
    }
    private Border Trend()
    {
        var panel = new StackPanel(); panel.Children.Add(Heading(T("Activity7"), "tokens"));
        var bars = new UniformGrid { Columns = 7, Margin = new Thickness(0, 10, 0, 0) };
        var days = Enumerable.Range(0, 7).Select(i => DateOnly.FromDateTime(DateTime.Today).AddDays(i - 6)).ToArray();
        long[] counts = days.Select(d => dashboard.DailyTokens.GetValueOrDefault(d)).ToArray();
        long max = Math.Max(1, counts.Max());
        for (int i = 0; i < 7; i++)
        {
            var slot = new StackPanel { Margin = new Thickness(3, 0, 3, 0) };
            var frame = new Grid { Height = 55 };
            frame.Children.Add(new Border { Height = Math.Max(2, (double)counts[i] / max * 55), Background = Theme.Brush(i == 6 ? "Accent" : "Tint"), CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Bottom, ToolTip = days[i].ToString("d", L10n.Culture) + ": " + UsageSummary.Compact(counts[i], L10n.Culture) + " tokens" });
            slot.Children.Add(frame);
            var day = Text(days[i].ToString("ddd", L10n.Culture), 10, false, "Muted"); day.HorizontalAlignment = HorizontalAlignment.Center;
            slot.Children.Add(day); bars.Children.Add(slot);
        }
        panel.Children.Add(bars); return Card(panel);
    }
}
