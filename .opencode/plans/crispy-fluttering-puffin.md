# Phase 5 — PreviewPane (text and image)

## Context

Phase 4 delivered 3 Miller columns with live folder preview. When a file is selected in the
Current column, the Preview column shows a placeholder: `[File: {name}]\nPreview not
implemented yet.` (see `MillerColumnsPage.xaml:172-179` and `MillerColumnsPage.xaml.cs:106-113`).
This phase adds actual text and image preview for files.

## Files to Create

### `XFiles/FileSystem/FilePreviewService.cs`

Static async service. Detects file type by extension and loads content on background thread.

```csharp
public enum FilePreviewType { None, Text, Image, Unsupported, Error }

public class FilePreviewResult
{
    public FilePreviewType Type { get; set; }
    public string TextContent { get; set; }      // for Text type
    public Windows.UI.Xaml.Media.ImageSource ImageSource { get; set; }  // for Image type
    public string ErrorMessage { get; set; }      // for Error type
    public string FileType { get; set; }          // e.g. "Text", "Image", ".zip"
    public long FileSizeBytes { get; set; }
    public bool IsTruncated { get; set; }
}
```

**Static method**: `Task<FilePreviewResult> GetPreviewAsync(string filePath)`

- Runs file I/O on `Task.Run` (background thread)
- **Text detection** (~50 extensions): `.txt`, `.log`, `.md`, `.csv`, `.ini`, `.cfg`, `.conf`,
  `.toml`, `.yaml`, `.yml`, `.json`, `.jsonc`, `.json5`, `.xml`, `.html`, `.htm`, `.css`,
  `.scss`, `.less`, `.js`, `.ts`, `.jsx`, `.tsx`, `.mjs`, `.cjs`, `.cs`, `.vb`, `.fs`,
  `.fsx`, `.py`, `.pyw`, `.rb`, `.java`, `.kt`, `.kts`, `.go`, `.rs`, `.c`, `.cpp`, `.cc`,
  `.cxx`, `.h`, `.hpp`, `.hxx`, `.sh`, `.bash`, `.zsh`, `.fish`, `.bat`, `.cmd`, `.ps1`,
  `.psm1`, `.sql`, `.r`, `.R`, `.lua`, `.pl`, `.pm`, `.swift`, `.dart`, `.vue`, `.svelte`,
  `.astro`, `.env`, `.gitignore`, `.gitattributes`, `.editorconfig`, `.dockerignore`,
  `.dockerfile`, `.makefile`, `.cmake`, `.gradle`, `.gradle.kts`, `.props`, `.targets`,
  `.csproj`, `.vbproj`, `.fsproj`, `.sln`, `.rc`, `.resx`, `.resw`, `.storyboard`,
  `.strings`, `.plist`, `.xaml`, `.axaml`
- **Image detection**: `.png`, `.jpg`, `.jpeg`, `.gif`, `.bmp`, `.tiff`, `.tif`, `.webp`, `.ico`, `.svg`
- **Text loading**: `StreamReader` with 256KB limit. If file > 256KB, reads first 256KB and
  appends `"\n\n... [truncated — showing 256 KB of {total}]"`. Encoding: UTF-8 with BOM
  detection (fallback to system default on failure).
- **Image loading**: `BitmapImage` initialized on UI thread via `Dispatcher.RunAsync`.
  Uses `StorageFile` + `Streams` for async decode. Falls back to `UriSource` for simple paths.
- **Unsupported**: any extension not in text/image lists. Returns `FilePreviewType.Unsupported`
  with file size info.
- **Errors**: wraps in try/catch. Permission denied → "Access denied". Corrupt image →
  "Cannot load image". IO error → generic message. Never throws.

## Files to Modify

### `XFiles/Navigation/ColumnNavigator.cs`

**`UpdatePreviewAsync()`** (line 110): after detecting `selected.IsDirectory` is false:

1. Call `await FilePreviewService.GetPreviewAsync(selected.FullPath)`
2. Set `ColumnState` properties: `PreviewType`, `PreviewTextContent`, `PreviewImageSource`,
   `PreviewErrorMessage`, `PreviewFileType`, `PreviewFileSize`, `PreviewIsTruncated`
3. For directories, set `PreviewType = FilePreviewType.None` (existing folder listing behavior)

**`ColumnState`** (line 163): add properties:

```csharp
public FilePreviewType PreviewType { get; set; }
public string PreviewTextContent { get; set; }
public Windows.UI.Xaml.Media.ImageSource PreviewImageSource { get; set; }
public string PreviewErrorMessage { get; set; }
public string PreviewFileType { get; set; }
public long PreviewFileSize { get; set; }
public bool PreviewIsTruncated { get; set; }
```

