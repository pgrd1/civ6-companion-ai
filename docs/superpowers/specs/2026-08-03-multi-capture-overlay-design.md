# Multi-capture overlay design

## Goal

Make the companion comfortable to use without repeatedly leaving Civilization VI: F7 queues screenshots, F8 analyzes every queued screenshot plus the current screen, and the overlay keeps a stable layout while chat works with Enter.

## Design

- Register F7 as a second global hotkey. Each press captures the foreground Civilization VI client area without invoking Codex and reports `화면 저장됨 (N/6)`.
- Keep at most six queued original PNG files in chronological order. If a seventh is added, dispose the oldest capture.
- F8 captures the current screen and sends queued paths followed by the current path as repeated Codex `--image` arguments. Clear queued captures only after a valid analysis response; preserve them after errors so the user can retry.
- The analysis prompt explicitly says that the images are chronological and must be combined into one recommendation.
- Use a fixed-size overlay. Only the recommendation body scrolls. The chat composer stays at the bottom with a fixed-height input and a visible Send button.
- Enter sends; Shift+Enter inserts a line break. Handle the key before the multiline TextBox consumes it.

## Error handling and limits

- F7 uses the same Civilization foreground-window validation and capture fallback as F8.
- Queue mutation is serialized with analysis and chat operations.
- Queued temporary files are disposed on successful analysis, eviction, new game, or application shutdown.
- The six-image limit bounds disk usage, Codex input size, cost, and latency.

## Verification

- Unit tests cover queue ordering, six-image eviction, preservation on failure, clearing on success, repeated `--image` arguments, F7 wiring, stable XAML sizing, and Enter/Shift+Enter behavior.
- Run the complete solution test suite and build a fresh executable before restart.
