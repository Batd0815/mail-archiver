using MailArchiver.Models;
using Xunit;

namespace MailArchiver.Tests.Services
{
    /// <summary>
    /// The exit code of an <c>--import-mbox</c> run.
    /// <para>
    /// The case that motivated the fix is <see cref="RepeatedImport_OnlyDuplicates_ExitsZero"/>:
    /// re-importing an mbox that is already archived skips every message and finishes with
    /// <c>CompletedWithDuplicates</c>, which is a successful outcome.
    /// </para>
    /// </summary>
    public class MBoxImportExitCodeTests
    {
        [Fact]
        public void CleanImport_ExitsZero()
        {
            Assert.Equal(0, MBoxImportExitCode.For(MBoxImportJobStatus.Completed, 0, 0));
        }

        [Fact]
        public void RepeatedImport_OnlyDuplicates_ExitsZero()
        {
            Assert.Equal(0, MBoxImportExitCode.For(MBoxImportJobStatus.CompletedWithDuplicates, 0, 0));
        }

        [Theory]
        [InlineData(MBoxImportJobStatus.Completed)]
        [InlineData(MBoxImportJobStatus.CompletedWithDuplicates)]
        [InlineData(MBoxImportJobStatus.CompletedWithErrors)]
        public void FailedMessages_ExitNonZero(MBoxImportJobStatus status)
        {
            Assert.Equal(1, MBoxImportExitCode.For(status, 1, 0));
        }

        [Theory]
        [InlineData(MBoxImportJobStatus.Completed)]
        [InlineData(MBoxImportJobStatus.CompletedWithDuplicates)]
        [InlineData(MBoxImportJobStatus.CompletedWithErrors)]
        public void MalformedMessages_ExitNonZero(MBoxImportJobStatus status)
        {
            Assert.Equal(1, MBoxImportExitCode.For(status, 0, 1));
        }

        /// <summary>
        /// The reason the status test cannot be dropped in favour of the counters alone: these
        /// paths abandon the run without ever setting a counter, so a pure counter check would
        /// report success for an import that never finished.
        /// </summary>
        [Theory]
        [InlineData(MBoxImportJobStatus.Cancelled)]
        [InlineData(MBoxImportJobStatus.Failed)]
        [InlineData(MBoxImportJobStatus.Queued)]
        [InlineData(MBoxImportJobStatus.Running)]
        public void UnfinishedRun_ExitsNonZero_EvenWithNoCounters(MBoxImportJobStatus status)
        {
            Assert.Equal(1, MBoxImportExitCode.For(status, 0, 0));
        }

        [Fact]
        public void FailedRun_WithCounters_ExitsNonZero()
        {
            Assert.Equal(1, MBoxImportExitCode.For(MBoxImportJobStatus.Failed, 3, 2));
        }
    }
}
