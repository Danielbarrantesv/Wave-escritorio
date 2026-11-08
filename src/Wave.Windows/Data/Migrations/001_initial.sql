-- Wave Windows - Migration 001: Schema inicial
-- Fecha: 2026-11
-- Versión: 0.1.0

-- Tabla de canciones
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
    art_uri         TEXT,
    last_modified   INTEGER NOT NULL DEFAULT 0,
    is_favorite     INTEGER NOT NULL DEFAULT 0,
    last_played_at  INTEGER NOT NULL DEFAULT 0,
    genre           TEXT
);
CREATE INDEX IF NOT EXISTS idx_songs_filepath ON songs(file_path);
CREATE INDEX IF NOT EXISTS idx_songs_fingerprint ON songs(fingerprint);
CREATE INDEX IF NOT EXISTS idx_songs_title_artist ON songs(title, artist);
CREATE INDEX IF NOT EXISTS idx_songs_is_favorite ON songs(is_favorite);
CREATE INDEX IF NOT EXISTS idx_songs_last_played ON songs(last_played_at);
CREATE INDEX IF NOT EXISTS idx_songs_genre ON songs(genre);

-- Tabla de playlists
CREATE TABLE IF NOT EXISTS playlists (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    name            TEXT NOT NULL UNIQUE,
    description     TEXT,
    created_at      INTEGER NOT NULL DEFAULT 0,
    updated_at      INTEGER NOT NULL DEFAULT 0,
    cover_uri       TEXT,
    folder_path     TEXT
);
CREATE INDEX IF NOT EXISTS idx_playlists_name ON playlists(name);

-- Asociación playlist ↔ song (con posición)
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

-- Metadatos de migraciones (para tracking)
CREATE TABLE IF NOT EXISTS migrations (
    id              INTEGER PRIMARY KEY,
    name            TEXT NOT NULL,
    applied_at      INTEGER NOT NULL
);
INSERT OR IGNORE INTO migrations (id, name, applied_at)
VALUES (1, '001_initial', strftime('%s', 'now'));
