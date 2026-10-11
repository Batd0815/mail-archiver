using System.Text;
using System.Text.Json;
using MailArchiver.Attributes;
using MailArchiver.Data;
using MailArchiver.Models;
using MailArchiver.Services;
using MailArchiver.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MailArchiver.Controllers;

[UserAccessRequired]
public sealed class ThunderbirdRuleImportController : Controller
{
    private const int MaxImportBytes = 512 * 1024;
    private const string SessionKeyPrefix = "ThunderbirdRuleImport:";
    private readonly MailArchiverDbContext _context;
    private readonly IAuthenticationService _authenticationService;
    private readonly IUserService _userService;

    public ThunderbirdRuleImportController(
        MailArchiverDbContext context,
        IAuthenticationService authenticationService,
        IUserService userService)
    {
        _context = context;
        _authenticationService = authenticationService;
        _userService = userService;
    }

    [HttpGet]
    public async Task<IActionResult> Import(int accountId)
    {
        if (!await HasAccountAccessAsync(accountId)) return Forbid();
        var account = await _context.MailAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId);
        if (account == null) return NotFound();

        return View("~/Views/MailRules/ImportThunderbird.cshtml", new ThunderbirdRuleImportViewModel
        {
            AccountId = accountId,
            AccountName = $"{account.Name} ({account.EmailAddress})"
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxImportBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImportBytes)]
    public async Task<IActionResult> Preview(int accountId, IFormFile? file)
    {
        if (!await HasAccountAccessAsync(accountId)) return Forbid();
        var account = await _context.MailAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId);
        if (account == null) return NotFound();
        var model = new ThunderbirdRuleImportViewModel
        {
            AccountId = accountId,
            AccountName = $"{account.Name} ({account.EmailAddress})"
        };

        if (file == null || file.Length == 0)
        {
            ModelState.AddModelError(nameof(file), "Bitte eine Thunderbird-Datei auswählen.");
            return View("~/Views/MailRules/ImportThunderbird.cshtml", model);
        }
        if (file.Length > MaxImportBytes)
        {
            ModelState.AddModelError(nameof(file), "Die Datei ist größer als 512 KB.");
            return View("~/Views/MailRules/ImportThunderbird.cshtml", model);
        }
        if (!string.Equals(Path.GetFileName(file.FileName), "msgFilterRules.dat", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetExtension(file.FileName), ".dat", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(file), "Bitte eine msgFilterRules.dat-Datei auswählen.");
            return View("~/Views/MailRules/ImportThunderbird.cshtml", model);
        }

