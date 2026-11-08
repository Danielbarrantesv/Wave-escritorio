using System.IO;
using System.Text.Json;

namespace Wave.Windows.Services;

/// <summary>
/// Servicio singleton de configuración de la app.
/// Persiste en %LocalAppData%\Wave\settings.json como diccionario JSON.
/// </summary>
public sealed class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Wave", "settings.json");

    private Dictionary<string, JsonElement> _settings = new();
    private readonly object _lock = new();

    public SettingsService()
    {
        Load();
    }

    /// <summary>Tema: "dark" o "light".</summary>
    public string Theme
    {
        get => Get("theme", "dark");
        set => Set("theme", value);
    }

    /// <summary>Color de acento en formato HEX (#RRGGBB).</summary>
    public string AccentColor
    {
        get => Get("accent_color", "#32C98B");
        set => Set("accent_color", value);
    }

    /// <summary>Idioma ISO (es, en, ja, etc.).</summary>
    public string Language
    {
        get => Get("language", "es");
        set => Set("language", value);
    }

    /// <summary>Repetir toda la cola al llegar al final.</summary>
    public bool RepeatAll
    {
        get => Get("repeat_all", false);
        set => Set("repeat_all", value);
    }

    /// <summary>Volumen de 0.0 a 1.0.</summary>
    public float Volume
    {
        get => Get("volume", 1.0f);
        set => Set("volume", value);
    }

    /// <summary>Shuffle activado por defecto al iniciar.</summary>
    public bool ShuffleDefault
    {
        get => Get("shuffle_default", false);
        set => Set("shuffle_default", value);
    }

    /// <summary>Iniciar Wave con Windows.</summary>
    public bool AutostartWithWindows
    {
        get => Get("autostart", false);
        set
        {
            Set("autostart", value);
            UpdateAutostartRegistry(value);
        }
    }

    /// <summary>Carpetas vigiladas para re-importar automáticamente.</summary>
    public List<string> WatchedFolders
    {
        get => Get("watched_folders", new List<string>());
        set => Set("watched_folders", value);
    }

    /// <summary>Obtiene un valor tipado del almacén.</summary>
    public T Get<T>(string key, T defaultValue = default!)
    {
        lock (_lock)
        {
            if (_settings.TryGetValue(key, out var elem))
                return JsonSerializer.Deserialize<T>(elem.GetRawText()) ?? defaultValue;
            return defaultValue;
        }
    }

    /// <summary>Guarda un valor tipado en el almacén y persiste a disco.</summary>
    public void Set<T>(string key, T value)
    {
        lock (_lock)
        {
            _settings[key] = JsonSerializer.SerializeToElement(value);
            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                _settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new();
            }
        }
        catch
        {
            _settings = new();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // swallow: si no podemos guardar settings, no rompemos la app
        }
    }

    private static void UpdateAutostartRegistry(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;
            var exePath = Environment.ProcessPath;
            if (enable && exePath != null)
                key.SetValue("Wave", $"\"{exePath}\"");
            else
                key.DeleteValue("Wave", throwOnMissingValue: false);
        }
        catch
        {
            // ignore
        }
    }
}
