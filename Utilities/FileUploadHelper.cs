namespace MailArchiver.Utilities
{
    /// <summary>
    /// Helper methods for validating uploaded files.
    /// </summary>
    public static class FileUploadHelper
    {
        private static readonly HashSet<string> AllowedImportExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".mbox",
                ".eml",
                ".zip"
            };

        private static readonly HashSet<string> AllowedMboxExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".mbox",
                ".mbx"
            };

        /// <summary>
        /// Checks whether the file name has an allowed extension for mail import operations.
        /// </summary>
        public static bool IsAllowedImportExtension(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            var extension = Path.GetExtension(fileName);
            return AllowedImportExtensions.Contains(extension);
        }

        /// <summary>
        /// Checks whether a file name is suitable for an MBox import. Thunderbird stores
        /// many MBox folders without an extension (for example, "Inbox" or "Android").
        /// </summary>
        public static bool IsAllowedMboxFileName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            var name = Path.GetFileName(fileName);
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith('.'))
                return false;

            var extension = Path.GetExtension(name);
            return string.IsNullOrEmpty(extension) || AllowedMboxExtensions.Contains(extension);
        }
    }
}
