# Translation

Keyina can translate the text currently selected in another Windows application and replace that selection without activating a Keyina window.

## Providers

Keyina uses a provider-neutral translation router.

- **DeepL API** remains the primary provider. API Free keys normally end in `:fx`; Keyina selects the Free or Pro endpoint automatically.
- **LibreTranslate** is an optional user-configured fallback and can also run as the only provider. Keyina never selects a public mirror automatically.
- LibreTranslate credentials are optional because self-hosted servers may not require an API key.
- Fallback occurs only when DeepL is unavailable, rate-limited, or out of quota. Authentication, unsupported-language, malformed-response, and protected-token failures are not hidden by fallback.

Translation is disabled by default. Keyina does not ship shared provider credentials or a default LibreTranslate endpoint.

## Configure

1. Open **Cài đặt Keyina → Dịch nhanh**.
2. Configure at least one provider:
   - For DeepL, paste an API key into **Khóa DeepL API Free** and select **Lưu khóa**.
   - For LibreTranslate, enable the fallback, enter the exact server endpoint, and optionally store its API key.
3. Choose the target language.
4. Enable **Bật dịch nhanh văn bản đang chọn**.

DeepL and LibreTranslate keys are masked and stored separately under `Keyina/DeepL/ApiKey` and `Keyina/LibreTranslate/ApiKey` in Windows Credential Manager. They are never written to `settings.json` or portable exports.

Public LibreTranslate endpoints must use HTTPS. HTTP is accepted only after the user explicitly enables local mode and DNS resolves exclusively to loopback or private addresses. Redirects are disabled, and mixed public/private DNS responses are rejected.

## Use

1. Select text in the foreground application.
2. Press `Ctrl + Alt + T`.
3. Keep the same window and text control focused until the request finishes.
4. When preview mode is enabled, compare both versions and choose **Replace**, **Copy**, or **Cancel**.

Keyina temporarily captures the selected Unicode text, restores the previous clipboard contents, sends one translation request, and replaces the selection through its marked Unicode input path. Press `Escape` to cancel an in-flight request.

Before the request, Keyina detects code spans and blocks, URLs, email addresses, Windows paths, template placeholders, command flags, method calls, and path-like identifiers. These tokens are wrapped in deterministic XML keep tags and sent with DeepL XML tag handling v2. The response is accepted only when every protected token appears exactly once; otherwise Keyina refuses to insert it. Content made only of protected tokens is returned unchanged without consuming translation quota.

Keyina refuses to insert the result if either the foreground window or the focused text control changed after capture. A new translation command cancels the previous command.

After a successful replacement, `Ctrl + Alt + Z` can restore the original text once for 30 seconds. Undo is accepted only when the same foreground window and focused control still contain the exact translated text immediately before the caret. The undo entry exists only in memory, is replaced by a newer translation, and is never logged or persisted.

Preview mode is opt-in. It opens an interactive comparison window instead of replacing immediately. The preview expires after two minutes; **Replace** restores focus to the captured application before inserting and then creates the same one-shot undo entry. **Copy** writes only the translated result to the clipboard after an explicit user action.

Translation progress uses Keyina's existing no-focus feedback system. Windowed applications receive a compact overlay and sound according to the selected feedback mode; fullscreen-like applications suppress the overlay automatically and use audio only. Feedback contains only status and target-language names, never selected or translated content.

The translation shortcut is registered only while translation is enabled and at least one provider is usable: either a DeepL credential exists or LibreTranslate is enabled with a validated endpoint. An incomplete or unused feature therefore does not reserve `Ctrl + Alt + T`. If another application already owns that chord, Keyina keeps the Vietnamese input hook and its other shortcuts running, shows the conflict in settings, and leaves translation available from the tray menu.

## Safety limits

- Maximum input: 20,000 Unicode characters.
- Request timeout: 8 seconds.
- Response limit: 256 KiB.
- No automatic retries, because a retry could consume quota twice.
- No source text, translated text, clipboard data, or API key is written to logs.
- Code, URLs, email addresses, paths, and placeholders are restored byte-for-byte after translation.
- Empty selections and malformed provider responses are rejected without inserting partial output.
- Interactive previews expire after two minutes.
- Translation undo is one-shot and expires after 30 seconds.

## Privacy

Ordinary Vietnamese typing remains offline and does not use this feature.

When translation is enabled, the selected text is sent to the provider selected by the router. Do not use cloud endpoints for personal data, confidential information, secrets, or other sensitive content. A self-hosted LibreTranslate instance can reduce third-party exposure but remains the user's responsibility to secure, update, and operate.

See the providers' current documentation and terms before enabling the feature:

- https://developers.deepl.com/docs/resources/usage-limits
- https://developers.deepl.com/docs/getting-started/auth
- https://developers.deepl.com/docs/api-reference/translate
- https://www.deepl.com/en/pro-data-security
- https://docs.libretranslate.com/guides/api_usage/
- https://docs.libretranslate.com/guides/manage_api_keys/
