using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CameywareOrder.Localization;
using CameywareOrder.Services;
using Microsoft.Win32;

namespace CameywareOrder.Views;

/// <summary>
/// Word-like editor to preset a shared logo plus per-language rich header/footer
/// content, injected into printed receipts and the measurements PDF export.
/// </summary>
public partial class ReceiptBrandingWindow : Window
{
    private readonly LocalizationService _localization;
    private readonly ReceiptBrandingSettings _settings;

    private readonly Dictionary<string, (RichTextBox Header, RichTextBox Footer)> _editors = new();
    private RichTextBox? _activeEditor;

    private string? _pendingLogoSourcePath;
    private bool _logoRemoved;

    public ReceiptBrandingWindow(LocalizationService localization)
    {
        InitializeComponent();
        _localization = localization;
        _settings = ReceiptBrandingStore.Load();

        foreach (var size in new[] { 10, 11, 12, 14, 16, 18, 20, 24, 28, 32 })
            FontSizeBox.Items.Add(size);
        FontSizeBox.SelectedItem = 12;

        BuildLanguageTabs();
        UpdateLogoPreview();

        PlacementLeftRadio.IsChecked = _settings.LogoPlacement == LogoPlacement.Left;
        PlacementCenterRadio.IsChecked = _settings.LogoPlacement == LogoPlacement.Center;
        PlacementRightRadio.IsChecked = _settings.LogoPlacement == LogoPlacement.Right;
    }

    private void BuildLanguageTabs()
    {
        foreach (var language in _localization.AvailableLanguages)
        {
            var branding = _settings.ForLanguage(language.Code);
            var headerEditor = CreateEditor(branding.HeaderXaml);
            var footerEditor = CreateEditor(branding.FooterXaml);
            _editors[language.Code] = (headerEditor, footerEditor);

            var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            panel.Children.Add(BuildEditorCard("Branding.Header", headerEditor));
            panel.Children.Add(BuildEditorCard("Branding.Footer", footerEditor));

            LanguageTabs.Items.Add(new TabItem
            {
                Header = language.Name,
                Content = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = panel
                }
            });
        }

