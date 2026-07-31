# Keyina Manual Dictation Finalization and Translation Reader Design

## Scope

This change completes two existing user flows without changing their shortcuts or providers:

1. `Ctrl + Alt + V` starts one continuous Speechmatics dictation session and the second press stops it, flushes the provider, and inserts exactly one combined transcript.
2. `Ctrl + Alt + T` keeps its preview flow but presents the translated text as a clean, resizable reader that remains usable for long content.

## Speechmatics behavior

Speechmatics `AddTranscript` messages are immutable final fragments, not proof that the user has ended the whole dictation session. Keyina must therefore keep receiving audio and aggregate every distinct final fragment while the session remains active. It must not write any transcript into the focused application until the user presses `Ctrl + Alt + V` again.

The second toggle stops microphone capture, sends `EndOfStream`, waits for every final fragment and `EndOfTranscript`, and then writes one IPC envelope containing the complete transcript. Duplicate provider fragments remain deduplicated. Cancelling or provider failure emits no transcript.

The Vietnamese production configuration is explicit:

- `model`: `enhanced`
- `max_delay`: `2.0`
- `max_delay_mode`: `flexible`
- `enable_partials`: `true`
- `conversation_config.end_of_utterance_silence_trigger`: `0`
- raw mono PCM S16LE at 16 kHz

The explicit zero silence trigger disables provider turn detection. Partials remain overlay-only and provide live feedback while finals accumulate privately.

## Finalization ordering

`EndOfTranscript` is forwarded through the session event queue after all preceding final transcript messages. The coordinator waits for that event before creating the single combined envelope, preventing the final provider fragment from being lost during shutdown.

## Translation reader

The preview becomes an adaptive reader:

- resizable window with a compact minimum and bounded automatic initial size;
- one clear title and a subdued provider/source-language line;
- borderless, padded, read-only rich-text reading surface with word wrap and vertical scrolling;
- no automatic full-text selection when shown;
- font zoom controls and `Ctrl + mouse wheel` zoom;
- fixed footer actions: `Thay thế`, `Sao chép`, and `Đóng`;
- `Esc` closes, `Ctrl + Enter` replaces, and the copy action remains available without modifying the original text;
- keyboard focus starts on the primary action instead of the reader.

The window stays topmost, does not create a taskbar entry, remains DPI-aware, and uses the existing Fluent palette.

## Verification

Automated coverage must prove:

- exact Speechmatics JSON contains enhanced, 2-second flexible mode, and disabled end-of-utterance detection;
- no IPC envelope is written while dictation is still listening, even after multiple final fragments;
- stopping emits one ordered combined envelope only after `EndOfTranscript`;
- duplicate final fragments are ignored;
- `EndOfTranscript` is observable by the coordinator;
- the translation form is resizable, exposes the three actions, uses a scrollable rich reader, and does not initially select the translated text.
