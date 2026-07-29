using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Keyina.Host.Windows.Hotkeys;

public sealed class HotkeyMessageWindow : IDisposable
{
    private const int WmClose = 0x0010;
    private const int WmDestroy = 0x0002;
    private const int WmHotkey = 0x0312;
    private const int WmInvoke = 0x804B;
    private const int ErrorClassAlreadyExists = 1410;
    private const string WindowClassName = "Keyina.Host.HotkeyMessageWindow.v1";
    private static readonly nint HwndMessage = new(-3);
    private static readonly WindowProcedureDelegate WindowProcedureInstance = WindowProcedure;
    private static readonly ConcurrentDictionary<nint, HotkeyMessageWindow> Windows = new();

    private readonly ManualResetEventSlim ready = new();
    private readonly ConcurrentQueue<Action> pendingInvocations = new();
    private readonly Thread thread;
    private nint handle;
    private uint ownerThreadId;
    private Exception? startupFailure;
    private bool disposed;

    public HotkeyMessageWindow()
    {
        thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "Keyina hotkey message window",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!ready.Wait(TimeSpan.FromSeconds(5)))
        {
            disposed = true;
            throw new TimeoutException("Keyina hotkey message window did not initialize in time.");
        }

        if (startupFailure is not null)
        {
            disposed = true;
            throw new InvalidOperationException(
                "Keyina hotkey message window failed to initialize.",
                startupFailure);
        }
    }

    public event EventHandler<int>? HotkeyReceived;

    public event EventHandler<Exception>? DispatchFailed;

    public nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return handle;
        }
    }

    public T Invoke<T>(Func<T> action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(action);
        if (GetCurrentThreadId() == ownerThreadId)
        {
            return action();
        }

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        pendingInvocations.Enqueue(() =>
        {
            try
            {
                completion.TrySetResult(action());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        if (!PostMessageW(handle, WmInvoke, 0, 0))
        {
            completion.TrySetException(new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not dispatch work to the Keyina hotkey window thread."));
        }

        return completion.Task
            .WaitAsync(TimeSpan.FromSeconds(5))
            .GetAwaiter()
            .GetResult();
    }

    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = Invoke(() =>
        {
            action();
            return true;
        });
    }

    public bool PostHotkeyForTest(int hotkeyId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return PostMessageW(handle, WmHotkey, hotkeyId, 0);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        var currentHandle = handle;
        if (currentHandle != 0)
        {
            _ = PostMessageW(currentHandle, WmClose, 0, 0);
        }

        if (!thread.Join(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Keyina hotkey message window did not stop in time.");
        }

        ready.Dispose();
    }

    private void ThreadMain()
    {
        try
        {
            var module = GetModuleHandleW(null);
            var windowClass = new WindowClass
            {
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureInstance),
                Instance = module,
                ClassName = WindowClassName,
            };
            var atom = RegisterClassW(ref windowClass);
            if (atom == 0 && Marshal.GetLastWin32Error() != ErrorClassAlreadyExists)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Windows rejected the Keyina hotkey window class.");
            }

            var createdHandle = CreateWindowExW(
                0,
                WindowClassName,
                string.Empty,
                0,
                0,
                0,
                0,
                0,
                HwndMessage,
                0,
                module,
                0);
            if (createdHandle == 0)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Windows could not create the Keyina hotkey message window.");
            }

            handle = createdHandle;
            ownerThreadId = GetCurrentThreadId();
            Windows[createdHandle] = this;
            ready.Set();

            while (true)
            {
                var result = GetMessageW(out var message, 0, 0, 0);
                if (result == 0)
                {
                    break;
                }
                if (result == -1)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Keyina hotkey message loop failed.");
                }

                _ = TranslateMessage(ref message);
                _ = DispatchMessageW(ref message);
            }
        }
        catch (Exception exception)
        {
            startupFailure = exception;
            ready.Set();
        }
        finally
        {
            var currentHandle = handle;
            if (currentHandle != 0)
            {
                Windows.TryRemove(currentHandle, out _);
                handle = 0;
            }
        }
    }

    private static nint WindowProcedure(
        nint windowHandle,
        uint message,
        nint wordParameter,
        nint longParameter)
    {
        if (message == WmInvoke &&
            Windows.TryGetValue(windowHandle, out var dispatchWindow))
        {
            dispatchWindow.DrainInvocations();
            return 0;
        }

        if (message == WmHotkey &&
            Windows.TryGetValue(windowHandle, out var window))
        {
            try
            {
                window.HotkeyReceived?.Invoke(window, unchecked((int)wordParameter));
            }
            catch (Exception exception)
            {
                try
                {
                    window.DispatchFailed?.Invoke(window, exception);
                }
                catch
                {
                    // Never unwind through a native window procedure.
                }
            }
            return 0;
        }

        if (message == WmClose)
        {
            _ = DestroyWindow(windowHandle);
            return 0;
        }

        if (message == WmDestroy)
        {
            PostQuitMessage(0);
            return 0;
        }

        return DefWindowProcW(windowHandle, message, wordParameter, longParameter);
    }

    private void DrainInvocations()
    {
        while (pendingInvocations.TryDequeue(out var invocation))
        {
            invocation();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public nint WindowProcedure;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint BackgroundBrush;
        public string? MenuName;
        public string ClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint WindowHandle;
        public uint Message;
        public nuint WordParameter;
        public nint LongParameter;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private delegate nint WindowProcedureDelegate(
        nint windowHandle,
        uint message,
        nint wordParameter,
        nint longParameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(
        nint windowHandle,
        uint message,
        nint wordParameter,
        nint longParameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessageW(
        out NativeMessage message,
        nint windowHandle,
        uint minimumMessage,
        uint maximumMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessageW(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(
        nint windowHandle,
        uint message,
        nint wordParameter,
        nint longParameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
