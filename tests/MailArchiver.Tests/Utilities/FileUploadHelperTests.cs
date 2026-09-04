using MailArchiver.Utilities;
using Xunit;

namespace MailArchiver.Tests.Utilities;

/// <summary>
/// Unit tests for <see cref="FileUploadHelper.IsAllowedImportExtension"/>.
/// </summary>
public class FileUploadHelperTests
{
    [Theory]
    [InlineData("archive.mbox")]
    [InlineData("email.eml")]
    [InlineData("bundle.zip")]
    [InlineData("ARCHIVE.MBOX")]
    [InlineData("Email.EML")]
    [InlineData("a/b/c.zip")]
    public void IsAllowedImportExtension_AllowedExtensions_ReturnsTrue(string fileName)
        => Assert.True(FileUploadHelper.IsAllowedImportExtension(fileName));

    [Theory]
    [InlineData("file.txt")]
    [InlineData("file.pdf")]
    [InlineData("file.7z")]
    [InlineData("file")]
    [InlineData("file.tar.gz")]
    public void IsAllowedImportExtension_DisallowedExtensions_ReturnsFalse(string fileName)
        => Assert.False(FileUploadHelper.IsAllowedImportExtension(fileName));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAllowedImportExtension_NullOrEmpty_ReturnsFalse(string? fileName)
        => Assert.False(FileUploadHelper.IsAllowedImportExtension(fileName));

    [Theory]
    [InlineData("archive.mbox")]
    [InlineData("archive.mbx")]
    [InlineData("Inbox")]
    [InlineData("Android")]
    [InlineData("Mail/Local Folders/Archive")]
    public void IsAllowedMboxFileName_ThunderbirdAndMboxNames_ReturnsTrue(string fileName)
        => Assert.True(FileUploadHelper.IsAllowedMboxFileName(fileName));

    [Theory]
    [InlineData("file.eml")]
    [InlineData("file.zip")]
    [InlineData("file.txt")]
    [InlineData(".hidden")]
    [InlineData(null)]
    public void IsAllowedMboxFileName_NonMboxNames_ReturnsFalse(string? fileName)
        => Assert.False(FileUploadHelper.IsAllowedMboxFileName(fileName));
}
