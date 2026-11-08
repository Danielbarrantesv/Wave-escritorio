// Wave App - lógica de la interfaz
let allSongs = [];
let filteredSongs = [];
let currentPlaylist = null;
let currentSort = 'recentlyAdded';
let isSeeking = false;
let currentLyrics = null;
let currentLyricIndex = -1;

// === Init ===
document.addEventListener('DOMContentLoaded', async () => {
    WaveAPI.init();
    WaveAPI.onPlaybackChanged = onPlaybackChanged;
    bindEvents();
    await loadSongs();
    await loadPlaylists();
});

// === Event binding ===
function bindEvents() {
    document.getElementById('btn-add').onclick = onAddMusic;
    document.getElementById('btn-play').onclick = onPlayPause;
    document.getElementById('btn-next').onclick = onNext;
    document.getElementById('btn-prev').onclick = onPrev;
    document.getElementById('btn-shuffle').onclick = onShuffle;
    document.getElementById('btn-repeat').onclick = onRepeat;
    document.getElementById('btn-mini').onclick = onMiniPlayer;
    document.getElementById('btn-lyrics').onclick = onLyrics;
    document.getElementById('btn-eq').onclick = onEqualizer;
    document.getElementById('btn-backup').onclick = onBackup;

    document.getElementById('search').oninput = onSearch;
    document.getElementById('seek-bar').oninput = onSeek;
    document.getElementById('seek-bar').onchange = onSeekEnd;

    // Sort
    const sortChip = document.getElementById('sort-chip');
    const sortMenu = document.getElementById('sort-menu');
    sortChip.onclick = (e) => { e.stopPropagation(); sortMenu.classList.toggle('hidden'); };
    document.addEventListener('click', () => sortMenu.classList.add('hidden'));
    document.querySelectorAll('.sort-option').forEach(el => {
        el.onclick = () => {
            currentSort = el.dataset.sort;
            document.getElementById('sort-label').textContent = el.textContent;
            sortMenu.classList.add('hidden');
            applySort();
        };
    });
}

// === Playback handlers ===
async function onPlayPause() { await WaveAPI.call('playback.togglePlayPause'); }
async function onNext() { await WaveAPI.call('playback.next'); }
async function onPrev() { await WaveAPI.call('playback.prev'); }
async function onShuffle() { await WaveAPI.call('playback.toggleShuffle'); }
async function onRepeat() { await WaveAPI.call('playback.toggleRepeat'); }

// === Other handlers ===
function onMiniPlayer() {
    // Abrir mini player nativo via C#
    WaveAPI.call('window.openMiniPlayer', {});
}

function onEqualizer() {
    WaveAPI.call('window.openEqualizer', {});
}

async function onLyrics() {
    const panel = document.getElementById('lyrics-panel');
    const content = document.getElementById('lyrics-content');
    const songsList = document.getElementById('songs-container');

    if (panel.classList.contains('hidden')) {
        const state = await WaveAPI.call('playback.getState');
        if (state && state.currentSong) {
            const lyrics = await WaveAPI.call('lyrics.get', state.currentSong);
            currentLyrics = lyrics;
            if (lyrics && lyrics.isSynced && lyrics.lines && lyrics.lines.length > 0) {
                content.innerHTML = lyrics.lines.map((l, i) =>
                    `<div class="lyric-line" data-idx="${i}" data-ts="${l.timestampMs}">${escapeHtml(l.text)}</div>`
                ).join('');
            } else if (lyrics && lyrics.plainLyrics) {
                content.innerHTML = lyrics.plainLyrics.split('\n').map(l =>
                    `<div class="lyric-line">${escapeHtml(l)}</div>`
                ).join('');
            } else {
                content.innerHTML = '<div class="empty-state"><div class="empty-icon">♪</div><p>Letra no disponible</p></div>';
            }
            panel.classList.remove('hidden');
            songsList.classList.add('hidden');
        }
    } else {
        panel.classList.add('hidden');
        songsList.classList.remove('hidden');
    }
}

async function onBackup() {
    const choice = confirm('¿Exportar biblioteca (Aceptar) o importar (Cancelar)?');
    if (choice) {
        await WaveAPI.call('backup.export', { path: '' });
    }
}

// === Library ===
async function loadSongs() {
    allSongs = await WaveAPI.call('library.getSongs') || [];
    applyFilter();
    applySort();
    renderSongs();
    updateCount();
}

