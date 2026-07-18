# Roadmap — Fases de Implementação

Cada fase tem um entregável testável e critérios explícitos de conclusão. Não avançar para
a próxima fase sem fechar os critérios da atual (ou documentar explicitamente por que foi
adiado, em `DECISIONS.md`).

---

## Fase 0 — Scaffold (ESTE COMMIT)

- [x] Estrutura de pastas criada (`Controls/`, `Navigation/`, `FileSystem/`,
      `ContextMenu/`, `Theming/`, `Assets/`).
- [x] Documentação completa em `docs/`.
- [x] `AGENTS.md` na raiz do projeto.
- [x] Projeto UWP C# "cru" (App.xaml, MainPage.xaml placeholder, csproj, manifest com
      capabilities e TargetDeviceFamily corretos) — sem lógica de negócio.
      **Não buildado/validado** (feito em ambiente Linux) — validação real de build/SDK
      acontece na Fase 1, na primeira abertura em máquina Windows.

**Critério de conclusão**: projeto abre no Visual Studio (Windows) sem erros de estrutura,
mesmo que não tenha sido buildado ainda (build real só é possível em Windows).

---

## Fase 1 — Esqueleto + Deploy Xbox validado

- [x] Abrir o projeto em uma máquina Windows com Visual Studio, resolver eventuais ajustes
      de `csproj`/versões de SDK que não deu para validar em ambiente Linux.
- [x] Build local (desktop) funcional — `MainPage` mostra um placeholder simples (ex: texto
      "X-Files" centralizado).
- [x] Ativar Developer Mode no Xbox (ver `docs/DEPLOY-XBOX.md`).
- [x] Deploy do "hello world" no Xbox via Visual Studio (Remote Machine) ou Device Portal.

**Critério de conclusão**: app abre no Xbox real, tela aparece, sem crash. Nenhuma feature
ainda — só validação de pipeline de build/deploy.

---

## Fase 2 — GamepadInputService + contrato INavigable

- [x] Implementar `GamepadInputService` (polling, edge-detection, dpad-repeat).
- [x] Implementar `INavigable` (interface) + uma implementação "mock" (ex: um contador
      simples na tela reagindo a D-pad/A/B) para validar o pipeline de input sem UI real de
      arquivos ainda.
- [x] Testes unitários (ou manuais documentados) para: edge-detection (JustPressed correto
      mesmo segurando botão), wrap-around, deadzone do analógico.
      `docs/PHASE2-TESTS.md` — 8 cenários documentados (edge, hold-repeat, direction
      change, stick deadzone, buttons, phantom inputs, disconnect/reconnect, simultâneo).

**Critério de conclusão**: no Xbox real, mover D-pad/analógico incrementa/decrementa um
contador na tela, com repeat funcionando ao segurar o botão, sem input fantasma.
**Status**: código implementado + testes manuais documentados. Validação em hardware
pendente ( executar `docs/PHASE2-TESTS.md` no Xbox).

---

## Fase 3 — DirectoryScanner + coluna única funcional

- [x] `FileEntry` model.
- [x] `DirectoryScanner` com P/Invoke (`FindFirstFileExFromAppW` + `GetLogicalDrives`).
- [x] Uma única coluna (`Controls/ColumnListView`) navegável por gamepad, listando drives
      na raiz e navegando para dentro de pastas reais (sem preview, sem colunas
      parent/preview ainda).
- [x] Ordenação (pastas antes de arquivos, alfabética) implementada e visualmente
      confirmada.

**Critério de conclusão**: no Xbox real, navega por qualquer pasta de um drive USB
conectado, entra/sai de subpastas com D-pad/A/B, sem crash em pastas vazias ou sem
permissão.
**Status**: implementado e validado no Xbox real. Loading indicator adicionado para
latência de USB spin-up. XrayLib adaptada para UWP (removido Console sink).

---

## Fase 4 — 3 colunas Miller + transições

- [x] `ColumnNavigator` implementando `INavigable`, controlando estado de 3 colunas
      (Parent/Current/Preview como conceito — Preview ainda mostra apenas listagem de
      pasta nesta fase, sem texto/imagem).
- [x] Layout XAML com 3 `Grid.ColumnDefinition` e binding reativo.
- [x] Transição ao entrar/sair de pasta (troca de conteúdo das 3 colunas, com ou sem
      animação simples).
- [x] GamepadInputService simplificado: D-pad Up/Down gerenciado nativamente pelo
      ListView; GamepadInputService só gerencia botões de ação (A/B/Y/LB/RB/LT/RT)
      e left stick.