### `XFiles/Controls/MillerColumnsPage.xaml`

Replace the placeholder `FilePreviewText` (line 172-179) with 4 new panels:

```xml
<!-- Text preview -->
<ScrollViewer Grid.Row="1" x:Name="PreviewTextScroll" Visibility="Collapsed"
              VerticalScrollBarVisibility="Auto" Padding="12,8">
    <TextBlock x:Name="PreviewTextBlock"
               FontFamily="Consolas" FontSize="14" Foreground="#CCCCCC"
               TextWrapping="Wrap" IsTextSelectionEnabled="True" />
</ScrollViewer>

<!-- Image preview -->
<Grid Grid.Row="1" x:Name="PreviewImagePanel" Visibility="Collapsed"
      Background="#111111">
    <Image x:Name="PreviewImage"
           HorizontalAlignment="Center" VerticalAlignment="Center"
           MaxWidth="90%" MaxHeight="90%"
           Stretch="Uniform" />
</Grid>

<!-- Error preview -->
<Grid Grid.Row="1" x:Name="PreviewErrorPanel" Visibility="Collapsed">
    <TextBlock x:Name="PreviewErrorText"
               Foreground="#CC4444" FontSize="16"
               HorizontalAlignment="Center" VerticalAlignment="Center"
               TextWrapping="Wrap" Padding="16" />
</Grid>

<!-- Unsupported preview -->
<Grid Grid.Row="1" x:Name="PreviewUnsupportedPanel" Visibility="Collapsed">
    <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center">
        <TextBlock x:Name="PreviewUnsupportedType"
                   Foreground="#666666" FontSize="18"
                   HorizontalAlignment="Center" />
        <TextBlock x:Name="PreviewUnsupportedSize"
                   Foreground="#444444" FontSize="14"
                   HorizontalAlignment="Center" Margin="0,8,0,0" />
    </StackPanel>
</Grid>
```

Remove the old `FilePreviewText` TextBlock entirely.

### `XFiles/Controls/MillerColumnsPage.xaml.cs`

**`UpdatePreviewColumn()`** (line 96): rewrite to handle 5 states:

1. **Folder** (`PreviewType == None && Entries.Count > 0`): show `PreviewList` (existing behavior)
2. **Text** (`PreviewType == Text`): show `PreviewTextScroll`, set `PreviewTextBlock.Text`
3. **Image** (`PreviewType == Image`): show `PreviewImagePanel`, set `PreviewImage.Source`
4. **Error** (`PreviewType == Error`): show `PreviewErrorPanel`, set `PreviewErrorText.Text`
5. **Unsupported** (`PreviewType == Unsupported`): show `PreviewUnsupportedPanel`, set type/size text
6. **Empty/None**: hide all panels (existing empty state)

Helper method to hide all preview panels before showing the right one.

## Key Decisions

- **100% file I/O on background thread** — never block UI thread for file reads
- **256KB text limit** — generous enough for most source files, won't blow up memory
- **Monospaced font: Consolas** — available on all Windows, fallback to `Cascadia Mono`
- **BitmapImage on UI thread** — UWP requires image decode on dispatcher; use
  `Dispatcher.RunAsync` for `BitmapImage.SetSource()`
- **No file size pre-check for text** — `StreamReader` handles EOF naturally; truncation
  at 256KB read boundary
- **No caching** — preview is ephemeral, re-loaded on selection change (debounced at 150ms)
- **IsTextSelectionEnabled=True** on text preview — allows copying text with gamepad/mouse
- **SVG**: included in image list — `BitmapImage` can decode SVG in UWP natively

## Verification

1. **Text preview**: navigate to a `.txt`/`.cs`/`.json` file → truncated content appears in
   monospaced font, scrollable, no UI freeze
2. **Image preview**: navigate to a `.png`/`.jpg` file → thumbnail appears centered, no
   UI freeze
3. **Large text**: navigate to file > 256KB → truncated with "[truncated]" message
4. **Unsupported**: navigate to `.zip`/`.exe` → "File preview not available" + file size
5. **Error**: navigate to permission-denied file → "Access denied" message
6. **Rapid scrolling**: scroll quickly through 20+ files → no exceptions, debounce prevents
   loading intermediate previews
7. **Folder listing**: still works (existing behavior unchanged)
8. **Build**: `MSBuild.exe "XFiles.sln" /p:Configuration=Debug /p:Platform=x64` on Windows
