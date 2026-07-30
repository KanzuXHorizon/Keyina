using System.Runtime.InteropServices;
using System.Text;

namespace Keyina.Host.Windows.Typing;

public enum NativeEngineKeyKind : uint
{
    Character = 0,
    Backspace = 1,
    CommitBoundary = 2,
    Reset = 3,
}

public interface IVietnameseEngine : IDisposable
{
    HookEdit Process(
        NativeEngineKeyKind kind,
        Rune character = default,
        bool shift = false,
        bool control = false,
        bool alt = false);

    void Configure(
        bool traditionalTonePlacement = false,
        bool applicationBypass = false,
        bool restoreInvalidWord = false);

    void Reset();
}

public sealed class NativeEngineClient : IVietnameseEngine
{
    private const int SingleCharacterCacheLength = 0x2000;
    private static readonly string?[] SingleCharacterCache =
        new string[SingleCharacterCacheLength];

    private readonly nint library;
    private readonly EngineDestroy destroy;
    private readonly EngineReset reset;
    private readonly EngineConfigure configure;
    private readonly EngineProcess process;
    private nint handle;
    private bool disposed;

    public NativeEngineClient(string? libraryPath = null)
    {
        libraryPath ??= FindLibrary();
        library = NativeLibrary.Load(libraryPath);
        var create = GetDelegate<EngineCreate>("keyina_engine_create");
        destroy = GetDelegate<EngineDestroy>("keyina_engine_destroy");
        reset = GetDelegate<EngineReset>("keyina_engine_reset");
        configure = GetDelegate<EngineConfigure>("keyina_engine_configure");
        process = GetDelegate<EngineProcess>("keyina_engine_process");
        handle = create();
        if (handle == 0)
        {
            NativeLibrary.Free(library);
            throw new InvalidOperationException("Keyina native engine could not be created.");
        }
    }

    public HookEdit Process(
        NativeEngineKeyKind kind,
        Rune character = default,
        bool shift = false,
        bool control = false,
        bool alt = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var result = default(NativeEditResult);
        Span<char> buffer = stackalloc char[128];
        unsafe
        {
            fixed (char* destination = buffer)
            {
                var status = process(
                    handle,
                    (uint)kind,
                    (uint)character.Value,
                    shift ? (byte)1 : (byte)0,
                    control ? (byte)1 : (byte)0,
                    alt ? (byte)1 : (byte)0,
                    destination,
                    (uint)buffer.Length,
                    ref result);
                if (status != 1)
                {
                    throw new InvalidOperationException(
                        $"Keyina native engine returned status {status}.");
                }
            }
        }

        var insertLength = checked((int)result.InsertUtf16Units);
        return new HookEdit(
            checked((int)result.EraseCodepoints),
            CreateInsertText(buffer, insertLength),
            result.Consumed != 0);
    }

    public void Configure(
        bool traditionalTonePlacement = false,
        bool applicationBypass = false,
        bool restoreInvalidWord = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var status = configure(
            handle,
            traditionalTonePlacement ? (byte)1 : (byte)0,
            applicationBypass ? (byte)1 : (byte)0,
            restoreInvalidWord ? (byte)1 : (byte)0);
        if (status != 1)
        {
            throw new InvalidOperationException(
                $"Keyina native engine configuration returned status {status}.");
        }
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        reset(handle);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        if (handle != 0)
        {
            destroy(handle);
            handle = 0;
        }
        NativeLibrary.Free(library);
    }

    private static string CreateInsertText(Span<char> buffer, int length)
    {
        if (length == 0)
        {
            return string.Empty;
        }
        if (length != 1 || buffer[0] >= SingleCharacterCacheLength)
        {
            return new string(buffer[..length]);
        }

        ref var slot = ref SingleCharacterCache[buffer[0]];
        var cached = Volatile.Read(ref slot);
        if (cached is not null)
        {
            return cached;
        }

        var created = new string(buffer[0], 1);
        return Interlocked.CompareExchange(ref slot, created, null) ?? created;
    }

    private T GetDelegate<T>(string name)
        where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    private static string FindLibrary()
    {
        var direct = Path.Combine(AppContext.BaseDirectory, "KeyinaEngine.dll");
        if (File.Exists(direct))
        {
            return direct;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
#if DEBUG
        string[] configurations = ["Debug", "Release"];
#else
        string[] configurations = ["Release", "Debug"];
#endif
        while (directory is not null)
        {
            foreach (var configuration in configurations)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "build",
                    configuration == "Release" ? "windows-msvc-release" : "windows-msvc-debug",
                    "platform",
                    "windows",
                    "hook",
                    configuration,
                    "KeyinaEngine.dll");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "KeyinaEngine.dll was not found. Build the native hook backend first.",
            direct);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeEditResult
    {
        public uint EraseCodepoints;
        public uint InsertUtf16Units;
        public byte Consumed;
        public byte CommitBefore;
        public ushort Reserved;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint EngineCreate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EngineDestroy(nint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EngineReset(nint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EngineConfigure(
        nint handle,
        byte traditionalTonePlacement,
        byte applicationBypass,
        byte restoreInvalidWord);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate int EngineProcess(
        nint handle,
        uint kind,
        uint character,
        byte shift,
        byte control,
        byte alt,
        char* insertBuffer,
        uint insertCapacity,
        ref NativeEditResult result);
}
