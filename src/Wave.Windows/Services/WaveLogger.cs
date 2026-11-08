using System.IO;

namespace Wave.Windows.Services;

/// <summary>
/// Logger estático que escribe a %LocalAppData%\Wave\wave.log.
/// Puede ser usado desde cualquier clase (TagReader, LibraryService, AudioEngine, etc.)
/// sin necesidad de inyección de dependencias.
/// </summary>
public static class WaveLogger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Wave", "wave.log");

    private static readonly object _lock = new();

    /// <summary>Escribe un mensaje INFO al log.</summary>
    public static void Info(string message)
    {
        Write("INFO", message);
    }

    /// <summary>Escribe un mensaje WARN al log.</summary>
    public static void Warn(string message)
    {
        Write("WARN", message);
    }

    /// <summary>Escribe un mensaje ERROR al log (con exception opcional).</summary>
    public static void Error(string message, Exception? ex = null)
    {
        var text = ex != null ? $"{message}: {ex}" : message;
        Write("ERROR", text);
    }

    /// <summary>Escribe un mensaje DEBUG al log.</summary>
    public static void Debug(string message)
    {
        Write("DEBUG", message);
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                var logDir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(logDir)) Directory.CreateDirectory(logDir);
                var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line);
            }
        }
        catch
        {
            // Si no podemos loguear, no rompemos la app
        }
    }
}
