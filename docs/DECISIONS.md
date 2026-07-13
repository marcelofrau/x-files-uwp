# Decisões de Arquitetura (ADRs curtos)

Registro das decisões tomadas antes de qualquer linha de código, para não precisarmos
re-discutir os mesmos trade-offs no futuro.

---

## ADR-001: C# + UWP, não C++/CX

**Contexto**: o projeto irmão `dosbox-pure-uwp` é C++/CX porque hospeda um core libretro em C
legado e precisa de compatibilidade binária estreita com DirectX. O X-Files não tem esse
requisito — é um app novo, sem core nativo a integrar.

**Decisão**: C# + UWP puro (sem CX, sem C++/WinRT).

**Motivo**: acesso direto a `System.IO.Compression`, `SharpCompress`, LINQ, async/await,
produtividade muito maior para CRUD de arquivos e para UI. Não há motivo técnico para pagar o
custo de C++ aqui.

---

## ADR-002: XAML com `ControlTemplate` customizado, não Win2D/D2D

**Contexto**: o `dosbox-pure-uwp` implementa toda a UI (menu, file browser, diálogos) em
Direct2D imperativo porque é C++/CX e precisa evitar XAML por razões de compatibilidade com
código C legado. Cogitamos replicar essa abordagem para ter visual 100% customizado
(estilo "terminal retro", como o `FileBrowser.cpp` daquele projeto).

**Decisão**: XAML nativo, com `ControlTemplate`/`ItemContainerStyle`/`VisualStateManager`
totalmente redesenhados (sem chrome padrão do Fluent Design).

**Motivo**:
- UWP dá foco de gamepad **de graça** via `XYFocusUp/Down/Left/Right` e `IsFocusEngaged`.
  Replicar isso em D2D significa reimplementar hit-test, foco, scroll e wrap-around à mão
  (o `FileBrowser.cpp` do dosbox-pure-uwp tem ~900 linhas só para isso).
- Um `ControlTemplate` customizado consegue visual idêntico ao D2D (cores, fontes
  monoespaçadas, sem bordas/chrome do Windows) sem abrir mão do foco nativo.
- Menos código = menos superfície de bugs num app cujo valor está na navegação e não no
  motor de renderização.

**Trade-off aceito**: perdemos controle pixel-perfect de baixo nível (ex: efeitos de partícula,
shaders customizados) que o D2D daria. Não é necessário para um file browser.

---

## ADR-003: Inspiração em UX do `yazi`, sem reaproveitar código/core dele

**Contexto**: o usuário queria uma experiência parecida com o `yazi` (file manager em Rust,
terminal, colunas Miller, preview ao vivo).

**Decisão**: reimplementar o **conceito** de colunas Miller (Parent | Current | Preview) e
preview ao vivo em C#/XAML, sem qualquer dependência do código-fonte do yazi (que é Rust,
orientado a terminal, com sistema de plugins Lua — tecnologia incompatível com UWP/Xbox).

**Motivo do nome do projeto**: descartamos nomes como "yazi-uwp" ou similares para não criar
expectativa de ser um port real. O nome escolhido é **X-Files** (repo: `x-files-uwp`) —
referência geek à série, sem vínculo com nenhuma lib/nome existente.

---

## ADR-004: SharpCompress para zip/7z/rar

**Contexto**: precisamos navegar (listar entradas, "entrar" como se fosse pasta) dentro de
arquivos `.zip`, `.7z` e `.rar` sem necessariamente extrair tudo.

**Decisão**: usar `SharpCompress` (NuGet) como biblioteca única para os 3 formatos, com
fallback opcional para `System.IO.Compression.ZipFile` (nativo do .NET) em zip puro, se
performance for um problema.

**Motivo**: uma única API cobre os 3 formatos, evitando P/Invoke nativo extra (ex:
`7z.dll`/`SevenZipSharp`, que dependem de binário nativo por plataforma — problemático em
UWP/Xbox por causa do sandbox e da arquitetura ARM/x64).

**Risco conhecido**: `SharpCompress` tem suporte a `.rar` **somente leitura** (não cria/edita
rar) — aceitável, já que o app é um *browser*, não um compactador.

---

## ADR-005: Sem navegação de rede (SMB/UNC) no MVP

**Contexto**: o `dosbox-pure-uwp` não implementa isso; depende apenas de drives mapeados
aparecerem via `GetLogicalDrives()`.

**Decisão**: MVP cobre apenas drives locais/USB conectados ao Xbox e o sandbox
`LocalFolder`/`broadFileSystemAccess`. Navegação explícita de `\\servidor\share` (com
descoberta, autenticação, etc.) fica fora de escopo, documentada como trabalho futuro em
`ROADMAP.md`.

**Motivo**: escopo de MVP enxuto; SMB no UWP sandbox do Xbox tem restrições adicionais (sem
`Windows.Networking.Sockets` de baixo nível fácil, precisa de `capabilities` extras) que
merecem investigação própria antes de comprometer prazo.

---

## ADR-006: Ação do botão A é contextual, preview é ao vivo (sem esperar confirmação)

**Contexto**: no `yazi`, mover a seleção já atualiza a preview automaticamente. Queríamos
manter essa fluidez, mas também precisávamos de um menu de ações (copiar, mover, extrair,
abrir com).

**Decisão**:
- Mover seleção (D-pad/stick) **sempre** atualiza a coluna de preview automaticamente —
  nenhuma confirmação necessária.
- **A** em uma pasta = entra nela (drill-in, shift de colunas).
- **A** em um arquivo = ação padrão contextual (definida por tipo de arquivo — abrir com app
  associado, por exemplo), configurável futuramente.
- **Y** abre explicitamente o `FileActionSheet` (menu de contexto: abrir com, extrair, copiar,
  mover, renomear, deletar) — usuário não precisa "confirmar" para só *ver* o preview.

**Motivo**: separa claramente "olhar" (preview automático, sem custo) de "agir" (menu de
contexto explícito), evitando ações destrutivas acidentais com o botão mais usado (A).
