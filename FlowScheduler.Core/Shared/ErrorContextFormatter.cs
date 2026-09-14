using System.Text;
using System.Text.RegularExpressions;

namespace FlowScheduler.Core.Shared;

/// <summary>
/// Converts raw error data (Dictionary rows) into lightweight Markdown
/// for LLM consumption, reducing token usage by 60-80%.
/// Preserves .cs file references from stack traces for GitHub agent integration.
/// </summary>
public static class ErrorContextFormatter {
    private static readonly HashSet<string> MetadataFields = new(StringComparer.OrdinalIgnoreCase) {
        "EnableAiAnalysis", "Id", "ID", "RowNumber"
    };

    private static readonly Regex StackTraceFileRegex = new(
        @"in\s+(.+?\.cs)(?::line\s+(\d+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private const int MaxOutputChars = 2000;

    /// <summary>
    /// Formats grouped error rows into compact Markdown.
    /// Deduplicates identical messages and strips metadata fields.
    /// </summary>
    public static string FormatErrorGroups(
        IEnumerable<IGrouping<string, Dictionary<string, object>>> errorGroups,
        string taskName) {

        var sb = new StringBuilder();
        sb.AppendLine($"# Errori: {taskName}");

        foreach (var group in errorGroups) {
            var rows = group.ToList();
            sb.AppendLine($"\n## Gruppo: {group.Key} ({rows.Count} errori)");

            // Deduplicate by content fingerprint
            var uniqueErrors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var uniqueRows = new List<Dictionary<string, object>>();

            foreach (var row in rows) {
                string fingerprint = GetFingerprint(row);
                if (uniqueErrors.TryGetValue(fingerprint, out int count)) {
                    uniqueErrors[fingerprint] = count + 1;
                } else {
                    uniqueErrors[fingerprint] = 1;
                    uniqueRows.Add(row);
                }
            }

            foreach (var row in uniqueRows) {
                string fingerprint = GetFingerprint(row);
                int count = uniqueErrors[fingerprint];
                string countLabel = count > 1 ? $" (x{count})" : "";

                sb.Append($"- ");
                var fields = row
                    .Where(kv => !MetadataFields.Contains(kv.Key)
                              && kv.Value != null
                              && kv.Value != DBNull.Value
                              && !string.IsNullOrWhiteSpace(kv.Value.ToString()))
                    .ToList();

                if (fields.Count == 0) {
                    sb.AppendLine($"[vuoto]{countLabel}");
                    continue;
                }

                // If there's a Content/Message field, lead with it
                var contentField = fields.FirstOrDefault(f =>
                    f.Key.Equals("Content", StringComparison.OrdinalIgnoreCase) ||
                    f.Key.Equals("Message", StringComparison.OrdinalIgnoreCase) ||
                    f.Key.Equals("ErrorMessage", StringComparison.OrdinalIgnoreCase));

                if (contentField.Key != null) {
                    string fullContent = contentField.Value.ToString()!;
                    string stackFiles = ExtractStackTraceFiles(fullContent);
                    string val = Truncate(fullContent, 300);
                    sb.Append(val);
                    sb.Append(countLabel);

                    // Append remaining fields inline
                    var rest = fields.Where(f => f.Key != contentField.Key).ToList();
                    if (rest.Any()) {
                        sb.Append(" | ");
                        sb.Append(string.Join(", ", rest.Select(f => $"{f.Key}={Truncate(f.Value.ToString()!, 80)}")));
                    }
                    if (!string.IsNullOrEmpty(stackFiles)) {
                        sb.Append($"\n  StackTrace Files: [{stackFiles}]");
                    }
                    sb.AppendLine();
                } else {
                    // No content field — dump all as key=value
                    sb.Append(string.Join(", ", fields.Select(f => $"{f.Key}={Truncate(f.Value.ToString()!, 120)}")));
                    sb.AppendLine(countLabel);
                }

                if (sb.Length > MaxOutputChars) {
                    sb.AppendLine($"\n... (troncato, {rows.Count - uniqueRows.IndexOf(row) - 1} errori rimanenti)");
                    break;
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Formats a single group of error rows for the non-AI fallback path.
    /// </summary>
    public static string FormatSingleGroup(string groupKey, List<Dictionary<string, object>> rows) {
        var sb = new StringBuilder();
        sb.AppendLine($"## {groupKey} ({rows.Count} errori)");
        foreach (var row in rows.Take(20)) {
            string content = row.TryGetValue("Content", out var c) ? c?.ToString() ?? "" :
                             row.TryGetValue("Message", out var m) ? m?.ToString() ?? "" : "N/D";
            sb.AppendLine($"- {Truncate(content, 200)}");
        }
        if (rows.Count > 20)
            sb.AppendLine($"... e altri {rows.Count - 20} errori");
        return sb.ToString();
    }

    private static string GetFingerprint(Dictionary<string, object> row) {
        // Fingerprint based on content/message fields only
        if (row.TryGetValue("Content", out var c) && c != null) return c.ToString()!;
        if (row.TryGetValue("Message", out var m) && m != null) return m.ToString()!;
        if (row.TryGetValue("ErrorMessage", out var em) && em != null) return em.ToString()!;
        return string.Join("|", row.Where(kv => !MetadataFields.Contains(kv.Key))
                                   .Select(kv => kv.Value?.ToString() ?? ""));
    }

    /// <summary>
    /// Extracts .cs file references from stack traces before truncation.
    /// Returns compact format: "File.cs:45, Other.cs:120"
    /// </summary>
    private static string ExtractStackTraceFiles(string text) {
        var matches = StackTraceFileRegex.Matches(text);
        if (matches.Count == 0) return string.Empty;

        var files = matches
            .Select(m => {
                var fileName = Path.GetFileName(m.Groups[1].Value);
                var line = m.Groups[2].Success ? $":{m.Groups[2].Value}" : "";
                return $"{fileName}{line}";
            })
            .Distinct()
            .ToList();

        return string.Join(", ", files);
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";
}
