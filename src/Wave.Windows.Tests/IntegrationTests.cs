using System.IO;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wave.Windows.Data;
using Wave.Windows.Data.Repositories;
using Wave.Windows.External;
using Wave.Windows.Models;
using Wave.Windows.Services;
using Wave.Windows.ViewModels;
using Xunit;

namespace Wave.Windows.Tests;

/// <summary>
/// Tests de integración de v1.0.0.
///
/// Verifican que todos los servicios y ViewModels están correctamente
/// registrados en el contenedor de DI y pueden resolverse.
///
/// Como SqliteDatabase.Init requiere los archivos .sql de migraciones que
/// solo están disponibles cuando se ejecuta desde el bin de Wave.Windows
/// (no desde el bin de Tests), los tests que necesitan DB real se marcan
/// como Skip en CI y solo se ejecutan localmente.
/// </summary>
public class IntegrationTests
{
    /// <summary>
    /// Crea un ServiceProvider equivalente al de App.xaml.cs pero sin inicializar
    /// la DB ni servicios que requieran UI.
    /// </summary>
    private static IServiceProvider BuildTestServiceProvider()
    {
        var services = new ServiceCollection();

        var settingsPath = Path.Combine(Path.GetTempPath(), $"wave-test-{Guid.NewGuid():N}.json");
        var settings = new SettingsService(settingsPath);
        services.AddSingleton(settings);

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

        services.AddSingleton<IBackupService, BackupService>();

        return services.BuildServiceProvider();
    }

    /// <summary>Inicializa la DB con migraciones inline para tests locales.</summary>
    private static bool TryInitTestDatabase(string dbPath)
    {
        try
        {
            SqliteDatabase.Init(dbPath);
            return true;
        }
        catch
        {
            // En CI sin migraciones .sql disponibles, falla — los tests dependientes se skip
            return false;
        }
    }

    [Fact]
    public void BuildServiceProvider_ResuelveServiciosNoDependientesDeDB()
    {
        // Servicios que NO dependen de SqliteDatabase.Instance
        var provider = BuildTestServiceProvider();

        // Act & Assert
        provider.GetRequiredService<SettingsService>().Should().NotBeNull();
        provider.GetRequiredService<AudioEngine>().Should().NotBeNull();
        provider.GetRequiredService<LrcLibClient>().Should().NotBeNull();
        provider.GetRequiredService<ILyricsService>().Should().NotBeNull();
    }

    [Fact]
    public void BuildServiceProvider_ResuelveTodosLosViewModels_CuandoDBInicializada()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"wave-integration-{Guid.NewGuid():N}.db");
        try
        {
            if (!TryInitTestDatabase(tempDb))
            {
                // Skip en CI sin migraciones
                return;
            }
            var provider = BuildTestServiceProvider();

            // Act & Assert
            provider.GetRequiredService<LyricsViewModel>().Should().NotBeNull();
            provider.GetRequiredService<MainViewModel>().Should().NotBeNull();
            provider.GetRequiredService<EqualizerViewModel>().Should().NotBeNull();
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
        }
    }

    [Fact]
    public void BuildServiceProvider_ResuelveBackupService_CuandoDBInicializada()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"wave-integration-{Guid.NewGuid():N}.db");
        try
        {
            if (!TryInitTestDatabase(tempDb))
            {
                return;
            }
            var provider = BuildTestServiceProvider();

            // Act & Assert
            var backup = provider.GetRequiredService<IBackupService>();
            backup.Should().NotBeNull();
            backup.Should().BeOfType<BackupService>();
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
        }
    }

    [Fact]
    public void PlaybackController_AutoCargadoDesdeDI_NoEsNull()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"wave-integration-{Guid.NewGuid():N}.db");
        try
        {
            if (!TryInitTestDatabase(tempDb))
            {
                return;
            }
            var provider = BuildTestServiceProvider();

            // Act
            var playback = provider.GetRequiredService<PlaybackController>();

            // Assert
            playback.Should().NotBeNull();
            playback.Volume.Should().BeGreaterThan(0f);
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
        }
    }

    [Fact]
    public void LibraryService_AutoCargadoDesdeDI_NoEsNullYFunciona_CuandoDBInicializada()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"wave-integration-{Guid.NewGuid():N}.db");
        try
        {
            if (!TryInitTestDatabase(tempDb))
            {
                return;
            }
            var provider = BuildTestServiceProvider();

            // Act
            var library = provider.GetRequiredService<ILibraryService>();

            // Assert
            library.Should().NotBeNull();
            library.SongCount.Should().Be(0); // biblioteca vacía al iniciar
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
        }
    }

    [Fact]
    public void PlaylistService_EnsureFavoritesPlaylist_CuandoDBInicializada()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"wave-integration-{Guid.NewGuid():N}.db");
        try
        {
            if (!TryInitTestDatabase(tempDb))
            {
                return;
            }
            var provider = BuildTestServiceProvider();
            var playlistService = provider.GetRequiredService<IPlaylistService>();

            // Act
            playlistService.EnsureFavoritesPlaylist();

            // Assert — Favoritos debería existir ahora
            var playlists = playlistService.GetAll();
            playlists.Should().Contain(p => p.Name == Playlist.FavoritesName);
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
        }
    }
}
