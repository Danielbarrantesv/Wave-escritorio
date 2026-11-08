using FluentAssertions;
using Moq;
using Wave.Windows.Models;
using Wave.Windows.Services;
using Wave.Windows.Data.Repositories;
using Xunit;

namespace Wave.Windows.Tests;

/// <summary>
/// Tests del PlaybackController (v0.2.0).
///
/// Nota: No testeamos reproducción real de audio (eso requiere NAudio + archivos
/// físicos en el runner de CI). Testeamos la lógica de cola, shuffle y repeat.
/// </summary>
public class PlaybackControllerTests : IDisposable
{
    private readonly SettingsService _settings;
    private readonly AudioEngine _engine;
    private readonly Mock<ISongRepository> _songRepoMock;
    private readonly PlaybackController _controller;

    public PlaybackControllerTests()
    {
        // SettingsService efímero (no persiste a disco real porque usa un path distinto en CI)
        _settings = new SettingsService();
        _engine = new AudioEngine();
        _songRepoMock = new Mock<ISongRepository>();
        _controller = new PlaybackController(_settings, _engine, _songRepoMock.Object);
    }

    [Fact]
    public void SetQueue_ConCancionesValidas_EstableceCurrentSong()
    {
        // Arrange
        var songs = new List<Song>
        {
            new() { Id = 1, FilePath = "test1.mp3", Title = "Canción 1" },
            new() { Id = 2, FilePath = "test2.mp3", Title = "Canción 2" }
        };

        // Act (autoPlay=false para no intentar cargar archivos inexistentes)
        _controller.SetQueue(songs, 0, autoPlay: false);

        // Assert
        _controller.CurrentSong.Should().NotBeNull();
        _controller.CurrentSong!.Title.Should().Be("Canción 1");
        _controller.Queue.Count.Should().Be(2);
    }

    [Fact]
    public void SetQueue_ConListaVacia_NoHaceNada()
    {
        // Act
        _controller.SetQueue(new List<Song>(), 0, autoPlay: false);

        // Assert
        _controller.CurrentSong.Should().BeNull();
        _controller.Queue.Should().BeEmpty();
    }

    [Fact]
    public void SetQueue_ConStartIndexFueraDeRango_LoClampa()
    {
        // Arrange
        var songs = new List<Song>
        {
            new() { Id = 1, FilePath = "test.mp3", Title = "Test" }
        };

        // Act (startIndex=99 debería clampear a 0)
        _controller.SetQueue(songs, 99, autoPlay: false);

        // Assert
        _controller.CurrentSong.Should().NotBeNull();
        _controller.CurrentSong!.Title.Should().Be("Test");
    }

    [Fact]
    public void ToggleShuffle_CuandoActivado_EstableceIsShuffleTrue()
    {
        // Act
        _controller.ToggleShuffle();

        // Assert
        _controller.IsShuffle.Should().BeTrue();
    }

    [Fact]
    public void ToggleShuffle_CuandoLlamadoDosVeces_VuelveAFalse()
    {
        // Act
        _controller.ToggleShuffle();
        _controller.ToggleShuffle();

        // Assert
        _controller.IsShuffle.Should().BeFalse();
    }

    [Fact]
    public void ToggleRepeatOne_CuandoActivado_EstableceRepeatOneTrue()
    {
        // Act
        _controller.ToggleRepeatOne();

        // Assert
        _controller.RepeatOne.Should().BeTrue();
    }

