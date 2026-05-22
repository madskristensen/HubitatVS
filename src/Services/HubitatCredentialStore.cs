using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace HubitatVS
{
    internal static class HubitatCredentialStore
    {
        private const int CredTypeGeneric = 1;
        private const int CredPersistLocalMachine = 2;
        private const int ErrorNotFound = 1168;

        public static void SavePassword(HubitatHubConfig hub, string password)
        {
            if (hub == null)
                throw new ArgumentNullException(nameof(hub));
            if (password == null)
                throw new ArgumentNullException(nameof(password));

            var target = BuildTargetName(hub);
            var userName = string.IsNullOrWhiteSpace(hub.Username) ? hub.Name : hub.Username;
            var blob = Encoding.Unicode.GetBytes(password);

            var credential = new NativeCredential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                UserName = userName,
                Persist = CredPersistLocalMachine,
                AttributeCount = 0,
                Comment = "HubitatVS hub credential",
                CredentialBlobSize = blob.Length,
            };

            var blobHandle = IntPtr.Zero;
            try
            {
                blobHandle = Marshal.AllocCoTaskMem(blob.Length);
                Marshal.Copy(blob, 0, blobHandle, blob.Length);
                credential.CredentialBlob = blobHandle;

                if (!CredWrite(ref credential, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to save credentials for hub '{hub.Name}'.");
            }
            finally
            {
                if (blobHandle != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(blobHandle);
            }
        }

        public static string ReadPassword(HubitatHubConfig hub)
        {
            if (hub == null)
                throw new ArgumentNullException(nameof(hub));

            var target = BuildTargetName(hub);
            if (!CredRead(target, CredTypeGeneric, 0, out var credPtr))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorNotFound)
                    return string.Empty;

                throw new Win32Exception(error, $"Failed to read credentials for hub '{hub.Name}'.");
            }

            try
            {
                var credential = Marshal.PtrToStructure<NativeCredential>(credPtr);
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize <= 0)
                    return string.Empty;

                return Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2) ?? string.Empty;
            }
            finally
            {
                CredFree(credPtr);
            }
        }

        public static void DeletePassword(HubitatHubConfig hub)
        {
            if (hub == null)
                throw new ArgumentNullException(nameof(hub));

            var target = BuildTargetName(hub);
            if (CredDelete(target, CredTypeGeneric, 0))
                return;

            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
                throw new Win32Exception(error, $"Failed to delete credentials for hub '{hub.Name}'.");
        }

        private static string BuildTargetName(HubitatHubConfig hub)
            => $"HubitatVS:{hub.Name}|{hub.Host}";

        [DllImport("advapi32", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite([In] ref NativeCredential userCredential, [In] uint flags);

        [DllImport("advapi32", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int flags, out IntPtr credentialPtr);

        [DllImport("advapi32", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32", SetLastError = true)]
        private static extern void CredFree([In] IntPtr cred);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }
    }
}
