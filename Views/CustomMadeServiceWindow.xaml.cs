using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.ObjectModel;
using CameywareOrder.Localization;
using CameywareOrder.Models;
using CameywareOrder.Services;
using Microsoft.Win32;
using System.Diagnostics.CodeAnalysis;

namespace CameywareOrder.Views;

public partial class CustomMadeServiceWindow : Window
{
    public CustomMadeServiceRecord? Result { get; private set; }

    private const string ValidationTitleKey = "OrderEdit.ValidationTitle";

    // Backstop against pathological backtracking on pasted input (S6444). MUST be declared before
    // the patterns that use it: static field initializers run in textual order, so a timeout
    // declared below them would still be TimeSpan.Zero when they construct — which Regex rejects,
    // and the failure would surface as a TypeInitializationException on first use, not a build error.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex MoneyInputPattern =
        new(@"^\d*(\.\d{0,2})?$", RegexOptions.None, RegexTimeout);
    private static readonly Regex MeasurementInputPattern =
        new(@"^(\d+(\.\d*)?[+-]?)?$", RegexOptions.None, RegexTimeout);
    // The number pattern and the cm-per-inch constant moved to MeasurementUnits, which now owns
    // conversion for the editor, the printed sheet and the PDF alike.

    private readonly LocalizationService _localization;
    private readonly string? _defaultOrderNumber;
    private readonly CustomMadeServiceRecord _workingRecord;
    private readonly bool _isReadOnly;
    private bool _isInitializing;
    private bool _isRefreshingLanguage;
    private bool _isApplyingMeasurementView;
    private bool _isInch;

    // Garment-driven measurements replace the old static Jacket/Shirt fields. The
    // cache is the session's source of truth (both units per value); the editors
    // are the live text boxes generated for the currently selected garments.
    private readonly MeasurementTermsService _terms = MeasurementTermsService.Instance;
    private readonly List<string> _selectedGarmentIds = new();
    private readonly Dictionary<string, Dictionary<string, MeasurementCell>> _valueCache = new();
    private readonly List<TermInputEditor> _termEditors = new();

    // Documents are grouped by category into their own collections so each category renders its
    // own list. Uploaded files are copied into the store immediately, but the changes are only
    // committed to disk on Save, and rolled back on cancel or close.
    private readonly ObservableCollection<CustomMadeDocument> _handwritingDocs = new();
    private readonly ObservableCollection<CustomMadeDocument> _fabricDocs = new();
    private readonly ObservableCollection<CustomMadeDocument> _photoDocs = new();
    private readonly ObservableCollection<CustomMadeDocument> _otherDocs = new();
    private readonly List<string> _pendingAddedFiles = new();
    private readonly List<string> _pendingRemovedFiles = new();

    // Bound from XAML to gate the edit buttons (upload/replace/delete) in view mode.
    public bool CanEditDocuments => !_isReadOnly;

