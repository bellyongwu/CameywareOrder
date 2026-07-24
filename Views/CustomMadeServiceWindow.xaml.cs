using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LeeYongeOrdering.Localization;
using LeeYongeOrdering.Models;
using Microsoft.Win32;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LeeYongeOrdering.Views;

public partial class CustomMadeServiceWindow : Window
{
    public CustomMadeServiceRecord? Result { get; private set; }

    private static readonly Regex MoneyInputPattern = new(@"^\d*(\.\d{0,2})?$");
    private static readonly Regex MeasurementInputPattern = new(@"^(\d+(\.\d*)?[+-]?)?$");
    // Splits a measurement into its numeric part and an optional trailing +/- so a
    // unit conversion only touches the digits (e.g. "20+" -> convert 20, keep "+").
    private static readonly Regex MeasurementNumberPattern = new(@"^(\d+(?:\.\d*)?)([+-]?)$");
    private const decimal CentimetersPerInch = 2.54m;

    private readonly LocalizationService _localization;
    private readonly string? _defaultOrderNumber;
    private readonly CustomMadeServiceRecord _workingRecord;
    private bool _isInitializing;
    private bool _isRefreshingLanguage;
    private bool _isInch;

    public CustomMadeServiceWindow(LocalizationService localization, CustomMadeServiceRecord? existing = null, string? defaultOrderNumber = null, string? defaultCustomerName = null, string? defaultPhoneNumber = null, string? defaultEmail = null)
    {
        _isInitializing = true;
        InitializeComponent();
        _localization = localization;
        _defaultOrderNumber = defaultOrderNumber;
        _workingRecord = existing is null ? new CustomMadeServiceRecord() : Clone(existing);

        CustomerNameBox.Text = _workingRecord.CustomerName = existing?.CustomerName ?? defaultCustomerName ?? string.Empty;
        PhoneNumberBox.Text = _workingRecord.PhoneNumber = existing?.PhoneNumber ?? defaultPhoneNumber ?? string.Empty;
        EmailBox.Text = _workingRecord.Email = existing?.Email ?? defaultEmail;

        JacketLengthBox.Text = _workingRecord.JacketLength = existing?.JacketLength;
        JacketChestBox.Text = _workingRecord.JacketChest = existing?.JacketChest;
        JacketSitAroundBox.Text = _workingRecord.JacketSitAround = existing?.JacketSitAround;
        JacketSleevesBox.Text = _workingRecord.JacketSleeves = existing?.JacketSleeves;
        ShirtLengthBox.Text = _workingRecord.ShirtLength = existing?.ShirtLength;
        ShirtChestBox.Text = _workingRecord.ShirtChest = existing?.ShirtChest;
        ShirtSitAroundBox.Text = _workingRecord.ShirtSitAround = existing?.ShirtSitAround;
        ShirtSleevesBox.Text = _workingRecord.ShirtSleeves = existing?.ShirtSleeves;
        CustomPriceBox.Text = _workingRecord.Price?.ToString("0.##") ?? string.Empty;

        RegisterInputFilters();

        InitializeMode(existing?.ServiceMode ?? CustomMadeServiceMode.CustomFromScratch);
        InitializeAgeType(existing?.AgeType ?? CustomMadeAgeType.AdultMale);

        if (_localization.CurrentLanguageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            DownloadEnglishRadio.IsChecked = true;

        _isInitializing = false;

        RefreshMeasurementContextText();
        RefreshGenderButtons(_workingRecord.AgeType);
        RefreshCustomPriceTotals();
        _localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_isRefreshingLanguage)
            return;

        _isRefreshingLanguage = true;
        try
        {
            RefreshMeasurementContextText();
            RefreshGenderButtons(_workingRecord.AgeType);
        }
        finally
        {
            _isRefreshingLanguage = false;
        }
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
            return;

        SelectMode(GetSelectedMode());
    }

    private void OnAgeGroupChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
            return;

