using Keyina.Host.Core.Applications;

namespace Keyina.Host.UI;

public sealed partial class SettingsForm
{
    private TextBox[] GetApplicationRuleTextBoxes() =>
    [
        disableVietnameseApplications,
        disableSpeechApplications,
        disableTranslationApplications,
        suppressVisualFeedbackApplications,
    ];

    private void UpdateApplicationRulesDisplay(ApplicationPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var normalized = preferences.Normalize();
        disableVietnameseApplications.Lines = normalized.DisableVietnamese;
        disableSpeechApplications.Lines = normalized.DisableSpeech;
        disableTranslationApplications.Lines = normalized.DisableTranslation;
        suppressVisualFeedbackApplications.Lines = normalized.SuppressVisualFeedback;
        applicationRulesStatus.Text = "Mỗi dòng là một tên file .exe, ví dụ game.exe.";
        applicationRulesStatus.ForeColor = palette.TextTertiary;
    }

    private void SaveApplicationPreferences()
    {
        try
        {
            var preferences = new ApplicationPreferences(
                ParseApplicationRules(disableVietnameseApplications),
                ParseApplicationRules(disableSpeechApplications),
                ParseApplicationRules(disableTranslationApplications),
                ParseApplicationRules(suppressVisualFeedbackApplications))
                .Normalize();
            applicationRulesDirty = false;
            UpdateApplicationRulesDisplay(preferences);
            actions.SetApplicationPreferences(preferences);
            applicationRulesStatus.Text = "Đã kiểm tra và lưu quy tắc ứng dụng.";
            applicationRulesStatus.ForeColor = palette.Success;
        }
        catch (ArgumentException exception)
        {
            applicationRulesDirty = true;
            applicationRulesStatus.Text = LocalizeApplicationRuleError(exception.Message);
            applicationRulesStatus.ForeColor = palette.Error;
        }
    }

    private void AddForegroundApplication(TextBox target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var executableName = actions.GetForegroundApplicationName();
        if (string.IsNullOrWhiteSpace(executableName))
        {
            applicationRulesStatus.Text =
                "Không xác định được ứng dụng trước khi mở Cài đặt. Hãy nhập tên file .exe thủ công.";
            applicationRulesStatus.ForeColor = palette.Warning;
            return;
        }

        try
        {
            var normalized = ApplicationPreferences.NormalizeExecutableName(executableName);
            var existing = ParseApplicationRules(target);
            if (existing.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                applicationRulesStatus.Text = $"{normalized} đã có trong danh sách.";
                applicationRulesStatus.ForeColor = palette.Warning;
                return;
            }

            target.Lines = existing.Append(normalized).ToArray();
            applicationRulesDirty = true;
            applicationRulesStatus.Text = $"Đã thêm {normalized}; nhấn Lưu quy tắc để áp dụng.";
            applicationRulesStatus.ForeColor = palette.Warning;
        }
        catch (ArgumentException)
        {
            applicationRulesStatus.Text =
                "Ứng dụng hiện tại không cung cấp tên file .exe hợp lệ.";
            applicationRulesStatus.ForeColor = palette.Error;
        }
    }

    private static string[] ParseApplicationRules(TextBox textBox) =>
        textBox.Lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

    private static string LocalizeApplicationRuleError(string message)
    {
        if (message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
        {
            return "Danh sách có tên ứng dụng bị trùng.";
        }

        if (message.Contains("path", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("wildcard", StringComparison.OrdinalIgnoreCase))
        {
            return "Chỉ nhập tên file .exe, không nhập đường dẫn hoặc wildcard.";
        }

        if (message.Contains(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return "Mỗi dòng phải là một tên file Windows kết thúc bằng .exe.";
        }

        return "Quy tắc ứng dụng không hợp lệ. Hãy kiểm tra từng dòng.";
    }
}