    public CustomMadeServiceWindow(LocalizationService localization, CustomMadeServiceRecord? existing = null, string? defaultOrderNumber = null, string? defaultCustomerName = null, string? defaultPhoneNumber = null, string? defaultEmail = null, bool isReadOnly = false)
    {
        _isInitializing = true;
        InitializeComponent();
        _localization = localization;
        _defaultOrderNumber = defaultOrderNumber;
        _isReadOnly = isReadOnly;
        _workingRecord = existing is null ? new CustomMadeServiceRecord() : Clone(existing);

        CustomerNameBox.Text = _workingRecord.CustomerName = existing?.CustomerName ?? defaultCustomerName ?? string.Empty;
        PhoneNumberBox.Text = _workingRecord.PhoneNumber = existing?.PhoneNumber ?? defaultPhoneNumber ?? string.Empty;
        EmailBox.Text = _workingRecord.Email = existing?.Email ?? defaultEmail;

        InitializeGarmentState();

        CustomPriceBox.Text = _workingRecord.Price?.ToString("0.##") ?? string.Empty;

        RegisterInputFilters();

        InitializeDocumentLists();

        InitializeMode(existing?.ServiceMode ?? CustomMadeServiceMode.CustomFromScratch);
        InitializeAgeType(existing?.AgeType ?? CustomMadeAgeType.AdultMale);

        if (_localization.CurrentLanguageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            DownloadEnglishRadio.IsChecked = true;

        _isInitializing = false;

        RefreshMeasurementContextText();
        RefreshGenderButtons(_workingRecord.AgeType);
        RefreshCustomPriceTotals();

        if (_isReadOnly)
            ApplyReadOnlyMode();

        RefreshWindowTitle();

        _localization.LanguageChanged += OnLanguageChanged;
    }

    private void RefreshWindowTitle()
    {
        var titleKey = _isReadOnly ? "OrderEdit.ViewCustomMade" : "OrderEdit.EditCustomMade";
        var title = _localization[titleKey];
        Title = title;
        TitleText.Text = title;
    }

    private void ApplyReadOnlyMode()
    {
        SaveButton.Visibility = Visibility.Collapsed;
        ReadOnlyNotice.Visibility = Visibility.Visible;

        CustomerNameBox.IsReadOnly = true;
        PhoneNumberBox.IsReadOnly = true;
        EmailBox.IsReadOnly = true;
        CustomPriceBox.IsReadOnly = true;
        GarmentSelectorPanel.IsEnabled = false;
        foreach (var editor in _termEditors)
            editor.Box.IsReadOnly = true;

        CustomFromScratchRadio.IsEnabled = false;
        MeasurementsOnlyRadio.IsEnabled = false;
        AdultRadio.IsEnabled = false;
        TeenRadio.IsEnabled = false;
        ChildRadio.IsEnabled = false;
        GenderButtonsPanel.IsEnabled = false;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_isRefreshingLanguage)
            return;

        _isRefreshingLanguage = true;
        try
        {
            RefreshWindowTitle();
            RefreshMeasurementContextText();
            RefreshGenderButtons(_workingRecord.AgeType);
            RefreshUploadButtonTexts();
            PersistAllEditors(updateOppositeUnit: true);
            BuildGarmentSelector();
            RebuildGarmentMeasurements();
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

        PersistAllEditors(updateOppositeUnit: false);
        _isInch = toInch;
        ApplyEditorsForUnit();
    }

    // Delegated to MeasurementUnits so the figure the editor shows, the figure the printed sheet
    // carries and the figure the PDF exports are produced by one piece of code. They used to be
    // separate, which is how the print path came to treat a missing inch figure as no measurement
    // at all instead of converting the centimetres it did have.
    private static string ConvertMeasurement(string? text, bool toInch)
        => MeasurementUnits.Convert(text, toInch);

    private void OnMeasurementValueChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _isApplyingMeasurementView)
            return;

        if (sender is TextBox { Tag: TermInputEditor editor })
            PersistEditor(editor, updateOppositeUnit: true);
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

            if (!saveDialog.ShowDialog().GetValueOrDefault())
                return;

            SaveMeasurementsPdf(saveDialog.FileName, languageCode);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            MessageBox.Show(ex.Message, _localization[ValidationTitleKey], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_isReadOnly)
            return;

        ErrorText.Text = string.Empty;

        PersistAllEditors(updateOppositeUnit: true);
        BuildGarmentsIntoRecord();

