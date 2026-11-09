// Wave App v1.3.0
let allSongs = []; let filteredSongs = []; let currentPlaylist = null;
let currentSort = 'recentlyAdded'; let isSeeking = false;
let isVolumeDragging = false;
let lastVolumeChange = 0; // timestamp del último cambio de volumen del usuario
let currentLyrics = null; let currentLyricIndex = -1;
let currentLyricsSongId = null;

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
    document.getElementById('btn-settings').onclick = onSettings;
    document.getElementById('search').oninput = onSearch;
    document.getElementById('seek-bar').oninput = onSeek;
    document.getElementById('seek-bar').onchange = onSeekEnd;
    document.getElementById('volume-bar').oninput = onVolumeChange;
    document.getElementById('volume-bar').onmousedown = () => { isVolumeDragging = true; };
    document.getElementById('volume-bar').onmouseup = () => { isVolumeDragging = false; };
    document.getElementById('volume-bar').onchange = () => { isVolumeDragging = false; };

    // EQ buttons
    const eqToggle = document.getElementById('eq-toggle');
    const eqReset = document.getElementById('eq-reset');
    if (eqToggle) eqToggle.onclick = onEqToggle;
    if (eqReset) eqReset.onclick = onEqReset;

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
    document.addEventListener('click', () => {
        document.getElementById('context-menu').classList.add('hidden');
        document.getElementById('playlist-context-menu').classList.add('hidden');
    });
}

// === Modal dialog ===
function showModal(title, bodyHtml, actions) {
    const overlay = document.getElementById('modal-overlay');
    const dialog = document.getElementById('modal-dialog');
    document.getElementById('modal-body').innerHTML = `
        <button class="modal-close" onclick="closeModal();">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor"><path d="M8.4,17L7,15.6L10.6,12L7,8.42l1.4-1.4l3.6,3.6l3.58-3.6l1.4,1.4L15.58,17L12,13.4L8.4,17Z"/></svg>
        </button>
        <div class="modal-title">${title}</div>
        ${bodyHtml}
        <div class="modal-actions">${actions}</div>`;
    overlay.classList.remove('hidden');
}
function closeModal() { document.getElementById('modal-overlay').classList.add('hidden'); }
function closeModalIfOverlay(e) { if (e.target.id === 'modal-overlay') closeModal(); }

// Promise-based prompt
function modalPrompt(title, defaultValue = '') {
    return new Promise(resolve => {
        const id = 'modal-input-' + Date.now();
        showModal(title, `<input type="text" class="modal-input" id="${id}" value="${escapeHtml(defaultValue)}" onkeydown="if(event.key==='Enter'){window._modalResolve=this.value;closeModal();}" />`,
            `<button class="modal-btn modal-btn-cancel" onclick="window._modalResolve=null;closeModal();">Cancelar</button>
             <button class="modal-btn modal-btn-primary" onclick="window._modalResolve=document.getElementById('${id}').value;closeModal();">Aceptar</button>`);
        window._modalResolve = undefined;
        const check = setInterval(() => {
            if (document.getElementById('modal-overlay').classList.contains('hidden')) {
                clearInterval(check);
                resolve(window._modalResolve);
            }
        }, 50);
        setTimeout(() => { document.getElementById(id)?.focus(); document.getElementById(id)?.select(); }, 100);
    });
}

// Promise-based confirm
function modalConfirm(title, message) {
    return new Promise(resolve => {
        showModal(title, `<div class="modal-message">${escapeHtml(message)}</div>`,
            `<button class="modal-btn modal-btn-cancel" onclick="window._modalResolve=false;closeModal();">Cancelar</button>
             <button class="modal-btn modal-btn-primary" onclick="window._modalResolve=true;closeModal();">Aceptar</button>`);
        window._modalResolve = undefined;
        const check = setInterval(() => {
            if (document.getElementById('modal-overlay').classList.contains('hidden')) {
                clearInterval(check);
                resolve(window._modalResolve || false);
            }
        }, 50);
    });
}

