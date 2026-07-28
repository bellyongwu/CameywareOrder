using System.Text.Json;
using CameywareOrder.Configuration;

namespace CameywareOrder.Localization;

public sealed class LanguagePreferenceStore
{
    private const string FileName = "language-preference.json";

    private static string PreferenceFilePath => UserDataPaths.ResolveConfigFile(FileName);

    private static string PreferenceDirectory => System.IO.Path.GetDirectoryName(PreferenceFilePath)!;

    public static string? TryLoadLanguageCode()
    {
        try
        {
            if (!System.IO.File.Exists(PreferenceFilePath))
                return null;

            var json = System.IO.File.ReadAllText(PreferenceFilePath);
            var payload = JsonSerializer.Deserialize<LanguagePreferencePayload>(json);
            return string.IsNullOrWhiteSpace(payload?.LanguageCode) ? null : payload.LanguageCode;
        }
        catch
        {
            return null;
        }
    }

    public void SaveLanguageCode(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return;

        try
        {
            System.IO.Directory.CreateDirectory(PreferenceDirectory);
            var payload = new LanguagePreferencePayload { LanguageCode = languageCode };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(PreferenceFilePath, json);
        }
        catch
        {
            // Non-fatal: localization should still work even if persistence fails.
        }
    }

    private sealed class LanguagePreferencePayload
    {
        public string LanguageCode { get; set; } = string.Empty;
    }
}
