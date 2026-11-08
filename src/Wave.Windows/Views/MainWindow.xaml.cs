using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Wave.Windows.Models;
using Wave.Windows.Services;

namespace Wave.Windows.Views;

/// <summary>
/// Ventana principal de Wave.
/// v0.4.0: integra PlaylistService (sidebar + Favoritos por canción).
/// </summary>
public partial class MainWindow : Window
{
    private readonly PlaybackController _playback;
    private readonly ILibraryService _library;
    private readonly IPlaylistService _playlistService;

    /// <summary>Lista observable de canciones mostradas en el ListView.</summary>
    public ObservableCollection<Song> Songs { get; } = new();

    /// <summary>Lista observable de playlists en el sidebar.</summary>
    public ObservableCollection<Playlist> Playlists { get; } = new();

    /// <summary>Playlist seleccionada en el sidebar (null = todas las canciones).</summary>
    private Playlist? _selectedPlaylist;

    /// <summary>Total de canciones en la biblioteca (para mostrar "X canciones").</summary>
    public int SongCount => Songs.Count;

    public MainWindow()
    {
        InitializeComponent();

        _playback = App.Services.GetRequiredService<PlaybackController>();
        _library = App.Services.GetRequiredService<ILibraryService>();
        _playlistService = App.Services.GetRequiredService<IPlaylistService>();

        // Cargar biblioteca y playlists desde DB al iniciar
        RefreshSongs();
        RefreshPlaylists();

        // DataContext = this (la propia ventana expone Songs + Playlists + SongCount)
        DataContext = this;

        // Bindear el PlayerBar al PlaybackController via Resources
        Resources["Playback"] = _playback;
    }

    /// <summary>Recarga la lista de canciones según la playlist seleccionada.</summary>
    private void RefreshSongs()
    {
        Songs.Clear();

        List<Song> songs;
        if (_selectedPlaylist == null)
        {
            // "Todas" — mostrar biblioteca completa
            songs = _library.GetAllSongs();
        }
        else
        {
            // Playlist específica — mostrar solo sus canciones
            songs = _playlistService.GetSongs(_selectedPlaylist.Id);
        }

        foreach (var song in songs)
        {
            Songs.Add(song);
        }
    }

    /// <summary>Recarga la lista de playlists en el sidebar.</summary>
    private void RefreshPlaylists()
    {
        Playlists.Clear();
        foreach (var playlist in _playlistService.GetAllSorted())
        {
            Playlists.Add(playlist);
        }
    }

    /// <summary>Botón "Agregar música" del TopBar (abre folder picker).</summary>
    private async void AddMusic_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Selecciona la carpeta de música",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true) return;

        var folderPath = dialog.FolderName;
        if (string.IsNullOrEmpty(folderPath)) return;

        Title = $"Wave — Importando desde {Path.GetFileName(folderPath)}...";

        try
        {
            _library.ScanProgress += OnScanProgress;
            var result = await _library.ImportFromFolderAsync(folderPath);
            _library.ScanProgress -= OnScanProgress;

            // Refrescar UI: lista de canciones + sidebar de playlists
            RefreshSongs();
            RefreshPlaylists();
            Title = "Wave";

            MessageBox.Show(
                $"Importación completada:\n\n" +
                $"• {result.Added} canciones nuevas agregadas\n" +
                $"• {result.Duplicates} duplicadas ignoradas\n" +
                $"• Total procesadas: {result.Total}\n\n" +
                (result.Added > 0 ? "Se crearon playlists automáticas por género." : ""),
                "Wave — Importación",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _library.ScanProgress -= OnScanProgress;
            Title = "Wave";
            MessageBox.Show(
                $"Error al importar música: {ex.Message}",
                "Wave - Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnScanProgress(object? sender, (int Current, int Total) e)
    {
        Dispatcher.Invoke(() =>
        {
            Title = $"Wave — Escaneando {e.Current}/{e.Total} archivos...";
        });
    }

    /// <summary>Drag&drop de archivos o carpetas.</summary>
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;

        var allFiles = new List<string>();
        foreach (var path in files)
        {
            if (Directory.Exists(path))
                allFiles.AddRange(_library.Scan(path));
            else if (File.Exists(path) && _library.IsAudioFile(path))
                allFiles.Add(path);
        }

        if (allFiles.Count == 0)
        {
            MessageBox.Show("No se encontraron archivos de audio soportados.",
                "Wave", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var result = await _library.ImportFilesAsync(allFiles);
            RefreshSongs();
            RefreshPlaylists();

            MessageBox.Show(
                $"Importación: {result.Added} nuevas, {result.Duplicates} duplicadas.",
                "Wave — Drag & Drop",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}", "Wave - Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Doble-click en canción → reproducir.</summary>
    private void SongRow_MouseDoubleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not Song song) return;

        _playback.SetQueue(Songs.ToList(), Songs.IndexOf(song), autoPlay: true);
    }

    /// <summary>Click en el botón Favorito (corazón) → toggle favorito.</summary>
    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not Song song) return;

        _playlistService.ToggleFavorite(song);

        // Refrescar playlist de Favoritos si está seleccionada
        if (_selectedPlaylist?.IsFavorites == true)
        {
            RefreshSongs();
        }
    }

    /// <summary>Click en una playlist del sidebar → mostrar sus canciones.</summary>
    private void PlaylistsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PlaylistsList.SelectedItem is Playlist selected)
        {
            _selectedPlaylist = selected;
            RefreshSongs();
        }
    }

    // === PlayerBar handlers ===
    private void PlayPause_Click(object sender, RoutedEventArgs e) => _playback.TogglePlayPause();
    private void Next_Click(object sender, RoutedEventArgs e) => _playback.SkipToNext();
    private void Prev_Click(object sender, RoutedEventArgs e) => _playback.SkipToPrevious();
    private void Shuffle_Click(object sender, RoutedEventArgs e) => _playback.ToggleShuffle();
    private void Repeat_Click(object sender, RoutedEventArgs e) => _playback.ToggleRepeatOne();
}