// Promise-based alert
function modalAlert(title, message) {
    return new Promise(resolve => {
        showModal(title, `<div class="modal-message">${escapeHtml(message)}</div>`,
            `<button class="modal-btn modal-btn-primary" onclick="closeModal();">OK</button>`);
        const check = setInterval(() => {
            if (document.getElementById('modal-overlay').classList.contains('hidden')) {
                clearInterval(check);
                resolve();
            }
        }, 50);
    });
}

function modalConfirmCustom(title, message, btn1Text, btn2Text) {
    return new Promise(resolve => {
        const id = 'modal-confirm-' + Date.now();
        showModal(title, `<div class="modal-message">${escapeHtml(message)}</div>`,
            `<button class="modal-btn modal-btn-cancel" onclick="window._modalChoice='cancel';closeModal();">Cancelar</button>
             <button class="modal-btn modal-btn-cancel" onclick="window._modalChoice='minimize';closeModal();">${escapeHtml(btn1Text)}</button>
             <button class="modal-btn modal-btn-primary" onclick="window._modalChoice='exit';closeModal();">${escapeHtml(btn2Text)}</button>`);
        window._modalChoice = 'cancel';
        const check = setInterval(() => {
            if (document.getElementById('modal-overlay').classList.contains('hidden')) {
                clearInterval(check);
                resolve(window._modalChoice);
            }
        }, 50);
    });
}

// === Window controls ===
function minimizeWindow() { WaveAPI.call('window.minimize'); }
function maximizeWindow() { WaveAPI.call('window.maximize'); }
async function closeWindow() {
    const choice = await modalConfirmCustom('Cerrar Wave', '¿Qué quieres hacer?',
        'Minimizar a bandeja', 'Salir');
    if (choice === 'minimize') {
        WaveAPI.call('window.minimize');
    } else if (choice === 'exit') {
        WaveAPI.call('window.close');
    }
}
function dragWindow() { /* handled by -webkit-app-region: drag in CSS */ }

// === Playback ===
async function onPlayPause() { await WaveAPI.call('playback.togglePlayPause'); }
async function onNext() { await WaveAPI.call('playback.next'); }
async function onPrev() { await WaveAPI.call('playback.prev'); }
async function onShuffle() { await WaveAPI.call('playback.toggleShuffle'); }
async function onRepeat() { await WaveAPI.call('playback.toggleRepeat'); }

async function onVolumeChange(e) {
    lastVolumeChange = Date.now();
    const vol = parseInt(e.target.value) / 100;
    await WaveAPI.call('playback.setVolume', { volume: vol });
    updateVolumeIcon(parseInt(e.target.value));
}

function updateVolumeIcon(volPercent) {
    const container = document.getElementById('vol-icon-container');
    const icon = document.getElementById('vol-icon');
    if (volPercent === 0) {
        icon.innerHTML = '<path d="M7,15V9h4l5-5V20l-5-5H7Zm2-2h2.85L14,15.15V8.85L11.85,11H9v2Zm2.5-1Z"/>';
        container.classList.add('muted');
    } else if (volPercent <= 50) {
        icon.innerHTML = '<path d="M5,15V9H9l5-5V20L9,15H5Zm11,1V7.95q1.13,0.53 1.81,1.63T18.5,12t-0.69,2.4Q17.13,15.48 16,16ZM12,8.85L9.85,11H7v2H9.85L12,15.15V8.85ZM9.5,12Z"/>';
        container.classList.remove('muted');
    } else {
        icon.innerHTML = '<path d="M14,20.73V18.68q2.25-0.65 3.63-2.5T19,11.98t-1.38-4.2Q16.25,5.93 14,5.27V3.22q3.1,0.7 5.05,3.14T21,11.98t-1.95,5.61T14,20.73ZM3,15V9H7l5-5V20L7,15H3Zm11,1V7.95Q15.18,8.5 15.84,9.6T16.5,12q0,1.27-0.66,2.36T14,16ZM10,8.85L7.85,11H5v2H7.85L10,15.15V8.85ZM7.5,12Z"/>';
        container.classList.remove('muted');
    }
}

