using System.IO;
using FluentAssertions;
using Moq;
using Wave.Windows.Data.Repositories;
using Wave.Windows.Models;
using Wave.Windows.Services;
using Xunit;

namespace Wave.Windows.Tests;

/// <summary>
/// Tests del BackupService (v0.9.0).
///
/// Como BackupService depende de SongRepository y PlaylistRepository (que requieren SQLite),
/// los tests usan mocks y verifican la lógica de serialización/deserialización.
/// </summary>
public class BackupServiceTests : IDisposable
{
    private readonly Mock<ISongRepository> _songRepoMock;
    private readonly Mock<IPlaylistRepository> _playlistRepoMock;
    private readonly Mock<IPlaylistService> _playlistServiceMock;
    private readonly BackupService _backup;
    private readonly string _tempPath;

    public BackupServiceTests()
    {
        _songRepoMock = new Mock<ISongRepository>();
        _playlistRepoMock = new Mock<IPlaylistRepository>();
        _playlistServiceMock = new Mock<IPlaylistService>();
        _backup = new BackupService(_songRepoMock.Object, _playlistRepoMock.Object, _playlistServiceMock.Object);
        _tempPath = Path.Combine(Path.GetTempPath(), $"wave-backup-{Guid.NewGuid():N}.json");
    }

    [Fact]
    public async Task ExportAsync_ConCanciones_LasIncluyeEnJSON()
    {
        // Arrange
        var songs = new List<Song>
        {
            new() { Id = 1, Title = "Canción A", Artist = "Artista X", FilePath = "/path/a.mp3" },
            new() { Id = 2, Title = "Canción B", Artist = "Artista Y", FilePath = "/path/b.mp3" }
        };
        _songRepoMock.Setup(r => r.ListAll()).Returns(songs);
        _playlistRepoMock.Setup(r => r.GetAll()).Returns(new List<Playlist>());

        // Act
        await _backup.ExportAsync(_tempPath);

        // Assert
        var content = await File.ReadAllTextAsync(_tempPath);
        content.Should().Contain("Canción A");
        content.Should().Contain("Canción B");
        content.Should().Contain("Artista X");
    }

    [Fact]
    public async Task ExportAsync_ConPlaylists_LasIncluyeEnJSON()
    {
        // Arrange
        var playlists = new List<Playlist>
        {
            new() { Id = 1, Name = "Mi Playlist", Description = "test" }
        };
        _songRepoMock.Setup(r => r.ListAll()).Returns(new List<Song>());
        _playlistRepoMock.Setup(r => r.GetAll()).Returns(playlists);
        // GetAllPlaylistSongs llama GetSongsInPlaylist para cada playlist
        _playlistRepoMock.Setup(r => r.GetSongsInPlaylist(1)).Returns(new List<Song>());

        // Act
        await _backup.ExportAsync(_tempPath);

        // Assert
        var content = await File.ReadAllTextAsync(_tempPath);
        content.Should().Contain("Mi Playlist");
    }

