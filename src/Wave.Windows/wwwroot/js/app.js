// Wave App v1.3.0
let allSongs = []; let filteredSongs = []; let currentPlaylist = null;
let currentSort = 'recentlyAdded'; let isSeeking = false;
let isVolumeDragging = false;
let lastVolumeChange = 0; // timestamp del último cambio de volumen del usuario
let currentLyrics = null; let currentLyricIndex = -1;
let currentLyricsSongId = null;
// Multi-selección (Ctrl+click, Shift+click)
let selectedSongIds = new Set();
let lastClickedId = null;

document.addEventListener('DOMContentLoaded', async () => {
    WaveAPI.init();
    WaveAPI.onPlaybackChanged = onPlaybackChanged;
    bindEvents();
    await loadSongs();
    await loadPlaylists();
    await initVolume();
    // Render sliders al arrancar
    renderRecentSlider();
    renderArtistsSlider();
    // Consultar estado inicial de playback para sincronizar el ícono play/pausa
    try {
        const state = await WaveAPI.call('playback.getState');
        if (state && state.currentSong) {
            // Hay canción reproduciéndose/pausada: sincronizar UI
            onPlaybackChanged(state);
        } else if (allSongs.length > 0) {
            // Primera vez o no hay nada cargado: poner la primera canción en pausa
            // para que el usuario vea info al abrir la app
            const first = allSongs[0];
            try {
                const songIds = allSongs.map(s => s.id);
                // SetQueue con autoPlay=false: carga la canción sin reproducir
                await WaveAPI.call('playback.playSong', { songId: first.id, songIds, autoPlay: false });
                const newState = await WaveAPI.call('playback.getState');
                if (newState) onPlaybackChanged(newState);
            } catch (e) { /* ignore */ }
        }
    } catch (e) { /* ignore */ }
});

function bindEvents() {
    document.getElementById('btn-add').onclick = onAddMusic;
    document.getElementById('btn-play').onclick = onPlayPause;
    document.getElementById('btn-next').onclick = onNext;
    document.getElementById('btn-prev').onclick = onPrev;
    document.getElementById('btn-shuffle').onclick = onShuffle;
    document.getElementById('btn-repeat').onclick = onRepeat;
    document.getElementById('btn-fav').onclick = onToggleCurrentFavorite;
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
            sortMenu.classList.add('hidden');
            applySort();
            renderSongs();  // ← CRITICAL: antes faltaba esta llamada, el sort no se veía
        };
    });

    // Close context menu on click anywhere
    document.addEventListener('click', () => {
        document.getElementById('context-menu').classList.add('hidden');
        document.getElementById('playlist-context-menu').classList.add('hidden');
    });

    // Esc limpia la selección
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && selectedSongIds.size > 0) {
            clearSelection();
        }
    });
}

