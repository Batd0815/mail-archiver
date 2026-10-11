using MailArchiver.Data;
using MailArchiver.Models;
using Microsoft.EntityFrameworkCore;

namespace MailArchiver.Services;

/// <summary>
/// Applies enabled, account-specific move rules to newly archived provider messages.
/// Existing archive rows are intentionally handled through the preview-and-confirm flow.
/// </summary>
public sealed class MailRuleSyncService
{
    private readonly MailArchiverDbContext _context;
    private readonly ILogger<MailRuleSyncService> _logger;
    private readonly Dictionary<int, IReadOnlyList<MailRule>> _rulesByAccount = new();

    public MailRuleSyncService(MailArchiverDbContext context, ILogger<MailRuleSyncService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<string> ResolveFolderAsync(
        ArchivedEmail email,
        int accountId,
        string sourceFolderName,
        CancellationToken cancellationToken = default)
    {
        var rules = await GetEnabledRulesAsync(accountId, cancellationToken);
        var rule = MailRuleEvaluator.FindFirstMoveRuleForSync(email, sourceFolderName, rules, accountId);
        if (rule == null)
            return sourceFolderName;

        _logger.LogDebug(
            "Applied mail rule {RuleName} to newly synced email {MessageId} for account {AccountId}; archive folder is now {TargetFolder}",
            rule.Name, email.MessageId, accountId, rule.TargetFolder);
        return rule.TargetFolder!;
    }

    private async Task<IReadOnlyList<MailRule>> GetEnabledRulesAsync(int accountId, CancellationToken cancellationToken)
    {
        if (_rulesByAccount.TryGetValue(accountId, out var cachedRules))
            return cachedRules;

        try
        {
            var rules = await _context.MailRules
                .AsNoTracking()
                .Where(rule => rule.IsEnabled && rule.MailAccountId == accountId && rule.Action == MailRuleAction.MoveToFolder)
                .OrderBy(rule => rule.Priority)
                .ThenBy(rule => rule.Id)
                .ToListAsync(cancellationToken);
            _rulesByAccount[accountId] = rules;
            return rules;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A rule lookup must never prevent the underlying message from being archived.
            // The next account sync uses a fresh scoped service and retries the lookup.
            _logger.LogWarning(exception,
                "Could not load automatic mail rules for account {AccountId}; newly synced mail will keep its provider folder for this sync scope",
                accountId);
            _rulesByAccount[accountId] = Array.Empty<MailRule>();
            return _rulesByAccount[accountId];
        }
    }
}