    [Fact]
    public void Volume_CuandoSetado_PersisteEnSettings()
    {
        // Act
        _controller.Volume = 0.5f;

        // Assert
        _controller.Volume.Should().BeApproximately(0.5f, 0.001f);
        _settings.Volume.Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public void Volume_CuandoSetadoFueraDeRango_LoClampa()
    {
        // Act
        _controller.Volume = 5.0f;

        // Assert (clamp a 1.0)
        _controller.Volume.Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void Volume_CuandoSetadoNegativo_LoClampa()
    {
        // Act
        _controller.Volume = -0.5f;

        // Assert (clamp a 0)
        _controller.Volume.Should().BeApproximately(0f, 0.001f);
    }

    [Fact]
    public void JumpTo_ConIndiceValido_CambiaCurrentSong()
    {
        // Arrange
        var songs = new List<Song>
        {
            new() { Id = 1, FilePath = "test1.mp3", Title = "Canción 1" },
            new() { Id = 2, FilePath = "test2.mp3", Title = "Canción 2" },
            new() { Id = 3, FilePath = "test3.mp3", Title = "Canción 3" }
        };
        _controller.SetQueue(songs, 0, autoPlay: false);

        // Act
        _controller.JumpTo(2);

        // Assert
        _controller.CurrentSong!.Title.Should().Be("Canción 3");
    }

    [Fact]
    public void JumpTo_ConIndiceInvalido_NoCambiaCurrentSong()
    {
        // Arrange
        var songs = new List<Song>
        {
            new() { Id = 1, FilePath = "test1.mp3", Title = "Canción 1" }
        };
        _controller.SetQueue(songs, 0, autoPlay: false);

        // Act (índice negativo o > count)
        _controller.JumpTo(-1);
        _controller.JumpTo(99);

        // Assert
        _controller.CurrentSong!.Title.Should().Be("Canción 1");
    }

    [Fact]
    public void SaveQueueToPrefs_CuandoHayCola_PersisteIds()
    {
        // Arrange
        var songs = new List<Song>
        {
            new() { Id = 10, FilePath = "a.mp3", Title = "A" },
            new() { Id = 20, FilePath = "b.mp3", Title = "B" }
        };
        _controller.SetQueue(songs, 1, autoPlay: false);

        // Act
        _controller.SaveQueueToPrefs();

        // Assert
        var savedIds = _settings.Get<string>("queue_song_ids");
        savedIds.Should().Be("10,20");
        _settings.Get<int>("queue_index").Should().Be(1);
    }

    [Fact]
    public void SaveQueueToPrefs_CuandoNoHayCola_NoPersiste()
    {
        // Act
        _controller.SaveQueueToPrefs();

        // Assert
        var savedIds = _settings.Get<string>("queue_song_ids");
        savedIds.Should().BeNullOrEmpty();
    }

    [Fact]
    public void RestoreSavedQueue_CuandoNoHayPrefs_NoHaceNada()
    {
        // Arrange — no hay prefs
        _songRepoMock.Setup(r => r.ListAll()).Returns(new List<Song>());

        // Act
        _controller.RestoreSavedQueue();

        // Assert
        _controller.CurrentSong.Should().BeNull();
        _controller.Queue.Should().BeEmpty();
    }

    [Fact]
    public void RestoreSavedQueue_CuandoHayPrefs_RestauraCola()
    {
        // Arrange — guardar primero
        var songs = new List<Song>
        {
            new() { Id = 100, FilePath = "x.mp3", Title = "X" },
            new() { Id = 200, FilePath = "y.mp3", Title = "Y" }
        };
        _controller.SetQueue(songs, 0, autoPlay: false);
        _controller.SaveQueueToPrefs();

        // Configurar mock para que devuelva las canciones al restaurar
        _songRepoMock.Setup(r => r.ListAll()).Returns(songs);

        // Act — restaurar en un controlador nuevo
        var newController = new PlaybackController(_settings, new AudioEngine(), _songRepoMock.Object);
        newController.RestoreSavedQueue();

        // Assert
        newController.Queue.Count.Should().Be(2);
        newController.CurrentSong!.Title.Should().Be("X");

        newController.Dispose();
    }

    [Fact]
    public void TogglePlayPause_CuandoNoHayCancion_NoHaceNada()
    {
        // Act (no hay cola, debería no lanzar excepción)
        _controller.TogglePlayPause();

        // Assert
        _controller.IsPlaying.Should().BeFalse();
    }

    public void Dispose()
    {
        _controller.Dispose();
        _engine.Dispose();
    }
}
