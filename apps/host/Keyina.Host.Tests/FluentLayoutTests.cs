using Keyina.Host.UI.Fluent;

namespace Keyina.Host.Tests;

[KeyinaInteractiveTest]
internal static class FluentLayoutTests
{
    [KeyinaTest("Fluent layout tokens expose the approved semantic scale")]
    private static void SemanticTokensMatchDesignSpec()
    {
        AssertEx.Equal(4, FluentSpacing.Micro);
        AssertEx.Equal(8, FluentSpacing.Compact);
        AssertEx.Equal(12, FluentSpacing.Standard);
        AssertEx.Equal(16, FluentSpacing.Control);
        AssertEx.Equal(24, FluentSpacing.Section);
        AssertEx.Equal(32, FluentSpacing.Page);
        AssertEx.Equal(32, FluentControlMetrics.CompactHeight);
        AssertEx.Equal(36, FluentControlMetrics.DefaultHeight);
        AssertEx.Equal(40, FluentControlMetrics.ProminentHeight);
    }

    [KeyinaTest("setting row exposes task copy and keeps its action keyboard reachable")]
    private static void SettingRowIsAccessibleAndActionOriented()
    {
        using var action = new Button { Text = "Bật", TabStop = true };
        using var row = new FluentSettingRow
        {
            Title = "Bộ gõ tiếng Việt",
            Description = "Bật hoặc tắt xử lý tiếng Việt trên toàn hệ thống.",
            Action = action,
        };

        AssertEx.Equal("Bộ gõ tiếng Việt", row.AccessibleName);
        AssertEx.True(
            row.AccessibleDescription?.Contains("toàn hệ thống", StringComparison.Ordinal) == true,
            "Setting description was not exposed to accessibility clients.");
        AssertEx.True(action.TabStop, "Setting action must remain keyboard reachable.");
        AssertEx.Equal(1, row.Controls.Find("settingRowTitle", true).Length);
        AssertEx.Equal(1, row.Controls.Find("settingRowDescription", true).Length);
    }

    [KeyinaTest("inline messages never communicate severity by color alone")]
    private static void InlineMessageIncludesSeverityText()
    {
        using var message = new FluentInlineMessage();
        message.SetMessage("Không thể kết nối dịch vụ.", FluentInlineMessageKind.Error);

        AssertEx.True(message.Visible, "Non-empty inline message should be visible.");
        AssertEx.True(
            message.AccessibleName?.StartsWith("Lỗi:", StringComparison.Ordinal) == true,
            "Error severity was not included in accessible text.");

        message.SetMessage("Endpoint mạng riêng có thể làm lộ dữ liệu.", FluentInlineMessageKind.Warning);
        AssertEx.True(
            message.AccessibleName?.StartsWith("Cảnh báo:", StringComparison.Ordinal) == true,
            "Warning severity was not included in accessible text.");
    }

    [KeyinaTest("section headers hide empty supporting copy")]
    private static void SectionHeaderUsesProgressiveDisclosure()
    {
        using var header = new FluentSectionHeader
        {
            Title = "Dịch nhanh",
            Description = string.Empty,
        };

        var description = (Label)header.Controls.Find("sectionHeaderDescription", true).Single();
        AssertEx.False(description.Visible, "Empty section description should not reserve visual space.");
        AssertEx.Equal("Dịch nhanh", header.AccessibleName);
    }
}
