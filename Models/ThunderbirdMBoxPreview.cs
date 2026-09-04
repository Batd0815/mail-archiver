namespace MailArchiver.Models
{
    /// <summary>
    /// A single MBox file found in an approved Thunderbird profile directory.
    /// No mail content is retained; this is preview metadata only.
    /// </summary>
    public sealed class ThunderbirdMBoxPreviewItem
    {
        public string SourcePath { get; init; } = string.Empty;
        public string RelativePath { get; init; } = string.Empty;
        public string SuggestedTargetFolder { get; init; } = string.Empty;
        public long FileSize { get; init; }
        public bool IsEmpty { get; init; }
    }

    /// <summary>
    /// Non-mutating result of scanning a Thunderbird profile for MBox folders.
    /// </summary>
    public sealed class ThunderbirdMBoxPreviewResult
    {
        public string SourceDirectory { get; init; } = string.Empty;
        public List<ThunderbirdMBoxPreviewItem> Items { get; } = new();
        public int IgnoredMetadataFiles { get; internal set; }
        public int IgnoredNonMboxFiles { get; internal set; }
        public int SkippedReparsePoints { get; internal set; }
        public List<string> Warnings { get; } = new();
    }
}
