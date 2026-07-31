using System.Runtime.InteropServices;
using System.Text;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.UI;

public sealed partial class SettingsForm
{
    private void StartTypingDiagnosticCapture()
    {
        if (!typingDiagnosticInput.IsHandleCreated)
        {
            _ = typingDiagnosticInput.Handle;
        }

        TypingDiagnosticTrace.Activate(typingDiagnosticInput.Handle);
        typingDiagnosticStatus.Text = "Đang ghi — chỉ ô sandbox này.";
        typingDiagnosticStatus.ForeColor = palette.Success;
        typingDiagnosticTimer.Start();
        RecordTypingDiagnosticControlEvent("Session.Start");
    }

    private void PauseTypingDiagnosticCapture()
    {
        if (typingDiagnosticInput.IsHandleCreated)
        {
            RecordTypingDiagnosticControlEvent("Session.Pause");
            TypingDiagnosticTrace.Deactivate(typingDiagnosticInput.Handle);
        }

        typingDiagnosticTimer.Stop();
        typingDiagnosticStatus.Text = "Tạm dừng — log vẫn được giữ để xem hoặc xuất.";
        typingDiagnosticStatus.ForeColor = palette.TextSecondary;
        RefreshTypingDiagnosticLog();
    }

    private void RecordTypingDiagnosticControlEvent(string eventName)
    {
        if (!typingDiagnosticInput.IsHandleCreated)
        {
            return;
        }

        TypingDiagnosticTrace.RecordOutput(
            typingDiagnosticInput.Handle,
            eventName,
            typingDiagnosticInput.Text,
            typingDiagnosticInput.SelectionStart,
            typingDiagnosticInput.SelectionLength);
        RefreshTypingDiagnosticLog();
    }

    private void RefreshTypingDiagnosticLog()
    {
        if (typingDiagnosticLog.IsDisposed)
        {
            return;
        }

        var snapshot = TypingDiagnosticTrace.FormatSnapshot(GetTypingDiagnosticFilter());
        if (string.Equals(typingDiagnosticLog.Text, snapshot, StringComparison.Ordinal))
        {
            return;
        }

        typingDiagnosticLog.Text = snapshot;
        if (typingDiagnosticLog.TextLength > 0)
        {
            typingDiagnosticLog.SelectionStart = typingDiagnosticLog.TextLength;
            typingDiagnosticLog.SelectionLength = 0;
            typingDiagnosticLog.ScrollToCaret();
        }
    }

    private TypingDiagnosticTraceKind? GetTypingDiagnosticFilter() =>
        typingDiagnosticFilter.SelectedIndex switch
        {
            0 => null,
            1 => TypingDiagnosticTraceKind.Physical,
            2 => TypingDiagnosticTraceKind.Engine,
            3 => TypingDiagnosticTraceKind.Output,
            4 => TypingDiagnosticTraceKind.Anomaly,
            _ => null,
        };

    private void ClearTypingDiagnosticLog()
    {
        TypingDiagnosticTrace.Clear();
        typingDiagnosticLog.Clear();
        typingDiagnosticStatus.Text = TypingDiagnosticTrace.IsEnabled
            ? "Đang ghi — log vừa được xóa."
            : "Tạm dừng — log đã được xóa.";
        typingDiagnosticStatus.ForeColor = TypingDiagnosticTrace.IsEnabled
            ? palette.Success
            : palette.TextSecondary;
    }

    private void CopyTypingDiagnosticLog()
    {
        var text = TypingDiagnosticTrace.FormatSnapshot(GetTypingDiagnosticFilter());
        if (text.Length == 0)
        {
            typingDiagnosticStatus.Text = "Chưa có sự kiện để sao chép.";
            typingDiagnosticStatus.ForeColor = palette.Warning;
            return;
        }

        try
        {
            Clipboard.SetText(text, TextDataFormat.UnicodeText);
            typingDiagnosticStatus.Text = "Đã sao chép log đang hiển thị.";
            typingDiagnosticStatus.ForeColor = palette.Success;
        }
        catch (ExternalException)
        {
            typingDiagnosticStatus.Text = "Clipboard đang bận; hãy thử lại.";
            typingDiagnosticStatus.ForeColor = palette.Error;
        }
    }

    private void ExportTypingDiagnosticLog()
    {
        var text = TypingDiagnosticTrace.FormatSnapshot(GetTypingDiagnosticFilter());
        if (text.Length == 0)
        {
            typingDiagnosticStatus.Text = "Chưa có sự kiện để xuất.";
            typingDiagnosticStatus.ForeColor = palette.Warning;
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Xuất log chẩn đoán bộ gõ",
            Filter = "Keyina typing log (*.log)|*.log|Text file (*.txt)|*.txt",
            DefaultExt = "log",
            AddExtension = true,
            FileName = $"keyina-typing-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllText(
                dialog.FileName,
                text,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            typingDiagnosticStatus.Text = "Đã xuất log vào file đã chọn.";
            typingDiagnosticStatus.ForeColor = palette.Success;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException)
        {
            typingDiagnosticStatus.Text = "Không thể xuất log; hãy chọn vị trí khác.";
            typingDiagnosticStatus.ForeColor = palette.Error;
        }
    }

    private static string FormatDiagnosticCharacter(char character) => character switch
    {
        '\r' => "\\r",
        '\n' => "\\n",
        '\t' => "\\t",
        _ => character.ToString(),
    };
}
