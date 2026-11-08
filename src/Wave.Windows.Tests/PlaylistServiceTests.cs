using FluentAssertions;
using Moq;
using Wave.Windows.Data.Repositories;
using Wave.Windows.Models;
using Wave.Windows.Services;
using Xunit;

namespace Wave.Windows.Tests;

/// <summary>
/// Tests del PlaylistService (v0.4.0).
///
/// Tests cubren: CRUD de playlists, Favoritos idempotente,
/// ToggleFavorite (DB + playlist Favoritos), CreateGenrePlaylists,
/// GetAllSorted (Favoritos primero).
/// </summary>
public class PlaylistServiceTests
{
    private readonly Mock<IPlaylistRepository> _playlistRepoMock;
    private readonly Mock<ISongRepository> _songRepoMock;
    private readonly PlaylistService _service;

    public PlaylistServiceTests()
    {
        _playlistRepoMock = new Mock<IPlaylistRepository>();
        _songRepoMock = new Mock<ISongRepository>();
        _service = new PlaylistService(_playlistRepoMock.Object, _songRepoMock.Object);
    }

    // === EnsureFavoritesPlaylist ===

    [Fact]
    public void EnsureFavoritesPlaylist_CuandoNoExiste_LoCrea()
    {
        // Arrange
        _playlistRepoMock.Setup(r => r.GetByName(Playlist.FavoritesName))
                         .Returns((Playlist?)null);

        // Act
        _service.EnsureFavoritesPlaylist();

        // Assert
        _playlistRepoMock.Verify(r => r.Insert(It.Is<Playlist>(p =>
            p.Name == Playlist.FavoritesName &&
            p.Description == "default"
        )), Times.Once);
    }

    [Fact]
    public void EnsureFavoritesPlaylist_CuandoYaExiste_NoHaceNada()
    {
        // Arrange
        var existing = new Playlist { Id = 1, Name = Playlist.FavoritesName };
        _playlistRepoMock.Setup(r => r.GetByName(Playlist.FavoritesName))
                         .Returns(existing);

        // Act
        _service.EnsureFavoritesPlaylist();

        // Assert
        _playlistRepoMock.Verify(r => r.Insert(It.IsAny<Playlist>()), Times.Never);
    }

    // === GetAllSorted ===

    [Fact]
    public void GetAllSorted_RetornaFavoritosPrimeroYLuegoPorUpdatedAt()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var playlists = new List<Playlist>
        {
            new() { Id = 1, Name = "Rock", UpdatedAt = now - 100 },
            new() { Id = 2, Name = Playlist.FavoritesName, UpdatedAt = now - 1000 },
            new() { Id = 3, Name = "Pop", UpdatedAt = now - 50 }
        };
        _playlistRepoMock.Setup(r => r.GetAll()).Returns(playlists);

        // Act
        var result = _service.GetAllSorted();

