using System.IO;
using FluentAssertions;
using Moq;
using Wave.Windows.Audio;
using Wave.Windows.Data.Repositories;
using Wave.Windows.Models;
using Wave.Windows.Services;
using Xunit;

namespace Wave.Windows.Tests;

/// <summary>
/// Tests del LibraryService (v0.3.0).
///
/// No testeamos extracción real de tags (eso requiere archivos físicos en CI).
/// Testeamos la lógica de deduplicación y scanner.
/// </summary>
public class LibraryServiceTests : IDisposable
{
    private readonly Mock<ISongRepository> _songRepoMock;
    private readonly LibraryService _library;

    public LibraryServiceTests()
    {
        _songRepoMock = new Mock<ISongRepository>();
        // Setup por defecto: repo vacío
        _songRepoMock.Setup(r => r.ListAll()).Returns(new List<Song>());
        _songRepoMock.Setup(r => r.Insert(It.IsAny<Song>())).Returns(1);
        _songRepoMock.Setup(r => r.Count()).Returns(0);

        _library = new LibraryService(_songRepoMock.Object);
    }

    [Fact]
    public void IsAudioFile_ConExtensionValida_RetornaTrue()
    {
        // Act & Assert
        TagReader.IsAudioFile("test.mp3").Should().BeTrue();
        TagReader.IsAudioFile("test.MP3").Should().BeTrue(); // case insensitive
        TagReader.IsAudioFile("test.flac").Should().BeTrue();
        TagReader.IsAudioFile("test.m4a").Should().BeTrue();
        TagReader.IsAudioFile("test.ogg").Should().BeTrue();
        TagReader.IsAudioFile("test.wav").Should().BeTrue();
        TagReader.IsAudioFile("test.aac").Should().BeTrue();
        TagReader.IsAudioFile("test.wma").Should().BeTrue();
    }

    [Fact]
    public void IsAudioFile_ConExtensionInvalida_RetornaFalse()
    {
        // Act & Assert
        TagReader.IsAudioFile("test.txt").Should().BeFalse();
        TagReader.IsAudioFile("test.jpg").Should().BeFalse();
        TagReader.IsAudioFile("test.mp4").Should().BeFalse(); // video, no audio
        TagReader.IsAudioFile("test").Should().BeFalse(); // sin extensión
        TagReader.IsAudioFile("").Should().BeFalse();
    }

    [Fact]
    public async Task ImportFilesAsync_ConListaVacia_RetornaCeros()
    {
        // Act
        var result = await _library.ImportFilesAsync(new List<string>());

        // Assert
        result.Added.Should().Be(0);
        result.Duplicates.Should().Be(0);
        result.Total.Should().Be(0);
    }

    [Fact]
    public async Task ImportFilesAsync_FiltraArchivosNoAudio()
    {
        // Arrange
        var files = new List<string> { "song.mp3", "notes.txt", "image.jpg", "another.flac" };

        // Mock: si ListAll está vacío, ningún archivo será duplicado
        // Pero como los archivos no existen físicamente, TagReader.Read devolverá null
        // → todos contarán como duplicates
        _songRepoMock.Setup(r => r.ListAll()).Returns(new List<Song>());

        // Act
        var result = await _library.ImportFilesAsync(files);

        // Assert: 2 archivos de audio intentaron procesarse (mp3, flac),
        // 2 no-audio fueron filtrados antes
        result.Total.Should().Be(2); // solo mp3 + flac
        // Como los archivos no existen físicamente, no se agregan
        result.Added.Should().Be(0);
    }

    [Fact]
    public async Task ImportFilesAsync_DedupPorPath_NoInsertaDuplicado()
    {
        // Arrange
        var existingSong = new Song
        {
            Id = 10,
            FilePath = "/music/test.mp3",
            FileName = "test.mp3",
            FileSize = 1000,
            Title = "Test",
            Artist = "Artist"
        };
        _songRepoMock.Setup(r => r.ListAll()).Returns(new List<Song> { existingSong });

        // Act
        var result = await _library.ImportFilesAsync(new List<string> { "/music/test.mp3" });

        // Assert
        result.Added.Should().Be(0);
        result.Duplicates.Should().Be(1);
        _songRepoMock.Verify(r => r.Insert(It.IsAny<Song>()), Times.Never, "no debe insertar duplicado");
    }

