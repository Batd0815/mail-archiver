using MailArchiver.Models;

namespace MailArchiver.Utilities
{
    /// <summary>
    /// Finds Thunderbird MBox folders without importing them. The scanner is deliberately
    /// filesystem-only: it does not open a mail account or read message bodies into memory.
    /// </summary>
    public static class ThunderbirdMBoxScanner
    {
        private const int HeaderProbeLength = 64 * 1024;

        public static ThunderbirdMBoxPreviewResult Scan(string sourceDirectory, string targetBaseFolder = "Thunderbird")
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
                throw new ArgumentException("A source directory is required.", nameof(sourceDirectory));

            var root = Path.GetFullPath(sourceDirectory);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException($"Thunderbird source directory not found: {root}");

            var result = new ThunderbirdMBoxPreviewResult { SourceDirectory = root };
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(root, "*", options))
                {
                    var fileName = Path.GetFileName(filePath);
                    if (IsThunderbirdMetadata(fileName))
                    {
                        result.IgnoredMetadataFiles++;
                        continue;
                    }

                    if (!FileUploadHelper.IsAllowedMboxFileName(fileName))
                    {
                        result.IgnoredNonMboxFiles++;
                        continue;
                    }

                    var info = new FileInfo(filePath);
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        result.SkippedReparsePoints++;
                        continue;
                    }

                    if (!IsMboxOrEmpty(filePath, info.Length))
                    {
                        result.IgnoredNonMboxFiles++;
                        continue;
                    }

                    var relativePath = Path.GetRelativePath(root, filePath);
                    result.Items.Add(new ThunderbirdMBoxPreviewItem
                    {
                        SourcePath = filePath,
                        RelativePath = relativePath,
                        SuggestedTargetFolder = MapToTargetFolder(relativePath, targetBaseFolder),
                        FileSize = info.Length,
                        IsEmpty = info.Length == 0
                    });
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Warnings.Add($"A directory could not be read: {ex.Message}");
            }
            catch (IOException ex)
            {
                result.Warnings.Add($"The profile scan could not complete: {ex.Message}");
            }

            result.Items.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath));
            return result;
        }

        internal static string MapToTargetFolder(string relativePath, string targetBaseFolder)
        {
            var parts = relativePath
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Select(part => part.EndsWith(".sbd", StringComparison.OrdinalIgnoreCase) ? part[..^4] : part)
                .ToList();

            if (parts.Count > 0)
            {
                var last = parts[^1];
                if (last.EndsWith(".mbox", StringComparison.OrdinalIgnoreCase))
                    parts[^1] = last[..^5];
                else if (last.EndsWith(".mbx", StringComparison.OrdinalIgnoreCase))
                    parts[^1] = last[..^4];
            }

            return string.Join('/', new[] { targetBaseFolder.Trim('/', ' ') }.Concat(parts.Where(part => !string.IsNullOrWhiteSpace(part))));
        }

        private static bool IsThunderbirdMetadata(string fileName) =>
            fileName.EndsWith(".msf", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("msgFilterRules.dat", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("panacea.dat", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("folderCache.json", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("parent.lock", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("lock", StringComparison.OrdinalIgnoreCase);

        private static bool IsMboxOrEmpty(string filePath, long length)
        {
            if (length == 0)
                return true;

            var probeLength = (int)Math.Min(length, HeaderProbeLength);
            var buffer = new byte[probeLength];
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            var text = System.Text.Encoding.Latin1.GetString(buffer, 0, bytesRead);
            return text.StartsWith("From ", StringComparison.Ordinal)
                || text.Contains("\nFrom ", StringComparison.Ordinal);
        }
    }
}
