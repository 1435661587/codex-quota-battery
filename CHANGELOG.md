# 2.2.2

- Handoff invocation-provided desktop bridge updates across plugin cache versions through a shared per-user file.
- Prevent inherited stale environment paths from overriding refreshed runtime configuration.
- Restart an exited recovery worker from the widget every five seconds; preserve pending state and deduplication across reconnects.
- Keep manual stops excluded and never discover other desktop endpoints.

# 2.2.1

- Fix auto-resume connection loss after Codex updates remove the cached Node runtime. Refresh official runtime files and invocation-provided bridge identity; reconnect when these change.
- Preserve opt-in, explicit quota-failure evidence, manual-stop exclusion, final recheck and persistent deduplication.

# Changelog

## 2.2.0
- Fix missed real quota failures when desktop summaries omit codexErrorInfo.
- Match the official terminal error prefix only, with manual-stop exclusion.
- Add bounded decision events and an automatic-recovery status menu.
- Portable Windows source installer, MIT license and one-prompt setup.
- Public file allowlist, release checks and explicit GitHub sync workflow.

## 2.1.0
- Guarded recovery for observed local tasks interrupted by quota exhaustion.
- Exclude manual stops, normal completion, new turns and duplicate delivery.

## 2.0.0
- Native liquid-glass dual quota batteries, dragging and saved position.
- Official quota reads, local MCP entry and animation toggle.
