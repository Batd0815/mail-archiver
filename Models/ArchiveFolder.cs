namespace MailArchiver.Models;

/// <summary>
/// A persistent folder in the local archive tree. Unlike ArchivedEmail.FolderName,
/// this entity also represents empty folders (for example a future year).
/// </summary>
public class ArchiveFolder
{
    public int Id { get; set; }
    public int MailAccountId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual MailAccount MailAccount { get; set; } = null!;
    public virtual ArchiveFolder? Parent { get; set; }
    public virtual ICollection<ArchiveFolder> Children { get; set; } = new List<ArchiveFolder>();
}
