using System.Runtime.InteropServices;
using System.Text;
using PhoneBridge.Core;

namespace PhoneBridge.Host;

static class Native
{
    public const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2, WM_CLIPBOARDUPDATE = 0x031D;

    [StructLayout(LayoutKind.Sequential)]
    public struct Margins { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_SYSTEMBACKDROP_TYPE = 38;
    const int DWMWCP_ROUND = 2, DWMSBT_MAINWINDOW = 2;

    /// <summary>Mica backdrop + rounded corners (Windows 11 22H2+). Returns false when Mica is not available.</summary>
    public static bool ApplyMica(IntPtr hwnd, bool dark)
    {
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        SetDarkMode(hwnd, dark);
        var corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        var backdrop = DWMSBT_MAINWINDOW;
        return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0;
    }

    public static void SetDarkMode(IntPtr hwnd, bool dark)
    {
        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
    }

    public static bool SystemUsesDarkTheme()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    /// <summary>Native window move: keeps Aero Snap and the shake/drag behavior.</summary>
    public static void StartDrag(IntPtr hwnd)
    {
        ReleaseCapture();
        SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, IntPtr.Zero);
    }
}

/// <summary>Secrets in Windows Credential Manager (generic credentials, current user only).</summary>
sealed class CredentialStore(string prefix) : ISecretStore
{
    const int CRED_TYPE_GENERIC = 1, CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Credential
    {
        public int Flags, Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredRead(string target, int type, int flags, out IntPtr credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredWrite(ref Credential credential, int flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredDelete(string target, int type, int flags);
    [DllImport("advapi32.dll")] static extern void CredFree(IntPtr buffer);

    public string? Get(string key)
    {
        if (!CredRead(prefix + key, CRED_TYPE_GENERIC, 0, out var ptr)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<Credential>(ptr);
            return Marshal.PtrToStringUni(cred.CredentialBlob, cred.CredentialBlobSize / 2);
        }
        finally { CredFree(ptr); }
    }

    public void Set(string key, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var cred = new Credential
            {
                Type = CRED_TYPE_GENERIC, TargetName = prefix + key, CredentialBlob = blob, CredentialBlobSize = bytes.Length,
                Persist = CRED_PERSIST_LOCAL_MACHINE, UserName = "PhoneBridge",
            };
            if (!CredWrite(ref cred, 0)) throw new InvalidOperationException($"CredWrite failed ({Marshal.GetLastWin32Error()})");
        }
        finally { Marshal.FreeHGlobal(blob); }
    }

    public void Delete(string key) => CredDelete(prefix + key, CRED_TYPE_GENERIC, 0);
}
