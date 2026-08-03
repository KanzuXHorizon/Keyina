using Keyina.Host.Core.Overlay;
using Keyina.Host.UI.Fluent;

namespace Keyina.Host.UI;

public sealed partial class SettingsForm
{
    private FluentToggle keystrokeOverlayToggle = null!;
    private FluentToggle keystrokeOverlayPresentationToggle = null!;
    private ComboBox keystrokeOverlayMotion = null!;
    private ComboBox keystrokeOverlayCorner = null!;
    private NumericUpDown keystrokeOverlaySize = null!;
    private NumericUpDown keystrokeOverlayOpacity = null!;
    private NumericUpDown keystrokeOverlayHideDelay = null!;
    private FluentToggle keystrokeOverlaySoundToggle = null!;
    private NumericUpDown keystrokeOverlaySoundVolume = null!;
    private FluentButton previewKeystrokeOverlay = null!;
    private Label keystrokeOverlayPreviewText = null!;
    private System.Windows.Forms.Timer keystrokeOverlayPreviewTimer = null!;
    private int keystrokeOverlayPreviewStage;

    private void InitializeKeystrokeOverlayControls()
    {
        keystrokeOverlayToggle = CreateToggle(
            "keystrokeOverlayToggle",
            "Hiển thị overlay phím gõ");
        keystrokeOverlayPresentationToggle = CreateToggle(
            "keystrokeOverlayPresentationToggle",
            "Chế độ trình chiếu");
        keystrokeOverlayMotion = CreateOverlaySelector(
            "keystrokeOverlayMotion",
            "Mức chuyển động",
            ["Thích ứng — khuyến nghị", "Đầy đủ", "Giảm chuyển động", "Tắt"]);
        keystrokeOverlayCorner = CreateOverlaySelector(
            "keystrokeOverlayCorner",
            "Góc dự phòng",
            ["Phải dưới", "Trái dưới", "Phải trên", "Trái trên"]);
        keystrokeOverlaySize = CreateOverlayNumber(
            "keystrokeOverlaySize",
            "Kích thước overlay theo phần trăm",
            KeystrokeOverlayPreferences.MinimumSizePercent,
            KeystrokeOverlayPreferences.MaximumSizePercent,
            5);
        keystrokeOverlayOpacity = CreateOverlayNumber(
            "keystrokeOverlayOpacity",
            "Độ mờ overlay theo phần trăm",
            KeystrokeOverlayPreferences.MinimumOpacityPercent,
            KeystrokeOverlayPreferences.MaximumOpacityPercent,
            5);
        keystrokeOverlayHideDelay = CreateOverlayNumber(
            "keystrokeOverlayHideDelay",
            "Thời gian tự ẩn overlay theo mili giây",
            KeystrokeOverlayPreferences.MinimumHideDelayMilliseconds,
            KeystrokeOverlayPreferences.MaximumHideDelayMilliseconds,
            100);
        keystrokeOverlaySoundToggle = CreateToggle(
            "keystrokeOverlaySoundToggle",
            "Bật âm thanh theo phím");
        keystrokeOverlaySoundVolume = CreateOverlayNumber(
            "keystrokeOverlaySoundVolume",
            "Âm lượng âm thanh phím theo phần trăm",
            KeystrokeOverlayPreferences.MinimumSoundVolumePercent,
            KeystrokeOverlayPreferences.MaximumSoundVolumePercent,
            5);
        previewKeystrokeOverlay = CreateButton(
            "previewKeystrokeOverlay",
            "Xem thử",
            FluentButtonKind.Secondary,
            108);
        keystrokeOverlayPreviewText = CreateLabel(
            "keystrokeOverlayPreviewText",
            "n   g   u   y   e   n   →   nguyễn",
            LabelRole.Heading);
        keystrokeOverlayPreviewText.TextAlign = ContentAlignment.MiddleCenter;
        keystrokeOverlayPreviewText.Dock = DockStyle.Fill;
        keystrokeOverlayPreviewText.AccessibleDescription =
            "Mô phỏng quá trình phím thô chuyển thành chữ tiếng Việt hoàn chỉnh.";
        keystrokeOverlayPreviewTimer = new System.Windows.Forms.Timer
        {
            Interval = 140,
        };
        keystrokeOverlayPreviewTimer.Tick += (_, _) => AdvanceKeystrokeOverlayPreview();
    }

