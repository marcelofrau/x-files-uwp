# File Browser — Modelo de Dados e Acesso ao Disco

Referência: `dosbox-pure-uwp` (`Content/FileBrowser.cpp`, `vfs_implementation_uwp.cpp`,
`dosbox_uwpMain.cpp`). Patterns C++ validados no Xbox real, adaptados para C#/XAML.

---

## Manifest — Capabilities obrigatórias

```xml
<rescap:Capability Name="broadFileSystemAccess" />
<rescap:Capability Name="runFullTrust" />
```

Ambas são `rescap:` (restricted) — precisam estar no `Package.appxmanifest`. Sem essas
duas, APIs `*FromApp` falham silenciosamente no Xbox. Não usar `musicLibrary`,
`picturesLibrary`, etc. — `broadFileSystemAccess` cobre tudo.

---

## Regra de ouro: APIs Win32 devem usar variantes `*FromApp`

No Xbox UWP, mesmo com `broadFileSystemAccess`, as APIs CRT padrão são **bloqueadas**:

| CRT/Win32 padrão (BLOQUEADA) | Variante UWP (USAR) |
|---|---|
| `FindFirstFile` / `FindNextFile` | `FindFirstFileExFromAppW` / `FindNextFileW` |
| `_wfopen` / `fopen` | `CreateFile2FromAppW` |
| `_wstat64` / `_waccess` | `GetFileAttributesExFromAppW` |
| `CreateFileW` | `CreateFile2FromAppW` |
| `DeleteFileW` | `DeleteFileFromAppW` |
| `MoveFileW` | `MoveFileFromAppW` |
| `CreateDirectoryW` | `CreateDirectoryFromAppW` |

Header necessário: `#include <fileapifromapp.h>` (C++). Em C#, declarar via P/Invoke
com `[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]` apontando pra versão
`FromApp`.

---

## P/Invoke — Declarações C# necessárias

```csharp
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct WIN32_FIND_DATA
{
    public uint dwFileAttributes;
    public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
    public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
    public System.Runtime.InteropServices.Comtras.FILETIME ftLastWriteTime;
    public uint nFileSizeHigh;
    public uint nFileSizeLow;
    public uint dwReserved0;
    public uint dwReserved1;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string cFileName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
    public string cAlternateFileName;
}

private const uint FIND_FIRST_EX_LARGE_FETCH = 0x00000002;
private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
private const int INVALID_HANDLE_VALUE = -1;

[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
private static extern IntPtr FindFirstFileExFromAppW(
    string lpFileName,
    FINDEX_INFO_LEVELS fInfoLevelId,
    out WIN32_FIND_DATA lpFindFileData,
    FINDEX_SEARCH_OPS fSearchOp,
    IntPtr lpSearchFilter,
    uint dwAdditionalFlags);

[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
private static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

[DllImport("kernel32.dll", SetLastError = true)]
private static extern bool FindClose(IntPtr hFindFile);

[DllImport("kernel32.dll")]
private static extern uint GetLogicalDrives();

public enum FINDEX_INFO_LEVELS { FindExInfoStandard = 0 }
public enum FINDEX_SEARCH_OPS { FindExSearchNameMatch = 0 }
```

---

## `FileEntry` (modelo base)

```csharp
public class FileEntry
{
    public string Name { get; set; }
    public string FullPath { get; set; }
    public bool IsDirectory { get; set; }
    public bool IsArchive { get; set; }      // .zip/.7z/.rar — tratado como "pasta virtual"
    public long SizeBytes { get; set; }      // 0 para diretórios
    public DateTimeOffset? LastModified { get; set; }

    // Presente apenas quando o entry vive DENTRO de um arquivo compactado:
    public string ArchiveRootPath { get; set; }     // caminho do .zip/.7z/.rar no disco real
    public string ArchiveInternalPath { get; set; } // caminho relativo dentro do arquivo
}
```

`IsArchive` é derivado da extensão (`.zip`, `.7z`, `.rar`) e faz o item se comportar como
pasta na navegação (drill-in com A/D-pad direita), mesmo sendo fisicamente um arquivo.

---

## `DirectoryScanner`

Responsável por listar o conteúdo de um caminho **real** (fora de arquivos compactados —
esse caso é do `ArchiveBrowser`, ver `ARCHIVES.md`).

### Nível raiz (`path == null`/vazio)

```csharp
uint drives = GetLogicalDrives(); // bitmask: bit 0 = A:, bit 1 = B:, etc.
for (int i = 0; i < 26; i++)
{
    if ((drives & (1 << i)) != 0)
    {
        string driveLetter = $"{(char)('A' + i)}:\\";
        entries.Add(new FileEntry { Name = driveLetter, IsDirectory = true });
    }
}
```

- Entrada sintética `[App Data]` apontando para
  `ApplicationData.Current.LocalFolder.Path` (sandbox da própria app, sempre acessível
  mesmo sem `broadFileSystemAccess`).
- Não há detecção específica de USB — todos os drives são listados idênticos.

### Nível não-raiz — Scan de diretório

**NÃO usar `StorageFolder.GetFoldersAsync()`/`GetFilesAsync()`** como método primário —
essas APIs exigem que o caminho esteja na `FutureAccessList` ou dentro de pastas
declaradas, o que não cobre "qualquer drive USB conectado ao Xbox".

