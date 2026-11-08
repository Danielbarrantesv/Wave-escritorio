// Wave App - lógica de la interfaz
let allSongs = [];
let filteredSongs = [];
let currentPlaylist = null;
let currentSort = 'recentlyAdded';
let isSeeking = false;

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

    document.querySelectorAll('.nav-item').forEach(el => {
        el.onclick = () => selectView(el.dataset.view);
    });

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

    // Drag & drop
    document.body.ondragover = (e) => { e.preventDefault(); };
    document.body.ondrop = (e) => {
        e.preventDefault();
        if (e.dataTransfer.files.length > 0) {
            // Notificar a C# para importar
            // Por ahora, solo mostrar mensaje
            alert('Arrastra archivos a la ventana principal para importar.');
        }
    };
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
        el.innerHTML = `<span>♪</span><span>${escapeHtml(pl.name)}</span>`;
        el.onclick = () => selectPlaylist(pl);
        container.appendChild(el);
    });
}

function selectView(view) {
    document.querySelectorAll('.nav-item').forEach(el => el.classList.remove('active'));
    document.querySelector(`[data-view="${view}"]`).classList.add('active');
    currentPlaylist = null;
    loadSongs();
}

async function selectPlaylist(pl) {
    document.querySelectorAll('.nav-item').forEach(el => el.classList.remove('active'));
    currentPlaylist = pl;
    const songs = await WaveAPI.call('playlists.getSongs', { playlistId: pl.id }) || [];
    allSongs = songs;
    renderSongs();
    updateCount();
}

// === Search ===
function onSearch(e) {
    applyFilter();
    renderSongs();
    updateCount();
}

function applyFilter() {
    const q = document.getElementById('search').value.trim().toLowerCase();
    if (!q) { filteredSongs = [...allSongs]; return; }
    filteredSongs = allSongs.filter(s =>
        (s.title || '').toLowerCase().includes(q) ||
        (s.artist || '').toLowerCase().includes(q) ||
        (s.album || '').toLowerCase().includes(q) ||
        (s.fileName || '').toLowerCase().includes(q)
    );
}

