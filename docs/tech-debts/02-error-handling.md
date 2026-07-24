# Error Handling Debt

## Acceptable Empty Catches (resource disposal)

These are in cleanup/teardown paths where the operation is best-effort:

| File | Line | Code | Verdict |
|---|---|---|---|
| `Audio/AudioLevelService.cs` | 418 | `try { _mediaSourceNode.Dispose(); } catch { }` | OK — dispose during teardown |
| `Audio/AudioLevelService.cs` | 424 | `try { _graph.Stop(); } catch { }` | OK — graph may already be stopped |
| `Audio/AudioLevelService.cs` | 425 | `try { _graph.QuantumStarted -= OnQuantumStarted; } catch { }` | OK — event detach during teardown |
| `Audio/AudioLevelService.cs` | 429 | `try { _fileInputNode.FileCompleted -= OnFileCompleted; } catch { }` | OK — event detach during teardown |
| `Audio/AudioLevelService.cs` | 487 | `try { frame.Dispose(); } catch { }` | OK — frame disposal in callback |
| `FileSystem/ArchiveBrowser.cs` | 340 | `try { archive.Dispose(); } catch { }` | OK — cache cleanup |

## MEDIUM: Should Log But Don't

| File | Line | Code | Risk |
|---|---|---|---|
| `FileSystem/TextEditorService.cs` | 374 | `catch { }` in encoding detection | Could mask encoding bugs; should log at Verbose |
| `Metadata/FilenameParser.cs` | 29 | `catch { }` around `Path.GetDirectoryName` | Low risk but hides unexpected failures |
| `Metadata/MusicBrainzProvider.cs` | 92 | `try { await Task.Delay(1100); } catch { }` | Delay cancellation in finally — acceptable but log at Verbose |
| `Log.cs` | 104 | `catch { }` around stack trace walk | Would cause infinite recursion if logged — leave as-is |

## LOW: Already Fixed

| File | Line | Previous | Fixed to |
|---|---|---|---|
| `Controls/SettingsPage.xaml.cs` | — | `catch { }` on cache count | `catch (Exception ex) { Log.Warning(...) }` |
| `Metadata/MusicBrainzProvider.cs` | — | `catch { }` on JSON fallback | `catch (Exception) { Log.Warning(...) }` |

## Correct Patterns (no issues)

- `OperationCanceledException` re-throw in DeezerProvider (lines 105, 133) and MusicBrainzProvider (lines 84, 189) — correct, no change needed.
- `DisplayRequest` catches in MillerColumnsPage (lines 113, 118) — log + continue, correct.