let lastVolume = 100;
function toggleMute() {
    lastVolumeChange = Date.now();
    const bar = document.getElementById('volume-bar');
    if (parseInt(bar.value) > 0) {
        lastVolume = parseInt(bar.value);
        bar.value = 0;
    } else {
        bar.value = lastVolume;
    }
    onVolumeChange({ target: bar });
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
    const gainDb = (val - 0.5) * 24;
    document.getElementById(`eq-val-${idx}`).textContent = `${gainDb >= 0 ? '+' : ''}${gainDb.toFixed(0)}dB`;
    WaveAPI.call('playback.setEqBand', { index: idx, gain: gainDb });
}

let eqEnabled = true;
function onEqToggle() {
    eqEnabled = !eqEnabled;
    const btn = document.getElementById('eq-toggle');
    btn.textContent = eqEnabled ? 'ON' : 'OFF';
    btn.style.opacity = eqEnabled ? '1' : '0.5';
    WaveAPI.call('playback.setEqEnabled', { enabled: eqEnabled });
}

function onEqReset() {
    for (let i = 0; i < eqBands.length; i++) {
        eqBands[i] = 0.5;
        const slider = document.querySelectorAll('.eq-slider')[i];
        if (slider) slider.value = 0.5;
        document.getElementById(`eq-val-${i}`).textContent = '0dB';
        WaveAPI.call('playback.setEqBand', { index: i, gain: 0 });
    }
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
        let iconHtml;
        if (pl.name === 'Favoritos') {
            iconHtml = '<div class="fav-heart"><svg viewBox="0 0 24 24" fill="currentColor"><path d="M12,21.35l-1.45-1.32C5.4,15.36 2,12.28 2,8.5 2,5.42 4.42,3 7.5,3c1.74,0 3.41,0.81 4.5,2.09C13.09,3.81 14.76,3 16.5,3 19.58,3 22,5.42 22,8.5c0,3.78-3.4,6.86-8.55,11.54L12,21.35z"/></svg></div>';
        } else {
            const covers = await getPlaylistCovers(pl.id);
            iconHtml = covers.length > 0
                ? `<div class="playlist-cover-grid">${covers.map(c => `<div style="background-image:url('file:///${c.replace(/\\/g,'/')}')"></div>`).join('')}</div>`
                : '<div class="playlist-cover-grid"><div class="empty"></div><div class="empty"></div><div class="empty"></div><div class="empty"></div></div>';
        }
        el.innerHTML = `${iconHtml}<span>${escapeHtml(pl.name)}</span>`;
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
    const newName = await modalPrompt('Editar nombre', oldName);
    if (newName && newName.trim() && newName !== oldName) {
        await WaveAPI.call('playlists.rename', { playlistId: id, newName: newName.trim() });
        await loadPlaylists();
    }
}

