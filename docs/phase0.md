# Fase 0 — resultados (2026-11-07)

> Medições do spike de viabilidade que definiu a arquitetura. O código do spike não faz parte do repositório; as conclusões estão em [architecture.md](architecture.md).

Ambiente: Windows 11 Pro 26300, .NET SDK 10.0.403, VS 2026 Build Tools, receptor LIGHTSPEED 046D:C547, G HUB rodando.

## HID++ (hoje `tools/HidppProbe`)
- Mouse: `PRO X Wireless` (slot 1, HID++ 4.2, tipo Mouse).
- Bateria via **0x1004 UNIFIED_BATTERY** (feature index 6): 66% → 65%, ~9 ms por leitura. 0x1001/0x1000 não existem.
- Funciona **com o G HUB aberto** (handle compartilhado, software id 0x0D). Funciona em NativeAOT.
- Obs.: as flags de nível vieram "full" com 65% → usar `state_of_charge`, não as flags.
- Obs.: a coleção curta (FF00:0001) não era pareada no spike por causa do sufixo do instance id — corrigido no Voltray (ver [protocols/logitech-hidpp.md](protocols/logitech-hidpp.md)).

## Tray (NativeAOT, Win32 puro)
| Métrica | Valor |
|---|---|
| exe | 2,1 MB, arquivo único |
| processo → ícone na bandeja | **59 ms** |
| memória | **5 MB privados** / 16 MB working set |
| CPU parado (65 s, 1 refresh) | ~0 ms |

- Ícone registrado por GUID fixo; o Windows criou a entrada em `HKCU\Control Panel\NotifyIconSettings` (IsPromoted vazio = decisão do usuário).
- Trocar o caminho do exe (Debug → publish) com o mesmo GUID funcionou nesta build.
- % no ícone legível em 16/20/24/32 px, temas claro e escuro (hoje `Voltray.exe --icon-preview arquivo.png`).

## WinUI 3 (Windows App SDK 2.6.5, NativeAOT, self-contained)
| Momento | Tempo | Working set | Privado |
|---|---|---|---|
| App iniciado (sem janela) | ~60–150 ms | 27–43 MB | 61–95 MB |
| Primeiro frame da janela | **280–400 ms** | 96 MB | 142 MB |
| Janela aberta +5 s | | 107 MB | 150 MB |
| Janela fechada +6 s | | **108 MB (não libera)** | 150 MB |

- Publicação: 141 MB / 291 arquivos (runtime do WinAppSDK embutido).
- Armadilhas: `dotnet publish -o` não copia `WinUiSpike.pri` (crash 0xC000027B ao procurar estilos); build AOT precisa do `vswhere` no PATH.

## Conclusão
WinUI 3 dentro do processo da bandeja custaria ~100 MB permanentes. Arquitetura recomendada:
**dois processos** — núcleo + bandeja em Win32/NativeAOT (sempre rodando, ~5 MB) e UI WinUI 3 aberta sob demanda, que encerra ao fechar (IPC por named pipe).
