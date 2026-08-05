using System.Reflection;
using Keyina.Host.UI;
using Keyina.Host.UI.Fluent;

namespace Keyina.Host.Tests;

internal static class SettingsResponsiveTests
{
    [KeyinaTest("settings shell adapts across narrow compact and expanded widths")]
    private static void SettingsShellAdaptsAcrossSupportedWidths()
    {
        using var form = CreateHiddenForm();

        form.Size = new Size(760, 620);
        Application.DoEvents();
        AssertEx.Equal(SettingsLayoutMode.Narrow, form.CurrentLayoutMode);
        AssertEx.Equal(760, form.MinimumSize.Width);
        AssertEx.Equal(620, form.MinimumSize.Height);
        AssertEx.True(
            form.Controls.Find("sidebar", true).Single().Width <= 80,
            "Narrow mode did not collapse the sidebar to an icon rail.");
        AssertEx.False(
            form.Controls.Find("systemThemeStatus", true).Single().Visible,
            "Narrow mode retained non-essential theme metadata.");
        foreach (var navigation in FindDescendants<FluentNavigationButton>(form))
        {
            AssertEx.True(navigation.Compact,
                $"{navigation.Name} did not switch to compact icon-only painting.");
            AssertEx.True(!string.IsNullOrWhiteSpace(navigation.AccessibleName),
                $"{navigation.Name} lost its accessible name in narrow mode.");
        }

        form.Size = new Size(900, 680);
        Application.DoEvents();
        AssertEx.Equal(SettingsLayoutMode.Compact, form.CurrentLayoutMode);
        var shell = (TableLayoutPanel)form.Controls.Find("settingsShell", true).Single();
        AssertEx.Equal(196F, shell.ColumnStyles[0].Width);
        AssertEx.True(form.Controls.Find("systemThemeStatus", true).Single().Visible,
            "Compact mode should retain theme metadata.");
        AssertEx.True(
            FindDescendants<FluentNavigationButton>(form).All(button => !button.Compact),
            "Compact mode unexpectedly hid navigation labels.");

        form.Size = new Size(1100, 760);
        Application.DoEvents();
        AssertEx.Equal(SettingsLayoutMode.Expanded, form.CurrentLayoutMode);
        AssertEx.Equal(228F, shell.ColumnStyles[0].Width);
    }

    [KeyinaTest("settings navigation supports arrows home end and focus transfer")]
    private static void SettingsNavigationIsKeyboardOperable()
    {
        using var form = CreateHiddenForm(new Size(760, 620));
        var overview = (FluentNavigationButton)form.Controls.Find("navOverview", true).Single();
        var typing = (FluentNavigationButton)form.Controls.Find("navTyping", true).Single();
        var diagnostics = (FluentNavigationButton)form.Controls.Find("navDiagnostics", true).Single();
        overview.Select();
        _ = overview.Focus();
        AssertEx.Equal(AccessibleRole.PageTab, overview.AccessibleRole);
        AssertEx.True(
            overview.AccessibleDescription?.Contains(
                "đang chọn",
                StringComparison.OrdinalIgnoreCase) == true,
            "Selected navigation item did not expose its state to accessibility clients.");

        var down = InvokeKeyDown(overview, Keys.Down);
        Application.DoEvents();
        AssertEx.True(typing.Selected && typing.Focused,
            "Down did not select and focus the next Settings section.");
        AssertEx.True(
            typing.AccessibleDescription?.Contains(
                "đang chọn",
                StringComparison.OrdinalIgnoreCase) == true,
            "Keyboard navigation did not announce the newly selected section.");
        AssertEx.False(
            overview.AccessibleDescription?.Contains(
                "đang chọn",
                StringComparison.OrdinalIgnoreCase) == true,
            "Previously selected navigation item kept a stale selected announcement.");
        AssertEx.True(down.Handled && down.SuppressKeyPress,
            "Handled navigation key was not suppressed.");

        _ = InvokeKeyDown(typing, Keys.End);
        Application.DoEvents();
        AssertEx.True(diagnostics.Selected && diagnostics.Focused,
            "End did not select and focus the last Settings section.");

        _ = InvokeKeyDown(diagnostics, Keys.Home);
        Application.DoEvents();
        AssertEx.True(overview.Selected && overview.Focused,
            "Home did not return to the first Settings section.");

        form.OpenSection("speech");
        Application.DoEvents();
        var speechPage = form.Controls.Find("speechPage", true).Single();
        AssertEx.True(
            FindDescendants<Control>(speechPage).Any(control => control.TabStop && control.Focused),
            "Opening a section did not focus its first interactive control.");
    }

