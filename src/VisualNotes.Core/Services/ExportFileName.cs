using System.Text;

namespace VisualNotes.Core.Services;

/// <summary>Creates a deterministic, portable file stem for exported notes.</summary>
public static class ExportFileName
{
    private static readonly HashSet<char> InvalidCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static string Sanitize(string? value, string fallback = "notas", int maximumLength = 120)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);
        var builder = new StringBuilder();
        foreach (var character in value?.Normalize(NormalizationForm.FormC).Trim() ?? string.Empty)
        {
            if (!char.IsControl(character) && !InvalidCharacters.Contains(character)) builder.Append(character);
        }

        var result = builder.ToString().Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(result)) result = fallback;
        return result.Length <= maximumLength ? result : result[..maximumLength].TrimEnd(' ', '.');
    }
}
