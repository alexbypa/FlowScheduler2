using System.Text.RegularExpressions;

namespace FlowScheduler.Core.Models;

public static class VectorStoreConstants {
    public const string ContextOps = "ops";
    public const string ContextLibrary = "library";
    public const string SubCategoryNoneTag = "none";
    public const string DocumentTypeDefault = "document";

    public static string NormalizeContextTag(string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return ContextOps;
        var v = value.Trim().ToLowerInvariant();
        if (v == ContextLibrary)
            return ContextLibrary;
        if (v == ContextOps)
            return ContextOps;
        return ContextOps;
    }

    public static string NormalizeTag(string? value, string fallback) {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        var s = value.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"[^a-z0-9\-]+", "-");
        s = Regex.Replace(s, "-{2,}", "-").Trim('-');
        return string.IsNullOrEmpty(s) ? fallback : s;
    }
}