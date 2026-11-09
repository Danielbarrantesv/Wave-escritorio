// Wave App v1.3.0
let allSongs = []; let filteredSongs = []; let currentPlaylist = null;
let currentSort = 'recentlyAdded'; let isSeeking = false;
let currentLyrics = null; let currentLyricIndex = -1;

document.addEventListener('DOMContentLoaded', async () => {
    WaveAPI.init();
    WaveAPI.onPlaybackChanged = onPlaybackChanged;
    bindEvents();
    await loadSongs();
    await loadPlaylists();
    await initVolume();
});

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
    document.getElementById('volume-bar').oninput = onVolumeChange;

    const sortChip = document.getElementById('sort-chip');
    const sortMenu = document.getElementById('sort-menu');
    sortChip.onclick = (e) => { e.stopPropagation(); sortMenu.classList.toggle('hidden'); };
    document.addEventListener('click', () => sortMenu.classList.add('hidden'));
    document.querySelectorAll('.sort-option').forEach(el => {
        el.onclick = () => {
            currentSort = el.dataset.sort;
            document.getElementById('sort-label').textContent = el.textContent;
            sortMenu.classList.add('hidden'); applySort();
        };
    });

    // Close context menu on click anywhere
    document.addEventListener('click', () => hideContextMenu());
}

// === Window controls ===
function minimizeWindow() { WaveAPI.call('window.minimize'); }
function maximizeWindow() { WaveAPI.call('window.maximize'); }
function closeWindow() { WaveAPI.call('window.close'); }
function dragWindow() { /* handled by -webkit-app-region: drag in CSS */ }

// === Playback ===
async function onPlayPause() { await WaveAPI.call('playback.togglePlayPause'); }
async function onNext() { await WaveAPI.call('playback.next'); }
async function onPrev() { await WaveAPI.call('playback.prev'); }
async function onShuffle() { await WaveAPI.call('playback.toggleShuffle'); }
async function onRepeat() { await WaveAPI.call('playback.toggleRepeat'); }

async function onVolumeChange(e) {
    const vol = parseInt(e.target.value) / 100;
    await WaveAPI.call('playback.setVolume', { volume: vol });
}

async function initVolume() {
    const state = await WaveAPI.call('playback.getState');
    if (state) {
        document.getElementById('volume-bar').value = Math.round((state.volume || 1) * 100);
    }
}

// === Mini player toggle ===
function onMiniPlayer() {
    WaveAPI.call('window.toggleMiniPlayer');
}

// === Equalizer inline ===
function onEqualizer() {
    const eqPanel = document.getElementById('eq-panel');
    const songsList = document.getElementById('songs-container');
    const lyricsPanel = document.getElementById('lyrics-panel');
    if (eqPanel.classList.contains('hidden')) {
        songsList.classList.add('hidden');
        lyricsPanel.classList.add('hidden');
        eqPanel.classList.remove('hidden');
        initEqualizerBands();
    } else {
        eqPanel.classList.add('hidden');
        songsList.classList.remove('hidden');
    }
}

let eqBands = [];
async function initEqualizerBands() {
    if (eqBands.length > 0) return;
    const freqs = [60, 230, 910, 3600, 14000];
    const labels = ['60Hz', '230Hz', '910Hz', '3.6kHz', '14kHz'];
    const container = document.getElementById('eq-bands');
    container.innerHTML = '';
    for (let i = 0; i < freqs.length; i++) {
        const div = document.createElement('div');
        div.className = 'eq-band';
        div.innerHTML = `
            <span class="eq-value" id="eq-val-${i}">0dB</span>
            <input type="range" class="eq-slider" min="0" max="1" step="0.01" value="0.5"
                oninput="onEqChange(${i}, this.value)" />
            <span class="eq-band-label">${labels[i]}</span>`;
        container.appendChild(div);
        eqBands.push(0.5);
    }
}

