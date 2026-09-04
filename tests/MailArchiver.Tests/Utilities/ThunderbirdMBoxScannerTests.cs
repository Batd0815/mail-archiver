using MailArchiver.Utilities;
using Xunit;

namespace MailArchiver.Tests.Utilities;

public class ThunderbirdMBoxScannerTests
{
    [Fact]
    public void Scan_FindsThunderbirdMboxFilesAndMapsFolders()
    {
        var root = CreateTempDirectory();
        try
        {
            WriteFile(root, "Mail/pop.example/Inbox", Mbox("Inbox"));
            WriteFile(root, "Mail/pop.example/Inbox.msf", "metadata");
            WriteFile(root, "Mail/Local Folders/Projects.sbd/2025", Mbox("2025"));
            WriteFile(root, "Mail/Local Folders/global-messages-db.sqlite", "not an mbox");
            WriteFile(root, "Mail/Local Folders/Empty", string.Empty);

            var result = ThunderbirdMBoxScanner.Scan(root, "Altbestand/Thunderbird");

            Assert.Equal(3, result.Items.Count);
            Assert.Contains(result.Items, item => item.RelativePath.EndsWith("Inbox")
                && item.SuggestedTargetFolder == "Altbestand/Thunderbird/Mail/pop.example/Inbox");
            Assert.Contains(result.Items, item => item.RelativePath.EndsWith("Projects.sbd" + Path.DirectorySeparatorChar + "2025")
                && item.SuggestedTargetFolder == "Altbestand/Thunderbird/Mail/Local Folders/Projects/2025");
            Assert.Contains(result.Items, item => item.IsEmpty && item.SuggestedTargetFolder.EndsWith("/Empty"));
            Assert.Equal(1, result.IgnoredMetadataFiles);
            Assert.Equal(1, result.IgnoredNonMboxFiles);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Inbox.mbox", "Thunderbird/Inbox")]
    [InlineData("Archive.mbx", "Thunderbird/Archive")]
    [InlineData("Inbox.sbd/Child", "Thunderbird/Inbox/Child")]
    public void MapToTargetFolder_NormalizesThunderbirdConventions(string source, string expected)
    {
        var relativePath = source.Replace('/', Path.DirectorySeparatorChar);
        Assert.Equal(expected, ThunderbirdMBoxScanner.MapToTargetFolder(relativePath, "Thunderbird"));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "mail-archiver-thunderbird-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private static string Mbox(string subject) =>
        $"From sender@example.test Thu Jan 01 00:00:00 2026{Environment.NewLine}" +
        $"Subject: {subject}{Environment.NewLine}{Environment.NewLine}Body{Environment.NewLine}";
}
