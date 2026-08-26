# ADR 0008: Autosave and recovery generations

Status: Accepted

Date: 2026-08-27

## Context

The legacy autosave stores package XML and media-change path references under the operating-system temporary directory. It cannot recover a never-saved document, can lose newly added media when the referenced temporary source disappears, derives identity from the canonical path, and does not provide an atomic boundary between package data and recovery metadata.

Canonical save and recovery snapshotting also have different ownership semantics. A canonical save may detach and reload the live `SIDocument` container after replacing its source file. Recovery must never redirect the live document to a recovery file or accept pending media changes as if the canonical package had been saved.

## Decision

- Store recoveries under `IAppPaths.RecoveryDirectory`, using a random per-document recovery ID rather than an encoded filename.
- Store each recovery as a complete, validated `.siq` containing all pending media, plus schema-versioned JSON metadata with original path, display name, UTC timestamp, length, and SHA-256.
- Write each SIQ as a new immutable generation. Atomically replace only the small metadata pointer after the new generation has been flushed, loaded through `SIDocument.Load`, and hashed. Delete older generations only after the pointer commit. A crash before the pointer commit therefore leaves the previous generation authoritative.
- Share a non-mutating `DocumentSnapshotWriter` between canonical staging and recovery generation, while retaining canonical commit/reload logic in `SafeDocumentPersistenceService`.
- Serialize autosave, manual save, and close cleanup through the existing per-document lock. A successful canonical save or user-approved close removes its recovery; failed saves retain it.
- Treat a recovery as stale only when the canonical file is newer and also loads successfully. Corrupt newer files do not suppress recovery.
- Retain malformed, unsupported, or path-escaping recovery entries for diagnosis instead of deleting them automatically.
- Keep the legacy recovery reader during migration. New writes use only the generation format.

## Consequences

- Unsaved documents and all four media collections can be recovered without relying on source media paths.
- Two documents with the same filename have independent recovery identities.
- Recovery inventory performs hashing and package validation; archive parsing is moved off the UI thread.
- Recovery storage can temporarily contain an obsolete generation or metadata backup after an interrupted cleanup, but inventory ignores unreferenced files.
- The current startup flow offers the existing restore-or-discard confirmation and opens recovered documents dirty. A richer recovery workspace with per-entry preview, reveal, restore, discard, and retention-policy controls remains required before recovery parity is complete.
