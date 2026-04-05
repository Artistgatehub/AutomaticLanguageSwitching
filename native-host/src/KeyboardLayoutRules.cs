using System.Text.RegularExpressions;

namespace AutomaticLanguageSwitching.NativeHost;

internal static partial class KeyboardLayoutRules
{
    private static readonly Regex LayoutIdPattern = LayoutIdRegex();

    public static string? NormalizeLayoutId(string layoutId)
    {
        if (string.IsNullOrWhiteSpace(layoutId))
        {
            return null;
        }

        var normalized = layoutId.Trim().ToUpperInvariant();
        return LayoutIdPattern.IsMatch(normalized) ? normalized : null;
    }

    public static string GetPreferredRestoreLayoutId(
        string layoutId,
        IEnumerable<string> configuredLayoutIds)
    {
        var normalized = NormalizeLayoutId(layoutId);
        if (normalized is null)
        {
            return layoutId;
        }

        if (configuredLayoutIds.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var languageFallback = GetLanguageFallbackLayoutId(normalized);
        if (configuredLayoutIds.Contains(languageFallback, StringComparer.OrdinalIgnoreCase))
        {
            return languageFallback;
        }

        return normalized;
    }

    public static StableLayoutResolution ResolveStableLayoutCandidate(
        string source,
        string? candidateLayoutId,
        IReadOnlyCollection<string> configuredLayoutIds,
        IReadOnlyCollection<string> installedLayoutIds)
    {
        var normalized = NormalizeLayoutId(candidateLayoutId ?? string.Empty);
        if (normalized is null)
        {
            return new StableLayoutResolution(null, $"{source}:invalid");
        }

        var stableConfiguredLayoutIds = GetStrictStableLayoutIds(configuredLayoutIds);
        var stableInstalledLayoutIds = GetStrictStableLayoutIds(installedLayoutIds);
        var finalizedExact = FinalizeStableLayoutId(normalized, stableConfiguredLayoutIds, stableInstalledLayoutIds);

        if (finalizedExact is not null && string.Equals(finalizedExact, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return new StableLayoutResolution(finalizedExact, $"{source}:stable-exact");
        }

        var canonical = GetPreferredRestoreLayoutId(normalized, configuredLayoutIds);
        var finalizedCanonical = FinalizeStableLayoutId(canonical, stableConfiguredLayoutIds, stableInstalledLayoutIds);
        if (finalizedCanonical is not null)
        {
            return new StableLayoutResolution(finalizedCanonical, $"{source}:stable-canonical");
        }

        var languageFallback = GetLanguageFallbackLayoutId(normalized);
        var finalizedLanguageFallback = FinalizeStableLayoutId(languageFallback, stableConfiguredLayoutIds, stableInstalledLayoutIds);
        if (finalizedLanguageFallback is not null)
        {
            return new StableLayoutResolution(finalizedLanguageFallback, $"{source}:stable-language");
        }

        var lowWordMatch = FindLayoutByLanguageSuffix(normalized, stableConfiguredLayoutIds)
            ?? FindLayoutByLanguageSuffix(normalized, stableInstalledLayoutIds);
        if (lowWordMatch is not null)
        {
            return new StableLayoutResolution(lowWordMatch, $"{source}:suffix-match");
        }

        return new StableLayoutResolution(null, $"{source}:transient-or-non-normalized");
    }

    public static string? FinalizeStableLayoutId(
        string? candidateLayoutId,
        IReadOnlyCollection<string> stableConfiguredLayoutIds,
        IReadOnlyCollection<string> stableInstalledLayoutIds)
    {
        var strictStableLayoutId = TryNormalizeToStrictStableKlid(candidateLayoutId);
        if (strictStableLayoutId is null)
        {
            return null;
        }

        if (stableConfiguredLayoutIds.Contains(strictStableLayoutId, StringComparer.OrdinalIgnoreCase))
        {
            return strictStableLayoutId;
        }

        if (stableInstalledLayoutIds.Contains(strictStableLayoutId, StringComparer.OrdinalIgnoreCase))
        {
            return strictStableLayoutId;
        }

        return null;
    }

    public static string? TryNormalizeToStrictStableKlid(string? layoutId)
    {
        var normalized = NormalizeLayoutId(layoutId ?? string.Empty);
        if (normalized is null)
        {
            return null;
        }

        return GetLanguageFallbackLayoutId(normalized);
    }

    public static HashSet<string> GetStrictStableLayoutIds(IEnumerable<string> layoutIds)
    {
        var stableLayoutIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var layoutId in layoutIds)
        {
            var strictStableLayoutId = TryNormalizeToStrictStableKlid(layoutId);
            if (strictStableLayoutId is not null)
            {
                stableLayoutIds.Add(strictStableLayoutId);
            }
        }

        return stableLayoutIds;
    }

    public static string GetLanguageFallbackLayoutId(string layoutId)
    {
        return $"0000{layoutId[4..]}";
    }

    public static string? FindLayoutByLanguageSuffix(string layoutId, IEnumerable<string> candidates)
    {
        var suffix = layoutId[4..];
        return candidates
            .Where(candidate => candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    [GeneratedRegex("^[0-9A-F]{8}$", RegexOptions.Compiled)]
    private static partial Regex LayoutIdRegex();
}
