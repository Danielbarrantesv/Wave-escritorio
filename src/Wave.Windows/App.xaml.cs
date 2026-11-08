using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Wave.Windows.Data;
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
        // Los servicios restantes se registran en versiones posteriores
        // cuando se implementen:
        //   PlaybackController, LibraryService, PlaylistService, LyricsService,
        //   BackupService, TrayService, SmtcService, HotkeyService, MiniPlayerWindow
        Services = services.BuildServiceProvider();

        // 3. Capturar excepciones no controladas
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 4. Crear y mostrar MainWindow
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
        // Dispose de servicios pendiente cuando se implementen (PlaybackController, TrayService, etc.)
        if (Services.GetService<SqliteDatabase>() is { } db)
        {
            db.Dispose();
        }
        base.OnExit(e);
    }
}
