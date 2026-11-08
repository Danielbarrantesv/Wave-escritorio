using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Wave.Windows.Data;
using Wave.Windows.Data.Repositories;
using Wave.Windows.External;
using Wave.Windows.Services;
using Wave.Windows.ViewModels;
using Wave.Windows.Views;

namespace Wave.Windows;

/// <summary>
/// Punto de entrada de la aplicación Wave for Windows.
/// Inicializa DB, configura DI y lanza MainWindow.
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private HotkeyService? _hotkeyService;

    /// <summary>Ruta del archivo de log para diagnóstico.</summary>
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Wave", "wave.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        // Truncar log viejo al iniciar (mantener solo la sesión actual)
        try { File.WriteAllText(LogPath, ""); } catch { }

        // Log de startup — cualquier excepción se captura aquí
        LogInfo("=== Wave starting ===");
        LogInfo($"Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
        LogInfo($"BaseDirectory: {AppContext.BaseDirectory}");
        LogInfo($"OS: {Environment.OSVersion.VersionString}");
        LogInfo($".NET Runtime: {Environment.Version}");

        try
        {
            base.OnStartup(e);
            LogInfo("OnStartup base OK");

            InitializeDatabase();
            LogInfo("Database initialized OK");

            ConfigureDependencyInjection();
            LogInfo("DI configured OK");

            // Capturar excepciones no controladas
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // Asegurar playlist Favoritos existe + restaurar cola guardada
            var playlistService = Services.GetRequiredService<IPlaylistService>();
            playlistService.EnsureFavoritesPlaylist();
            LogInfo("Favoritos playlist ensured");

            var playback = Services.GetRequiredService<PlaybackController>();
            playback.RestoreSavedQueue();
            LogInfo("Queue restored");

            // Inicializar SmtcService (puede fallar en CI/sin WinRT — defensivo)
            try
            {
                Services.GetRequiredService<SmtcService>();
                LogInfo("SMTC initialized");
            }
            catch (Exception ex)
            {
                LogInfo($"SMTC not available (expected in some environments): {ex.Message}");
            }

            // Crear y mostrar MainWindow PRIMERO (antes de servicios que pueden fallar)
            LogInfo("Creating MainWindow...");
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            LogInfo("MainWindow shown");

            // Inicializar TrayService (defensivo)
            try
            {
                var tray = Services.GetRequiredService<TrayService>();
                tray.ShowMainWindowRequested += (s, args) => mainWindow.ShowFromTray();
                tray.ExitRequested += (s, args) =>
                {
                    LogInfo("Exit requested from tray");
                    Environment.Exit(0);
                };
                LogInfo("TrayService initialized");
            }
            catch (Exception ex)
            {
                LogInfo($"TrayService failed (continuing without tray): {ex.Message}");
            }

            // Inicializar HotkeyService (defensivo)
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(mainWindow);
                var hotkeyService = new HotkeyService(helper.Handle, playback, Services.GetRequiredService<AudioEngine>());
                _hotkeyService = hotkeyService;
                LogInfo("HotkeyService initialized");
            }
            catch (Exception ex)
            {
                LogInfo($"HotkeyService failed (continuing without hotkeys): {ex.Message}");
            }

            LogInfo("=== Wave startup complete ===");
        }
        catch (Exception ex)
        {
            LogError("FATAL ERROR during startup", ex);
            // Mostrar MessageBox con el error
            MessageBox.Show(
                $"Error al iniciar Wave:\n\n{ex.Message}\n\nStack trace:\n{ex.StackTrace}\n\n" +
                $"Log completo en:\n{LogPath}",
                "Wave - Error Fatal",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>Inicializa la DB en %LocalAppData%\Wave\wave.db</summary>
    private static void InitializeDatabase()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wave");
        Directory.CreateDirectory(appData);
        var dbPath = Path.Combine(appData, "wave.db");
        LogInfo($"DB path: {dbPath}");
        SqliteDatabase.Init(dbPath);
    }

    /// <summary>Configura el contenedor de DI con todos los servicios.</summary>
    private void ConfigureDependencyInjection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<AudioEngine>();
        services.AddSingleton<ISongRepository, SongRepository>();
        services.AddSingleton<IPlaylistRepository, PlaylistRepository>();
        services.AddSingleton<IPlaylistService, PlaylistService>();
        services.AddSingleton<PlaybackController>();
        services.AddSingleton<ILibraryService, LibraryService>();
        services.AddSingleton<LrcLibClient>();
        services.AddSingleton<ILyricsService, LyricsService>();
        services.AddSingleton<LyricsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<EqualizerViewModel>();
        services.AddSingleton<TrayService>();
        services.AddSingleton<SmtcService>();
        services.AddSingleton<IBackupService, BackupService>();
        Services = services.BuildServiceProvider();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError("Dispatcher unhandled exception", e.Exception);
        MessageBox.Show(
            $"Ocurrió un error:\n\n{e.Exception.Message}\n\nStack trace:\n{e.Exception.StackTrace}\n\n" +
            $"Log completo en:\n{LogPath}",
            "Wave - Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        LogError("AppDomain unhandled exception", ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogError("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogInfo("=== Wave exiting ===");

        // Cerrar todas las ventanas hijas (MiniPlayer, Lyrics, Equalizer)
        try
        {
            foreach (Window window in Windows)
            {
                if (window != MainWindow && window.IsLoaded)
                {
                    LogInfo($"Closing window: {window.GetType().Name}");
                    window.Close();
                }
            }
        }
        catch (Exception ex)
        {
            LogError("Error closing child windows", ex);
        }

        try
        {
            // Services puede ser null si el startup falló antes de configurar DI
            if (Services == null)
            {
                LogInfo("Services is null, skipping dispose");
                return;
            }

            _hotkeyService?.Dispose();
            if (Services.GetService<SmtcService>() is { } smtc) smtc.Dispose();
            if (Services.GetService<TrayService>() is { } tray) tray.Dispose();
            if (Services.GetService<EqualizerViewModel>() is { } eqVm) eqVm.Dispose();
            if (Services.GetService<MainViewModel>() is { } mainVm) mainVm.Dispose();
            if (Services.GetService<LyricsViewModel>() is { } lyricsVm) lyricsVm.Dispose();
            if (Services.GetService<PlaybackController>() is { } playback) playback.Dispose();
            if (Services.GetService<LrcLibClient>() is { } lrcClient) lrcClient.Dispose();
            if (Services.GetService<SqliteDatabase>() is { } db) db.Dispose();
        }
        catch (Exception ex)
        {
            LogError("Error during dispose", ex);
        }
        base.OnExit(e);
    }

    // === Logging helpers ===

    private static void LogInfo(string message)
    {
        WriteLog("INFO", message);
    }

    private static void LogError(string context, Exception? ex)
    {
        WriteLog("ERROR", $"{context}: {ex?.ToString() ?? "(null)"}");
    }

    private static void WriteLog(string level, string message)
    {
        try
        {
            var logDir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(logDir)) Directory.CreateDirectory(logDir);
            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
            File.AppendAllText(LogPath, line);
            System.Diagnostics.Debug.WriteLine(line.Trim());
        }
        catch
        {
            // Si no podemos loguear, no rompemos la app
        }
    }
}
