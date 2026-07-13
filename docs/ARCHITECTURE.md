# Arquitetura — X-Files

## Visão geral

X-Files é um file browser UWP orientado a gamepad, inspirado na UX de colunas Miller do
`yazi` (preview ao vivo, 3 colunas), mas implementado nativamente em C#/XAML para rodar bem
no Xbox (foco de gamepad nativo do UWP), sem reaproveitar nenhum código do yazi (ver
`DECISIONS.md`, ADR-003).

## Camadas

```
┌─────────────────────────────────────────────────────────────┐
│  XAML Views (MainPage, Controls/ColumnListView, PreviewPane) │  ← binding, templates, foco visual
├─────────────────────────────────────────────────────────────┤
│  ViewModels (ColumnViewModel, PreviewViewModel)               │  ← estado observável, comandos
├─────────────────────────────────────────────────────────────┤
│  Navigation (INavigable, ColumnNavigator)                     │  ← lógica de navegação pura, sem UI
├─────────────────────────────────────────────────────────────┤
│  Input (GamepadInputService)                                  │  ← polling Windows.Gaming.Input, edge-detect
├─────────────────────────────────────────────────────────────┤
│  FileSystem (DirectoryScanner, ArchiveBrowser, FileOperations) │  ← acesso a disco, P/Invoke, SharpCompress
└─────────────────────────────────────────────────────────────┘
```

Cada camada só conhece a camada imediatamente abaixo. `Navigation` não sabe nada de XAML;
`FileSystem` não sabe nada de gamepad. Isso permite testar `ColumnNavigator` e
`DirectoryScanner` sem UI (unit tests puros).

## Fluxo de input → tela

```
Windows.Gaming.Input.Gamepad.GetCurrentReading()
        │  (a cada tick / CompositionTarget.Rendering ou DispatcherTimer)
        ▼
GamepadInputService
  - compara bitmask atual vs anterior → detecta "JustPressed"
  - dpad held → repeat-after-delay (como no dosbox-pure-uwp)
        │  eventos semânticos: DPadUp, DPadDown, Confirm, Back, ContextMenu, PageUp, PageDown
        ▼
INavigable (implementado por ColumnNavigator)
  - OnDPad(bool up)
  - OnConfirm()
  - OnBack()
  - OnContextMenu()
  - OnPageUp() / OnPageDown()
        │  atualiza estado (índice selecionado, pilha de colunas)
        ▼
ColumnViewModel / PreviewViewModel (INotifyPropertyChanged)
        │  data binding (x:Bind)
        ▼
XAML re-renderiza (ItemsControl com ControlTemplate customizado)
```

## Modelo de colunas Miller

3 `ItemsControl` lado a lado em um `Grid` de 3 colunas:

| Coluna | Conteúdo | Largura |
|---|---|---|
| Parent | listagem do diretório pai, com o item "atual" destacado | ~20% |
| Current | listagem do diretório atual, com seleção ativa (foco do gamepad) | ~35% |
| Preview | conteúdo do item selecionado na coluna Current | ~45% |

Ao apertar **A** sobre uma pasta:
1. `Current` vira `Parent` (desliza visualmente para a esquerda — pode ser uma
   `Storyboard` simples de translação, ou apenas troca de conteúdo sem animação no MVP).
2. `Preview` vira `Current`.
3. Nova coluna `Preview` é carregada com o conteúdo do item agora selecionado no novo
   `Current`.

Ao apertar **B** (ou D-pad esquerda): processo inverso.

## Preview ao vivo (sem ação explícita)

Mover a seleção na coluna `Current` dispara imediatamente:
- Pasta → lista os filhos (mesmo componente de listagem, sem interação)
- Arquivo texto → primeiras N linhas / KB (truncado, com indicador "..." se maior)
- Imagem → thumbnail via `BitmapImage` (decodificação assíncrona, cache simples em memória)
- `.zip`/`.7z`/`.rar` → listagem das entradas internas via `ArchiveBrowser` (mesma UI de
  listagem, tratada como "pasta virtual")
- Binário desconhecido → mensagem "sem preview disponível" (sem tentar hex dump no MVP —
  fica em backlog, ver `ROADMAP.md`)

## Por que não D2D (recapitulando ADR-002)

Foco de gamepad (`XYFocusUp/Down/Left/Right`, `IsFocusEngaged`) é nativo do XAML/UWP.
Implementar em D2D significaria recriar manualmente: hit-test, scroll-follow-seleção,
wrap-around, marquee de texto longo — tudo que o `dosbox-pure-uwp` teve que fazer à mão
(ver `Content/FileBrowser.cpp`, ~900 linhas, naquele repo). Com XAML + `ControlTemplate`
customizado, temos o mesmo visual "sem cara de Windows padrão" com uma fração do código.

## Persistência de tema/config

`Theming/AppTheme.cs` carrega um JSON editável (`x-files-theme.json`, salvo em
`ApplicationData.Current.LocalFolder`) via `System.Text.Json` com
`JsonCommentHandling.Skip` (permite comentários `//` no JSON, mesma convenção do
`dosbox-pure-uwp`, mas sem precisar de strip manual). O JSON popula um `ResourceDictionary`
em runtime (brushes/fontes usados pelos `ControlTemplate`).