async function loadPlaylists() {
    const playlists = await WaveAPI.call('playlists.getAll') || [];
    const container = document.getElementById('playlists-list');
    container.innerHTML = '';
    playlists.forEach(pl => {
        const el = document.createElement('div');
        el.className = 'playlist-item';
        el.dataset.playlistId = pl.id;
        el.innerHTML = `<span>♪</span><span>${escapeHtml(pl.name)}</span>`;
        el.onclick = () => selectPlaylist(pl);
        container.appendChild(el);
    });
}

function selectAllSongs() {
    document.querySelectorAll('.nav-item').forEach(el => el.classList.remove('active'));
    document.querySelector('[data-view="all"]').classList.add('active');
    document.querySelectorAll('.playlist-item').forEach(el => el.classList.remove('active'));
    currentPlaylist = null;
    loadSongs();
}

async function selectPlaylist(pl) {
    document.querySelectorAll('.nav-item').forEach(el => el.classList.remove('active'));
    document.querySelectorAll('.playlist-item').forEach(el => el.classList.remove('active'));
    document.querySelector(`[data-playlist-id="${pl.id}"]`).classList.add('active');
    currentPlaylist = pl;
    const songs = await WaveAPI.call('playlists.getSongs', { playlistId: pl.id }) || [];
    allSongs = songs;
    applyFilter();
    applySort();
    renderSongs();
    updateCount();
}

// === Search ===
function onSearch() {
    applyFilter();
    renderSongs();
    updateCount();
}

function applyFilter() {
    const q = document.getElementById('search').value.trim().toLowerCase();
    if (!q) { filteredSongs = [...allSongs]; return; }
    filteredSongs = allSongs.filter(s =>
        (s.displayTitle || s.title || '').toLowerCase().includes(q) ||
        (s.displayArtist || s.artist || '').toLowerCase().includes(q) ||
        (s.displayAlbum || s.album || '').toLowerCase().includes(q) ||
        (s.fileName || '').toLowerCase().includes(q)
    );
}

function applySort() {
    const sorts = {
        recentlyAdded: (a, b) => (b.dateAddedSec || 0) - (a.dateAddedSec || 0),
        recentlyPlayed: (a, b) => (b.lastPlayedAt || 0) - (a.lastPlayedAt || 0),
        title: (a, b) => (a.displayTitle || a.title || '').localeCompare(b.displayTitle || b.title || ''),
        artist: (a, b) => (a.displayArtist || a.artist || '').localeCompare(b.displayArtist || b.artist || ''),
        duration: (a, b) => (a.durationMs || 0) - (b.durationMs || 0)
    };
    filteredSongs.sort(sorts[currentSort] || sorts.recentlyAdded);
}

// === Render ===
function renderSongs() {
    const list = document.getElementById('songs-list');
    const empty = document.getElementById('empty-state');
    if (filteredSongs.length === 0) {
        list.innerHTML = '';
        empty.classList.remove('hidden');
        return;
    }
    empty.classList.add('hidden');
    list.innerHTML = filteredSongs.map(s => {
        const title = s.displayTitle || s.title || s.fileName || 'Sin título';
        const artist = s.displayArtist || s.artist || 'Artista desconocido';
        const album = s.displayAlbum || s.album || '';
        const cover = s.artUri ? `<img src="file:///${(s.artUri||'').replace(/\\/g,'/')}" />` : '♪';
        const fav = s.isFavorite ? '♥' : '♡';
        const favClass = s.isFavorite ? 'fav-btn active' : 'fav-btn';
        return `<div class="song-row" data-id="${s.id}" onclick="playSong(${s.id})">
            <div class="song-cover">${cover}</div>
            <div class="song-info">
                <div class="song-title">${escapeHtml(title)}</div>
                <div class="song-artist">${escapeHtml(artist)}</div>
            </div>
            <div class="song-album">${escapeHtml(album)}</div>
            <button class="${favClass}" onclick="event.stopPropagation(); toggleFavorite(${s.id})">${fav}</button>
            <div class="song-duration">${formatDuration(s.durationMs)}</div>
        </div>`;
    }).join('');
}

function updateCount() {
    const total = allSongs.length;
    const visible = filteredSongs.length;
    document.getElementById('song-count').textContent = visible === total ? `${total} canciones` : `${visible} de ${total} canciones`;
}

