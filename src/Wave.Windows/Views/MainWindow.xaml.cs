using System.IO;
using System.Windows;
using Microsoft.Win32;
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

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Inicializar WebView2
            await WebView.EnsureCoreWebView2Async(null);

            // Configurar ruta virtual: los archivos de wwwroot se sirven desde el bin
            var wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            WebView.CoreWebView2.SetVirtualHostToFolderMapping("wave.app", wwwrootPath,
                Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

            // Navegar a la página principal
            WebView.CoreWebView2.Navigate("https://wave.app/index.html");

            // Crear el bridge de comunicación C# ↔ JS
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
            MessageBox.Show($"Error al inicializar la interfaz:\n\n{ex.Message}\n\n" +
                          "Asegúrate de tener WebView2 Runtime instalado desde:\n" +
                          "https://developer.microsoft.com/microsoft-edge/webview2/",
                          "Wave - Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Interceptar navegación para abrir folder picker cuando JS lo solicite.</summary>
    private void WebView_NavigationStarting(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
    {
        WaveLogger.Info($"WebView navigating to: {e.Uri}");
    }

    /// <summary>Mostrar ventana desde el tray.</summary>
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

        if (App.Services.GetService<TrayService>() is not null)
        {
            var result = MessageBox.Show(
                "¿Minimizar a bandeja (Sí) o cerrar (No)?",
                "Wave — Cerrar",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes) { e.Cancel = true; Hide(); return; }
            if (result == MessageBoxResult.No) { _isExiting = true; base.OnClosing(e); Environment.Exit(0); return; }
            e.Cancel = true; return;
        }
        base.OnClosing(e);
        Environment.Exit(0);
    }

    private bool _isExiting = false;

    private void Window_Drop(object sender, DragEventArgs e) { }
    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
}