        // Assert
        result.Should().HaveCount(3);
        result[0].Name.Should().Be(Playlist.FavoritesName); // Favoritos primero
        result[1].Name.Should().Be("Pop"); // UpdatedAt mayor (now - 50 > now - 100)
        result[2].Name.Should().Be("Rock");
    }

    [Fact]
    public void GetAll_CuandoNoHay_RetornaListaVacia()
    {
        // Arrange
        _playlistRepoMock.Setup(r => r.GetAll()).Returns(new List<Playlist>());

        // Act
        var result = _service.GetAll();

        // Assert
        result.Should().BeEmpty();
    }

    // === CreatePlaylist ===

    [Fact]
    public void CreatePlaylist_ConNombreValido_LoInserta()
    {
        // Arrange
        _playlistRepoMock.Setup(r => r.GetByName("Mi Playlist")).Returns((Playlist?)null);
        _playlistRepoMock.Setup(r => r.Insert(It.IsAny<Playlist>())).Returns(42);

        // Act
        var id = _service.CreatePlaylist("Mi Playlist");

        // Assert
        id.Should().Be(42);
        _playlistRepoMock.Verify(r => r.Insert(It.Is<Playlist>(p =>
            p.Name == "Mi Playlist"
        )), Times.Once);
    }

    [Fact]
    public void CreatePlaylist_CuandoYaExiste_RetornaIdExistenteSinInsertar()
    {
        // Arrange
        var existing = new Playlist { Id = 10, Name = "Mi Playlist" };
        _playlistRepoMock.Setup(r => r.GetByName("Mi Playlist")).Returns(existing);

        // Act
        var id = _service.CreatePlaylist("Mi Playlist");

        // Assert
        id.Should().Be(10);
        _playlistRepoMock.Verify(r => r.Insert(It.IsAny<Playlist>()), Times.Never);
    }

    [Fact]
    public void CreatePlaylist_ConNombreVacio_LanzaArgumentException()
    {
        // Act
        var act = () => _service.CreatePlaylist("");

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreatePlaylist_ConSongs_AgregaCancionesALaPlaylist()
    {
        // Arrange
        _playlistRepoMock.Setup(r => r.GetByName("Mix")).Returns((Playlist?)null);
        _playlistRepoMock.Setup(r => r.Insert(It.IsAny<Playlist>())).Returns(5);
        var songIds = new List<long> { 1, 2, 3 };

        // Act
        var id = _service.CreatePlaylist("Mix", songIds);

        // Assert
        id.Should().Be(5);
        _playlistRepoMock.Verify(r => r.AddSongsToPlaylist(5, songIds), Times.Once);
    }

    // === ToggleFavorite ===

    [Fact]
    public void ToggleFavorite_CuandoNoEraFavorito_LoMarcaComoFavoritoYLoAgrega()
    {
        // Arrange
        var song = new Song { Id = 1, Title = "Test", IsFavorite = false };
        var favPlaylist = new Playlist { Id = 100, Name = Playlist.FavoritesName };
        _playlistRepoMock.Setup(r => r.GetByName(Playlist.FavoritesName))
                         .Returns(favPlaylist);

        // Act
        _service.ToggleFavorite(song);

        // Assert
        song.IsFavorite.Should().BeTrue();
        _songRepoMock.Verify(r => r.UpdateFavorite(1, true), Times.Once);
        _playlistRepoMock.Verify(r => r.AddSongsToPlaylist(100, It.Is<List<long>>(l => l.Contains(1))), Times.Once);
    }

    [Fact]
    public void ToggleFavorite_CuandoYaEraFavorito_LoQuita()
    {
        // Arrange
        var song = new Song { Id = 2, Title = "Test", IsFavorite = true };
        var favPlaylist = new Playlist { Id = 100, Name = Playlist.FavoritesName };
        _playlistRepoMock.Setup(r => r.GetByName(Playlist.FavoritesName))
                         .Returns(favPlaylist);

        // Act
        _service.ToggleFavorite(song);

        // Assert
        song.IsFavorite.Should().BeFalse();
        _songRepoMock.Verify(r => r.UpdateFavorite(2, false), Times.Once);
        _playlistRepoMock.Verify(r => r.RemoveSong(100, 2), Times.Once);
    }

    [Fact]
    public void ToggleFavorite_CuandoNoExisteFavoritos_LoCreaYDespuesAgrega()
    {
        // Arrange
        var song = new Song { Id = 1, IsFavorite = false };
        // Primera llamada: Favoritos no existe. Segunda: ya existe.
        _playlistRepoMock.SetupSequence(r => r.GetByName(Playlist.FavoritesName))
                         .Returns((Playlist?)null)
                         .Returns(new Playlist { Id = 200, Name = Playlist.FavoritesName });
        _playlistRepoMock.Setup(r => r.Insert(It.IsAny<Playlist>())).Returns(200);

        // Act
        _service.ToggleFavorite(song);

        // Assert
        song.IsFavorite.Should().BeTrue();
        _playlistRepoMock.Verify(r => r.Insert(It.Is<Playlist>(p => p.Name == Playlist.FavoritesName)), Times.Once);
        _playlistRepoMock.Verify(r => r.AddSongsToPlaylist(200, It.Is<List<long>>(l => l.Contains(1))), Times.Once);
    }

    // === CreateGenrePlaylists ===

    [Fact]
    public void CreateGenrePlaylists_CuandoNoHayCanciones_NoHaceNada()
    {
        // Arrange
        _songRepoMock.Setup(r => r.ListAll()).Returns(new List<Song>());

        // Act
        _service.CreateGenrePlaylists();

        // Assert
        _playlistRepoMock.Verify(r => r.Insert(It.IsAny<Playlist>()), Times.Never);
        _playlistRepoMock.Verify(r => r.AddSongsToPlaylist(It.IsAny<long>(), It.IsAny<List<long>>()), Times.Never);
    }

    [Fact]
    public void CreateGenrePlaylists_CuandoHayCancionesConGenero_CreaPlaylistsPorGenero()
    {
        // Arrange
        var songs = new List<Song>
        {
            new() { Id = 1, Genre = "Rock" },
            new() { Id = 2, Genre = "Rock" },
            new() { Id = 3, Genre = "Pop" },
            new() { Id = 4, Genre = null } // sin género → ignorada
        };
        _songRepoMock.Setup(r => r.ListAll()).Returns(songs);
        // Primera y segunda llamada: no existe. Tercera ya no se llama.
        _playlistRepoMock.Setup(r => r.GetByName(It.IsAny<string>())).Returns((Playlist?)null);
        _playlistRepoMock.Setup(r => r.Insert(It.IsAny<Playlist>())).Returns(1);

        // Act
        _service.CreateGenrePlaylists();

        // Assert: 2 playlists creadas (Rock y Pop)
        _playlistRepoMock.Verify(r => r.Insert(It.Is<Playlist>(p => p.Name == "Rock" && p.Description == "auto-genre")), Times.Once);
        _playlistRepoMock.Verify(r => r.Insert(It.Is<Playlist>(p => p.Name == "Pop" && p.Description == "auto-genre")), Times.Once);
        _playlistRepoMock.Verify(r => r.Insert(It.IsAny<Playlist>()), Times.Exactly(2));

        // Y agrega las canciones a cada playlist
        _playlistRepoMock.Verify(r => r.AddSongsToPlaylist(
            It.IsAny<long>(),
            It.Is<List<long>>(l => l.Count == 2 && l.Contains(1) && l.Contains(2)) // Rock
        ), Times.Once);
        _playlistRepoMock.Verify(r => r.AddSongsToPlaylist(
            It.IsAny<long>(),
            It.Is<List<long>>(l => l.Count == 1 && l.Contains(3)) // Pop
        ), Times.Once);
    }

    [Fact]
    public void CreateGenrePlaylists_CuandoPlaylistYaExiste_NoLaCreaPeroAgrega()
    {
        // Arrange
        var songs = new List<Song>
        {
            new() { Id = 1, Genre = "Rock" }
        };
        _songRepoMock.Setup(r => r.ListAll()).Returns(songs);
        var existing = new Playlist { Id = 50, Name = "Rock" };
        _playlistRepoMock.Setup(r => r.GetByName("Rock")).Returns(existing);

        // Act
        _service.CreateGenrePlaylists();

        // Assert: no insertó (ya existía) pero sí agregó canciones
        _playlistRepoMock.Verify(r => r.Insert(It.IsAny<Playlist>()), Times.Never);
        _playlistRepoMock.Verify(r => r.AddSongsToPlaylist(50, It.Is<List<long>>(l => l.Contains(1))), Times.Once);
    }

    [Fact]
    public void CreateGenrePlaylists_AgrupaCaseInsensitive()
    {
        // Arrange — "rock", "Rock", "ROCK" deben agruparse en una sola playlist
        var songs = new List<Song>
        {
            new() { Id = 1, Genre = "rock" },
            new() { Id = 2, Genre = "Rock" },
            new() { Id = 3, Genre = "ROCK" }
        };
        _songRepoMock.Setup(r => r.ListAll()).Returns(songs);
        _playlistRepoMock.Setup(r => r.GetByName(It.IsAny<string>())).Returns((Playlist?)null);
        _playlistRepoMock.Setup(r => r.Insert(It.IsAny<Playlist>())).Returns(1);

        // Act
        _service.CreateGenrePlaylists();

        // Assert: solo 1 playlist creada (todas las variantes se agrupan)
        _playlistRepoMock.Verify(r => r.Insert(It.IsAny<Playlist>()), Times.Once);
        // Y las 3 canciones se agregaron a la misma playlist
        _playlistRepoMock.Verify(r => r.AddSongsToPlaylist(
            It.IsAny<long>(),
            It.Is<List<long>>(l => l.Count == 3)
        ), Times.Once);
    }

    // === Delete ===

    [Fact]
    public void Delete_LlamaAlRepositoryDeleteById()
    {
        // Act
        _service.Delete(42);

        // Assert
        _playlistRepoMock.Verify(r => r.DeleteById(42), Times.Once);
    }

    // === GetSongs ===

    [Fact]
    public void GetSongs_LlamaAlRepositoryGetSongsInPlaylist()
    {
        // Arrange
        var songs = new List<Song> { new() { Id = 1, Title = "Test" } };
        _playlistRepoMock.Setup(r => r.GetSongsInPlaylist(5)).Returns(songs);

        // Act
        var result = _service.GetSongs(5);

        // Assert
        result.Should().HaveCount(1);
        result[0].Title.Should().Be("Test");
    }

    // === AddSongs / RemoveSong ===

    [Fact]
    public void AddSongs_LlamaAlRepositoryAddSongsToPlaylist()
    {
        // Act
        _service.AddSongs(10, new List<long> { 1, 2 });

        // Assert
        _playlistRepoMock.Verify(r => r.AddSongsToPlaylist(10, It.Is<List<long>>(l => l.Count == 2)), Times.Once);
    }

    [Fact]
    public void RemoveSong_LlamaAlRepositoryRemoveSong()
    {
        // Act
        _service.RemoveSong(10, 7);

        // Assert
        _playlistRepoMock.Verify(r => r.RemoveSong(10, 7), Times.Once);
    }
}
