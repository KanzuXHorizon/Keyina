using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Keyina.Host.Windows.Credentials;

public sealed class WindowsCredentialVault : ICredentialVault
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumCredentialBlobBytes = 2_560;

    public void Write(string target, string secret)
    {
        ValidateTarget(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var blobSize = Encoding.Unicode.GetByteCount(secret);
        if (blobSize > MaximumCredentialBlobBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(secret),
                $"Credential secret cannot exceed {MaximumCredentialBlobBytes} UTF-16 bytes.");
        }

        var blob = Marshal.StringToCoTaskMemUni(secret);
        try
        {
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = target,
                CredentialBlobSize = checked((uint)blobSize),
                CredentialBlob = blob,
                Persist = CredentialPersistLocalMachine,
                UserName = Environment.UserName,
            };

            if (!CredWriteW(ref credential, 0))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Windows Credential Manager rejected the credential write.");
            }
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    public string? Read(string target)
    {
        ValidateTarget(target);
        if (!CredReadW(target, CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(
                error,
                "Windows Credential Manager rejected the credential read.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return string.Empty;
            }

            if ((credential.CredentialBlobSize & 1U) != 0 ||
                credential.CredentialBlobSize > MaximumCredentialBlobBytes)
            {
                throw new InvalidDataException("Stored credential has an invalid UTF-16 blob size.");
            }

            return Marshal.PtrToStringUni(
                credential.CredentialBlob,
                checked((int)credential.CredentialBlobSize / sizeof(char)));
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public bool Delete(string target)
    {
        ValidateTarget(target);
        if (CredDeleteW(target, CredentialTypeGeneric, 0))
        {
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        if (error == ErrorNotFound)
        {
            return false;
        }

        throw new Win32Exception(
            error,
            "Windows Credential Manager rejected the credential deletion.");
    }

    private static void ValidateTarget(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (target.Length > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(
        string target,
        uint type,
        uint flags,
        out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