// === Playback ===
async function playSong(songId) {
    await WaveAPI.call('playback.playSong', { songId });
}

async function toggleFavorite(songId) {
    const song = allSongs.find(s => s.id === songId);
    if (!song) return;
    await WaveAPI.call('playlists.toggleFavorite', song);
    // Recargar canciones desde DB para que el cambio persista
    await loadSongs();
}

function onPlaybackChanged(state) {
    if (!state) return;
    const isPlaying = state.isPlaying;
    const currentSong = state.currentSong;

    // Update play/pause icon
    const playIcon = document.getElementById('play-icon');
    if (isPlaying) {
        playIcon.innerHTML = '<path d="M6 19h4V5H6v14zm8-14v14h4V5h-4z"/>';
    } else {
        playIcon.innerHTML = '<path d="M8 5v14l11-7z"/>';
    }

    // Update song info
    if (currentSong) {
        const title = currentSong.displayTitle || currentSong.title || 'Sin título';
        const artist = currentSong.displayArtist || currentSong.artist || '—';
        document.getElementById('player-title').textContent = title;
        document.getElementById('player-artist').textContent = artist;

        const cover = document.getElementById('player-cover');
        if (currentSong.artUri) {
            cover.innerHTML = `<img src="file:///${currentSong.artUri.replace(/\\/g,'/')}" />`;
        } else {
            cover.innerHTML = '<span style="color:#0f0f0f;font-size:24px;">♪</span>';
        }

        // Highlight playing song in list
        document.querySelectorAll('.song-row').forEach(el => el.classList.remove('playing'));
        const row = document.querySelector(`[data-id="${currentSong.id}"]`);
        if (row) row.classList.add('playing');
    }

    // Update seek bar
    if (!isSeeking) {
        const seek = document.getElementById('seek-bar');
        const dur = state.durationMs || 100;
        const pos = state.positionMs || 0;
        seek.max = dur;
        seek.value = pos;
        seek.style.setProperty('--progress', `${(pos/dur*100)}%`);
    }
    document.getElementById('time-current').textContent = formatDuration(state.positionMs);
    document.getElementById('time-total').textContent = formatDuration(state.durationMs);

    // Update lyrics highlight
    if (currentLyrics && currentLyrics.isSynced && currentSong) {
        updateLyricsHighlight(state.positionMs);
    }
}

function updateLyricsHighlight(positionMs) {
    if (!currentLyrics || !currentLyrics.lines) return;
    let newIdx = -1;
    for (let i = currentLyrics.lines.length - 1; i >= 0; i--) {
        if (currentLyrics.lines[i].timestampMs <= positionMs) {
            newIdx = i;
            break;
        }
    }
    if (newIdx !== currentLyricIndex && newIdx >= 0) {
        currentLyricIndex = newIdx;
        document.querySelectorAll('.lyric-line').forEach((el, i) => {
            el.classList.toggle('active', i === newIdx);
            if (i === newIdx) {
                el.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
        });
    }
}

function onSeek(e) {
    isSeeking = true;
    const pos = parseInt(e.target.value);
    const dur = parseInt(e.target.max) || 100;
    document.getElementById('time-current').textContent = formatDuration(pos);
    e.target.style.setProperty('--progress', `${(pos/dur*100)}%`);
}

function onSeekEnd(e) {
    WaveAPI.call('playback.seek', { positionMs: parseInt(e.target.value) });
    isSeeking = false;
}

// === Import ===
async function onAddMusic() {
    const result = await WaveAPI.call('library.importFromFolder', '');
    if (result && !result.cancelled) {
        alert(`Importación: ${result.added} nuevas, ${result.duplicates} duplicadas`);
        await loadSongs();
        await loadPlaylists();
    }
}

// === Utils ===
function formatDuration(ms) {
    if (!ms || ms <= 0) return '0:00';
    const totalSec = Math.floor(ms / 1000);
    const min = Math.floor(totalSec / 60);
    const sec = totalSec % 60;
    const hours = Math.floor(min / 60);
    return hours > 0 ? `${hours}:${String(min % 60).padStart(2,'0')}:${String(sec).padStart(2,'0')}` : `${min}:${String(sec).padStart(2,'0')}`;
}

function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}
