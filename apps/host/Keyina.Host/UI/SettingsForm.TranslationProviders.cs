using Keyina.Host.Core.Translation;
using Keyina.Host.UI.Fluent;

namespace Keyina.Host.UI;

public sealed partial class SettingsForm
{
    private FluentCard CreateLibreTranslateCard()
    {
        var card = CreateCard("libreTranslateCard", 352);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 8,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        card.Controls.Add(layout);

        var title = CreateLabel(
            "libreTranslateTitle",
            "Fallback LibreTranslate",
            LabelRole.Heading);
        title.Dock = DockStyle.Fill;
        layout.Controls.Add(title, 0, 0);
        layout.SetColumnSpan(title, 2);
        libreTranslateCredentialStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        layout.Controls.Add(libreTranslateCredentialStatus, 2, 0);

        var intro = CreateLabel(
            "libreTranslateIntro",
            "Chỉ dùng endpoint do bạn chọn. Keyina không tự kết nối public mirror; fallback chỉ xảy ra khi DeepL mất mạng, rate-limit hoặc hết quota.",
            LabelRole.Secondary);
        intro.Dock = DockStyle.Fill;
        layout.Controls.Add(intro, 0, 1);
        layout.SetColumnSpan(intro, 3);

        libreTranslateToggle.Dock = DockStyle.Fill;
        layout.Controls.Add(libreTranslateToggle, 0, 2);
        layout.SetColumnSpan(libreTranslateToggle, 3);

        var endpointLabel = CreateLabel(
            "libreTranslateEndpointLabel",
            "Endpoint server",
            LabelRole.Caption);
        endpointLabel.Dock = DockStyle.Fill;
        endpointLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(endpointLabel, 0, 3);
        layout.SetColumnSpan(endpointLabel, 3);
        var endpointFrame = CreateInputFrame(libreTranslateEndpoint);
        endpointFrame.Dock = DockStyle.Fill;
        endpointFrame.Margin = new Padding(0, 3, 0, 3);
        layout.Controls.Add(endpointFrame, 0, 4);
        layout.SetColumnSpan(endpointFrame, 3);

        allowLocalTranslationEndpointToggle.Dock = DockStyle.Fill;
        layout.Controls.Add(allowLocalTranslationEndpointToggle, 0, 5);
        layout.SetColumnSpan(allowLocalTranslationEndpointToggle, 3);

        var reveal = CreateButton(
            "toggleLibreTranslateKeyVisibility",
            "Hiện",
            FluentButtonKind.Subtle,
            72);
        reveal.AccessibleName = "Hiện hoặc ẩn khóa LibreTranslate";
        reveal.Click += (_, _) =>
        {
            libreTranslateApiKey.UseSystemPasswordChar =
                !libreTranslateApiKey.UseSystemPasswordChar;
            reveal.Text = libreTranslateApiKey.UseSystemPasswordChar ? "Hiện" : "Ẩn";
        };
        var keyFrame = CreateInputFrame(libreTranslateApiKey, reveal);
        keyFrame.Dock = DockStyle.Fill;
        keyFrame.Margin = new Padding(0, 3, 0, 3);
        layout.Controls.Add(keyFrame, 0, 6);
        layout.SetColumnSpan(keyFrame, 3);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        var actionsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 3, 0, 0),
        };
        saveLibreTranslateKey.Margin = new Padding(0, 0, 8, 0);
        removeLibreTranslateKey.Margin = Padding.Empty;
        actionsPanel.Controls.Add(saveLibreTranslateKey);
        actionsPanel.Controls.Add(removeLibreTranslateKey);
        footer.Controls.Add(actionsPanel, 0, 0);
        var safety = CreateLabel(
            "libreTranslateSafety",
            "HTTPS bắt buộc cho server public. HTTP chỉ hợp lệ khi bật local mode và DNS chỉ trả địa chỉ local/private.",
            LabelRole.Tertiary);
        safety.Dock = DockStyle.Fill;
        safety.TextAlign = ContentAlignment.MiddleLeft;
        safety.Margin = new Padding(12, 0, 0, 0);
        footer.Controls.Add(safety, 1, 0);
        layout.Controls.Add(footer, 0, 7);
        layout.SetColumnSpan(footer, 3);
        return card;
    }

    private void SaveTranslationProviderPreferences()
    {
        try
        {
            var preferences = new TranslationProviderPreferences(
                libreTranslateToggle.Checked,
                libreTranslateEndpoint.Text,
                allowLocalTranslationEndpointToggle.Checked)
                .Normalize();
            actions.SetTranslationProviders(preferences);
        }
        catch (ArgumentException)
        {
            SetBadge(
                libreTranslateCredentialStatus,
                "Endpoint chưa hợp lệ",
                FluentTone.Error);
        }
    }

    private void SaveLibreTranslateCredential()
    {
        var secret = libreTranslateApiKey.Text.Trim();
        if (secret.Length == 0)
        {
            return;
        }
        actions.SaveLibreTranslateApiKey(secret);
        libreTranslateApiKey.Clear();
    }
}
