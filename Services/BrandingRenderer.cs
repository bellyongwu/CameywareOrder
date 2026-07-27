using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace CameywareOrder.Services;

/// <summary>
/// Bridges the rich header/footer content between its three representations:
/// the editor's <see cref="System.Windows.Controls.RichTextBox"/> (a FlowDocument),
/// the persisted XAML string, the printed receipt (native FlowDocument blocks), and
/// the QuestPDF measurements export (walked into styled text spans).
/// </summary>
public static class BrandingRenderer
{
    // Serialization only ever round-trips FlowDocument content produced by this app's
    // own editor, so XamlReader.Parse is operating on trusted, self-generated markup.
    public static string SerializeDocument(FlowDocument document) => XamlWriter.Save(document);

    public static FlowDocument? TryParseDocument(string? xaml)
    {
        if (string.IsNullOrWhiteSpace(xaml))
            return null;

        try
        {
            return XamlReader.Parse(xaml) as FlowDocument;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True when the stored content has no visible text.</summary>
    public static bool IsEmpty(string? xaml)
    {
        var document = TryParseDocument(xaml);
        if (document is null)
            return true;

        var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
        return string.IsNullOrWhiteSpace(text);
    }

    /// <summary>Loads an image file without keeping a lock on it (so it can be replaced/deleted).</summary>
    public static BitmapImage LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>A logo block for a printed FlowDocument aligned per <paramref name="placement"/>, or null when unavailable.</summary>
    public static Block? CreateLogoBlock(string? logoPath, double maxHeight, LogoPlacement placement = LogoPlacement.Center)
    {
        if (string.IsNullOrWhiteSpace(logoPath))
            return null;

        try
        {
            var (imageAlignment, blockAlignment) = placement switch
            {
                LogoPlacement.Left => (System.Windows.HorizontalAlignment.Left, TextAlignment.Left),
                LogoPlacement.Right => (System.Windows.HorizontalAlignment.Right, TextAlignment.Right),
                _ => (System.Windows.HorizontalAlignment.Center, TextAlignment.Center)
            };

            var image = new System.Windows.Controls.Image
            {
                Source = LoadBitmap(logoPath),
                MaxHeight = maxHeight,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = imageAlignment
            };

            return new BlockUIContainer(image)
            {
                TextAlignment = blockAlignment,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Moves the parsed blocks of <paramref name="xaml"/> into <paramref name="target"/>,
    /// either at the very top (preserving order) or appended at the bottom.
    /// </summary>
    public static void AppendToFlowDocument(FlowDocument target, string? xaml, bool atTop)
    {
        var source = TryParseDocument(xaml);
        if (source is null)
            return;

        var blocks = source.Blocks.ToList();
        foreach (var block in blocks)
            source.Blocks.Remove(block);

        if (atTop)
        {
            var anchor = target.Blocks.FirstBlock;
            foreach (var block in blocks)
            {
                if (anchor is null)
                    target.Blocks.Add(block);
                else
                    target.Blocks.InsertBefore(anchor, block);
            }
        }
        else
        {
            foreach (var block in blocks)
                target.Blocks.Add(block);
        }
    }

    /// <summary>Renders the rich content into a QuestPDF column as styled, aligned text lines.</summary>
    public static void RenderToPdf(ColumnDescriptor column, string? xaml)
    {
        var document = TryParseDocument(xaml);
        if (document is null)
            return;

        foreach (var paragraph in document.Blocks.OfType<Paragraph>())
            RenderParagraph(column, paragraph);
    }

    private static void RenderParagraph(ColumnDescriptor column, Paragraph paragraph)
    {
        var runs = new List<InlineRun>();
        CollectInlines(paragraph.Inlines, InlineFormat.Default, runs);

        var alignment = paragraph.TextAlignment;
        column.Item().Text(text =>
        {
            ApplyAlignment(text, alignment);

            if (runs.Count == 0)
            {
                text.Span(" ");
                return;
            }

            foreach (var run in runs)
                EmitRun(text, run);
        });
    }

    private static void ApplyAlignment(TextDescriptor text, TextAlignment alignment)
    {
        switch (alignment)
        {
            case TextAlignment.Center: text.AlignCenter(); break;
            case TextAlignment.Right: text.AlignRight(); break;
            default: text.AlignLeft(); break;
        }
    }

    /// <summary>Aligns a QuestPDF container for the logo image per the stored placement.</summary>
    public static IContainer AlignLogo(IContainer container, LogoPlacement placement) => placement switch
    {
        LogoPlacement.Left => container.AlignLeft(),
        LogoPlacement.Right => container.AlignRight(),
        _ => container.AlignCenter()
    };

    private static void EmitRun(TextDescriptor text, InlineRun run)
    {
        var span = text.Span(run.Text);
        if (run.Bold) span.Bold();
        if (run.Italic) span.Italic();
        if (run.Underline) span.Underline();
        if (run.FontSize is { } size) span.FontSize((float)size);
        if (run.Color is { } color) span.FontColor(color);
    }

    private static void CollectInlines(InlineCollection inlines, InlineFormat format, List<InlineRun> accumulator)
    {
        foreach (var inline in inlines)
        {
            var merged = Merge(format, inline);
            switch (inline)
            {
                case Run run:
                    accumulator.Add(new InlineRun(run.Text, merged));
                    break;
                case LineBreak:
                    accumulator.Add(new InlineRun("\n", merged));
                    break;
                case Span span:
                    CollectInlines(span.Inlines, merged, accumulator);
                    break;
            }
        }
    }

    private static InlineFormat Merge(InlineFormat format, Inline inline)
    {
        // Bold/Italic/Underline are Span subclasses that imply their style.
        if (inline is Bold)
            format = format with { Bold = true };
        if (inline is Italic)
            format = format with { Italic = true };
        if (inline is Underline)
            format = format with { Underline = true };

        // Locally set properties (e.g. applied to a selection) win over inheritance.
        if (inline.ReadLocalValue(TextElement.FontWeightProperty) is System.Windows.FontWeight weight)
            format = format with { Bold = weight.ToOpenTypeWeight() >= 600 };
        if (inline.ReadLocalValue(TextElement.FontStyleProperty) is System.Windows.FontStyle style)
            format = format with { Italic = style == FontStyles.Italic };
        if (inline.ReadLocalValue(TextElement.FontSizeProperty) is double fontSize)
            format = format with { FontSize = fontSize };
        if (inline.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush brush)
            format = format with { Color = ToHex(brush.Color) };
        if (inline.ReadLocalValue(Inline.TextDecorationsProperty) is TextDecorationCollection decorations)
            format = format with { Underline = decorations.Any(d => d.Location == TextDecorationLocation.Underline) };

        return format;
    }

    private static string ToHex(System.Windows.Media.Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private readonly record struct InlineFormat(bool Bold, bool Italic, bool Underline, double? FontSize, string? Color)
    {
        public static InlineFormat Default => new(false, false, false, null, null);
    }

    private readonly record struct InlineRun(string Text, InlineFormat Format)
    {
        public bool Bold => Format.Bold;
        public bool Italic => Format.Italic;
        public bool Underline => Format.Underline;
        public double? FontSize => Format.FontSize;
        public string? Color => Format.Color;
    }
}
