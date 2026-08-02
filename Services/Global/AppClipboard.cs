namespace CameywareOrder.Services;

/// <summary>
/// What the application is currently holding for a paste — the records copied by Ctrl+C, and a token
/// saying what KIND of thing they are.
/// </summary>
/// <remarks>
/// Deliberately not the Windows clipboard. What Ctrl+V does here is "make another one of these",
/// which needs the records themselves — their ids, their child rows, their per-shop files — not a
/// serialised rendering of them. Handing that to the system clipboard would put an application's
/// internal record shape somewhere any other program can read it, and would let a paste act on rows
/// that were deleted in between.
///
/// The <see cref="Kind"/> token is what stops a paste landing on the wrong surface: orders copied
/// from the order list must not be pasteable into Store Management, and a surface that asks for its
/// own kind cannot be handed anything else. It is a plain string because the set of copyable surfaces
/// is open — a screen added later declares its own token and needs nothing here to change.
///
/// One slot, application-wide, exactly like the system clipboard: a second copy replaces the first.
/// Static state, and single-threaded by construction — every writer is a UI event handler.
/// </remarks>
public static class AppClipboard
{
    /// <summary>What kind of records are held, or null when nothing is.</summary>
    public static string? Kind { get; private set; }

    /// <summary>The records held. Empty whenever <see cref="Kind"/> is null.</summary>
    public static IReadOnlyList<object> Items { get; private set; } = Array.Empty<object>();

    /// <summary>Replaces whatever was held. An empty selection CLEARS it rather than holding nothing under a kind.</summary>
    public static void Set(string kind, IReadOnlyList<object> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(items);

        if (items.Count == 0)
        {
            Clear();
            return;
        }

        Kind = kind;
        Items = items;
    }

    public static void Clear()
    {
        Kind = null;
        Items = Array.Empty<object>();
    }

    /// <summary>Whether what is held belongs to <paramref name="kind"/> and can therefore be pasted into it.</summary>
    public static bool Holds(string kind)
        => Items.Count > 0 && string.Equals(Kind, kind, StringComparison.Ordinal);

    /// <summary>What is held, typed — empty when the clipboard holds something else.</summary>
    public static IReadOnlyList<T> ItemsOf<T>(string kind)
        => Holds(kind) ? Items.OfType<T>().ToList() : Array.Empty<T>();
}
