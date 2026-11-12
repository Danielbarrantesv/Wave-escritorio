using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Wave.Windows.Services;

/// <summary>
/// Hook global de teclas multimedia usando RegisterHotKey de la API de Windows.
/// Permite que las media keys (Play/Pause, Next, Prev, Stop) funcionen
// incluso cuando la app está minimizada o en segundo plano.
/// </summary>
public class GlobalMediaKeyHook : IDisposable
{
    // IDs de los hotkeys registrados
    private const int HOTKEY_PLAY_PAUSE = 9001;
    private const int HOTKEY_NEXT = 9002;
    private const int HOTKEY_PREV = 9003;
    private const int HOTKEY_STOP = 9004;

    // Modifiers
    private const uint MOD_NONE = 0x0000;

    // Virtual key codes para media keys
    private const uint VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint VK_MEDIA_NEXT_TRACK = 0xB0;
    private const uint VK_MEDIA_PREV_TRACK = 0xB1;
    private const uint VK_MEDIA_STOP = 0xB2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr _windowHandle;
    private HwndSource? _source;
    private readonly PlaybackController _playback;
    private bool _registered;

    public GlobalMediaKeyHook(PlaybackController playback)
    {
        _playback = playback;
    }

    public void Register(Window window)
    {
        if (_registered) return;

        _windowHandle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_windowHandle);
        if (_source != null)
        {
            _source.AddHook(HwndHook);
        }

        // Registrar los 4 hotkeys globales
        RegisterHotKey(_windowHandle, HOTKEY_PLAY_PAUSE, MOD_NONE, VK_MEDIA_PLAY_PAUSE);
        RegisterHotKey(_windowHandle, HOTKEY_NEXT, MOD_NONE, VK_MEDIA_NEXT_TRACK);
        RegisterHotKey(_windowHandle, HOTKEY_PREV, MOD_NONE, VK_MEDIA_PREV_TRACK);
        RegisterHotKey(_windowHandle, HOTKEY_STOP, MOD_NONE, VK_MEDIA_STOP);

        _registered = true;
        WaveLogger.Info("GlobalMediaKeyHook registered (Play/Pause, Next, Prev, Stop)");
    }

    public void Unregister()
    {
        if (!_registered) return;

        UnregisterHotKey(_windowHandle, HOTKEY_PLAY_PAUSE);
        UnregisterHotKey(_windowHandle, HOTKEY_NEXT);
        UnregisterHotKey(_windowHandle, HOTKEY_PREV);
        UnregisterHotKey(_windowHandle, HOTKEY_STOP);

        if (_source != null)
        {
            _source.RemoveHook(HwndHook);
            _source = null;
        }

        _registered = false;
        WaveLogger.Info("GlobalMediaKeyHook unregistered");
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;

        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            switch (id)
            {
                case HOTKEY_PLAY_PAUSE:
                    _playback.TogglePlayPause();
                    handled = true;
                    break;
                case HOTKEY_NEXT:
                    _playback.SkipToNext();
                    handled = true;
                    break;
                case HOTKEY_PREV:
                    _playback.SkipToPrevious();
                    handled = true;
                    break;
                case HOTKEY_STOP:
                    _playback.Pause();
                    _playback.Seek(0);
                    handled = true;
                    break;
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        GC.SuppressFinalize(this);
    }
}
