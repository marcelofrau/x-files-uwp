# File Browser — Modelo de Dados e Acesso ao Disco

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

## `DirectoryScanner`

Responsável por listar o conteúdo de um caminho **real** (fora de arquivos compactados —
esse caso é do `ArchiveBrowser`, ver `ARCHIVES.md`).

### Nível raiz (`path == null`/vazio)
- `GetLogicalDrives()` (P/Invoke, `kernel32.dll`) → bitmask de drives → gera um `FileEntry`
  por drive (`C:\`, `D:\`, etc.), `IsDirectory = true`.
- Entrada sintética `[App Data]` apontando para
  `ApplicationData.Current.LocalFolder.Path` (sandbox da própria app, sempre acessível
  mesmo sem `broadFileSystemAccess`).

### Nível não-raiz
- **Não usar `StorageFolder.GetFoldersAsync()`/`GetFilesAsync()`** como método primário —
  essas APIs exigem que o caminho esteja na `FutureAccessList` ou dentro de pastas
  declaradas, o que não cobre "qualquer drive USB conectado ao Xbox".
- Usar P/Invoke direto: `FindFirstFileExFromAppW` (padrão `<path>\*`,
  `FindExInfoStandard`, `FindExSearchNameMatch`, `FIND_FIRST_EX_LARGE_FETCH`) +
  `FindNextFileW`, exatamente como valida o `dosbox-pure-uwp`
  (`Content/FileBrowser.cpp:156-217`) — só funciona com as capabilities
  `broadFileSystemAccess` + `runFullTrust` declaradas no manifest.
- Isso é uma decisão técnica validada por precedente (RetroArch UWP também usa essa
  abordagem no Xbox) — reduz risco de reinventar algo que não funciona no Developer Mode.

### Ordenação
1. `..` (voltar) sempre primeiro, quando aplicável.
2. Diretórios em ordem alfabética (case-insensitive).
3. Arquivos em ordem alfabética (case-insensitive).
4. Arquivos compactados (`IsArchive == true`) ficam misturados com os arquivos normais na
   ordenação alfabética (não em grupo separado) — mantém a lista previsível.

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

## Extensões whitelisted vs mostrar tudo

Diferente do `dosbox-pure-uwp` (que filtra por extensão porque só interessam formatos que o
emulador consegue carregar), o X-Files é um file browser genérico: **mostra todos os
arquivos**, sem whitelist de extensão. `IsArchive` apenas habilita comportamento extra
(drill-in), não filtra visibilidade.
