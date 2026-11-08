# Wave for Windows

Reproductor de música **offline** para Windows 10/11, escrito en C#/.NET 8 con WPF. Inspirado en la app Android [Wave](https://github.com/Danielbarrantesv/wave-app).

## Características principales

- Reproduce archivos locales: MP3, FLAC, AAC, OGG, M4A, WAV, WMA (vía NAudio)
- Biblioteca organizada con metadatos extraídos de tags (TagLib#)
- Playlists manuales, Favoritos y automáticas por género
- Letras sincronizadas desde lrclib.net con cache local
- Ecualizador de 5 bandas
- Backup/restore de biblioteca en JSON
- System tray icon
- SMTC (System Media Transport Controls) + media keys
- Hotkeys globales (Ctrl+Alt+P, etc.)
- Mini player flotante
- Drag & drop para importar
- Visualizador de audio (FFT spectrum)
- Tema claro/oscuro

## Stack tecnológico

| Componente | Tecnología | Versión |
|---|---|---|
| Framework | .NET | 8.0 LTS |
| UI | WPF | built-in |
| Lenguaje | C# | 12 |
| Audio | NAudio | 2.2.1 |
| Tags | TagLib# | 2.3.0 |
| DB | SQLite + Dapper | 3.45+ / 2.1.35 |
| MVVM | CommunityToolkit.Mvvm | 8.2.2 |
| Logging | Serilog | 3.1.1 |
| Letras | lrclib.net API | REST |

## Estructura del proyecto

```
Wave-escritorio/
├── src/
│   ├── Wave.Windows/             # App WPF principal
│   │   ├── Models/               # Song, Playlist, PlaybackState, etc.
│   │   ├── Data/                 # SQLite + Repositories (Dapper)
│   │   ├── Services/             # PlaybackController, LibraryService, etc.
│   │   ├── Audio/                # TagReader, VisualizerEngine
│   │   ├── External/             # LrcLibClient
│   │   ├── ViewModels/           # MVVM
│   │   ├── Views/                # WPF XAML
│   │   ├── Themes/               # Light/Dark/Accent
│   │   └── Assets/               # Iconos, imágenes
│   └── Wave.Windows.Tests/       # xUnit
├── docs/                         # Especificación técnica
└── .github/workflows/            # CI/CD
```

## Roadmap de versiones (SemVer)

| Versión | Alcance | Estado |
|---|---|---|
| `v0.1.0` | Scaffold inicial + modelos + DB layer + App.xaml.cs | ✅ Released |
| `v0.2.0` | AudioEngine + PlaybackController (NAudio) + bindings UI | ✅ Released |
| `v0.3.0` | LibraryService + TagReader (TagLib#) + importación carpeta | ✅ Released |
| `v0.4.0` | PlaylistService + Favoritos + género automático | ✅ Released |
| `v0.5.0` | LyricsService + LrcLibClient + parser LRC + vista de letras | ✅ Released |
| `v0.6.0` | MainViewModel + búsqueda + sort chip + PlayerBar custom | ✅ Released |
| `v0.7.0` | Equalizer (5 bandas biquad) + Visualizer (FFT spectrum) | ✅ Released |
| `v0.8.0` | System tray icon + SMTC + hotkeys globales (Ctrl+Alt+P, etc.) | ✅ Released |
| `v0.9.0` | Mini player flotante + Backup/Restore en JSON | ✅ Released |
| `v1.0.0` | Estabilización + tests + empaquetado MSIX | Pendiente |

## Cómo construir

> Requiere .NET 8 SDK en Windows 10/11.

```bash
# Restaurar dependencias
dotnet restore

# Build debug
dotnet build src/Wave.Windows/Wave.Windows.csproj -c Debug

# Build release (single-file, self-contained)
dotnet publish src/Wave.Windows/Wave.Windows.csproj -c Release \
    -r win-x64 --self-contained true \
    -p:PublishSingleFile=true \
    -o ./publish
```

## Documentación

La especificación técnica completa está en [`docs/Wave_Windows_Specification.md`](docs/Wave_Windows_Specification.md) (22 secciones, ~2500 líneas de código de referencia).

## Licencia

MIT — ver [LICENSE](LICENSE).

## Autor

Daniel Barrantes — [github.com/Danielbarrantesv](https://github.com/Danielbarrantesv)

Basado en la app Android Wave v2.5.24.