**Critério de conclusão**: navegação completa por pastas reais usando as 3 colunas, preview
da coluna à direita sempre mostra o conteúdo da pasta/arquivo selecionado na coluna do
meio, sem esperar confirmação.
**Status**: implementado e validado no Xbox real. Double-fire bug resolvido delegando
navegação Up/Down ao ListView nativo. RetroListView sobrescreve OnKeyDown para bloquear
PageUp/PageDown nativos.

---

## Fase 5 — PreviewPane (texto e imagem)

- [ ] `DataTemplateSelector` para escolher entre `FolderPreviewTemplate` (já existe da Fase
      4), `TextPreviewTemplate`, `ImagePreviewTemplate`, `UnsupportedPreviewTemplate`.
- [ ] Leitura truncada de arquivos texto (limite de KB configurável).
- [ ] Carregamento assíncrono de imagem (não bloquear navegação enquanto carrega).
- [ ] Estado de erro amigável para arquivos ilegíveis/permissão negada.

**Critério de conclusão**: navegar sobre um `.txt` mostra conteúdo truncado; navegar sobre
uma imagem comum mostra thumbnail; navegar rapidamente entre vários arquivos não trava a
UI nem gera exceções não tratadas.

---

## Fase 6 — ArchiveBrowser (zip/7z/rar)

- [ ] Integrar `SharpCompress`.
- [ ] `IArchiveBrowser` implementado, detecção de `IsArchive` no `DirectoryScanner`.
- [ ] Drill-in em arquivo compactado tratado como pasta (reaproveitando `ColumnNavigator`).
- [ ] Preview de entradas texto/imagem dentro do arquivo (via `OpenEntryStream`).
- [ ] Validar performance em arquivos grandes (> 100MB) — decidir se cache/streaming
      precisa de ajuste (documentar decisão em `DECISIONS.md` se motor precisar trocar).

**Critério de conclusão**: abrir um `.zip`, `.7z` e `.rar` de teste, navegar pelas entradas
internas, preview funcionando para pelo menos um arquivo texto e uma imagem dentro de cada
formato.

---

## Fase 7 — FileActionSheet + FileOperations

- [ ] `FileActionSheet` (menu de contexto acionado por Y), estilizado conforme
      `docs/UI-THEMING.md`.
- [ ] Ações: Abrir com (`Launcher.LaunchFileAsync`), Copiar, Mover, Renomear, Deletar,
      Extrair.
- [ ] Fluxo de "escolher pasta destino" para Copiar/Mover/Extrair reaproveitando navegação
      de coluna (modo especial, ver `FILEBROWSER.md`).
- [ ] Confirmação obrigatória antes de Deletar (diálogo navegável por gamepad).

**Critério de conclusão**: todas as ações funcionam de ponta a ponta no Xbox real, sem
necessidade de teclado/mouse, incluindo escolha de pasta destino.

---

## Fase 8 — Tema/Polish

- [ ] `Theming/AppTheme.cs` lendo/gravando `x-files-theme.json`.
- [ ] `RetroTheme.xaml` com todos os `Style`/`ControlTemplate` finalizados (sem chrome
      padrão do Windows visível em nenhum lugar).
- [ ] Estado vazio (sem controle conectado, pasta vazia, etc.) com mensagens/visual
      tratados.
- [ ] Passada de UX: animações leves de transição de coluna, feedback visual de
      loading/erro consistente.

**Critério de conclusão**: critérios de "pronto" do MVP em `docs/SPEC.md` totalmente
atendidos.

---

## Assets & Ícones

Processo de assets documentado em `docs/ASSETS-GUIDE.md`. Skill disponível em
`.opencode/skills/assets-icons/SKILL.md`. Resumo:

- Ícones PNG sempre, source: `F:\workspace\icons8-personal-set`
- Naming: `{viewname}-{descriptor}-{size}.png` (lowercase, hifens)
- Organização: `XFiles/Assets/Views/{ViewName}/` por view
- Referência XAML: `ms-appx:///Assets/Views/{ViewName}/{filename}`
- Registro obrigatório no `XFiles.csproj` como `<Content>`

Cada fase que introduce nova view deve incluir seus ícones nessa fase.

---

## Backlog pós-MVP (não planejado em fases ainda)

- Navegação de rede (SMB/UNC).
- Preview de hex dump para binários.
- Edição de texto simples.
- Múltiplos usuários/gamepads simultâneos.
- Zips aninhados profundos com streaming real (sem `MemoryStream` intermediário).
- Suporte a arquivos protegidos por senha.
- Localização (i18n) — hoje docs/specs em português, mas UI pode nascer em inglês ou
  suportar ambos — decisão a tomar quando chegar a hora.