    [KeyinaTest("narrow settings keeps every page readable without horizontal overflow")]
    private static void NarrowSettingsAvoidsHorizontalOverflow()
    {
        using var form = CreateHiddenForm(new Size(760, 620));
        foreach (var section in new[]
                 {
                     "overview",
                     "typing",
                     "speech",
                     "translation",
                     "hotkeys",
                     "applications",
                     "snippets",
                     "diagnostics",
                 })
        {
            form.OpenSection(section);
            Application.DoEvents();
            form.PerformLayout();

            var page = form.Controls.Find($"{section}Page", true).Single();
            foreach (var stack in FindDescendants<FlowLayoutPanel>(page)
                         .Where(panel => panel.AutoScroll))
            {
                AssertEx.False(stack.HorizontalScroll.Visible,
                    $"{section} exposed a horizontal scrollbar at 760 px.");
            }
            foreach (var card in FindDescendants<FluentCard>(page))
            {
                AssertEx.Equal(16, card.Padding.Left,
                    $"{card.Name} did not use narrow card density.");
                var expectedMargin = card.Name.StartsWith(
                    "snippet_",
                    StringComparison.Ordinal)
                    ? 6
                    : 10;
                AssertEx.Equal(expectedMargin, card.Margin.Bottom,
                    $"{card.Name} did not use narrow card spacing.");
            }
        }

        form.Size = new Size(1100, 760);
        Application.DoEvents();
        form.OpenSection("overview");
        Application.DoEvents();
        foreach (var card in FindDescendants<FluentCard>(
                     form.Controls.Find("overviewPage", true).Single()))
        {
            AssertEx.Equal(20, card.Padding.Left,
                $"{card.Name} did not restore expanded card density.");
            AssertEx.Equal(12, card.Margin.Bottom,
                $"{card.Name} did not restore expanded card spacing.");
        }
    }

    [KeyinaTest("settings screenshot renderer supports responsive gallery sizes")]
    private static void ScreenshotRendererSupportsResponsiveGallerySizes()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"Keyina.Responsive.Gallery.{Guid.NewGuid():N}");
        try
        {
            var paths = SettingsScreenshotRenderer.RenderGallery(
                directory,
                SettingsSnapshot.Sample,
                new Size(760, 620),
                "narrow-");

            AssertEx.Equal(8, paths.Count);
            AssertEx.True(
                paths.All(path => Path.GetFileName(path).StartsWith(
                    "narrow-",
                    StringComparison.Ordinal)),
                "Responsive gallery prefix was not applied.");
            foreach (var path in paths)
            {
                using var image = Image.FromFile(path);
                AssertEx.Equal(760, image.Width);
                AssertEx.Equal(620, image.Height);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static SettingsForm CreateHiddenForm(Size? size = null)
    {
        var form = new SettingsForm(SettingsSnapshot.Sample, SettingsActions.NoOp)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-10_000, -10_000),
            Opacity = 0,
            Size = size ?? new Size(1100, 760),
        };
        form.Show();
        Application.DoEvents();
        return form;
    }

    private static KeyEventArgs InvokeKeyDown(Control control, Keys key)
    {
        var onKeyDown = control.GetType().GetMethod(
            "OnKeyDown",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Control key handler could not be invoked.");
        var eventArgs = new KeyEventArgs(key);
        _ = onKeyDown.Invoke(control, [eventArgs]);
        return eventArgs;
    }

    private static List<TControl> FindDescendants<TControl>(Control root)
        where TControl : Control
    {
        var results = new List<TControl>();
        foreach (Control child in root.Controls)
        {
            if (child is TControl typed)
            {
                results.Add(typed);
            }
            results.AddRange(FindDescendants<TControl>(child));
        }
        return results;
    }
}
