namespace MailArchiver.Models;

/// <summary>
/// A mail rule definition. Rules are intentionally declarative so they can be
/// previewed without changing archived mail or contacting a provider.
/// </summary>
public sealed class MailRule
{
    public int Id { get; set; }
    public int? MailAccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = false;
    public int Priority { get; set; }
    public bool StopProcessing { get; set; } = true;

    public string? FromContains { get; set; }
    public string? ToContains { get; set; }
    public string? SubjectContains { get; set; }
    public string? SourceFolderContains { get; set; }
    public bool? HasAttachments { get; set; }
    public DateTime? SentFrom { get; set; }
    public DateTime? SentTo { get; set; }
    public MailRuleDateField DateField { get; set; } = MailRuleDateField.SentDate;

    public MailRuleAction Action { get; set; } = MailRuleAction.MoveToFolder;
    /// <summary>Folder path using '/' as the canonical separator.</summary>
    public string? TargetFolder { get; set; }
    public string? Tag { get; set; }
}

public enum MailRuleDateField
{
    SentDate,
    ReceivedDate
}

public enum MailRuleAction
{
    MoveToFolder,
    CopyToFolder,
    AddTag
}