        var customerName = CustomerNameBox.Text.Trim();
        var phoneNumber = PhoneNumberBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(customerName))
        {
            ErrorText.Text = _localization["OrderEdit.Validate.CustomerName"];
            MessageBox.Show(_localization["OrderEdit.Validate.CustomerName"], _localization[ValidationTitleKey], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            ErrorText.Text = _localization["OrderEdit.Validate.PhoneNumber"];
            MessageBox.Show(_localization["OrderEdit.Validate.PhoneNumber"], _localization[ValidationTitleKey], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _workingRecord.CustomerName = customerName;
        _workingRecord.PhoneNumber = phoneNumber;
        _workingRecord.Email = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim();
        _workingRecord.ServiceMode = GetSelectedMode();
        _workingRecord.AgeType = GetSelectedAgeType();
        _workingRecord.Price = ParseNullableDecimal(CustomPriceBox.Text);
        _workingRecord.TaxRate = null;

        CommitDocumentChanges();

        Result = Clone(_workingRecord);
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    // Discard any files uploaded this session when the window closes without saving.
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (DialogResult is true)
            return;

        foreach (var storedName in _pendingAddedFiles)
            DocumentStorageService.DeleteByStoredName(storedName);

        _pendingAddedFiles.Clear();
        _pendingRemovedFiles.Clear();
    }

    private void InitializeDocumentLists()
    {
        HandwritingDocsList.ItemsSource = _handwritingDocs;
        FabricDocsList.ItemsSource = _fabricDocs;
        PhotoDocsList.ItemsSource = _photoDocs;
        OtherDocsList.ItemsSource = _otherDocs;

        foreach (var document in _workingRecord.Documents)
            GetCollection(document.Category).Add(document);

        _handwritingDocs.CollectionChanged += (_, _) => RefreshUploadButtonText(_handwritingDocs, HandwritingUploadText);
        _fabricDocs.CollectionChanged += (_, _) => RefreshUploadButtonText(_fabricDocs, FabricUploadText);
        _photoDocs.CollectionChanged += (_, _) => RefreshUploadButtonText(_photoDocs, PhotoUploadText);
        _otherDocs.CollectionChanged += (_, _) => RefreshUploadButtonText(_otherDocs, OtherUploadText);

        RefreshUploadButtonTexts();
    }

    // The upload label reads "Start upload" while a category is empty and switches
    // to "Add more images" once at least one image exists.
    private void RefreshUploadButtonTexts()
    {
        RefreshUploadButtonText(_handwritingDocs, HandwritingUploadText);
        RefreshUploadButtonText(_fabricDocs, FabricUploadText);
        RefreshUploadButtonText(_photoDocs, PhotoUploadText);
        RefreshUploadButtonText(_otherDocs, OtherUploadText);
    }

    private void RefreshUploadButtonText(ObservableCollection<CustomMadeDocument> collection, TextBlock target)
    {
        var key = collection.Count == 0
            ? "CustomMade.Documents.StartUpload"
            : "CustomMade.Documents.AddMore";
        target.Text = _localization[key];
    }

    private ObservableCollection<CustomMadeDocument> GetCollection(CustomMadeDocumentCategory category)
        => category switch
        {
            CustomMadeDocumentCategory.HandwritingReceipt => _handwritingDocs,
            CustomMadeDocumentCategory.Fabric => _fabricDocs,
            CustomMadeDocumentCategory.Photo => _photoDocs,
            _ => _otherDocs
        };

    private void OnUploadDocumentClick(object sender, RoutedEventArgs e)
    {
        if (_isReadOnly || sender is not FrameworkElement { Tag: CustomMadeDocumentCategory category })
            return;

        var path = PickImageFile();
        if (path is null)
            return;

        var document = DocumentStorageService.Import(path, category);
        _pendingAddedFiles.Add(document.StoredFileName);
        GetCollection(category).Add(document);
    }

    private void OnViewDocumentClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CustomMadeDocument document })
            return;

        if (!DocumentStorageService.Exists(document))
        {
            MessageBox.Show(_localization["CustomMade.Documents.PreviewMissing"],
                _localization[ValidationTitleKey], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var preview = new DocumentPreviewWindow(
            DocumentStorageService.GetFullPath(document.StoredFileName), document.FileName)
        {
            Owner = this
        };
        preview.ShowDialog();
    }

    private void OnDownloadDocumentClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CustomMadeDocument document })
            return;

        if (!DocumentStorageService.Exists(document))
        {
            MessageBox.Show(_localization["CustomMade.Documents.PreviewMissing"],
                _localization[ValidationTitleKey], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = document.FileName,
            Filter = DocumentStorageService.ImageFileFilter
        };

        if (dialog.ShowDialog(this) is true)
            DocumentStorageService.Export(document, dialog.FileName);
    }

    private void OnReplaceDocumentClick(object sender, RoutedEventArgs e)
    {
        if (_isReadOnly || sender is not FrameworkElement { DataContext: CustomMadeDocument document })
            return;

        var path = PickImageFile();
        if (path is null)
            return;

        var collection = GetCollection(document.Category);
        var index = collection.IndexOf(document);
        if (index < 0)
            return;

        DiscardStoredFile(document.StoredFileName);

        var replacement = DocumentStorageService.Import(path, document.Category);
        _pendingAddedFiles.Add(replacement.StoredFileName);
        collection[index] = replacement;
    }

    private void OnDeleteDocumentClick(object sender, RoutedEventArgs e)
    {
        if (_isReadOnly || sender is not FrameworkElement { DataContext: CustomMadeDocument document })
            return;

        var confirm = MessageBox.Show(
            _localization["CustomMade.Documents.DeleteConfirm"],
            _localization["CustomMade.Documents.DeleteTitle"],
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        DiscardStoredFile(document.StoredFileName);
        GetCollection(document.Category).Remove(document);
    }

    private string? PickImageFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = DocumentStorageService.ImageFileFilter,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) is not true)
            return null;

        if (!DocumentStorageService.IsSupportedImage(dialog.FileName))
        {
            MessageBox.Show(_localization["CustomMade.Documents.InvalidImage"],
                _localization[ValidationTitleKey], MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        return dialog.FileName;
    }

    // Removing a document that was uploaded this session deletes its file right away
    // (it was never committed); an already-saved file is only deleted on Save.
    private void DiscardStoredFile(string storedFileName)
    {
        if (_pendingAddedFiles.Remove(storedFileName))
            DocumentStorageService.DeleteByStoredName(storedFileName);
        else
            _pendingRemovedFiles.Add(storedFileName);
    }

    private void CommitDocumentChanges()
    {
        foreach (var storedName in _pendingRemovedFiles)
            DocumentStorageService.DeleteByStoredName(storedName);

        _pendingRemovedFiles.Clear();
        _pendingAddedFiles.Clear();

        _workingRecord.Documents = _handwritingDocs
            .Concat(_fabricDocs)
            .Concat(_photoDocs)
            .Concat(_otherDocs)
            .ToList();
    }

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
        => (CustomFromScratchRadio?.IsChecked).GetValueOrDefault()
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
        if (AdultRadio.IsChecked.GetValueOrDefault())
            options = new[] { CustomMadeAgeType.AdultMale, CustomMadeAgeType.AdultFemale };
        else if (TeenRadio.IsChecked.GetValueOrDefault())
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
        if (AdultRadio.IsChecked.GetValueOrDefault())
            return _localization["OrderEdit.Panel.Adult"];
        if (TeenRadio.IsChecked.GetValueOrDefault())
            return _localization["OrderEdit.Panel.Teen"];
        return _localization["OrderEdit.Panel.Child"];
    }

    private string GetAgeGroupLabel(string languageCode)
    {
        if (AdultRadio.IsChecked.GetValueOrDefault())
            return _localization.GetText("OrderEdit.Panel.Adult", languageCode);
        if (TeenRadio.IsChecked.GetValueOrDefault())
            return _localization.GetText("OrderEdit.Panel.Teen", languageCode);
        return _localization.GetText("OrderEdit.Panel.Child", languageCode);
    }

    private void SaveMeasurementsPdf(string filePath, string languageCode)
    {
        string L(string key) => _localization.GetText(key, languageCode);

        PersistAllEditors(updateOppositeUnit: true);

        var infoRows = new List<(string Label, string Value)>();
        AddInfoRow(infoRows, L("Order.Fields.OrderNumber"), _defaultOrderNumber);
        AddInfoRow(infoRows, L("Order.Fields.CustomerName"), CustomerNameBox.Text);
        AddInfoRow(infoRows, L("Order.Fields.PhoneNumber"), PhoneNumberBox.Text);
        AddInfoRow(infoRows, L("Order.Fields.Email"), EmailBox.Text);
        AddInfoRow(infoRows, L("OrderEdit.Panel.MeasurementMode"), GetModeLabel(GetSelectedMode(), languageCode));
        AddInfoRow(infoRows, L("OrderEdit.Panel.AgeType"), $"{GetAgeGroupLabel(languageCode)} / {GetAgeTypeLabel(_workingRecord.AgeType, languageCode)}");
        AddInfoRow(infoRows, L("Measure.Unit.Label"), L(_isInch ? "Measure.Unit.Inch" : "Measure.Unit.Cm"));

        var brandingSettings = ReceiptBrandingStore.Load();
        var branding = brandingSettings.ForLanguage(languageCode);

        // Every string is resolved HERE, where L() knows the language chosen in the print dialog —
        // which is not necessarily the UI language. The document composer localizes nothing.
        var taxNumber = ReceiptBrandingStore.ResolveTaxRegistrationNumber(brandingSettings);

        MeasurementSheetDocument.Save(
            new MeasurementSheetContent
            {
                Title = L("Customer.Measurements.PrintTitle"),
                InfoRows = infoRows.Select(row => new MeasurementSheetRow(row.Label, row.Value)).ToList(),
                Sections = BuildPdfGarmentSections(languageCode),
                TaxLine = string.IsNullOrWhiteSpace(taxNumber)
                    ? null
                    : string.Format(
                        System.Globalization.CultureInfo.CurrentCulture,
                        L("Receipt.TaxNumberLine"),
                        taxNumber.Trim()),
                HeaderXaml = branding.HeaderXaml,
                FooterXaml = branding.FooterXaml,
                LogoBytes = ReceiptBrandingStore.GetLogoBytes(brandingSettings),
                LogoPlacement = brandingSettings.LogoPlacement,
            },
            filePath);
    }

    private static List<MeasurementSheetRow> FilterMeasurementRows(IEnumerable<(string Label, string? Value)> rows)
        => rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Value))
            .Select(row => new MeasurementSheetRow(row.Label, row.Value!.Trim()))
            .ToList();

    private List<MeasurementSheetSection> BuildPdfGarmentSections(string languageCode)
    {
        var sections = new List<MeasurementSheetSection>();

        var orderedIds = _terms.Garments
            .Select(g => g.Id)
            .Where(id => _selectedGarmentIds.Contains(id))
            .ToList();

        foreach (var garmentId in orderedIds)
        {
            if (!_valueCache.TryGetValue(garmentId, out var cells))
                continue;

            var rows = new List<(string Label, string? Value)>();
            foreach (var term in _terms.GetGarmentTerms(garmentId))
            {
                if (!cells.TryGetValue(term.Id, out var cell))
                    continue;
                // Same resolution as the printed sheet: convert from the unit that WAS filled in
                // rather than exporting a blank because this one was not.
                var display = MeasurementUnits.Resolve(cell.Cm, cell.In, _isInch);
                rows.Add((MeasurementTermsService.ResolveTermName(term, languageCode), MeasurementForPdf(display)));
            }

            var filtered = FilterMeasurementRows(rows);
            if (filtered.Count > 0)
            {
                sections.Add(new MeasurementSheetSection(
                    MeasurementTermsService.ResolveGarmentName(_terms.FindGarment(garmentId)!, languageCode),
                    filtered));
            }
        }

        return sections;
    }

    private static void AddInfoRow(List<(string Label, string Value)> rows, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        rows.Add((label, value.Trim()));
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
        sanitized = Regex.Replace(sanitized, @"[\s_]+", "_", RegexOptions.None, RegexTimeout).Trim('_');
        return $"{sanitized}_{ShortLanguageName(languageCode)}";
    }

    /// <summary>
    /// Short language name for the exported file's suffix — "zh" from "zh-CN", "en" from "en-US".
    /// </summary>
    /// <remarks>
    /// The primary subtag of a BCP-47 tag IS the short language name, so this derives it rather than
    /// listing cases. It used to read `StartsWith("zh") ? "zh" : "en"`, which named every future
    /// language "en": a French export would have been written as Measurements_en.pdf.
    ///
    /// Deliberately NOT a Format.* entry in the language files. Unlike a list separator this is not
    /// a choice a language gets to make — it is the same mechanical rule for all of them, and data
    /// that can be derived should not be maintained by hand.
    /// </remarks>
    private static string ShortLanguageName(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return "en";

        var primary = languageCode.Split('-')[0].Trim();
        return primary.Length == 0 ? "en" : primary.ToLowerInvariant();
    }

    private static string? MeasurementForPdf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        return text.Trim();
    }

    // --- Garment-driven measurements -------------------------------------------

    private void InitializeGarmentState()
    {
        SeedValueCacheFromRecord();
        BuildGarmentSelector();
        RebuildGarmentMeasurements();
    }

    // Populate the cache + selection from the record's garment list, falling back to
    // the legacy static Jacket/Shirt fields for records saved before this system.
    private void SeedValueCacheFromRecord()
    {
        _valueCache.Clear();
        _selectedGarmentIds.Clear();

        if (_workingRecord.Garments.Count > 0)
        {
            SeedFromGarmentList();
            return;
        }

        SeedLegacyGarment("jacket", new (string, string?, string?, string?)[]
        {
            ("length", _workingRecord.JacketLengthCm, _workingRecord.JacketLengthIn, _workingRecord.JacketLength),
            ("chest", _workingRecord.JacketChestCm, _workingRecord.JacketChestIn, _workingRecord.JacketChest),
            ("sitAround", _workingRecord.JacketSitAroundCm, _workingRecord.JacketSitAroundIn, _workingRecord.JacketSitAround),
            ("sleeve", _workingRecord.JacketSleevesCm, _workingRecord.JacketSleevesIn, _workingRecord.JacketSleeves)
        });
        SeedLegacyGarment("shirt", new (string, string?, string?, string?)[]
        {
            ("length", _workingRecord.ShirtLengthCm, _workingRecord.ShirtLengthIn, _workingRecord.ShirtLength),
            ("chest", _workingRecord.ShirtChestCm, _workingRecord.ShirtChestIn, _workingRecord.ShirtChest),
            ("sitAround", _workingRecord.ShirtSitAroundCm, _workingRecord.ShirtSitAroundIn, _workingRecord.ShirtSitAround),
            ("sleeve", _workingRecord.ShirtSleevesCm, _workingRecord.ShirtSleevesIn, _workingRecord.ShirtSleeves)
        });
    }

    private void SeedFromGarmentList()
    {
        foreach (var garment in _workingRecord.Garments)
        {
            if (_terms.FindGarment(garment.GarmentId) is null)
                continue;

            var cells = GetOrCreateGarmentCache(garment.GarmentId);
            foreach (var value in garment.Values)
            {
                var (cm, inch) = BuildInitialMeasurementPair(value.Cm, value.In, null);
                if (cm is null && inch is null)
                    continue;
                cells[value.TermId] = new MeasurementCell { Cm = cm, In = inch };
            }

            if (!_selectedGarmentIds.Contains(garment.GarmentId))
                _selectedGarmentIds.Add(garment.GarmentId);
        }
    }

    private void SeedLegacyGarment(string garmentId, IEnumerable<(string TermId, string? Cm, string? In, string? Legacy)> values)
    {
        if (_terms.FindGarment(garmentId) is null)
            return;

        var cells = GetOrCreateGarmentCache(garmentId);
        var hasAny = false;
        foreach (var (termId, cm, inch, legacy) in values)
        {
            var (normCm, normIn) = BuildInitialMeasurementPair(cm, inch, legacy);
            if (normCm is null && normIn is null)
                continue;
            cells[termId] = new MeasurementCell { Cm = normCm, In = normIn };
            hasAny = true;
        }

        if (hasAny && !_selectedGarmentIds.Contains(garmentId))
            _selectedGarmentIds.Add(garmentId);
    }

    private Dictionary<string, MeasurementCell> GetOrCreateGarmentCache(string garmentId)
    {
        if (!_valueCache.TryGetValue(garmentId, out var cells))
        {
            cells = new Dictionary<string, MeasurementCell>();
            _valueCache[garmentId] = cells;
        }
        return cells;
    }

    private void BuildGarmentSelector()
    {
        GarmentSelectorPanel.Children.Clear();
        foreach (var garment in _terms.Garments)
        {
            var toggle = new ToggleButton
            {
                Content = MeasurementTermsService.ResolveGarmentName(garment),
                Tag = garment.Id,
                IsChecked = _selectedGarmentIds.Contains(garment.Id),
                Style = (Style)FindResource("SelectionToggleButtonStyle"),
                Margin = new Thickness(0, 0, 10, 10)
            };
            toggle.Click += OnGarmentToggleClick;
            GarmentSelectorPanel.Children.Add(toggle);
        }
    }

    private void OnGarmentToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string garmentId } toggle)
            return;

        // Persist current inputs first so toggling never loses typed values.
        PersistAllEditors(updateOppositeUnit: true);

        if (toggle.IsChecked is true)
        {
            if (!_selectedGarmentIds.Contains(garmentId))
                _selectedGarmentIds.Add(garmentId);
        }
        else
        {
            _selectedGarmentIds.Remove(garmentId);
        }

        RebuildGarmentMeasurements();
    }

    // Regenerate the per-garment measurement input cards for the current selection.
    private void RebuildGarmentMeasurements()
    {
        _termEditors.Clear();
        GarmentMeasurementsPanel.Children.Clear();

        var orderedIds = _terms.Garments
            .Select(g => g.Id)
            .Where(id => _selectedGarmentIds.Contains(id))
            .ToList();

        foreach (var garmentId in orderedIds)
            GarmentMeasurementsPanel.Children.Add(BuildGarmentCard(garmentId));

        NoGarmentText.Visibility = orderedIds.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_isReadOnly)
        {
            foreach (var editor in _termEditors)
                editor.Box.IsReadOnly = true;
        }
    }

    private Border BuildGarmentCard(string garmentId)
    {
        var cells = GetOrCreateGarmentCache(garmentId);
        var terms = _terms.GetGarmentTerms(garmentId);

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = MeasurementTermsService.ResolveGarmentName(_terms.FindGarment(garmentId)!),
            FontWeight = FontWeights.SemiBold,
            Foreground = Hex("#2980B9"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        if (terms.Count == 0)
        {
            content.Children.Add(new TextBlock
            {
                Text = _localization["MeasureTerms.EmptyGarment"],
                Foreground = Hex("#9AA6B2"),
                FontStyle = FontStyles.Italic
            });
        }
        else
        {
            content.Children.Add(BuildTermsGrid(garmentId, terms, cells));
        }

        return new Border
        {
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = Hex("#E1E5EA"),
            Background = Hex("#FAFBFC"),
            Child = content
        };
    }

    private Grid BuildTermsGrid(string garmentId, IReadOnlyList<MeasurementTerm> terms, Dictionary<string, MeasurementCell> cells)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rowCount = (terms.Count + 1) / 2;
        for (var i = 0; i < rowCount; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var index = 0; index < terms.Count; index++)
        {
            var term = terms[index];
            var row = index / 2;
            var columnPair = (index % 2) * 2;

            if (!cells.TryGetValue(term.Id, out var cell))
            {
                cell = new MeasurementCell();
                cells[term.Id] = cell;
            }

            var label = new TextBlock
            {
                Text = MeasurementTermsService.ResolveTermName(term),
                Margin = new Thickness(0, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, columnPair);
            grid.Children.Add(label);

            var box = CreateMeasurementBox(garmentId, term.Id, cell, columnPair == 0);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, columnPair + 1);
            grid.Children.Add(box);
        }

        return grid;
    }

    private TextBox CreateMeasurementBox(string garmentId, string termId, MeasurementCell cell, bool isLeftColumn)
    {
        var box = new TextBox
        {
            Margin = new Thickness(0, 4, isLeftColumn ? 14 : 0, 4),
            Padding = new Thickness(6, 4, 6, 4),
            Text = _isInch ? (cell.In ?? string.Empty) : (cell.Cm ?? string.Empty)
        };
        var editor = new TermInputEditor { GarmentId = garmentId, TermId = termId, Box = box, Cell = cell };
        box.Tag = editor;
        box.PreviewTextInput += OnMeasurementPreviewTextInput;
        box.TextChanged += OnMeasurementValueChanged;
        DataObject.AddPastingHandler(box, (s, e) => HandlePaste(s, e, MeasurementInputPattern));
        _termEditors.Add(editor);
        return box;
    }

    private void ApplyEditorsForUnit()
    {
        _isApplyingMeasurementView = true;
        try
        {
            foreach (var editor in _termEditors)
                editor.Box.Text = _isInch ? (editor.Cell.In ?? string.Empty) : (editor.Cell.Cm ?? string.Empty);
        }
        finally
        {
            _isApplyingMeasurementView = false;
        }
    }

    private void PersistAllEditors(bool updateOppositeUnit)
    {
        foreach (var editor in _termEditors)
            PersistEditor(editor, updateOppositeUnit);
    }

    private void PersistEditor(TermInputEditor editor, bool updateOppositeUnit)
    {
        var (cm, inch) = BuildMeasurementPairFromDisplay(
            editor.Box.Text, _isInch, editor.Cell.Cm, editor.Cell.In, updateOppositeUnit);
        editor.Cell.Cm = cm;
        editor.Cell.In = inch;
    }

    // Build the record's garment list from the cache for the current selection.
    private void BuildGarmentsIntoRecord()
    {
        var garments = new List<GarmentMeasurement>();

        var orderedIds = _terms.Garments
            .Select(g => g.Id)
            .Where(id => _selectedGarmentIds.Contains(id))
            .ToList();

        foreach (var garmentId in orderedIds)
        {
            if (!_valueCache.TryGetValue(garmentId, out var cells))
                continue;

            var values = new List<MeasurementValue>();
            foreach (var termId in _terms.GetGarmentTerms(garmentId).Select(t => t.Id))
            {
                if (!cells.TryGetValue(termId, out var cell))
                    continue;
                if (string.IsNullOrWhiteSpace(cell.Cm) && string.IsNullOrWhiteSpace(cell.In))
                    continue;
                values.Add(new MeasurementValue { TermId = termId, Cm = cell.Cm, In = cell.In });
            }

            if (values.Count > 0)
                garments.Add(new GarmentMeasurement { GarmentId = garmentId, Values = values });
        }

        _workingRecord.Garments = garments;
    }

    private static SolidColorBrush Hex(string hex)
        => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;


    private static (string? Cm, string? In) BuildInitialMeasurementPair(string? cm, string? inch, string? legacyValue)
    {
        var normalizedCm = NullIfWhiteSpace(cm) ?? NullIfWhiteSpace(legacyValue);
        var normalizedIn = NullIfWhiteSpace(inch);

        if (normalizedCm is null && normalizedIn is null)
            return (null, null);

        if (normalizedCm is null)
            normalizedCm = ConvertMeasurement(normalizedIn, toInch: false);

        if (normalizedIn is null)
            normalizedIn = ConvertMeasurement(normalizedCm, toInch: true);

        return (normalizedCm, normalizedIn);
    }

    private static (string? Cm, string? In) BuildMeasurementPairFromDisplay(
        string? value,
        bool isInch,
        string? currentCm,
        string? currentIn,
        bool updateOppositeUnit)
    {
        var normalized = NullIfWhiteSpace(value);
        if (normalized is null)
            return (null, null);

        if (isInch)
        {
            var updatedCm = updateOppositeUnit || string.IsNullOrWhiteSpace(currentCm)
                ? ConvertMeasurement(normalized, toInch: false)
                : currentCm;
            return (updatedCm, normalized);
        }

        var updatedIn = updateOppositeUnit || string.IsNullOrWhiteSpace(currentIn)
            ? ConvertMeasurement(normalized, toInch: true)
            : currentIn;
        return (normalized, updatedIn);
    }

    private void RefreshCustomPriceTotals()
    {
        var subtotal = ParseDecimalOrZero(CustomPriceBox.Text);
        CustomSubtotalText.Text = subtotal.ToString("0.00");
    }

    private void RegisterInputFilters()
    {
        DataObject.AddPastingHandler(CustomPriceBox, (s, e) => HandlePaste(s, e, MoneyInputPattern));
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Named from XAML (PreviewTextInput=\"OnMoneyPreviewTextInput\"). The generated " +
                        "InitializeComponent wires it as this.OnMoneyPreviewTextInput, which does not compile " +
                        "against a static method. Its measurement sibling below IS static, because that one is " +
                        "only ever attached from code.")]
    private void OnMoneyPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is TextBox box)
            e.Handled = !MoneyInputPattern.IsMatch(GetProposedText(box, e.Text));
    }

    /// <summary>
    /// Static, unlike its money sibling above: this one is only ever attached from code
    /// (<c>box.PreviewTextInput += …</c>), where a static handler is fine. A handler named in XAML
    /// cannot be static — the generated InitializeComponent wires it as <c>this.Handler</c>.
    /// </summary>
    private static void OnMeasurementPreviewTextInput(object sender, TextCompositionEventArgs e)
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
            JacketLengthCm = source.JacketLengthCm,
            JacketLengthIn = source.JacketLengthIn,
            JacketChestCm = source.JacketChestCm,
            JacketChestIn = source.JacketChestIn,
            JacketSitAroundCm = source.JacketSitAroundCm,
            JacketSitAroundIn = source.JacketSitAroundIn,
            JacketSleevesCm = source.JacketSleevesCm,
            JacketSleevesIn = source.JacketSleevesIn,
            ShirtLengthCm = source.ShirtLengthCm,
            ShirtLengthIn = source.ShirtLengthIn,
            ShirtChestCm = source.ShirtChestCm,
            ShirtChestIn = source.ShirtChestIn,
            ShirtSitAroundCm = source.ShirtSitAroundCm,
            ShirtSitAroundIn = source.ShirtSitAroundIn,
            ShirtSleevesCm = source.ShirtSleevesCm,
            ShirtSleevesIn = source.ShirtSleevesIn,
            Price = source.Price,
            TaxRate = source.TaxRate,
            Garments = source.Garments
                .Select(g => new GarmentMeasurement
                {
                    GarmentId = g.GarmentId,
                    Values = g.Values
                        .Select(v => new MeasurementValue { TermId = v.TermId, Cm = v.Cm, In = v.In })
                        .ToList()
                })
                .ToList(),
            Documents = source.Documents
                .Select(d => new CustomMadeDocument
                {
                    Id = d.Id,
                    Category = d.Category,
                    FileName = d.FileName,
                    StoredFileName = d.StoredFileName,
                    UploadedAtUtc = d.UploadedAtUtc
                })
                .ToList()
        };

    private sealed class MeasurementCell
    {
        public string? Cm { get; set; }
        public string? In { get; set; }
    }

    private sealed class TermInputEditor
    {
        public string GarmentId { get; init; } = string.Empty;
        public string TermId { get; init; } = string.Empty;
        public TextBox Box { get; init; } = null!;
        public MeasurementCell Cell { get; init; } = null!;
    }
}