async function deletePlaylist(id) {
    hideContextMenu();
    if (await modalConfirm('Eliminar playlist', '¿Eliminar esta playlist?')) {
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
    const plEl = document.querySelector(`[data-playlist-id="${pl.id}"]`);
    if (plEl) plEl.classList.add('active');
    currentPlaylist = pl;
    showSongsView();
    // Cargar canciones desde la DB cada vez
    const songs = await WaveAPI.call('playlists.getSongs', { playlistId: pl.id }) || [];
    allSongs = songs;
    applyFilter(); applySort(); renderSongs(); updateCount();
}

async function createPlaylist() {
    // Modal con nombre + botón de seleccionar carpeta
    const nameId = 'pl-name-' + Date.now();
    const folderId = 'pl-folder-' + Date.now();

    showModal('Crear playlist',
        `<div style="display:flex;gap:8px;align-items:center;margin-bottom:12px;">
            <input type="text" class="modal-input" id="${nameId}" placeholder="Nombre de la playlist" style="flex:1;margin:0;" />
            <button class="modal-btn modal-btn-cancel" onclick="window._selectFolder=true;" style="white-space:nowrap;display:flex;align-items:center;gap:6px;padding:10px 12px;">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M4,20q-0.82,0-1.41-0.59T2,18V6Q2,5.18 2.59,4.59T4,4h6l2,2h8q0.83,0 1.41,0.59Q22,7.18 22,8H11.18l-2-2H4V18l2.4-8H23.5l-2.57,8.57q-0.2,0.65-0.74,1.04T19,20H4Z"/></svg>
                <span>Folder</span>
            </button>
        </div>
        <div id="${folderId}" style="color:var(--text-dim);font-size:13px;margin-bottom:16px;"></div>`,
        `<button class="modal-btn modal-btn-cancel" onclick="window._modalChoice='cancel';closeModal();">Cancelar</button>
         <button class="modal-btn modal-btn-primary" onclick="window._modalChoice='create';closeModal();">Crear</button>`);

    window._modalChoice = null;
    window._selectFolder = false;
    window._selectedFolder = null;

    const checkFolder = setInterval(() => {
        if (window._selectFolder) {
            window._selectFolder = false;
            WaveAPI.call('window.selectFolder', {}).then(path => {
                if (path && !path.cancelled) {
                    window._selectedFolder = path;
                    const nameInput = document.getElementById(nameId);
                    if (nameInput && !nameInput.value) {
                        const folderName = path.split('\\').pop().split('/').pop();
                        nameInput.value = folderName;
                    }
                    document.getElementById(folderId).textContent = '📁 ' + path;
                }
            });
        }
    }, 100);

    const check = setInterval(async () => {
        if (document.getElementById('modal-overlay').classList.contains('hidden')) {
            clearInterval(check);
            clearInterval(checkFolder);
            if (window._modalChoice === 'create') {
                const name = document.getElementById(nameId)?.value || '';
                if (name.trim()) {
                    await WaveAPI.call('playlists.create', { name: name.trim() });
                    if (window._selectedFolder) {
                        const playlists = await WaveAPI.call('playlists.getAll') || [];
                        const created = playlists.find(p => p.name === name.trim());
                        if (created) {
                            const result = await WaveAPI.call('library.importFromFolderToPlaylist', {
                                folderPath: window._selectedFolder,
                                playlistId: created.id
                            });
                            if (result) {
                                await modalAlert('Playlist creada', `"${name}" creada con ${result.added} canciones.`);
                            }
                        }
                    }
                    await loadPlaylists();
                }
            }
        }
    }, 50);
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
        return `<div class="song-row" data-id="${s.id}" oncontextmenu="showContextMenu(event, ${s.id})">
            <div class="song-cover">${cover}</div>
            <div class="song-info"><div class="song-title">${escapeHtml(title)}</div><div class="song-artist">${escapeHtml(artist)}</div></div>
            <div class="song-album">${escapeHtml(album)}</div>
            <button class="${favClass}" onclick="event.stopPropagation(); toggleFavorite(${s.id})">${fav}</button>
            <div class="song-duration">${formatDuration(s.durationMs)}</div>
        </div>`;
    }).join('');

    // Event delegation: un solo click reproduce
    list.querySelectorAll('.song-row').forEach(row => {
        row.onclick = () => {
            const id = parseInt(row.dataset.id);
            playSong(id);
        };
    });
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

    // Volume - solo actualizar si el usuario NO cambió el volumen en los últimos 3s
    const now = Date.now();
    if (!isVolumeDragging && (now - lastVolumeChange > 3000)) {
        document.getElementById('volume-bar').value = Math.round((state.volume || 1) * 100);
        updateVolumeIcon(Math.round((state.volume || 1) * 100));
    }

    // Lyrics - recargar si la canción cambió y el panel está visible
    if (state.currentSong) {
        const songId = state.currentSong.id;
        const lyricsPanel = document.getElementById('lyrics-panel');
        if (!lyricsPanel.classList.contains('hidden') && songId !== currentLyricsSongId) {
            currentLyricsSongId = songId;
            // Usar setTimeout para no bloquear el hilo principal
            setTimeout(() => reloadLyrics(state.currentSong), 0);
        }
    }
    if (currentLyrics && currentLyrics.isSynced) updateLyricsHighlight(state.positionMs);
}

