# Especificação Funcional — X-Files

## Objetivo

File browser para Xbox (UWP), 100% operável via gamepad, com navegação por colunas Miller
(estilo yazi) e preview ao vivo, incluindo navegação dentro de arquivos compactados
(zip/7z/rar) sem precisar extrair antes.

## Escopo do MVP

### Incluído
1. Navegação de diretórios locais e drives conectados (USB, drives do Xbox visíveis via
   `GetLogicalDrives`).
2. Listagem em 3 colunas (Parent | Current | Preview), com preview ao vivo ao mover seleção.
3. Preview de:
   - Texto (truncado)
   - Imagem (thumbnail)
   - Pastas (listagem)
   - Arquivos `.zip`, `.7z`, `.rar` (listagem interna, tratados como pasta virtual)
4. Navegação **para dentro** de arquivos compactados (drill-in como se fosse pasta),
   incluindo múltiplos níveis (zip dentro de zip, se existir — melhor esforço).
5. Menu de contexto (botão Y) com ações:
   - Abrir com app associado (`Launcher.LaunchFileAsync`)
   - Extrair (arquivo compactado → pasta de destino escolhida)
   - Copiar / Mover / Renomear / Deletar
6. 100% navegável por gamepad (`Windows.Gaming.Input.Gamepad`), sem depender de mouse/teclado
   (mas sem quebrá-los — devem funcionar como bônus, não como requisito).
7. Tema visual customizado (não usa chrome padrão do Fluent Design), configurável via JSON.
8. Deploy funcional no Xbox via Developer Mode + Device Portal (sideload `.appx`/`.msix`).

### Fora de escopo (MVP) — backlog documentado em ROADMAP.md
- Navegação de rede (SMB/UNC, `\\servidor\share`)
- Preview de binário/hex dump
- Edição de arquivos de texto
- Múltiplas abas/painéis simultâneos
- Compressão (criar novos zips) — só leitura/extração
- Suporte a `.rar` com senha ou multi-volume complexo (aceita o que `SharpCompress` suportar
  nativamente, sem features extras)
- Sincronização em nuvem (OneDrive, etc.)

## Requisitos não funcionais

- **Responsividade**: navegação (mover seleção, trocar coluna) deve responder em < 100ms
  percebido, mesmo com preview de imagem/arquivo grande carregando em background
  (async, não bloqueia o input thread).
- **Robustez de I/O**: diretórios sem permissão, drives desconectados durante navegação,
  arquivos corrompidos — devem falhar com mensagem visível na coluna Preview, nunca crashar
  o app.
- **Compatibilidade Xbox**: `TargetDeviceFamily Name="Windows.Xbox"`, testado via Developer
  Mode real (não apenas emulador/desktop).
- **Sem dependência de mouse/teclado**: todo fluxo (incluindo diálogos de confirmação,
  seleção de pasta destino em "Mover/Copiar") deve ter alternativa 100% gamepad.

## Personas / uso esperado

Usuário final: dono de Xbox com Developer Mode ativo, quer navegar pendrive/HD externo ou
pasta local do app para organizar ROMs, ISOs, backups de save, etc., sem precisar de
teclado/mouse conectado — controle apenas.

## Critério de "pronto" do MVP

- [ ] Roda no Xbox real via sideload, sem input de teclado/mouse necessário em nenhum
      fluxo.
- [ ] Navega para dentro de pelo menos um arquivo de cada formato (zip/7z/rar) e mostra
      listagem correta.
- [ ] Copiar, mover, renomear, deletar e extrair funcionam sem erros em cenários normais.
- [ ] Preview de texto e imagem funcionam para os formatos mais comuns
      (`.txt`/`.log`/`.md`, `.png`/`.jpg`/`.bmp`).
- [ ] Tema pode ser trocado editando o JSON, sem recompilar.
