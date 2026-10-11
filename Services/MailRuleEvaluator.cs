using MailArchiver.Models;

namespace MailArchiver.Services;

public sealed record MailRuleMatch(MailRule Rule, ArchivedEmail Email);

/// <summary>
/// Pure rule matching logic used by previews and sync-time execution.
/// It has no database or provider side effects.
/// </summary>
public sealed class MailRuleEvaluator
{
    public IReadOnlyList<MailRuleMatch> Evaluate(
        IEnumerable<ArchivedEmail> emails,
        IEnumerable<MailRule> rules,
        int? accountId = null)
    {
        var orderedRules = rules
            .Where(r => r.IsEnabled && (accountId == null || r.MailAccountId == null || r.MailAccountId == accountId))
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id)
            .ToList();

        var matches = new List<MailRuleMatch>();
        foreach (var email in emails)
        {
            foreach (var rule in orderedRules)
            {
                if (!Matches(email, rule))
                    continue;

                matches.Add(new MailRuleMatch(rule, email));
                if (rule.StopProcessing)
                    break;
            }
        }

        return matches;
    }

    public static bool Matches(ArchivedEmail email, MailRule rule)
        => Matches(email, rule, email.FolderName);

    public static MailRule? FindFirstMoveRuleForSync(
        ArchivedEmail email,
        string sourceFolderName,
        IEnumerable<MailRule> rules,
        int accountId)
    {
        if (email.IsLocked)
            return null;

        return rules
            .Where(rule => rule.IsEnabled &&
                           rule.MailAccountId == accountId &&
                           rule.Action == MailRuleAction.MoveToFolder &&
                           !string.IsNullOrWhiteSpace(rule.TargetFolder))
            .OrderBy(rule => rule.Priority)
            .ThenBy(rule => rule.Id)
            .FirstOrDefault(rule => Matches(email, rule, sourceFolderName));
    }

    private static bool Matches(ArchivedEmail email, MailRule rule, string? folderName)
    {
        if (rule.MailAccountId.HasValue && rule.MailAccountId != email.MailAccountId)
            return false;
        if (!Contains(email.From, rule.FromContains) ||
            !Contains(email.To, rule.ToContains) ||
            !Contains(email.Subject, rule.SubjectContains) ||
            !Contains(folderName, rule.SourceFolderContains))
            return false;
        if (rule.HasAttachments.HasValue && email.HasAttachments != rule.HasAttachments.Value)
            return false;
        var selectedDate = rule.DateField == MailRuleDateField.ReceivedDate ? email.ReceivedDate : email.SentDate;
        if (rule.SentFrom.HasValue && selectedDate < rule.SentFrom.Value.Date)
            return false;
        if (rule.SentTo.HasValue && selectedDate >= rule.SentTo.Value.Date.AddDays(1))
            return false;

        return true;
    }

    private static bool Contains(string? value, string? search)
        => string.IsNullOrWhiteSpace(search) ||
           (!string.IsNullOrEmpty(value) && value.Contains(search, StringComparison.OrdinalIgnoreCase));
}