        _activeEditor = _editors.Values.FirstOrDefault().Header;
    }

    private RichTextBox CreateEditor(string? xaml)
    {
        var editor = new RichTextBox
        {
            MinHeight = 150,
            FontSize = 13,
            Padding = new Thickness(8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0xE1, 0xE7)),
            BorderThickness = new Thickness(1),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AcceptsTab = true
        };

        var document = BrandingRenderer.TryParseDocument(xaml);
        if (document is not null)
            editor.Document = document;

        editor.GotKeyboardFocus += (_, _) => _activeEditor = editor;
        return editor;
    }

    private Border BuildEditorCard(string labelKey, RichTextBox editor)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = _localization[labelKey],
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x4D)),
            Margin = new Thickness(0, 0, 0, 6)
        });
        stack.Children.Add(editor);

        return new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE7, 0xEC)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12),
            Child = stack
        };
    }

    // ── Formatting ribbon ────────────────────────────────────────────────

    private void OnBoldClick(object sender, RoutedEventArgs e)
    {
        if (_activeEditor is null)
            return;

        var isBold = _activeEditor.Selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight weight
            && weight == FontWeights.Bold;
        _activeEditor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, isBold ? FontWeights.Normal : FontWeights.Bold);
        _activeEditor.Focus();
    }

    private void OnItalicClick(object sender, RoutedEventArgs e)
    {
        if (_activeEditor is null)
            return;

        var isItalic = _activeEditor.Selection.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle style
            && style == FontStyles.Italic;
        _activeEditor.Selection.ApplyPropertyValue(TextElement.FontStyleProperty, isItalic ? FontStyles.Normal : FontStyles.Italic);
        _activeEditor.Focus();
    }

    private void OnUnderlineClick(object sender, RoutedEventArgs e)
    {
        if (_activeEditor is null)
            return;

        var hasUnderline = _activeEditor.Selection.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection decorations
            && decorations.Any(d => d.Location == TextDecorationLocation.Underline);
        _activeEditor.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
            hasUnderline ? new TextDecorationCollection() : TextDecorations.Underline);
        _activeEditor.Focus();
    }

    private void OnFontSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _activeEditor is null)
            return;

        if (double.TryParse(FontSizeBox.SelectedItem?.ToString() ?? FontSizeBox.Text, out var size) && size > 0)
        {
            _activeEditor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            _activeEditor.Focus();
        }
    }

    private void OnAlignLeftClick(object sender, RoutedEventArgs e) => ApplyAlignment(TextAlignment.Left);
    private void OnAlignCenterClick(object sender, RoutedEventArgs e) => ApplyAlignment(TextAlignment.Center);
    private void OnAlignRightClick(object sender, RoutedEventArgs e) => ApplyAlignment(TextAlignment.Right);

    private void ApplyAlignment(TextAlignment alignment)
    {
        if (_activeEditor is null)
            return;

        _activeEditor.Selection.ApplyPropertyValue(Block.TextAlignmentProperty, alignment);
        _activeEditor.Focus();
    }

    private void OnColorSwatchClick(object sender, RoutedEventArgs e)
    {
        if (_activeEditor is null || sender is not Button { Tag: string hex })
            return;

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        _activeEditor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
        _activeEditor.Focus();
    }

    // ── Logo ─────────────────────────────────────────────────────────────

    private void OnChooseLogoClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        _pendingLogoSourcePath = dialog.FileName;
        _logoRemoved = false;
        UpdateLogoPreview();
    }

    private void OnRemoveLogoClick(object sender, RoutedEventArgs e)
    {
        _pendingLogoSourcePath = null;
        _logoRemoved = true;
        UpdateLogoPreview();
    }

    private void UpdateLogoPreview()
    {
        var path = _logoRemoved ? null : _pendingLogoSourcePath ?? ReceiptBrandingStore.GetLogoPath(_settings);

        if (string.IsNullOrWhiteSpace(path))
        {
            LogoPreview.Source = null;
            LogoPreview.Visibility = Visibility.Collapsed;
            NoLogoText.Visibility = Visibility.Visible;
            RemoveLogoButton.IsEnabled = false;
            return;
        }

        LogoPreview.Source = BrandingRenderer.LoadBitmap(path);
        LogoPreview.Visibility = Visibility.Visible;
        NoLogoText.Visibility = Visibility.Collapsed;
        RemoveLogoButton.IsEnabled = true;
    }

    // ── Save / Cancel ────────────────────────────────────────────────────

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        foreach (var (code, editors) in _editors)
        {
            var branding = _settings.ForLanguage(code);
            var headerXaml = BrandingRenderer.SerializeDocument(editors.Header.Document);
            var footerXaml = BrandingRenderer.SerializeDocument(editors.Footer.Document);
            branding.HeaderXaml = BrandingRenderer.IsEmpty(headerXaml) ? null : headerXaml;
            branding.FooterXaml = BrandingRenderer.IsEmpty(footerXaml) ? null : footerXaml;
        }

        if (_logoRemoved)
            ReceiptBrandingStore.RemoveLogo(_settings);
        else if (!string.IsNullOrWhiteSpace(_pendingLogoSourcePath))
            _settings.LogoFileName = ReceiptBrandingStore.ImportLogo(_pendingLogoSourcePath);

        if (PlacementLeftRadio.IsChecked.GetValueOrDefault())
            _settings.LogoPlacement = LogoPlacement.Left;
        else if (PlacementRightRadio.IsChecked.GetValueOrDefault())
            _settings.LogoPlacement = LogoPlacement.Right;
        else
            _settings.LogoPlacement = LogoPlacement.Center;

        try
        {
            ReceiptBrandingStore.Save(_settings);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Foreground = Brushes.IndianRed;
            StatusText.Text = ex.Message;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