Padrão validado em `FileBrowser.cpp:207-271`:

```csharp
string searchPath = Path.Combine(path, "*");
IntPtr hFind = FindFirstFileExFromAppW(
    searchPath,
    FINDEX_INFO_LEVELS.FindExInfoStandard,
    out WIN32_FIND_DATA findData,
    FINDEX_SEARCH_OPS.FindExSearchNameMatch,
    IntPtr.Zero,
    FIND_FIRST_EX_LARGE_FETCH);

if (hFind == new IntPtr(INVALID_HANDLE_VALUE))
{
    // Falha (AccessDenied, etc.) — adiciona ".." pra usuário poder voltar
    entries.Add(new FileEntry { Name = "..", IsDirectory = true });
    return;
}

var dirs = new List<FileEntry>();
var files = new List<FileEntry>();

do
{
    string name = findData.cFileName;
    if (name == "." ) continue;
    if (name == "..")
    {
        dirs.Insert(0, new FileEntry { Name = "..", IsDirectory = true });
        continue;
    }
    bool isDir = (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
    var entry = new FileEntry
    {
        Name = name,
        FullPath = Path.Combine(path, name),
        IsDirectory = isDir,
        SizeBytes = isDir ? 0 : (findData.nFileSizeHigh << 32) | findData.nFileSizeLow
    };
    if (isDir) dirs.Add(entry);
    else files.Add(entry);
}
while (FindNextFileW(hFind, out findData));

FindClose(hFind);
```

### Ordenação
1. `..` (voltar) sempre primeiro, quando aplicável.
2. Diretórios em ordem alfabética (case-insensitive).
3. Arquivos em ordem alfabética (case-insensitive).
4. Arquivos compactados (`IsArchive == true`) ficam misturados com os arquivos normais na
   ordenação alfabética (não em grupo separado) — mantém a lista previsível.

---

## Error handling — padrão do dosbox-pure-uwp

O precedente mostra tratamento graceful/degradado:

- **Scan falhou** (`INVALID_HANDLE_VALUE`): adiciona `".."` sem lançar exceção → usuário
  pode voltar. Não mostra dialog de erro.
- **`ApplicationData.Current.LocalFolder`** falha: try/catch, log warning,continua sem
  entrada `[App Data]`.
- **`CreateFile2FromAppW` falha** (`INVALID_HANDLE_VALUE`): retorna null/erro sem crash.
- **Drive sem permissão**: scan retorna `".."` apenas, lista fica quase vazia.

Filosofia: **nunca crashar** — sempre ter saída de navegação disponível.

---

## ACL — Permissão pós-move

Arquivos movidos com `MoveFileFromAppW` **perdem herança de ACL** no UWP. Precisam de
`SetSecurityInfo` pra conceder acesso a `S-1-15-2-1` (ALL_APPLICATION_PACKAGES):

```csharp
// Só necessário após MoveFileFromAppW. DeleteFileFromAppW não precisa.
// Detalhes em vfs_implementation_uwp.cpp:909-972 (função uwp_set_acl).
```

Na prática: `Copy + Delete` pode evitar esse problema (ou tratar no `FileOperations`).

---

## Path normalization

UWP é sensível a forward slashes. Normalizar sempre pra backslash:

```csharp
path = path.Replace('/', '\\');
```

---

## `System.IO` vs `StorageFile` — quando usar cada

| Contexto | Usar |
|---|---|
| Listar diretório (DirectoryScanner) | P/Invoke `FindFirstFileExFromAppW` |
| Abrir arquivo pra leitura/escrita | `CreateFile2FromAppW` ou `System.IO.FileStream` |
| Copiar/Mover/Deletar | `System.IO.File.Copy/Move/Delete` (funciona com broadFileSystemAccess) |
| Obter LocalFolder da app | `ApplicationData.Current.LocalFolder` (API UWP normal) |
| Ler conteúdo de arquivo pra preview | `System.IO.StreamReader` / `StorageFile` (ambos funcionam) |

---

## Ações de arquivo (`FileOperations`)

Implementadas como operações assíncronas simples sobre caminhos reais (fora de arquivo
compactado — dentro de compactado, só leitura/extração faz sentido):

```csharp
Task CopyAsync(string sourcePath, string destDir, IProgress<double> progress);
Task MoveAsync(string sourcePath, string destDir);
Task RenameAsync(string path, string newName);
Task DeleteAsync(string path); // confirmação obrigatória via diálogo antes de chamar
```

- Usa `System.IO` diretamente (não `StorageFile`) pelo mesmo motivo do `DirectoryScanner`:
  cobertura de caminhos fora do sandbox declarado, com `broadFileSystemAccess`.
- Operações de "Copiar/Mover" precisam de um segundo momento de navegação (escolher pasta
  destino) — reaproveita o mesmo componente de coluna `Current`, em um "modo de seleção de
  destino" (flag na ViewModel, não uma tela nova).

---

## Extensões whitelisted vs mostrar tudo

Diferente do `dosbox-pure-uwp` (que filtra por extensão porque só interessam formatos que o
emulador consegue carregar), o X-Files é um file browser genérico: **mostra todos os
arquivos**, sem whitelist de extensão. `IsArchive` apenas habilita comportamento extra
(drill-in), não filtra visibilidade.
