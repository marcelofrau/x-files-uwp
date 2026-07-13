# Deploy no Xbox — Guia de Referência

Baseado no mesmo fluxo usado/documentado no projeto irmão `dosbox-pure-uwp` (ver
`README.md` e `AGENTS.md` daquele repo para detalhes específicos já validados).

## 1. Pré-requisitos

- Console Xbox com **Developer Mode** ativado (via app "Dev Home" da Microsoft Store,
  requer conta de desenvolvedor registrada).
- Máquina Windows com Visual Studio 2022 (workload "Universal Windows Platform
  development") — **este projeto não builda em Linux/WSL**, apenas a estrutura/docs são
  criadas aqui; a compilação e o deploy real precisam ser feitos em uma máquina Windows.
- Xbox e máquina de desenvolvimento na mesma rede local.

## 2. Habilitar Developer Mode no console

1. Instalar o app "Dev Home" (Microsoft Store) no Xbox em modo Retail normal.
2. Seguir o fluxo de registro (requer conta de desenvolvedor Microsoft — gratuita ou paga
   dependendo do momento/região).
3. Console reinicia em Developer Mode; um app "Dev Home" mostra o **endereço IP** e um
   **código de emparelhamento** para o Device Portal.

## 3. Device Portal

1. No navegador da máquina Windows: `https://<IP-DO-XBOX>:11443`.
2. Autenticar com o código de emparelhamento mostrado no "Dev Home".
3. Menu **Apps** → permite instalar pacotes `.appx`/`.msix` (ou `.appxbundle`) diretamente
   pela interface web, ou via Visual Studio (mais prático durante desenvolvimento).

## 4. Deploy via Visual Studio (recomendado durante desenvolvimento)

1. Abrir `XFiles.sln` no Visual Studio (Windows).
2. Selecionar plataforma `x64` (ou `ARM`/`ARM64` dependendo do console-alvo — Xbox usa
   arquitetura interna específica; consultar a documentação atual do Xbox Developer Mode
   para o valor correto no momento do build, isso muda entre gerações de console).
3. No dropdown de dispositivo de deploy, escolher **Remote Machine**, inserir IP do Xbox
   (Developer Mode expõe uma porta de depuração remota separada da porta do Device Portal).
4. F5 (Debug) ou Ctrl+F5 (Run sem debug) — Visual Studio empacota, copia e instala
   automaticamente.

## 5. Deploy via Device Portal (para builds "release", sem Visual Studio)

1. Gerar pacote via **Project → Publish → Create App Packages** no Visual Studio,
   selecionando "Sideloading" (não "Microsoft Store").
2. No Device Portal do Xbox → Apps → **Add** → selecionar o `.appxbundle`/`.msix` gerado +
   arquivo de certificado (`.cer`) se necessário.
3. Instalar e executar a partir da lista de apps do Device Portal ou do próprio dashboard
   do Xbox (Developer Mode expõe um menu separado "Dev Mode Home" com os apps sideloaded).

## 6. Capabilities obrigatórias no manifest

```xml
<Capabilities>
  <rescap:Capability Name="broadFileSystemAccess" />
  <rescap:Capability Name="runFullTrust" />
</Capabilities>
```

Sem essas duas, `FindFirstFileExFromAppW`/`GetLogicalDrives` (ver `FILEBROWSER.md`) falham
silenciosamente ou retornam acesso negado para qualquer caminho fora do sandbox da app.
Precisam do namespace `rescap` declarado no `Package.appxmanifest`
(`xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"`)
e da declaração correspondente em `<Dependencies>`/`TargetDeviceFamily`.

## 7. `TargetDeviceFamily`

```xml
<Dependencies>
  <TargetDeviceFamily Name="Windows.Xbox" MinVersion="10.0.0.0" MaxVersionTested="10.0.0.0" />
</Dependencies>
```

Ajustar `MinVersion`/`MaxVersionTested` para os valores reais do SDK instalado no momento
do build (Visual Studio preenche automaticamente ao trocar o `TargetDeviceFamily` no
projeto).

## 8. Checklist de troubleshooting comum

- [ ] App não aparece no Device Portal → verificar se o certificado de assinatura do
      pacote é confiável no dispositivo (self-signed precisa ser instalado manualmente via
      Device Portal → Certificates antes do primeiro deploy).
- [ ] `Access Denied` ao listar drives → confirmar as duas capabilities acima e que o
      Developer Mode realmente está ativo (não apenas "Retail com sideload", que tem
      restrições diferentes).
- [ ] Gamepad não detectado → confirmar que o app está em primeiro plano (Xbox só entrega
      input de gamepad para o app com foco) e que `Gamepad.GamepadAdded` foi assinado antes
      da enumeração inicial (race condition comum: controle já conectado antes do app
      iniciar não dispara `GamepadAdded` — precisa também iterar `Gamepad.Gamepads` no
      startup).