        string content;
        await using (var stream = file.OpenReadStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            content = await reader.ReadToEndAsync();

        ThunderbirdRuleParseResult parseResult;
        try
        {
            parseResult = ThunderbirdRuleFileParser.Parse(content);
        }
        catch (FormatException exception)
        {
            ModelState.AddModelError(nameof(file), exception.Message);
            return View("~/Views/MailRules/ImportThunderbird.cshtml", model);
        }

        var token = Guid.NewGuid().ToString("N");
        HttpContext.Session.SetString(
            GetSessionKey(accountId),
            JsonSerializer.Serialize(new PendingImport(token, content)));

        model.ConfirmationToken = token;
        model.SupportedRules = parseResult.SupportedRules.ToList();
        model.UnsupportedRules = parseResult.UnsupportedRules.ToList();
        model.DuplicateCount = await CountExistingDefinitionsAsync(accountId, model.SupportedRules);
        return View("~/Views/MailRules/PreviewThunderbirdImport.cshtml", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(int accountId, string confirmationToken)
    {
        if (!await HasAccountAccessAsync(accountId)) return Forbid();
        var sessionKey = GetSessionKey(accountId);
        var payloadJson = HttpContext.Session.GetString(sessionKey);
        if (string.IsNullOrEmpty(payloadJson))
        {
            TempData["ErrorMessage"] = "Die Importvorschau ist abgelaufen. Bitte lade die Datei erneut hoch.";
            return RedirectToAction(nameof(Import), new { accountId });
        }

        PendingImport? pending;
        try { pending = JsonSerializer.Deserialize<PendingImport>(payloadJson); }
        catch (JsonException) { pending = null; }
        if (pending == null || !string.Equals(pending.Token, confirmationToken, StringComparison.Ordinal))
        {
            TempData["ErrorMessage"] = "Die Importbestätigung passt nicht zur Vorschau. Bitte lade die Datei erneut hoch.";
            return RedirectToAction(nameof(Import), new { accountId });
        }

        var parseResult = ThunderbirdRuleFileParser.Parse(pending.Content);
        if (parseResult.SupportedRules.Count == 0)
        {
            TempData["ErrorMessage"] = "Es wurden keine unterstützten Regeln gefunden.";
            return RedirectToAction(nameof(Import), new { accountId });
        }

        var existingRules = await _context.MailRules.Where(r => r.MailAccountId == accountId).ToListAsync();
        var allFolders = await _context.ArchiveFolders.Where(f => f.MailAccountId == accountId).ToListAsync();
        var foldersByPath = allFolders.ToDictionary(f => BuildPath(f, allFolders), StringComparer.OrdinalIgnoreCase);
        var knownDefinitions = existingRules.ToList();
        var nextPriority = (existingRules.Select(r => (int?)r.Priority).Max() ?? 0) + 10;
        var addedRules = 0;
        var createdFolders = 0;

        await using var transaction = await _context.Database.BeginTransactionAsync();
        foreach (var imported in parseResult.SupportedRules)
        {
            if (knownDefinitions.Any(rule => IsSameDefinition(rule, imported)))
                continue;

            EnsureImportTargetFolder(accountId, imported.TargetFolder, allFolders, foldersByPath, ref createdFolders);
            var rule = new MailRule
            {
                MailAccountId = accountId,
                Name = imported.Name,
                IsEnabled = false,
                Priority = nextPriority,
                StopProcessing = true,
                FromContains = imported.FromContains,
                SubjectContains = imported.SubjectContains,
                // Thunderbird type 17 means incoming mail plus manual execution.
                // Preserve the incoming-mail scope for automatic sync in this app.
                SourceFolderContains = "INBOX",
                Action = MailRuleAction.MoveToFolder,
                TargetFolder = imported.TargetFolder
            };
            _context.MailRules.Add(rule);
            knownDefinitions.Add(rule);
            nextPriority += 10;
            addedRules++;
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        HttpContext.Session.Remove(sessionKey);

        var skipped = parseResult.SupportedRules.Count - addedRules;
        TempData["SuccessMessage"] = $"{addedRules} Regel(n) importiert und pausiert; {createdFolders} Zielordner angelegt; {skipped} bereits vorhandene Regel(n) übersprungen. Nicht unterstützte Regeln: {parseResult.UnsupportedRules.Count}.";
        return RedirectToAction("Index", "MailRules", new { accountId });
    }

    private async Task<int> CountExistingDefinitionsAsync(int accountId, IReadOnlyCollection<ParsedThunderbirdRule> imported)
    {
        if (imported.Count == 0) return 0;
        var existing = await _context.MailRules.Where(r => r.MailAccountId == accountId).ToListAsync();
        return imported.Count(definition => existing.Any(rule => IsSameDefinition(rule, definition)));
    }

    private void EnsureImportTargetFolder(
        int accountId,
        string targetPath,
        List<ArchiveFolder> allFolders,
        IDictionary<string, ArchiveFolder> foldersByPath,
        ref int createdCount)
    {
        ArchiveFolder? parent = null;
        var pathSegments = new List<string>();
        foreach (var segment in targetPath.Replace('\\', '/')
                     .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            pathSegments.Add(segment);
            var path = string.Join('/', pathSegments);
            if (!foldersByPath.TryGetValue(path, out var folder))
            {
                folder = new ArchiveFolder { MailAccountId = accountId, Name = segment, Parent = parent };
                _context.ArchiveFolders.Add(folder);
                allFolders.Add(folder);
                foldersByPath[path] = folder;
                createdCount++;
            }
            parent = folder;
        }
    }

    private static bool IsSameDefinition(MailRule existing, ParsedThunderbirdRule imported) =>
        Same(existing.Name, imported.Name) &&
        Same(existing.FromContains, imported.FromContains) &&
        Same(existing.SubjectContains, imported.SubjectContains) &&
        Same(existing.ToContains, null) &&
        Same(existing.SourceFolderContains, "INBOX") &&
        Same(existing.TargetFolder, imported.TargetFolder) &&
        existing.Action == MailRuleAction.MoveToFolder;

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim() ?? string.Empty, right?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> HasAccountAccessAsync(int accountId)
    {
        if (_authenticationService.IsCurrentUserAdmin(HttpContext)) return true;
        var userId = _authenticationService.GetCurrentUserId(HttpContext);
        return userId.HasValue && await _userService.IsUserAuthorizedForAccountAsync(userId.Value, accountId);
    }

    private static string GetSessionKey(int accountId) => $"{SessionKeyPrefix}{accountId}";

    private static string BuildPath(ArchiveFolder folder, IReadOnlyCollection<ArchiveFolder> all)
    {
        var parts = new List<string>();
        for (ArchiveFolder? current = folder; current != null; current = current.ParentId.HasValue ? all.FirstOrDefault(f => f.Id == current.ParentId) : null)
            parts.Add(current.Name);
        parts.Reverse();
        return string.Join('/', parts);
    }

    private sealed record PendingImport(string Token, string Content);
}
