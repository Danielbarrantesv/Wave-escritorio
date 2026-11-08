using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Wave.Windows.Services;

/// <summary>
/// Servicio que registra hotkeys globales de Windows.
///
/// Hotkeys registradas:
/// - Ctrl+Alt+P → Play/Pausa
/// - Ctrl+Alt+→ → Siguiente
/// - Ctrl+Alt+← → Anterior
/// - Ctrl+Alt+↑ → Subir volumen
/// - Ctrl+Alt+↓ → Bajar volumen
///
/// Usa RegisterHotKey/UnregisterHotKey de user32.dll (P/Invoke).
/// Los hotkeys funcionan aunque la app no esté en foco.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Modifiers
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    // Virtual key codes
    private const uint VK_P = 0x50;       // 'P'
    private const uint VK_RIGHT = 0x27;   // Right arrow
    private const uint VK_LEFT = 0x25;    // Left arrow
    private const uint VK_UP = 0x26;      // Up arrow
    private const uint VK_DOWN = 0x28;    // Down arrow

    // Hotkey IDs
    private const int HOTKEY_PLAY_PAUSE = 1;
    private const int HOTKEY_NEXT = 2;
    private const int HOTKEY_PREV = 3;
    private const int HOTKEY_VOL_UP = 4;
    private const int HOTKEY_VOL_DOWN = 5;

    private const int WM_HOTKEY = 0x0312;

    private readonly HwndSource? _source;
    private readonly PlaybackController _playback;
    private readonly AudioEngine _audioEngine;
    private bool _disposed;
    private bool _initialized;

    /// <summary>True si los hotkeys se registraron correctamente.</summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// Registra los hotkeys globales.
    /// Requiere el handle de la ventana principal de WPF.
    /// </summary>
    public HotkeyService(IntPtr windowHandle, PlaybackController playback, AudioEngine audioEngine)
    {
        _playback = playback;
        _audioEngine = audioEngine;

        try
        {
            _source = HwndSource.FromHwnd(windowHandle);
            if (_source == null)
            {
                System.Diagnostics.Debug.WriteLine("[Wave] HotkeyService: HwndSource no disponible");
                return;
            }

            _source.AddHook(WndProc);

            // Registrar todos los hotkeys: Ctrl+Alt+<key>, MOD_NOREPEAT evita repetición
            var mods = MOD_CONTROL | MOD_ALT | MOD_NOREPEAT;

            RegisterHotKey(_source.Handle, HOTKEY_PLAY_PAUSE, mods, VK_P);
            RegisterHotKey(_source.Handle, HOTKEY_NEXT,      mods, VK_RIGHT);
            RegisterHotKey(_source.Handle, HOTKEY_PREV,      mods, VK_LEFT);
            RegisterHotKey(_source.Handle, HOTKEY_VOL_UP,    mods, VK_UP);
            RegisterHotKey(_source.Handle, HOTKEY_VOL_DOWN,  mods, VK_DOWN);

            _initialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wave] HotkeyService init failed: {ex.Message}");
            _initialized = false;
        }
    }

    /// <summary>Window procedure que intercepta mensajes WM_HOTKEY.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
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
                case HOTKEY_VOL_UP:
                    _audioEngine.Volume = Math.Min(1f, _audioEngine.Volume + 0.1f);
                    handled = true;
                    break;
                case HOTKEY_VOL_DOWN:
                    _audioEngine.Volume = Math.Max(0f, _audioEngine.Volume - 0.1f);
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_initialized && _source != null)
        {
            UnregisterHotKey(_source.Handle, HOTKEY_PLAY_PAUSE);
            UnregisterHotKey(_source.Handle, HOTKEY_NEXT);
            UnregisterHotKey(_source.Handle, HOTKEY_PREV);
            UnregisterHotKey(_source.Handle, HOTKEY_VOL_UP);
            UnregisterHotKey(_source.Handle, HOTKEY_VOL_DOWN);
            _source.RemoveHook(WndProc);
        }
    }
}