// === Modal dialog ===
function showModal(title, bodyHtml, actions) {
    const overlay = document.getElementById('modal-overlay');
    const dialog = document.getElementById('modal-dialog');
    document.getElementById('modal-body').innerHTML = `
        <button class="modal-close" onclick="closeModal();">
            <svg viewBox="0 0 24 24" fill="currentColor"><path d="M6.4,19L5,17.6L10.6,12L5,6.4L6.4,5L12,10.6L17.6,5L19,6.4L13.4,12L19,17.6L17.6,19L12,13.4L6.4,19Z"/></svg>
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
    updateVolumeBarFill();
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
        updateVolumeBarFill();
        updateVolumeIcon(Math.round((state.volume || 1) * 100));
    }
}

// === Side panel (lyrics / equalizer) ===
// Panel lateral derecho que se desliza desde la derecha.
// No oculta la lista de canciones; muestra la portada actual como fondo difuminado.
function openSidePanel(mode) {
    const panel = document.getElementById('side-panel');
    const lyricsContent = document.getElementById('lyrics-content');
    const eqContent = document.getElementById('eq-content');

    // Forzar reflow para reiniciar la animación si ya está abierto en otro modo
    const wasClosed = panel.classList.contains('closed');
    if (!wasClosed && panel.dataset.mode === mode) {
        // Click en el mismo botón: cerrar
        closeSidePanel();
        return;
    }

    if (mode === 'lyrics') {
        lyricsContent.style.display = '';
        eqContent.style.display = 'none';
    } else if (mode === 'eq') {
        lyricsContent.style.display = 'none';
        eqContent.style.display = '';
        initEqualizerBands();
    }
    panel.dataset.mode = mode;
    panel.classList.remove('closed');
    panel.classList.add('open');
    // Actualizar fondo con la portada actual
    updateSidePanelBackground();
}

function closeSidePanel() {
    const panel = document.getElementById('side-panel');
    panel.classList.remove('open');
    panel.classList.add('closed');
    delete panel.dataset.mode;
}

function updateSidePanelBackground() {
    const bg = document.getElementById('side-panel-bg');
    if (!bg) return;
    // Leer la portada del player-cover actual
    const coverImg = document.querySelector('#player-cover img');
    if (coverImg && coverImg.src) {
        bg.style.backgroundImage = `url('${coverImg.src}')`;
    } else {
        bg.style.backgroundImage = 'none';
    }
}

// === Mini player toggle ===
function onMiniPlayer() {
    WaveAPI.call('window.toggleMiniPlayer');
}

// === Equalizer inline ===
function onEqualizer() {
    openSidePanel('eq');
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
    const panel = document.getElementById('side-panel');
    // Si ya está abierto en modo lyrics, cerrar
    if (panel.dataset.mode === 'lyrics' && !panel.classList.contains('closed')) {
        closeSidePanel();
        return;
    }
    openSidePanel('lyrics');
    // Cargar letra de la canción actual
    const state = await WaveAPI.call('playback.getState');
    if (state && state.currentSong) {
        await reloadLyrics(state.currentSong);
    } else {
        document.getElementById('lyrics-content').innerHTML =
            '<div class="empty-state"><div class="empty-icon">♪</div><p>No hay canción reproduciéndose</p></div>';
    }
}

function showSongsView() {
    // Cerrar side panel al cambiar de vista
    closeSidePanel();
}

// === Library ===
async function loadSongs() {
    allSongs = await WaveAPI.call('library.getSongs') || [];
    applyFilter(); applySort(); renderSongs(); updateCount();
    // Actualizar slider de artistas cuando cambia la biblioteca
    renderArtistsSlider();
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
    // Iconos SVG (path data del usuario)
    const iconImport = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M11.25,16.62h1.5V12.17l1.86,1.86l1.05-1.05L12,9.31L8.36,12.95l1.05,1.05l1.84-1.84v4.45ZM4.31,19.5q-0.76,0-1.28-0.52T2.5,17.69V6.31q0-0.76 0.52-1.28T4.31,4.5H9.8l2,2h7.89q0.76,0 1.28,0.52T21.5,8.31v9.38q0,0.76-0.52,1.28T19.69,19.5H4.31Zm0-1.5H19.69q0.13,0 0.22-0.09T20,17.69V8.31q0-0.13-0.09-0.22T19.69,8H11.18l-2-2H4.31Q4.17,6 4.09,6.09T4,6.31V17.69q0,0.13 0.09,0.22T4.31,18ZM4,18q0,0 0-0.09t0-0.22V6.31Q4,6.17 4,6.09T4,6V8Q4,8 4,8.09T4,8.31v9.38q0,0.13 0,0.22T4,18Z"/></svg>';
    const iconEdit = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M5.31,20.5q-0.76,0-1.28-0.52T3.5,18.69V5.31q0-0.76 0.52-1.28T5.31,3.5h8.66L12.46,5H5.31Q5.19,5 5.1,5.1T5,5.31V18.69q0,0.12 0.1,0.21T5.31,19H18.69q0.12,0 0.21-0.1T19,18.69V11.47l1.5-1.5v8.72q0,0.76-0.52,1.28T18.69,20.5H5.31ZM12,12ZM9.5,14.5V11.07l9.06-9.06q0.23-0.23 0.51-0.34t0.57-0.11T20.2,1.67T20.7,2l1.26,1.25q0.22,0.23 0.33,0.51T22.4,4.33T22.3,4.89T21.97,5.4l-9.09,9.1H9.5ZM20.92,4.34L19.66,3.06l1.26,1.28ZM11,13h1.25l6.23-6.23L17.86,6.14L17.19,5.5L11,11.69V13Zm6.86-6.86L17.19,5.5l0.67,0.64l0.62,0.62L17.86,6.14Z"/></svg>';
    const iconDelete = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M14.45,21.65L13.39,20.6l2.6-2.6l-2.6-2.6l1.05-1.05l2.6,2.6l2.6-2.6L20.7,15.4L18.1,18l2.6,2.6l-1.05,1.05l-2.6-2.6l-2.6,2.6ZM3.3,15.75v-1.5h7v1.5h-7Zm0-4v-1.5h11v1.5H3.3Zm0-4V6.25h11v1.5H3.3Z"/></svg>';
    const iconLock = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M18 8h-1V6c0-2.76-2.24-5-5-5S7 3.24 7 6v2H6c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2h12c1.1 0 2-.9 2-2V10c0-1.1-.9-2-2-2zm-6 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zm3.1-9H8.9V6c0-1.71 1.39-3.1 3.1-3.1 1.71 0 3.1 1.39 3.1 3.1v2z"/></svg>';
    let items = '';
    if (pl.name !== 'Favoritos') {
        items += `<div class="ctx-item" onclick="addFolderToPlaylist(${pl.id})">${iconImport}<span>Importar carpeta a esta playlist</span></div>`;
        items += `<div class="ctx-separator"></div>`;
        items += `<div class="ctx-item" onclick="renamePlaylist(${pl.id}, '${escapeHtml(pl.name)}')">${iconEdit}<span>Editar nombre</span></div>`;
        items += `<div class="ctx-separator"></div>`;
        items += `<div class="ctx-item danger" onclick="deletePlaylist(${pl.id})">${iconDelete}<span>Eliminar playlist</span></div>`;
    } else {
        items += `<div class="ctx-item" style="opacity:0.5;cursor:default">${iconLock}<span>No se puede eliminar Favoritos</span></div>`;
    }
    menu.innerHTML = items;
    menu.style.left = e.clientX + 'px';
    menu.style.top = e.clientY + 'px';
    menu.classList.remove('hidden');
}

async function addFolderToPlaylist(playlistId) {
    hideContextMenu();
    const res = await WaveAPI.call('window.selectFolder', {});
    if (!res || res.cancelled || !res.path) return;
    const result = await WaveAPI.call('library.importFromFolderToPlaylist', {
        folderPath: res.path,
        playlistId
    });
    if (result && !result.cancelled) {
        await loadPlaylists();
        // Recargar vista si estamos en esa playlist
        if (currentPlaylist && currentPlaylist.id === playlistId) {
            await selectPlaylist(currentPlaylist);
        }
        await modalAlert('Importación completa',
            `${result.added} canciones nuevas, ${result.duplicates} ya en biblioteca.`);
    } else if (result && result.error) {
        await modalAlert('Error', result.error);
    }
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
    // Limpiar el buscador para no filtrar las canciones de la nueva vista
    document.getElementById('search').value = '';
    showSongsView();
    loadSongs();
}

async function selectPlaylist(pl) {
    document.querySelectorAll('.nav-item').forEach(el => el.classList.remove('active'));
    document.querySelectorAll('.playlist-item').forEach(el => el.classList.remove('active'));
    const plEl = document.querySelector(`[data-playlist-id="${pl.id}"]`);
    if (plEl) plEl.classList.add('active');
    currentPlaylist = pl;
    // Limpiar el buscador para no filtrar las canciones de la playlist con el artista anterior
    document.getElementById('search').value = '';
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
            WaveAPI.call('window.selectFolder', {}).then(res => {
                // res = { cancelled: false, path: "C:\\..." }
                if (res && !res.cancelled && res.path) {
                    const folderStr = res.path;
                    window._selectedFolder = folderStr;
                    const nameInput = document.getElementById(nameId);
                    if (nameInput && !nameInput.value) {
                        const folderName = folderStr.split('\\').pop().split('/').pop();
                        nameInput.value = folderName;
                    }
                    document.getElementById(folderId).innerHTML =
                        `📁 <span style="color:var(--accent);word-break:break-all">${escapeHtml(folderStr)}</span>`;
                } else {
                    document.getElementById(folderId).textContent = 'No se seleccionó ninguna carpeta';
                }
            }).catch(err => {
                document.getElementById(folderId).textContent = 'Error: ' + err;
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
                    const created = await WaveAPI.call('playlists.create', { name: name.trim() });
                    let addedMsg = '';
                    if (window._selectedFolder) {
                        // Buscar la playlist recién creada y cargar la carpeta
                        const playlists = await WaveAPI.call('playlists.getAll') || [];
                        const target = playlists.find(p => p.name === name.trim());
                        if (target) {
                            const result = await WaveAPI.call('library.importFromFolderToPlaylist', {
                                folderPath: window._selectedFolder,
                                playlistId: target.id
                            });
                            if (result && !result.cancelled) {
                                addedMsg = `${result.added} canciones nuevas, ${result.duplicates} ya en biblioteca.`;
                            } else {
                                addedMsg = 'No se pudo importar la carpeta.';
                            }
                        } else {
                            addedMsg = 'No se encontró la playlist creada.';
                        }
                    } else {
                        addedMsg = 'Playlist vacía creada. Agrega canciones con clic derecho.';
                    }
                    await loadPlaylists();
                    await modalAlert('Playlist creada', `"${name}" creada. ${addedMsg}`);
                    // Seleccionar automáticamente la playlist recién creada
                    const playlists = await WaveAPI.call('playlists.getAll') || [];
                    const created2 = playlists.find(p => p.name === name.trim());
                    if (created2) await selectPlaylist(created2);
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
    if (filteredSongs.length === 0) { list.innerHTML = ''; empty.classList.remove('hidden'); updateSelectionBar(); return; }
    empty.classList.add('hidden');
    list.innerHTML = filteredSongs.map(s => {
        const title = s.displayTitle || s.title || s.fileName || 'Sin título';
        const artist = s.displayArtist || s.artist || 'Artista desconocido';
        const album = s.displayAlbum || s.album || '';
        const cover = s.artUri ? `<img src="file:///${(s.artUri||'').replace(/\\/g,'/')}" />` :
            `<div class="recent-placeholder"><svg viewBox="0 0 24 24" fill="currentColor"><path d="M3.18,19.83Q2,18.65 2,17t1.18-2.83T6,13q0.58,0 1.06,0.14T8,13.55V5L20,3V15q0,1.65-1.18,2.82T16,19t-2.82-1.18T12,15t1.18-2.83T16,11q0.57,0 1.06,0.14T18,11.55V7.43L10,9v8q0,1.65-1.17,2.82T6,21T3.18,19.83Z"/></svg></div>`;
        const isSelected = selectedSongIds.has(s.id);
        const selClass = isSelected ? ' selected' : '';
        const checkIcon = isSelected
            ? '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z"/></svg>'
            : '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="4" y="4" width="16" height="16" rx="2"/></svg>';
        // Corazón verde, mismo ícono que la lista de Favoritos del sidebar
        const favSvg = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12,21.35l-1.45-1.32C5.4,15.36 2,12.28 2,8.5 2,5.42 4.42,3 7.5,3c1.74,0 3.41,0.81 4.5,2.09C13.09,3.81 14.76,3 16.5,3 19.58,3 22,5.42 22,8.5c0,3.78-3.4,6.86-8.55,11.54L12,21.35z"/></svg>';
        const favClass = s.isFavorite ? 'fav-btn active' : 'fav-btn';
        return `<div class="song-row${selClass}" data-id="${s.id}" oncontextmenu="showContextMenu(event, ${s.id})">
            <div class="song-check" onclick="event.stopPropagation(); toggleRowSelection(${s.id}, event)">${checkIcon}</div>
            <div class="song-cover">${cover}</div>
            <div class="song-info"><div class="song-title">${escapeHtml(title)}</div><div class="song-artist">${escapeHtml(artist)}</div></div>
            <div class="song-album">${escapeHtml(album)}</div>
            <button class="${favClass}" onclick="event.stopPropagation(); toggleFavorite(${s.id})">${favSvg}</button>
            <div class="song-duration">${formatDuration(s.durationMs)}</div>
        </div>`;
    }).join('');

    // Click handler con soporte multi-selección (Ctrl/Shift)
    list.querySelectorAll('.song-row').forEach(row => {
        row.onclick = (e) => {
            const id = parseInt(row.dataset.id);
            if (e.ctrlKey || e.metaKey) {
                toggleRowSelection(id, e);
            } else if (e.shiftKey && lastClickedId !== null) {
                selectRange(lastClickedId, id);
            } else {
                // Click normal: limpiar selección y reproducir
                clearSelection();
                playSong(id);
                lastClickedId = id;
            }
        };
    });
    updateSelectionBar();
}

function toggleRowSelection(id, e) {
    if (selectedSongIds.has(id)) selectedSongIds.delete(id);
    else selectedSongIds.add(id);
    lastClickedId = id;
    renderSongs();
}

function selectRange(fromId, toId) {
    const fromIdx = filteredSongs.findIndex(s => s.id === fromId);
    const toIdx = filteredSongs.findIndex(s => s.id === toId);
    if (fromIdx < 0 || toIdx < 0) return;
    const lo = Math.min(fromIdx, toIdx);
    const hi = Math.max(fromIdx, toIdx);
    for (let i = lo; i <= hi; i++) selectedSongIds.add(filteredSongs[i].id);
    renderSongs();
}

function clearSelection() {
    selectedSongIds.clear();
    renderSongs();
}

function selectAll() {
    filteredSongs.forEach(s => selectedSongIds.add(s.id));
    renderSongs();
}

function updateSelectionBar() {
    const bar = document.getElementById('selection-bar');
    const count = document.getElementById('selection-count');
    if (!bar || !count) return;
    const n = selectedSongIds.size;
    if (n > 0) {
        bar.classList.remove('hidden');
        count.textContent = `${n} seleccionada${n > 1 ? 's' : ''}`;
        document.body.classList.add('has-selection');
    } else {
        bar.classList.add('hidden');
        document.body.classList.remove('has-selection');
    }
}

async function addSelectionToPlaylist() {
    const ids = Array.from(selectedSongIds);
    if (ids.length === 0) return;
    const playlists = await WaveAPI.call('playlists.getAll') || [];
    const playableLists = playlists.filter(p => p.name !== 'Favoritos');

    let bodyHtml = '<div class="playlist-picker">';
    // Opción crear nueva playlist con las canciones seleccionadas
    bodyHtml += `<button class="modal-btn modal-btn-cancel playlist-picker-create" onclick="window._modalSelect='create';closeModal();">
        <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" style="margin-right:8px;vertical-align:middle"><path d="M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z"/></svg>
        Crear nueva playlist con ${ids.length} canción${ids.length>1?'es':''}
    </button>`;
    if (playableLists.length > 0) {
        playableLists.forEach((p, i) => {
            bodyHtml += `<button class="modal-btn modal-btn-cancel" onclick="window._modalSelect=${i};closeModal();">${escapeHtml(p.name)}</button>`;
        });
    } else {
        bodyHtml += '<div style="color:var(--text-faint);font-size:13px;padding:8px;text-align:center">No hay playlists. Crea una nueva arriba ↑</div>';
    }
    bodyHtml += '</div>';

    showModal(`Agregar ${ids.length} canción${ids.length>1?'es':''} a playlist`, bodyHtml,
        `<button class="modal-btn modal-btn-cancel" onclick="window._modalSelect=-1;closeModal();">Cancelar</button>`);

    window._modalSelect = -1;
    const check = setInterval(async () => {
        if (document.getElementById('modal-overlay').classList.contains('hidden')) {
            clearInterval(check);
            const sel = window._modalSelect;
            if (sel === 'create') {
                // Crear nueva playlist + agregar canciones
                const name = await modalPrompt('Nombre de la nueva playlist', '');
                if (name && name.trim()) {
                    await WaveAPI.call('playlists.create', { name: name.trim(), songIds: ids });
                    await loadPlaylists();
                    clearSelection();
                    await modalAlert('Playlist creada', `"${name}" creada con ${ids.length} canciones.`);
                }
            } else if (typeof sel === 'number' && sel >= 0 && sel < playableLists.length) {
                await WaveAPI.call('playlists.addSongs', { playlistId: playableLists[sel].id, songIds: ids });
                await loadPlaylists();
                clearSelection();
                await modalAlert('Agregado', `${ids.length} canciones agregadas a "${playableLists[sel].name}"`);
            }
        }
    }, 50);
}

async function deleteSelection() {
    const ids = Array.from(selectedSongIds);
    if (ids.length === 0) return;
    // Si estamos en una playlist (no en "Todas las canciones"):
    // - Eliminar SOLO de la playlist, NO de la biblioteca
    // Si estamos en "Todas las canciones":
    // - Eliminar de la biblioteca (afecta a todas las playlists)
    if (currentPlaylist) {
        if (await modalConfirm('Quitar de playlist', `¿Quitar ${ids.length} canción${ids.length>1?'es':''} de "${currentPlaylist.name}"?\n\nNo se borrarán de la biblioteca.`)) {
            for (const id of ids) {
                await WaveAPI.call('playlists.removeSong', { playlistId: currentPlaylist.id, songId: id });
            }
            clearSelection();
            await selectPlaylist(currentPlaylist);
        }
    } else {
        if (await modalConfirm('Eliminar canciones', `¿Eliminar ${ids.length} canciones de la biblioteca?\n\nSe borrarán de TODAS las playlists.`)) {
            for (const id of ids) await WaveAPI.call('library.deleteSong', { songId: id });
            clearSelection();
            await loadSongs();
        }
    }
}

function updateCount() {
    const t = allSongs.length, v = filteredSongs.length;
    document.getElementById('song-count').textContent = v === t ? `${t} canciones` : `${v} de ${t} canciones`;
}

// === Playback ===
// El contexto (IDs visibles actualmente) se envía para que shuffle/next/prev
// se mantengan DENTRO de la playlist/vista activa y NO mezclen con toda la biblioteca.
async function playSong(songId) {
    const songIds = filteredSongs.map(s => s.id);
    await WaveAPI.call('playback.playSong', { songId, songIds });
}

async function onPlayPause() {
    // Optimistic UI: cambiar el ícono inmediatamente antes del round-trip a C#
    const playIcon = document.getElementById('play-icon');
    const currentlyPlaying = playIcon.dataset.playing === 'true';
    if (currentlyPlaying) {
        // Va a pausar: mostrar play
        playIcon.innerHTML = '<path d="M8 5v14l11-7z"/>';
        playIcon.dataset.playing = 'false';
    } else {
        // Va a reproducir: mostrar pausa
        playIcon.innerHTML = '<path d="M6 19h4V5H6v14zm8-14v14h4V5h-4z"/>';
        playIcon.dataset.playing = 'true';
    }
    await WaveAPI.call('playback.togglePlayPause');
}

async function toggleFavorite(songId) {
    const song = allSongs.find(s => s.id === songId);
    if (!song) return;
    await WaveAPI.call('playlists.toggleFavorite', song);
    await loadSongs();
}

// Toggle favorito de la canción ACTUAL (botón corazón en la playerbar)
async function onToggleCurrentFavorite() {
    const state = window._lastPlaybackState;
    if (!state || !state.currentSong) {
        await modalAlert('Sin canción', 'No hay ninguna canción reproduciéndose.');
        return;
    }
    await WaveAPI.call('playlists.toggleFavorite', state.currentSong);
    // Actualizar el estado visual del botón inmediatamente (optimistic)
    const btn = document.getElementById('btn-fav');
    const isFav = btn.classList.contains('active');
    btn.classList.toggle('active', !isFav);
    // Recargar la biblioteca para actualizar el corazón en la fila
    await loadSongs();
}

// === Recent songs slider (últimas 30 reproducidas, persistente) ===
const RECENT_KEY = 'recent_songs_v1';
const RECENT_MAX = 30;

function loadRecentSongs() {
    try {
        const raw = localStorage.getItem(RECENT_KEY);
        if (!raw) return [];
        const arr = JSON.parse(raw);
        return Array.isArray(arr) ? arr : [];
    } catch { return []; }
}

function saveRecentSongs(arr) {
    try { localStorage.setItem(RECENT_KEY, JSON.stringify(arr.slice(0, RECENT_MAX))); } catch {}
}

async function pushRecentSong(song) {
    if (!song || !song.id) return;
    let recents = loadRecentSongs();
    // Quitar si ya existe (para moverlo al frente)
    recents = recents.filter(r => r.id !== song.id);
    recents.unshift({ id: song.id, title: song.displayTitle || song.title, artist: song.displayArtist || song.artist, artUri: song.artUri });
    recents = recents.slice(0, RECENT_MAX);
    saveRecentSongs(recents);
    renderRecentSlider();
}

function renderRecentSlider() {
    const section = document.getElementById('recent-section');
    const slider = document.getElementById('recent-slider');
    if (!slider) return;
    const recents = loadRecentSongs();
    if (recents.length === 0) {
        section.style.display = 'none';
        return;
    }
    section.style.display = '';
    const placeholderSvg = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M3.18,19.83Q2,18.65 2,17t1.18-2.83T6,13q0.58,0 1.06,0.14T8,13.55V5L20,3V15q0,1.65-1.18,2.82T16,19t-2.82-1.18T12,15t1.18-2.83T16,11q0.57,0 1.06,0.14T18,11.55V7.43L10,9v8q0,1.65-1.17,2.82T6,21T3.18,19.83Z"/></svg>';
    slider.innerHTML = recents.map(r => {
        const cover = r.artUri
            ? `<img src="file:///${(r.artUri||'').replace(/\\/g,'/')}" />`
            : `<div class="recent-placeholder">${placeholderSvg}</div>`;
        const tooltip = `${r.title || 'Sin título'} — ${r.artist || 'Artista desconocido'}`;
        return `<div class="recent-card" data-id="${r.id}" title="${escapeHtml(tooltip)}">${cover}</div>`;
    }).join('');
    // Click en card → reproducir
    slider.querySelectorAll('.recent-card').forEach(card => {
        card.onclick = () => {
            const id = parseInt(card.dataset.id);
            if (id) playSong(id);
        };
    });
    // Resaltar la que está sonando
    const state = window._lastPlaybackState;
    if (state && state.currentSong) updateRecentPlaying(state.currentSong.id);
}

function updateRecentPlaying(songId) {
    document.querySelectorAll('.recent-card').forEach(c => {
        c.classList.toggle('playing', parseInt(c.dataset.id) === songId);
    });
}

// === Artists slider ===
// Agrupa canciones por artista, toma una portada por artista (la primera con cover),
// y muestra el total de canciones del artista.
function renderArtistsSlider() {
    const section = document.getElementById('artists-section');
    const slider = document.getElementById('artists-slider');
    if (!slider) return;
    if (allSongs.length === 0) {
        section.style.display = 'none';
        return;
    }

    // Agrupar por artista (case-insensitive, sin empty)
    const groups = {};
    for (const s of allSongs) {
        const name = (s.displayArtist || s.artist || '').trim();
        if (!name) continue;
        const key = name.toLowerCase();
        if (!groups[key]) {
            groups[key] = { name, songs: [], coverUri: null };
        }
        groups[key].songs.push(s);
        if (!groups[key].coverUri && s.artUri) groups[key].coverUri = s.artUri;
    }

    const artists = Object.values(groups).sort((a, b) => b.songs.length - a.songs.length);
    if (artists.length === 0) {
        section.style.display = 'none';
        return;
    }
    section.style.display = '';

    const placeholderSvg = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M3.18,19.83Q2,18.65 2,17t1.18-2.83T6,13q0.58,0 1.06,0.14T8,13.55V5L20,3V15q0,1.65-1.18,2.82T16,19t-2.82-1.18T12,15t1.18-2.83T16,11q0.57,0 1.06,0.14T18,11.55V7.43L10,9v8q0,1.65-1.17,2.82T6,21T3.18,19.83Z"/></svg>';

    slider.innerHTML = artists.map(a => {
        const cover = a.coverUri
            ? `<img src="file:///${(a.coverUri||'').replace(/\\/g,'/')}" />`
            : `<div class="recent-placeholder">${placeholderSvg}</div>`;
        return `<div class="artist-card" data-artist="${escapeHtml(a.name)}" title="${escapeHtml(a.name)} — ${a.songs.length} canciones">
            <div class="artist-cover">${cover}<span class="count-badge">${a.songs.length}</span></div>
            <div class="artist-name">${escapeHtml(a.name)}</div>
        </div>`;
    }).join('');

    // Click en card → filtrar la biblioteca por ese artista (usando el search)
    slider.querySelectorAll('.artist-card').forEach(card => {
        card.onclick = () => {
            const artistName = card.dataset.artist;
            if (!artistName) return;
            const search = document.getElementById('search');
            search.value = artistName;
            onSearch();
        };
    });
}

function onPlaybackChanged(state) {
    window._lastPlaybackState = state;
    if (!state) return;
    const playIcon = document.getElementById('play-icon');

    // Si la canción cambió y llegó un evento de "pausa" muy rápido (probablemente
    // porque el engine está cargando la siguiente), ignoramos esa pausa momentánea
    // para que el icono NO salte a "play" y se mantenga en "pausa" como el usuario espera.
    if (!state.isPlaying && state.currentSong) {
        const songId = state.currentSong.id;
        if (window._lastSongId !== songId) {
            // Canción cambió. Si justo ahora llegó una pausa y antes estaba sonando,
            // asumimos que es la transición entre canciones: mantener el icono actual.
            if (playIcon.dataset.playing === 'true') {
                // ignorar esta pausa momentánea — el engine terminará de cargar y disparará IsPlaying=true
                // pero si en 500ms no llega un nuevo evento de playing, sí mostramos pausa
                if (window._pendingPauseTimer) clearTimeout(window._pendingPauseTimer);
                window._pendingPauseTimer = setTimeout(() => {
                    if (!window._lastPlaybackState.isPlaying) {
                        playIcon.innerHTML = '<path d="M8 5v14l11-7z"/>';
                        playIcon.dataset.playing = 'false';
                    }
                }, 500);
                window._lastSongId = songId;
                // Actualizar info de canción sin tocar el icono
                updatePlayerSongInfo(state);
                updateSeekBar(state);
                updateShuffleRepeat(state);
                updateVolumeDisplay(state);
                handleLyricsAndRecents(state);
                return;
            }
        }
        window._lastSongId = songId;
    }

    if (state.isPlaying) {
        if (window._pendingPauseTimer) { clearTimeout(window._pendingPauseTimer); window._pendingPauseTimer = null; }
        playIcon.innerHTML = '<path d="M6 19h4V5H6v14zm8-14v14h4V5h-4z"/>';
        playIcon.dataset.playing = 'true';
    } else {
        playIcon.innerHTML = '<path d="M8 5v14l11-7z"/>';
        playIcon.dataset.playing = 'false';
    }

    updatePlayerSongInfo(state);
    updateSeekBar(state);
    updateShuffleRepeat(state);
    updateVolumeDisplay(state);
    handleLyricsAndRecents(state);
    if (currentLyrics && currentLyrics.isSynced) updateLyricsHighlight(state.positionMs);
}

// Helpers extraídos de onPlaybackChanged para evitar duplicación
function updatePlayerSongInfo(state) {
    if (!state.currentSong) return;
    const title = state.currentSong.displayTitle || state.currentSong.title || 'Sin título';
    const artist = state.currentSong.displayArtist || state.currentSong.artist || '—';
    const titleEl = document.getElementById('player-title');
    const artistEl = document.getElementById('player-artist');
    titleEl.textContent = title;
    artistEl.textContent = artist;
    // Marquee con JS: solo activa si el texto no cabe en el contenedor
    setupMarquee(titleEl, title);
    setupMarquee(artistEl, artist);
    const cover = document.getElementById('player-cover');
    if (state.currentSong.artUri)
        cover.innerHTML = `<img src="file:///${state.currentSong.artUri.replace(/\\/g,'/')}" />`;
    else cover.innerHTML = `<div class="placeholder-icon"><svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" height="24px" width="24px" fill="currentColor"><path d="M3.18,19.83Q2,18.65 2,17t1.18-2.83T6,13q0.58,0 1.06,0.14T8,13.55V5L20,3V15q0,1.65-1.18,2.82T16,19t-2.82-1.18T12,15t1.18-2.83T16,11q0.57,0 1.06,0.14T18,11.55V7.43L10,9v8q0,1.65-1.17,2.82T6,21T3.18,19.83Z"/></svg></div>`;
    document.querySelectorAll('.song-row').forEach(el => el.classList.remove('playing'));
    const row = document.querySelector(`[data-id="${state.currentSong.id}"]`);
    if (row) row.classList.add('playing');
    updateRecentPlaying(state.currentSong.id);
    // Actualizar estado del botón corazón en la playerbar
    const favBtn = document.getElementById('btn-fav');
    if (favBtn) favBtn.classList.toggle('active', !!state.currentSong.isFavorite);
}

