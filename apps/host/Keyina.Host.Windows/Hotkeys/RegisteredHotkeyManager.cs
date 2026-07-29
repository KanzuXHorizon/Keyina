using System.ComponentModel;
using System.Runtime.InteropServices;
using Keyina.Host.Core.Hotkeys;

namespace Keyina.Host.Windows.Hotkeys;

public sealed record RegisteredHotkeyBinding(
    int Id,
    HotkeyChord Chord,
    HotkeyCommand Command);

public sealed class HotkeyRegistrationException : Exception
{
    public HotkeyRegistrationException(
        int hotkeyId,
        HotkeyChord chord,
        int nativeErrorCode)
        : base($"Windows rejected hotkey {chord} (id {hotkeyId}, error {nativeErrorCode}).")
    {
        HotkeyId = hotkeyId;
        Chord = chord;
        NativeErrorCode = nativeErrorCode;
    }

    public int HotkeyId { get; }

    public HotkeyChord Chord { get; }

    public int NativeErrorCode { get; }
}

public interface IRegisteredHotkeyNativeApi
{
    bool Register(
        nint windowHandle,
        int id,
        uint modifiers,
        uint virtualKey,
        out int errorCode);

    bool Unregister(nint windowHandle, int id, out int errorCode);
}

public sealed class RegisteredHotkeyManager : IDisposable
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWindows = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly IRegisteredHotkeyNativeApi nativeApi;
    private readonly nint windowHandle;
    private readonly Dictionary<int, RegisteredHotkeyBinding> registered = [];
    private bool disposed;

    public RegisteredHotkeyManager(
        IRegisteredHotkeyNativeApi? nativeApi,
        nint windowHandle)
    {
        this.nativeApi = nativeApi ?? new Win32RegisteredHotkeyNativeApi();
        ArgumentOutOfRangeException.ThrowIfZero(windowHandle);
        this.windowHandle = windowHandle;
    }

    public event EventHandler<HotkeyCommand>? CommandReceived;

    public int RegisteredCount => registered.Count;

    public void Register(IReadOnlyList<RegisteredHotkeyBinding> bindings)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(bindings);
        if (registered.Count != 0)
        {
            throw new InvalidOperationException("Hotkeys are already registered.");
        }

        Validate(bindings);
        var successfulIds = new List<int>(bindings.Count);
        try
        {
            foreach (var binding in bindings)
            {
                var nativeModifiers = ToNativeModifiers(binding.Chord.Modifiers);
                if (!nativeApi.Register(
                        windowHandle,
                        binding.Id,
                        nativeModifiers,
                        (uint)binding.Chord.Key,
                        out var errorCode))
                {
                    throw new HotkeyRegistrationException(
                        binding.Id,
                        binding.Chord,
                        errorCode);
                }

                registered.Add(binding.Id, binding);
                successfulIds.Add(binding.Id);
            }
        }
        catch
        {
            for (var index = successfulIds.Count - 1; index >= 0; index--)
            {
                _ = nativeApi.Unregister(windowHandle, successfulIds[index], out _);
                registered.Remove(successfulIds[index]);
            }
            throw;
        }
    }

    public bool TryDispatch(int hotkeyId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!registered.TryGetValue(hotkeyId, out var binding))
        {
            return false;
        }

        CommandReceived?.Invoke(this, binding.Command);
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var id in registered.Keys.OrderDescending().ToArray())
        {
            _ = nativeApi.Unregister(windowHandle, id, out _);
        }
        registered.Clear();
    }

    private static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        uint nativeModifiers = ModNoRepeat;
        if ((modifiers & HotkeyModifiers.Alt) != 0)
        {
            nativeModifiers |= ModAlt;
        }
        if ((modifiers & HotkeyModifiers.Control) != 0)
        {
            nativeModifiers |= ModControl;
        }
        if ((modifiers & HotkeyModifiers.Shift) != 0)
        {
            nativeModifiers |= ModShift;
        }
        if ((modifiers & HotkeyModifiers.Windows) != 0)
        {
            nativeModifiers |= ModWindows;
        }
        return nativeModifiers;
    }

    private static void Validate(IReadOnlyList<RegisteredHotkeyBinding> bindings)
    {
        var ids = new HashSet<int>();
        var chords = new HashSet<HotkeyChord>();
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding);
            if (binding.Id <= 0 || !ids.Add(binding.Id))
            {
                throw new ArgumentException("Registered hotkey IDs must be unique positive integers.", nameof(bindings));
            }
            if (binding.Command == HotkeyCommand.None)
            {
                throw new ArgumentException("Registered hotkeys must produce a command.", nameof(bindings));
            }
            if (binding.Chord.IsModifierOnly)
            {
                throw new ArgumentException("RegisterHotKey requires a non-modifier key.", nameof(bindings));
            }
            if (!chords.Add(binding.Chord))
            {
                throw new ArgumentException("Registered hotkey chords must be unique.", nameof(bindings));
            }
        }
    }

    private sealed class Win32RegisteredHotkeyNativeApi : IRegisteredHotkeyNativeApi
    {
        public bool Register(
            nint windowHandle,
            int id,
            uint modifiers,
            uint virtualKey,
            out int errorCode)
        {
            var result = RegisterHotKey(windowHandle, id, modifiers, virtualKey);
            errorCode = result ? 0 : Marshal.GetLastWin32Error();
            return result;
        }

        public bool Unregister(nint windowHandle, int id, out int errorCode)
        {
            var result = UnregisterHotKey(windowHandle, id);
            errorCode = result ? 0 : Marshal.GetLastWin32Error();
            return result;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(
            nint windowHandle,
            int id,
            uint modifiers,
            uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(nint windowHandle, int id);
    }
}