function onEqChange(idx, val) {
    eqBands[idx] = parseFloat(val);
    const gainDb = (val - 0.5) * 24; // -12 to +12 dB
    document.getElementById(`eq-val-${idx}`).textContent = `${gainDb >= 0 ? '+' : ''}${gainDb.toFixed(0)}dB`;
    WaveAPI.call('playback.setEqBand', { index: idx, gain: gainDb });
}

// === Lyrics ===
async function onLyrics() {
    const panel = document.getElementById('lyrics-panel');
    const content = document.getElementById('lyrics-content');
    const songsList = document.getElementById('songs-container');
    const eqPanel = document.getElementById('eq-panel');
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
                    `<div class="lyric-line">${escapeHtml(l)}</div>`).join('');
            } else {
                content.innerHTML = '<div class="empty-state"><div class="empty-icon">♪</div><p>Letra no disponible</p></div>';
            }
            eqPanel.classList.add('hidden');
            songsList.classList.add('hidden');
            panel.classList.remove('hidden');
        }
    } else {
        panel.classList.add('hidden');
        songsList.classList.remove('hidden');
    }
}

function showSongsView() {
    document.getElementById('lyrics-panel').classList.add('hidden');
    document.getElementById('eq-panel').classList.add('hidden');
    document.getElementById('songs-container').classList.remove('hidden');
}

// === Library ===
async function loadSongs() {
    allSongs = await WaveAPI.call('library.getSongs') || [];
    applyFilter(); applySort(); renderSongs(); updateCount();
}

async function loadPlaylists() {
    const playlists = await WaveAPI.call('playlists.getAll') || [];
    const container = document.getElementById('playlists-list');
    container.innerHTML = '';
    for (const pl of playlists) {
        const el = document.createElement('div');
        el.className = 'playlist-item';
        el.dataset.playlistId = pl.id;
        const covers = await getPlaylistCovers(pl.id);
        const coverHtml = covers.length > 0
            ? covers.map(c => `<div style="background-image:url('file:///${c.replace(/\\/g,'/')}')"></div>`).join('')
            : '<div class="empty"></div><div class="empty"></div><div class="empty"></div><div class="empty"></div>';
        el.innerHTML = `<div class="playlist-cover-grid">${coverHtml}</div><span>${escapeHtml(pl.name)}</span>`;
        el.onclick = () => selectPlaylist(pl);
        el.oncontextmenu = (e) => { e.preventDefault(); e.stopPropagation(); showPlaylistContextMenu(e, pl); };
        container.appendChild(el);
    }
}

function showPlaylistContextMenu(e, pl) {
    const menu = document.getElementById('playlist-context-menu');
    let items = '';
    if (pl.name !== 'Favoritos') {
        items += `<div class="ctx-item" onclick="renamePlaylist(${pl.id}, '${escapeHtml(pl.name)}')">Editar nombre</div>`;
        items += `<div class="ctx-separator"></div>`;
        items += `<div class="ctx-item" onclick="deletePlaylist(${pl.id})">Eliminar lista</div>`;
    } else {
        items += `<div class="ctx-item" style="opacity:0.5;cursor:default">No se puede eliminar Favoritos</div>`;
    }
    menu.innerHTML = items;
    menu.style.left = e.clientX + 'px';
    menu.style.top = e.clientY + 'px';
    menu.classList.remove('hidden');
}

async function renamePlaylist(id, oldName) {
    hideContextMenu();
    const newName = prompt('Nuevo nombre:', oldName);
    if (newName && newName.trim() && newName !== oldName) {
        await WaveAPI.call('playlists.rename', { playlistId: id, newName: newName.trim() });
        await loadPlaylists();
    }
}

async function deletePlaylist(id) {
    hideContextMenu();
    if (confirm('¿Eliminar esta playlist?')) {
        await WaveAPI.call('playlists.delete', { playlistId: id });
        selectAllSongs();
        await loadPlaylists();
    }
}

