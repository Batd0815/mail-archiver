using MailArchiver.Models;
using MailArchiver.Services;

namespace MailArchiver.Tests;

public sealed class MailRuleEvaluatorTests
{
    [Fact]
    public void Matches_sender_and_subject_case_insensitively()
    {
        var email = new ArchivedEmail
        {
            MailAccountId = 1,
            From = "News@Example.test",
            To = "rainer@example.test",
            Subject = "Weekly Newsletter",
            SentDate = new DateTime(2026, 9, 1),
        };
        var rule = new MailRule { FromContains = "news@example", SubjectContains = "newsletter" };

        Assert.True(MailRuleEvaluator.Matches(email, rule));
    }

    [Fact]
    public void Stop_processing_only_keeps_the_first_matching_rule()
    {
        var email = new ArchivedEmail { MailAccountId = 1, Subject = "Invoice" };
        var rules = new[]
        {
            new MailRule { Id = 1, Priority = 1, IsEnabled = true, SubjectContains = "invoice", TargetFolder = "Invoices" },
            new MailRule { Id = 2, Priority = 2, IsEnabled = true, SubjectContains = "invoice", TargetFolder = "Review" },
        };

        var matches = new MailRuleEvaluator().Evaluate(new[] { email }, rules);

        var match = Assert.Single(matches);
        Assert.Equal(1, match.Rule.Id);
    }

    [Fact]
    public void Can_match_a_source_subfolder_and_keep_a_nested_target_path()
    {
        var email = new ArchivedEmail
        {
            MailAccountId = 1,
            FolderName = "Altbestand/Thunderbird/Mail/Local Folders/Newsletter",
            Subject = "Weekly Newsletter",
        };
        var rule = new MailRule
        {
            IsEnabled = true,
            SourceFolderContains = "Newsletter",
            TargetFolder = "Archiv/Newsletter",
        };

        var match = Assert.Single(new MailRuleEvaluator().Evaluate(new[] { email }, new[] { rule }));

        Assert.Equal("Archiv/Newsletter", match.Rule.TargetFolder);
    }

    [Fact]
    public void Date_range_includes_the_entire_end_day()
    {
        var email = new ArchivedEmail
        {
            MailAccountId = 1,
            SentDate = new DateTime(2024, 12, 31, 23, 59, 59),
        };
        var rule = new MailRule
        {
            SentFrom = new DateTime(2024, 1, 1),
            SentTo = new DateTime(2024, 12, 31),
        };

        Assert.True(MailRuleEvaluator.Matches(email, rule));
        email.SentDate = new DateTime(2025, 1, 1);
        Assert.False(MailRuleEvaluator.Matches(email, rule));
    }

    [Fact]
    public void Can_filter_by_received_date_instead_of_sent_date()
    {
        var email = new ArchivedEmail
        {
            MailAccountId = 1,
            SentDate = new DateTime(2023, 12, 31),
            ReceivedDate = new DateTime(2024, 1, 1, 0, 1, 0),
        };
        var rule = new MailRule
        {
            DateField = MailRuleDateField.ReceivedDate,
            SentFrom = new DateTime(2024, 1, 1),
            SentTo = new DateTime(2024, 1, 1),
        };

        Assert.True(MailRuleEvaluator.Matches(email, rule));
    }

    [Fact]
    public void Sync_uses_first_enabled_rule_for_same_account_and_provider_source_folder()
    {
        var email = new ArchivedEmail
        {
            MailAccountId = 7,
            FolderName = "Already-mapped-target",
            From = "newsletter@example.test",
            SentDate = new DateTime(2024, 6, 15),
        };
        var rules = new[]
        {
            new MailRule { Id = 1, MailAccountId = 7, IsEnabled = true, Priority = 5, FromContains = "newsletter", TargetFolder = "Later" },
            new MailRule { Id = 2, MailAccountId = 7, IsEnabled = true, Priority = 2, SourceFolderContains = "INBOX", TargetFolder = "Archive/2024" },
            new MailRule { Id = 3, MailAccountId = 7, IsEnabled = false, Priority = 1, TargetFolder = "Disabled" },
            new MailRule { Id = 4, MailAccountId = 8, IsEnabled = true, Priority = 0, TargetFolder = "Other account" },
        };

        var match = MailRuleEvaluator.FindFirstMoveRuleForSync(email, "INBOX", rules, accountId: 7);

        Assert.NotNull(match);
        Assert.Equal(2, match.Id);
    }

    [Fact]
    public void Sync_does_not_move_locked_mail()
    {
        var email = new ArchivedEmail { MailAccountId = 7, IsLocked = true, Subject = "Invoice" };
        var rule = new MailRule { MailAccountId = 7, IsEnabled = true, SubjectContains = "invoice", TargetFolder = "Invoices" };

        var match = MailRuleEvaluator.FindFirstMoveRuleForSync(email, "INBOX", new[] { rule }, accountId: 7);

        Assert.Null(match);
    }
}
