# UI e Theming — XAML com ControlTemplate customizado

Ver `DECISIONS.md` (ADR-002) para o porquê de XAML em vez de Win2D.

## Princípio geral

Nenhum controle usa a aparência padrão do Fluent Design (`ListView`/`GridView` "de
fábrica"). Todo controle interativo (linha de arquivo, botão do menu de contexto, etc.) tem
`Style`/`ControlTemplate` próprio, definido em `Theming/RetroTheme.xaml` (um
`ResourceDictionary` mesclado no `App.xaml`).

## Foco de gamepad (nativo do UWP — não reimplementar)

- `IsTabStop="True"` + `UseSystemFocusVisuals="False"` (para trocar o retângulo azul padrão
  por indicador visual próprio via `FocusVisualStyle` ou `VisualStateManager` no estado
  `Focused`/`PointerFocused`).
- `XYFocusUp`/`XYFocusDown`/`XYFocusLeft`/`XYFocusRight` usados para amarrar a navegação
  entre as 3 colunas quando necessário (ex: transição de foco entre `Current` e `Preview`
  ao trocar de "modo" — normalmente cada coluna gerencia seu próprio foco internamente via
  `INavigable`, e `XYFocus` cobre casos onde o usuário usa D-pad físico do Xbox em vez do
  fluxo lógico do `GamepadInputService`. Definir explicitamente para não depender de
  heurística automática do sistema, que pode escolher elemento errado em layouts
  assimétricos).
- `IsFocusEngaged` não usado no MVP (reservado para um modo futuro de "travar foco" dentro
  de um diálogo modal, ex: `FileActionSheet`).

## Estrutura do ResourceDictionary

```
Theming/RetroTheme.xaml
├── Brushes                     (Background, Foreground, Selected, Accent, Border, ...)
├── Typography                  (FontFamily monoespaçada, tamanhos por papel: Title/Item/Meta)
├── Styles
│   ├── ColumnItemStyle         (linha de arquivo/pasta — Normal/PointerOver/Focused/Selected)
│   ├── ColumnHeaderStyle       (cabeçalho de coluna, ex: caminho atual)
│   ├── ContextMenuItemStyle    (linha do FileActionSheet)
│   └── StatusBarStyle          (rodapé com dica de botões — "A: Abrir  B: Voltar  Y: Menu")
```

## Tema editável em runtime (JSON)

`Theming/AppTheme.cs`:
1. Lê `x-files-theme.json` de `ApplicationData.Current.LocalFolder` (cria com defaults se
   não existir, na primeira execução).
2. Parse via `System.Text.Json` com `JsonCommentHandling.Skip` (aceita comentários `//` no
   JSON, sem precisar de strip manual como o `dosbox-pure-uwp` faz com nlohmann/json).
3. Popula os `Brush`/`FontFamily` do `ResourceDictionary` em runtime (via
   `Application.Current.Resources["NomeDoBrush"] = new SolidColorBrush(...)`).

Exemplo do schema JSON (mesma filosofia do `PUREMENU-THEMING.md` do dosbox-pure-uwp,
adaptado):

```jsonc
{
  // Cores em formato #AARRGGBB ou #RRGGBB
  "background": "#0D0D0D",
  "foreground": "#E0E0E0",
  "accent": "#33AA55",
  "selectedBackground": "#1F3D2B",
  "border": "#333333",
  "fontFamily": "Consolas" // trocar por fonte customizada embutida em Assets/Fonts se desejado
}
```

## Fonte customizada (opcional, pós-MVP)

Se quisermos uma identidade visual "retro terminal" como o `dosbox-pure-uwp` (fonte VCR OSD
Mono), embutir `.ttf` em `Assets/Fonts/` e referenciar via
`FontFamily="/Assets/Fonts/NomeDaFonte.ttf#Nome Da Fonte"`. Não faz parte do scaffold
inicial — usar `Consolas`/`Cascadia Mono` (já disponíveis no Windows) como default.

## Layout base (3 colunas)

```xml
<Grid>
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="1*" />   <!-- Parent -->
    <ColumnDefinition Width="1.75*" /> <!-- Current -->
    <ColumnDefinition Width="2.25*" /> <!-- Preview -->
  </Grid.ColumnDefinitions>
  <!-- Controls/ColumnListView x3 (Parent, Current) + Controls/PreviewPane (Preview) -->
</Grid>
```
Proporções ajustáveis; valores acima são ponto de partida razoável (yazi usa algo
parecido — pai menor, atual médio, preview maior).
