using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using CameywareOrder.Controls.Charts;
using CameywareOrder.Data;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CameywareOrder.Views;

/// <summary>
/// The settlement report: what the shop took over a period, by service line and by payment method.
/// </summary>
/// <remarks>
/// <b>This window computes nothing.</b> It loads orders, hands them to
/// <see cref="SettlementCalculator"/>, and renders what comes back. Every figure on screen —
/// including the ones in the charts and the ones in the PDF — is the same
/// <see cref="SettlementReport"/> object, so the three cannot disagree.
///
/// It reads through a <see cref="LocalizationScope"/>, so the report can be produced in a language
/// the application is not running in without switching it. Same control and same rule as Measurement
/// Terms: what is being READ follows the preview; the print dialog and any error stay in the
/// operator's own language.
/// </remarks>
public partial class SettlementWindow : Window
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LocalizationService _localization;
    private readonly LocalizationScope _scope;

    private readonly ObservableCollection<MetricRow> _metrics = new();
    private readonly ObservableCollection<MetricRow> _counts = new();
    private readonly ObservableCollection<MetricRow> _payments = new();
    private readonly ObservableCollection<LineRow> _lines = new();

    private DateRange _period = DateRange.CurrentMonth();
    private SettlementReport? _report;
    private bool _loading;

    public SettlementWindow(IServiceScopeFactory scopeFactory, LocalizationService localization)
    {
        InitializeComponent();

        _scopeFactory = scopeFactory;
        _localization = localization;
        _scope = (LocalizationScope)Resources["Scope"];
        _scope.TextChanged += OnScopeChanged;

        MetricList.ItemsSource = _metrics;
        CountList.ItemsSource = _counts;
        PaymentList.ItemsSource = _payments;
        LineList.ItemsSource = _lines;

        _loading = true;
        MonthChip.IsChecked = true;     // month to date is the settlement a shop wants by default
        FromPicker.SelectedDate = _period.Start;
        ToPicker.SelectedDate = _period.LastDay;
        _loading = false;

        RefreshLabels();
        Reload();
    }

    /// <summary>The culture whose month names the period heading uses — the previewed language's.</summary>
    private CultureInfo PreviewCulture => XmlLanguage.GetLanguage(_scope.EffectiveLanguageCode).GetSpecificCulture();

    protected override void OnClosed(EventArgs e)
    {
        _scope.TextChanged -= OnScopeChanged;
        _scope.Detach();
        base.OnClosed(e);
    }

    private void OnScopeChanged(object? sender, EventArgs e)
    {
        // The calendar drop-downs render their month names from this, so it moves with the preview.
        Language = XmlLanguage.GetLanguage(_scope.EffectiveLanguageCode);
        RefreshLabels();
        Render();
    }

    /// <summary>Every fixed piece of chrome, in the previewed language.</summary>
    private void RefreshLabels()
    {
        Title = _scope["Settlement.Title"];
        TitleText.Text = _scope["Settlement.Title"];
        SubtitleText.Text = _scope["Settlement.Subtitle"];
        PrintButton.Content = _scope["Settlement.Print"];

        DayChip.Content = _scope["Settlement.Period.Day"];
        MonthChip.Content = _scope["Settlement.Period.Month"];
        YearChip.Content = _scope["Settlement.Period.Year"];
        CustomChip.Content = _scope["Settlement.Period.Custom"];
        PreviousButton.Content = _scope["Settlement.Period.Previous"];
        NextButton.Content = _scope["Settlement.Period.Next"];
        FromLabel.Text = _scope["Settlement.Period.From"];
        ToLabel.Text = _scope["Settlement.Period.To"];

        ServiceChartTitle.Text = _scope["Settlement.Chart.ByService"];
        MethodChartTitle.Text = _scope["Settlement.Chart.ByMethod"];
        ServicesTitle.Text = _scope["Settlement.Section.Services"];
        OrdersTitle.Text = _scope["Settlement.Section.Orders"];
        PaymentsTitle.Text = _scope["Settlement.Section.Payments"];
        EmptyText.Text = _scope["Settlement.Empty"];

        HeadName.Text = _scope["Settlement.Section.Services"];
        HeadOrders.Text = _scope["Settlement.OrderCount"].Replace("{0}", string.Empty).Trim();
        HeadPreTax.Text = _scope["Settlement.PreTax"];
        HeadTax.Text = _scope["Settlement.Tax"];
        HeadPostTax.Text = _scope["Settlement.PostTax"];
        HeadReceived.Text = _scope["Settlement.Received"];
        HeadOutstanding.Text = _scope["Settlement.Outstanding"];
        TotalName.Text = _scope["Settlement.Section.Revenue"];
    }

    // ── period ────────────────────────────────────────────────────────────────

    private void OnPeriodKindChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        var anchor = _period.Start;
        _period = true switch
        {
            _ when DayChip.IsChecked is true => DateRange.Day(anchor),
            _ when YearChip.IsChecked is true => DateRange.Year(anchor),
            _ when CustomChip.IsChecked is true => CustomFromPickers(),
            _ => DateRange.Month(anchor)
        };

        CustomRangePanel.Visibility = CustomChip.IsChecked is true ? Visibility.Visible : Visibility.Collapsed;

        // Stepping is meaningless once the user has named both ends themselves.
        PreviousButton.IsEnabled = CustomChip.IsChecked is not true;
        NextButton.IsEnabled = PreviousButton.IsEnabled;

        Reload();
    }

    private DateRange CustomFromPickers()
        => DateRange.Custom(
            FromPicker.SelectedDate ?? DateTime.Today,
            ToPicker.SelectedDate ?? DateTime.Today);

    private void OnCustomRangeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CustomChip.IsChecked is not true)
            return;

        _period = CustomFromPickers();
        Reload();
    }

    private void OnPreviousClick(object sender, RoutedEventArgs e) => Step(-1);

    private void OnNextClick(object sender, RoutedEventArgs e) => Step(1);

    private void Step(int periods)
    {
        _period = _period.Shift(periods);
        SyncPickers();
        Reload();
    }

    private void SyncPickers()
    {
        _loading = true;
        FromPicker.SelectedDate = _period.Start;
        ToPicker.SelectedDate = _period.LastDay;
        _loading = false;
    }

    // ── data ──────────────────────────────────────────────────────────────────

    private void Reload()
    {
        var shop = ShopContext.Instance.Current;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orders = db.Orders.AsNoTracking().Include(order => order.Items).ToList();

        _report = SettlementCalculator.For(orders, _period, shop?.CurrencyType ?? CurrencyType.CAD);
        Render();
    }

    private void Render()
    {
        if (_report is not { } report)
            return;

        PeriodText.Text = report.Period.Title(_scope, PreviewCulture);
        GeneratedText.Text = _scope.Format(
            "Settlement.GeneratedOn", DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));

        EmptyText.Visibility = report.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

        RenderMetrics(report);
        RenderLines(report);
        RenderCounts(report);
        RenderPayments(report);
        RenderCharts(report);
    }

    private void RenderMetrics(SettlementReport report)
    {
        _metrics.Clear();
        _metrics.Add(new MetricRow(_scope["Settlement.PreTax"], Money(report.PreTaxTotal), Tint.Neutral));
        _metrics.Add(new MetricRow(_scope["Settlement.Tax"], Money(report.TaxTotal), Tint.Neutral));
        _metrics.Add(new MetricRow(_scope["Settlement.PostTax"], Money(report.PostTaxTotal), Tint.Accent));
        _metrics.Add(new MetricRow(_scope["Settlement.Received"], Money(report.ReceivedTotal), Tint.Good));
        _metrics.Add(new MetricRow(_scope["Settlement.Outstanding"], Money(report.OutstandingTotal), Tint.Warn));
        _metrics.Add(new MetricRow(_scope["Settlement.RefundedValue"], Money(report.RefundedValue), Tint.Bad));
    }

    private void RenderLines(SettlementReport report)
    {
        _lines.Clear();
        for (var i = 0; i < report.Lines.Count; i++)
        {
            var line = report.Lines[i];
            _lines.Add(new LineRow(
                ChartPalette.At(i),
                LineName(line.Line),
                _scope.Format("Settlement.OrderCount", line.OrderCount),
                Money(line.PreTax), Money(line.Tax), Money(line.PostTax),
                Money(line.Received), Money(line.Outstanding)));
        }

        TotalOrders.Text = _scope.Format("Settlement.OrderCount", report.Counts.Earning);
        TotalPreTax.Text = Money(report.PreTaxTotal);
        TotalTax.Text = Money(report.TaxTotal);
        TotalPostTax.Text = Money(report.PostTaxTotal);
        TotalReceived.Text = Money(report.ReceivedTotal);
        TotalOutstanding.Text = Money(report.OutstandingTotal);
    }

    private void RenderCounts(SettlementReport report)
    {
        var counts = report.Counts;
        _counts.Clear();
        _counts.Add(new MetricRow(_scope["Main.Records"], counts.Total.ToString(), Tint.Neutral));
        _counts.Add(new MetricRow(_scope["Settlement.Orders.Unfinished"], counts.Unfinished.ToString(), Tint.Warn));
        _counts.Add(new MetricRow(_scope["Status.Completed"], counts.Completed.ToString(), Tint.Good));
        _counts.Add(new MetricRow(_scope["Status.Shipped"], counts.Shipped.ToString(), Tint.Good));
        _counts.Add(new MetricRow(_scope["Status.Cancelled"], counts.Cancelled.ToString(), Tint.Bad));
        _counts.Add(new MetricRow(_scope["Status.Returned"], counts.Returned.ToString(), Tint.Bad));
    }

    private void RenderPayments(SettlementReport report)
    {
        _payments.Clear();
        _payments.Add(new MetricRow(_scope["PaymentMethod.Cash"], Money(report.CashReceived), Tint.Good));
        _payments.Add(new MetricRow(_scope["Settlement.CardTotal"], Money(report.CardReceived), Tint.Accent));
        _payments.Add(new MetricRow(_scope["PaymentMethod.Etransfer"], Money(report.TransferReceived), Tint.Neutral));

        // Anything else that actually took money — a method added later shows up here without this
        // window being changed.
        foreach (var method in report.Methods)
        {
            if (method.Method is PaymentMethod.Cash or PaymentMethod.Etransfer
                or PaymentMethod.DebitCard or PaymentMethod.CreditCard or PaymentMethod.Card)
            {
                continue;
            }

            _payments.Add(new MetricRow(MethodName(method.Method), Money(method.Received), Tint.Neutral));
        }
    }

    private void RenderCharts(SettlementReport report)
    {
        ServicePie.Slices = report.Lines
            .Select((line, index) => new ChartSlice(LineName(line.Line), (double)line.PostTax, ChartPalette.At(index))
            {
                DisplayValue = Money(line.PostTax)
            })
            .ToList();

        ServicePie.CentreText = Money(report.PostTaxTotal);
        ServicePie.CentreCaption = _scope["Settlement.PostTax"];

        MethodBars.Slices = report.Methods
            .Select((method, index) => new ChartSlice(MethodName(method.Method), (double)method.Received, ChartPalette.At(index))
            {
                DisplayValue = Money(method.Received)
            })
            .ToList();
    }

    // ── printing ──────────────────────────────────────────────────────────────

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        if (_report is not { } report)
            return;

        // The DIALOG is in the operator's own language, never the previewed one — a Save-as they
        // cannot read is not a preview, it is a trap. Same rule the terms panel follows.
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"settlement-{report.Period.Start:yyyy-MM-dd}.pdf",
            Title = _localization["Settlement.Print"]
        };

        if (dialog.ShowDialog() is not true)
            return;

        try
        {
            var branding = ReceiptBrandingStore.Load();
            var language = _scope.EffectiveLanguageCode;

            var content = SettlementContent.Build(
                report, _scope, PreviewCulture, language, Money, LineName, MethodName) with
            {
                // The charts are the ones on screen, rendered rather than redrawn — see ChartImage.
                ServiceChart = ChartImage.Render(ServicePie, 420, 220),
                MethodChart = ChartImage.Render(MethodBars, 420, 220),
                HeaderXaml = branding.ForLanguage(language).HeaderXaml,
                FooterXaml = branding.ForLanguage(language).FooterXaml,
                LogoBytes = ReceiptBrandingStore.GetLogoBytes(branding),
                LogoPlacement = branding.LogoPlacement
            };

            SettlementDocument.Save(content, dialog.FileName);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(
                _localization.Format("Status.PrintFailed", ex.Message),
                _localization["Settlement.Title"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    // ── formatting ────────────────────────────────────────────────────────────

    private string Money(decimal amount)
        => CurrencySettingService.GetSymbol(_report?.Currency ?? CurrencyType.CAD)
           + amount.ToString("N2", CultureInfo.InvariantCulture);

    private string LineName(ServiceLine line) => _scope[line switch
    {
        ServiceLine.Alterations => "ServiceType.Alterations",
        ServiceLine.CustomMade => "ServiceType.CustomMade",
        _ => "ServiceType.ReadyMade"
    }];

    private string MethodName(PaymentMethod method) => _scope[$"PaymentMethod.{method}"];

    /// <summary>The card backgrounds, named by what they mean rather than by colour.</summary>
    private static class Tint
    {
        internal static readonly Brush Neutral = Frozen(0xF3, 0xF4, 0xF6);
        internal static readonly Brush Accent = Frozen(0xEE, 0xF2, 0xFF);
        internal static readonly Brush Good = Frozen(0xE7, 0xF8, 0xEE);
        internal static readonly Brush Warn = Frozen(0xFE, 0xF3, 0xE2);
        internal static readonly Brush Bad = Frozen(0xFD, 0xEC, 0xEA);

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>One KPI card. Bound from XAML; see MetricTemplate.</summary>
    internal sealed record MetricRow(string Caption, string Value, Brush Background);

    /// <summary>One row of the per-service table. Bound from XAML; see LineRowTemplate.</summary>
    internal sealed record LineRow(
        Brush Swatch, string Name, string Orders,
        string PreTax, string Tax, string PostTax, string Received, string Outstanding);
}
