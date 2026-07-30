using System.Drawing.Imaging;

namespace Keyina.Host.UI;

public static class SettingsScreenshotRenderer
{
    public static void Render(string path, SettingsSnapshot snapshot) =>
        RenderSection(path, snapshot, "navOverview");

    public static IReadOnlyList<string> RenderGallery(
        string directory,
        SettingsSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException("Gallery directory must be fully qualified.", nameof(directory));
        }

        Directory.CreateDirectory(directory);
        var sections = new (string FileName, string NavigationName)[]
        {
            ("overview.png", "navOverview"),
            ("typing.png", "navTyping"),
            ("speech.png", "navSpeech"),
            ("translation.png", "navTranslation"),
            ("hotkeys.png", "navHotkeys"),
            ("snippets.png", "navSnippets"),
            ("diagnostics.png", "navDiagnostics"),
        };
        var paths = new List<string>(sections.Length);
        foreach (var (fileName, navigationName) in sections)
        {
            var path = Path.Combine(directory, fileName);
            RenderSection(path, snapshot, navigationName);
            paths.Add(path);
        }
        return paths;
    }

    private static void RenderSection(
        string path,
        SettingsSnapshot snapshot,
        string navigationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "Screenshot path must be fully qualified.",
                nameof(path));
        }

        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException(
                "Screenshot path must have a parent directory.",
                nameof(path));
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";

        using var form = new SettingsForm(snapshot, SettingsActions.NoOp)
        {
            ClientSize = new Size(980, 690),
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32_000, -32_000),
        };
        form.Show();
        var navigation = form.Controls.Find(navigationName, searchAllChildren: true)
            .OfType<Button>()
            .Single();
        navigation.PerformClick();
        form.PerformLayout();
        form.Refresh();
        Application.DoEvents();
        using var bitmap = new Bitmap(
            form.ClientSize.Width,
            form.ClientSize.Height,
            PixelFormat.Format32bppPArgb);
        form.DrawToBitmap(
            bitmap,
            new Rectangle(Point.Empty, form.ClientSize));

        try
        {
            bitmap.Save(temporaryPath, ImageFormat.Png);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
