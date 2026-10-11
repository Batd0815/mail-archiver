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
public sealed class MailRulesController : Controller
{
    private readonly MailArchiverDbContext _context;
    private readonly IAuthenticationService _authenticationService;
    private readonly IUserService _userService;

    public MailRulesController(MailArchiverDbContext context, IAuthenticationService authenticationService, IUserService userService)
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

        var model = new MailRuleIndexViewModel { SelectedAccountId = accountId };
        model.AccountOptions = accounts.Select(a => new SelectListItem
        {
            Text = $"{a.Name} ({a.EmailAddress})",
            Value = a.Id.ToString(),
            Selected = a.Id == accountId
        }).ToList();

        if (accountId.HasValue)
        {
            await EnsureFolderRowsAsync(accountId.Value);
            model.Rules = await _context.MailRules
                .Where(r => r.MailAccountId == accountId.Value)
                .OrderBy(r => r.Priority).ThenBy(r => r.Id)
                .ToListAsync();
        }

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int accountId)
    {
        if (!await HasAccountAccessAsync(accountId)) return Forbid();
        var model = new MailRuleEditViewModel { MailAccountId = accountId };
        await PopulateFolderOptionsAsync(model);
        return View("Edit", model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var rule = await _context.MailRules.FindAsync(id);
        if (rule == null) return NotFound();
        if (!rule.MailAccountId.HasValue || !await HasAccountAccessAsync(rule.MailAccountId.Value)) return Forbid();

        var model = new MailRuleEditViewModel
        {
            Id = rule.Id,
            MailAccountId = rule.MailAccountId.Value,
            IsEnabled = rule.IsEnabled,
            Name = rule.Name,
            FromContains = rule.FromContains,
            ToContains = rule.ToContains,
            SubjectContains = rule.SubjectContains,
            SourceFolderContains = rule.SourceFolderContains,
            HasAttachments = rule.HasAttachments,
            SentFrom = rule.SentFrom,
            SentTo = rule.SentTo,
            DateField = rule.DateField,
            TargetFolder = rule.TargetFolder ?? string.Empty
        };
        await PopulateFolderOptionsAsync(model);
        return View("Edit", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(MailRuleEditViewModel model)
    {
        if (!await HasAccountAccessAsync(model.MailAccountId)) return Forbid();

        var hasCriteria = !string.IsNullOrWhiteSpace(model.FromContains) ||
            !string.IsNullOrWhiteSpace(model.ToContains) ||
            !string.IsNullOrWhiteSpace(model.SubjectContains) ||
            !string.IsNullOrWhiteSpace(model.SourceFolderContains) ||
            model.HasAttachments.HasValue || model.SentFrom.HasValue || model.SentTo.HasValue;
        if (!hasCriteria)
            ModelState.AddModelError(string.Empty, "Bitte mindestens ein Suchkriterium angeben.");
        if (model.SentFrom.HasValue && model.SentTo.HasValue && model.SentFrom.Value.Date > model.SentTo.Value.Date)
            ModelState.AddModelError(nameof(model.SentTo), "Das Bis-Datum muss am oder nach dem Von-Datum liegen.");

        await EnsureFolderRowsAsync(model.MailAccountId);
        var folders = await _context.ArchiveFolders.Where(f => f.MailAccountId == model.MailAccountId).ToListAsync();
        var targetExists = folders.Any(f => string.Equals(BuildPath(f, folders), model.TargetFolder, StringComparison.OrdinalIgnoreCase));
        if (!targetExists)
            ModelState.AddModelError(nameof(model.TargetFolder), "Bitte einen vorhandenen Archivordner als Ziel auswählen.");

        if (!ModelState.IsValid)
        {
            await PopulateFolderOptionsAsync(model);
            return View("Edit", model);
        }

        MailRule rule;
        if (model.Id.HasValue)
        {
            var existingRule = await _context.MailRules.FirstOrDefaultAsync(r => r.Id == model.Id.Value && r.MailAccountId == model.MailAccountId);
            if (existingRule == null) return NotFound();
            rule = existingRule;
        }
        else
        {
            rule = new MailRule { MailAccountId = model.MailAccountId, Priority = await GetNextPriorityAsync(model.MailAccountId) };
            _context.MailRules.Add(rule);
        }

        rule.Name = model.Name.Trim();
        rule.IsEnabled = model.IsEnabled;
        rule.FromContains = Clean(model.FromContains);
        rule.ToContains = Clean(model.ToContains);
        rule.SubjectContains = Clean(model.SubjectContains);
        rule.SourceFolderContains = Clean(model.SourceFolderContains);
        rule.HasAttachments = model.HasAttachments;
        rule.SentFrom = model.SentFrom?.Date;
        rule.SentTo = model.SentTo?.Date;
        rule.DateField = model.DateField;
        rule.Action = MailRuleAction.MoveToFolder;
        rule.TargetFolder = BuildPath(folders.First(f => string.Equals(BuildPath(f, folders), model.TargetFolder, StringComparison.OrdinalIgnoreCase)), folders);
        await _context.SaveChangesAsync();

        TempData["SuccessMessage"] = rule.IsEnabled
            ? $"Regel „{rule.Name}“ wurde gespeichert und wird bei neu synchronisierten Mails angewendet. Bereits archivierte Mails bleiben unverändert; dafür bitte die Vorschau verwenden."
            : $"Regel „{rule.Name}“ wurde gespeichert und pausiert. Bereits archivierte Mails bleiben unverändert.";
        return RedirectToAction(nameof(Index), new { accountId = model.MailAccountId });
    }

    [HttpGet]
    public async Task<IActionResult> Preview(int id, int page = 1, int pageSize = 50)
    {
        var rule = await _context.MailRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (rule == null) return NotFound();
        if (!rule.MailAccountId.HasValue || !await HasAccountAccessAsync(rule.MailAccountId.Value)) return Forbid();

        var account = await _context.MailAccounts.AsNoTracking().FirstAsync(a => a.Id == rule.MailAccountId.Value);
        var matches = BuildMatchQuery(rule);
        var alreadyInTarget = await matches.CountAsync(e => e.FolderName == rule.TargetFolder);
        var locked = await matches.CountAsync(e => e.IsLocked && e.FolderName != rule.TargetFolder);
        var movable = matches.Where(e => !e.IsLocked && e.FolderName != rule.TargetFolder);
        var movableCount = await movable.CountAsync();
        if (pageSize is not (20 or 50 or 100)) pageSize = 50;
        var totalPages = Math.Max(1, (int)Math.Ceiling(movableCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var orderedPage = rule.DateField == MailRuleDateField.ReceivedDate
            ? movable.OrderByDescending(e => e.ReceivedDate).ThenBy(e => e.Id)
            : movable.OrderByDescending(e => e.SentDate).ThenBy(e => e.Id);
        var model = new MailRulePreviewViewModel
        {
            Rule = rule,
            AccountName = $"{account.Name} ({account.EmailAddress})",
            MatchCount = await matches.CountAsync(),
            AlreadyInTargetCount = alreadyInTarget,
            LockedCount = locked,
            MovableCount = movableCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            FirstResult = movableCount == 0 ? 0 : ((page - 1) * pageSize) + 1,
            LastResult = Math.Min(page * pageSize, movableCount),
            Samples = await orderedPage.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(e => new MailRulePreviewEmailViewModel
                {
                    Id = e.Id,
                    Subject = e.Subject,
                    From = e.From,
                    SentDate = e.SentDate,
                    ReceivedDate = e.ReceivedDate,
                    FolderName = e.FolderName
                }).ToListAsync()
        };

        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(int id)
    {
        var rule = await _context.MailRules.FirstOrDefaultAsync(r => r.Id == id);
        if (rule == null) return NotFound();
        if (!rule.MailAccountId.HasValue || !await HasAccountAccessAsync(rule.MailAccountId.Value)) return Forbid();

        var targetStillExists = await GetFolderPathAsync(rule.MailAccountId.Value, rule.TargetFolder ?? string.Empty);
        if (!targetStillExists)
        {
            TempData["ErrorMessage"] = "Der Zielordner existiert nicht mehr. Bitte die Regel bearbeiten und ein neues Ziel auswählen.";
            return RedirectToAction(nameof(Preview), new { id });
        }

        var toMove = BuildMatchQuery(rule)
            .Where(e => !e.IsLocked && e.FolderName != rule.TargetFolder);
        var previousCommandTimeout = _context.Database.GetCommandTimeout();
        _context.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        int moved;
        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            moved = await toMove.ExecuteUpdateAsync(update => update.SetProperty(e => e.FolderName, rule.TargetFolder!));
            await transaction.CommitAsync();
        }
        finally
        {
            _context.Database.SetCommandTimeout(previousCommandTimeout);
        }

        TempData["SuccessMessage"] = moved == 0
            ? "Es waren keine verschiebbaren Mails mehr vorhanden."
            : $"{moved:N0} Mails wurden in „{rule.TargetFolder}“ verschoben. Die Originale beim Anbieter wurden nicht verändert.";
        return RedirectToAction(nameof(Preview), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var rule = await _context.MailRules.FindAsync(id);
        if (rule == null) return NotFound();
        if (!rule.MailAccountId.HasValue || !await HasAccountAccessAsync(rule.MailAccountId.Value)) return Forbid();
        var accountId = rule.MailAccountId.Value;
        _context.MailRules.Remove(rule);
        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Die Regel wurde gelöscht. Bereits verschobene Mails bleiben in ihrem Ordner.";
        return RedirectToAction(nameof(Index), new { accountId });
    }

    private IQueryable<ArchivedEmail> BuildMatchQuery(MailRule rule)
    {
        if (!rule.MailAccountId.HasValue) return _context.ArchivedEmails.Where(_ => false);
        var query = _context.ArchivedEmails.AsNoTracking().Where(e => e.MailAccountId == rule.MailAccountId.Value);

        if (!string.IsNullOrWhiteSpace(rule.FromContains))
        {
            var term = rule.FromContains.Trim().ToLowerInvariant();
            query = query.Where(e => e.From.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(rule.ToContains))
        {
            var term = rule.ToContains.Trim().ToLowerInvariant();
            query = query.Where(e => e.To.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(rule.SubjectContains))
        {
            var term = rule.SubjectContains.Trim().ToLowerInvariant();
            query = query.Where(e => e.Subject.ToLower().Contains(term));
        }
        if (!string.IsNullOrWhiteSpace(rule.SourceFolderContains))
        {
            var term = rule.SourceFolderContains.Trim().ToLowerInvariant();
            query = query.Where(e => e.FolderName.ToLower().Contains(term));
        }
        if (rule.HasAttachments.HasValue)
            query = query.Where(e => e.HasAttachments == rule.HasAttachments.Value);

        if (rule.DateField == MailRuleDateField.ReceivedDate)
        {
            if (rule.SentFrom.HasValue) query = query.Where(e => e.ReceivedDate >= rule.SentFrom.Value.Date);
            if (rule.SentTo.HasValue)
            {
                var endExclusive = rule.SentTo.Value.Date.AddDays(1);
                query = query.Where(e => e.ReceivedDate < endExclusive);
            }
        }
        else
        {
            if (rule.SentFrom.HasValue) query = query.Where(e => e.SentDate >= rule.SentFrom.Value.Date);
            if (rule.SentTo.HasValue)
            {
                var endExclusive = rule.SentTo.Value.Date.AddDays(1);
                query = query.Where(e => e.SentDate < endExclusive);
            }
        }

        return query;
    }

    private async Task PopulateFolderOptionsAsync(MailRuleEditViewModel model)
    {
        await EnsureFolderRowsAsync(model.MailAccountId);
        var folders = await _context.ArchiveFolders.Where(f => f.MailAccountId == model.MailAccountId).ToListAsync();
        model.FolderOptions = folders
            .Select(f => BuildPath(f, folders))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new SelectListItem { Text = path, Value = path, Selected = path == model.TargetFolder })
            .ToList();
    }

    private async Task<bool> GetFolderPathAsync(int accountId, string path)
    {
        var folders = await _context.ArchiveFolders.Where(f => f.MailAccountId == accountId).ToListAsync();
        return folders.Any(f => string.Equals(BuildPath(f, folders), path, StringComparison.OrdinalIgnoreCase));
    }

    private async Task EnsureFolderRowsAsync(int accountId)
    {
        var paths = await _context.ArchivedEmails
            .Where(e => e.MailAccountId == accountId && e.FolderName != "")
            .Select(e => e.FolderName).Distinct().ToListAsync();
        var folders = await _context.ArchiveFolders.Where(f => f.MailAccountId == accountId).ToListAsync();
        var foldersByPath = folders.ToDictionary(f => BuildPath(f, folders), StringComparer.OrdinalIgnoreCase);
        var addedAny = false;
        foreach (var sourcePath in paths)
        {
            ArchiveFolder? parent = null;
            var accumulated = new List<string>();
            foreach (var segment in sourcePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                accumulated.Add(segment);
                var path = string.Join('/', accumulated);
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
        return user == null ? new List<MailAccount>() : await _userService.GetUserMailAccountsAsync(user.Id);
    }

    private async Task<bool> HasAccountAccessAsync(int accountId)
    {
        if (_authenticationService.IsCurrentUserAdmin(HttpContext)) return true;
        var userId = _authenticationService.GetCurrentUserId(HttpContext);
        return userId.HasValue && await _userService.IsUserAuthorizedForAccountAsync(userId.Value, accountId);
    }

    private async Task<int> GetNextPriorityAsync(int accountId) =>
        (await _context.MailRules.Where(r => r.MailAccountId == accountId).Select(r => (int?)r.Priority).MaxAsync() ?? 0) + 10;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string BuildPath(ArchiveFolder folder, IReadOnlyCollection<ArchiveFolder> all)
    {
        var parts = new List<string>();
        for (ArchiveFolder? current = folder; current != null; current = current.ParentId.HasValue ? all.FirstOrDefault(f => f.Id == current.ParentId) : null)
            parts.Add(current.Name);
        parts.Reverse();
        return string.Join('/', parts);
    }
}