    private FluentCard CreateKeystrokeOverlayCard()
    {
        var card = CreateCard("keystrokeOverlayCard", 456);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(4),
            Margin = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
        for (var index = 1; index <= 8; index++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
        }
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        card.Controls.Add(layout);

        var heading = CreateIconTextLayout(
            "\uE8D7",
            "Overlay phím gõ",
            "Hiện token gõ rồi chuyển mượt thành chữ tiếng Việt hoàn chỉnh. Tự ẩn trong trường bảo mật và không giữ lịch sử.");
        layout.SetColumnSpan(heading, 2);
        layout.Controls.Add(heading, 0, 0);

        AddOverlaySetting(layout, 1, "Bật overlay", keystrokeOverlayToggle);
        AddOverlaySetting(layout, 2, "Chuyển động", keystrokeOverlayMotion);

        var sizePanel = CreateOverlayNumberPanel(keystrokeOverlaySize, "%");
        AddOverlaySetting(layout, 3, "Kích thước", sizePanel);
        var opacityPanel = CreateOverlayNumberPanel(keystrokeOverlayOpacity, "%");
        AddOverlaySetting(layout, 4, "Độ mờ", opacityPanel);
        var hidePanel = CreateOverlayNumberPanel(keystrokeOverlayHideDelay, "ms");
        AddOverlaySetting(layout, 5, "Tự ẩn", hidePanel);

        AddOverlaySetting(layout, 6, "Góc dự phòng", keystrokeOverlayCorner);
        AddOverlaySetting(layout, 7, "Âm thanh", keystrokeOverlaySoundToggle);
        var soundPanel = CreateOverlayNumberPanel(keystrokeOverlaySoundVolume, "%");
        AddOverlaySetting(layout, 8, "Âm lượng", soundPanel);

        var previewPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 0),
        };
        previewPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        previewPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        previewPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116F));
        previewPanel.Controls.Add(keystrokeOverlayPreviewText, 0, 0);
        previewPanel.Controls.Add(keystrokeOverlayPresentationToggle, 1, 0);
        previewPanel.Controls.Add(previewKeystrokeOverlay, 2, 0);
        layout.SetColumnSpan(previewPanel, 2);
        layout.Controls.Add(previewPanel, 0, 9);
        return card;
    }

    private void WireKeystrokeOverlayEvents()
    {
        keystrokeOverlayToggle.CheckedChanged += (_, _) => SaveKeystrokeOverlayPreferences();
        keystrokeOverlayPresentationToggle.CheckedChanged += (_, _) => SaveKeystrokeOverlayPreferences();
        keystrokeOverlayMotion.SelectedIndexChanged += (_, _) => SaveKeystrokeOverlayPreferences();
        keystrokeOverlayCorner.SelectedIndexChanged += (_, _) => SaveKeystrokeOverlayPreferences();
        keystrokeOverlaySize.ValueChanged += (_, _) => SaveKeystrokeOverlayPreferences();
        keystrokeOverlayOpacity.ValueChanged += (_, _) => SaveKeystrokeOverlayPreferences();
        keystrokeOverlayHideDelay.ValueChanged += (_, _) => SaveKeystrokeOverlayPreferences();
        keystrokeOverlaySoundToggle.CheckedChanged += (_, _) => SaveKeystrokeOverlayPreferences();
        keystrokeOverlaySoundVolume.ValueChanged += (_, _) => SaveKeystrokeOverlayPreferences();
        previewKeystrokeOverlay.Click += (_, _) => StartKeystrokeOverlayPreview();
    }

    private void StartKeystrokeOverlayPreview()
    {
        keystrokeOverlayPreviewTimer.Stop();
        keystrokeOverlayPreviewStage = 0;
        previewKeystrokeOverlay.Enabled = false;
        previewKeystrokeOverlay.Text = "Đang xem…";
        keystrokeOverlayPreviewText.Text = "n   g   u   y   e   n";
        keystrokeOverlayPreviewText.AccessibleName = "Đang mô phỏng các phím n g u y e n";

        if (keystrokeOverlayMotion.SelectedIndex ==
            (int)KeystrokeOverlayMotionLevel.Off)
        {
            CompleteKeystrokeOverlayPreview();
            return;
        }
        keystrokeOverlayPreviewTimer.Interval =
            keystrokeOverlayMotion.SelectedIndex ==
                (int)KeystrokeOverlayMotionLevel.Reduced
                ? 160
                : 110;
        keystrokeOverlayPreviewTimer.Start();
    }

    private void AdvanceKeystrokeOverlayPreview()
    {
        if (IsDisposed || Disposing)
        {
            keystrokeOverlayPreviewTimer.Stop();
            return;
        }

        if (keystrokeOverlayPreviewStage++ == 0)
        {
            keystrokeOverlayPreviewText.Text = "nguyen";
            keystrokeOverlayPreviewText.AccessibleName =
                "Đang ghép các phím thành từ nguyen";
            return;
        }
        CompleteKeystrokeOverlayPreview();
    }

    private void CompleteKeystrokeOverlayPreview()
    {
        keystrokeOverlayPreviewTimer.Stop();
        keystrokeOverlayPreviewText.Text = "nguyễn";
        keystrokeOverlayPreviewText.AccessibleName =
            "Kết quả xem trước: nguyễn";
        previewKeystrokeOverlay.Text = "Xem thử";
        previewKeystrokeOverlay.Enabled = true;
        actions.PreviewKeystrokeOverlay();
    }

    private void ApplyKeystrokeOverlaySnapshot(KeystrokeOverlayPreferences preferences)
    {
        keystrokeOverlayToggle.Checked = preferences.Enabled;
        keystrokeOverlayPresentationToggle.Checked = preferences.PresentationMode;
        keystrokeOverlayMotion.SelectedIndex = (int)preferences.Motion;
        keystrokeOverlayCorner.SelectedIndex = (int)preferences.FallbackCorner;
        keystrokeOverlaySize.Value = preferences.SizePercent;
        keystrokeOverlayOpacity.Value = preferences.OpacityPercent;
        keystrokeOverlayHideDelay.Value = preferences.HideDelayMilliseconds;
        keystrokeOverlaySoundToggle.Checked = preferences.PerKeySoundEnabled;
        keystrokeOverlaySoundVolume.Value = preferences.SoundVolumePercent;
        UpdateKeystrokeOverlayControlState();
    }

    private void SaveKeystrokeOverlayPreferences()
    {
        if (applyingSnapshot || keystrokeOverlayMotion.SelectedIndex < 0 ||
            keystrokeOverlayCorner.SelectedIndex < 0)
        {
            return;
        }
        var preferences = new KeystrokeOverlayPreferences(
            keystrokeOverlayToggle.Checked,
            (KeystrokeOverlayMotionLevel)keystrokeOverlayMotion.SelectedIndex,
            decimal.ToInt32(keystrokeOverlaySize.Value),
            decimal.ToInt32(keystrokeOverlayOpacity.Value),
            decimal.ToInt32(keystrokeOverlayHideDelay.Value),
            (KeystrokeOverlayFallbackCorner)keystrokeOverlayCorner.SelectedIndex,
            keystrokeOverlayPresentationToggle.Checked,
            keystrokeOverlaySoundToggle.Checked,
            decimal.ToInt32(keystrokeOverlaySoundVolume.Value));
        preferences.Validate();
        UpdateKeystrokeOverlayControlState();
        actions.SetKeystrokeOverlayPreferences(preferences);
    }

    private void UpdateKeystrokeOverlayControlState()
    {
        var enabled = keystrokeOverlayToggle.Checked;
        keystrokeOverlayMotion.Enabled = enabled;
        keystrokeOverlayCorner.Enabled = enabled;
        keystrokeOverlaySize.Enabled = enabled;
        keystrokeOverlayOpacity.Enabled = enabled;
        keystrokeOverlayHideDelay.Enabled = enabled;
        keystrokeOverlayPresentationToggle.Enabled = enabled;
        keystrokeOverlaySoundToggle.Enabled = enabled;
        keystrokeOverlaySoundVolume.Enabled = enabled && keystrokeOverlaySoundToggle.Checked;
    }

    private ComboBox CreateOverlaySelector(
        string name,
        string accessibleName,
        string[] items)
    {
        var selector = new ComboBox
        {
            Name = name,
            AccessibleName = accessibleName,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Width = 220,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 9.25F),
        };
        selector.Items.AddRange(items);
        return selector;
    }

    private NumericUpDown CreateOverlayNumber(
        string name,
        string accessibleName,
        int minimum,
        int maximum,
        int increment) => new()
        {
            Name = name,
            AccessibleName = accessibleName,
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            ThousandsSeparator = false,
            BorderStyle = BorderStyle.FixedSingle,
            Width = 118,
            TextAlign = HorizontalAlignment.Right,
            Font = new Font(Font.FontFamily, 9.25F),
        };

    private static FlowLayoutPanel CreateOverlayNumberPanel(NumericUpDown input, string suffix)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        panel.Controls.Add(input);
        panel.Controls.Add(new Label
        {
            Text = suffix,
            AutoSize = true,
            Margin = new Padding(6, 7, 0, 0),
        });
        return panel;
    }

    private void AddOverlaySetting(
        TableLayoutPanel layout,
        int row,
        string title,
        Control control)
    {
        var label = CreateLabel($"keystrokeOverlay{row}Label", title, LabelRole.Secondary);
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(control, 1, row);
    }
}