function applySort() {
    const sorts = {
        recentlyAdded: (a, b) => b.dateAddedSec - a.dateAddedSec,
        recentlyPlayed: (a, b) => b.lastPlayedAt - a.lastPlayedAt,
        title: (a, b) => (a.displayTitle || '').localeCompare(b.displayTitle || ''),
        artist: (a, b) => (a.displayArtist || '').localeCompare(b.displayArtist || ''),
        duration: (a, b) => a.durationMs - b.durationMs
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
    list.innerHTML = filteredSongs.map(s => `
        <div class="song-row" data-id="${s.id}" onclick="playSong(${s.id})">
            <div class="song-cover">${s.artUri ? `<img src="file:///${(s.artUri||'').replace(/\\/g,'/')}" />` : ''}</div>
            <div class="song-info">
                <div class="song-title">${escapeHtml(s.displayTitle || s.fileName)}</div>
                <div class="song-artist">${escapeHtml(s.displayArtist || 'Artista desconocido')}</div>
            </div>
            <div class="song-album">${escapeHtml(s.displayAlbum || '')}</div>
            <button class="fav-btn ${s.isFavorite ? 'active' : ''}" onclick="event.stopPropagation(); toggleFavorite(${s.id})">${s.isFavorite ? '♥' : '♡'}</button>
            <div class="song-duration">${formatDuration(s.durationMs)}</div>
        </div>
    `).join('');
}

function updateCount() {
    const total = allSongs.length;
    const visible = filteredSongs.length;
    const label = document.getElementById('song-count');
    label.textContent = visible === total ? `${total} canciones` : `${visible} de ${total} canciones`;
}

// === Playback ===
async function playSong(songId) {
    await WaveAPI.call('playback.playSong', { songId });
}

async function toggleFavorite(songId) {
    const song = allSongs.find(s => s.id === songId);
    if (!song) return;
    song.isFavorite = !song.isFavorite;
    await WaveAPI.call('playlists.toggleFavorite', song);
    renderSongs();
}

function onPlaybackChanged(state) {
    if (!state) return;
    // Update play button
    document.getElementById('btn-play').textContent = state.isPlaying ? '⏸' : '▶';
    // Update song info
    if (state.currentSong) {
        document.getElementById('player-title').textContent = state.currentSong.displayTitle || 'Sin título';
        document.getElementById('player-artist').textContent = state.currentSong.displayArtist || '—';
        const cover = document.getElementById('player-cover');
        if (state.currentSong.artUri) {
            cover.innerHTML = `<img src="file:///${state.currentSong.artUri.replace(/\\/g,'/')}" />`;
        }
    }
    // Update seek bar
    if (!isSeeking) {
        const seek = document.getElementById('seek-bar');
        seek.max = state.durationMs || 100;
        seek.value = state.positionMs || 0;
    }
    document.getElementById('time-current').textContent = formatDuration(state.positionMs);
    document.getElementById('time-total').textContent = formatDuration(state.durationMs);
    // Highlight playing song
    document.querySelectorAll('.song-row').forEach(el => el.classList.remove('playing'));
    if (state.currentSong) {
        const row = document.querySelector(`[data-id="${state.currentSong.id}"]`);
        if (row) row.classList.add('playing');
    }
}

function onSeek(e) {
    isSeeking = true;
    document.getElementById('time-current').textContent = formatDuration(parseInt(e.target.value));
}

function onSeekEnd(e) {
    WaveAPI.call('playback.seek', { positionMs: parseInt(e.target.value) });
    isSeeking = false;
}

// === Playback handlers ===
async function onPlayPause() { await WaveAPI.call('playback.togglePlayPause'); }
async function onNext() { await WaveAPI.call('playback.next'); }
async function onPrev() { await WaveAPI.call('playback.prev'); }
async function onShuffle() { await WaveAPI.call('playback.toggleShuffle'); }
async function onRepeat() { await WaveAPI.call('playback.toggleRepeat'); }

// === Other handlers ===
function onMiniPlayer() {
    // TODO: abrir mini player window via C# bridge
    alert('Mini player próximamente');
}

async function onLyrics() {
    const panel = document.getElementById('lyrics-panel');
    const content = document.getElementById('lyrics-content');
    const songsList = document.getElementById('songs-container');

    if (panel.classList.contains('hidden')) {
        // Mostrar letras
        const state = await WaveAPI.call('playback.getState');
        if (state && state.currentSong) {
            const lyrics = await WaveAPI.call('lyrics.get', state.currentSong);
            if (lyrics && lyrics.lines && lyrics.lines.length > 0) {
                content.innerHTML = lyrics.lines.map(l =>
                    `<div class="lyric-line">${escapeHtml(l.text)}</div>`
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
        } else {
            alert('No hay canción reproduciéndose');
        }
    } else {
        // Ocultar letras
        panel.classList.add('hidden');
        songsList.classList.remove('hidden');
    }
}

function onEqualizer() {
    alert('Ecualizador próximamente');
}

// === Import ===
async function onAddMusic() {
    // Llamar a C# que abre el folder picker nativo
    const result = await WaveAPI.call('library.importFromFolder', '');
    if (result && !result.cancelled) {
        alert(`Importación: ${result.added} nuevas, ${result.duplicates} duplicadas`);
        await loadSongs();
        await loadPlaylists();
    }
}

async function onBackup() {
    const choice = confirm('¿Exportar biblioteca (Aceptar) o importar (Cancelar)?');
    if (choice) {
        const path = prompt('Ruta del archivo de backup:');
        if (path) {
            await WaveAPI.call('backup.export', { path });
            alert('Biblioteca exportada.');
        }
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
