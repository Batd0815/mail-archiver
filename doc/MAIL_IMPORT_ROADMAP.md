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
