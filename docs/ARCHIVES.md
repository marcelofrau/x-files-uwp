# Arquivos Compactados — zip / 7z / rar

## Biblioteca

**SharpCompress** (NuGet), única dependência para os 3 formatos. Ver `DECISIONS.md`
(ADR-004) para justificativa.

Fallback opcional: `System.IO.Compression.ZipFile` (nativo .NET) para zip puro, caso
`SharpCompress` tenha overhead perceptível em arquivos zip grandes — decisão de
performance a validar na Fase 6 do roadmap, não bloqueante para o MVP.

## `ArchiveBrowser` — arquivo como pasta virtual

Interface conceitual (mesmo contrato de listagem que `DirectoryScanner`, para reuso de UI):

```csharp
public interface IArchiveBrowser
{
    // Lista entradas no "diretório" internalPath dentro do arquivo em archivePath.
    // internalPath == "" lista a raiz do arquivo.
    IReadOnlyList<FileEntry> ListEntries(string archivePath, string internalPath);

    // Abre um stream de leitura para uma entrada específica (usado por Preview/Extração).
    Stream OpenEntryStream(string archivePath, string internalEntryPath);
}
```

- Detecção de formato por assinatura de arquivo (magic bytes) além da extensão, para evitar
  falso-negativo em arquivos renomeados — `SharpCompress.Common.ArchiveFactory` já faz essa
  detecção automaticamente ao abrir (`ArchiveFactory.Open(stream)`), preferir essa API a
  `ZipArchive`/`SevenZipArchive` diretos quando possível.
- Entradas listadas viram `FileEntry` com `ArchiveRootPath` + `ArchiveInternalPath`
  preenchidos, e `IsDirectory` derivado da própria estrutura de pastas dentro do arquivo
  (SharpCompress expõe `IsDirectory` por entry).
- Navegação para dentro de um `.zip` dentro de outro `.zip` (aninhado): melhor esforço —
  abre o stream da entrada interna em um `MemoryStream` temporário e repete o processo.
  Puxar para o backlog se performance/memória for problema em arquivos grandes (documentar
  limite prático, ex: só aninha se o zip interno for < 50MB).

## Extração

Ação explícita via `FileActionSheet` → "Extrair":
1. Usuário escolhe pasta destino (reaproveitando navegação em "modo seleção de destino",
   ver `FILEBROWSER.md`).
2. `SharpCompress` extrai todas as entradas (ou apenas a entrada/pasta selecionada, se o
   usuário estiver navegando dentro do arquivo no momento da ação — extração parcial é
   suportada por entry).
3. Progresso reportado via `IProgress<double>`, exibido na UI (barra simples, sem bloquear
   navegação de outras colunas).

## Preview de conteúdo dentro de arquivo compactado

Mesma lógica de preview de arquivo normal (`ARCHITECTURE.md` → "Preview ao vivo"), mas a
leitura do conteúdo passa por `IArchiveBrowser.OpenEntryStream` em vez de `File.OpenRead`.
Texto/imagem dentro de zip/7z/rar deve funcionar sem extrair para disco primeiro.

## Limitações conhecidas (documentadas, não bugs)

- `.rar`: leitura apenas (SharpCompress não escreve rar) — extração funciona, criação não é
  objetivo do app.
- Arquivos protegidos por senha: fora do MVP; se detectado (`SharpCompress` lança exceção
  ao tentar ler entry), exibir mensagem clara na coluna Preview ("Arquivo protegido por
  senha — não suportado"), nunca crashar.
- Arquivos multi-volume (`.7z.001`, `.part1.rar`): fora do MVP, mesmo tratamento de erro
  amigável.
