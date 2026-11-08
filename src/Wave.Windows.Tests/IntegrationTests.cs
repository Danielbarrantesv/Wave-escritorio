using System.IO;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wave.Windows.Data;
using Wave.Windows.Data.Repositories;
using Wave.Windows.External;
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
/// No require SQLite ni WinRT (los tests se enfocan en el contenedor DI).
/// </summary>
public class IntegrationTests
{
    /// <summary>
    /// Crea un ServiceProvider equivalente al de App.xaml.cs pero sin inicializar
    /// la DB ni servicios que requieran UI (TrayService, SmtcService, HotkeyService).
    /// Útil para tests aislados.
    /// </summary>
    private static IServiceProvider BuildTestServiceProvider()
    {
        var services = new ServiceCollection();

        // SettingsService con path efímero para evitar contaminación entre tests
        var settingsPath = Path.Combine(Path.GetTempPath(), $"wave-test-{Guid.NewGuid():N}.json");
        var settings = new SettingsService(settingsPath);
        services.AddSingleton(settings);

        // Audio
        services.AddSingleton<AudioEngine>();
        services.AddSingleton<ISongRepository, SongRepository>();
        services.AddSingleton<IPlaylistRepository, PlaylistRepository>();
        services.AddSingleton<IPlaylistService, PlaylistService>();
        services.AddSingleton<PlaybackController>();
        services.AddSingleton<ILibraryService, LibraryService>();
        services.AddSingleton<LrcLibClient>();
        services.AddSingleton<ILyricsService, LyricsService>();

        // ViewModels
        services.AddSingleton<LyricsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<EqualizerViewModel>();

        // v0.9.0: Backup
        services.AddSingleton<IBackupService, BackupService>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void BuildServiceProvider_ResuelveTodosLosServiciosPrincipales()
    {
        // Arrange
        // Inicializar SqliteDatabase en memoria (temp file) para que los repos funcione
        var tempDb = Path.Combine(Path.GetTempPath(), $"wave-integration-{Guid.NewGuid():N}.db");
        try
        {
            SqliteDatabase.Init(tempDb);
            var provider = BuildTestServiceProvider();

            // Act & Assert — todos los servicios deberían poder resolverse
            provider.GetRequiredService<SettingsService>().Should().NotBeNull();
            provider.GetRequiredService<AudioEngine>().Should().NotBeNull();
            provider.GetRequiredService<ISongRepository>().Should().NotBeNull();
            provider.GetRequiredService<IPlaylistRepository>().Should().NotBeNull();
            provider.GetRequiredService<IPlaylistService>().Should().NotBeNull();
            provider.GetRequiredService<PlaybackController>().Should().NotBeNull();
            provider.GetRequiredService<ILibraryService>().Should().NotBeNull();
            provider.GetRequiredService<LrcLibClient>().Should().NotBeNull();
            provider.GetRequiredService<ILyricsService>().Should().NotBeNull();
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
        }
    }

    [Fact]
    public void BuildServiceProvider_ResuelveTodosLosViewModels()
    {
        // Arrange
        var tempDb = Path.Combine(Path.GetTempPath(), $"wave-integration-{Guid.NewGuid():N}.db");
        try
        {
            SqliteDatabase.Init(tempDb);
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
    public void BuildServiceProvider_ResuelveBackupService()
    {
        // Arrange
        var tempDb = Path.Combine(Path.GetTempPath(), $"wave-integration-{Guid.NewGuid():N}.db");
        try
        {
            SqliteDatabase.Init(tempDb);
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
            SqliteDatabase.Init(tempDb);
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
    public void LibraryService_AutoCargadoDesdeDI_NoEsNullYFunciona()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"wave-integration-{Guid.NewGuid():N}.db");
        try
        {
            SqliteDatabase.Init(tempDb);
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
    public void PlaylistService_AutoCargadoDesdeDI_NoEsNull()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"wave-integration-{Guid.NewGuid():N}.db");
        try
        {
            SqliteDatabase.Init(tempDb);
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