    [Fact]
    public async Task ExportAsync_ConPathVacio_LanzaArgumentException()
    {
        var act = async () => await _backup.ExportAsync("");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExportAsync_CreaDirectorioSiNoExiste()
    {
        // Arrange
        var nestedPath = Path.Combine(Path.GetTempPath(), $"wave-nested-{Guid.NewGuid():N}", "backup.json");
        _songRepoMock.Setup(r => r.ListAll()).Returns(new List<Song>());
        _playlistRepoMock.Setup(r => r.GetAll()).Returns(new List<Playlist>());

        // Act
        await _backup.ExportAsync(nestedPath);

        // Assert
        File.Exists(nestedPath).Should().BeTrue();

        // Cleanup
        try { Directory.Delete(Path.GetDirectoryName(nestedPath)!, true); } catch { }
    }

    [Fact]
    public async Task ExportAsync_CreaArchivoValido()
    {
        // Arrange
        _songRepoMock.Setup(r => r.ListAll()).Returns(new List<Song>());
        _playlistRepoMock.Setup(r => r.GetAll()).Returns(new List<Playlist>());

        // Act
        await _backup.ExportAsync(_tempPath);

        // Assert
        File.Exists(_tempPath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(_tempPath);
        content.Should().Contain("\"Version\": 1");
        content.Should().Contain("\"Songs\"");
        content.Should().Contain("\"Playlists\"");
    }

    [Fact]
    public async Task ImportAsync_ConArchivoInexistente_LanzaFileNotFoundException()
    {
        var act = async () => await _backup.ImportAsync("/no/existe/backup.json");
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ImportAsync_ConJSONInvalido_LanzaInvalidDataException()
    {
        // Arrange
        await File.WriteAllTextAsync(_tempPath, "{ invalid json }");

        // Act & Assert
        var act = async () => await _backup.ImportAsync(_tempPath);
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ImportAsync_ConBackupValido_BorraDBActualEInsertaNuevos()
    {
        // Arrange — crear un backup válido
        var backup = new BackupData
        {
            Version = 1,
            ExportDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Songs = new List<Song>
            {
                new() { Id = 1, Title = "Canción Importada", FilePath = "/imported.mp3" }
            },
            Playlists = new List<Playlist>
            {
                new() { Id = 1, Name = "Importada", CreatedAt = 1, UpdatedAt = 1 }
            },
            PlaylistSongs = new List<PlaylistSong>()
        };
        var json = System.Text.Json.JsonSerializer.Serialize(backup);
        await File.WriteAllTextAsync(_tempPath, json);

        // Mock: DeleteAll y Insert deben llamarse
        _songRepoMock.Setup(r => r.ListAll()).Returns(new List<Song>());
        _playlistRepoMock.Setup(r => r.GetAll()).Returns(new List<Playlist>());
        _songRepoMock.Setup(r => r.Insert(It.IsAny<Song>())).Returns(99);
        _playlistRepoMock.Setup(r => r.Insert(It.IsAny<Playlist>())).Returns(99);

        // Act
        await _backup.ImportAsync(_tempPath);

        // Assert — se borraron canciones y playlists existentes
        _songRepoMock.Verify(r => r.DeleteAll(), Times.Once);

        // Se insertó la canción nueva
        _songRepoMock.Verify(r => r.Insert(It.Is<Song>(s => s.Title == "Canción Importada")), Times.Once);

        // Se insertó la playlist nueva
        _playlistRepoMock.Verify(r => r.Insert(It.Is<Playlist>(p => p.Name == "Importada")), Times.Once);
    }

    [Fact]
    public async Task ExportThenImport_RoundTrip_PreservaDatos()
    {
        // Arrange — exportar biblioteca con 1 canción y 1 playlist
        var song = new Song { Id = 1, Title = "Round Trip", Artist = "Test", FilePath = "/test.mp3" };
        var playlist = new Playlist { Id = 1, Name = "RT Playlist", CreatedAt = 1, UpdatedAt = 1 };
        _songRepoMock.Setup(r => r.ListAll()).Returns(new List<Song> { song });
        _playlistRepoMock.Setup(r => r.GetAll()).Returns(new List<Playlist> { playlist });
        _playlistRepoMock.Setup(r => r.GetSongsInPlaylist(1)).Returns(new List<Song> { song });

        // Export
        await _backup.ExportAsync(_tempPath);
        var exportedContent = await File.ReadAllTextAsync(_tempPath);
        exportedContent.Should().Contain("Round Trip");
        exportedContent.Should().Contain("RT Playlist");

        // Cambiar mocks para simular DB vacía antes de importar
        _songRepoMock.Setup(r => r.ListAll()).Returns(new List<Song>());
        _playlistRepoMock.Setup(r => r.GetAll()).Returns(new List<Playlist>());
        _songRepoMock.Setup(r => r.Insert(It.IsAny<Song>())).Returns(1);
        _playlistRepoMock.Setup(r => r.Insert(It.IsAny<Playlist>())).Returns(1);

        // Import
        await _backup.ImportAsync(_tempPath);

        // Assert — se insertaron la canción y playlist exportadas
        _songRepoMock.Verify(r => r.Insert(It.Is<Song>(s => s.Title == "Round Trip")), Times.Once);
        _playlistRepoMock.Verify(r => r.Insert(It.Is<Playlist>(p => p.Name == "RT Playlist")), Times.Once);
    }

    public void Dispose()
    {
        try { if (File.Exists(_tempPath)) File.Delete(_tempPath); } catch { }
    }
}