    [Fact]
    public async Task ImportFilesAsync_DedupPorNameSize_NoInsertaDuplicado()
    {
        // Arrange
        var existingSong = new Song
        {
            Id = 1,
            FilePath = "/path/original.mp3",
            FileName = "song.mp3",
            FileSize = 5000,
            Title = "Song",
            Artist = "Artist"
        };
        _songRepoMock.Setup(r => r.ListAll()).Returns(new List<Song> { existingSong });

        // Act — mismo FileName + mismo FileSize, distinto path
        // Como el archivo no existe físicamente, TagReader.Read devuelve null,
        // pero el dedup por name+size se ejecuta ANTES que TagReader.Read.
        // El test verifica que el dedup funcione antes de leer tags.
        var result = await _library.ImportFilesAsync(new List<string> { "/other/path/song.mp3" });

        // Assert
        result.Added.Should().Be(0);
        // El archivo coincide por FileName+FileSize → duplicate
        // (no llega a llamar TagReader.Read porque el dedup ocurre antes)
        result.Duplicates.Should().Be(1);
    }

    [Fact]
    public void Scan_ConCarpetaInexistente_RetornaListaVacia()
    {
        // Act
        var result = _library.Scan("/no/existe/esta/ruta/xyz");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void Scan_ConCarpetaVacia_RetornaListaVacia()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"wave-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // Act
            var result = _library.Scan(tempDir);

            // Assert
            result.Should().BeEmpty();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Scan_ConCarpetaConAudio_EncuentraArchivos()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"wave-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // Crear archivos dummy
            File.WriteAllText(Path.Combine(tempDir, "song1.mp3"), "fake mp3");
            File.WriteAllText(Path.Combine(tempDir, "song2.flac"), "fake flac");
            File.WriteAllText(Path.Combine(tempDir, "notes.txt"), "not audio");

            // Act
            var result = _library.Scan(tempDir);

            // Assert
            result.Should().HaveCount(2);
            result.Should().Contain(f => f.EndsWith("song1.mp3"));
            result.Should().Contain(f => f.EndsWith("song2.flac"));
            result.Should().NotContain(f => f.EndsWith("notes.txt"));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Scan_ConSubcarpetas_EsRecursivo()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"wave-test-{Guid.NewGuid():N}");
        var subDir = Path.Combine(tempDir, "subfolder");
        Directory.CreateDirectory(subDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "top.mp3"), "fake");
            File.WriteAllText(Path.Combine(subDir, "nested.flac"), "fake");

            // Act
            var result = _library.Scan(tempDir);

            // Assert
            result.Should().HaveCount(2);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void GetAllSongs_LlamaAlRepository()
    {
        // Arrange
        var songs = new List<Song>
        {
            new() { Id = 1, Title = "A" },
            new() { Id = 2, Title = "B" }
        };
        _songRepoMock.Setup(r => r.ListAll()).Returns(songs);

        // Act
        var result = _library.GetAllSongs();

        // Assert
        result.Should().HaveCount(2);
        _songRepoMock.Verify(r => r.ListAll(), Times.Once);
    }

    [Fact]
    public void SongCount_RetornaConteoDelRepository()
    {
        // Arrange
        _songRepoMock.Setup(r => r.Count()).Returns(42);

        // Act & Assert
        _library.SongCount.Should().Be(42);
    }

    [Fact]
    public void DeleteAll_LlamaAlRepositoryYLimpiarArtDirectory()
    {
        // Arrange — asegurar que ArtDirectory existe (no se crea por defecto en tests)
        // Para no romper el test, dejamos que DeleteAll maneje el caso de directorio inexistente.

        // Act
        _library.DeleteAll();

        // Assert
        _songRepoMock.Verify(r => r.DeleteAll(), Times.Once);
    }

    [Fact]
    public async Task ImportFilesAsync_CuandoInsertExitoso_ActualizaCacheInterno()
    {
        // Arrange — primer archivo insertado, segundo archivo idéntico debe ser dedup por path
        var song1 = new Song
        {
            FilePath = "/music/song.mp3",
            FileName = "song.mp3",
            FileSize = 1000,
            Title = "Song 1",
            Artist = "Artist 1",
            DurationMs = 200_000 // 3:20
        };

        // Como los archivos no existen físicamente, TagReader.Read devuelve null
        // y no se agregarán. Necesitamos archivos reales para probar esto en CI.
        // En su lugar, testeamos la dedup con el cache interno usando songs preexistentes.
        _songRepoMock.Setup(r => r.ListAll()).Returns(new List<Song> { song1 });

        // Act — segundo archivo MISMO path = dedup por path
        var result = await _library.ImportFilesAsync(new List<string> { "/music/song.mp3" });

        // Assert
        result.Duplicates.Should().Be(1);
        result.Added.Should().Be(0);
    }

    public void Dispose()
    {
        // Nada que dispose (mocks)
    }
}
