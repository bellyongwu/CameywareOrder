using System.Text.RegularExpressions;
using CameywareOrder.Localization;

namespace CameywareOrder.Services;

/// <summary>
/// Names the customer on a copied order: the source's own name plus a numbered "copy" suffix, so a
/// duplicate is recognisable as one in the list rather than sitting there as a second identical row.
/// </summary>
/// <remarks>
/// The suffix is a string-table value (<c>Order.Copy.Suffix</c>), not a literal, for the reason
/// <c>Store.Copy.Suffix</c> already is: it is punctuation as much as it is a word, and Chinese and
/// Japanese write the brackets full-width.
///
/// COMPOSING and STRIPPING live together on purpose. Copying a copy has to recover the real name
/// first — otherwise the suffixes stack ("Mary - Copy 1 - Copy 1") and the number stops meaning
/// anything — and a strip that did not know every shape the compose can produce would leave the
/// old suffix in place. The strip therefore reads EVERY shipped language's format, not just the one
/// the application is currently in: the name was written by whoever made the first copy, in whatever
/// language they had on screen, and it is one stored string from then on.
/// </remarks>
public static class OrderCopyName
{
    /// <summary>The one key both halves of this class resolve.</summary>
    public const string SuffixKey = "Order.Copy.Suffix";

    // Above the patterns that use it: static field initializers run in TEXTUAL order, and a timeout
    // declared below them is still TimeSpan.Zero when the Regex constructors run — which Regex
    // rejects, as a TypeInitializationException on first use rather than a build error.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    // A name carrying more than a handful of suffixes is not a real state; the bound is here so a
    // pattern that somehow matched an empty string cannot spin.
    private const int MaxSuffixDepth = 10;

    // Same shape as ShopAdministration.CopyNames: bounded, with a unique-by-construction fallback,
    // because refusing to copy an order over its name would be a worse answer than an odd number.
    private const int MaxIndexScan = 1_000;

    /// <summary>
    /// Every shipped language's shape of the suffix. This is the one place that reaches the
    /// localization SERVICE rather than an <see cref="ILocalizedText"/>: the question is "what can
    /// this suffix look like in any language", which is the service's own, and no single language
    /// can answer it.
    /// </summary>
    public static IReadOnlyList<string> ShippedSuffixFormats()
    {
        var service = LocalizationService.Instance;

        return service.AvailableLanguages
            .Select(language => service.GetText(SuffixKey, language.Code))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The real customer name behind <paramref name="name"/> — the value with any copy suffix, in
    /// any shipped language, removed.
    /// </summary>
    public static string BaseName(string? name) => BaseName(name, ShippedSuffixFormats());

    /// <inheritdoc cref="BaseName(string?)"/>
    public static string BaseName(string? name, IEnumerable<string> suffixFormats)
        => Split(name ?? string.Empty, PatternsFor(suffixFormats)).Base;

    /// <summary>
    /// The name for the next copy of <paramref name="sourceName"/>: its base name plus the first
    /// index no name in <paramref name="takenNames"/> already uses.
    /// </summary>
    /// <param name="takenNames">
    /// Every customer name already in use. Supply it case-insensitively, and GROW it as copies are
    /// written — two copies made in one click would otherwise both come out "- Copy 1", which is the
    /// defect batch Copy Order and batch Copy Shop have each shipped with once.
    /// </param>
    public static string Next(string? sourceName, ICollection<string> takenNames, ILocalizedText localization)
        => Next(sourceName, takenNames, localization, ShippedSuffixFormats());

    /// <inheritdoc cref="Next(string?, ICollection{string}, ILocalizedText)"/>
    public static string Next(
        string? sourceName,
        ICollection<string> takenNames,
        ILocalizedText localization,
        IEnumerable<string> suffixFormats)
    {
        ArgumentNullException.ThrowIfNull(takenNames);
        ArgumentNullException.ThrowIfNull(localization);

        var patterns = PatternsFor(suffixFormats);
        var baseName = Split(sourceName ?? string.Empty, patterns).Base;
        var start = HighestIndexFor(baseName, takenNames, patterns) + 1;

        // The scan starts past the highest number already in use rather than at 1, so the numbering
        // survives a language switch. One customer's first copy made in Chinese and their next made
        // in English are different STRINGS, so a plain collision test would find "1" free and hand
        // out a second one — two rows both calling themselves the first copy.
        for (var index = start; index < start + MaxIndexScan; index++)
        {
            var candidate = baseName + localization.Format(SuffixKey, index);
            if (!takenNames.Contains(candidate))
                return candidate;
        }

        return baseName + localization.Format(SuffixKey, DateTime.Now.Ticks);
    }

    /// <summary>The highest copy number any taken name already carries for this base name.</summary>
    private static int HighestIndexFor(
        string baseName, IEnumerable<string> takenNames, IReadOnlyList<Regex> patterns)
    {
        var highest = 0;

        foreach (var taken in takenNames)
        {
            var split = Split(taken ?? string.Empty, patterns);
            if (split.Index > highest && string.Equals(split.Base, baseName, StringComparison.OrdinalIgnoreCase))
                highest = split.Index;
        }

        return highest;
    }

    /// <summary>
    /// A stored name split into the customer's real name and the copy number it carries (0 when it
    /// carries none). Stripping repeats, so a name that somehow stacked two suffixes still yields
    /// the real name; the number reported is the OUTERMOST one, which is the most recent copy.
    /// </summary>
    private static (string Base, int Index) Split(string name, IReadOnlyList<Regex> patterns)
    {
        var current = name;
        var outermost = 0;

        for (var pass = 0;
             pass < MaxSuffixDepth && TryStripOnce(current, patterns, out var stripped, out var index);
             pass++)
        {
            if (pass == 0)
                outermost = index;

            current = stripped;
        }

        return (current, outermost);
    }

    private static bool TryStripOnce(
        string name, IReadOnlyList<Regex> patterns, out string stripped, out int index)
    {
        foreach (var pattern in patterns)
        {
            var match = pattern.Match(name);
            if (!match.Success)
                continue;

            stripped = name[..match.Index];
            // A number too large to parse (the ticks fallback above) is still a suffix worth
            // stripping; it just cannot contribute to the numbering.
            index = int.TryParse(match.Groups["index"].Value, out var parsed) ? parsed : 0;
            return true;
        }

        stripped = name;
        index = 0;
        return false;
    }

    /// <summary>
    /// One anchored pattern per format. A format is used only if it holds exactly one
    /// <c>{0}</c> — anything else is a translation this class cannot reverse, and guessing at it
    /// would strip text that is part of somebody's name.
    /// </summary>
    private static IReadOnlyList<Regex> PatternsFor(IEnumerable<string> suffixFormats)
    {
        ArgumentNullException.ThrowIfNull(suffixFormats);

        return suffixFormats
            .Select(format => format.Split("{0}"))
            .Where(parts => parts.Length == 2)
            .Select(parts => new Regex(
                Regex.Escape(parts[0]) + @"(?<index>\d+)" + Regex.Escape(parts[1]) + "$",
                RegexOptions.None,
                RegexTimeout))
            .ToList();
    }
}
