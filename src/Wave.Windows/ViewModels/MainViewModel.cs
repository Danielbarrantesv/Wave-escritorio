using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wave.Windows.Models;
using Wave.Windows.Services;

namespace Wave.Windows.ViewModels;

/// <summary>
/// ViewModel principal de MainWindow. Gestiona:
/// - Lista observable de canciones (con filtrado y ordenación)
/// - Búsqueda en tiempo real (Title, Artist, Album)
/// - Sort por 5 criterios (Recientes, Reproducidas, Título, Artista, Duración)
/// - Lista observable de playlists para el sidebar
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ILibraryService _library;
    private readonly IPlaylistService _playlistService;
    private readonly PlaybackController _playback;
    private bool _disposed;

    /// <summary>Lista completa de canciones (sin filtrar).</summary>
    private List<Song> _allSongs = new();

    /// <summary>Playlist seleccionada en el sidebar (null = todas las canciones).</summary>
    private Playlist? _selectedPlaylist;

    /// <summary>Lista observable de canciones mostradas (filtradas + ordenadas).</summary>
    public ObservableCollection<Song> Songs { get; } = new();

    /// <summary>Vista filtrada para que la UI bindee.</summary>
    public ICollectionView SongsView { get; }

    /// <summary>Lista observable de playlists en el sidebar.</summary>
    public ObservableCollection<Playlist> Playlists { get; } = new();

    /// <summary>Total de canciones en la biblioteca (independiente del filtro).</summary>
    public int SongCount => _allSongs.Count;

    /// <summary>Canciones visibles (después del filtro de búsqueda).</summary>
    public int VisibleCount => Songs.Count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleCount))]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private SortOrder _sortOrder = SortOrder.RecentlyAdded;

    public MainViewModel(ILibraryService library, IPlaylistService playlistService, PlaybackController playback)
    {
        _library = library;
        _playlistService = playlistService;
        _playback = playback;

        // Configurar CollectionViewSource para filtrado y ordenación
        SongsView = CollectionViewSource.GetDefaultView(Songs);
        SongsView.Filter = FilterSong;

        // Cargar datos iniciales desde DB
        RefreshSongs();
        RefreshPlaylists();
    }

    /// <summary>Recarga la lista de canciones desde DB según la playlist seleccionada.</summary>
    public void RefreshSongs()
    {
        _allSongs = _selectedPlaylist == null
            ? _library.GetAllSongs()
            : _playlistService.GetSongs(_selectedPlaylist.Id);

        Songs.Clear();
        foreach (var song in _allSongs)
        {
            Songs.Add(song);
        }

        ApplySort();
        OnPropertyChanged(nameof(SongCount));
        OnPropertyChanged(nameof(VisibleCount));
    }

    /// <summary>Recarga la lista de playlists en el sidebar.</summary>
    public void RefreshPlaylists()
    {
        Playlists.Clear();
        foreach (var playlist in _playlistService.GetAllSorted())
        {
            Playlists.Add(playlist);
        }
    }

    /// <summary>Cambia la playlist seleccionada (null = mostrar todas las canciones).</summary>
    public void SelectPlaylist(Playlist? playlist)
    {
        _selectedPlaylist = playlist;
        SearchQuery = string.Empty; // Reset búsqueda al cambiar de playlist
        RefreshSongs();
    }

    /// <summary>
    /// Filtro aplicado a cada canción cuando se escribe en el SearchBox.
    /// Compara contra Title, Artist y Album (case-insensitive).
    /// </summary>
    private bool FilterSong(object obj)
    {
        if (obj is not Song song) return false;
        if (string.IsNullOrWhiteSpace(SearchQuery)) return true;

        var q = SearchQuery.Trim();
        // Comparación case-insensitive en Title, Artist, Album, FileName
        return (song.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) == true)
            || (song.Artist?.Contains(q, StringComparison.OrdinalIgnoreCase) == true)
            || (song.Album?.Contains(q, StringComparison.OrdinalIgnoreCase) == true)
            || (song.FileName.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Aplica el ordenamiento actual a SongsView.</summary>
    partial void OnSortOrderChanged(SortOrder value)
    {
        ApplySort();
    }

    /// <summary>Re-aplica filtro cuando cambia el SearchQuery.</summary>
    partial void OnSearchQueryChanged(string value)
    {
        SongsView.Refresh();
        OnPropertyChanged(nameof(VisibleCount));
    }

    /// <summary>Configura el SortDescription de SongsView según SortOrder.</summary>
    private void ApplySort()
    {
        using var defer = SongsView.DeferRefresh();
        SongsView.SortDescriptions.Clear();

        var sortDescription = SortOrder switch
        {
            SortOrder.RecentlyAdded => new SortDescription(nameof(Song.DateAddedSec), ListSortDirection.Descending),
            SortOrder.RecentlyPlayed => new SortDescription(nameof(Song.LastPlayedAt), ListSortDirection.Descending),
            SortOrder.Title => new SortDescription(nameof(Song.DisplayTitle), ListSortDirection.Ascending),
            SortOrder.Artist => new SortDescription(nameof(Song.DisplayArtist), ListSortDirection.Ascending),
            SortOrder.Duration => new SortDescription(nameof(Song.DurationMs), ListSortDirection.Ascending),
            _ => new SortDescription(nameof(Song.DateAddedSec), ListSortDirection.Descending)
        };
        SongsView.SortDescriptions.Add(sortDescription);
    }

    /// <summary>Comando: reproducir una canción (lanza SetQueue con toda la lista visible).</summary>
    [RelayCommand]
    private void PlaySong(Song? song)
    {
        if (song == null) return;
        var index = Songs.IndexOf(song);
        if (index >= 0)
        {
            _playback.SetQueue(Songs.ToList(), index, autoPlay: true);
        }
    }

    /// <summary>Comando: toggle favorito de una canción.</summary>
    [RelayCommand]
    private void ToggleFavorite(Song? song)
    {
        if (song == null) return;
        _playlistService.ToggleFavorite(song);

        // Si la playlist seleccionada es Favoritos, refrescar
        if (_selectedPlaylist?.IsFavorites == true)
        {
            RefreshSongs();
        }
    }

    /// <summary>Comando: importar música desde una carpeta (async).</summary>
    [RelayCommand]
    private async Task ImportFromFolderAsync(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return;

        var result = await _library.ImportFromFolderAsync(folderPath);

        // Refrescar UI
        RefreshSongs();
        RefreshPlaylists();

        // Retornar resultado para que la View muestre el MessageBox
        System.Diagnostics.Debug.WriteLine($"[Wave] Import: {result.Added} added, {result.Duplicates} duplicates");
    }

    /// <summary>Comando: crear playlist nueva (pide nombre al usuario).</summary>
    [RelayCommand]
    private void CreatePlaylist(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        _playlistService.CreatePlaylist(name);
        RefreshPlaylists();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // No hay eventos que desuscribir (CollectionViewSource.GetDefaultView es estático)
    }
}
