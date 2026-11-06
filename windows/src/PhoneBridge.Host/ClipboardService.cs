using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using PhoneBridge.Core;

namespace PhoneBridge.Host;

/// <summary>Sets the Windows clipboard (with history/cloud exclusion for codes) and watches user copies.</summary>
sealed class ClipboardService : IDisposable
{
    // Documented clipboard formats honored by Win+V history, cloud clipboard and clipboard monitors.
    const string ExcludeFromMonitors = "ExcludeClipboardContentFromMonitorProcessing";
    const string CanIncludeInHistory = "CanIncludeInClipboardHistory";
    const string CanUploadToCloud = "CanUploadToCloudClipboard";
    const string LegacyViewerIgnore = "Clipboard Viewer Ignore";   // used by password managers

    readonly HwndSource _sink;
    uint _ownSequence;

    /// <summary>The user copied something on the PC. Args: kind ("text"/"url"/"image"), text, PNG base64, sensitive.</summary>
    public event Action<string, string?, string?, bool>? Copied;

    public ClipboardService()
    {
        // Message-only window (HWND_MESSAGE) just to receive WM_CLIPBOARDUPDATE.
        _sink = new HwndSource(new HwndSourceParameters("PhoneBridgeClipboard") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        _sink.AddHook(WndProc);
        Native.AddClipboardFormatListener(_sink.Handle);
    }

    public void SetText(string text, bool sensitive)
    {
        var data = new DataObject();
        data.SetText(text);
        if (sensitive)
        {
            data.SetData(ExcludeFromMonitors, new MemoryStream(new byte[4]));
            data.SetData(CanIncludeInHistory, new MemoryStream(BitConverter.GetBytes(0)));
            data.SetData(CanUploadToCloud, new MemoryStream(BitConverter.GetBytes(0)));
        }
        Set(() => Clipboard.SetDataObject(data, true));
    }

    public void SetImage(byte[] imageBytes)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(imageBytes);
        image.EndInit();
        image.Freeze();
        Set(() => Clipboard.SetImage(image));
    }

    public string? GetText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch (System.Runtime.InteropServices.COMException) { return null; }
    }

    /// <summary>Clears the clipboard only if it still holds <paramref name="text"/> (OTP auto-clear after 60 s).</summary>
    public void ClearIfEquals(string text)
    {
        if (GetText() == text) Set(Clipboard.Clear);
    }

    void Set(Action action)
    {
        try { action(); }
        catch (System.Runtime.InteropServices.COMException) { /* clipboard locked by another app; WPF already retried */ }
        _ownSequence = Native.GetClipboardSequenceNumber();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_CLIPBOARDUPDATE || Native.GetClipboardSequenceNumber() == _ownSequence) return IntPtr.Zero;
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null || data.GetDataPresent(ExcludeFromMonitors) || data.GetDataPresent(LegacyViewerIgnore)) return IntPtr.Zero;
            var sensitive = data.GetDataPresent(CanIncludeInHistory);   // apps set it (to 0) for secrets
            if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text && text.Length > 0)
            {
                sensitive |= OtpDetector.Detect(text) is not null;
                var kind = Uri.TryCreate(text.Trim(), UriKind.Absolute, out var u) && u.Scheme is "http" or "https" ? "url" : "text";
                if (text.Length <= 1_000_000) Copied?.Invoke(kind, text, null, sensitive);
            }
            else if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } bitmap)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                if (ms.Length <= 15 * 1024 * 1024) Copied?.Invoke("image", null, Convert.ToBase64String(ms.ToArray()), sensitive);
            }
        }
        catch (System.Runtime.InteropServices.COMException) { /* clipboard busy: skip this change */ }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Native.RemoveClipboardFormatListener(_sink.Handle);
        _sink.Dispose();
    }
}