// Configura el efecto marquee en un elemento si el texto no cabe en su contenedor padre.
// Mide scrollWidth vs clientWidth del padre y setea un CSS variable --marquee-distance
// que usa la animación para desplazar lo justo.
function setupMarquee(el, text) {
    if (!el || !el.parentElement) return;
    // Quitar marquee primero para medir
    el.classList.remove('marquee');
    el.style.transform = '';
    // Forzar reflow para medir bien
    void el.offsetWidth;
    const parent = el.parentElement;
    const textWidth = el.scrollWidth;
    const containerWidth = parent.clientWidth;
    if (textWidth > containerWidth + 2) {
        // El texto no cabe: activar marquee con distancia dinámica
        const distance = textWidth - containerWidth + 30; // +30px de margen
        el.style.setProperty('--marquee-distance', `-${distance}px`);
        el.classList.add('marquee');
    }
}

function updateSeekBar(state) {
    const seek = document.getElementById('seek-bar');
    const dur = state.durationMs || 100, pos = state.positionMs || 0;
    if (!isSeeking) {
        seek.max = dur; seek.value = pos;
    }
    document.getElementById('time-current').textContent = formatDuration(state.positionMs);
    document.getElementById('time-total').textContent = formatDuration(state.durationMs);
    updateSeekBarFill();
}

