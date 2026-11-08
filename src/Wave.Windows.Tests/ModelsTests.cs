using FluentAssertions;
using Wave.Windows.Models;
using Wave.Windows.Services;
using Xunit;

namespace Wave.Windows.Tests;

/// <summary>
/// Tests de los modelos de dominio (v0.1.0).
/// Tests de servicios (PlaybackController, LibraryService) se agregan en v0.2.0+.
/// </summary>
public class ModelsTests
{
    [Fact]
    public void Song_DisplayTitle_CuandoTitleVacio_RetornaFileName()
    {
        var song = new Song
        {
            FilePath = "C:/music/test.mp3",
            FileName = "test.mp3",
            Title = null
        };

        song.DisplayTitle.Should().Be("test.mp3");
    }

    [Fact]
    public void Song_DisplayTitle_CuandoTitlePresente_RetornaTitle()
    {
        var song = new Song
        {
            FileName = "test.mp3",
            Title = "Canción Real"
        };

        song.DisplayTitle.Should().Be("Canción Real");
    }

    [Fact]
    public void Song_DisplayArtist_CuandoArtistVacio_RetornaDesconocido()
    {
        var song = new Song { Artist = null };
        song.DisplayArtist.Should().Be("Artista desconocido");
    }

    [Fact]
    public void Song_DisplayArtist_CuandoEsUnknown_RetornaDesconocido()
    {
        var song = new Song { Artist = "<unknown>" };
        song.DisplayArtist.Should().Be("Artista desconocido");
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(1000, "0:01")]
    [InlineData(60000, "1:00")]
    [InlineData(3600000, "1:00:00")]
    [InlineData(3661000, "1:01:01")]
    public void Song_DisplayDuration_FormateaCorrectamente(long durationMs, string expected)
    {
        var song = new Song { DurationMs = durationMs };
        song.DisplayDuration.Should().Be(expected);
    }

    [Fact]
    public void Playlist_IsFavorites_CuandoNombreEsFavoritos_RetornaTrue()
    {
        var playlist = new Playlist { Name = Playlist.FavoritesName };
        playlist.IsFavorites.Should().BeTrue();
    }

    [Fact]
    public void Playlist_IsAutoGenre_CuandoDescriptionEsAutoGenre_RetornaTrue()
    {
        var playlist = new Playlist { Name = "Rock", Description = "auto-genre" };
        playlist.IsAutoGenre.Should().BeTrue();
    }

    [Fact]
    public void ImportResult_ProgressPercent_CalculaCorrectamente()
    {
        var result = new ImportResult(Added: 75, Duplicates: 25, Total: 100);
        result.ProgressPercent.Should().Be(75.0);
    }

    [Fact]
    public void ParsedLyrics_IsEmpty_CuandoTodoVacio_RetornaTrue()
    {
        var lyrics = new ParsedLyrics(PlainLyrics: null, Lines: new(), IsSynced: false);
        lyrics.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ParsedLyrics_IsEmpty_CuandoTieneLineas_RetornaFalse()
    {
        var lyrics = new ParsedLyrics(
            PlainLyrics: null,
            Lines: new List<LyricLine> { new(0, "Hello") },
            IsSynced: true);
        lyrics.IsEmpty.Should().BeFalse();
    }
}