async function getPlaylistCovers(playlistId) {
    try {
        const songs = await WaveAPI.call('playlists.getSongs', { playlistId }) || [];
        const withCovers = songs.filter(s => s.artUri);
        // Shuffle and take 4
        const shuffled = withCovers.sort(() => Math.random() - 0.5).slice(0, 4);
        return shuffled.map(s => s.artUri);
    } catch { return []; }
}

function selectAllSongs() {
    document.querySelectorAll('.nav-item').forEach(el => el.classList.remove('active'));
    document.querySelector('[data-view="all"]').classList.add('active');
    document.querySelectorAll('.playlist-item').forEach(el => el.classList.remove('active'));
    currentPlaylist = null;
    showSongsView();
    loadSongs();
}

async function selectPlaylist(pl) {
    document.querySelectorAll('.nav-item').forEach(el => el.classList.remove('active'));
    document.querySelectorAll('.playlist-item').forEach(el => el.classList.remove('active'));
    document.querySelector(`[data-playlist-id="${pl.id}"]`).classList.add('active');
    currentPlaylist = pl;
    showSongsView();
    const songs = await WaveAPI.call('playlists.getSongs', { playlistId: pl.id }) || [];
    allSongs = songs; applyFilter(); applySort(); renderSongs(); updateCount();
}

async function createPlaylist() {
    const name = prompt('Nombre de la nueva playlist:');
    if (!name || !name.trim()) return;
    await WaveAPI.call('playlists.create', { name: name.trim() });
    await loadPlaylists();
}

// === Search & Sort ===
function onSearch() { applyFilter(); renderSongs(); updateCount(); }
function applyFilter() {
    const q = document.getElementById('search').value.trim().toLowerCase();
    if (!q) { filteredSongs = [...allSongs]; return; }
    filteredSongs = allSongs.filter(s =>
        (s.displayTitle || s.title || '').toLowerCase().includes(q) ||
        (s.displayArtist || s.artist || '').toLowerCase().includes(q) ||
        (s.displayAlbum || s.album || '').toLowerCase().includes(q) ||
        (s.fileName || '').toLowerCase().includes(q));
}
function applySort() {
    const sorts = {
        recentlyAdded: (a,b) => (b.dateAddedSec||0)-(a.dateAddedSec||0),
        recentlyPlayed: (a,b) => (b.lastPlayedAt||0)-(a.lastPlayedAt||0),
        title: (a,b) => (a.displayTitle||a.title||'').localeCompare(b.displayTitle||b.title||''),
        artist: (a,b) => (a.displayArtist||a.artist||'').localeCompare(b.displayArtist||b.artist||''),
        duration: (a,b) => (a.durationMs||0)-(b.durationMs||0)
    };
    filteredSongs.sort(sorts[currentSort] || sorts.recentlyAdded);
}

// === Render ===
function renderSongs() {
    const list = document.getElementById('songs-list');
    const empty = document.getElementById('empty-state');
    if (filteredSongs.length === 0) { list.innerHTML = ''; empty.classList.remove('hidden'); return; }
    empty.classList.add('hidden');
    list.innerHTML = filteredSongs.map(s => {
        const title = s.displayTitle || s.title || s.fileName || 'Sin título';
        const artist = s.displayArtist || s.artist || 'Artista desconocido';
        const album = s.displayAlbum || s.album || '';
        const cover = s.artUri ? `<img src="file:///${(s.artUri||'').replace(/\\/g,'/')}" />` : '♪';
        const fav = s.isFavorite ? '♥' : '♡';
        const favClass = s.isFavorite ? 'fav-btn active' : 'fav-btn';
        return `<div class="song-row" data-id="${s.id}" onclick="playSong(${s.id})" oncontextmenu="showContextMenu(event, ${s.id})">
            <div class="song-cover">${cover}</div>
            <div class="song-info"><div class="song-title">${escapeHtml(title)}</div><div class="song-artist">${escapeHtml(artist)}</div></div>
            <div class="song-album">${escapeHtml(album)}</div>
            <button class="${favClass}" onclick="event.stopPropagation(); toggleFavorite(${s.id})">${fav}</button>
            <div class="song-duration">${formatDuration(s.durationMs)}</div>
        </div>`;
    }).join('');
}