// Relleno visual del seek-bar con gradient verde-oscuro
function updateSeekBarFill() {
    const seek = document.getElementById('seek-bar');
    if (!seek) return;
    const max = parseFloat(seek.max) || 100;
    const val = parseFloat(seek.value) || 0;
    const pct = Math.max(0, Math.min(100, (val / max) * 100));
    seek.style.backgroundImage = `linear-gradient(to right, var(--accent) 0%, var(--accent) ${pct}%, var(--border) ${pct}%, var(--border) 100%)`;
}

// Relleno visual del volume-bar con gradient verde-oscuro
function updateVolumeBarFill() {
    const bar = document.getElementById('volume-bar');
    if (!bar) return;
    const max = parseFloat(bar.max) || 100;
    const val = parseFloat(bar.value) || 0;
    const pct = Math.max(0, Math.min(100, (val / max) * 100));
    bar.style.backgroundImage = `linear-gradient(to right, var(--accent) 0%, var(--accent) ${pct}%, var(--border) ${pct}%, var(--border) 100%)`;
}

function updateShuffleRepeat(state) {
    document.getElementById('btn-shuffle').classList.toggle('active', state.isShuffle);
    document.getElementById('btn-repeat').classList.toggle('active', state.repeatOne);
}

function updateVolumeDisplay(state) {
    const now = Date.now();
    if (!isVolumeDragging && (now - lastVolumeChange > 3000)) {
        document.getElementById('volume-bar').value = Math.round((state.volume || 1) * 100);
        updateVolumeIcon(Math.round((state.volume || 1) * 100));
    }
    updateVolumeBarFill();
}

