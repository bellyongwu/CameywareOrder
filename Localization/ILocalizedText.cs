namespace CameywareOrder.Localization;

/// <summary>
/// Somewhere to read UI text from — the application's current language, or one panel's own.
/// </summary>
/// <remarks>
/// This exists so a helper that composes a localized string does not have to care WHICH language it
/// is composing in. Before it, every such helper took <see cref="LocalizationService"/> itself, which
/// hard-wires "the language the whole application is currently in" into code that has no opinion on
/// the matter — and that is exactly the assumption a preview panel breaks.
///
/// <see cref="LocalizationService"/> implements it (the singleton, resolving the current language)
/// and so does <see cref="LocalizationScope"/> (one panel, resolving a language of its own). Take
/// this in a helper; take the concrete service only where you genuinely mean the application's
/// setting — switching it, persisting it, or listing what is available.
/// </remarks>
public interface ILocalizedText
{
    /// <summary>The text for <paramref name="key"/>, or the key itself when nothing resolves.</summary>
    string this[string key] { get; }

    /// <summary>
    /// <see cref="string.Format(string, object[])"/> against the text for <paramref name="key"/>.
    /// </summary>
    string Format(string key, params object[] args);
}
