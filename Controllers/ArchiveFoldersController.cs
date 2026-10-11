using MailArchiver.Attributes;
using MailArchiver.Data;
using MailArchiver.Models;
using MailArchiver.Services;
using MailArchiver.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace MailArchiver.Controllers;

[UserAccessRequired]
public sealed class ArchiveFoldersController : Controller
{
    private readonly MailArchiverDbContext _context;
    private readonly IAuthenticationService _authenticationService;
    private readonly IUserService _userService;
    public ArchiveFoldersController(MailArchiverDbContext context, IAuthenticationService authenticationService, IUserService userService)
    {
        _context = context;
        _authenticationService = authenticationService;
        _userService = userService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? accountId)
    {
        var accounts = await GetAccessibleAccountsAsync();
        if (accountId.HasValue && accounts.All(a => a.Id != accountId.Value)) return Forbid();
        accountId ??= accounts.FirstOrDefault()?.Id;
        if (accountId.HasValue) await EnsureFolderRowsAsync(accountId.Value);

        var model = new ArchiveFolderViewModel { SelectedAccountId = accountId };
        model.AccountOptions = accounts.Select(a => new SelectListItem
        {
            Text = $"{a.Name} ({a.EmailAddress})", Value = a.Id.ToString(), Selected = a.Id == accountId
        }).ToList();

        if (accountId.HasValue)
        {
            var folders = await _context.ArchiveFolders.Where(f => f.MailAccountId == accountId).ToListAsync();
            var counts = await _context.ArchivedEmails.Where(e => e.MailAccountId == accountId)
                .GroupBy(e => e.FolderName).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);
            var baseRows = folders.Select(f =>
            {
                var path = BuildPath(f, folders);
                return new ArchiveFolderRowViewModel { Id = f.Id, ParentId = f.ParentId, Path = path, MessageCount = counts.GetValueOrDefault(path), Depth = SplitPath(path).Count() - 1 };
            }).ToList();
            model.Folders = baseRows.Select(row => row with
            {
                SubtreeMessageCount = counts.Where(kvp => IsSameOrDescendantPath(kvp.Key, row.Path)).Sum(kvp => kvp.Value)
            }).OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
            model.FolderOptions = model.Folders.Select(f => new SelectListItem
            {
                Text = $"{new string(' ', f.Depth * 3)}{f.Path}", Value = f.Id.ToString()
            }).ToList();
        }
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int accountId, string name, int? parentId)
    {
        if (!await HasAccountAccessAsync(accountId)) return Forbid();
        name = NormalizeSegment(name);
        if (parentId.HasValue && !await _context.ArchiveFolders.AnyAsync(f => f.Id == parentId.Value && f.MailAccountId == accountId)) return BadRequest("Ungültiger übergeordneter Ordner.");
        if (!string.IsNullOrEmpty(name) && !await _context.ArchiveFolders.AnyAsync(f => f.MailAccountId == accountId && f.ParentId == parentId && f.Name == name))
        {
            _context.ArchiveFolders.Add(new ArchiveFolder { MailAccountId = accountId, ParentId = parentId, Name = name });
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index), new { accountId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(int id, int accountId, string name)
    {
        var folder = await _context.ArchiveFolders.FindAsync(id);
        if (folder == null) return NotFound();
        if (folder.MailAccountId != accountId || !await HasAccountAccessAsync(accountId)) return Forbid();
        var normalizedName = NormalizeSegment(name);
        if (string.IsNullOrEmpty(normalizedName)) return BadRequest("Der Ordnername darf nicht leer sein.");
        if (await _context.ArchiveFolders.AnyAsync(f => f.MailAccountId == accountId && f.ParentId == folder.ParentId && f.Name == normalizedName && f.Id != id))
        {
            TempData["ErrorMessage"] = "Auf derselben Ebene gibt es bereits einen Ordner mit diesem Namen.";
            return RedirectToAction(nameof(Index), new { accountId });
        }
        var oldPath = await GetPathAsync(folder);
        await using var transaction = await _context.Database.BeginTransactionAsync();
        folder.Name = normalizedName;
        var newPath = await GetPathAsync(folder);
        await RewriteMailPathsAsync(folder.MailAccountId, oldPath, newPath);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Index), new { accountId = folder.MailAccountId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(int id, int accountId, int? parentId)
    {
        var folder = await _context.ArchiveFolders.FindAsync(id);
        if (folder == null) return NotFound();
        if (folder.MailAccountId != accountId || !await HasAccountAccessAsync(accountId)) return Forbid();
        var all = await _context.ArchiveFolders.Where(f => f.MailAccountId == folder.MailAccountId).ToListAsync();
        if (parentId.HasValue && (!all.Any(f => f.Id == parentId) || parentId == id || IsDescendant(parentId.Value, id, all)))
            return BadRequest("Ungültiger Zielordner.");
        var destinationNameConflict = await _context.ArchiveFolders.AnyAsync(f => f.MailAccountId == accountId && f.ParentId == parentId && f.Name == folder.Name && f.Id != id);
        if (destinationNameConflict)
        {
            TempData["ErrorMessage"] = "Im Zielordner gibt es bereits einen Ordner mit diesem Namen.";
            return RedirectToAction(nameof(Index), new { accountId });
        }
        var oldPath = BuildPath(folder, all);
        await using var transaction = await _context.Database.BeginTransactionAsync();
        folder.ParentId = parentId;
        var newPath = BuildPath(folder, all);
        await RewriteMailPathsAsync(folder.MailAccountId, oldPath, newPath);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToAction(nameof(Index), new { accountId = folder.MailAccountId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, int accountId)
    {
        var folder = await _context.ArchiveFolders.FindAsync(id);
        if (folder == null) return NotFound();
        if (folder.MailAccountId != accountId || !await HasAccountAccessAsync(accountId)) return Forbid();
        var path = await GetPathAsync(folder);
        var occupied = await _context.ArchiveFolders.AnyAsync(f => f.ParentId == id) ||
            await _context.ArchivedEmails.AnyAsync(e => e.MailAccountId == folder.MailAccountId && (e.FolderName == path || e.FolderName.StartsWith(path + "/"))) ||
            await _context.MailRules.AnyAsync(r => r.MailAccountId == folder.MailAccountId && (r.TargetFolder == path || r.TargetFolder!.StartsWith(path + "/")));
        if (occupied)
        {
            TempData["ErrorMessage"] = "Der Ordner ist nicht leer. Bitte zuerst verschieben.";
            return RedirectToAction(nameof(Index), new { accountId = folder.MailAccountId });
        }
        _context.ArchiveFolders.Remove(folder);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { accountId = folder.MailAccountId });
    }

    private async Task EnsureFolderRowsAsync(int accountId)
    {
        var paths = await _context.ArchivedEmails.Where(e => e.MailAccountId == accountId && e.FolderName != "").Select(e => e.FolderName).Distinct().ToListAsync();
        var folders = await _context.ArchiveFolders.Where(f => f.MailAccountId == accountId).ToListAsync();
        var foldersByPath = folders.ToDictionary(f => BuildPath(f, folders), StringComparer.OrdinalIgnoreCase);
        var addedAny = false;
        foreach (var segmentPath in paths)
        {
            ArchiveFolder? parent = null;
            var accumulatedPath = new List<string>();
            foreach (var segment in SplitPath(segmentPath))
            {
                accumulatedPath.Add(segment);
                var path = string.Join('/', accumulatedPath);
                if (!foldersByPath.TryGetValue(path, out var folder))
                {
                    folder = new ArchiveFolder { MailAccountId = accountId, Name = segment, Parent = parent };
                    _context.ArchiveFolders.Add(folder);
                    folders.Add(folder);
                    foldersByPath[path] = folder;
                    addedAny = true;
                }
                parent = folder;
            }
        }
        if (addedAny) await _context.SaveChangesAsync();
    }

    private async Task<List<MailAccount>> GetAccessibleAccountsAsync()
    {
        if (_authenticationService.IsCurrentUserAdmin(HttpContext))
            return await _context.MailAccounts.OrderBy(a => a.Name).ToListAsync();
        var userId = _authenticationService.GetCurrentUserId(HttpContext);
        if (!userId.HasValue) return new List<MailAccount>();
        var user = await _userService.GetUserByIdAsync(userId.Value);
        if (user == null) return new List<MailAccount>();
        return await _userService.GetUserMailAccountsAsync(user.Id);
    }

    private async Task<bool> HasAccountAccessAsync(int accountId)
    {
        if (_authenticationService.IsCurrentUserAdmin(HttpContext)) return true;
        var userId = _authenticationService.GetCurrentUserId(HttpContext);
        return userId.HasValue && await _userService.IsUserAuthorizedForAccountAsync(userId.Value, accountId);
    }

    private async Task<string> GetPathAsync(ArchiveFolder folder)
    {
        var all = await _context.ArchiveFolders.Where(f => f.MailAccountId == folder.MailAccountId).ToListAsync();
        return BuildPath(folder, all);
    }

    private async Task RewriteMailPathsAsync(int accountId, string oldPath, string newPath)
    {
        await _context.ArchivedEmails
            .Where(e => e.MailAccountId == accountId &&
                (e.FolderName == oldPath || e.FolderName.StartsWith(oldPath + "/") || e.FolderName.StartsWith(oldPath + "\\")))
            .ExecuteUpdateAsync(update => update.SetProperty(
                email => email.FolderName,
                email => newPath + email.FolderName.Substring(oldPath.Length)));
        await _context.MailRules
            .Where(r => r.MailAccountId == accountId && r.TargetFolder != null &&
                (r.TargetFolder == oldPath || r.TargetFolder.StartsWith(oldPath + "/")))
            .ExecuteUpdateAsync(update => update.SetProperty(
                rule => rule.TargetFolder,
                rule => newPath + rule.TargetFolder!.Substring(oldPath.Length)));
    }

    private static string BuildPath(ArchiveFolder folder, IReadOnlyCollection<ArchiveFolder> all)
    {
        var parts = new List<string>();
        for (ArchiveFolder? current = folder; current != null; current = current.ParentId.HasValue ? all.FirstOrDefault(f => f.Id == current.ParentId) : null) parts.Add(current.Name);
        parts.Reverse();
        return string.Join('/', parts);
    }

    private static bool IsDescendant(int candidateId, int ancestorId, IReadOnlyCollection<ArchiveFolder> all)
    {
        var current = all.FirstOrDefault(f => f.Id == candidateId);
        while (current?.ParentId is int parentId)
        {
            if (parentId == ancestorId) return true;
            current = all.FirstOrDefault(f => f.Id == parentId);
        }
        return false;
    }

    private static bool IsSameOrDescendantPath(string candidate, string parent) =>
        string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase) ||
        candidate.StartsWith(parent + ".", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SplitPath(string path) => path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static string NormalizeSegment(string value) => (value ?? string.Empty).Trim().Replace('/', '-').Replace('\\', '-');
}
