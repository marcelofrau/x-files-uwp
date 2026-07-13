# Gamepad — Mapeamento e Contrato de Navegação

## Fonte de input

`Windows.Gaming.Input.Gamepad` (API nativa UWP, sem SDL — diferente do `dosbox-pure-uwp`,
que usa SDL_GameController + fallback UWP porque também roda em plataformas não-UWP via o
core libretro compartilhado). Aqui não há esse requisito multiplataforma, então usamos
direto a API nativa, mais simples:

```csharp
var gamepads = Gamepad.Gamepads;        // IReadOnlyList<Gamepad>
var reading = gamepad.GetCurrentReading(); // GamepadReading { Buttons, LeftThumbstickX/Y, ... }
```

Eventos `Gamepad.GamepadAdded` / `Gamepad.GamepadRemoved` tratam hotplug (controle
conectado/desconectado em runtime).

## GamepadInputService — responsabilidades

1. Poll a cada frame/tick (via `CompositionTarget.Rendering` ou `DispatcherTimer` de ~16ms).
2. Comparar `GamepadButtons` atual vs anterior (bitwise) → detectar "JustPressed" (borda de
   subida) e "JustReleased".
3. D-pad: repeat-enquanto-segurado, com delay inicial (ex: 400ms) e depois repeat rápido
   (ex: 100ms) — mesma lógica de `dosbox_uwpMain.cpp` do projeto irmão.
4. Left Thumbstick: mapeado para os mesmos eventos de D-pad quando além de um deadzone
   (~0.5), permitindo navegar com o analógico também.
5. Traduzir estado bruto em eventos semânticos e repassar para o `INavigable` ativo (o
   `ColumnNavigator`, ver `ARCHITECTURE.md`).

## Contrato `INavigable`

```csharp
public interface INavigable
{
    void OnDPad(bool up);       // true = up/left (anterior), false = down/right (próximo)
    void OnDPadLeft();          // sobe nível (equivalente a Back)
    void OnDPadRight();         // desce nível (equivalente a Confirm em pasta)
    void OnConfirm();           // botão A
    void OnBack();              // botão B
    void OnContextMenu();       // botão Y
    void OnPageUp();            // LB
    void OnPageDown();          // RB
}
```

Mesma "forma" usada por `FrontendMenu`/`FileBrowser` no `dosbox-pure-uwp` (ver relatório de
exploração em `docs/frontend`/`docs/filebrowser` daquele repo) — decisão intencional de
manter o padrão testado.

## Tabela de botões (MVP)

| Botão físico | Evento semântico | Ação no X-Files |
|---|---|---|
| D-pad Up / Left Stick Up | `OnDPad(up: true)` | mover seleção para cima na coluna Current (wrap-around) |
| D-pad Down / Left Stick Down | `OnDPad(up: false)` | mover seleção para baixo na coluna Current (wrap-around) |
| D-pad Left / Left Stick Left | `OnDPadLeft()` | subir um nível (equivalente a B) |
| D-pad Right / Left Stick Right | `OnDPadRight()` | entrar na pasta selecionada (equivalente a A em pasta) |
| A | `OnConfirm()` | pasta → drill-in; arquivo → ação padrão contextual (ex: abrir com app associado) |
| B | `OnBack()` | subir um nível; se já na raiz, sem efeito (ou sai do app, a definir) |
| Y | `OnContextMenu()` | abre `FileActionSheet` sobre item selecionado |
| X | (reservado) | alternar modo de preview (ex: forçar hex) — pós-MVP |
| LB | `OnPageUp()` | rolar uma página para cima na coluna Current |
| RB | `OnPageDown()` | rolar uma página para baixo na coluna Current |
| Start/Menu | (reservado) | abrir configurações/tema — pós-MVP |

## Regras de navegação (portadas do FileBrowser.cpp do dosbox-pure-uwp)

- **Wrap-around**: mover para baixo no último item volta para o primeiro; mover para cima
  no primeiro item vai para o último.
- **Scroll-follow-seleção**: se o índice selecionado sair da janela visível (para cima ou
  para baixo), a lista rola automaticamente para mantê-lo visível, com uma margem de
  "olhar à frente" (ex: 2-3 itens antes de rolar no limite).
- **Skip de entradas vazias/separadoras**: se no futuro existirem separadores visuais na
  lista (ex: cabeçalho "Pastas" / "Arquivos"), a navegação deve pulá-los automaticamente
  (mesma lógica do `do { } while` visto em `FileBrowser.cpp:706-733`).

## Edge cases de input

- Sem controle conectado: exibir mensagem de estado vazio ("Conecte um controle") em vez de
  crashar — `GamepadInputService` deve expor `IsControllerConnected` observável.
- Múltiplos controles conectados: MVP usa apenas `Gamepad.Gamepads[0]` (o primeiro
  detectado). Suporte a múltiplos usuários fica no backlog.
- Debounce: threshold de deadzone do analógico (0.5) evita "chattering" de navegação
  indesejada por drift do stick.