function handleLyricsAndRecents(state) {
    if (!state.currentSong) return;
    const songId = state.currentSong.id;
    const sidePanel = document.getElementById('side-panel');
    if (sidePanel && sidePanel.dataset.mode === 'lyrics' && songId !== currentLyricsSongId) {
        currentLyricsSongId = songId;
        updateSidePanelBackground();
        setTimeout(() => reloadLyrics(state.currentSong), 0);
    }
    if (songId !== window._lastRecentPushedId) {
        window._lastRecentPushedId = songId;
        pushRecentSong(state.currentSong);
    }
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
    updateSeekBarFill();
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

    // Si la canción no está seleccionada y hay otras seleccionadas, resetear selección a esta
    if (!selectedSongIds.has(songId) && selectedSongIds.size > 0) {
        clearSelection();
    }
    // Asegurar que al menos esta canción esté seleccionada
    if (selectedSongIds.size === 0) {
        selectedSongIds.add(songId);
        renderSongs();
    }

    // Iconos SVG (path data del usuario)
    const iconAddToPlaylist = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M3.25,15.75v-1.5h7v1.5h-7Zm0-4v-1.5h11v1.5h-11Zm0-4V6.25h11v1.5h-11Zm13,12v-4h-4v-1.5h4v-4h1.5v4h4v1.5h-4v4h-1.5Z"/></svg>';
    const iconRemoveFav = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M11,11.48Zm0,8.85L8.83,18.38q-2.04-1.86-3.44-3.25T3.12,12.6T1.88,10.43T1.5,8.35q0-2.13 1.44-3.56T6.5,3.37q1.31,0 2.47,0.61T11,5.73q0.86-1.14 2.03-1.76T15.5,3.37q1.94,0 3.49,1.36t1.55,3.62q0,0.4-0.07,0.83t-0.22,0.88q0,0-0.26,0t-0.55,0t-0.55,0t-0.26,0q0.17-0.5 0.26-0.94t0.09-0.83q0-1.82-1.22-2.62T15.5,4.87q-1.25,0-2.2,0.69T11.49,7.5H10.51Q9.67,6.23 8.68,5.55T6.5,4.87q-1.44,0-2.47,0.99T3,8.35q0,0.83 0.35,1.69T4.6,12.03t2.45,2.63T11,18.3q0.71-0.63 1.52-1.34t1.36-1.24q0,0 0.17,0.17t0.37,0.37t0.37,0.37t0.17,0.17q-0.57,0.53-1.36,1.22T12.1,19.33L11,20.33Zm3.87-6.58v-1.5h7.5v1.5h-7.5Z"/></svg>';
    const iconAddFav = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12,21.35l-1.45-1.32C5.4,15.36 2,12.28 2,8.5 2,5.42 4.42,3 7.5,3c1.74,0 3.41,0.81 4.5,2.09C13.09,3.81 14.76,3 16.5,3 19.58,3 22,5.42 22,8.5c0,3.78-3.4,6.86-8.55,11.54L12,21.35z"/></svg>';
    const iconEditSong = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M4.5,14V12.5h7V14h-7Zm0-4V8.5h11V10H4.5Zm0-4V4.5h11V6H4.5Zm8.62,13.5V16.86l5.43-5.4q0.19-0.19 0.4-0.26t0.43-0.08q0.24,0 0.46,0.09t0.4,0.27l0.93,0.93q0.16,0.19 0.25,0.4t0.09,0.43t-0.08,0.44T21.16,14.1l-5.4,5.4H13.12Zm7.19-6.26l-0.92-0.93l0.92,0.93Zm-6,5.07h0.95l3.25-3.26l-0.46-0.47L17.58,14.1l-3.27,3.26v0.95Zm3.74-3.74L17.58,14.1l0.92,0.94l-0.46-0.47Z"/></svg>';
    const iconDeleteSong = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M11.65,13.14l2.1-2.1l2.1,2.1l1.04-1.04L14.79,10l2.1-2.1L15.85,6.86l-2.1,2.1l-2.1-2.1L10.61,7.9l2.1,2.1l-2.1,2.1l1.04,1.04ZM8.06,17.5q-0.76,0-1.28-0.52T6.25,15.69V4.31q0-0.76 0.52-1.28T8.06,2.5H19.44q0.76,0 1.28,0.52t0.52,1.28V15.69q0,0.76-0.52,1.28T19.44,17.5H8.06Zm0-1.5H19.44q0.12,0 0.21-0.1t0.1-0.21V4.31q0-0.12-0.1-0.21T19.44,4H8.06Q7.94,4 7.85,4.1T7.75,4.31V15.69q0,0.12 0.1,0.21T8.06,16Zm-3.5,5Q3.8,21 3.28,20.47T2.75,19.19V6.31h1.5V19.19q0,0.12 0.1,0.21t0.21,0.1H17.44V21H4.56ZM7.75,4q0,0 0,0.09t0,0.22V15.69q0,0.13 0,0.22t0,0.09q0,0 0-0.09t0-0.22V4.31q0-0.13 0-0.22T7.75,4Z"/></svg>';
    const iconRemoveFromPlaylist = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M11.65,13.14l2.1-2.1l2.1,2.1l1.04-1.04L14.79,10l2.1-2.1L15.85,6.86l-2.1,2.1l-2.1-2.1L10.61,7.9l2.1,2.1l-2.1,2.1l1.04,1.04ZM8.06,17.5q-0.76,0-1.28-0.52T6.25,15.69V4.31q0-0.76 0.52-1.28T8.06,2.5H19.44q0.76,0 1.28,0.52t0.52,1.28V15.69q0,0.76-0.52,1.28T19.44,17.5H8.06Z"/></svg>';

    const n = selectedSongIds.size;
    const plural = n > 1 ? ` (${n} canciones)` : '';
    let items = '';
    items += `<div class="ctx-item" onclick="addSelectionToPlaylist()">${iconAddToPlaylist}<span>Agregar a playlist...${plural}</span></div>`;
    if (currentPlaylist) {
        items += `<div class="ctx-item" onclick="removeSelectedFromPlaylist()">${iconRemoveFromPlaylist}<span>Quitar de "${escapeHtml(currentPlaylist.name)}"${plural}</span></div>`;
    }
    items += `<div class="ctx-separator"></div>`;
    if (n === 1) {
        if (song.isFavorite) {
            items += `<div class="ctx-item" onclick="toggleFavorite(${songId})">${iconRemoveFav}<span>Quitar de favoritos</span></div>`;
        } else {
            items += `<div class="ctx-item" onclick="toggleFavorite(${songId})">${iconAddFav}<span>Agregar a favoritos</span></div>`;
        }
        items += `<div class="ctx-separator"></div>`;
        items += `<div class="ctx-item" onclick="editSong(${songId})">${iconEditSong}<span>Editar información</span></div>`;
        items += `<div class="ctx-separator"></div>`;
    }
    // Texto dinámico según contexto:
    // - En playlist: "Quitar de la playlist" (NO borra de biblioteca)
    // - En "Todas las canciones": "Eliminar canción" (borra de biblioteca)
    const deleteLabel = currentPlaylist
        ? `Quitar de la playlist${plural}`
        : `Eliminar${plural} canción${n>1?'es':''} de la biblioteca`;
    const isDanger = !currentPlaylist;
    items += `<div class="ctx-item${isDanger ? ' danger' : ''}" onclick="deleteSelection()">${iconDeleteSong}<span>${deleteLabel}</span></div>`;
    menu.innerHTML = items;
    menu.style.left = e.clientX + 'px';
    menu.style.top = e.clientY + 'px';
    menu.classList.remove('hidden');
}

