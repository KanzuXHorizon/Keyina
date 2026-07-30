using System.ComponentModel;
using System.Diagnostics;
using Keyina.Host.UI;
using Microsoft.Win32;

namespace Keyina.Host.Runtime;

public sealed class TsfSetupService
{
    internal const string Clsid = "{D66D2599-6B75-4AFF-95B3-476C310CDE70}";

    private readonly ITsfSetupPlatform platform;
    private readonly string nativeDllPath;

    public TsfSetupService()
        : this(new WindowsTsfSetupPlatform(), FindNativeDll())
    {
    }

    public TsfSetupService(ITsfSetupPlatform platform, string nativeDllPath)
    {
        this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeDllPath);
        if (!Path.IsPathFullyQualified(nativeDllPath))
        {
            throw new ArgumentException("TSF DLL path must be fully qualified.", nameof(nativeDllPath));
        }
        this.nativeDllPath = nativeDllPath;
    }

    public bool IsRegistered() =>
        platform.FileExists(nativeDllPath) &&
        platform.IsComRegistered() &&
        platform.IsProfileRegistered();

    public Task<TsfHealthResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dllPresent = platform.FileExists(nativeDllPath);
        var comRegistered = dllPresent && platform.IsComRegistered();
        var profileRegistered = dllPresent && platform.IsProfileRegistered();
        var state = !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
            ? TsfSetupState.Unavailable
            : !dllPresent || (!comRegistered && !profileRegistered)
                ? TsfSetupState.NotInstalled
                : comRegistered && profileRegistered
                    ? TsfSetupState.Ready
                    : TsfSetupState.NeedsRepair;
        return Task.FromResult(new TsfHealthResult(
            state,
            dllPresent,
            comRegistered,
            profileRegistered,
            state == TsfSetupState.Unavailable ? "unsupported_windows" : null));
    }

    public async Task<TsfRegistrationResult> RegisterAsync(CancellationToken cancellationToken)
    {
        var health = await CheckAsync(cancellationToken).ConfigureAwait(false);
        if (!health.NativeDllPresent)
        {
            return new TsfRegistrationResult(false, "Không tìm thấy KeyinaTsf.dll.", "native_dll_missing");
        }
        if (health.State == TsfSetupState.Ready)
        {
            return new TsfRegistrationResult(true, "Keyina TSF đã được đăng ký.", null);
        }

        var request = new TsfProcessRequest(
            Path.Combine(Environment.SystemDirectory, "regsvr32.exe"),
            $"/s \"{nativeDllPath}\"",
            "runas",
            Path.GetDirectoryName(nativeDllPath)!);
        var process = await platform.LaunchAsync(request, cancellationToken).ConfigureAwait(false);
        if (process.Cancelled)
        {
            return new TsfRegistrationResult(false, "Bạn đã hủy yêu cầu quyền Administrator.", "elevation_cancelled");
        }
        if (process.ExitCode != 0)
        {
            return new TsfRegistrationResult(false, $"Đăng ký TSF thất bại với mã {process.ExitCode}.", "registration_failed");
        }

        health = await CheckAsync(cancellationToken).ConfigureAwait(false);
        return health.State == TsfSetupState.Ready
            ? new TsfRegistrationResult(true, "Đã đăng ký Keyina TSF.", null)
            : new TsfRegistrationResult(false, "Windows chưa ghi nhận đầy đủ profile Keyina.", "registration_not_verified");
    }

    public void OpenLanguageSettings() => platform.OpenLanguageSettings();

    public async Task<string> InstallDeveloperBuildAsync(CancellationToken cancellationToken)
    {
        var result = await RegisterAsync(cancellationToken).ConfigureAwait(false);
        return result.Message;
    }

    private static string FindNativeDll()
    {
        var direct = Path.Combine(AppContext.BaseDirectory, "KeyinaTsf.dll");
        if (File.Exists(direct))
        {
            return direct;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            foreach (var configuration in new[] { "Release", "Debug" })
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "build",
                    configuration == "Release" ? "windows-msvc-release" : "windows-msvc-debug",
                    "platform",
                    "windows",
                    "tsf",
                    configuration,
                    "KeyinaTsf.dll");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            directory = directory.Parent;
        }
        return direct;
    }

    private sealed class WindowsTsfSetupPlatform : ITsfSetupPlatform
    {
        public bool FileExists(string path) => File.Exists(path);

        public bool IsComRegistered()
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"Software\Classes\CLSID\{Clsid}\InprocServer32",
                writable: false);
            return key is not null;
        }

        public bool IsProfileRegistered()
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\CTF\TIP\{Clsid}",
                writable: false);
            return key is not null;
        }

        public async Task<TsfProcessResult> LaunchAsync(
            TsfProcessRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = request.FileName,
                    Arguments = request.Arguments,
                    Verb = request.Verb,
                    WorkingDirectory = request.WorkingDirectory,
                    UseShellExecute = true,
                }) ?? throw new InvalidOperationException("Không thể mở tiến trình đăng ký TSF.");
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                return new TsfProcessResult(process.ExitCode, Cancelled: false);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
            {
                return TsfProcessResult.CancelledResult;
            }
        }

        public void OpenLanguageSettings()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:regionlanguage",
                UseShellExecute = true,
            });
        }
    }
}
