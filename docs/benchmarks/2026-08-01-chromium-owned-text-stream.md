# Chromium Owned Text Stream Verification — 2026-08-01

## Defect reproduced

Deleting the deferred single-slot replacement queue removed one ordering race, but burst input still mixed two independent streams:

- physical literal characters passed through the hook;
- transformed text was synchronously injected with selection replacement.

A native sentence probe processed every expected physical event and reported zero injection failures, yet the target received corrupted mixed Vietnamese/Latin text. A controller-only regression produced the correct sentence, locating the defect in Win32 delivery rather than Telex composition.

## Fix

For safe Chromium selection-replacement contexts, Keyina now owns the complete supported text stream:

- literal text key-down events are encoded as validated UTF-16 and injected with the Keyina marker;
- transformed decisions continue through selection replacement;
- physical text key-down and matching key-up events are suppressed;
- key-up reconciles both owned-literal state and controller suppression, covering auto-repeat transitions from literal to transformed input;
- disabled Vietnamese mode, secure/bypassed input, clipboard compatibility, shortcuts, navigation, unsupported keys, and injection failures remain outside the owned stream.

The implementation uses one fixed 256-key bitset. It adds no production thread, queue, timer, heap allocation, or dependency.

## Deterministic coverage

Native unit tests verify:

- owned-stream policy accepts only the safe configuration;
- clipboard mode takes precedence;
- secure/bypassed and ordinary targets are rejected;
- BMP and non-BMP literal Unicode encoding;
- invalid Unicode rejection without output mutation;
- mixed Vietnamese and Latin sentence composition remains exact.

Final native unit result: `136/136` passed.

## Interactive ordering probe

Command:

```text
KeyinaInput.exe --chromium-ordering-self-test
```

Workload:

```text
raw:      tuyf banj cuws research vaf dduwa ra huowngs toots nhaats <space>
expected: tuỳ bạn cứ research và đưa ra hướng tốt nhất <space>
delays:   0 ms, 5 ms, 10 ms
```

Clean Debug run:

```text
all three delay cases passed
116/116 marked physical events processed per case
58/58 text injections succeeded per case
0 failed injections
exact final text: tuỳ bạn cứ research và đưa ra hướng tốt nhất <space>
```

The probe is not registered as default CTest because foreground ownership is nondeterministic on a shared interactive Windows desktop. A stolen foreground produces an explicit diagnostic failure instead of a misleading release regression.

## Final verification

- Native Debug CTest: `12/12` passed.
- Native Release CTest: `12/12` passed.
- Managed Release tests: `303/303` passed.
- Managed Release build: 0 warnings, 0 errors.
- Native resident without tray: 2,576,384-byte private working set, 4 threads, 0 thread delta, budget passed.
- Native resident with tray: 2,682,880-byte private working set, 4 threads, 0 thread delta, budget passed.

## Evidence limits

The isolated probe exercises the real low-level hook, physical marked events, context capture, controller, selection replacement, and target text verification. It is not a substitute for a manual test in the actual Microsoft Edge address bar and web text controls. No claim of real-Edge completion is made from this synthetic target alone.
