using System.IO;
using System.Windows;
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

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
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

            // Forzar zoom al 100% — importante para pantallas HiDPI donde el sistema
            // usa 125%/150% por defecto, lo que hace que todo se vea "muy grande"
            try { WebView.CoreWebView2.SetZoomFactor(1.0); } catch { /* ignore */ }

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
    /// Cuando la ventana se maximiza, limitar al área de trabajo (WorkArea)
    /// para que NO cubra la barra de tareas de Windows.
    /// </summary>
    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            // Obtener el área de trabajo (sin barra de tareas)
            var workArea = SystemParameters.WorkArea;
            // Limitar tamaño y posición
            MaxWidth = workArea.Width;
            MaxHeight = workArea.Height;
            Top = workArea.Top;
            Left = workArea.Left;
        }
        else
        {
            // Restaurar valores por defecto cuando no está maximizada
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;
        }
    }
}
