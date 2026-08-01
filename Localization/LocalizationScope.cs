using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CameywareOrder.Localization;

/// <summary>
/// One panel's own view of the string table, in a language it chooses for itself.
/// </summary>
/// <remarks>
/// The application has ONE language, and until this existed every piece of UI text was read straight
/// off <see cref="LocalizationService.Instance"/>, so seeing a screen in another language meant
/// switching the whole application into it and back again. A scope is the smaller unit: it reads the
/// same table through the same fallbacks, but against a language of its own, and switching it moves
/// nothing outside the panel that owns it.
///
/// It is a plain object with a parameterless constructor precisely so a panel can declare one in its
/// own <c>Resources</c> and bind to it exactly as it bound to the singleton — same indexer, same
/// <c>Path=[Some.Key]</c>, one word changed at the binding site:
/// <code>
/// &lt;loc:LocalizationScope x:Key="Scope"/&gt;
/// ...
/// Text="{Binding Source={StaticResource Scope}, Path=[Some.Key], Mode=OneWay}"
/// </code>
/// Pair it with <c>Controls/LanguageScopeSelector</c> to give the panel a picker; that control needs
/// nothing but the scope.
///
/// <b>Following, versus being set.</b> A fresh scope FOLLOWS the application: it renders whatever the
/// app is in and re-renders when the app switches. Assigning <see cref="LanguageCode"/> pins it, and
/// <see cref="Follow"/> lets go again. Following is the default because a panel that has never been
/// asked to preview anything must behave exactly as it did before it grew a scope.
///
/// <b>Detach it.</b> A scope subscribes to the localization singleton, which therefore holds a
/// reference to it for as long as the process lives. A window that opens one must call
/// <see cref="Detach"/> when it closes, or every window ever opened stays alive through that
/// subscription. Same rule, and the same reason, as <c>MainViewModel.Detach</c>.
/// </remarks>
public sealed class LocalizationScope : INotifyPropertyChanged, ILocalizedText
{
    private readonly LocalizationService _localization;
    private string? _languageCode;

    public LocalizationScope() : this(LocalizationService.Instance) { }

    /// <summary>Takes the service explicitly, so the rules are testable against a table that is not the loaded one.</summary>
    public LocalizationScope(LocalizationService localization)
    {
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _localization.LanguageChanged += OnApplicationLanguageChanged;
    }

    /// <summary>
    /// The language this scope renders in. Null while it follows the application; assigning a code
    /// pins it, and assigning null is the same as calling <see cref="Follow"/>.
    /// </summary>
    public string? LanguageCode
    {
        get => _languageCode;
        set
        {
            var resolved = string.IsNullOrWhiteSpace(value) ? null : value;
            if (string.Equals(_languageCode, resolved, StringComparison.OrdinalIgnoreCase))
                return;

            _languageCode = resolved;
            NotifyTextChanged();
        }
    }

    /// <summary>The code text is actually resolved against — the pinned one, else the application's.</summary>
    public string EffectiveLanguageCode => _languageCode ?? _localization.CurrentLanguageCode;

    /// <summary>Whether this scope still moves with the application's own language setting.</summary>
    public bool FollowsApplication => _languageCode is null;

    /// <summary>Raised whenever the text this scope returns has changed, for callers that render in code.</summary>
    public event EventHandler? TextChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => _localization.GetText(key, EffectiveLanguageCode);

    public string Format(string key, params object[] args) => string.Format(this[key], args);

    /// <summary>Joins items the way <see cref="EffectiveLanguageCode"/> punctuates a list.</summary>
    public string JoinList(IEnumerable<string> values) => _localization.JoinList(values, EffectiveLanguageCode);

    /// <summary>Gives up a pinned language and moves with the application again.</summary>
    public void Follow() => LanguageCode = null;

    /// <summary>
    /// Unsubscribes from the localization singleton. Call it when the owning panel closes — see the
    /// remarks on the class for what leaks otherwise.
    /// </summary>
    public void Detach() => _localization.LanguageChanged -= OnApplicationLanguageChanged;

    private void OnApplicationLanguageChanged(object? sender, EventArgs e)
    {
        // A pinned scope is deliberately unmoved by the application switching languages — that is the
        // whole point of pinning it. It still re-announces, because the fallback for a key missing
        // from the pinned language resolves through the application's, so its text CAN change.
        NotifyTextChanged();
    }

    private void NotifyTextChanged()
    {
        OnPropertyChanged(nameof(EffectiveLanguageCode));
        OnPropertyChanged(nameof(FollowsApplication));
        // The name WPF gives an indexer's change notification. Without it every {Binding Path=[Key]}
        // in the panel keeps whatever it first resolved, and a scope that cannot re-render is no use
        // to anybody — this is the same line LocalizationService raises for the same reason.
        OnPropertyChanged("Item[]");
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
