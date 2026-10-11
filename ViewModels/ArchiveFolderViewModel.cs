using Microsoft.AspNetCore.Mvc.Rendering;

namespace MailArchiver.ViewModels;

public sealed class ArchiveFolderViewModel
{
    public int? SelectedAccountId { get; set; }
    public List<SelectListItem> AccountOptions { get; set; } = new();
    public List<ArchiveFolderRowViewModel> Folders { get; set; } = new();
    public List<SelectListItem> FolderOptions { get; set; } = new();
}

public sealed record ArchiveFolderRowViewModel
{
    public int Id { get; init; }
    public int? ParentId { get; init; }
    public string Path { get; init; } = string.Empty;
    public int MessageCount { get; init; }
    public int SubtreeMessageCount { get; init; }
    public int Depth { get; init; }
}
