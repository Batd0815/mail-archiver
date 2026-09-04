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
server must be available for the controlled end-to-end check before proceeding to
the next phase.
