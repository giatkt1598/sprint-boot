using System.Text.Json;

namespace SprintBoot.Identity;

/// <summary>Transactional email copy per language, embedded from the Emails folder.</summary>
public static class EmailTexts
{
    public static IReadOnlyDictionary<string, string> Load(string language)
    {
        var file = language == "vi" ? "vi" : "en";
        using var stream = typeof(EmailTexts).Assembly.GetManifestResourceStream($"SprintBoot.Identity.Emails.{file}.json")
            ?? throw new InvalidOperationException($"Missing email texts for {file}.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
}
