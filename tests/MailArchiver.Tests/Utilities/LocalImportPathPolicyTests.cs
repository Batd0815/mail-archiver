using MailArchiver.Utilities;
using Xunit;

namespace MailArchiver.Tests.Utilities;

public class LocalImportPathPolicyTests
{
    [Fact]
    public void IsAllowed_AllowsTheDirectoryAndItsChildren()
    {
        var root = Path.Combine(Path.GetTempPath(), "mail-archiver-allowed");

        Assert.True(LocalImportPathPolicy.IsAllowed(root, new[] { root }));
        Assert.True(LocalImportPathPolicy.IsAllowed(Path.Combine(root, "profile", "Inbox"), new[] { root }));
    }

    [Fact]
    public void IsAllowed_RejectsAPathWithOnlyTheSamePrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "mail-archiver-allowed");
        var lookAlike = root + "-other";

        Assert.False(LocalImportPathPolicy.IsAllowed(Path.Combine(lookAlike, "Inbox"), new[] { root }));
    }
}