async function showAddToPlaylist(songId) {
    hideContextMenu();
    const playlists = await WaveAPI.call('playlists.getAll') || [];
    const playableLists = playlists.filter(p => p.name !== 'Favoritos');

    let bodyHtml = '<div class="playlist-picker">';
    bodyHtml += `<button class="modal-btn modal-btn-cancel playlist-picker-create" onclick="window._modalSelect='create';closeModal();">
        <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" style="margin-right:8px;vertical-align:middle"><path d="M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z"/></svg>
        Crear nueva playlist
    </button>`;
    if (playableLists.length > 0) {
        playableLists.forEach((p, i) => {
            bodyHtml += `<button class="modal-btn modal-btn-cancel" onclick="window._modalSelect=${i};closeModal();">${escapeHtml(p.name)}</button>`;
        });
    }
    bodyHtml += '</div>';

    showModal('Agregar a playlist', bodyHtml, `<button class="modal-btn modal-btn-cancel" onclick="window._modalSelect=-1;closeModal();">Cancelar</button>`);
    window._modalSelect = -1;
    const check = setInterval(async () => {
        if (document.getElementById('modal-overlay').classList.contains('hidden')) {
            clearInterval(check);
            const sel = window._modalSelect;
            if (sel === 'create') {
                const name = await modalPrompt('Nombre de la nueva playlist', '');
                if (name && name.trim()) {
                    await WaveAPI.call('playlists.create', { name: name.trim(), songIds: [songId] });
                    await loadPlaylists();
                    await modalAlert('Playlist creada', `"${name}" creada con la canción.`);
                }
            } else if (typeof sel === 'number' && sel >= 0 && sel < playableLists.length) {
                await WaveAPI.call('playlists.addSongs', { playlistId: playableLists[sel].id, songIds: [songId] });
                await loadPlaylists();
            }
        }
    }, 50);
}