async function reloadLyrics(song) {
    try {
        const lyrics = await WaveAPI.call('lyrics.get', song);
        currentLyrics = lyrics;
        const content = document.getElementById('lyrics-content');
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
        currentLyricIndex = -1;
    } catch (e) { /* ignore lyrics errors */ }
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
        await modalAlert('Sin playlists', 'No hay playlists. Crea una primero con el botón +');
        return;
    }
    let bodyHtml = '<div style="display:flex;flex-direction:column;gap:4px;max-height:300px;overflow-y:auto;">';
    playableLists.forEach((p, i) => {
        bodyHtml += `<button class="modal-btn modal-btn-cancel" style="text-align:left;justify-content:flex-start" onclick="window._modalSelect=${i};closeModal();">${escapeHtml(p.name)}</button>`;
    });
    bodyHtml += '</div>';
    showModal('Agregar a playlist', bodyHtml, `<button class="modal-btn modal-btn-cancel" onclick="window._modalSelect=-1;closeModal();">Cancelar</button>`);
    window._modalSelect = -1;
    const check = setInterval(() => {
        if (document.getElementById('modal-overlay').classList.contains('hidden')) {
            clearInterval(check);
            const idx = window._modalSelect;
            if (idx >= 0 && idx < playableLists.length) {
                WaveAPI.call('playlists.addSongs', { playlistId: playableLists[idx].id, songIds: [songId] }).then(() => {
                    loadPlaylists();
                });
            }
        }
    }, 50);
}

async function deleteSong(songId) {
    hideContextMenu();
    if (await modalConfirm('Eliminar canción', '¿Eliminar esta canción de la biblioteca?')) {
        await WaveAPI.call('library.deleteSong', { songId });
        await loadSongs();
    }
}

async function editSong(songId) {
    hideContextMenu();
    const song = allSongs.find(s => s.id === songId);
    if (!song) return;
    const title = await modalPrompt('Título', song.displayTitle || song.title || '');
    if (title === null) return;
    const artist = await modalPrompt('Artista', song.displayArtist || song.artist || '');
    if (artist === null) return;
    const album = await modalPrompt('Álbum', song.displayAlbum || song.album || '');
    if (album === null) return;
    const genre = await modalPrompt('Género', song.genre || '');
    if (genre === null) return;
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
    const choice = await modalConfirm('Agregar música', '¿Importar carpeta (Aceptar) o archivos individuales (Cancelar)?');
    if (choice) {
        const result = await WaveAPI.call('library.importFromFolder', '');
        if (result && !result.cancelled) {
            await modalAlert('Importación', `${result.added} canciones nuevas, ${result.duplicates} duplicadas`);
            await loadSongs(); await loadPlaylists();
        }
    } else {
        const result = await WaveAPI.call('library.importFiles', {});
        if (result && !result.cancelled) {
            await modalAlert('Importación', `${result.added} canciones nuevas, ${result.duplicates} duplicadas`);
            await loadSongs(); await loadPlaylists();
        }
    }
}

async function onBackup() {
    await WaveAPI.call('backup.export', { path: '' });
}

async function onSettings() {
    const choice = await modalConfirm('Configuración', '¿Exportar archivo de logs (wave.log)?\n\nAceptar: Exportar logs\nCancelar: Cerrar');
    if (choice) {
        await WaveAPI.call('window.exportLogs', {});
    }
}

// === Drag & drop ===
async function handleDrop(e) {
    e.preventDefault();
    e.stopPropagation();
    // En WebView2, no podemos acceder a File API paths directamente.
    // Necesitamos que C# maneje el drop. Por ahora, mostrar modal.
    await modalAlert('Arrastrar y soltar', 'Para importar música, usa el botón "+ Agregar música"');
}

// === Utils ===
function formatDuration(ms) {
    if (!ms || ms <= 0) return '0:00';
    const s = Math.floor(ms / 1000), m = Math.floor(s / 60), h = Math.floor(m / 60);
    return h > 0 ? `${h}:${String(m%60).padStart(2,'0')}:${String(s%60).padStart(2,'0')}` : `${m}:${String(s%60).padStart(2,'0')}`;
}
function escapeHtml(t) { if (!t) return ''; const d = document.createElement('div'); d.textContent = t; return d.innerHTML; }
