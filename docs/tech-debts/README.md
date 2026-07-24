# Technical Debt Audit

Audit performed: Jul 2025. Scope: `XFiles/**/*.cs` + `XFiles/**/*.xaml`.

## Severity Matrix

| Severity | Description | Action |
|---|---|---|
| **CRITICAL** | Blocks features, causes crashes, or violates platform rules | Fix before next phase |
| **HIGH** | Degrades maintainability or risks subtle bugs | Fix within current phase |
| **MEDIUM** | Code smell, potential future issue | Schedule for cleanup sprint |
| **LOW** | Cosmetic, minor duplication | Accept or fix opportunistically |

## Summary

| Category | CRITICAL | HIGH | MEDIUM | LOW |
|---|---|---|---|---|
| [Architecture](01-architecture.md) | 1 | 2 | 3 | — |
| [Error Handling](02-error-handling.md) | — | — | 4 | 1 |
| [UWP Compliance](03-uwp-compliance.md) | — | 2 | — | — |
| [Async Patterns](04-async-patterns.md) | — | — | 1 | — |
| [Code Duplication](05-code-duplication.md) | — | — | — | 2 |
| **Total** | **1** | **4** | **8** | **3** |

## Priority Order

1. **UWP Compliance** — 2 items that cause silent failures on Xbox (SubtitleDetector P/Invoke, File.ReadAllBytes)
2. **Architecture** — MillerColumnsPage god object is the #1 maintainability blocker
3. **Async Patterns** — TCS deadlock risk (rare but hard to debug)
4. **Error Handling** — Logged exceptions help debugging, low effort
5. **Code Duplication** — Cosmetic, fix opportunistically

## Clean Areas (no issues found)

- Zero TODO/FIXME/HACK/WORKAROUND comments
- Zero `.Result` or `.Wait()` blocking calls
- Zero commented-out code blocks
- `OperationCanceledException` re-throw patterns are correct (DeezerProvider, MusicBrainzProvider)
- `DisplayRequest` catch blocks are acceptable (log + continue)
