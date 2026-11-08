using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Wave.Windows.Data;
using Wave.Windows.Data.Repositories;
using Wave.Windows.Services;
using Wave.Windows.Views;

namespace Wave.Windows;

/// <summary>
/// Punto de entrada de la aplicación Wave for Windows.
/// Inicializa DB, configura DI y lanza MainWindow.
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Inicializar DB en %LocalAppData%\Wave\wave.db
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wave");
        Directory.CreateDirectory(appData);
        var dbPath = Path.Combine(appData, "wave.db");
        SqliteDatabase.Init(dbPath);

        // 2. Configurar DI
        var services = new ServiceCollection();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<AudioEngine>();
        services.AddSingleton<ISongRepository, SongRepository>();
        services.AddSingleton<IPlaylistRepository, PlaylistRepository>();
        services.AddSingleton<IPlaylistService, PlaylistService>();
        services.AddSingleton<PlaybackController>();
        services.AddSingleton<ILibraryService, LibraryService>();
        // Los servicios restantes se registran en versiones posteriores
        // cuando se implementen:
        //   LyricsService, BackupService, TrayService, SmtcService,
        //   HotkeyService, MiniPlayerWindow
        Services = services.BuildServiceProvider();

        // 3. Capturar excepciones no controladas
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 4. Asegurar playlist Favoritos existe + restaurar cola guardada
        var playlistService = Services.GetRequiredService<IPlaylistService>();
        playlistService.EnsureFavoritesPlaylist();

        var playback = Services.GetRequiredService<PlaybackController>();
        playback.RestoreSavedQueue();

        // 5. Crear y mostrar MainWindow
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Log y mostrar mensaje amigable; marcar como handled evita el crash
        System.Diagnostics.Debug.WriteLine($"[Wave] Dispatcher exception: {e.Exception}");
        MessageBox.Show(
            $"Ocurrió un error: {e.Exception.Message}",
            "Wave - Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        System.Diagnostics.Debug.WriteLine($"[Wave] AppDomain exception: {ex}");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[Wave] Unobserved task exception: {e.Exception}");
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Dispose ordenado: PlaybackController guarda la cola y dispose del engine,
        // luego SqliteDatabase cierra la conexión.
        if (Services.GetService<PlaybackController>() is { } playback)
        {
            playback.Dispose();
        }
        if (Services.GetService<SqliteDatabase>() is { } db)
        {
            db.Dispose();
        }
        base.OnExit(e);
    }
}
