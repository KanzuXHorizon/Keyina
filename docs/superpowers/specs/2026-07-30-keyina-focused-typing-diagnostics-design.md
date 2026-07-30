# Focused Typing Diagnostics Design

## Goal

Add a local diagnostic sandbox to the **Chẩn đoán** page so incorrect Vietnamese composition, misplaced tones, duplicated Telex keys, and injection loops can be reproduced with enough evidence to identify the failing stage.

## Scope

- Capture data only while the dedicated diagnostic text box owns keyboard focus.
- Never capture input from another Keyina control or another application.
- Keep the trace in memory for the current settings-window lifetime.
- Persist data only after an explicit **Xuất log** action.
- Clear all raw diagnostic content when the settings window closes.
- Preserve the existing content-free `TypingTraceBuffer` and latency telemetry contracts.

## Architecture

### Target-scoped runtime trace

Introduce `TypingDiagnosticTrace`, a bounded in-memory trace owned by `Keyina.Host.Windows`. The UI activates it with the diagnostic text box HWND. Every runtime record must include the current focused HWND and is accepted only when it exactly matches the active target.

The resident keyboard hook records:

- physical key down and key up;
- virtual key, scan code, character, modifier state, injected state, and repeated-key-down classification;
- engine decisions such as bypass, literal pass-through, transform, backspace count, inserted text, and injection failure.

When inactive, the hook performs only one cheap enabled-state check and records nothing.

### UI event trace

The diagnostic text box records its own `KeyDown`, `KeyPress`, `KeyUp`, and `TextChanged` observations into the same trace. Output records include the visible text and caret/selection state so the runtime stages can be correlated with the result rendered by WinForms.

### Diagnostics UI

Add a **Sandbox debug bộ gõ** card to the Diagnostics page containing:

- a multiline input named `typingDiagnosticInput`;
- a visible recording state that is active only while that input has focus;
- a read-only multiline log named `typingDiagnosticLog`;
- a filter for all, physical, engine, output, and anomaly entries;
- **Xóa log**, **Sao chép log**, and **Xuất log** actions.

The log refreshes on a lightweight UI timer only while the settings form is alive. Moving focus to the action buttons pauses capture but retains the existing trace for review and export.

## Privacy and safety

- Exact keys and text are allowed only in this explicitly labelled sandbox.
- `TypingDiagnosticTrace` rejects records whose focused HWND differs from the active target.
- The existing diagnostics report, general typing trace, clipboard logic, speech logic, and telemetry remain content-free.
- Capacity is bounded; old entries are overwritten.
- Closing the form disables and clears the trace.

## Error handling

- Clipboard copy failures use the existing safe UI error path.
- Export uses a user-selected absolute `.log` path and UTF-8 without BOM.
- Hook instrumentation must fail open and never change whether a physical key is consumed.
- Formatting failures must not escape the hook callback.

## Testing

- Unit tests prove inactive and wrong-HWND records are rejected.
- Unit tests prove exact physical-key metadata and repeated-key-down anomalies are captured only for the target.
- Unit tests prove clearing/disabling removes sensitive data.
- Settings tests prove the card, controls, privacy copy, focus activation, pause behavior, clear action, and filtered log rendering exist.
- Run the complete host test runner and Release build after implementation.
