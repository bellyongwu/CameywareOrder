using CameywareOrder.Localization;
using CameywareOrder.Models;

namespace CameywareOrder.Views;

/// <summary>
/// How a measurement term's gender classification is shown: its symbol and its localized name.
/// </summary>
/// <remarks>
/// Shared rather than repeated, for the reason <see cref="UserPresentation"/> exists: the terms list
/// already drew a ♂ / ♀ badge from its own private switch, and the term editor's gender picker needs
/// exactly the same symbols. Two copies of a symbol table drift, and a drifted copy shows the user a
/// mark that means something other than what it says.
///
/// The symbols are CHARACTERS, not drawn geometry. U+2642 and U+2640 are long-established, present
/// in the UI fonts this application already relies on, and are what the badge has always used —
/// inventing a second, vector, version of the same two marks would be the drift this class exists to
/// prevent. "Common" is the pair together rather than the combined sign U+26A5, which is NOT reliably
/// present in a UI font and would render as a missing-glyph box; showing both marks says "applies to
/// both" using only glyphs already proven on screen here.
/// </remarks>
internal static class MeasurementGenderPresentation
{
    private const string MaleSign = "♂";
    private const string FemaleSign = "♀";

    /// <summary>Symbol for a classification. Empty for <see cref="MeasurementGender.Common"/>.</summary>
    /// <remarks>
    /// Common is deliberately blank HERE: the terms list badges only the gendered terms, so that a
    /// row carrying a mark means "this one is specific". Use <see cref="SymbolWithCommon"/> where
    /// every option has to be labelled, such as a picker listing all three.
    /// </remarks>
    public static string Symbol(MeasurementGender gender) => gender switch
    {
        MeasurementGender.Male => MaleSign,
        MeasurementGender.Female => FemaleSign,
        _ => string.Empty
    };

    /// <summary>As <see cref="Symbol"/>, but Common is shown as both marks rather than as nothing.</summary>
    public static string SymbolWithCommon(MeasurementGender gender) => gender switch
    {
        MeasurementGender.Male => MaleSign,
        MeasurementGender.Female => FemaleSign,
        _ => MaleSign + FemaleSign
    };

    /// <summary>String-table key naming a classification.</summary>
    public static string NameKey(MeasurementGender gender) => gender switch
    {
        MeasurementGender.Male => "TermLanguage.GenderMale",
        MeasurementGender.Female => "TermLanguage.GenderFemale",
        _ => "TermLanguage.GenderCommon"
    };

    /// <summary>Localized name of a classification.</summary>
    /// <param name="text">
    /// Where to read it from. <see cref="ILocalizedText"/> rather than the localization service, so
    /// this works just as well against a panel previewing itself in another language — the terms
    /// screen labels its gender badges through its own scope.
    /// </param>
    public static string NameText(ILocalizedText text, MeasurementGender gender)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text[NameKey(gender)];
    }
}
