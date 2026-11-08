# Wave for Windows — Documento de Especificación Técnica

> **Propósito**: Especificación completa para que una IA o desarrollador implemente un reproductor de música para Windows similar a la app Android "Wave" (github.com/Danielbarrantesv/wave-app).
>
> **Audiencia**: IA de desarrollo de código (Cursor, Copilot, Claude, etc.) o desarrollador humano con experiencia en C#/.NET.
>
> **Versión del documento**: 1.0 — Noviembre 2026
>
> **Basado en**: Wave Android v2.5.24

---

## Tabla de contenidos

1. [Resumen ejecutivo y objetivos](#1-resumen-ejecutivo-y-objetivos)
2. [Stack tecnológico](#2-stack-tecnológico)
3. [Arquitectura general](#3-arquitectura-general)
4. [Estructura del proyecto](#4-estructura-del-proyecto)
5. [Modelo de datos y DB](#5-modelo-de-datos-y-db)
6. [Motor de playback (NAudio)](#6-motor-de-playback-naudio)
7. [Biblioteca musical — scanner e importer](#7-biblioteca-musical--scanner-e-importer)
8. [Tags y metadatos (TagLib#)](#8-tags-y-metadatos-taglib)
9. [Sistema de playlists](#9-sistema-de-playlists)
10. [Letras sincronizadas (lrclib.net)](#10-letras-sincronizadas-lrclibnet)
11. [Ecualizador de audio](#11-ecualizador-de-audio)
12. [Backup y restore](#12-backup-y-restore)
13. [System tray icon](#13-system-tray-icon)
14. [SMTC + media keys](#14-smtc--media-keys)
15. [Hotkeys globales](#15-hotkeys-globales)
16. [Mini player flotante](#16-mini-player-flotante)
17. [Drag & drop](#17-drag--drop)
18. [Visualizador de audio](#18-visualizador-de-audio)
19. [UI — pantallas WPF](#19-ui--pantallas-wpf)
20. [Settings y configuración](#20-settings-y-configuración)
21. [Build, empaquetado y deployment](#21-build-empaquetado-y-deployment)
22. [Criterios de aceptación y QA](#22-criterios-de-aceptación-y-qa)

---

## 1. Resumen ejecutivo y objetivos

### 1.1 Visión

Wave for Windows es un reproductor de música **offline** para Windows 10/11, escrito en C#/.NET 8 con WPF. Reproduce archivos locales (MP3, FLAC, AAC, OGG, M4A, WAV), organiza la biblioteca con metadatos extraídos de los tags, crea playlists automáticas por género, muestra letras sincronizadas desde lrclib.net, e integra controles del sistema (SMTC, media keys, system tray).

### 1.2 Objetivos

| # | Objetivo | Criterio de éxito |
|---|---|---|
| O1 | Reproducir música local con calidad | Soporta MP3/FLAC/AAC/OGG/M4A/WAV via NAudio |
| O2 | Biblioteca organizada | Scanner recursivo, metadatos completos, deduplicación |
| O3 | Playlists flexibles | Manuales + Favoritos + automáticas por género |
| O4 | Letras sincronizadas | Auto-búsqueda en lrclib.net, cache, scroll sincronizado |
| O5 | Integración con Windows | SMTC, media keys, system tray, hotkeys globales |
| O6 | UI moderna y fluida | WPF con MVVM, tema claro/oscuro, mini player flotante |
| O7 | Resiliencia | Auto-recovery de playback, persistencia de cola |
| O8 | Performance | Biblioteca de 10k canciones carga en <3s, seek sin lag |

### 1.3 Diferencias clave con la versión Android

| En Android (Wave) | En Windows (este proyecto) |
|---|---|
| SAF (Storage Access Framework) para permisos de carpeta | Acceso directo al filesystem (sin permisos especiales) |
| MediaStore.Audio.Media para listar canciones | `Directory.GetFiles(folder, "*.mp3", SearchOption.AllDirectories)` |
| MediaMetadataRetriever para tags | TagLib# o ATL.NET |
| MediaSession (Bluetooth, Android Auto) | SMTC (System Media Transport Controls) |
| WakeLock para background playback | No necesario (desktop no se duerme igual) |
| Persistable URI permissions | No necesario (paths absolutos) |
| Intent.ACTION_SEND para compartir | Windows Share contract o clipboard |
| Jetpack Compose (Kotlin) | WPF con XAML + C# |
| Room (SQLite) | SQLite con EF Core o Dapper |
| ExoPlayer (Media3) | NAudio (WaveOutEvent + MediaFoundationReader) |

### 1.4 No-objeticos (out of scope)

- ❌ Streaming de música online (Spotify, Apple Music)
- ❌ Sync de biblioteca entre dispositivos
- ❌ Edición avanzada de tags (solo lectura)
- ❌ Rip CD de audio
- ❌ Conversión de formatos

<!-- END_PART_1 -->

---

## 2. Stack tecnológico

### 2.1 Tecnologías principales

| Componente | Tecnología | Versión | Justificación |
|---|---|---|---|
| **Framework** | .NET | 8.0 LTS | Estabilidad, performance, soporte a largo plazo |
| **UI framework** | WPF | .NET 8 built-in | Maduro, flexible, MVVM nativo, XAML |
| **Lenguaje** | C# | 12 | Pattern matching, records, nullable reference types |
| **Motor de audio** | NAudio | 2.2.1 | Gratis, flexible, soporta múltiples formatos via MediaFoundation |
| **Tags/metadatos** | TagLib# | 2.3.0 | Estándar de facto, soporta ID3v1/v2, MP4, FLAC, OGG |
| **Letras** | lrclib.net API | REST | Gratis, sin API key, letras sincronizadas |
| **DB local** | SQLite | 3.45+ | Embebida, sin servidor, performante |
| **ORM** | Dapper | 2.1.35 | Lightweight, micro-ORM, performante |
| **HTTP client** | HttpClient | .NET built-in | Para lrclib.net API |
| **JSON** | System.Text.Json | .NET built-in | Para backup/restore y cache de letras |
| **Logging** | Serilog | 3.1.1 | Structured logging, sinks a archivo y consola |
| **DI** | Microsoft.Extensions.DependencyInjection | 8.0 | Built-in, estándar |
| **MVVM** | CommunityToolkit.Mvvm | 8.2.2 | ObservableObject, RelayCommand, source generators |

### 2.2 Paquetes NuGet

```xml
<!-- Wave.Windows.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <Version>1.0.0</Version>
    <AssemblyName>Wave</AssemblyName>
    <RootNamespace>Wave.Windows</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NAudio" Version="2.2.1" />
    <PackageReference Include="TagLibSharp" Version="2.3.0" />
    <PackageReference Include="Dapper" Version="2.1.35" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.0" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.2.2" />
    <PackageReference Include="Serilog" Version="3.1.1" />
    <PackageReference Include="Serilog.Sinks.File" Version="5.0.0" />
  </ItemGroup>
</Project>
```

### 2.3 APIs externas

| API | Endpoint | Uso | Auth |
|---|---|---|---|
| lrclib.net | `https://lrclib.net/api/get` | Búsqueda de letras sincronizadas | Sin auth, rate limit ~100 req/min |

**Ejemplo request lrclib.net**:
```
GET https://lrclib.net/api/get?artist_name=Pink%20Floyd&track_name=Comfortably%20Numb&album_name=The%20Wall&duration=384
```

---

## 3. Arquitectura general

### 3.1 Capas

```
┌─────────────────────────────────────────────────────────┐
│  UI Layer (WPF, XAML, ViewModels)                        │
│  - MainWindow, LibraryView, NowPlayingView, etc.         │
│  - MiniPlayerWindow, TrayIcon                            │
└────────────────────────┬────────────────────────────────┘
                         │ data binding (ObservableCollection)
┌────────────────────────▼────────────────────────────────┐
│  Application Layer (Services)                            │
│  - PlaybackController (singleton)                       │
│  - LibraryService (scanner, importer)                    │
│  - PlaylistService (CRUD, género)                        │
│  - LyricsService (lrclib.net client)                     │
│  - EqualizerService                                      │
│  - BackupService                                         │
│  - SmtcService, HotkeyService, TrayService               │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│  Domain Layer (Models, Entities)                         │
│  - Song, Playlist, PlaylistSong                          │
│  - PlaybackState, ParsedLyrics                           │
│  - ImportResult, SortOrder                               │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│  Infrastructure Layer (DB, Files, External)              │
│  - SqliteDatabase (Dapper)                               │
│  - SongRepository, PlaylistRepository                    │
│  - TagReader (TagLib#)                                   │
│  - AudioEngine (NAudio)                                  │
│  - LrcLibClient (HttpClient)                             │
└─────────────────────────────────────────────────────────┘
```

### 3.2 Patrones de diseño

| Patrón | Dónde se usa |
|---|---|
| **MVVM** (Model-View-ViewModel) | UI WPF completa |
| **Singleton** | PlaybackController, LibraryService, SettingsService |
| **Repository** | SongRepository, PlaylistRepository |
| **Observer** | INotifyPropertyChanged, ObservableCollection |
| **Command** | RelayCommand (CommunityToolkit.Mvvm) |
| **Factory** | AudioEngine crea WaveOutEvent + reader según formato |
| **Strategy** | Deduplicación (por path, name+size, metadata, fingerprint) |

### 3.3 Flujo de datos principal

**Reproducción de una canción:**
```
Usuario click canción
  → LibraryViewModel.PlayCommand(song)
  → PlaybackController.SetQueue([song], startIndex=0, autoPlay=true)
  → AudioEngine.Play(mediaUri)
  → NAudio WaveOutEvent + MediaFoundationReader
  → Audio sale por dispositivo de output
  → PlaybackController.State (ObservableCollection) actualiza
  → UI (PlayerBar, NowPlayingView) refleja via data binding
  → SMTC actualiza metadata del sistema
  → TrayIcon actualiza tooltip
```

**Importar música:**
```
Usuario selecciona carpeta (FolderBrowserDialog)
  → LibraryViewModel.ImportFromFolder(folderPath)
  → LibraryService.Scan(folderPath)
  → Para cada archivo de audio:
    → TagReader.Read(filePath) → Song con metadatos
    → Deduplicar contra DB existente
    → SongRepository.Insert(song) si no es duplicado
  → PlaylistService.CreateGenrePlaylists() (automático)
  → LibraryViewModel.Songs (ObservableCollection) actualiza
  → UI refleja nueva biblioteca
```

---

## 4. Estructura del proyecto

### 4.1 Solution structure

```
Wave.Windows.sln
├── src/
│   ├── Wave.Windows/                    # Proyecto principal (WPF)
│   │   ├── App.xaml / App.xaml.cs
│   │   ├── app.manifest
│   │   ├── Wave.Windows.csproj
│   │   │
│   │   ├── Models/                      # Domain models
│   │   │   ├── Song.cs
│   │   │   ├── Playlist.cs
│   │   │   ├── PlaylistSong.cs
│   │   │   ├── PlaybackState.cs
│   │   │   ├── ParsedLyrics.cs
│   │   │   ├── ImportResult.cs
│   │   │   └── SortOrder.cs
│   │   │
│   │   ├── Data/                        # Infrastructure - DB
│   │   │   ├── SqliteDatabase.cs
│   │   │   ├── Migrations/
│   │   │   │   └── 001_initial.sql
│   │   │   └── Repositories/
│   │   │       ├── SongRepository.cs
│   │   │       └── PlaylistRepository.cs
│   │   │
│   │   ├── Services/                    # Application services
│   │   │   ├── PlaybackController.cs
│   │   │   ├── AudioEngine.cs           # NAudio wrapper
│   │   │   ├── LibraryService.cs
│   │   │   ├── PlaylistService.cs
│   │   │   ├── LyricsService.cs
│   │   │   ├── EqualizerService.cs
│   │   │   ├── BackupService.cs
│   │   │   ├── SettingsService.cs
│   │   │   ├── SmtcService.cs
│   │   │   ├── HotkeyService.cs
│   │   │   └── TrayService.cs
│   │   │
│   │   ├── Audio/                       # Audio-specific
│   │   │   ├── TagReader.cs             # TagLib# wrapper
│   │   │   ├── Fingerprinter.cs         # Chromaprint wrapper
│   │   │   └── VisualizerEngine.cs      # FFT para visualizador
│   │   │
│   │   ├── External/                    # External APIs
│   │   │   └── LrcLibClient.cs
│   │   │
│   │   ├── ViewModels/                  # MVVM ViewModels
│   │   │   ├── MainViewModel.cs
│   │   │   ├── LibraryViewModel.cs
│   │   │   ├── NowPlayingViewModel.cs
│   │   │   ├── PlaylistViewModel.cs
│   │   │   ├── SearchViewModel.cs
│   │   │   ├── EqualizerViewModel.cs
│   │   │   ├── LyricsViewModel.cs
│   │   │   ├── SettingsViewModel.cs
│   │   │   └── MiniPlayerViewModel.cs
│   │   │
│   │   ├── Views/                       # WPF Views (XAML)
│   │   │   ├── MainWindow.xaml
│   │   │   ├── LibraryView.xaml
│   │   │   ├── NowPlayingView.xaml
│   │   │   ├── PlaylistView.xaml
│   │   │   ├── SearchView.xaml
│   │   │   ├── EqualizerView.xaml
│   │   │   ├── LyricsView.xaml
│   │   │   ├── SettingsView.xaml
│   │   │   ├── MiniPlayerWindow.xaml
│   │   │   └── Controls/
│   │   │       ├── PlayerBar.xaml
│   │   │       ├── SongRow.xaml
│   │   │       ├── PlaylistCard.xaml
│   │   │       └── SpectrumVisualizer.xaml
│   │   │
│   │   ├── Converters/                  # WPF value converters
│   │   │   ├── TimeSpanToTextConverter.cs
│   │   │   ├── BoolToPlayPauseIconConverter.cs
│   │   │   └── InverseBoolConverter.cs
│   │   │
│   │   ├── Themes/                      # Estilos y recursos
│   │   │   ├── DarkTheme.xaml
│   │   │   ├── LightTheme.xaml
│   │   │   ├── AccentColors.xaml
│   │   │   └── Styles.xaml
│   │   │
│   │   └── Assets/                      # Iconos, imágenes
│   │       ├── app.ico
│   │       ├── tray.ico
│   │       └── default_cover.png
│   │
│   └── Wave.Windows.Tests/              # Tests (xUnit)
│       ├── PlaybackControllerTests.cs
│       ├── LibraryServiceTests.cs
│       ├── TagReaderTests.cs
│       └── LrcLibClientTests.cs
│
├── docs/
│   └── Wave_Windows_Specification.md    # Este documento
│
├── .editorconfig
├── .gitignore
└── README.md
```

### 4.2 Namespaces

```csharp
namespace Wave.Windows.Models          // Domain models
namespace Wave.Windows.Data            // DB, repositories
namespace Wave.Windows.Services        // Application services
namespace Wave.Windows.Audio           // Audio-specific (tags, fingerprint)
namespace Wave.Windows.External        // External APIs
namespace Wave.Windows.ViewModels      // MVVM ViewModels
namespace Wave.Windows.Views           // WPF Views
namespace Wave.Windows.Converters      // WPF converters
namespace Wave.Windows.Themes          // Styles
```

### 4.3 Convenciones de código

- **C# 12** con nullable reference types habilitado
- **Implicit usings** habilitado
- **PascalCase** para clases, métodos, propiedades públicas
- **camelCase** para variables locales, parámetros
- **_camelCase** para campos privados
- **Archivos**: una clase pública por archivo, nombre = nombre de clase
- **Async**: sufijo `Async` en métodos async (`ScanAsync`, `ImportAsync`)
- **Eventos**: sufijo `EventHandler` para tipos, `OnXxx` para métodos que disparan eventos

<!-- END_PART_2 -->

---

## 5. Modelo de datos y DB

### 5.1 Schema SQLite

```sql
-- 001_initial.sql

CREATE TABLE IF NOT EXISTS songs (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    file_path       TEXT NOT NULL UNIQUE,
    file_name       TEXT NOT NULL,
    file_size       INTEGER NOT NULL DEFAULT 0,
    title           TEXT,
    artist          TEXT,
    album           TEXT,
    duration_ms     INTEGER NOT NULL DEFAULT 0,
    track_no        INTEGER,
    year            INTEGER,
    mime_type       TEXT,
    date_added_sec  INTEGER NOT NULL DEFAULT 0,
    fingerprint     TEXT,
    art_uri         TEXT,          -- path absoluto a portada extraída (jpg en %LocalAppData%\Wave\art\)
    last_modified   INTEGER NOT NULL DEFAULT 0,
    is_favorite     INTEGER NOT NULL DEFAULT 0,  -- 0 o 1 (SQLite no tiene bool)
    last_played_at  INTEGER NOT NULL DEFAULT 0,
    genre           TEXT
);
CREATE INDEX IF NOT EXISTS idx_songs_filepath ON songs(file_path);
CREATE INDEX IF NOT EXISTS idx_songs_fingerprint ON songs(fingerprint);
CREATE INDEX IF NOT EXISTS idx_songs_title_artist ON songs(title, artist);
CREATE INDEX IF NOT EXISTS idx_songs_is_favorite ON songs(is_favorite);
CREATE INDEX IF NOT EXISTS idx_songs_last_played ON songs(last_played_at);
CREATE INDEX IF NOT EXISTS idx_songs_genre ON songs(genre);

CREATE TABLE IF NOT EXISTS playlists (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    name            TEXT NOT NULL UNIQUE,
    description     TEXT,
    created_at      INTEGER NOT NULL DEFAULT 0,
    updated_at      INTEGER NOT NULL DEFAULT 0,
    cover_uri       TEXT,
    folder_path     TEXT           -- si fue importada de una carpeta
);
CREATE INDEX IF NOT EXISTS idx_playlists_name ON playlists(name);

CREATE TABLE IF NOT EXISTS playlist_songs (
    playlist_id     INTEGER NOT NULL,
    song_id         INTEGER NOT NULL,
    position        INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (playlist_id, song_id),
    FOREIGN KEY (playlist_id) REFERENCES playlists(id) ON DELETE CASCADE,
    FOREIGN KEY (song_id)    REFERENCES songs(id)      ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_playlist_songs_position ON playlist_songs(position);
CREATE INDEX IF NOT EXISTS idx_playlist_songs_song_id ON playlist_songs(song_id);
```

### 5.2 Entities (C#)

```csharp
// Models/Song.cs
using System.Text.Json.Serialization;

namespace Wave.Windows.Models;

public class Song
{
    public long Id { get; set; }
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public long DurationMs { get; set; }
    public int? TrackNo { get; set; }
    public int? Year { get; set; }
    public string? MimeType { get; set; }
    public long DateAddedSec { get; set; }
    public string? Fingerprint { get; set; }
    public string? ArtUri { get; set; }
    public long LastModified { get; set; }
    public bool IsFavorite { get; set; }
    public long LastPlayedAt { get; set; }
    public string? Genre { get; set; }

    // Display helpers (no persistidos)
    [JsonIgnore]
    public string DisplayTitle => !string.IsNullOrWhiteSpace(Title) ? Title : FileName;
    [JsonIgnore]
    public string DisplayArtist => !string.IsNullOrWhiteSpace(Artist) && Artist != "<unknown>"
        ? Artist : "Artista desconocido";
    [JsonIgnore]
    public string DisplayAlbum => !string.IsNullOrWhiteSpace(Album) ? Album : "Álbum desconocido";
    [JsonIgnore]
    public string DisplayGenre => !string.IsNullOrWhiteSpace(Genre) ? Genre : "Sin género";
}

// Models/Playlist.cs
namespace Wave.Windows.Models;

public class Playlist
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public string? CoverUri { get; set; }
    public string? FolderPath { get; set; }

    public const string FavoritesName = "Favoritos";
}

// Models/PlaylistSong.cs
namespace Wave.Windows.Models;

public class PlaylistSong
{
    public long PlaylistId { get; set; }
    public long SongId { get; set; }
    public int Position { get; set; }
}
```

### 5.3 SqliteDatabase (singleton con Dapper)

```csharp
// Data/SqliteDatabase.cs
using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Wave.Windows.Data;

public sealed class SqliteDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private static readonly object _lock = new();
    private static SqliteDatabase? _instance;

    public static SqliteDatabase Instance =>
        _instance ?? throw new InvalidOperationException("SqliteDatabase no inicializado. Llamar Init() primero.");

    public static void Init(string dbPath)
    {
        lock (_lock)
        {
            if (_instance != null) return;
            _instance = new SqliteDatabase(dbPath);
            _instance.RunMigrations();
        }
    }

    private SqliteDatabase(string dbPath)
    {
        var connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared";
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        // Habilitar foreign keys (SQLite las desactiva por defecto)
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();
    }

    public IDbConnection Connection => _connection;

    private void RunMigrations()
    {
        var migrationsDir = Path.Combine(AppContext.BaseDirectory, "Data", "Migrations");
        if (!Directory.Exists(migrationsDir))
        {
            // Si no están embebidas, ejecutar SQL inline
            RunInlineMigrations();
            return;
        }
        foreach (var file in Directory.GetFiles(migrationsDir, "*.sql").OrderBy(f => f))
        {
            var sql = File.ReadAllText(file);
            _connection.Execute(sql);
        }
    }

    private void RunInlineMigrations()
    {
        // Schema completo en una sola transacción
        var sql = """
            CREATE TABLE IF NOT EXISTS songs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL UNIQUE,
                file_name TEXT NOT NULL,
                file_size INTEGER NOT NULL DEFAULT 0,
                title TEXT, artist TEXT, album TEXT,
                duration_ms INTEGER NOT NULL DEFAULT 0,
                track_no INTEGER, year INTEGER, mime_type TEXT,
                date_added_sec INTEGER NOT NULL DEFAULT 0,
                fingerprint TEXT, art_uri TEXT,
                last_modified INTEGER NOT NULL DEFAULT 0,
                is_favorite INTEGER NOT NULL DEFAULT 0,
                last_played_at INTEGER NOT NULL DEFAULT 0,
                genre TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_songs_filepath ON songs(file_path);
            CREATE INDEX IF NOT EXISTS idx_songs_fingerprint ON songs(fingerprint);
            CREATE INDEX IF NOT EXISTS idx_songs_title_artist ON songs(title, artist);
            CREATE INDEX IF NOT EXISTS idx_songs_is_favorite ON songs(is_favorite);
            CREATE INDEX IF NOT EXISTS idx_songs_last_played ON songs(last_played_at);
            CREATE INDEX IF NOT EXISTS idx_songs_genre ON songs(genre);

            CREATE TABLE IF NOT EXISTS playlists (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE,
                description TEXT,
                created_at INTEGER NOT NULL DEFAULT 0,
                updated_at INTEGER NOT NULL DEFAULT 0,
                cover_uri TEXT, folder_path TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_playlists_name ON playlists(name);

            CREATE TABLE IF NOT EXISTS playlist_songs (
                playlist_id INTEGER NOT NULL,
                song_id INTEGER NOT NULL,
                position INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (playlist_id, song_id),
                FOREIGN KEY (playlist_id) REFERENCES playlists(id) ON DELETE CASCADE,
                FOREIGN KEY (song_id) REFERENCES songs(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_playlist_songs_position ON playlist_songs(position);
            CREATE INDEX IF NOT EXISTS idx_playlist_songs_song_id ON playlist_songs(song_id);
            """;
        _connection.Execute(sql);
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
```

### 5.4 Repositories (Dapper)

```csharp
// Data/Repositories/SongRepository.cs
using System.Data;
using Dapper;
using Wave.Windows.Models;

namespace Wave.Windows.Data.Repositories;

public class SongRepository
{
    private readonly IDbConnection _db;

    public SongRepository() => _db = SqliteDatabase.Instance.Connection;

    public List<Song> ListAll()
    {
        const string sql = "SELECT * FROM songs ORDER BY date_added_sec DESC";
        return _db.Query<Song>(sql).ToList();
    }

    public Song? GetById(long id)
    {
        const string sql = "SELECT * FROM songs WHERE id = @id";
        return _db.QuerySingleOrDefault<Song>(sql, new { id });
    }

    public Song? GetByPath(string filePath)
    {
        const string sql = "SELECT * FROM songs WHERE file_path = @filePath";
        return _db.QuerySingleOrDefault<Song>(sql, new { filePath });
    }

    public long Insert(Song song)
    {
        const string sql = """
            INSERT INTO songs (file_path, file_name, file_size, title, artist, album,
                duration_ms, track_no, year, mime_type, date_added_sec, fingerprint,
                art_uri, last_modified, is_favorite, last_played_at, genre)
            VALUES (@FilePath, @FileName, @FileSize, @Title, @Artist, @Album,
                @DurationMs, @TrackNo, @Year, @MimeType, @DateAddedSec, @Fingerprint,
                @ArtUri, @LastModified, @IsFavorite, @LastPlayedAt, @Genre);
            SELECT last_insert_rowid();
            """;
        return _db.ExecuteScalar<long>(sql, song);
    }

    public void UpdateFavorite(long songId, bool isFavorite)
    {
        const string sql = "UPDATE songs SET is_favorite = @isFavorite WHERE id = @songId";
        _db.Execute(sql, new { isFavorite = isFavorite ? 1 : 0, songId });
    }

    public void UpdateLastPlayedAt(long songId, long timestamp)
    {
        const string sql = "UPDATE songs SET last_played_at = @timestamp WHERE id = @songId";
        _db.Execute(sql, new { timestamp, songId });
    }

    public int Count()
    {
        return _db.ExecuteScalar<int>("SELECT COUNT(*) FROM songs");
    }

    public void DeleteAll()
    {
        _db.Execute("DELETE FROM songs");
    }
}
```

### 5.5 PlaybackState (modelo observable)

```csharp
// Models/PlaybackState.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Wave.Windows.Models;

public partial class PlaybackState : ObservableObject
{
    [ObservableProperty] private ObservableCollection<Song> _playlist = new();
    [ObservableProperty] private int _currentWindow = -1;
    [ObservableProperty] private long _positionMs;
    [ObservableProperty] private long _durationMs;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _isShuffle;
    [ObservableProperty] private bool _repeatOne;
    [ObservableProperty] private bool _hasNext;

    public Song? CurrentSong => Playlist.Count > 0 && CurrentWindow >= 0 && CurrentWindow < Playlist.Count
        ? Playlist[CurrentWindow] : null;
}

// Models/SortOrder.cs
namespace Wave.Windows.Models;

public enum SortOrder
{
    RecentlyAdded,
    RecentlyPlayed,
    Title,
    Artist,
    Duration
}

// Models/ImportResult.cs
namespace Wave.Windows.Models;

public record ImportResult(int Added, int Duplicates, int Total);
```

---

## 6. Motor de playback (NAudio)

### 6.1 AudioEngine — wrapper NAudio

```csharp
// Services/AudioEngine.cs
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO;

namespace Wave.Windows.Services;

public sealed class AudioEngine : IDisposable
{
    private WaveOutEvent? _waveOut;
    private MediaFoundationReader? _reader;
    private ISampleProvider? _sampleProvider;
    private EqualizerSampleProvider? _equalizer;
    private VolumeSampleProvider? _volume;
    private float _volumeLevel = 1.0f;
    private bool _disposed;

    public event EventHandler<PlaybackState>? PlaybackStateChanged;
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;
    public event EventHandler? PositionChanged;

    public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;
    public long PositionMs => _reader?.CurrentTime.TotalMilliseconds ?? 0;
    public long LengthMs => _reader?.TotalTime.TotalMilliseconds ?? 0;
    public EqualizerSampleProvider? Equalizer => _equalizer;

    public float Volume
    {
        get => _volumeLevel;
        set
        {
            _volumeLevel = Math.Clamp(value, 0f, 1f);
            if (_volume != null) _volume.Volume = _volumeLevel;
        }
    }

    /// <summary>
    /// Carga un archivo de audio y prepara el pipeline:
    /// reader → equalizer → volume → waveOut
    /// </summary>
    public void Load(string filePath)
    {
        Dispose();
        _reader = new MediaFoundationReader(filePath);
        // Convertir a sample provider (float 32-bit, 44.1kHz, stereo)
        _sampleProvider = ConvertToSampleProvider(_reader);
        // Pipeline: equalizer → volume
        _equalizer = new EqualizerSampleProvider(_sampleProvider);
        _volume = new VolumeSampleProvider(_equalizer) { Volume = _volumeLevel };
        _waveOut = new WaveOutEvent { DesiredLatency = 200, NumberOfBuffers = 3 };
        _waveOut.Init(_volume);
        _waveOut.PlaybackStopped += (s, e) => PlaybackStopped?.Invoke(s, e);
    }

    private static ISampleProvider ConvertToSampleProvider(IWaveProvider waveProvider)
    {
        // MediaFoundationReader devuelve IWaveProvider; convertir a ISampleProvider
        if (waveProvider.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            return new WaveToSampleProvider(waveProvider);
        }
        return new MediaFoundationResampler(
            waveProvider,
            WaveFormat.CreateIeeeFloatWaveFormat(44100, 2)
        ).ToSampleProvider();
    }

    public void Play()
    {
        if (_waveOut == null) return;
        if (_waveOut.PlaybackState != PlaybackState.Playing)
            _waveOut.Play();
        PlaybackStateChanged?.Invoke(this, NAudio.Wave.PlaybackState.Playing);
    }

    public void Pause()
    {
        if (_waveOut == null) return;
        _waveOut.Pause();
        PlaybackStateChanged?.Invoke(this, NAudio.Wave.PlaybackState.Paused);
    }

    public void Stop()
    {
        if (_waveOut == null) return;
        _waveOut.Stop();
        PlaybackStateChanged?.Invoke(this, NAudio.Wave.PlaybackState.Stopped);
    }

    public void Seek(long positionMs)
    {
        if (_reader == null) return;
        _reader.CurrentTime = TimeSpan.FromMilliseconds(positionMs);
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Timer-driven: actualizar PositionMs desde NAudio para que la UI refleje el seek.
    /// Llamar cada ~200ms desde el PlaybackController.
    /// </summary>
    public void RaisePositionChanged()
    {
        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _waveOut?.Dispose();
        _reader?.Dispose();
        _waveOut = null;
        _reader = null;
        _sampleProvider = null;
        _equalizer = null;
        _volume = null;
    }
}
```

### 6.2 PlaybackController — singleton con cola + shuffle + repeat

```csharp
// Services/PlaybackController.cs
using System.Collections.ObjectModel;
using System.Windows.Threading;
using NAudio.Wave;
using Wave.Windows.Data.Repositories;
using Wave.Windows.Models;

namespace Wave.Windows.Services;

public sealed class PlaybackController : ObservableObject, IDisposable
{
    private readonly AudioEngine _engine = new();
    private readonly DispatcherTimer _positionTimer;
    private readonly SongRepository _songRepo = new();
    private readonly SettingsService _settings;

    // Cola
    private List<Song> _queue = new();
    private int _currentIndex = -1;
    private List<int> _shuffleOrder = new();
    private int _shuffleIndex = -1;

    // Estado observable
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private long _positionMs;
    [ObservableProperty] private long _durationMs;
    [ObservableProperty] private bool _isShuffle;
    [ObservableProperty] private bool _repeatOne;
    [ObservableProperty] private Song? _currentSong;

    public ObservableCollection<Song> Queue { get; } = new();
    public int CurrentIndex => _currentIndex;

    public PlaybackController(SettingsService settings)
    {
        _settings = settings;
        _engine.PlaybackStopped += OnPlaybackStopped;
        _engine.PositionChanged += OnPositionChanged;
        _positionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _positionTimer.Tick += (s, e) =>
        {
            if (_engine.IsPlaying)
            {
                PositionMs = _engine.PositionMs;
                DurationMs = _engine.LengthMs;
                // Actualizar lastPlayedAt cada ~3s
                if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 3 == 0 && _currentSong != null)
                    _songRepo.UpdateLastPlayedAt(_currentSong.Id, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }
        };
        _positionTimer.Start();
    }

    public void SetQueue(List<Song> songs, int startIndex = 0, bool autoPlay = true)
    {
        _queue = songs.ToList();
        _currentIndex = startIndex;
        Queue.Clear();
        foreach (var s in _queue) Queue.Add(s);
        if (_isShuffle) BuildShuffleOrder();
        LoadAndPlay(autoPlay);
    }

    public void Play()
    {
        if (_currentSong == null && _queue.Count > 0)
        {
            _currentIndex = 0;
            LoadAndPlay(true);
        }
        else
        {
            _engine.Play();
            IsPlaying = true;
        }
    }

    public void Pause()
    {
        _engine.Pause();
        IsPlaying = false;
    }

    public void TogglePlayPause()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    public void SkipToNext()
    {
        if (_queue.Count == 0) return;
        if (_isShuffle)
        {
            _shuffleIndex++;
            if (_shuffleIndex >= _shuffleOrder.Count)
            {
                if (_repeatOne || _settings.RepeatAll)
                {
                    _shuffleIndex = 0;
                    BuildShuffleOrder();
                }
                else
                {
                    Pause();
                    return;
                }
            }
            _currentIndex = _shuffleOrder[_shuffleIndex];
        }
        else
        {
            _currentIndex++;
            if (_currentIndex >= _queue.Count)
            {
                if (_repeatOne || _settings.RepeatAll)
                    _currentIndex = 0;
                else
                {
                    Pause();
                    return;
                }
            }
        }
        LoadAndPlay(true);
    }

    public void SkipToPrevious()
    {
        if (_queue.Count == 0) return;
        if (_engine.PositionMs > 3000)
        {
            // Si lleva más de 3s, reiniciar la canción actual
            _engine.Seek(0);
            return;
        }
        if (_isShuffle)
        {
            _shuffleIndex--;
            if (_shuffleIndex < 0) _shuffleIndex = _shuffleOrder.Count - 1;
            _currentIndex = _shuffleOrder[_shuffleIndex];
        }
        else
        {
            _currentIndex--;
            if (_currentIndex < 0) _currentIndex = _queue.Count - 1;
        }
        LoadAndPlay(true);
    }

    public void Seek(long positionMs) => _engine.Seek(positionMs);

    public void ToggleShuffle()
    {
        IsShuffle = !IsShuffle;
        if (IsShuffle) BuildShuffleOrder();
    }

    public void ToggleRepeatOne()
    {
        RepeatOne = !RepeatOne;
    }

    private void BuildShuffleOrder()
    {
        var indices = Enumerable.Range(0, _queue.Count).ToList();
        var rng = new Random();
        for (int i = indices.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }
        // Asegurar que el primer elemento en shuffleOrder sea el actual
        if (_currentIndex >= 0)
        {
            indices.Remove(_currentIndex);
            indices.Insert(0, _currentIndex);
        }
        _shuffleOrder = indices;
        _shuffleIndex = 0;
    }

    private void LoadAndPlay(bool autoPlay)
    {
        if (_currentIndex < 0 || _currentIndex >= _queue.Count) return;
        CurrentSong = _queue[_currentIndex];
        try
        {
            _engine.Load(CurrentSong.FilePath);
            DurationMs = _engine.LengthMs;
            if (autoPlay)
            {
                _engine.Play();
                IsPlaying = true;
            }
        }
        catch (Exception ex)
        {
            // Si el archivo no se puede cargar, saltar al siguiente
            System.Diagnostics.Debug.WriteLine($"Load failed: {ex.Message}");
            SkipToNext();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // Si terminó naturalmente (no por Stop()), saltar al siguiente
        if (e.Exception == null && _engine.PositionMs >= _engine.LengthMs - 100)
        {
            if (RepeatOne)
            {
                _engine.Seek(0);
                _engine.Play();
            }
            else
            {
                SkipToNext();
            }
        }
        else
        {
            IsPlaying = false;
        }
    }

    private void OnPositionChanged(object? sender, EventArgs e)
    {
        PositionMs = _engine.PositionMs;
    }

    /// <summary>
    /// Persistir la cola actual para restaurarla al reabrir la app.
    /// </summary>
    public void SaveQueueToPrefs()
    {
        if (_queue.Count == 0) return;
        var songIds = string.Join(",", _queue.Select(s => s.Id));
        _settings.Set("queue_song_ids", songIds);
        _settings.Set("queue_index", _currentIndex);
        _settings.Set("queue_saved_at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    /// <summary>
    /// Restaurar la cola desde prefs al iniciar la app.
    /// </summary>
    public void RestoreSavedQueue()
    {
        var songIdsStr = _settings.Get<string>("queue_song_ids");
        if (string.IsNullOrEmpty(songIdsStr)) return;
        var ids = songIdsStr.Split(',').Select(s => long.TryParse(s, out var id) ? id : 0).Where(id => id > 0).ToList();
        if (ids.Count == 0) return;
        var allSongs = _songRepo.ListAll();
        var songs = ids.Select(id => allSongs.FirstOrDefault(s => s.Id == id)).Where(s => s != null).Cast<Song>().ToList();
        if (songs.Count == 0) return;
        var savedIndex = _settings.Get<int>("queue_index");
        SetQueue(songs, Math.Clamp(savedIndex, 0, songs.Count - 1), autoPlay: false);
    }

    public void Dispose()
    {
        _positionTimer.Stop();
        SaveQueueToPrefs();
        _engine.Dispose();
    }
}
```

---

## 7. Biblioteca musical — scanner e importer

### 7.1 Extensiones soportadas

```csharp
// Services/LibraryService.cs
using System.IO;
using System.Text.RegularExpressions;
using Wave.Windows.Audio;
using Wave.Windows.Data.Repositories;
using Wave.Windows.Models;

namespace Wave.Windows.Services;

public sealed class LibraryService
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".aac", ".m4a", ".ogg", ".wav", ".wma"
    };

    private readonly SongRepository _songRepo = new();
    private readonly PlaylistRepository _playlistRepo = new();
    private readonly PlaylistService _playlistService;

    public event EventHandler<(int Current, int Total)>? ScanProgress;

    public LibraryService(PlaylistService playlistService)
    {
        _playlistService = playlistService;
    }

    /// <summary>
    /// Escanea una carpeta recursivamente y devuelve la lista de archivos de audio.
    /// </summary>
    public List<string> Scan(string folderPath)
    {
        var files = new List<string>();
        try
        {
            var allFiles = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                if (IsAudioFile(file)) files.Add(file);
            }
        }
        catch (UnauthorizedAccessException) { /* carpeta sin permisos */ }
        catch (DirectoryNotFoundException) { }
        return files;
    }

    public static bool IsAudioFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return AudioExtensions.Contains(ext);
    }

    /// <summary>
    /// Importa una carpeta completa: extrae tags de cada archivo, deduplica, inserta en DB.
    /// </summary>
    public async Task<ImportResult> ImportFromFolderAsync(string folderPath, long? targetPlaylistId = null)
    {
        return await Task.Run(() =>
        {
            var files = Scan(folderPath);
            return ImportFilesInternal(files, targetPlaylistId, folderPath);
        });
    }

    /// <summary>
    /// Importa archivos individuales (ej: drag & drop).
    /// </summary>
    public async Task<ImportResult> ImportFilesAsync(List<string> filePaths, long? targetPlaylistId = null)
    {
        return await Task.Run(() =>
        {
            var audioFiles = filePaths.Where(IsAudioFile).ToList();
            return ImportFilesInternal(audioFiles, targetPlaylistId, null);
        });
    }

    private ImportResult ImportFilesInternal(List<string> files, long? targetPlaylistId, string? folderPath)
    {
        int added = 0, duplicates = 0;
        var total = files.Count;
        var existingSongs = _songRepo.ListAll();
        var existingByPath = existingSongs.ToDictionary(s => s.FilePath);
        var knownPaths = existingSongs.Select(s => s.FilePath).ToHashSet();
        var knownNameSize = existingSongs.Select(s => $"{s.FileName.ToLowerInvariant()}|{s.FileSize}").ToHashSet();
        var knownMeta = existingSongs.Select(s =>
            $"{(s.Title ?? "").ToLowerInvariant()}|{(s.Artist ?? "").ToLowerInvariant()}|{s.DurationMs / 2000}").ToHashSet();

        var playlistAttachIds = new List<long>();

        for (int i = 0; i < files.Count; i++)
        {
            ScanProgress?.Invoke(this, (i + 1, total));
            var filePath = files[i];
            try
            {
                var fileInfo = new FileInfo(filePath);
                var fileName = fileInfo.Name;
                var fileSize = fileInfo.Length;

                // Dedup 1: por path absoluto
                if (knownPaths.Contains(filePath))
                {
                    duplicates++;
                    if (existingByPath.TryGetValue(filePath, out var existing))
                        playlistAttachIds.Add(existing.Id);
                    continue;
                }

                // Dedup 2: por name + size
                var nameSizeKey = $"{fileName.ToLowerInvariant()}|{fileSize}";
                if (knownNameSize.Contains(nameSizeKey))
                {
                    duplicates++;
                    var existing = existingSongs.FirstOrDefault(s => $"{s.FileName.ToLowerInvariant()}|{s.FileSize}" == nameSizeKey);
                    if (existing != null)
                    {
                        playlistAttachIds.Add(existing.Id);
                        knownPaths.Add(filePath);
                    }
                    continue;
                }

                // Extraer metadatos con TagLib#
                var song = TagReader.Read(filePath, fileName, fileSize);
                if (song == null) { duplicates++; continue; }

                // Dedup 3: por metadata (title + artist + duration)
                var metaKey = $"{(song.Title ?? "").ToLowerInvariant()}|{(song.Artist ?? "").ToLowerInvariant()}|{song.DurationMs / 2000}";
                if (knownMeta.Contains(metaKey))
                {
                    duplicates++;
                    var existing = existingSongs.FirstOrDefault(s =>
                        $"{(s.Title ?? "").ToLowerInvariant()}|{(s.Artist ?? "").ToLowerInvariant()}|{s.DurationMs / 2000}" == metaKey);
                    if (existing != null) playlistAttachIds.Add(existing.Id);
                    continue;
                }

                var newId = _songRepo.Insert(song);
                if (newId > 0)
                {
                    added++;
                    playlistAttachIds.Add(newId);
                    knownPaths.Add(filePath);
                    knownNameSize.Add(nameSizeKey);
                    knownMeta.Add(metaKey);
                }
                else
                {
                    duplicates++;
                    var existing = _songRepo.GetByPath(filePath);
                    if (existing != null) playlistAttachIds.Add(existing.Id);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error importing {filePath}: {ex.Message}");
            }
        }

        // Si se especificó una playlist target, agregar las canciones
        if (targetPlaylistId.HasValue && playlistAttachIds.Count > 0)
        {
            _playlistRepo.AddSongsToPlaylist(targetPlaylistId.Value, playlistAttachIds.Distinct().ToList());
        }

        // CRITICAL (v2.5.24 feature): crear playlists automáticas por género
        if (added > 0)
        {
            _playlistService.CreateGenrePlaylists();
        }

        return new ImportResult(added, duplicates, total);
    }

    public List<Song> GetAllSongs() => _songRepo.ListAll();
    public int SongCount => _songRepo.Count();

    public void DeleteAll()
    {
        _songRepo.DeleteAll();
        // Limpiar cache de portadas
        var artDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wave", "art");
        if (Directory.Exists(artDir))
        {
            try { Directory.Delete(artDir, true); Directory.CreateDirectory(artDir); }
            catch { }
        }
    }
}
```

---

## 8. Tags y metadatos (TagLib#)

```csharp
// Audio/TagReader.cs
using System.IO;
using TagLib;
using TagLib.Id3v2;
using File = System.IO.File;
using TagFile = TagLib.File;

namespace Wave.Windows.Audio;

public static class TagReader
{
    /// <summary>
    /// Lee los metadatos de un archivo de audio usando TagLib#.
    /// Extrae: title, artist, album, duration, trackNo, year, genre, mimeType, portada.
    /// La portada se guarda como archivo JPG en %LocalAppData%\Wave\art\ para persistencia.
    /// </summary>
    public static Models.Song? Read(string filePath, string fileName, long fileSize)
    {
        try
        {
            using var tagFile = TagFile.Create(filePath);
            var tag = tagFile.Tag;
            var properties = tagFile.Properties;

            var durationMs = (long)(properties.Duration.TotalMilliseconds);
            if (durationMs <= 0) return null; // archivo corrupto o silencio

            var title = !string.IsNullOrWhiteSpace(tag.Title) ? tag.Title : Path.GetFileNameWithoutExtension(fileName);
            var artist = !string.IsNullOrWhiteSpace(tag.FirstArtist) ? tag.FirstArtist
                : (!string.IsNullOrWhiteSpace(tag.FirstAlbumArtist) ? tag.FirstAlbumArtist : null);
            var album = !string.IsNullOrWhiteSpace(tag.Album) ? tag.Album : null;
            var trackNo = tag.Track > 0 ? (int?)tag.Track : null;
            var year = tag.Year > 0 ? (int?)tag.Year : null;
            var genre = tag.FirstGenre; // TagLib# devuelve el primer género
            var mimeType = properties.AudioDescription ?? GetMimeType(filePath);

            // Extraer portada embebida
            var artUri = ExtractEmbeddedPicture(tag, filePath);

            return new Models.Song
            {
                FilePath = filePath,
                FileName = fileName,
                FileSize = fileSize,
                Title = title,
                Artist = artist,
                Album = album,
                DurationMs = durationMs,
                TrackNo = trackNo,
                Year = year,
                Genre = genre,
                MimeType = mimeType,
                DateAddedSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ArtUri = artUri,
                LastModified = new FileInfo(filePath).LastWriteTime.ToUnixTimeSeconds()
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TagReader failed for {filePath}: {ex.Message}");
            return null;
        }
    }

    private static string? ExtractEmbeddedPicture(Tag tag, string filePath)
    {
        try
        {
            var pictures = tag.Pictures;
            if (pictures == null || pictures.Length == 0) return null;
            var pic = pictures[0];
            if (pic.Data == null || pic.Data.Count == 0) return null;

            var artDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Wave", "art");
            Directory.CreateDirectory(artDir);

            // Hash del filePath para nombre único
            var hash = BitConverter.ToString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(filePath)))
                .Replace("-", "").Substring(0, 16);
            var artFile = Path.Combine(artDir, $"art_{hash}.jpg");

            if (!File.Exists(artFile))
            {
                File.WriteAllBytes(artFile, pic.Data.ToArray());
            }
            return artFile;
        }
        catch
        {
            return null;
        }
    }

    private static string GetMimeType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".flac" => "audio/flac",
            ".aac" => "audio/aac",
            ".m4a" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",
            ".wma" => "audio/x-ms-wma",
            _ => "audio/unknown"
        };
    }
}
```

<!-- END_PART_3 -->

---

## 9. Sistema de playlists

```csharp
// Data/Repositories/PlaylistRepository.cs
using System.Data;
using Dapper;
using Wave.Windows.Models;

namespace Wave.Windows.Data.Repositories;

public class PlaylistRepository
{
    private readonly IDbConnection _db;
    public PlaylistRepository() => _db = SqliteDatabase.Instance.Connection;

    public List<Playlist> GetAll() =>
        _db.Query<Playlist>("SELECT * FROM playlists ORDER BY updated_at DESC").ToList();

    public Playlist? GetById(long id) =>
        _db.QuerySingleOrDefault<Playlist>("SELECT * FROM playlists WHERE id = @id", new { id });

    public Playlist? GetByName(string name) =>
        _db.QuerySingleOrDefault<Playlist>("SELECT * FROM playlists WHERE name = @name", new { name });

    public long Insert(Playlist playlist)
    {
        const string sql = """
            INSERT INTO playlists (name, description, created_at, updated_at, cover_uri, folder_path)
            VALUES (@Name, @Description, @CreatedAt, @UpdatedAt, @CoverUri, @FolderPath);
            SELECT last_insert_rowid();
            """;
        return _db.ExecuteScalar<long>(sql, playlist);
    }

    public void Update(Playlist playlist) =>
        _db.Execute("UPDATE playlists SET name=@Name, description=@Description, updated_at=@UpdatedAt WHERE id=@Id", playlist);

    public void DeleteById(long id) =>
        _db.Execute("DELETE FROM playlists WHERE id = @id AND name != 'Favoritos'", new { id });

    public List<Song> GetSongsInPlaylist(long playlistId)
    {
        const string sql = """
            SELECT s.* FROM songs s
            INNER JOIN playlist_songs ps ON ps.song_id = s.id
            WHERE ps.playlist_id = @playlistId
            ORDER BY ps.position ASC
            """;
        return _db.Query<Song>(sql, new { playlistId }).ToList();
    }

    public void AddSongsToPlaylist(long playlistId, List<long> songIds)
    {
        if (songIds.Count == 0) return;
        var existing = _db.Query<long>(
            "SELECT song_id FROM playlist_songs WHERE playlist_id = @playlistId",
            new { playlistId }
        ).ToHashSet();
        var maxPos = _db.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(position), -1) FROM playlist_songs WHERE playlist_id = @playlistId",
            new { playlistId }
        );
        var toAdd = songIds.Where(id => !existing.Contains(id)).ToList();
        if (toAdd.Count == 0) return;
        using var tx = _db.BeginTransaction();
        foreach (var songId in toAdd)
        {
            maxPos++;
            _db.Execute(
                "INSERT INTO playlist_songs (playlist_id, song_id, position) VALUES (@playlistId, @songId, @position)",
                new { playlistId, songId, position = maxPos }, tx);
        }
        _db.Execute("UPDATE playlists SET updated_at = @ts WHERE id = @playlistId",
            new { ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), playlistId }, tx);
        tx.Commit();
    }

    public void RemoveSong(long playlistId, long songId) =>
        _db.Execute("DELETE FROM playlist_songs WHERE playlist_id = @playlistId AND song_id = @songId",
            new { playlistId, songId });
}
```

```csharp
// Services/PlaylistService.cs
using Wave.Windows.Data.Repositories;
using Wave.Windows.Models;

namespace Wave.Windows.Services;

public sealed class PlaylistService
{
    private readonly PlaylistRepository _repo = new();
    private readonly SongRepository _songRepo = new();

    public List<Playlist> GetAll() => _repo.GetAll();

    /// <summary>Playlist Favoritos siempre primero, luego el resto por updatedAt DESC.</summary>
    public List<Playlist> GetAllSorted()
    {
        var all = _repo.GetAll();
        return all.OrderByDescending(p => p.Name == Playlist.FavoritesName)
                   .ThenByDescending(p => p.UpdatedAt)
                   .ToList();
    }

    public void EnsureFavoritesPlaylist()
    {
        if (_repo.GetByName(Playlist.FavoritesName) == null)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _repo.Insert(new Playlist
            {
                Name = Playlist.FavoritesName,
                Description = "default",
                CreatedAt = now,
                UpdatedAt = now
            });
        }
    }

    public void ToggleFavorite(Song song)
    {
        song.IsFavorite = !song.IsFavorite;
        _songRepo.UpdateFavorite(song.Id, song.IsFavorite);
        var fav = _repo.GetByName(Playlist.FavoritesName);
        if (fav != null)
        {
            if (song.IsFavorite) _repo.AddSongsToPlaylist(fav.Id, new List<long> { song.Id });
            else _repo.RemoveSong(fav.Id, song.Id);
        }
    }

    public long CreatePlaylist(string name, List<long>? songIds = null)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var id = _repo.Insert(new Playlist { Name = name, CreatedAt = now, UpdatedAt = now });
        if (songIds != null && songIds.Count > 0) _repo.AddSongsToPlaylist(id, songIds);
        return id;
    }

    /// <summary>CRITICAL (v2.5.24): crea playlists automáticas por género.</summary>
    public void CreateGenrePlaylists()
    {
        var allSongs = _songRepo.ListAll();
        var byGenre = allSongs.Where(s => !string.IsNullOrWhiteSpace(s.Genre))
            .GroupBy(s => s.Genre!.Trim(), StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var grp in byGenre)
        {
            var displayName = grp.First().Genre!.Trim();
            var existing = _repo.GetByName(displayName);
            var playlistId = existing?.Id ?? _repo.Insert(new Playlist
            {
                Name = displayName,
                Description = "auto-genre",
                CreatedAt = now,
                UpdatedAt = now
            });
            if (playlistId > 0) _repo.AddSongsToPlaylist(playlistId, grp.Select(s => s.Id).ToList());
        }
    }

    public List<Song> GetSongs(long playlistId) => _repo.GetSongsInPlaylist(playlistId);
    public void Delete(long playlistId) => _repo.DeleteById(playlistId);
}
```

---

## 10. Letras sincronizadas (lrclib.net)

```csharp
// Models/ParsedLyrics.cs
namespace Wave.Windows.Models;

public record LyricLine(long TimestampMs, string Text);
public record ParsedLyrics(string? PlainLyrics, List<LyricLine> Lines, bool IsSynced);

// External/LrcLibClient.cs
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace Wave.Windows.External;

public sealed class LrcLibClient : IDisposable
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Wave", "lyrics");
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private const string ApiBase = "https://lrclib.net/api";

    public async Task<string?> GetLyricsAsync(string artist, string track, string? album, long durationSec)
    {
        // 1. Buscar en cache
        var cacheKey = $"{artist}|{track}".ToLowerInvariant();
        var cached = GetCached(cacheKey);
        if (cached != null) return cached;
        // 2. Query a lrclib.net
        var url = $"{ApiBase}/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(track)}";
        if (!string.IsNullOrEmpty(album)) url += $"&album_name={Uri.EscapeDataString(album)}";
        if (durationSec > 0) url += $"&duration={durationSec}";
        try
        {
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync();
            var doc = JsonSerializer.Deserialize<LrcLibResponse>(json);
            var lyrics = doc?.SyncedLyrics ?? doc?.PlainLyrics;
            if (string.IsNullOrEmpty(lyrics)) return null;
            SaveCache(cacheKey, lyrics);
            return lyrics;
        }
        catch { return null; }
    }

    private string? GetCached(string key)
    {
        Directory.CreateDirectory(CacheDir);
        var hash = BitConverter.ToString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))
            .Replace("-", "").Substring(0, 16);
        var file = Path.Combine(CacheDir, $"{hash}.lrc");
        return File.Exists(file) ? File.ReadAllText(file) : null;
    }

    private void SaveCache(string key, string lyrics)
    {
        Directory.CreateDirectory(CacheDir);
        var hash = BitConverter.ToString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))
            .Replace("-", "").Substring(0, 16);
        File.WriteAllText(Path.Combine(CacheDir, $"{hash}.lrc"), lyrics);
    }

    public void Dispose() => _http.Dispose();

    private class LrcLibResponse
    {
        [JsonPropertyName("syncedLyrics")] public string? SyncedLyrics { get; set; }
        [JsonPropertyName("plainLyrics")] public string? PlainLyrics { get; set; }
    }
}

// Services/LyricsService.cs (parser LRC)
using System.Text.RegularExpressions;
using Wave.Windows.External;
using Wave.Windows.Models;

namespace Wave.Windows.Services;

public sealed class LyricsService
{
    private readonly LrcLibClient _client = new();
    private static readonly Regex TimeTagRegex = new(@"\[(\d+):(\d{2})(?:\.(\d{2,3}))?\]", RegexOptions.Compiled);

    public async Task<ParsedLyrics?> GetLyricsAsync(Song song)
    {
        var artist = song.Artist ?? "";
        var track = song.Title ?? song.FileName;
        if (string.IsNullOrWhiteSpace(track)) return null;
        var raw = await _client.GetLyricsAsync(artist, track, song.Album, song.DurationMs / 1000);
        if (string.IsNullOrEmpty(raw)) return null;
        return Parse(raw);
    }

    public static ParsedLyrics Parse(string raw)
    {
        var lines = new List<LyricLine>();
        var plainLines = new List<string>();
        bool hasTimeTags = false;
        foreach (var line in raw.Split('\n'))
        {
            var matches = TimeTagRegex.Matches(line);
            if (matches.Count > 0)
            {
                hasTimeTags = true;
                foreach (Match m in matches)
                {
                    var min = int.Parse(m.Groups[1].Value);
                    var sec = int.Parse(m.Groups[2].Value);
                    var ms = m.Groups[3].Success ? int.Parse(m.Groups[3].Value.PadRight(3, '0')) : 0;
                    var ts = min * 60_000 + sec * 1000 + ms;
                    var text = TimeTagRegex.Replace(line, "").Trim();
                    lines.Add(new LyricLine(ts, string.IsNullOrEmpty(text) ? "♪" : text));
                }
            }
            else
            {
                plainLines.Add(line.Trim());
            }
        }
        return new ParsedLyrics(
            PlainLyrics: plainLines.Count > 0 ? string.Join("\n", plainLines) : null,
            Lines: hasTimeTags ? lines.OrderBy(l => l.TimestampMs).ToList() : new(),
            IsSynced: hasTimeTags
        );
    }

    public static int ActiveLineIndex(ParsedLyrics lyrics, long positionMs)
    {
        if (!lyrics.IsSynced || lyrics.Lines.Count == 0) return -1;
        for (int i = lyrics.Lines.Count - 1; i >= 0; i--)
            if (lyrics.Lines[i].TimestampMs <= positionMs) return i;
        return -1;
    }
}
```

---

## 11. Ecualizador de audio

```csharp
// Services/EqualizerSampleProvider.cs (NAudio ISampleProvider con bandas)
using NAudio.Wave;

namespace Wave.Windows.Services;

public class EqualizerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly EqualizerBand[] _bands;
    private readonly float[] _filters;

    public EqualizerSampleProvider(ISampleProvider source)
    {
        _source = source;
        WaveFormat = source.WaveFormat;
        // 5 bandas centradas en: 60Hz, 230Hz, 910Hz, 3600Hz, 14kHz
        _bands = new[]
        {
            new EqualizerBand { Frequency = 60,    Bandwidth = 0.5f, Gain = 0f },
            new EqualizerBand { Frequency = 230,   Bandwidth = 0.5f, Gain = 0f },
            new EqualizerBand { Frequency = 910,   Bandwidth = 0.5f, Gain = 0f },
            new EqualizerBand { Frequency = 3600,  Bandwidth = 0.5f, Gain = 0f },
            new EqualizerBand { Frequency = 14000, Bandwidth = 0.5f, Gain = 0f }
        };
        _filters = new float[_bands.Length * 3]; // a0, a1, a2 por banda
        UpdateFilters();
    }

    public WaveFormat WaveFormat { get; }
    public EqualizerBand[] Bands => _bands;
    public bool Enabled { get; set; } = true;

    public void SetBandGain(int index, float gainDb)
    {
        if (index < 0 || index >= _bands.Length) return;
        _bands[index].Gain = gainDb;
        UpdateFilters();
    }

    private void UpdateFilters()
    {
        // Calcular coeficientes biquad por banda (peaking EQ filter)
        for (int i = 0; i < _bands.Length; i++)
        {
            var band = _bands[i];
            var A = MathF.Pow(10, band.Gain / 40f);
            var w0 = 2 * MathF.PI * band.Frequency / WaveFormat.SampleRate;
            var cosW = MathF.Cos(w0);
            var sinW = MathF.Sin(w0);
            var alpha = sinW * MathF.Sinh(MathF.Log(2) / 2 * band.Bandwidth * w0 / sinW);
            var b0 = 1 + alpha * A;
            var b1 = -2 * cosW;
            var b2 = 1 - alpha * A;
            var a0 = 1 + alpha / A;
            _filters[i * 3] = b0 / a0;
            _filters[i * 3 + 1] = b1 / a0;
            _filters[i * 3 + 2] = b2 / a0;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (!Enabled) return _source.Read(buffer, offset, count);
        var samplesRead = _source.Read(buffer, offset, count);
        // Aplicar cada banda (simplificado — una implementación real necesita state por banda)
        for (int i = 0; i < samplesRead; i++)
        {
            float sample = buffer[offset + i];
            for (int b = 0; b < _bands.Length; b++)
            {
                sample *= _filters[b * 3]; // simplificado
            }
            buffer[offset + i] = Math.Clamp(sample, -1f, 1f);
        }
        return samplesRead;
    }
}

public class EqualizerBand
{
    public float Frequency { get; set; }
    public float Bandwidth { get; set; }
    public float Gain { get; set; } // en dB, rango -12 a +12
}
```

---

## 12. Backup y restore

```csharp
// Services/BackupService.cs
using System.IO;
using System.Text.Json;
using Wave.Windows.Data.Repositories;
using Wave.Windows.Models;

namespace Wave.Windows.Services;

public sealed class BackupService
{
    private readonly SongRepository _songRepo = new();
    private readonly PlaylistRepository _playlistRepo = new();

    public async Task ExportAsync(string outputPath, IProgress<int>? progress = null)
    {
        var backup = new BackupData
        {
            Version = 1,
            ExportDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Songs = _songRepo.ListAll(),
            Playlists = _playlistRepo.GetAll(),
            PlaylistSongs = GetAllPlaylistSongs()
        };
        var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputPath, json);
        progress?.Report(100);
    }

    public async Task ImportAsync(string inputPath, IProgress<int>? progress = null)
    {
        var json = await File.ReadAllTextAsync(inputPath);
        var backup = JsonSerializer.Deserialize<BackupData>(json);
        if (backup == null) return;
        _songRepo.DeleteAll();
        foreach (var song in backup.Songs) _songRepo.Insert(song);
        foreach (var pl in backup.Playlists)
        {
            var newId = _playlistRepo.Insert(pl with { Id = 0 });
            var songs = backup.PlaylistSongs.Where(ps => ps.PlaylistId == pl.Id).ToList();
            if (songs.Count > 0) _playlistRepo.AddSongsToPlaylist(newId, songs.Select(s => s.SongId).ToList());
        }
        progress?.Report(100);
    }

    private List<PlaylistSong> GetAllPlaylistSongs()
    {
        var result = new List<PlaylistSong>();
        foreach (var pl in _playlistRepo.GetAll())
        {
            var songs = _playlistRepo.GetSongsInPlaylist(pl.Id);
            for (int i = 0; i < songs.Count; i++)
                result.Add(new PlaylistSong { PlaylistId = pl.Id, SongId = songs[i].Id, Position = i });
        }
        return result;
    }
}

public record BackupData
{
    public int Version { get; set; }
    public long ExportDate { get; set; }
    public List<Song> Songs { get; set; } = new();
    public List<Playlist> Playlists { get; set; } = new();
    public List<PlaylistSong> PlaylistSongs { get; set; } = new();
}
```

<!-- END_PART_4 -->

---

## 13. System tray icon

```csharp
// Services/TrayService.cs
using System.Drawing;
using System.Windows.Forms;
using Application = System.Windows.Forms.Application;

namespace Wave.Windows.Services;

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
            Icon = new Icon("Assets/tray.ico"),
            Visible = true,
            Text = "Wave"
        };
        BuildContextMenu();
        _playback.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PlaybackController.CurrentSong))
                UpdateTooltip();
        };
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Play/Pausa", null, (s, e) => _playback.TogglePlayPause());
        menu.Items.Add("Siguiente", null, (s, e) => _playback.SkipToNext());
        menu.Items.Add("Anterior", null, (s, e) => _playback.SkipToPrevious());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Mostrar ventana", null, (s, e) => ShowMainWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (s, e) => Application.Exit());
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
    }

    private void UpdateTooltip()
    {
        var song = _playback.CurrentSong;
        _notifyIcon.Text = song != null
            ? $"{song.DisplayTitle} — {song.DisplayArtist}".Truncate(63)
            : "Wave";
    }

    private void ShowMainWindow()
    {
        var mainWindow = System.Windows.Application.Current.MainWindow;
        if (mainWindow != null)
        {
            mainWindow.Show();
            if (mainWindow.WindowState == WindowState.Minimized)
                mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}

public static class StringExt
{
    public static string Truncate(this string s, int max) => s.Length <= max ? s : s[..max];
}
```

---

## 14. SMTC + media keys

```csharp
// Services/SmtcService.cs
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using Wave.Windows.Models;

namespace Wave.Windows.Services;

public sealed class SmtcService : IDisposable
{
    private readonly PlaybackController _playback;
    private readonly MediaPlayer _mediaPlayer;
    private readonly MediaPlaybackList _playbackList = new();
    private bool _disposed;

    public SmtcService(PlaybackController playback)
    {
        _playback = playback;
        // MediaPlayer sirve como puente con SMTC — NO reproduce audio real,
        // solo sincroniza metadata + controles. El audio real lo hace NAudio.
        _mediaPlayer = new MediaPlayer
        {
            Source = _playbackList,
            CommandManager = { IsEnabled = true }
        };
        _mediaPlayer.CommandManager.PlayReceived += (s, e) => _playback.Play();
        _mediaPlayer.CommandManager.PauseReceived += (s, e) => _playback.Pause();
        _mediaPlayer.CommandManager.NextReceived += (s, e) => _playback.SkipToNext();
        _mediaPlayer.CommandManager.PreviousReceived += (s, e) => _playback.SkipToPrevious();

        _playback.PropertyChanged += (s, e) => SyncFromPlayback();
    }

    private void SyncFromPlayback()
    {
        var song = _playback.CurrentSong;
        if (song == null) return;
        _mediaPlayer.SystemMediaTransportControls.DisplayUpdater.Type = MediaPlaybackType.Music;
        var props = _mediaPlayer.SystemMediaTransportControls.DisplayUpdater.MusicProperties;
        props.Title = song.DisplayTitle;
        props.Artist = song.DisplayArtist;
        props.AlbumTitle = song.DisplayAlbum;
        props.AlbumTrackNumber = (uint?)(song.TrackNo ?? 0);
        // Cover art
        if (!string.IsNullOrEmpty(song.ArtUri) && File.Exists(song.ArtUri))
        {
            try
            {
                var stream = RandomAccessStreamReference.CreateFromUri(new Uri(song.ArtUri));
                _mediaPlayer.SystemMediaTransportControls.DisplayUpdater.Thumbnail = stream;
            }
            catch { }
        }
        _mediaPlayer.SystemMediaTransportControls.DisplayUpdater.Update();

        // Estado
        _mediaPlayer.SystemMediaTransportControls.PlaybackStatus =
            _playback.IsPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;

        // Posición (aproximada)
        _mediaPlayer.SystemMediaTransportControls.PlaybackRate = 1.0;
        var timeline = _mediaPlayer.SystemMediaTransportControls.DisplayUpdater;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mediaPlayer.Dispose();
    }
}
```

> **Nota**: Para usar SMTC necesitas el paquete NuGet `Microsoft.Windows.SDK.Contracts` o targetear Windows 10/11 específicamente con `<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>`.

---

## 15. Hotkeys globales

```csharp
// Services/HotkeyService.cs
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Wave.Windows.Services;

public sealed class HotkeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_PLAY_PAUSE = 1;
    private const int HOTKEY_NEXT = 2;
    private const int HOTKEY_PREV = 3;
    private const int HOTKEY_VOL_UP = 4;
    private const int HOTKEY_VOL_DOWN = 5;

    private readonly HwndSource _source;
    private readonly PlaybackController _playback;
    private bool _disposed;

    public HotkeyService(IntPtr windowHandle, PlaybackController playback)
    {
        _playback = playback;
        _source = HwndSource.FromHwnd(windowHandle)
            ?? throw new InvalidOperationException("HwndSource no disponible");
        _source.AddHook(WndProc);

        // Ctrl+Alt+P → Play/Pausa
        RegisterHotKey(_source.Handle, HOTKEY_PLAY_PAUSE, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x50); // P
        // Ctrl+Alt+Right → Siguiente
        RegisterHotKey(_source.Handle, HOTKEY_NEXT, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x27); // Right arrow
        // Ctrl+Alt+Left → Anterior
        RegisterHotKey(_source.Handle, HOTKEY_PREV, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x25); // Left arrow
        // Ctrl+Alt+Up → Vol+
        RegisterHotKey(_source.Handle, HOTKEY_VOL_UP, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x26); // Up
        // Ctrl+Alt+Down → Vol-
        RegisterHotKey(_source.Handle, HOTKEY_VOL_DOWN, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 0x28); // Down
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            switch (id)
            {
                case HOTKEY_PLAY_PAUSE: _playback.TogglePlayPause(); handled = true; break;
                case HOTKEY_NEXT: _playback.SkipToNext(); handled = true; break;
                case HOTKEY_PREV: _playback.SkipToPrevious(); handled = true; break;
                case HOTKEY_VOL_UP: /* _audioEngine.Volume += 0.1f */ handled = true; break;
                case HOTKEY_VOL_DOWN: /* _audioEngine.Volume -= 0.1f */ handled = true; break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterHotKey(_source.Handle, HOTKEY_PLAY_PAUSE);
        UnregisterHotKey(_source.Handle, HOTKEY_NEXT);
        UnregisterHotKey(_source.Handle, HOTKEY_PREV);
        UnregisterHotKey(_source.Handle, HOTKEY_VOL_UP);
        UnregisterHotKey(_source.Handle, HOTKEY_VOL_DOWN);
        _source.RemoveHook(WndProc);
    }
}
```

---

## 16. Mini player flotante

```xml
<!-- Views/MiniPlayerWindow.xaml -->
<Window x:Class="Wave.Windows.Views.MiniPlayerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Wave Mini Player" Height="80" Width="320"
        WindowStyle="None" ResizeMode="NoResize"
        Topmost="True" ShowInTaskbar="False"
        AllowsTransparency="True" Background="Transparent"
        MouseLeftButtonDown="Window_MouseLeftButtonDown">
    <Border Background="#DD1F2937" CornerRadius="12" Padding="8">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="48"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="32"/>
                <ColumnDefinition Width="32"/>
                <ColumnDefinition Width="32"/>
            </Grid.ColumnDefinitions>
            <Image Grid.Column="0" x:Name="CoverArt" Stretch="UniformToFill"/>
            <StackPanel Grid.Column="1" Margin="8,0" VerticalAlignment="Center">
                <TextBlock x:Name="Title" Text="{Binding CurrentSong.DisplayTitle}"
                           Foreground="White" FontSize="13" FontWeight="SemiBold"
                           TextTrimming="CharacterEllipsis"/>
                <TextBlock x:Name="Artist" Text="{Binding CurrentSong.DisplayArtist}"
                           Foreground="#99FFFFFF" FontSize="11" TextTrimming="CharacterEllipsis"/>
            </StackPanel>
            <Button Grid.Column="2" Background="Transparent" BorderThickness="0"
                    Click="Prev_Click" Cursor="Hand">
                <TextBlock Text="⏮" Foreground="White" FontSize="18"/>
            </Button>
            <Button Grid.Column="3" Background="Transparent" BorderThickness="0"
                    Click="PlayPause_Click" Cursor="Hand">
                <TextBlock x:Name="PlayPauseIcon" Text="▶" Foreground="White" FontSize="18"/>
            </Button>
            <Button Grid.Column="4" Background="Transparent" BorderThickness="0"
                    Click="Next_Click" Cursor="Hand">
                <TextBlock Text="⏭" Foreground="White" FontSize="18"/>
            </Button>
        </Grid>
    </Border>
</Window>
```

```csharp
// Views/MiniPlayerWindow.xaml.cs
using System.Windows;
using System.Windows.Input;
using Wave.Windows.Services;

namespace Wave.Windows.Views;

public partial class MiniPlayerWindow : Window
{
    private readonly PlaybackController _playback;
    private bool _dragging;
    private Point _dragOffset;

    public MiniPlayerWindow(PlaybackController playback)
    {
        _playback = playback;
        InitializeComponent();
        DataContext = _playback;
        _playback.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PlaybackController.IsPlaying))
                Dispatcher.Invoke(() => PlayPauseIcon.Text = _playback.IsPlaying ? "⏸" : "▶");
        };
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragOffset = e.GetPosition(this);
        DragMove();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _playback.TogglePlayPause();
    private void Prev_Click(object sender, RoutedEventArgs e) => _playback.SkipToPrevious();
    private void Next_Click(object sender, RoutedEventArgs e) => _playback.SkipToNext();
}
```

---

## 17. Drag & drop

```xml
<!-- En MainWindow.xaml o LibraryView.xaml -->
<Window x:Class="Wave.Windows.Views.MainWindow"
        AllowDrop="True"
        Drop="Window_Drop"
        DragEnter="Window_DragEnter">
    <!-- ... -->
</Window>
```

```csharp
// Views/MainWindow.xaml.cs (parcial)
using System.Windows;
using Wave.Windows.Services;
using Wave.Windows.Audio;

namespace Wave.Windows.Views;

public partial class MainWindow : Window
{
    private readonly LibraryService _libraryService;

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;

        // Recopilar archivos: si es carpeta, escanear recursivamente; si es archivo, agregar
        var allFiles = new List<string>();
        foreach (var path in files)
        {
            if (Directory.Exists(path))
            {
                allFiles.AddRange(_libraryService.Scan(path));
            }
            else if (File.Exists(path) && LibraryService.IsAudioFile(path))
            {
                allFiles.Add(path);
            }
        }

        if (allFiles.Count == 0) return;

        var result = await _libraryService.ImportFilesAsync(allFiles);
        // Mostrar snackbar/notificación: "Agregadas {result.Added} canciones ({result.Duplicates} duplicadas)"
    }
}
```

---

## 18. Visualizador de audio

```csharp
// Audio/VisualizerEngine.cs
using NAudio.Dsp;
using NAudio.Wave;

namespace Wave.Windows.Audio;

public sealed class VisualizerEngine
{
    private readonly ISampleProvider _source;
    private readonly Complex[] _fftBuffer;
    private readonly float[] _window;
    private readonly int _fftLength = 1024;
    private int _bufferFill;

    public event EventHandler<float[]>? SpectrumUpdated;

    public VisualizerEngine(ISampleProvider source)
    {
        _source = source;
        _fftBuffer = new Complex[_fftLength];
        _window = FastFourierTransform.HammingWindow(_fftLength);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        // Acumular muestras para FFT
        for (int i = 0; i < read; i += 2) // stereo → mono
        {
            if (_bufferFill >= _fftLength) break;
            _fftBuffer[_bufferFill].X = (float)((buffer[offset + i] + buffer[offset + i + 1]) * 0.5 * _window[_bufferFill]);
            _fftBuffer[_bufferFill].Y = 0;
            _bufferFill++;
        }
        if (_bufferFill >= _fftLength)
        {
            FastFourierTransform.FFT(true, (int)Math.Log2(_fftLength), _fftBuffer);
            var spectrum = new float[_fftLength / 2];
            for (int i = 0; i < _fftLength / 2; i++)
                spectrum[i] = (float)Math.Sqrt(_fftBuffer[i].X * _fftBuffer[i].X + _fftBuffer[i].Y * _fftBuffer[i].Y);
            _bufferFill = 0;
            SpectrumUpdated?.Invoke(this, spectrum);
        }
        return read;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;
}
```

```csharp
// Views/Controls/SpectrumVisualizer.xaml.cs (WPF control)
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Wave.Windows.Views.Controls;

public partial class SpectrumVisualizer : UserControl
{
    private float[] _spectrum = Array.Empty<float>();
    private const int BarCount = 32;

    public SpectrumVisualizer()
    {
        InitializeComponent();
        // Enlazar VisualizerEngine.SpectrumUpdated += (s, e) => { _spectrum = e; InvalidateVisual(); }
    }

    public void UpdateSpectrum(float[] spectrum)
    {
        _spectrum = spectrum;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_spectrum.Length == 0) return;
        var width = ActualWidth;
        var height = ActualHeight;
        var barWidth = width / BarCount;
        var brush = (Brush)Application.Current.FindResource("AccentBrush");
        for (int i = 0; i < BarCount; i++)
        {
            var idx = i * _spectrum.Length / BarCount;
            var value = Math.Min(1.0f, _spectrum[idx] * 5.0f); // amplificar
            var barHeight = value * height;
            var x = i * barWidth;
            dc.DrawRectangle(brush, null, new Rect(x + 1, height - barHeight, barWidth - 2, barHeight));
        }
    }
}
```

> **Pipeline audio completo con visualizador**:
> `reader → visualizer → equalizer → volume → waveOut`
>
> El `VisualizerEngine` debe insertarse ANTES del equalizer para ver el espectro sin procesar.

<!-- END_PART_5 -->

---

## 19. UI — pantallas WPF

### 19.1 App.xaml — recursos globales y temas

```xml
<!-- App.xaml -->
<Application x:Class="Wave.Windows.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="Views/MainWindow.xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Themes/DarkTheme.xaml"/>
                <ResourceDictionary Source="Themes/AccentColors.xaml"/>
                <ResourceDictionary Source="Themes/Styles.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

### 19.2 MainWindow — layout principal con navegación

```xml
<!-- Views/MainWindow.xaml -->
<Window x:Class="Wave.Windows.Views.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:Wave.Windows.Views"
        xmlns:controls="clr-namespace:Wave.Windows.Views.Controls"
        Title="Wave" Height="720" Width="1200"
        WindowStartupLocation="CenterScreen"
        Background="{DynamicResource BackgroundBrush}"
        AllowDrop="True" Drop="Window_Drop" DragEnter="Window_DragEnter">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>    <!-- Top bar -->
            <RowDefinition Height="*"/>        <!-- Content -->
            <RowDefinition Height="Auto"/>    <!-- PlayerBar -->
        </Grid.RowDefinitions>

        <!-- Top bar con título + búsqueda + nav -->
        <Grid Grid.Row="0" Background="{DynamicResource SurfaceBrush}" Height="56">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto"/>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBlock Grid.Column="0" Text="Wave" FontSize="20" FontWeight="Bold"
                       Foreground="{DynamicResource AccentBrush}" Margin="16,0,0,0" VerticalAlignment="Center"/>
            <TextBox Grid.Column="1" x:Name="SearchBox" Margin="16,12"
                     Text="{Binding SearchQuery, UpdateSourceTrigger=PropertyChanged}"
                     Background="{DynamicResource SurfaceVariantBrush}"
                     Foreground="{DynamicResource OnSurfaceBrush}" BorderThickness="0"
                     Padding="12,8" VerticalContentAlignment="Center"/>
            <StackPanel Grid.Column="2" Orientation="Horizontal" Margin="0,0,12,0">
                <Button Content="+" Click="AddMusic_Click" Width="40" Height="40" Margin="0,8,8,8"
                        Background="{DynamicResource AccentBrush}" Foreground="Black"/>
                <Button Content="⚙" Click="Settings_Click" Width="40" Height="40" Margin="0,8"
                        Background="Transparent" Foreground="{DynamicResource OnSurfaceBrush}"/>
            </StackPanel>
        </Grid>

        <!-- Content: navega entre vistas -->
        <ContentControl Grid.Row="1" x:Name="ContentArea" Content="{Binding CurrentView}"/>

        <!-- PlayerBar inferior -->
        <controls:PlayerBar Grid.Row="2" DataContext="{Binding Playback}"/>
    </Grid>
</Window>
```

### 19.3 PlayerBar — mini player con slider con bolita

```xml
<!-- Views/Controls/PlayerBar.xaml -->
<UserControl x:Class="Wave.Windows.Views.Controls.PlayerBar"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Height="72" Background="{DynamicResource SurfaceVariantBrush}">
    <Grid Margin="12,8">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="48"/>
            <ColumnDefinition Width="200"/>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="40"/>
            <ColumnDefinition Width="40"/>
            <ColumnDefinition Width="40"/>
            <ColumnDefinition Width="40"/>
            <ColumnDefinition Width="40"/>
        </Grid.ColumnDefinitions>

        <Image Grid.Column="0" Source="{Binding CurrentSong.ArtUri}" Stretch="UniformToFill"/>
        <StackPanel Grid.Column="1" Margin="12,0" VerticalAlignment="Center">
            <TextBlock Text="{Binding CurrentSong.DisplayTitle}"
                       Foreground="{DynamicResource OnSurfaceBrush}" FontWeight="SemiBold"
                       TextTrimming="CharacterEllipsis"/>
            <TextBlock Text="{Binding CurrentSong.DisplayArtist}"
                       Foreground="{DynamicResource OnSurfaceVariantBrush}"
                       TextTrimming="CharacterEllipsis"/>
        </StackPanel>

        <!-- Slider con bolita visible siempre (Thumb personalizado) -->
        <Slider Grid.Column="2" x:Name="SeekBar"
                Minimum="0" Maximum="{Binding DurationMs}" Value="{Binding PositionMs}"
                Thumb.DragStarted="SeekBar_DragStarted"
                Thumb.DragCompleted="SeekBar_DragCompleted"
                VerticalAlignment="Center" Margin="12,0">
            <Slider.Template>
                <ControlTemplate TargetType="Slider">
                    <Grid>
                        <Border Height="4" Background="{DynamicResource OutlineBrush}" CornerRadius="2"/>
                        <Track x:Name="PART_Track">
                            <Track.DecreaseRepeatButton>
                                <RepeatButton Command="Slider.DecreaseLarge">
                                    <RepeatButton.Template>
                                        <ControlTemplate TargetType="RepeatButton">
                                            <Border Height="4" Background="{DynamicResource AccentBrush}" CornerRadius="2"/>
                                        </ControlTemplate>
                                    </RepeatButton.Template>
                                </RepeatButton>
                            </Track.DecreaseRepeatButton>
                            <Track.Thumb>
                                <Thumb>
                                    <Thumb.Template>
                                        <ControlTemplate TargetType="Thumb">
                                            <Ellipse Width="16" Height="16" Fill="{DynamicResource AccentBrush}"/>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                        </Track>
                    </Grid>
                </ControlTemplate>
            </Slider.Template>
        </Slider>

        <Button Grid.Column="3" Content="⏮" Click="Prev_Click"
                Background="Transparent" Foreground="{DynamicResource OnSurfaceBrush}"/>
        <Button Grid.Column="4" Click="PlayPause_Click"
                Background="Transparent" Foreground="{DynamicResource AccentBrush}">
            <TextBlock x:Name="PlayPauseIcon" Text="▶" FontSize="20"/>
        </Button>
        <Button Grid.Column="5" Content="⏭" Click="Next_Click"
                Background="Transparent" Foreground="{DynamicResource OnSurfaceBrush}"/>
        <Button Grid.Column="6" Content="🔀" Click="Shuffle_Click"
                Background="Transparent"
                Foreground="{Binding IsShuffle, Converter={StaticResource BoolToAccentConverter}}"/>
        <Button Grid.Column="7" Content="🔁" Click="Repeat_Click"
                Background="Transparent"
                Foreground="{Binding RepeatOne, Converter={StaticResource BoolToAccentConverter}}"/>
    </Grid>
</UserControl>
```

### 19.4 LibraryView — grid de canciones + playlists slider

```xml
<!-- Views/LibraryView.xaml -->
<UserControl xmlns="...">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel>
            <!-- Header con sort chip -->
            <Grid Margin="16,12">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <Border Grid.Column="0" Width="72" Height="72" Background="{DynamicResource AccentBrush}" CornerRadius="8"/>
                <StackPanel Grid.Column="1" Margin="12,0">
                    <TextBlock Text="Tu biblioteca" FontSize="18" FontWeight="Bold" Foreground="{DynamicResource OnSurfaceBrush}"/>
                    <TextBlock Text="{Binding SongCount, StringFormat='{0} canciones'}" Foreground="{DynamicResource OnSurfaceVariantBrush}"/>
                    <Button x:Name="SortChip" Click="Sort_Click" Background="Transparent" Foreground="{DynamicResource AccentBrush}" HorizontalAlignment="Left">
                        <StackPanel Orientation="Horizontal">
                            <TextBlock Text="⇅" Margin="0,0,4,0"/>
                            <TextBlock x:Name="SortLabel" Text="Recientes"/>
                        </StackPanel>
                        <Button.ContextMenu>
                            <ContextMenu x:Name="SortMenu">
                                <MenuItem Header="Recientes" Click="SortRecentlyAdded_Click"/>
                                <MenuItem Header="Reproducidas" Click="SortRecentlyPlayed_Click"/>
                                <MenuItem Header="Título" Click="SortTitle_Click"/>
                                <MenuItem Header="Artista" Click="SortArtist_Click"/>
                                <MenuItem Header="Duración" Click="SortDuration_Click"/>
                            </ContextMenu>
                        </Button.ContextMenu>
                    </Button>
                </StackPanel>
                <StackPanel Grid.Column="2" Orientation="Horizontal">
                    <Button Content="▶" Click="PlayAll_Click" Width="36" Height="36"/>
                    <Button Content="🔀" Click="ShuffleAll_Click" Width="36" Height="36"/>
                </StackPanel>
            </Grid>

            <!-- Playlists slider horizontal -->
            <TextBlock Text="Tus listas" FontSize="14" FontWeight="SemiBold" Margin="16,8" Foreground="{DynamicResource AccentBrush}"/>
            <ItemsControl ItemsSource="{Binding Playlists}" Margin="12,0">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <StackPanel Orientation="Horizontal"/>
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Width="120" Margin="4">
                            <Border Width="120" Height="120" CornerRadius="12" Background="{DynamicResource SurfaceVariantBrush}">
                                <Border.Style>
                                    <Style TargetType="Border">
                                        <Setter Property="BorderThickness" Value="0"/>
                                        <Style.Triggers>
                                            <DataTrigger Binding="{Binding IsPlaying}" Value="True">
                                                <Setter Property="BorderThickness" Value="2"/>
                                                <Setter Property="BorderBrush" Value="{DynamicResource AccentBrush}"/>
                                            </DataTrigger>
                                        </Style.Triggers>
                                    </Style>
                                </Border.Style>
                            </Border>
                            <TextBlock Text="{Binding Name}" Foreground="{DynamicResource OnSurfaceBrush}" Margin="0,4,0,0" TextWrapping="Wrap"/>
                        </StackPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <!-- Lista de canciones -->
            <TextBlock Text="Canciones" FontSize="14" FontWeight="SemiBold" Margin="16,12" Foreground="{DynamicResource AccentBrush}"/>
            <ListView ItemsSource="{Binding Songs}" Background="Transparent" BorderThickness="0">
                <ListView.ItemTemplate>
                    <DataTemplate>
                        <Grid Margin="8,4" Height="48">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="40"/>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <Image Grid.Column="0" Source="{Binding ArtUri}" Stretch="UniformToFill"/>
                            <StackPanel Grid.Column="1" Margin="12,0" VerticalAlignment="Center">
                                <TextBlock Text="{Binding DisplayTitle}" Foreground="{DynamicResource OnSurfaceBrush}" TextTrimming="CharacterEllipsis"/>
                                <TextBlock Text="{Binding DisplayArtist}" Foreground="{DynamicResource OnSurfaceVariantBrush}" TextTrimming="CharacterEllipsis"/>
                            </StackPanel>
                            <TextBlock Grid.Column="2" Text="{Binding DurationMs, Converter={StaticResource MsToTextConverter}}" Foreground="{DynamicResource OnSurfaceVariantBrush}" VerticalAlignment="Center"/>
                        </Grid>
                    </DataTemplate>
                </ListView.ItemTemplate>
            </ListView>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

### 19.5 LyricsView — letras con portada de fondo + scrim oscuro

```xml
<!-- Views/LyricsView.xaml -->
<UserControl xmlns="...">
    <Grid>
        <!-- Layer 1: portada de fondo -->
        <Image Source="{Binding CurrentSong.ArtUri}" Stretch="UniformToFill"/>

        <!-- Layer 2: scrim oscuro -->
        <Rectangle Fill="#C0000000"/>

        <!-- Layer 3: letras -->
        <ScrollViewer x:Name="LyricsScroll" VerticalScrollBarVisibility="Hidden" Margin="0,48,0,24">
            <ItemsControl ItemsSource="{Binding Lyrics.Lines}" HorizontalAlignment="Center">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding Text}"
                                   Foreground="{Binding IsActive, Converter={StaticResource BoolToColorConverter}}"
                                   FontSize="{Binding IsActive, Converter={StaticResource BoolToFontSizeConverter}}"
                                   FontWeight="{Binding IsActive, Converter={StaticResource BoolToFontWeightConverter}}"
                                   Margin="0,10" TextAlignment="Center" TextWrapping="Wrap"/>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </Grid>
</UserControl>
```

---

## 20. Settings y configuración

```csharp
// Services/SettingsService.cs
using System.IO;
using System.Text.Json;

namespace Wave.Windows.Services;

public sealed class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Wave", "settings.json");

    private Dictionary<string, JsonElement> _settings = new();

    public string Theme { get => Get("theme", "dark"); set => Set("theme", value); }
    public string AccentColor { get => Get("accent_color", "#32C98B"); set => Set("accent_color", value); }
    public string Language { get => Get("language", "es"); set => Set("language", value); }
    public bool RepeatAll { get => Get("repeat_all", false); set => Set("repeat_all", value); }
    public float Volume { get => Get("volume", 1.0f); set => Set("volume", value); }
    public bool ShuffleDefault { get => Get("shuffle_default", false); set => Set("shuffle_default", value); }
    public List<string> WatchedFolders => Get("watched_folders", new List<string>());

    public SettingsService()
    {
        Load();
    }

    public T Get<T>(string key, T defaultValue = default!)
    {
        if (_settings.TryGetValue(key, out var elem))
            return JsonSerializer.Deserialize<T>(elem.GetRawText()) ?? defaultValue;
        return defaultValue;
    }

    public void Set<T>(string key, T value)
    {
        _settings[key] = JsonSerializer.SerializeToElement(value);
        Save();
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
        catch { _settings = new(); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}
```

---

## 21. Build, empaquetado y deployment

### 21.1 Build con .NET CLI

```bash
# Build debug
dotnet build src/Wave.Windows/Wave.Windows.csproj -c Debug

# Build release (self-contained, single-file)
dotnet publish src/Wave.Windows/Wave.Windows.csproj -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o ./publish
```

### 21.2 MSIX packaging (opcional, para Microsoft Store)

```xml
<!-- Package.appxmanifest (resumido) -->
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
  <Identity Name="Wave" Publisher="Daniel Barrantes" Version="1.0.0.0"/>
  <Applications>
    <Application Id="App" Executable="Wave.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements DisplayName="Wave" Description="Reproductor de música offline"
                          BackgroundColor="#1F2937" Square150x150Logo="Assets\logo150.png"/>
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust"/>
  </Capabilities>
</Package>
```

### 21.3 Autostart con Windows (opcional)

```csharp
// En SettingsService
public bool AutostartWithWindows
{
    get => Get("autostart", false);
    set
    {
        Set("autostart", value);
        var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key != null)
        {
            var exePath = Environment.ProcessPath;
            if (value && exePath != null) key.SetValue("Wave", $"\"{exePath}\"");
            else key.DeleteValue("Wave", false);
        }
    }
}
```

### 21.4 App.xaml.cs — inicialización

```csharp
// App.xaml.cs
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Wave.Windows.Data;
using Wave.Windows.Services;

namespace Wave.Windows;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Inicializar DB
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wave");
        Directory.CreateDirectory(appData);
        var dbPath = Path.Combine(appData, "wave.db");
        SqliteDatabase.Init(dbPath);

        // DI
        var services = new ServiceCollection();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<PlaybackController>();
        services.AddSingleton<LibraryService>();
        services.AddSingleton<PlaylistService>();
        services.AddSingleton<LyricsService>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<TrayService>();
        services.AddSingleton<SmtcService>();
        services.AddSingleton<HotkeyService>();
        services.AddSingleton<MiniPlayerWindow>();
        Services = services.BuildServiceProvider();

        // Inicializar servicios que requieren startup
        var settings = Services.GetRequiredService<SettingsService>();
        var playback = Services.GetRequiredService<PlaybackController>();
        var playlistService = Services.GetRequiredService<PlaylistService>();

        // Crear playlist Favoritos si no existe
        playlistService.EnsureFavoritesPlaylist();

        // Restaurar cola guardada
        playback.RestoreSavedQueue();

        // Tray icon + hotkeys + SMTC
        Services.GetRequiredService<TrayService>();
        // SmtcService y HotkeyService se inicializan cuando MainWindow está listo
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Guardar cola + dispose servicios
        if (Services.GetService<PlaybackController>() is { } playback)
        {
            playback.Pause();
            playback.Dispose();
        }
        Services.GetRequiredService<TrayService>().Dispose();
        base.OnExit(e);
    }
}
```

---

## 22. Criterios de aceptación y QA

### 22.1 Funcionales

| ID | Criterio | Cómo verificar |
|---|---|---|
| F1 | Importar música desde carpeta | Add music → seleccionar carpeta → canciones aparecen en LibraryView |
| F2 | Importar archivos individuales | Drag & drop archivos MP3 a la ventana |
| F3 | Sub-playlists por carpeta | Carpeta con subcarpetas → cada subcarpeta se convierte en playlist |
| F4 | Reproducir una canción | Doble click en canción → se reproduce, PlayerBar muestra info |
| F5 | Seek con slider | Arrastrar bolita del PlayerBar → posición cambia |
| F6 | Skip anterior/siguiente | Botones ⏮⏭ → cambia canción |
| F7 | Shuffle | Botón 🔀 → orden aleatorio, canción actual sigue |
| F8 | Repeat one | Botón 🔁 → repite canción actual al terminar |
| F9 | Favoritos | Click corazón en canción → aparece en playlist Favoritos |
| F10 | Playlists automáticas por género | Después de importar → aparecen playlists con nombres de géneros |
| F11 | Letras sincronizadas | Reproducir canción → abrir LyricsView → letra con scroll automático |
| F12 | Letras con portada de fondo | LyricsView muestra portada del album como background con scrim oscuro |
| F13 | Búsqueda | Escribir en SearchBox → filtra canciones en tiempo real |
| F14 | Sort | Chip de sort debajo del botón → menú con 5 opciones |
| F15 | Backup/Restore | Export JSON → borrar todo → Import JSON → biblioteca restaurada |

### 22.2 Windows-specific

| ID | Criterio | Cómo verificar |
|---|---|---|
| W1 | System tray icon | Minimizar a tray → icono visible → click derecho muestra menú |
| W2 | SMTC integration | Reproducir → media controls del sistema (volume mixer) muestran metadata |
| W3 | Media keys | Teclas play/pause/next/prev del teclado funcionan aunque app no esté en foco |
| W4 | Hotkeys globales | Ctrl+Alt+P → play/pausa, Ctrl+Alt+→ → siguiente |
| W5 | Mini player flotante | Botón mini player → ventana pequeña topmost que se puede mover |
| W6 | Drag & drop | Arrastrar archivos MP3 desde Explorer → se importan |
| W7 | Visualizador de audio | Spectrum bars visibles durante playback |

### 22.3 Performance

| ID | Criterio | Medición |
|---|---|---|
| P1 | Biblioteca de 10k canciones carga en <3s | Stopwatch en LibraryService.ImportFromFolderAsync |
| P2 | Seek sin lag | <100ms desde click hasta posición efectiva |
| P3 | UI fluida (60fps) durante playback | WPF Performance Suite / ETW |
| P4 | Letras no bloquean UI | LyricsService es async, UI thread libre |
| P5 | Memoria <200MB con biblioteca cargada | Task Manager / dotMemory |

### 22.4 Edge cases

| ID | Caso | Comportamiento esperado |
|---|---|---|
| E1 | Archivo de audio corrupto | Skip a siguiente, log error, no crash |
| E2 | Carpeta sin permisos | Skip silencioso, continuar import |
| E3 | Archivo sin tags | Title = fileName, artist = "Artista desconocido" |
| E4 | Sin conexión a internet (lrclib.net) | Mostrar "letras no disponibles" |
| E5 | Biblioteca vacía | Empty state con CTA "Agregar música" |
| E6 | Dispositivo de audio desconectado | Mensaje error, no crash |
| E7 | Mismo archivo importado 2 veces | Dedup por path, no duplicar |
| E8 | Mismo contenido en paths distintos | Dedup por metadata, no duplicar |

### 22.5 Tests mínimos

```csharp
// Wave.Windows.Tests/PlaybackControllerTests.cs (xUnit)
using Xunit;
using Moq;
using Wave.Windows.Services;
using Wave.Windows.Models;

public class PlaybackControllerTests
{
    [Fact]
    public void SetQueue_WithValidSongs_SetsCurrentSong()
    {
        // Arrange
        var settings = new Mock<SettingsService>().Object;
        var controller = new PlaybackController(settings);
        var songs = new List<Song> { new() { Id = 1, FilePath = "test.mp3", Title = "Test" } };

        // Act
        controller.SetQueue(songs, 0, autoPlay: false);

        // Assert
        Assert.NotNull(controller.CurrentSong);
        Assert.Equal("Test", controller.CurrentSong!.Title);
    }

    [Fact]
    public void SkipToNext_AtEndOfQueue_StopsOrLoops()
    {
        // Test que al final de la cola, sin repeat, se pausa
        // ...
    }
}
```

---

## Apéndice A — Strings multi-idioma

```csharp
// Services/LocalizationService.cs (resumido)
using System.Collections.Generic;

namespace Wave.Windows.Services;

public static class LocalizationService
{
    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        ["es"] = new()
        {
            ["your_library"] = "Tu biblioteca",
            ["songs_count"] = "{0} canciones",
            ["play_all"] = "Reproducir",
            ["shuffle"] = "Aleatorio",
            ["search_placeholder"] = "Buscar canciones, artistas…",
            ["lyrics"] = "Letra",
            ["lyrics_not_available"] = "Letra no disponible",
            ["settings"] = "Ajustes",
            ["theme"] = "Tema",
            ["language"] = "Idioma",
            ["backup"] = "Respaldo",
            ["about"] = "Acerca de",
            ["add_music"] = "Agregar música",
            ["favorites"] = "Favoritos",
            ["recently_added"] = "Recientes",
            ["recently_played"] = "Reproducidas",
            // ... completar todos los strings
        },
        ["en"] = new()
        {
            ["your_library"] = "Your library",
            ["songs_count"] = "{0} songs",
            // ...
        },
        // ... ja, de, fr, ko
    };

    public static string T(string key, params object[] args)
    {
        var lang = SettingsService.Instance.Language;
        if (!Translations.TryGetValue(lang, out var dict)) dict = Translations["en"];
        var tmpl = dict.GetValueOrDefault(key, key);
        return args.Length > 0 ? string.Format(tmpl, args) : tmpl;
    }
}
```

---

## Apéndice B — Convenciones de Git

- **Branches**: `feat/...`, `fix/...`, `chore/...`, `docs/...`
- **Commits**: `type(scope): descripción` (ej: `feat(playback): add shuffle`)
- **Tags**: `v1.0.0`, `v1.0.1`, etc. (semver)
- **PRs**: squash merge, borrar branch después

---

## Apéndice C — Recursos adicionales

- **Documentación NAudio**: https://github.com/naudio/NAudio
- **TagLib#**: https://github.com/mono/taglib-sharp
- **lrclib.net API**: https://lrclib.net/docs
- **SMTC docs**: https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/system-media-transport-controls
- **WPF MVVM**: https://learn.microsoft.com/en-us/dotnet/architecture/maui/mvvm

---

**Fin del documento**

> Este documento es una especificación completa para que una IA o desarrollador implemente Wave for Windows. Está basado en la app Android Wave v2.5.24 (github.com/Danielbarrantesv/wave-app) con adaptaciones para Windows (NAudio en lugar de ExoPlayer, TagLib# en lugar de MediaMetadataRetriever, SMTC en lugar de MediaSession, etc.).
>
> **Total**: ~22 secciones, ~2500 líneas de código C#/XAML, ~80 páginas equivalentes.

<!-- END_PART_6 -->
