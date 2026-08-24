# Deep Sims 0.8.2 Beta — Install

## Requirements

- Erenshor
- Lunaris
- A local Ollama-compatible runtime and model for LLM-backed dialogue

Forgotten Roads Suite Hub, Practice Duel, Campmaster, Nemesis, PvP, Follow, and
Erenshor COOP are optional. Deep Sims must load and work without them.

## Install

1. Close Erenshor.
2. Install Lunaris and launch the game once if Lunaris has not initialized yet.
3. Copy `ErenshorDeepSims.dll` into `<Erenshor>/plugins/`.
4. Ensure there is exactly one active `ErenshorDeepSims.dll` under the live plugin tree.
5. Start Ollama and make the configured model available. The default is `qwen3.5:4b`.
6. Launch Erenshor, enter a character, and run `/aistatus` and `/dsims`.

Do not copy `Lunaris.dll`, `0Harmony.dll`, game assemblies, configs, memory files,
diagnostics, or prompt captures from this package. Lunaris owns its runtime libraries;
Deep Sims creates its own local config and sidecar data.

## Clean-start beta smoke

On first launch, confirm the Lunaris log reports Deep Sims 0.8.2, character scope
reaches ready, and no prompt-capture warning appears. Group with a normal local Sim,
send a social party-chat question, and open the Identity Editor from the Deep Sims UI.

If Ollama or the model is unavailable, use `Templates` mode to isolate the social
layer, then fix the local inference runtime before testing LLM dialogue.

## Uninstall

Remove only `ErenshorDeepSims.dll` to preserve local settings and memory. The included
development `UNINSTALL.ps1` is not part of the public beta ZIP. Deep Sims never stores
its social memory in Erenshor save files.