function updateCount() {
    const t = allSongs.length, v = filteredSongs.length;
    document.getElementById('song-count').textContent = v === t ? `${t} canciones` : `${v} de ${t} canciones`;
}

// === Playback ===
async function playSong(songId) { await WaveAPI.call('playback.playSong', { songId }); }

async function toggleFavorite(songId) {
    const song = allSongs.find(s => s.id === songId);
    if (!song) return;
    await WaveAPI.call('playlists.toggleFavorite', song);
    await loadSongs();
}

function onPlaybackChanged(state) {
    if (!state) return;
    const playIcon = document.getElementById('play-icon');
    if (state.isPlaying) playIcon.innerHTML = '<path d="M6 19h4V5H6v14zm8-14v14h4V5h-4z"/>';
    else playIcon.innerHTML = '<path d="M8 5v14l11-7z"/>';

    if (state.currentSong) {
        const title = state.currentSong.displayTitle || state.currentSong.title || 'Sin título';
        const artist = state.currentSong.displayArtist || state.currentSong.artist || '—';
        document.getElementById('player-title').textContent = title;
        document.getElementById('player-artist').textContent = artist;
        const cover = document.getElementById('player-cover');
        if (state.currentSong.artUri)
            cover.innerHTML = `<img src="file:///${state.currentSong.artUri.replace(/\\/g,'/')}" />`;
        else cover.innerHTML = '<span style="color:#0f0f0f;font-size:24px;">♪</span>';
        document.querySelectorAll('.song-row').forEach(el => el.classList.remove('playing'));
        const row = document.querySelector(`[data-id="${state.currentSong.id}"]`);
        if (row) row.classList.add('playing');
    }

    if (!isSeeking) {
        const seek = document.getElementById('seek-bar');
        const dur = state.durationMs || 100, pos = state.positionMs || 0;
        seek.max = dur; seek.value = pos;
    }
    document.getElementById('time-current').textContent = formatDuration(state.positionMs);
    document.getElementById('time-total').textContent = formatDuration(state.durationMs);

    // Shuffle/repeat visual state
    document.getElementById('btn-shuffle').classList.toggle('active', state.isShuffle);
    document.getElementById('btn-repeat').classList.toggle('active', state.repeatOne);

    // Volume
    document.getElementById('volume-bar').value = Math.round((state.volume || 1) * 100);

    // Lyrics
    if (currentLyrics && currentLyrics.isSynced) updateLyricsHighlight(state.positionMs);
}

