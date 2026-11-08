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
/// v0.3.0: integra LibraryService para importar y mostrar biblioteca.
/// </summary>
public partial class MainWindow : Window
{
    private readonly PlaybackController _playback;
    private readonly ILibraryService _library;

    /// <summary>Lista observable de canciones para que la UI bindee.</summary>
    public ObservableCollection<Song> Songs { get; } = new();

    /// <summary>Total de canciones (para mostrar "X canciones" en la UI).</summary>
    public int SongCount => Songs.Count;

    public MainWindow()
    {
        InitializeComponent();

        _playback = App.Services.GetRequiredService<PlaybackController>();
        _library = App.Services.GetRequiredService<ILibraryService>();

        // Cargar biblioteca existente desde DB al iniciar
        foreach (var song in _library.GetAllSongs())
        {
            Songs.Add(song);
        }

        // DataContext = this (la propia ventana expone Songs + SongCount)
        DataContext = this;

        // Bindear el PlayerBar al PlaybackController via Resources
        Resources["Playback"] = _playback;
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

        // Mostrar estado de carga en el contenido
        Title = $"Wave — Importando desde {Path.GetFileName(folderPath)}...";

        try
        {
            // Suscribirse al progreso para actualizar título
            _library.ScanProgress += OnScanProgress;

            var result = await _library.ImportFromFolderAsync(folderPath);

            _library.ScanProgress -= OnScanProgress;

            // Refrescar la lista observable
            Songs.Clear();
            foreach (var song in _library.GetAllSongs())
            {
                Songs.Add(song);
            }

            Title = "Wave";

            MessageBox.Show(
                $"Importación completada:\n\n" +
                $"• {result.Added} canciones nuevas agregadas\n" +
                $"• {result.Duplicates} duplicadas ignoradas\n" +
                $"• Total procesadas: {result.Total}",
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
        // Actualizar título con progreso (thread-safe via Dispatcher)
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

        // Recopilar: si es carpeta, escanear; si es archivo, agregar directo
        var allFiles = new List<string>();
        foreach (var path in files)
        {
            if (Directory.Exists(path))
            {
                allFiles.AddRange(_library.Scan(path));
            }
            else if (File.Exists(path) && _library.IsAudioFile(path))
            {
                allFiles.Add(path);
            }
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

            // Refrescar
            Songs.Clear();
            foreach (var song in _library.GetAllSongs())
            {
                Songs.Add(song);
            }

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

    /// <summary>Feedback visual al arrastrar archivos.</summary>
    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Click en canción → reproducir.</summary>
    private void SongRow_MouseDoubleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not Song song) return;

        // Reproducir esta canción como cola completa
        _playback.SetQueue(Songs.ToList(), Songs.IndexOf(song), autoPlay: true);
    }

    // === PlayerBar handlers ===
    private void PlayPause_Click(object sender, RoutedEventArgs e) => _playback.TogglePlayPause();
    private void Next_Click(object sender, RoutedEventArgs e) => _playback.SkipToNext();
    private void Prev_Click(object sender, RoutedEventArgs e) => _playback.SkipToPrevious();
    private void Shuffle_Click(object sender, RoutedEventArgs e) => _playback.ToggleShuffle();
    private void Repeat_Click(object sender, RoutedEventArgs e) => _playback.ToggleRepeatOne();
}
