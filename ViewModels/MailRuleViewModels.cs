using System.ComponentModel.DataAnnotations;
using MailArchiver.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MailArchiver.ViewModels;

public sealed class MailRuleIndexViewModel
{
    public int? SelectedAccountId { get; set; }
    public List<SelectListItem> AccountOptions { get; set; } = new();
    public List<MailRule> Rules { get; set; } = new();
}

public sealed class MailRuleEditViewModel
{
    public int? Id { get; set; }
    public int MailAccountId { get; set; }
    public bool IsEnabled { get; set; } = true;

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    public string? FromContains { get; set; }
    public string? ToContains { get; set; }
    public string? SubjectContains { get; set; }
    public string? SourceFolderContains { get; set; }
    public bool? HasAttachments { get; set; }
    [DataType(DataType.Date)]
    public DateTime? SentFrom { get; set; }
    [DataType(DataType.Date)]
    public DateTime? SentTo { get; set; }
    public MailRuleDateField DateField { get; set; } = MailRuleDateField.SentDate;

    [Required]
    public string TargetFolder { get; set; } = string.Empty;

    public List<SelectListItem> FolderOptions { get; set; } = new();
}

public sealed class MailRulePreviewViewModel
{
    public MailRule Rule { get; set; } = new();
    public string AccountName { get; set; } = string.Empty;
    public int MatchCount { get; set; }
    public int MovableCount { get; set; }
    public int LockedCount { get; set; }
    public int AlreadyInTargetCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public int FirstResult { get; set; }
    public int LastResult { get; set; }
    public List<MailRulePreviewEmailViewModel> Samples { get; set; } = new();
}

public sealed class MailRulePreviewEmailViewModel
{
    public int Id { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public DateTime SentDate { get; set; }
    public DateTime ReceivedDate { get; set; }
    public string FolderName { get; set; } = string.Empty;
}
