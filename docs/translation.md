# Translation

Keyina can translate the text currently selected in another Windows application and replace that selection without activating a Keyina window.

## Provider

The first supported provider is DeepL API Free.

- Free quota: up to 500,000 translated characters per month.
- Vietnamese and automatic source-language detection are supported.
- Free API keys normally end in `:fx` and use `https://api-free.deepl.com`.
- Keyina also accepts a DeepL API Pro key and selects the Pro endpoint automatically.

Translation is disabled by default. Keyina does not ship a shared provider key; each user must configure their own credential.

## Configure

1. Create a DeepL API Free account and obtain an authentication key.
2. Open **Cài đặt Keyina → Dịch nhanh**.
3. Use **Cách lấy khóa** to open DeepL's official authentication guide when needed.
4. Paste the key into **Khóa DeepL API Free** and select **Lưu khóa**. Leading and trailing whitespace from clipboard paste is removed before storage.
5. Choose the target language.
6. Enable **Bật dịch nhanh văn bản đang chọn**.

The key is masked while entered and stored under `Keyina/DeepL/ApiKey` in Windows Credential Manager. It is never written to `settings.json`. Removing the key also disables translation and releases its optional shortcut.

## Use

1. Select text in the foreground application.
2. Press `Ctrl + Alt + T`.
3. Keep the same window and text control focused until the request finishes.

Keyina temporarily captures the selected Unicode text, restores the previous clipboard contents, sends one translation request, and replaces the selection through its marked Unicode input path. Press `Escape` to cancel an in-flight request.

Before the request, Keyina detects code spans and blocks, URLs, email addresses, Windows paths, template placeholders, command flags, method calls, and path-like identifiers. These tokens are wrapped in deterministic XML keep tags and sent with DeepL XML tag handling v2. The response is accepted only when every protected token appears exactly once; otherwise Keyina refuses to insert it. Content made only of protected tokens is returned unchanged without consuming translation quota.

Keyina refuses to insert the result if either the foreground window or the focused text control changed after capture. A new translation command cancels the previous command.

Translation progress uses Keyina's existing no-focus feedback system. Windowed applications receive a compact overlay and sound according to the selected feedback mode; fullscreen-like applications suppress the overlay automatically and use audio only. Feedback contains only status and target-language names, never selected or translated content.

The translation shortcut is registered only while translation is enabled and a DeepL credential exists, so an incomplete or unused feature does not reserve `Ctrl + Alt + T`. The tray translation command also remains disabled until both requirements are met. If another application already owns that chord, Keyina keeps the Vietnamese input hook and its other shortcuts running, shows the conflict in settings, and leaves translation available from the tray menu.

## Safety limits

- Maximum input: 20,000 Unicode characters.
- Request timeout: 8 seconds.
- Response limit: 256 KiB.
- No automatic retries, because a retry could consume quota twice.
- No source text, translated text, clipboard data, or API key is written to logs.
- Code, URLs, email addresses, paths, and placeholders are restored byte-for-byte after translation.
- Empty selections and malformed provider responses are rejected without inserting partial output.

## Privacy

Ordinary Vietnamese typing remains offline and does not use this feature.

When translation is enabled, the selected text is sent to DeepL. DeepL API Free must not be used for personal data, confidential information, secrets, or other sensitive content. Disable translation when working with such material.

See DeepL's current API documentation and terms before enabling the feature:

- https://developers.deepl.com/docs/resources/usage-limits
- https://developers.deepl.com/docs/getting-started/auth
- https://developers.deepl.com/docs/api-reference/translate
- https://www.deepl.com/en/pro-data-security
