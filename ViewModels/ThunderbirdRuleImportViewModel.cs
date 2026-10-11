using MailArchiver.Services;

namespace MailArchiver.ViewModels;

public sealed class ThunderbirdRuleImportViewModel
{
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string ConfirmationToken { get; set; } = string.Empty;
    public int DuplicateCount { get; set; }
    public List<ParsedThunderbirdRule> SupportedRules { get; set; } = new();
    public List<UnsupportedThunderbirdRule> UnsupportedRules { get; set; } = new();
}
