using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Tests;

internal static class LiveKeyboardHookIntegrationTests
{
    [KeyinaTest("live Windows hook types Vietnamese into a focused textbox without TSF")]
    private static void LiveHookTypesIntoFocusedTextbox()
    {
        using var form = new Form
        {
            Text = $"Keyina Hook Test {Guid.NewGuid():N}",
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-1200, 100),
            Size = new Size(480, 180),
            ShowInTaskbar = false,
        };
        using var textBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
        };
        form.Controls.Add(textBox);
        form.Show();
        form.Activate();
        textBox.Select();
        Application.DoEvents();
        AssertEx.True(
            form.ContainsFocus,
            "The live hook test window must own keyboard focus before sending input.");

        using var hook = new VietnameseKeyboardHook();
        hook.Start(enabledInitially: true);

        SendAscii("tieengs vieetj");
        PumpUntil(
            () => string.Equals(textBox.Text, "tiếng việt", StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));

        AssertEx.Equal("tiếng việt", textBox.Text);

        textBox.SelectAll();
        hook.Reset();
        SendAscii("as");
        PumpUntil(
            () => string.Equals(textBox.Text, "á", StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));
        AssertEx.Equal("á", textBox.Text);

        textBox.Clear();
        hook.Reset();
        var burstCases = new[]
        {
            (Raw: "truocws dduocwj nuawx tieengs vieetj vaanx vowis mootj motoj ", Expected: "trước được nữa tiếng việt vẫn với một một "),
            (Raw: "truowcs dduowcj nuwxa tieesng vieejt vaanx voisw motoj mootj ", Expected: "trước được nữa tiếng việt vẫn với một một "),
            (Raw: "truwocs dduwocj nuawx tieengs vieejt vaanx vowis mootj motoj ", Expected: "trước được nữa tiếng việt vẫn với một một "),
            (Raw: "truowsc dduowjc nuwxa tieesng vieetj vaanx voisw motoj mootj ", Expected: "trước được nữa tiếng việt vẫn với một một "),
            (Raw: "truocsw dduocwj nuawx tieengs vieejt vaanx vowis mootj motoj ", Expected: "trước được nữa tiếng việt vẫn với một một "),
        };
        var expected = new StringBuilder();
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var testCase = burstCases[iteration % burstCases.Length];
            SendAscii(testCase.Raw, pumpEachKey: false);
            expected.Append(testCase.Expected);
        }
        PumpUntil(
            () => textBox.Text.Length >= expected.Length,
            TimeSpan.FromSeconds(5));
        AssertEx.Equal(expected.ToString(), textBox.Text);

        textBox.Text = "nội dung cũ cần thay";
        textBox.SelectAll();
        hook.Reset();
        SetClipboardText("đoạn văn được dán nguyên vẹn");
        var pasteTimer = Stopwatch.StartNew();
        SendControlV();
        PumpUntil(
            () => string.Equals(
                textBox.Text,
                "đoạn văn được dán nguyên vẹn",
                StringComparison.Ordinal),
            TimeSpan.FromSeconds(2));
        pasteTimer.Stop();
        AssertEx.Equal("đoạn văn được dán nguyên vẹn", textBox.Text);
        AssertEx.True(
            pasteTimer.Elapsed < TimeSpan.FromSeconds(1),
            $"Ctrl+V took {pasteTimer.Elapsed.TotalMilliseconds:F0} ms.");

        hook.Dispose();
        form.Close();
        Application.DoEvents();
    }

    private static void SetClipboardText(string value)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(value);
                return;
            }
            catch (ExternalException) when (attempt < 4)
            {
                Thread.Sleep(20);
            }
        }
        throw new InvalidOperationException("Could not acquire the Windows clipboard.");
    }

    private static void SendControlV()
    {
        const byte control = 0x11;
        const byte v = 0x56;
        const uint keyEventKeyUp = 0x0002;
        keybd_event(control, 0, 0, 0);
        keybd_event(v, 0, 0, 0);
        keybd_event(v, 0, keyEventKeyUp, 0);
        keybd_event(control, 0, keyEventKeyUp, 0);
    }

    private static void SendAscii(string text, bool pumpEachKey = true)
    {
        var queuedSincePump = 0;
        foreach (var character in text)
        {
            if (character == ' ')
            {
                SendVirtualKey(0x20, pumpEachKey);
            }
            else
            {
                var virtualKey = checked((ushort)char.ToUpperInvariant(character));
                SendVirtualKey(virtualKey, pumpEachKey);
            }

            if (!pumpEachKey && ++queuedSincePump >= 8)
            {
                Application.DoEvents();
                queuedSincePump = 0;
            }
        }

        if (!pumpEachKey)
        {
            Application.DoEvents();
        }
    }

    private static void SendVirtualKey(ushort virtualKey, bool pumpEachKey = true)
    {
        const uint keyEventKeyUp = 0x0002;
        keybd_event(checked((byte)virtualKey), 0, 0, 0);
        keybd_event(checked((byte)virtualKey), 0, keyEventKeyUp, 0);
        if (pumpEachKey)
        {
            Application.DoEvents();
        }
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(
        byte virtualKey,
        byte scanCode,
        uint flags,
        nuint extraInfo);
}
