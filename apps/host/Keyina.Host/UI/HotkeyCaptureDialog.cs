using Keyina.Host.Core.Hotkeys;
using Keyina.Host.UI.Fluent;

namespace Keyina.Host.UI;

public sealed class HotkeyCaptureDialog : Form
{
    private readonly HotkeyCommand command;
    private readonly HotkeyPreferences preferences;
    private readonly Label keycap;
    private readonly Label status;
    private readonly FluentButton saveButton;
    private readonly FluentThemePalette palette = FluentTheme.Current;

    public HotkeyCaptureDialog(
        HotkeyCommand command,
        HotkeyPreferences preferences)
    {
        this.command = command;
        this.preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _ = preferences.GetPreference(command);

        Text = "Đổi phím tắt";
        AccessibleName = "Ghi tổ hợp phím tắt mới";
        AccessibleDescription =
            "Nhấn tổ hợp phím mới. Escape hủy thao tác và không thay đổi cài đặt.";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        ClientSize = new Size(520, 286);
        MinimumSize = new Size(480, 270);
        Font = new Font("Segoe UI Variable Text", 9.5F, FontStyle.Regular);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(24, 22, 24, 20),
            Margin = Padding.Empty,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
        Controls.Add(layout);

        var title = new Label
        {
            Name = "hotkeyCaptureTitle",
            Text = GetCommandTitle(command),
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = new Font("Segoe UI Variable Display", 17F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false,
        };
        layout.Controls.Add(title, 0, 0);

        var description = new Label
        {
            Name = "hotkeyCaptureDescription",
            Text = GetInstruction(command),
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            UseMnemonic = false,
        };
        layout.Controls.Add(description, 0, 1);

        var keycapCard = new FluentCard
        {
            Name = "hotkeyCaptureCard",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 8),
            Padding = new Padding(12),
            Palette = palette,
            UseSecondarySurface = true,
        };
        keycap = new Label
        {
            Name = "hotkeyCaptureKeycap",
            Text = "Nhấn tổ hợp phím…",
            AccessibleName = "Tổ hợp phím đang ghi",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            UseMnemonic = false,
        };
        keycapCard.Controls.Add(keycap);
        layout.Controls.Add(keycapCard, 0, 2);

        status = new Label
        {
            Name = "hotkeyCaptureStatus",
            Text = "Escape để hủy. Keyina không cho phép phím Windows hoặc tổ hợp bị trùng.",
            AccessibleName = "Trạng thái tổ hợp phím",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            UseMnemonic = false,
        };
        layout.Controls.Add(status, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
        };
        var cancelButton = new FluentButton
        {
            Name = "cancelHotkeyCapture",
            Text = "Hủy",
            AccessibleName = "Hủy đổi phím tắt",
            Kind = FluentButtonKind.Secondary,
            Palette = palette,
            Width = 96,
            Height = 36,
            DialogResult = DialogResult.Cancel,
        };
        saveButton = new FluentButton
        {
            Name = "saveHotkeyCapture",
            Text = "Áp dụng",
            AccessibleName = "Áp dụng tổ hợp phím mới",
            Kind = FluentButtonKind.Primary,
            Palette = palette,
            Width = 112,
            Height = 36,
            Enabled = false,
            Margin = new Padding(0, 0, 8, 0),
        };
        saveButton.Click += (_, _) =>
        {
            if (CapturedChord is null)
            {
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        actions.Controls.Add(cancelButton);
        actions.Controls.Add(saveButton);
        layout.Controls.Add(actions, 0, 4);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        ApplyPalette();
    }

    public HotkeyChord? CapturedChord { get; private set; }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        FluentWindow.Apply(this, palette);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        CaptureKeyData(e.KeyData);
        e.SuppressKeyPress = true;
        e.Handled = true;
        base.OnKeyDown(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Enter && CapturedChord is not null)
        {
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }

        CaptureKeyData(keyData);
        return true;
    }

    private void CaptureKeyData(Keys keyData)
    {
        var keyCode = keyData & Keys.KeyCode;
        if (keyCode == Keys.Escape)
        {
            CapturedChord = null;
            DialogResult = DialogResult.Cancel;
            return;
        }

        var modifiers = ToModifiers(keyData);
        var isModifierKey = TryGetModifier(keyCode, out var pressedModifier);
        modifiers |= pressedModifier;
        var key = VirtualKey.None;
        if (!isModifierKey && !TryMapKey(keyCode, out key))
        {
            Reject("Phím này chưa được Keyina hỗ trợ. Hãy chọn một phím chữ, số, F1–F24 hoặc phím điều hướng.");
            return;
        }

        var chord = new HotkeyChord(modifiers, key);
        if (isModifierKey && command != HotkeyCommand.ToggleVietnamese)
        {
            Reject("Hãy nhấn thêm một phím chính cho thao tác này.");
            return;
        }
        if (preferences.ToBindings().Any(binding =>
                binding.Command != command && binding.Chord == chord))
        {
            Reject("Tổ hợp này đã được dùng bởi một thao tác khác trong Keyina.");
            return;
        }

        try
        {
            preferences.WithChord(command, chord).Validate();
        }
        catch (ArgumentException exception)
        {
            Reject(LocalizeValidationError(exception.Message));
            return;
        }

        CapturedChord = chord;
        keycap.Text = HotkeyText.Format(chord);
        status.Text = "Tổ hợp hợp lệ. Nhấn Áp dụng hoặc Enter để lưu.";
        status.ForeColor = palette.Success;
        saveButton.Enabled = true;
    }

    private void Reject(string message)
    {
        CapturedChord = null;
        keycap.Text = "Tổ hợp không hợp lệ";
        status.Text = message;
        status.ForeColor = palette.Error;
        saveButton.Enabled = false;
    }

    private void ApplyPalette()
    {
        BackColor = palette.Window;
        ForeColor = palette.TextPrimary;
        foreach (Control control in Controls)
        {
            ApplyPaletteRecursive(control);
        }
    }

    private void ApplyPaletteRecursive(Control control)
    {
        if (control is Label label)
        {
            label.ForeColor = palette.TextPrimary;
            label.BackColor = Color.Transparent;
        }
        foreach (Control child in control.Controls)
        {
            ApplyPaletteRecursive(child);
        }
    }

    private static HotkeyModifiers ToModifiers(Keys keyData)
    {
        var modifiers = HotkeyModifiers.None;
        if ((keyData & Keys.Control) != 0)
        {
            modifiers |= HotkeyModifiers.Control;
        }
        if ((keyData & Keys.Shift) != 0)
        {
            modifiers |= HotkeyModifiers.Shift;
        }
        if ((keyData & Keys.Alt) != 0)
        {
            modifiers |= HotkeyModifiers.Alt;
        }
        return modifiers;
    }

    private static bool TryGetModifier(
        Keys keyCode,
        out HotkeyModifiers modifier)
    {
        modifier = keyCode switch
        {
            Keys.ControlKey or Keys.LControlKey or Keys.RControlKey =>
                HotkeyModifiers.Control,
            Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey =>
                HotkeyModifiers.Shift,
            Keys.Menu or Keys.LMenu or Keys.RMenu => HotkeyModifiers.Alt,
            Keys.LWin or Keys.RWin => HotkeyModifiers.Windows,
            _ => HotkeyModifiers.None,
        };
        return modifier != HotkeyModifiers.None;
    }

    private static bool TryMapKey(Keys keyCode, out VirtualKey key)
    {
        var value = unchecked((ushort)keyCode);
        key = (VirtualKey)value;
        return Enum.IsDefined(key) &&
            key != VirtualKey.None &&
            !key.IsModifier();
    }

    private static string GetCommandTitle(HotkeyCommand command) => command switch
    {
        HotkeyCommand.ToggleVietnamese => "Bật hoặc tắt bộ gõ tiếng Việt",
        HotkeyCommand.PushToTalkPressed => "Giữ để nhập bằng giọng nói",
        HotkeyCommand.ToggleDictation => "Bắt đầu hoặc dừng nhập giọng nói",
        HotkeyCommand.TranslateSelection => "Dịch văn bản đang chọn",
        HotkeyCommand.CancelDictation => "Hủy thao tác đang chạy",
        _ => "Đổi phím tắt",
    };

    private static string GetInstruction(HotkeyCommand command) =>
        command == HotkeyCommand.ToggleVietnamese
            ? "Nhấn ít nhất hai phím bổ trợ, ví dụ Ctrl + Shift."
            : "Nhấn tổ hợp gồm phím bổ trợ và một phím chính.";

    private static string LocalizeValidationError(string message)
    {
        if (message.Contains("Windows-key", StringComparison.Ordinal))
        {
            return "Phím Windows được dành cho hệ điều hành. Hãy chọn Ctrl, Shift hoặc Alt.";
        }
        if (message.Contains("Duplicate", StringComparison.Ordinal))
        {
            return "Tổ hợp này đã được dùng bởi một thao tác khác trong Keyina.";
        }
        if (message.Contains("requires at least two", StringComparison.Ordinal))
        {
            return "Cần ít nhất hai phím bổ trợ cho thao tác bật hoặc tắt bộ gõ.";
        }
        if (message.Contains("requires a modifier", StringComparison.Ordinal) ||
            message.Contains("require a modifier", StringComparison.Ordinal))
        {
            return "Hãy thêm Ctrl, Shift hoặc Alt vào tổ hợp này.";
        }
        return "Tổ hợp này không phù hợp với loại thao tác đã chọn.";
    }
}
