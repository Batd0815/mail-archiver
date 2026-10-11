using System.Text.RegularExpressions;

namespace MailArchiver.Services;

public sealed record ParsedThunderbirdRule(
    int SourceIndex,
    string Name,
    bool SourceEnabled,
    string? FromContains,
    string? SubjectContains,
    string TargetFolder,
    string? Warning);

public sealed record UnsupportedThunderbirdRule(int SourceIndex, string Name, string Reason);

public sealed record ThunderbirdRuleParseResult(
    IReadOnlyList<ParsedThunderbirdRule> SupportedRules,
    IReadOnlyList<UnsupportedThunderbirdRule> UnsupportedRules);

/// <summary>
/// Converts the deliberately small Thunderbird filter subset used by the Mail-Archiver.
/// It never executes Thunderbird actions; it only reads one-condition "contains" move rules.
/// </summary>
public static partial class ThunderbirdRuleFileParser
{
    [GeneratedRegex(@"^(?:AND|OR)\s+\((from|subject),contains,([^)]*)\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SimpleConditionRegex();

    public static ThunderbirdRuleParseResult Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var supported = new List<ParsedThunderbirdRule>();
        var unsupported = new List<UnsupportedThunderbirdRule>();
        Dictionary<string, List<string>>? current = null;
        var sourceIndex = 0;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
            {
                if (current != null)
                    ParseBlock(current, ++sourceIndex, supported, unsupported);
                current = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }

            if (current == null) continue;
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            var value = Unquote(line[(separator + 1)..].Trim());
            if (!current.TryGetValue(key, out var values))
                current[key] = values = new List<string>();
            values.Add(value);
        }

        if (current != null)
            ParseBlock(current, ++sourceIndex, supported, unsupported);

        if (sourceIndex == 0)
            throw new FormatException("Die Datei enthält keine Thunderbird-Regelblöcke.");

        return new ThunderbirdRuleParseResult(supported, unsupported);
    }

    private static void ParseBlock(
        IReadOnlyDictionary<string, List<string>> block,
        int sourceIndex,
        ICollection<ParsedThunderbirdRule> supported,
        ICollection<UnsupportedThunderbirdRule> unsupported)
    {
        var name = Single(block, "name");
        var displayName = string.IsNullOrWhiteSpace(name) ? $"Regel {sourceIndex}" : name.Trim();
        var reason = ValidateBlock(block, out var termField, out var term, out var targetFolder, out var enabled);
        if (reason != null)
        {
            unsupported.Add(new UnsupportedThunderbirdRule(sourceIndex, displayName, reason));
            return;
        }

        var warning = term!.Length <= 2
            ? "Sehr kurzer Suchbegriff; Trefferbereich besonders sorgfältig prüfen."
            : termField == "subject" && term.Length <= 4
                ? "Kurzer Betreff-Suchbegriff; möglicher breiter Trefferbereich."
                : term.Equals("info@", StringComparison.OrdinalIgnoreCase) ||
                  term.Equals("mail@", StringComparison.OrdinalIgnoreCase) ||
                  term.Equals("otto", StringComparison.OrdinalIgnoreCase)
                    ? "Allgemeiner Absender-Suchbegriff; mögliche Fehlzuordnungen prüfen."
                    : null;

        supported.Add(new ParsedThunderbirdRule(
            sourceIndex,
            displayName,
            enabled,
            termField == "from" ? term : null,
            termField == "subject" ? term : null,
            targetFolder!,
            warning));
    }

    private static string? ValidateBlock(
        IReadOnlyDictionary<string, List<string>> block,
        out string? termField,
        out string? term,
        out string? targetFolder,
        out bool enabled)
    {
        termField = null;
        term = null;
        targetFolder = null;
        enabled = false;

        if (!TrySingle(block, "action", out var action) || !string.Equals(action, "Move to folder", StringComparison.OrdinalIgnoreCase))
            return "Aktion wird nicht unterstützt (erwartet wird „Move to folder“).";
        if (!TrySingle(block, "type", out var type) || type != "17")
            return "Der Thunderbird-Filtertyp wird nicht unterstützt; erwartet wird „Eingang + manuell“ (17).";
        if (!TrySingle(block, "actionValue", out var actionValue))
            return "Es fehlt ein eindeutiges Ziel für die Verschiebeaktion.";
        if (!TryLocalFolderPath(actionValue!, out targetFolder))
            return "Das Ziel ist kein unterstützter Thunderbird-Ordner unter „Local Folders“.";
        if (targetFolder.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
            return "Der Zielpfad enthält ein ungültiges Ordnersegment.";

        if (!TrySingle(block, "condition", out var condition))
            return "Es fehlt eine eindeutige Bedingung.";
        var match = SimpleConditionRegex().Match(condition!);
        if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[2].Value))
            return "Unterstützt wird nur eine einzelne Bedingung „Absender/Betreff enthält“.";
        termField = match.Groups[1].Value.ToLowerInvariant();
        term = match.Groups[2].Value.Trim();

        if (!TrySingle(block, "enabled", out var enabledValue) ||
            !(string.Equals(enabledValue, "yes", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(enabledValue, "no", StringComparison.OrdinalIgnoreCase)))
            return "Der Aktivierungsstatus ist ungültig oder nicht eindeutig.";
        enabled = string.Equals(enabledValue, "yes", StringComparison.OrdinalIgnoreCase);
        return null;
    }

    private static bool TryLocalFolderPath(string actionValue, out string? targetFolder)
    {
        targetFolder = null;
        const string marker = "local%20folders/";
        var index = actionValue.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        var encodedPath = index >= 0
            ? actionValue[(index + marker.Length)..]
            : null;
        if (encodedPath == null) return false;

        try
        {
            targetFolder = Uri.UnescapeDataString(encodedPath).Replace('\\', '/').Trim('/');
        }
        catch (UriFormatException)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(targetFolder) &&
               !targetFolder.Split('/').Any(string.IsNullOrWhiteSpace);
    }

    private static string? Single(IReadOnlyDictionary<string, List<string>> values, string key)
        => TrySingle(values, key, out var value) ? value : null;

    private static bool TrySingle(IReadOnlyDictionary<string, List<string>> values, string key, out string? value)
    {
        value = null;
        if (!values.TryGetValue(key, out var items) || items.Count != 1) return false;
        value = items[0];
        return true;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
        return value;
    }
}