async function deleteSong(songId) {
    hideContextMenu();
    if (currentPlaylist) {
        // En playlist: quitar SOLO de la playlist
        if (await modalConfirm('Quitar de playlist', `¿Quitar esta canción de "${currentPlaylist.name}"?\n\nNo se borrará de la biblioteca.`)) {
            await WaveAPI.call('playlists.removeSong', { playlistId: currentPlaylist.id, songId });
            await selectPlaylist(currentPlaylist);
        }
    } else {
        // En "Todas las canciones": borrar de la biblioteca
        if (await modalConfirm('Eliminar canción', '¿Eliminar esta canción de la biblioteca?\n\nSe borrará de TODAS las playlists.')) {
            await WaveAPI.call('library.deleteSong', { songId });
            await loadSongs();
        }
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

async function removeSelectedFromPlaylist() {
    hideContextMenu();
    if (!currentPlaylist) return;
    const ids = Array.from(selectedSongIds);
    if (ids.length === 0) return;
    for (const id of ids) {
        await WaveAPI.call('playlists.removeSong', { playlistId: currentPlaylist.id, songId: id });
    }
    clearSelection();
    await selectPlaylist(currentPlaylist);
}

// === Import ===
async function onAddMusic() {
    // Modal con 2 opciones: Canciones o Carpeta
    const iconFolder = '<svg viewBox="0 0 24 24" fill="currentColor" width="24" height="24"><path d="M4.25,19.5q-0.73,0-1.24-0.51T2.5,17.75V6.31q0-0.73 0.54-1.27T4.31,4.5H9.8l2,2h7.89q0.67,0 1.16,0.43T21.41,8H11.18l-2-2H4.31Q4.17,6 4.09,6.09T4,6.31V17.69q0,0.11 0.05,0.17t0.14,0.12l2.33-7.79H23.18l-2.42,8.07q-0.17,0.56-0.64,0.9T19.08,19.5H4.25ZM5.76,18H19.27l1.89-6.31H7.65L5.76,18Zm0,0l1.89-6.31L5.76,18ZM4,8V6.31Q4,6.17 4,6.09T4,6V8Z"/></svg>';
    const iconSong = '<svg viewBox="0 0 24 24" fill="currentColor" width="24" height="24"><path d="M10.75,18.85q0.88,0 1.49-0.61t0.61-1.49V12.88h2.87V11.12H11.96V15.1q-0.26-0.23-0.56-0.34t-0.65-0.11q-0.88,0-1.49,0.61T8.65,16.75t0.61,1.49t1.49,0.61ZM6.31,21.5q-0.76,0-1.28-0.52T4.5,19.69V4.31q0-0.76 0.52-1.28T6.31,2.5h7.94L19.5,7.75V19.69q0,0.76-0.52,1.28T17.69,21.5H6.31Zm7.19-13V4H6.31Q6.19,4 6.1,4.1T6,4.31V19.69q0,0.12 0.1,0.21T6.31,20H17.69q0.12,0 0.21-0.1T18,19.69V8.5H13.5ZM6,4V8.5V4V8.5V19.69q0,0.12 0,0.21T6,20q0,0 0-0.1t0-0.21V4.31Q6,4.19 6,4.1T6,4Z"/></svg>';

    let bodyHtml = '<div class="add-music-options">';
    bodyHtml += `<button class="add-music-option" onclick="window._addChoice='folder';closeModal();">${iconFolder}<div class="add-music-label"><div class="add-music-title">Carpeta</div><div class="add-music-desc">Importar todos los archivos de audio de una carpeta</div></div></button>`;
    bodyHtml += `<button class="add-music-option" onclick="window._addChoice='files';closeModal();">${iconSong}<div class="add-music-label"><div class="add-music-title">Canciones</div><div class="add-music-desc">Seleccionar archivos individuales de audio</div></div></button>`;
    bodyHtml += '</div>';

    showModal('Agregar música', bodyHtml, `<button class="modal-btn modal-btn-cancel" onclick="window._addChoice='cancel';closeModal();">Cerrar</button>`);
    window._addChoice = null;
    const check = setInterval(async () => {
        if (document.getElementById('modal-overlay').classList.contains('hidden')) {
            clearInterval(check);
            const choice = window._addChoice;
            if (choice === 'cancel' || !choice) return;
            let result;
            if (choice === 'folder') {
                result = await WaveAPI.call('library.importFromFolder', '');
            } else if (choice === 'files') {
                result = await WaveAPI.call('library.importFiles', {});
            }
            if (result && !result.cancelled) {
                await modalAlert('Importación', `${result.added} canciones nuevas, ${result.duplicates} duplicadas`);
                await loadSongs(); await loadPlaylists();
            }
        }
    }, 50);
}

async function onBackup() {
    await WaveAPI.call('backup.export', { path: '' });
}

async function onSettings() {
    // Modal de Configuración con secciones
    const iconLogs = '<svg viewBox="0 0 24 24" fill="currentColor" width="20" height="20"><path d="M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-7 14h-2v-2h2v2zm0-4h-2V7h2v6z"/></svg>';
    const iconBackup = '<svg viewBox="0 0 24 24" fill="currentColor" width="20" height="20"><path d="M19 12v7H5v-7H3v7c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2v-7h-2zm-6 .67l2.59-2.58L17 11.5l-5 5-5-5 1.41-1.41L11 12.67V3h2v9.67z"/></svg>';
    const iconTrash = '<svg viewBox="0 0 24 24" fill="currentColor" width="20" height="20"><path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z"/></svg>';
    const iconInfo = '<svg viewBox="0 0 24 24" fill="currentColor" width="20" height="20"><path d="M11 7h2v2h-2zm0 4h2v6h-2zm1-9C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z"/></svg>';

    let bodyHtml = '<div class="settings-list">';
    bodyHtml += `<div class="settings-section-title">Datos</div>`;
    bodyHtml += `<button class="settings-item" onclick="window._settingsChoice='backup';closeModal();">${iconBackup}<span>Exportar biblioteca (JSON)</span></button>`;
    bodyHtml += `<button class="settings-item" onclick="window._settingsChoice='logs';closeModal();">${iconLogs}<span>Exportar logs (wave.log)</span></button>`;
    bodyHtml += `<button class="settings-item danger" onclick="window._settingsChoice='clear';closeModal();">${iconTrash}<span>Borrar toda la biblioteca</span></button>`;
    bodyHtml += `<div class="settings-section-title">Acerca de</div>`;
    bodyHtml += `<button class="settings-item" onclick="window._settingsChoice='about';closeModal();">${iconInfo}<span>Acerca de Wave v1.7.11</span></button>`;
    bodyHtml += '</div>';

    showModal('Configuración', bodyHtml, `<button class="modal-btn modal-btn-cancel" onclick="window._settingsChoice='cancel';closeModal();">Cerrar</button>`);
    window._settingsChoice = null;
    const check = setInterval(async () => {
        if (document.getElementById('modal-overlay').classList.contains('hidden')) {
            clearInterval(check);
            const choice = window._settingsChoice;
            if (choice === 'cancel' || !choice) return;
            if (choice === 'logs') {
                await WaveAPI.call('window.exportLogs', {});
            } else if (choice === 'backup') {
                await onBackup();
            } else if (choice === 'about') {
                await modalAlert('Acerca de Wave', 'Wave for Windows\nVersión 1.7.11\n\nReproductor de música offline con soporte para MP3, FLAC, AAC, M4A, OGG, WAV y WMA.\n\nHecho con .NET 8 + WebView2 + NAudio.');
            } else if (choice === 'clear') {
                if (await modalConfirm('Borrar biblioteca', '¿Borrar TODAS las canciones de la biblioteca?\n\nEsta acción no se puede deshacer.')) {
                    await WaveAPI.call('library.deleteAll', {});
                    await loadSongs(); await loadPlaylists();
                    await modalAlert('Biblioteca borrada', 'Todas las canciones han sido eliminadas.');
                }
            }
        }
    }, 50);
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
