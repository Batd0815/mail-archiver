using MailArchiver.Models;

namespace MailArchiver.Utilities
{
    /// <summary>
    /// Applies the configured local-import directory allowlist with directory-boundary checks.
    /// </summary>
    public static class LocalImportPathPolicy
    {
        public static bool IsAllowed(string path, IEnumerable<string> allowedPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var normalizedPath = Path.GetFullPath(path);
            return allowedPaths.Any(allowed => IsWithinDirectory(normalizedPath, allowed));
        }

        private static bool IsWithinDirectory(string normalizedPath, string allowedPath)
        {
            if (string.IsNullOrWhiteSpace(allowedPath))
                return false;

            var normalizedAllowed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedPath));
            return string.Equals(normalizedPath, normalizedAllowed, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(normalizedAllowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(normalizedAllowed + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }
}
