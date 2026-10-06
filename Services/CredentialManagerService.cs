using System.Runtime.InteropServices;
using System.Text;

namespace QAFlowRecorder.Services;

public static class CredentialManagerService
{
    private const uint CredTypeGeneric = 1;
    private const uint PersistLocalMachine = 2;

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
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref NativeCredential userCredential, [In] uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr credential);

    public static void WriteSecret(string target, string secret)
    {
        var bytes = Encoding.Unicode.GetBytes(secret ?? string.Empty);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException($"Windows Credential Manager could not store the secret. Win32 error {Marshal.GetLastWin32Error()}.");
        }
        finally { Marshal.FreeCoTaskMem(blob); }
    }

    public static string ReadSecret(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return string.Empty;
        if (!CredRead(target, CredTypeGeneric, 0, out var ptr) || ptr == IntPtr.Zero) return string.Empty;
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(ptr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return string.Empty;
            var bytes = new byte[checked((int)credential.CredentialBlobSize)];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally { CredFree(ptr); }
    }

    public static void DeleteSecret(string target)
    {
        if (!string.IsNullOrWhiteSpace(target)) _ = CredDeleteW(target, CredTypeGeneric, 0);
    }

    public static string BuildTarget(Guid projectId, Guid dataSetId, Guid variableId) =>
        $"QAFlowRecorder/automation/{projectId:N}/{dataSetId:N}/{variableId:N}";

    public static string BuildEmailTarget(Guid projectId) =>
        $"QAFlowRecorder/automation/{projectId:N}/smtp-password";
}
