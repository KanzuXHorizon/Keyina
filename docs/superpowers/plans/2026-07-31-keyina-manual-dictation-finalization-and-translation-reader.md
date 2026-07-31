# Keyina Manual Dictation Finalization and Translation Reader Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Speechmatics dictation run continuously until the second `Ctrl + Alt + V` press and polish the translation preview into an adaptive reader.

**Architecture:** Speechmatics final fragments are accumulated in `TranscriptAggregator`, while `EndOfTranscript` is forwarded through the event stream so `DictationCoordinator` can flush one combined IPC envelope only after provider completion. The translation form keeps the current WinForms/Fluent surface but replaces the cramped fixed textbox with a resizable rich-text reader and explicit actions.

**Tech Stack:** .NET 8, WinForms, System.Text.Json, bounded channels, Keyina custom test runner.

## Global Constraints

- Preserve all unrelated uncommitted user changes.
- Do not change `Ctrl + Alt + V` or `Ctrl + Alt + T` bindings.
- Use Speechmatics `model=enhanced`, `max_delay=2.0`, `max_delay_mode=flexible`, partials enabled, and end-of-utterance silence trigger `0`.
- Never insert partials or intermediate final fragments into the focused application.
- Do not commit or push without explicit user authorization.

---

### Task 1: Lock the Speechmatics production contract

**Files:**
- Modify: `apps/host/Keyina.Host.Tests/SpeechmaticsProtocolTests.cs`
- Modify: `apps/host/Keyina.Speechmatics/SpeechmaticsOptions.cs`
- Modify: `apps/host/Keyina.Speechmatics/SpeechmaticsProtocol.cs`

**Interfaces:**
- Produces: `SpeechmaticsOptions.MaxDelayMode`, `SpeechmaticsOptions.EndOfUtteranceSilenceTriggerSeconds`, and deterministic `StartRecognition` JSON.

- [ ] Update the exact JSON/default tests to require enhanced, 2 seconds, flexible mode, and silence trigger 0.
- [ ] Run the Speechmatics protocol tests and confirm they fail against the current configuration.
- [ ] Add validated option properties and serialize them into `transcription_config`.
- [ ] Run the protocol tests and confirm they pass.

### Task 2: Flush one transcript only on manual stop

**Files:**
- Modify: `apps/host/Keyina.Host.Tests/TranscriptAggregatorTests.cs`
- Modify: `apps/host/Keyina.Host.Tests/SpeechmaticsSessionTests.cs`
- Modify: `apps/host/Keyina.Host.Tests/DictationCoordinatorTests.cs`
- Modify: `apps/host/Keyina.Host.Core/Speech/TranscriptAggregator.cs`
- Modify: `apps/host/Keyina.Speechmatics/SpeechmaticsRealtimeSession.cs`
- Modify: `apps/host/Keyina.Host/Speech/DictationCoordinator.cs`

**Interfaces:**
- Produces: `TranscriptAggregator.Complete(IpcSessionId, ulong)` returning one optional combined envelope.
- Produces: an `EndOfTranscript` event delivered by `ISpeechmaticsRealtimeSession.ReadEventAsync`.

- [ ] Change tests so final fragments produce no IPC while listening and one combined IPC after stop.
- [ ] Add a session test requiring `EndOfTranscript` to be observable after all final events.
- [ ] Run focused speech tests and confirm the new assertions fail.
- [ ] Change the aggregator to accumulate/deduplicate finals and expose one completion envelope.
- [ ] Forward `EndOfTranscript` through the realtime event queue in provider order.
- [ ] Make the coordinator wait for provider completion, then write one combined envelope.
- [ ] Run focused speech tests and confirm they pass.

### Task 3: Polish the translation preview reader

**Files:**
- Modify: `apps/host/Keyina.Host.Tests/TranslationPreviewFormTests.cs`
- Modify: `apps/host/Keyina.Host/UI/TranslationPreviewForm.cs`

**Interfaces:**
- Preserves: existing constructor callbacks for replace, copy, and cancel.
- Produces: named controls `replaceTranslationPreview`, `copyTranslationPreview`, `cancelTranslationPreview`, and `translationPreviewTranslated`.

- [ ] Update form tests to require a sizable adaptive form, scrollable rich reader, and all three actions.
- [ ] Run the interactive form test target or build and confirm the current form does not satisfy the structure.
- [ ] Implement the adaptive layout, rich reader, zoom controls, keyboard shortcuts, and initial focus behavior.
- [ ] Run the form tests/build and confirm they pass.

### Task 4: Full verification and diff review

**Files:**
- Review all modified files above and the two documentation files.

- [ ] Run all non-interactive host tests.
- [ ] Build the host solution/configuration used by the project.
- [ ] Inspect `git diff --check` and the scoped diff for accidental churn, secrets, or overwritten user work.
- [ ] Report actual commands, pass/fail counts, and any test that could not run on the available environment.
