using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Wave.Windows.Models;

namespace Wave.Windows.Services;

/// <summary>
/// Servicio que gestiona el icono de bandeja del sistema (system tray).
/// Muestra un NotifyIcon con menú contextual: Play/Pausa, Siguiente, Anterior,
/// Mostrar ventana, Salir.
///
/// El tooltip del icono se actualiza dinámicamente cuando cambia CurrentSong.
///
/// Nota: usa WinForms NotifyIcon (WPF no tiene equivalente nativo).
/// Por eso el csproj tiene UseWindowsForms=true.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly PlaybackController _playback;
    private bool _disposed;

    public TrayService(PlaybackController playback)
    {
        _playback = playback;
        _notifyIcon = new NotifyIcon
        {
            Icon = CreateDefaultIcon(),
            Visible = true,
            Text = "Wave"
        };
        BuildContextMenu();

        // Actualizar tooltip cuando cambia la canción actual
        _playback.PropertyChanged += OnPlaybackPropertyChanged;
    }

    private void OnPlaybackPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaybackController.CurrentSong))
        {
            UpdateTooltip();
        }
    }

    /// <summary>Construye el menú contextual del icono.</summary>
    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Play/Pausa", null, (s, e) => _playback.TogglePlayPause());
        menu.Items.Add("Siguiente", null, (s, e) => _playback.SkipToNext());
        menu.Items.Add("Anterior", null, (s, e) => _playback.SkipToPrevious());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Mostrar ventana", null, (s, e) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (s, e) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Actualiza el tooltip del icono con el título + artista de la canción actual.</summary>
    private void UpdateTooltip()
    {
        var song = _playback.CurrentSong;
        var text = song != null
            ? Truncate($"{song.DisplayTitle} — {song.DisplayArtist}", 63)
            : "Wave";
        // NotifyIcon.Text tiene límite de 63 caracteres
        _notifyIcon.Text = text;
    }

    /// <summary>Trunca un string a max caracteres (NotifyIcon.Text tiene límite 63).</summary>
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>
    /// Crea un icono default (cuadrado verde accent) si no hay archivo .ico disponible.
    /// En producción, se debería cargar desde Assets/tray.ico.
    /// </summary>
    private static Icon CreateDefaultIcon()
    {
        // Intentar cargar desde recursos embebidos (Assets/tray.ico)
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tray.ico");
            if (File.Exists(iconPath))
                return new Icon(iconPath);
        }
        catch
        {
            // Fallback abajo
        }

        // Fallback: icono cuadrado verde generado en memoria (16x16)
        var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.FromArgb(50, 201, 139)); // #32C98B (AccentBrush)
            g.FillEllipse(Brushes.White, 4, 4, 8, 8); // círculo blanco central
        }
        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    /// <summary>Se dispara cuando el usuario hace doble-click o elige "Mostrar ventana".</summary>
    public event EventHandler? ShowMainWindowRequested;

    /// <summary>Se dispara cuando el usuario elige "Salir".</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Oculta el icono de la bandeja.</summary>
    public void Hide()
    {
        _notifyIcon.Visible = false;
    }

    /// <summary>Muestra el icono de la bandeja.</summary>
    public void Show()
    {
        _notifyIcon.Visible = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _playback.PropertyChanged -= OnPlaybackPropertyChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
