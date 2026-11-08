using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Wave.Windows.Models;
using Wave.Windows.Services;
using Wave.Windows.ViewModels;
using Wave.Windows.Views.Controls;

namespace Wave.Windows.Views;

/// <summary>
/// Ventana principal de Wave.
/// v0.6.0: integra MainViewModel (búsqueda + sort), PlayerBar custom y lyrics.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ILibraryService _library;
    private readonly IPlaylistService _playlistService;
    private readonly PlaybackController _playback;
    private readonly AudioEngine _audioEngine;
    private readonly LyricsViewModel _lyricsViewModel;
    private readonly EqualizerViewModel _equalizerViewModel;
    private readonly IBackupService _backupService;
    private LyricsWindow? _lyricsWindow;
    private EqualizerWindow? _equalizerWindow;
    private MiniPlayerWindow? _miniPlayerWindow;
    private bool _isExiting = false;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        _library = App.Services.GetRequiredService<ILibraryService>();
        _playlistService = App.Services.GetRequiredService<IPlaylistService>();
        _playback = App.Services.GetRequiredService<PlaybackController>();
        _audioEngine = App.Services.GetRequiredService<AudioEngine>();
        _lyricsViewModel = App.Services.GetRequiredService<LyricsViewModel>();
        _equalizerViewModel = App.Services.GetRequiredService<EqualizerViewModel>();
        _backupService = App.Services.GetRequiredService<IBackupService>();

        DataContext = _viewModel;

        // Inicializar PlayerBar con el PlaybackController
        PlayerBarControl.DataContext = _playback;

        // Suscribirse a cambios de VisibleCount para actualizar el label
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateSongCountLabel();

        // Buscar placeholder (cuando SearchBox está vacío, mostrar "Buscar canciones...")
        SearchBox.GotFocus += (s, e) => { if (SearchBox.Text == "") SearchBox.Tag = ""; };
        UpdateSortLabel();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.VisibleCount) || e.PropertyName == nameof(MainViewModel.SongCount))
        {
            UpdateSongCountLabel();
        }
        else if (e.PropertyName == nameof(MainViewModel.SortOrder))
        {
            UpdateSortLabel();
        }
    }

    /// <summary>Actualiza el label "X de Y canciones" según VisibleCount y SongCount.</summary>
    private void UpdateSongCountLabel()
    {
        var visible = _viewModel.VisibleCount;
        var total = _viewModel.SongCount;

        SongCountLabel.Text = total == 0
            ? "0 canciones"
            : visible == total
                ? $"{total} canciones"
                : $"{visible} de {total} canciones";
    }

    /// <summary>Actualiza el label del chip de sort según SortOrder actual.</summary>
    private void UpdateSortLabel()
    {
        SortLabel.Text = _viewModel.SortOrder switch
        {
            SortOrder.RecentlyAdded => "Recientes",
            SortOrder.RecentlyPlayed => "Reproducidas",
            SortOrder.Title => "Título",
            SortOrder.Artist => "Artista",
            SortOrder.Duration => "Duración",
            _ => "Recientes"
        };
    }

    /// <summary>Botón "Agregar música" — abre folder picker.</summary>
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

            // Refrescar ViewModel
            _viewModel.SelectPlaylist(null); // Volver a "todas"
            _viewModel.RefreshSongs();
            _viewModel.RefreshPlaylists();

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
            _viewModel.SelectPlaylist(null);
            _viewModel.RefreshSongs();
            _viewModel.RefreshPlaylists();

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

        var index = _viewModel.Songs.IndexOf(song);
        if (index >= 0)
        {
            _playback.SetQueue(_viewModel.Songs.ToList(), index, autoPlay: true);
        }
    }

    /// <summary>Click en el botón Favorito (corazón) → toggle favorito.</summary>
    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not Song song) return;
        _viewModel.ToggleFavoriteCommand.Execute(song);
    }

    /// <summary>Click en playlist del sidebar → filtrar canciones por playlist.</summary>
    private void PlaylistsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlaylistsList.SelectedItem is Playlist selected)
        {
            _viewModel.SelectPlaylist(selected);
            UpdateSongCountLabel();
        }
    }

    /// <summary>Botón "Configuración" (placeholder).</summary>
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Configuración estará disponible en v1.0.0",
            "Wave", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Botón "Letra" del TopBar — abre LyricsWindow.</summary>
    private void Lyrics_Click(object sender, RoutedEventArgs e)
    {
        if (_lyricsWindow == null || !_lyricsWindow.IsLoaded)
        {
            _lyricsWindow = new LyricsWindow(_lyricsViewModel);
            _lyricsWindow.Owner = this;
            _lyricsWindow.Closed += (s, args) => _lyricsWindow = null;
        }
        _lyricsWindow.Show();
        _lyricsWindow.Activate();
    }

    /// <summary>Botón "EQ" del TopBar — abre EqualizerWindow.</summary>
    private void Equalizer_Click(object sender, RoutedEventArgs e)
    {
        if (_equalizerWindow == null || !_equalizerWindow.IsLoaded)
        {
            _equalizerWindow = new EqualizerWindow(_equalizerViewModel);
            _equalizerWindow.Owner = this;
            _equalizerWindow.Closed += (s, args) => _equalizerWindow = null;
        }
        _equalizerWindow.Show();
        _equalizerWindow.Activate();
    }

    /// <summary>Botón "Mini" — abre MiniPlayerWindow flotante.</summary>
    private void MiniPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (_miniPlayerWindow == null || !_miniPlayerWindow.IsLoaded)
        {
            _miniPlayerWindow = new MiniPlayerWindow(_playback);
            _miniPlayerWindow.Closed += (s, args) => _miniPlayerWindow = null;
        }
        _miniPlayerWindow.Show();
        _miniPlayerWindow.Activate();
    }

    /// <summary>Botón "Backup" — exporta o importa biblioteca.</summary>
    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "¿Quieres exportar (Sí) o importar (No) la biblioteca?\n\n" +
            "Exportar: guarda todas las canciones + playlists en JSON.\n" +
            "Importar: reemplaza la biblioteca actual con un JSON.",
            "Wave — Backup / Restore",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON|*.json",
            DefaultExt = ".json",
            FileName = result == MessageBoxResult.Yes ? "wave-backup.json" : ""
        };

        if (result == MessageBoxResult.No)
        {
            // Importar → abrir en su lugar OpenFileDialog
            var openDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON|*.json",
                Title = "Selecciona el archivo de backup a importar"
            };
            if (openDialog.ShowDialog() != true) return;

            try
            {
                await _backupService.ImportAsync(openDialog.FileName);
                _viewModel.RefreshSongs();
                _viewModel.RefreshPlaylists();
                MessageBox.Show("Biblioteca importada correctamente.", "Wave",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al importar: {ex.Message}", "Wave - Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return;
        }

        // Exportar
        if (dialog.ShowDialog() != true) return;

        try
        {
            await _backupService.ExportAsync(dialog.FileName);
            MessageBox.Show($"Biblioteca exportada a:\n{dialog.FileName}", "Wave",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al exportar: {ex.Message}", "Wave - Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Llamado por TrayService cuando el usuario hace doble-click en el icono
    /// o elige "Mostrar ventana" del menú contextual.
    /// Restaura la ventana desde minimizado/tray y la activa.
    /// </summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;  // Traer al frente
        Topmost = false; // Volver a normal
    }

    /// <summary>
    /// Al cerrar la ventana con la X, preguntar si minimizar al tray o salir.
    /// Si el usuario elige salir, cerrar de verdad (no quedarse pegado en memoria).
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Si ya estamos saliendo (via tray "Salir" o Shutdown), permitir el cierre
        if (_isExiting)
        {
            base.OnClosing(e);
            return;
        }

        // Si hay tray, preguntar si minimizar o salir
        if (App.Services.GetService<TrayService>() is not null)
        {
            var result = MessageBox.Show(
                "¿Quieres minimizar Wave a la bandeja (Sí) o cerrar la aplicación (No)?",
                "Wave — Cerrar",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Minimizar al tray
                e.Cancel = true;
                Hide();
                return;
            }
            else if (result == MessageBoxResult.No)
            {
                // Salir de verdad
                _isExiting = true;
                // Asegurar que el proceso termine
                base.OnClosing(e);
                Environment.Exit(0);
                return;
            }
            else
            {
                // Cancel: no hacer nada
                e.Cancel = true;
                return;
            }
        }

        base.OnClosing(e);
        Environment.Exit(0);
    }

    // === Sort chip ===
    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        // Mostrar ContextMenu bajo el botón
        SortMenu.PlacementTarget = SortChip;
        SortMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        SortMenu.IsOpen = true;
    }

    private void SortRecentlyAdded_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SortOrder = SortOrder.RecentlyAdded;
        UpdateSortLabel();
    }

    private void SortRecentlyPlayed_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SortOrder = SortOrder.RecentlyPlayed;
        UpdateSortLabel();
    }

    private void SortTitle_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SortOrder = SortOrder.Title;
        UpdateSortLabel();
    }

    private void SortArtist_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SortOrder = SortOrder.Artist;
        UpdateSortLabel();
    }

    private void SortDuration_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SortOrder = SortOrder.Duration;
        UpdateSortLabel();
    }
}
