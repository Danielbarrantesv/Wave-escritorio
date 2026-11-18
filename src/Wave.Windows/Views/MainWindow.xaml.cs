using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Wave.Windows.Services;
using Wave.Windows.Web;

namespace Wave.Windows.Views;

/// <summary>
/// Ventana principal de Wave con WebView2.
/// La UI se renderiza en HTML/CSS/JS, el backend en C#.
/// </summary>
public partial class MainWindow : Window
{
    private WebViewBridge? _bridge;
    private bool _isExiting = false;
    /// <summary>Seteado por JS cuando el usuario ya confirmó salir vía modal HTML.</summary>
    public bool JsConfirmedExit = false;
    private GlobalMediaKeyHook? _mediaKeyHook;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        // Intercept WM_GETMINMAXINFO para controlar el maximize
        SourceInitialized += (s, e) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var hwndSource = HwndSource.FromHwnd(handle);
            hwndSource?.AddHook(WndProc);
        };

        // WindowChrome permite resize desde todos los bordes y esquinas
        var chrome = new WindowChrome
        {
            CaptionHeight = 0,
            // Aumentar el grosor del borde de resize para que sea más fácil de agarrar
            // (antes era 6, ahora 8)
            ResizeBorderThickness = new Thickness(8),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        };
        WindowChrome.SetWindowChrome(this, chrome);
    }

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int WM_NCHITTEST = 0x0084;

    // Hit-test return codes
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            GetMonitorInfo(monitor, ref monitorInfo);

            // Ajustar el maximizado al área de trabajo (sin barra de tareas)
            // del monitor DONDE ESTÁ LA VENTANA (no el primario)
            // Con AllowsTransparency=True no hay border nativo que compensar.
            mmi.ptMaxPosition.X = monitorInfo.rcWork.Left - monitorInfo.rcMonitor.Left;
            mmi.ptMaxPosition.Y = monitorInfo.rcWork.Top - monitorInfo.rcMonitor.Top;
            mmi.ptMaxSize.X = monitorInfo.rcWork.Right - monitorInfo.rcWork.Left;
            mmi.ptMaxSize.Y = monitorInfo.rcWork.Bottom - monitorInfo.rcWork.Top;

            // Setear el tamaño MÍNIMO de resize manual (respeta MinWidth/MinHeight del XAML)
            // sin esto, el usuario puede hacer la ventana diminuta arrastrando desde una esquina
            var source = HwndSource.FromHwnd(hwnd);
            double dpiScale = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
            // DIPs a pixels: pixels = DIPs / dpiScale
            mmi.ptMinTrackSize.X = (int)Math.Ceiling(MinWidth / dpiScale);
            mmi.ptMinTrackSize.Y = (int)Math.Ceiling(MinHeight / dpiScale);

            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }
        else if (msg == WM_NCHITTEST)
        {
            // Permite el resize desde TODOS los bordes y esquinas, no solo la superior izquierda.
            // Con AllowsTransparency=True, WindowChrome no siempre mapea correctamente los hit-tests
            // de los bordes, así que lo hacemos manualmente.
            //
            // lParam contiene las coordenadas del cursor (LOWORD=x, HIWORD=y) en coords de pantalla.
            // Convertimos a coords de ventana y miramos en qué borde está.
            int screenX = (short)(lParam.ToInt32() & 0xFFFF);
            int screenY = (short)((lParam.ToInt32() >> 16) & 0xFFFF);

            // Obtener rect de la ventana en coords de pantalla
            GetWindowRect(hwnd, out var rc);
            const int border = 8; // ResizeBorderThickness del WindowChrome

            bool onLeft = screenX >= rc.Left && screenX < rc.Left + border;
            bool onRight = screenX <= rc.Right && screenX > rc.Right - border;
            bool onTop = screenY >= rc.Top && screenY < rc.Top + border;
            bool onBottom = screenY <= rc.Bottom && screenY > rc.Bottom - border;

            // Esquinas tienen prioridad sobre bordes
            if (onTop && onLeft) { handled = true; return new IntPtr(HTTOPLEFT); }
            if (onTop && onRight) { handled = true; return new IntPtr(HTTOPRIGHT); }
            if (onBottom && onLeft) { handled = true; return new IntPtr(HTBOTTOMLEFT); }
            if (onBottom && onRight) { handled = true; return new IntPtr(HTBOTTOMRIGHT); }
            if (onLeft) { handled = true; return new IntPtr(HTLEFT); }
            if (onRight) { handled = true; return new IntPtr(HTRIGHT); }
            if (onTop) { handled = true; return new IntPtr(HTTOP); }
            if (onBottom) { handled = true; return new IntPtr(HTBOTTOM); }
            // Si no está en ningún borde, dejar que WindowChrome lo maneje (HTCLIENT o HTCAPTION)
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await WebView.EnsureCoreWebView2Async(null);

            // Bloquear menú contextual nativo (Atrás/Actualizar/Guardar como/Imprimir/...)
            // Nota: la propiedad es AreDefaultContextMenusEnabled (plural "Menus")
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            // Bloquear aceleradores del navegador (F5 actualizar, Ctrl+S guardar, etc.)
            WebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            // Habilitar soporte de regiones no-client: permite que -webkit-app-region: drag
            // funcione para que el usuario pueda mover la ventana arrastrando la titlebar HTML
            try { WebView.CoreWebView2.Settings.IsNonClientRegionSupportEnabled = true; } catch { /* ignore */ }

            // Navegar directamente al index.html via file:// protocol
            var wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
            WaveLogger.Info($"Loading WebView2 from: {wwwrootPath}");
            WebView.CoreWebView2.Navigate(new Uri(wwwrootPath).AbsoluteUri);

            // Crear el bridge
            _bridge = new WebViewBridge(
                WebView.CoreWebView2,
                App.Services.GetRequiredService<ILibraryService>(),
                App.Services.GetRequiredService<IPlaylistService>(),
                App.Services.GetRequiredService<PlaybackController>(),
                App.Services.GetRequiredService<ILyricsService>(),
                App.Services.GetRequiredService<IBackupService>(),
                App.Services.GetRequiredService<SettingsService>(),
                App.Services.GetRequiredService<AudioEngine>()
            );

            WaveLogger.Info("WebView2 + Bridge initialized OK");

            // Limitar zoom a 67% - 125% via JS (Ctrl+rueda)
            WebView.CoreWebView2.NavigationCompleted += (s, args) =>
            {
                if (args.IsSuccess)
                {
                    try
                    {
                        _ = WebView.CoreWebView2.ExecuteScriptAsync(@"
                            document.addEventListener('wheel', function(e) {
                                if (e.ctrlKey) {
                                    setTimeout(function() {
                                        var z = document.body.style.zoom || 1;
                                        var pct = parseFloat(z);
                                        if (isNaN(pct)) pct = 1;
                                        if (pct < 0.67) document.body.style.zoom = '0.67';
                                        if (pct > 1.25) document.body.style.zoom = '1.25';
                                    }, 50);
                                }
                            }, { passive: true });
                        ");
                    }
                    catch { /* ignore */ }
                }
            };

            // Registrar hook global de media keys (funciona con app minimizada/en background)
            try
            {
                _mediaKeyHook = new GlobalMediaKeyHook(
                    App.Services.GetRequiredService<PlaybackController>());
                _mediaKeyHook.Register(this);
            }
            catch (Exception ex)
            {
                WaveLogger.Error("Failed to register global media key hook", ex);
            }
        }
        catch (Exception ex)
        {
            WaveLogger.Error("Failed to initialize WebView2", ex);
            MessageBox.Show($"Error al inicializar:\n\n{ex.Message}",
                "Wave - Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void WebView_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
    {
        WaveLogger.Info($"WebView navigating to: {e.Uri}");
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Desregistrar hook global de media keys antes de cerrar
        _mediaKeyHook?.Dispose();

        if (_isExiting) { base.OnClosing(e); return; }

        // Si JS ya confirmó la salida (modal HTML), salir sin pedir de nuevo
        if (JsConfirmedExit)
        {
            _isExiting = true;
            base.OnClosing(e);
            Environment.Exit(0);
            return;
        }

        // Cierre no confirmado (Alt+F4, taskbar, etc): cancelar y disparar el modal HTML
        e.Cancel = true;
        try
        {
            if (WebView?.CoreWebView2 != null)
            {
                _ = WebView.CoreWebView2.ExecuteScriptAsync("if(typeof closeWindow==='function')closeWindow();");
            }
        }
        catch { /* WebView no listo todavía */ }
    }

    private void Window_Drop(object sender, DragEventArgs e) { }
    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// Al maximizar, el handler WM_GETMINMAXINFO ya se encarga de todo:
    /// posiciona y dimensiona la ventana al área de trabajo del monitor correcto
    /// (sin barra de tareas), usando MonitorFromWindow(MONITOR_DEFAULTTONEAREST)
    /// que devuelve el monitor donde está la ventana, no el primario.
    /// </summary>
    private void Window_StateChanged(object? sender, EventArgs e)
    {
        // No-op: WM_GETMINMAXINFO maneja todo.
        // (Método conservado para no romper referencias XAML si las hubiera.)
    }

    // === Media key handlers ===
    // Estos métodos se ejecutan cuando el usuario presiona las teclas multimedia
    // del teclado (Play/Pause, Next, Prev, Stop) que traen algunos laptops.

    private PlaybackController? GetPlayback()
    {
        try { return App.Services.GetRequiredService<PlaybackController>(); }
        catch { return null; }
    }

    private void MediaTogglePlayPause_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
    {
        GetPlayback()?.TogglePlayPause();
    }

    private void MediaNextTrack_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
    {
        GetPlayback()?.SkipToNext();
    }

    private void MediaPreviousTrack_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
    {
        GetPlayback()?.SkipToPrevious();
    }

    private void MediaStop_Executed(object sender, System.Windows.Input.ExecutedRoutedEventArgs e)
    {
        var pb = GetPlayback();
        if (pb != null) { pb.Pause(); pb.Seek(0); }
    }
}
