# Mail import roadmap

This document records the staged extension of MBox and Thunderbird import. Work is
performed and verified in a development environment only. Production deployment
requires an explicit approval after a successful development test.

## Scope and invariants

- Keep the attachment filter, attachment-size display/sorting, and attachment-filter
  navigation behavior unchanged.
- Never make a disabled account contact IMAP or Microsoft 365 while it is used as an
  import target.
- Check target-account authorization at every POST boundary, not only in the form.
- Treat duplicate detection as an expected import outcome, not as a technical error.
- Do not permit arbitrary server-side filesystem paths for Thunderbird imports.

## Delivery order

1. **Import targets without synchronization** — disabled accounts and IMPORT accounts
   are selectable import targets. Their folder list comes only from archived data.
2. **Clear import result** — distinguish completed, completed with duplicates, and
   failed imports.
3. **Thunderbird preview** — scan approved local paths recursively, ignore metadata
   files, show proposed folder mapping and allow confirmation before import.
4. **Persistent import batches and provenance** — persist batch status and retain the
   original source and Thunderbird folder independently of the current mail folder.
5. **Tags and rules** — model tags as many-to-many data; introduce rules as a dry run
   before any bulk operation.

## Rules status

The first rule foundation is now implemented in `Models/MailRule.cs` and
`Services/MailRuleEvaluator.cs`. It supports account scoping, source-folder,
sender/recipient/subject matching, attachment presence, sent-date ranges,
priority ordering and stop-processing behavior. Target folders are represented
as canonical paths such as `Privat/Versicherung/2026`, so nested archive
structures remain selectable and predictable. The evaluator is side-effect free
and is covered by unit tests. Persisted rule definitions, the rule-management
page, preview, and execution actions remain follow-up steps.

## Archive folder management requirement

The archive tree must be persistent rather than derived only from mail rows.
This is required for empty folders such as a future year (`2027`). The folder
management UI will therefore support:

- create empty folders and nested subfolders;
- rename and move a folder together with its complete subtree;
- delete empty folders;
- remove non-empty folders only after their messages and subfolders have been
  moved elsewhere; folder removal never deletes archived mail.

Mail rows will keep their current path for efficient search, while a separate
folder entity preserves empty folders and provides a stable place for rules to
target. All destructive-looking operations require a preview and confirmation.

The DEV folder editor is available at `/ArchiveFolders`: it backfills persistent
folder rows from existing archived paths, creates empty folders and subfolders,
renames folders, moves a complete subtree, and refuses to delete folders that
still contain mail or subfolders. The archive tree also includes empty persistent
folders. The editor shows the affected mail count and asks for confirmation before
rename or move. A dedicated server-rendered preview page and move-before-delete
workflow remain before production use.

## Phase 1a status

Implemented in commit `98210d3`:

- Disabled accounts are available as MBox import targets and labelled accordingly.
- Their folder lookup is local-only and does not call a remote provider.
- The MBox import POST validates the target against the same account-access scope as
  the GET form.

Verification: build succeeded and 40 existing MBox tests passed. The development
server test then confirmed import into a disabled target account without synchronization.

## Phase 2 status

Implemented in commit `b6b6518`:

- A duplicate-only MBox import finishes as `CompletedWithDuplicates`, not as an error.
- The status page distinguishes duplicates from failed or malformed messages.
- The CLI treats the duplicate-only completion as a successful exit.

## Phase 3a status: Thunderbird preview

Implemented in the following commit:

- `--preview-thunderbird --directory <approved-path>` performs a read-only recursive scan.
- It detects MBox files without an extension as used by Thunderbird, ignores Thunderbird
  metadata and proposes target folders while normalizing `.sbd` directories.
- The scanner does not provide a web filesystem browser, never contacts a mail server and
  validates that the source directory is below `LocalImport:AllowedPaths`.
- The path allowlist now checks directory boundaries, so a path with only the same prefix
  cannot bypass the restriction.

The next Phase 3b is the explicit, reviewable batch-queue confirmation. It must use the
preview result rather than rescan arbitrary paths from a browser request.
