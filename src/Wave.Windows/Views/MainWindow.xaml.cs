using System.IO;
using System.Windows;
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

        // WindowChrome permite resize desde todos los bordes y esquinas
        // incluso con WindowStyle=None + AllowsTransparency=True.
        // ResizeBorderThickness define qué tan grueso es el borde agarrable.
        var chrome = new WindowChrome
        {
            CaptionHeight = 0,           // Sin caption nativo (ya tenemos titlebar HTML)
            ResizeBorderThickness = new Thickness(6), // 6px de borde agarrable en todos los lados
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        };
        WindowChrome.SetWindowChrome(this, chrome);
    }

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
    /// Al maximizar, limita la ventana al WorkArea (sin barra de tareas).
    /// Esto evita que la ventana cubra la barra de tareas de Windows.
    /// </summary>
    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            var workArea = SystemParameters.WorkArea;
            MaxWidth = workArea.Width;
            MaxHeight = workArea.Height;
            Top = workArea.Top;
            Left = workArea.Left;
        }
        else
        {
            // Al restaurar, quitar límites para poder redimensionar libremente
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
        }
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