function updateLyricsHighlight(positionMs) {
    if (!currentLyrics || !currentLyrics.lines) return;
    let newIdx = -1;
    for (let i = currentLyrics.lines.length - 1; i >= 0; i--) {
        if (currentLyrics.lines[i].timestampMs <= positionMs) { newIdx = i; break; }
    }
    if (newIdx !== currentLyricIndex && newIdx >= 0) {
        currentLyricIndex = newIdx;
        document.querySelectorAll('.lyric-line').forEach((el, i) => {
            el.classList.toggle('active', i === newIdx);
            if (i === newIdx) el.scrollIntoView({ behavior: 'smooth', block: 'center' });
        });
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

// === Context menu ===
function showContextMenu(e, songId) {
    e.preventDefault();
    e.stopPropagation();
    const menu = document.getElementById('context-menu');
    const song = allSongs.find(s => s.id === songId);
    if (!song) return;
    let items = '';
    // Agregar a playlist submenu
    items += `<div class="ctx-item" onclick="showAddToPlaylist(${songId})">Agregar a playlist...</div>`;
    if (currentPlaylist) {
        items += `<div class="ctx-item" onclick="removeFromPlaylist(${songId})">Eliminar de playlist</div>`;
    }
    items += `<div class="ctx-separator"></div>`;
    items += `<div class="ctx-item" onclick="toggleFavorite(${songId})">${song.isFavorite ? 'Quitar de favoritos' : 'Agregar a favoritos'}</div>`;
    items += `<div class="ctx-separator"></div>`;
    items += `<div class="ctx-item" onclick="editSong(${songId})">Editar información</div>`;
    items += `<div class="ctx-separator"></div>`;
    items += `<div class="ctx-item" onclick="deleteSong(${songId})" style="color:#ff4444">Eliminar canción</div>`;
    menu.innerHTML = items;
    menu.style.left = e.clientX + 'px';
    menu.style.top = e.clientY + 'px';
    menu.classList.remove('hidden');
}

async function showAddToPlaylist(songId) {
    hideContextMenu();
    const playlists = await WaveAPI.call('playlists.getAll') || [];
    const playableLists = playlists.filter(p => p.name !== 'Favoritos');
    if (playableLists.length === 0) {
        alert('No hay playlists. Crea una primero con el botón +');
        return;
    }
    let options = playableLists.map((p, i) => `${i+1}. ${p.name}`).join('\n');
    const choice = prompt(`Selecciona playlist:\n${options}\n\nEscribe el número:`);
    if (!choice) return;
    const idx = parseInt(choice) - 1;
    if (idx < 0 || idx >= playableLists.length) return;
    await WaveAPI.call('playlists.addSongs', { playlistId: playableLists[idx].id, songIds: [songId] });
    alert(`Agregada a "${playableLists[idx].name}"`);
    await loadPlaylists();
}

async function deleteSong(songId) {
    hideContextMenu();
    if (!confirm('¿Eliminar esta canción de la biblioteca?')) return;
    await WaveAPI.call('library.deleteSong', { songId });
    await loadSongs();
}

async function editSong(songId) {
    hideContextMenu();
    const song = allSongs.find(s => s.id === songId);
    if (!song) return;
    const title = prompt('Título:', song.displayTitle || song.title || '');
    const artist = prompt('Artista:', song.displayArtist || song.artist || '');
    const album = prompt('Álbum:', song.displayAlbum || song.album || '');
    const genre = prompt('Género:', song.genre || '');
    if (title !== null) song.title = title;
    if (artist !== null) song.artist = artist;
    if (album !== null) song.album = album;
    if (genre !== null) song.genre = genre;
    await WaveAPI.call('library.editSong', { songId, title, artist, album, genre });
    await loadSongs();
}

function hideContextMenu() { document.getElementById('context-menu').classList.add('hidden'); }

async function removeFromPlaylist(songId) {
    hideContextMenu();
    if (!currentPlaylist) return;
    await WaveAPI.call('playlists.removeSong', { playlistId: currentPlaylist.id, songId });
    await selectPlaylist(currentPlaylist);
}

// === Import ===
async function onAddMusic() {
    const choice = confirm('¿Importar carpeta (Aceptar) o archivos individuales (Cancelar)?');
    if (choice) {
        const result = await WaveAPI.call('library.importFromFolder', '');
        if (result && !result.cancelled) {
            alert(`${result.added} nuevas, ${result.duplicates} duplicadas`);
            await loadSongs(); await loadPlaylists();
        }
    } else {
        const result = await WaveAPI.call('library.importFiles', {});
        if (result && !result.cancelled) {
            alert(`${result.added} nuevas, ${result.duplicates} duplicadas`);
            await loadSongs(); await loadPlaylists();
        }
    }
}

async function onBackup() {
    await WaveAPI.call('backup.export', { path: '' });
}

// === Utils ===
function formatDuration(ms) {
    if (!ms || ms <= 0) return '0:00';
    const s = Math.floor(ms / 1000), m = Math.floor(s / 60), h = Math.floor(m / 60);
    return h > 0 ? `${h}:${String(m%60).padStart(2,'0')}:${String(s%60).padStart(2,'0')}` : `${m}:${String(s%60).padStart(2,'0')}`;
}
function escapeHtml(t) { if (!t) return ''; const d = document.createElement('div'); d.textContent = t; return d.innerHTML; }