        RefreshGenderButtons();
        RefreshMeasurementContextText();
    }

    private void OnPriceValuesChanged(object sender, TextChangedEventArgs e)
        => RefreshCustomPriceTotals();

    private void OnUnitChanged(object sender, RoutedEventArgs e)
    {
        if (_isInitializing)
            return;

        var toInch = InchRadio.IsChecked.GetValueOrDefault();
        if (toInch == _isInch)
            return;

        ConvertMeasurementBoxes(toInch);
        _isInch = toInch;
    }

    private void ConvertMeasurementBoxes(bool toInch)
    {
        foreach (var box in new[]
        {
            JacketLengthBox, JacketChestBox, JacketSitAroundBox, JacketSleevesBox,
            ShirtLengthBox, ShirtChestBox, ShirtSitAroundBox, ShirtSleevesBox
        })
        {
            box.Text = ConvertMeasurement(box.Text, toInch);
        }
    }

    private static string ConvertMeasurement(string? text, bool toInch)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? string.Empty;

        var trimmed = text.Trim();
        var match = MeasurementNumberPattern.Match(trimmed);
        if (!match.Success || !decimal.TryParse(match.Groups[1].Value, out var value))
            return trimmed;

        var converted = toInch ? value / CentimetersPerInch : value * CentimetersPerInch;
        var rounded = Math.Round(converted, 2, MidpointRounding.AwayFromZero);
        return rounded.ToString("0.##") + match.Groups[2].Value;
    }

    private string? MeasurementForStorage(string? text)
    {
        // Canonical storage is always in cm so receipts/summaries stay consistent.
        var normalized = _isInch ? ConvertMeasurement(text, toInch: false) : text;
        return NullIfWhiteSpace(normalized);
    }

    private void OnDownloadSubmitClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var languageCode = DownloadEnglishRadio.IsChecked.GetValueOrDefault() ? "en-US" : "zh-CN";
            var saveDialog = new SaveFileDialog
            {
                Filter = "PDF files (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = $"{BuildPdfFileName(languageCode)}.pdf"
            };

            if (saveDialog.ShowDialog() != true)
                return;

            SaveMeasurementsPdf(saveDialog.FileName, languageCode);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            MessageBox.Show(ex.Message, _localization["OrderEdit.ValidationTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var customerName = CustomerNameBox.Text.Trim();
        var phoneNumber = PhoneNumberBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(customerName))
        {
            ErrorText.Text = _localization["OrderEdit.Validate.CustomerName"];
            MessageBox.Show(_localization["OrderEdit.Validate.CustomerName"], _localization["OrderEdit.ValidationTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            ErrorText.Text = _localization["OrderEdit.Validate.PhoneNumber"];
            MessageBox.Show(_localization["OrderEdit.Validate.PhoneNumber"], _localization["OrderEdit.ValidationTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _workingRecord.CustomerName = customerName;
        _workingRecord.PhoneNumber = phoneNumber;
        _workingRecord.Email = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim();
        _workingRecord.ServiceMode = GetSelectedMode();
        _workingRecord.AgeType = GetSelectedAgeType();
        _workingRecord.JacketLength = MeasurementForStorage(JacketLengthBox.Text);
        _workingRecord.JacketChest = MeasurementForStorage(JacketChestBox.Text);
        _workingRecord.JacketSitAround = MeasurementForStorage(JacketSitAroundBox.Text);
        _workingRecord.JacketSleeves = MeasurementForStorage(JacketSleevesBox.Text);
        _workingRecord.ShirtLength = MeasurementForStorage(ShirtLengthBox.Text);
        _workingRecord.ShirtChest = MeasurementForStorage(ShirtChestBox.Text);
        _workingRecord.ShirtSitAround = MeasurementForStorage(ShirtSitAroundBox.Text);
        _workingRecord.ShirtSleeves = MeasurementForStorage(ShirtSleevesBox.Text);
        _workingRecord.Price = ParseNullableDecimal(CustomPriceBox.Text);
        _workingRecord.TaxRate = null;

        Result = Clone(_workingRecord);
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void InitializeMode(CustomMadeServiceMode mode)
    {
        MeasurementsOnlyRadio.IsChecked = mode == CustomMadeServiceMode.MeasurementsOnly;
        CustomFromScratchRadio.IsChecked = mode == CustomMadeServiceMode.CustomFromScratch;
    }

    private void SelectMode(CustomMadeServiceMode mode)
    {
        MeasurementsOnlyRadio.IsChecked = mode == CustomMadeServiceMode.MeasurementsOnly;
        CustomFromScratchRadio.IsChecked = mode == CustomMadeServiceMode.CustomFromScratch;
        RefreshGenderButtons();
        RefreshMeasurementContextText();
    }

    private CustomMadeServiceMode GetSelectedMode()
        => CustomFromScratchRadio?.IsChecked == true
            ? CustomMadeServiceMode.CustomFromScratch
            : CustomMadeServiceMode.MeasurementsOnly;

    private void InitializeAgeType(CustomMadeAgeType ageType)
    {
        _workingRecord.AgeType = ageType;

        switch (ageType)
        {
            case CustomMadeAgeType.AdultMale:
            case CustomMadeAgeType.AdultFemale:
                AdultRadio.IsChecked = true;
                break;
            case CustomMadeAgeType.TeenBoy:
            case CustomMadeAgeType.TeenGirl:
                TeenRadio.IsChecked = true;
                break;
            default:
                ChildRadio.IsChecked = true;
                break;
        }
    }

    private CustomMadeAgeType GetSelectedAgeType()
    {
        return _workingRecord.AgeType;
    }

    private void RefreshGenderButtons(CustomMadeAgeType? selected = null)
    {
        GenderButtonsPanel.Children.Clear();

        var ageType = selected ?? _workingRecord.AgeType;
        CustomMadeAgeType[] options;
        if (AdultRadio.IsChecked == true)
            options = new[] { CustomMadeAgeType.AdultMale, CustomMadeAgeType.AdultFemale };
        else if (TeenRadio.IsChecked == true)
            options = new[] { CustomMadeAgeType.TeenBoy, CustomMadeAgeType.TeenGirl };
        else
            options = new[] { CustomMadeAgeType.ChildBoy, CustomMadeAgeType.ChildGirl };

        if (!options.Contains(ageType))
            ageType = options[0];

        _workingRecord.AgeType = ageType;

        foreach (var option in options)
        {
            var button = new ToggleButton
            {
                Content = GetAgeTypeLabel(option),
                Tag = option,
                IsChecked = option == ageType,
                Style = (Style)FindResource("SelectionToggleButtonStyle")
            };
            button.Click += (_, _) =>
            {
                ClearGenderDefaults();
                button.IsChecked = true;
                _workingRecord.AgeType = option;
                RefreshMeasurementContextText();
            };
            GenderButtonsPanel.Children.Add(button);
        }

        var defaultButton = GenderButtonsPanel.Children.OfType<ToggleButton>().FirstOrDefault(button => button.Tag is CustomMadeAgeType current && current.Equals(ageType));
        if (defaultButton is not null)
            defaultButton.IsChecked = true;
    }

    private void ClearGenderDefaults()
    {
        foreach (var button in GenderButtonsPanel.Children.OfType<ToggleButton>())
            button.IsChecked = false;
    }

    private void RefreshMeasurementContextText()
        => MeasurementContextText.Text = _localization.Format(
            "Customer.Measurements.Context",
            GetAgeGroupLabel(),
            GetAgeTypeLabel(_workingRecord.AgeType));

    private string GetAgeGroupLabel()
    {
        if (AdultRadio.IsChecked == true)
            return _localization["OrderEdit.Panel.Adult"];
        if (TeenRadio.IsChecked == true)
            return _localization["OrderEdit.Panel.Teen"];
        return _localization["OrderEdit.Panel.Child"];
    }

    private string GetAgeGroupLabel(string languageCode)
    {
        if (AdultRadio.IsChecked == true)
            return _localization.GetText("OrderEdit.Panel.Adult", languageCode);
        if (TeenRadio.IsChecked == true)
            return _localization.GetText("OrderEdit.Panel.Teen", languageCode);
        return _localization.GetText("OrderEdit.Panel.Child", languageCode);
    }

    private void SaveMeasurementsPdf(string filePath, string languageCode)
    {
        string L(string key) => _localization.GetText(key, languageCode);

        var infoRows = new List<(string Label, string Value)>();
        AddInfoRow(infoRows, L("Order.Fields.OrderNumber"), _defaultOrderNumber);
        AddInfoRow(infoRows, L("Order.Fields.CustomerName"), CustomerNameBox.Text);
        AddInfoRow(infoRows, L("Order.Fields.PhoneNumber"), PhoneNumberBox.Text);
        AddInfoRow(infoRows, L("Order.Fields.Email"), EmailBox.Text);
        AddInfoRow(infoRows, L("OrderEdit.Panel.MeasurementMode"), GetModeLabel(GetSelectedMode(), languageCode));
        AddInfoRow(infoRows, L("OrderEdit.Panel.AgeType"), $"{GetAgeGroupLabel(languageCode)} / {GetAgeTypeLabel(_workingRecord.AgeType, languageCode)}");
        AddInfoRow(infoRows, L("Measure.Unit.Label"), L(_isInch ? "Measure.Unit.Inch" : "Measure.Unit.Cm"));

        var jacketRows = FilterMeasurementRows(new (string Label, string? Value)[]
        {
            (L("Measure.Length"), JacketLengthBox.Text),
            (L("Measure.Chest"), JacketChestBox.Text),
            (L("Measure.SitAround"), JacketSitAroundBox.Text),
            (L("Measure.Sleeves"), JacketSleevesBox.Text)
        });

        var shirtRows = FilterMeasurementRows(new (string Label, string? Value)[]
        {
            (L("Measure.Length"), ShirtLengthBox.Text),
            (L("Measure.Chest"), ShirtChestBox.Text),
            (L("Measure.SitAround"), ShirtSitAroundBox.Text),
            (L("Measure.Sleeves"), ShirtSleevesBox.Text)
        });

        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(text => text.FontSize(11));

                page.Content().Column(column =>
                {
                    column.Spacing(6);
                    column.Item().Text(L("Customer.Measurements.PrintTitle")).Bold().FontSize(18);

                    foreach (var (label, value) in infoRows)
                    {
                        column.Item().Row(row =>
                        {
                            row.ConstantItem(190).Text(label).SemiBold();
                            row.RelativeItem().Text($": {value}");
                        });
                    }

                    AddPdfMeasurementSection(column, L("Measure.Section.Jacket"), jacketRows);
                    AddPdfMeasurementSection(column, L("Measure.Section.Shirt"), shirtRows);
                });
            });
        }).GeneratePdf(filePath);
    }

    private static List<(string Label, string Value)> FilterMeasurementRows(IEnumerable<(string Label, string? Value)> rows)
        => rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Value))
            .Select(row => (row.Label, row.Value!.Trim()))
            .ToList();

    private static void AddInfoRow(List<(string Label, string Value)> rows, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        rows.Add((label, value.Trim()));
    }

    private static void AddPdfMeasurementSection(ColumnDescriptor column, string sectionTitle, IReadOnlyList<(string Label, string Value)> rows)
    {
        if (rows.Count == 0)
            return;

        column.Item().PaddingTop(8).Text(sectionTitle).Bold().FontSize(13);

        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(190);
                columns.RelativeColumn();
            });

            foreach (var (label, value) in rows)
            {
                table.Cell().PaddingVertical(2).Text(label).SemiBold();
                table.Cell().PaddingVertical(2).Text(value);
            }
        });
    }

    private string BuildPdfFileName(string languageCode)
    {
        var orderNumber = string.IsNullOrWhiteSpace(_defaultOrderNumber) ? "Order" : _defaultOrderNumber.Trim();
        var customerName = string.IsNullOrWhiteSpace(CustomerNameBox.Text) ? "Customer" : CustomerNameBox.Text.Trim();
        var combined = $"{orderNumber} {customerName}";
        var invalidChars = System.IO.Path.GetInvalidFileNameChars();
        var sanitized = new string(combined.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "Measurements";

        // Requirement 3: replace spaces (and any underscore runs) with a single "_",
        // then end the file name with the short language name (zh/en).
        sanitized = Regex.Replace(sanitized, @"[\s_]+", "_").Trim('_');
        return $"{sanitized}_{ShortLanguageName(languageCode)}";
    }

    private static string ShortLanguageName(string languageCode)
        => languageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";

    private void RefreshCustomPriceTotals()
    {
        var subtotal = ParseDecimalOrZero(CustomPriceBox.Text);
        CustomSubtotalText.Text = subtotal.ToString("0.00");
    }

    private void RegisterInputFilters()
    {
        DataObject.AddPastingHandler(CustomPriceBox, (s, e) => HandlePaste(s, e, MoneyInputPattern));

        foreach (var box in new[]
        {
            JacketLengthBox, JacketChestBox, JacketSitAroundBox, JacketSleevesBox,
            ShirtLengthBox, ShirtChestBox, ShirtSitAroundBox, ShirtSleevesBox
        })
        {
            DataObject.AddPastingHandler(box, (s, e) => HandlePaste(s, e, MeasurementInputPattern));
        }
    }

    private void OnMoneyPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is TextBox box)
            e.Handled = !MoneyInputPattern.IsMatch(GetProposedText(box, e.Text));
    }

    private void OnMeasurementPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is TextBox box)
            e.Handled = !MeasurementInputPattern.IsMatch(GetProposedText(box, e.Text));
    }

    private static void HandlePaste(object sender, DataObjectPastingEventArgs e, Regex pattern)
    {
        if (sender is not TextBox box)
            return;

        if (!e.SourceDataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pasted = e.SourceDataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        if (!pattern.IsMatch(GetProposedText(box, pasted)))
            e.CancelCommand();
    }

    private static string GetProposedText(TextBox textBox, string newText)
    {
        var current = textBox.Text ?? string.Empty;
        return current.Remove(textBox.SelectionStart, textBox.SelectionLength).Insert(textBox.SelectionStart, newText);
    }

    private static decimal ParseDecimalOrZero(string? value)
        => decimal.TryParse(value, out var result) ? result : 0m;

    private static decimal? ParseNullableDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return decimal.TryParse(value, out var result) ? result : null;
    }

    private string GetModeLabel(CustomMadeServiceMode mode, string languageCode)
        => _localization.GetText(mode == CustomMadeServiceMode.CustomFromScratch
            ? "OrderEdit.Panel.CustomFromScratch"
            : "OrderEdit.Panel.MeasurementsOnly", languageCode);

    private string GetAgeTypeLabel(CustomMadeAgeType ageType)
    {
        var key = AgeTypeKey(ageType);
        return key.Length == 0 ? ageType.ToString() : _localization[key];
    }

    private string GetAgeTypeLabel(CustomMadeAgeType ageType, string languageCode)
    {
        var key = AgeTypeKey(ageType);
        return key.Length == 0 ? ageType.ToString() : _localization.GetText(key, languageCode);
    }

    private static string AgeTypeKey(CustomMadeAgeType ageType)
        => ageType switch
        {
            CustomMadeAgeType.AdultMale => "AgeType.AdultMale",
            CustomMadeAgeType.AdultFemale => "AgeType.AdultFemale",
            CustomMadeAgeType.TeenBoy => "AgeType.TeenBoy",
            CustomMadeAgeType.TeenGirl => "AgeType.TeenGirl",
            CustomMadeAgeType.ChildBoy => "AgeType.ChildBoy",
            CustomMadeAgeType.ChildGirl => "AgeType.ChildGirl",
            _ => string.Empty
        };

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CustomMadeServiceRecord Clone(CustomMadeServiceRecord source)
        => new()
        {
            Id = source.Id,
            ServiceMode = source.ServiceMode,
            CustomerName = source.CustomerName,
            PhoneNumber = source.PhoneNumber,
            Email = source.Email,
            AgeType = source.AgeType,
            JacketLength = source.JacketLength,
            JacketChest = source.JacketChest,
            JacketSitAround = source.JacketSitAround,
            JacketSleeves = source.JacketSleeves,
            ShirtLength = source.ShirtLength,
            ShirtChest = source.ShirtChest,
            ShirtSitAround = source.ShirtSitAround,
            ShirtSleeves = source.ShirtSleeves,
            Price = source.Price,
            TaxRate = source.TaxRate
        };
}

