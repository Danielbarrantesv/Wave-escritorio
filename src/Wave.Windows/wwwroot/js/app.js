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
    // Cargar y aplicar settings guardadas al iniciar
    let settings = loadSettings();
    applySettings(settings);
    bindSettingsAutoSave();
    // Primera vez: mostrar selector de idioma
    if (settings.firstRun) {
        const lang = await showFirstRunLanguagePicker();
        settings.language = lang;
        settings.firstRun = false;
        saveSettings(settings);
        applySettings(settings);
    }
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
            // Obtener solo el texto del <span> interno (ignorando el SVG)
            const span = el.querySelector('span');
            document.getElementById('sort-label').textContent = span ? span.textContent : el.textContent;
            sortMenu.classList.add('hidden');
            applySort();
            renderSongs();
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
    const settingsContent = document.getElementById('settings-content');

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
        settingsContent.style.display = 'none';
    } else if (mode === 'eq') {
        lyricsContent.style.display = 'none';
        eqContent.style.display = '';
        settingsContent.style.display = 'none';
        initEqualizerBands();
    } else if (mode === 'settings') {
        lyricsContent.style.display = 'none';
        eqContent.style.display = 'none';
        settingsContent.style.display = '';
    }
    panel.dataset.mode = mode;
    panel.classList.remove('closed');
    panel.classList.add('open');
    // Actualizar fondo con la portada actual (solo para lyrics)
    if (mode === 'lyrics') updateSidePanelBackground();
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

    // Iconos para cada campo del formulario
    const iconTitle = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M8.78,17.46v-9H5.79V7.19h7.32V8.47H10.13v9H8.78Zm8.47,0.24q-0.99,0-1.6-0.62t-0.61-1.63v-4H13.69V10.29h1.35V8.12h1.28v2.18h1.85v1.16H16.33v3.76q0,0.58 0.31,0.93t0.78,0.35q0.19,0 0.39-0.06t0.4-0.17v1.27q-0.22,0.1-0.46,0.13t-0.5,0.04Z"/></svg>';
    const iconArtist = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M18.56,9.92h3.19v1.77h-2v5.37q0,0.94-0.65,1.6t-1.6,0.65t-1.6-0.65t-0.65-1.6t0.65-1.6t1.6-0.65q0.26,0 0.52,0.07t0.54,0.23V9.92ZM3.25,19.31V17.08q0-0.75 0.4-1.37t1.07-0.95q1.5-0.74 3-1.1t3.03-0.36q0.95,0 1.86,0.13t1.8,0.41q-0.34,0.25-0.61,0.57t-0.47,0.67q-0.61-0.14-1.27-0.21t-1.31-0.07q-1.38,0-2.71,0.33T5.4,16.1q-0.3,0.15-0.48,0.42T4.75,17.08v0.72h8.19q0.05,0.38 0.18,0.76t0.36,0.74H3.25Zm5.03-8.64Q7.25,9.64 7.25,8.19T8.28,5.72t2.47-1.03t2.47,1.03t1.03,2.47t-1.03,2.47t-2.47,1.03T8.28,10.67ZM12.16,9.6q0.59-0.59 0.59-1.41T12.16,6.78T10.75,6.19T9.34,6.78T8.75,8.19T9.34,9.6t1.41,0.59T12.16,9.6ZM10.75,8.19Zm0,9.62Z"/></svg>';
    const iconAlbum = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm0-12.5c-2.49 0-4.5 2.01-4.5 4.5s2.01 4.5 4.5 4.5 4.5-2.01 4.5-4.5-2.01-4.5-4.5-4.5zm0 5.5c-.55 0-1-.45-1-1s.45-1 1-1 1 .45 1 1-.45 1-1 1z"/></svg>';
    const iconGenre = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 3v10.55c-.59-.34-1.27-.55-2-.55-2.21 0-4 1.79-4 4s1.79 4 4 4 4-1.79 4-4V7h4V3h-6z"/></svg>';

    // Generar IDs únicos para los inputs
    const tId = `edit-title-${Date.now()}`;
    const aId = `edit-artist-${Date.now()}`;
    const alId = `edit-album-${Date.now()}`;
    const gId = `edit-genre-${Date.now()}`;

    let bodyHtml = '<div class="edit-form">';
    bodyHtml += `<div class="edit-field">${iconTitle}<input type="text" id="${tId}" class="modal-input edit-input" placeholder="Título" value="${escapeHtml(song.displayTitle || song.title || '')}" /></div>`;
    bodyHtml += `<div class="edit-field">${iconArtist}<input type="text" id="${aId}" class="modal-input edit-input" placeholder="Artista" value="${escapeHtml(song.displayArtist || song.artist || '')}" /></div>`;
    bodyHtml += `<div class="edit-field">${iconAlbum}<input type="text" id="${alId}" class="modal-input edit-input" placeholder="Álbum" value="${escapeHtml(song.displayAlbum || song.album || '')}" /></div>`;
    bodyHtml += `<div class="edit-field">${iconGenre}<input type="text" id="${gId}" class="modal-input edit-input" placeholder="Género" value="${escapeHtml(song.genre || '')}" /></div>`;
    bodyHtml += '</div>';

    showModal('Editar información', bodyHtml,
        `<button class="modal-btn modal-btn-cancel" onclick="window._editChoice='cancel';closeModal();">Cancelar</button>
         <button class="modal-btn modal-btn-primary" onclick="window._editChoice='save';closeModal();">Guardar</button>`);

    window._editChoice = null;
    const check = setInterval(async () => {
        if (document.getElementById('modal-overlay').classList.contains('hidden')) {
            clearInterval(check);
            if (window._editChoice !== 'save') return;
            const title = document.getElementById(tId)?.value || '';
            const artist = document.getElementById(aId)?.value || '';
            const album = document.getElementById(alId)?.value || '';
            const genre = document.getElementById(gId)?.value || '';
            await WaveAPI.call('library.editSong', { songId, title, artist, album, genre });
            // Recargar la vista actual (no siempre es "todas las canciones")
            if (currentPlaylist) {
                await selectPlaylist(currentPlaylist);
            } else {
                await loadSongs();
            }
        }
    }, 50);
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
    openSidePanel('settings');
    // Cargar valores guardados en los inputs
    loadSettingsToForm();
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

// === Settings ===
// Persistencia en localStorage. Las claves son set_<name>.
const SETTINGS_KEY = 'wave_settings_v1';

function defaultSettings() {
    return {
        volumeInitial: 100,
        repeatAll: false,
        shuffleDefault: false,
        skipZero: true,
        defaultFolder: '',
        importOnOpen: false,
        confirmDelete: true,
        rescanTags: false,
        theme: 'dark',
        accent: 'green',
        language: 'es',
        animations: true,
        showSliders: true,
        firstRun: true
    };
}

function loadSettings() {
    try {
        const raw = localStorage.getItem(SETTINGS_KEY);
        if (!raw) return defaultSettings();
        const parsed = JSON.parse(raw);
        return Object.assign(defaultSettings(), parsed);
    } catch { return defaultSettings(); }
}

function saveSettings(settings) {
    try { localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings)); } catch {}
}

function loadSettingsToForm() {
    const s = loadSettings();
    document.getElementById('set-volume-initial').value = s.volumeInitial;
    document.getElementById('set-repeat-all').checked = s.repeatAll;
    document.getElementById('set-shuffle-default').checked = s.shuffleDefault;
    document.getElementById('set-skip-zero').checked = s.skipZero;
    document.getElementById('set-default-folder').value = s.defaultFolder || '';
    document.getElementById('set-import-on-open').checked = s.importOnOpen;
    document.getElementById('set-confirm-delete').checked = s.confirmDelete;
    document.getElementById('set-rescan-tags').checked = s.rescanTags;
    document.getElementById('set-theme').value = s.theme;
    document.getElementById('set-language').value = s.language;
    document.getElementById('set-animations').checked = s.animations;
    document.getElementById('set-show-sliders').checked = s.showSliders;
    // Marcar el color de accent seleccionado en el color picker
    document.querySelectorAll('.color-dot').forEach(dot => {
        dot.classList.toggle('selected', dot.dataset.color === s.accent);
    });
}

function getSettingsFromForm() {
    const accent = document.querySelector('.color-dot.selected')?.dataset.color || 'green';
    return {
        volumeInitial: parseInt(document.getElementById('set-volume-initial').value),
        repeatAll: document.getElementById('set-repeat-all').checked,
        shuffleDefault: document.getElementById('set-shuffle-default').checked,
        skipZero: document.getElementById('set-skip-zero').checked,
        defaultFolder: document.getElementById('set-default-folder').value,
        importOnOpen: document.getElementById('set-import-on-open').checked,
        confirmDelete: document.getElementById('set-confirm-delete').checked,
        rescanTags: document.getElementById('set-rescan-tags').checked,
        theme: document.getElementById('set-theme').value,
        accent: accent,
        language: document.getElementById('set-language').value,
        animations: document.getElementById('set-animations').checked,
        showSliders: document.getElementById('set-show-sliders').checked,
        firstRun: false
    };
}

// Auto-guardar cuando cambia cualquier input del panel de settings
function bindSettingsAutoSave() {
    const inputs = document.querySelectorAll('#settings-content input, #settings-content select');
    inputs.forEach(input => {
        input.addEventListener('change', () => {
            const settings = getSettingsFromForm();
            saveSettings(settings);
            applySettings(settings);
            // Feedback visual sutil
            const panel = document.getElementById('settings-content');
            panel.style.background = 'rgba(50, 201, 139, 0.04)';
            setTimeout(() => { panel.style.background = ''; }, 200);
        });
    });
    // Click en los colores de accent
    document.querySelectorAll('.color-dot').forEach(dot => {
        dot.addEventListener('click', () => {
            document.querySelectorAll('.color-dot').forEach(d => d.classList.remove('selected'));
            dot.classList.add('selected');
            const settings = getSettingsFromForm();
            saveSettings(settings);
            applySettings(settings);
        });
    });
    // Añadir iconos a cada settings-row (para reconocimiento visual incluso
    // si el usuario cambió el idioma por error)
    addSettingsIcons();
}

// Iconos SVG para cada setting — identificación visual rápida
function addSettingsIcons() {
    const icons = {
        'set-volume-initial': '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z"/></svg>',
        'set-repeat-all': '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M7 7h10v3l4-4-4-4v3H5v6h2V7zm10 10H7v-3l-4 4 4 4v-3h12v-6h-2v4z"/></svg>',
        'set-shuffle-default': '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M10.59 9.17L5.41 4 4 5.41l5.17 5.17 1.42-1.41zM14.5 4l2.04 2.04L4 18.59 5.41 20 17.96 7.46 20 9.5V4h-5.5zm.33 9.41l-1.41 1.41 3.13 3.13L14.5 20H20v-5.5l-2.04 2.04-3.13-3.13z"/></svg>',
        'set-skip-zero': '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M6 18l8.5-6L6 6v12zM16 6v12h2V6h-2z"/></svg>',
        'set-default-folder': '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"/></svg>',
        'set-import-on-open': '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z"/></svg>',
        'set-confirm-delete': '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z"/></svg>',
        'set-rescan-tags': '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M17.65 6.35C16.2 4.9 14.21 4 12 4c-4.42 0-7.99 3.58-7.99 8s3.57 8 7.99 8c3.73 0 6.84-2.55 7.73-6h-2.08c-.82 2.33-3.04 4-5.65 4-3.31 0-6-2.69-6-6s2.69-6 6-6c1.66 0 3.14.69 4.22 1.78L13 11h7V4l-2.35 2.35z"/></svg>',
        'set-theme': '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 3c-4.97 0-9 4.03-9 9s4.03 9 9 9c.83 0 1.5-.67 1.5-1.5 0-.39-.15-.74-.39-1.01-.23-.26-.38-.61-.38-.99 0-.83.67-1.5 1.5-1.5H16c2.76 0 5-2.24 5-5 0-4.42-4.03-8-9-8zm-5.5 9c-.83 0-1.5-.67-1.5-1.5S5.67 9 6.5 9 8 9.67 8 10.5 7.33 12 6.5 12zm3-4C8.67 8 8 7.33 8 6.5S8.67 5 9.5 5s1.5.67 1.5 1.5S10.33 8 9.5 8zm5 0c-.83 0-1.5-.67-1.5-1.5S13.67 5 14.5 5s1.5.67 1.5 1.5S15.33 8 14.5 8zm3 4c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5z"/></svg>',
        'set-accent-picker': '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 22C6.49 22 2 17.51 2 12S6.04 2 11.55 2c2.86 0 5.49 1.09 7.42 3.07l1.27-1.27c.39-.39 1.02-.39 1.41 0l-3.6 3.6c-.39.39-1.02.39-1.41 0l1.27-1.27C16.34 4.16 13.92 3 11.55 3 6.66 3 3 6.66 3 11.55s4.04 8.55 8.55 8.55c4.91 0 8.55-4.04 8.55-8.55h1c0 5.51-4.49 9.45-10 9.45z"/></svg>',
        'set-language': '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zm6.93 6h-2.95c-.32-1.25-.78-2.45-1.38-3.56 1.84.63 3.37 1.91 4.33 3.56zM12 4.04c.83 1.2 1.48 2.53 1.91 3.96h-3.82c.43-1.43 1.08-2.76 1.91-3.96zM4.26 14C4.1 13.36 4 12.69 4 12s.1-1.36.26-2h3.38c-.08.66-.14 1.32-.14 2 0 .68.06 1.34.14 2H4.26zm.82 2h2.95c.32 1.25.78 2.45 1.38 3.56-1.84-.63-3.37-1.9-4.33-3.56zm2.95-8H5.08c.96-1.66 2.49-2.93 4.33-3.56C8.81 5.55 8.35 6.75 8.03 8zM12 19.96c-.83-1.2-1.48-2.53-1.91-3.96h3.82c-.43 1.43-1.08 2.76-1.91 3.96zM14.34 14H9.66c-.09-.66-.16-1.32-.16-2 0-.68.07-1.35.16-2h4.68c.09.65.16 1.32.16 2 0 .68-.07 1.34-.16 2zm.25 5.56c.6-1.11 1.06-2.31 1.38-3.56h2.95c-.96 1.65-2.49 2.93-4.33 3.56zM16.36 14c.08-.66.14-1.32.14-2 0-.68-.06-1.34-.14-2h3.38c.16.64.26 1.31.26 2s-.1 1.36-.26 2h-3.38z"/></svg>',
        'set-animations': '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M13 2L3 14h7v8l10-12h-7V2z"/></svg>',
        'set-show-sliders': '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M4 14h4v-4H4v4zm0 5h4v-3H4v3zM4 9h4V6H4v3zm5 5h12v-4H9v4zm0 5h12v-3H9v3zM9 6v3h12V6H9z"/></svg>'
    };

    document.querySelectorAll('#settings-content .settings-row').forEach(row => {
        // Buscar el input/select dentro de la fila
        const input = row.querySelector('input, select, .color-picker');
        if (!input || !input.id) return;
        const iconSvg = icons[input.id];
        if (!iconSvg) return;
        // Si ya tiene icono, no duplicar
        if (row.querySelector('.row-icon')) return;
        // Crear el wrapper row-label con icono + label existente
        const label = row.querySelector('label');
        if (!label) return;
        const wrapper = document.createElement('div');
        wrapper.className = 'row-label';
        // Icono
        const iconSpan = document.createElement('span');
        iconSpan.className = 'row-icon';
        iconSpan.innerHTML = iconSvg;
        // Mover el label existente dentro del wrapper
        row.insertBefore(wrapper, label);
        wrapper.appendChild(iconSpan);
        wrapper.appendChild(label);
    });
}

// Aplicar settings al estado actual de la app
function applySettings(settings) {
    // Aplicar tema y accent al <html>
    document.documentElement.setAttribute('data-theme', settings.theme || 'dark');
    document.documentElement.setAttribute('data-accent', settings.accent || 'green');
    // Toggle animaciones
    document.body.classList.toggle('no-animations', !settings.animations);
    // Toggle sliders de recientes y artistas
    const recent = document.getElementById('recent-section');
    const artists = document.getElementById('artists-section');
    if (recent) recent.style.display = settings.showSliders ? '' : 'none';
    if (artists) artists.style.display = settings.showSliders ? '' : 'none';
    // Aplicar idioma
    applyLanguage(settings.language || 'es');
    // Notificar al backend (C#) para que actualice el mini player y ventana principal
    try {
        WaveAPI.call('window.applyTheme', { theme: settings.theme || 'dark', accent: settings.accent || 'green' });
    } catch (e) { /* ignore si el bridge no está listo */ }
}

// Acciones de los botones de settings
async function settingsPickFolder() {
    const res = await WaveAPI.call('window.selectFolder', {});
    if (res && !res.cancelled && res.path) {
        document.getElementById('set-default-folder').value = res.path;
        // Trigger auto-save
        document.getElementById('set-default-folder').dispatchEvent(new Event('change'));
    }
}

async function settingsBackup() {
    await WaveAPI.call('backup.export', { path: '' });
}

async function settingsRestore() {
    if (await modalConfirm('Importar biblioteca', '¿Importar biblioteca desde un archivo JSON?\n\nEsto reemplazará la biblioteca actual.')) {
        await WaveAPI.call('library.importBackup', {});
        await loadSongs(); await loadPlaylists();
    }
}

async function settingsExportLogs() {
    await WaveAPI.call('window.exportLogs', {});
}

async function settingsClearLibrary() {
    if (await modalConfirm('Borrar biblioteca', '¿Borrar TODAS las canciones de la biblioteca?\n\nEsta acción no se puede deshacer.')) {
        await WaveAPI.call('library.deleteAll', {});
        await loadSongs(); await loadPlaylists();
        await modalAlert('Biblioteca borrada', 'Todas las canciones han sido eliminadas.');
    }
}

async function settingsReportBug() {
    await modalAlert('Reportar bug', 'Para reportar un bug:\n\n1. Exporta los logs desde Configuración → Exportar logs (wave.log)\n2. Ve a: https://github.com/Danielbarrantesv/Wave-escritorio/issues\n3. Crea un nuevo issue con:\n   - Descripción del bug\n   - Pasos para reproducirlo\n   - Archivo wave.log adjunto');
}

async function settingsShowChangelog() {
    await modalAlert('Changelog', 'v1.9.0 — Tema claro + 8 colores de accent + 6 idiomas + i18n + fix toggle switch\nv1.8.1 — Sección Configuración completa como panel lateral\nv1.7.11 — Mini player slider con bolita visible\nv1.7.9 — Bolita mini player al frente\nv1.7.7 — Icono close del mini player arreglado\nv1.7.5 — Limpiar search al cambiar playlist + slider mini player + corazón playerbar + scrollbar pintada + fix marquee\nv1.7.4 — Bloquear context menu nativo + selection sticky + mini player toggle + icono play/pausa estable + barras con relleno\nv1.7.1 — Botón crear playlist visible + slider artistas + sticky + RECENT_MAX=30\nv1.7.0 — UI overhaul: side panel fixed, marquee, recientes, favoritos sync, delete contextual\nv1.6.1 — Side panel lyrics/EQ + UI play/pausa + animaciones\nv1.6.0 — Shuffle respeta playlist + multi-selección + fix diálogo doble\nv1.5.2 — Mini player click cover/info + playlist pierde canciones + más espacio iconos');
}

// === Internationalization (i18n) ===
// Diccionario de traducciones para los textos principales de la UI.
// Las claves son data-i18n attributes en el HTML; al cambiar el idioma se
// buscan todos los [data-i18n] y se actualiza su texto.
const I18N = {
    es: {
        app_title: 'Wave', add_music: '+ Agregar música', search_placeholder: 'Buscar canciones, artistas, álbumes...',
        all_songs: 'Todas las canciones', playlists: 'Playlists', create_playlist: 'Crear playlist',
        recent_label: 'Recientes', recent_played: 'Reproducidas', title: 'Título', artist: 'Artista', duration: 'Duración',
        no_music: 'Aún no hay música', no_music_desc: 'Arrastra archivos aquí o usa "Agregar música"',
        select_folder: 'Seleccionar carpeta', artists_section: 'Artistas',
        recent_section_title: 'Reproducidas recientemente', cancel: 'Cancelar', accept: 'Aceptar', close: 'Cerrar',
        add_to_playlist: 'Agregar a playlist...', remove_from_playlist: 'Quitar de', edit_info: 'Editar información',
        add_to_favorites: 'Agregar a favoritos', remove_from_favorites: 'Quitar de favoritos',
        delete_song: 'Eliminar canción de la biblioteca', delete_from_playlist: 'Quitar de la playlist',
        create_new_playlist: 'Crear nueva playlist', import_folder_to_playlist: 'Importar carpeta a esta playlist',
        edit_name: 'Editar nombre', delete_playlist: 'Eliminar playlist', cannot_delete_favorites: 'No se puede eliminar Favoritos',
        settings: 'Configuración', playback: 'Reproducción', library: 'Biblioteca', interface: 'Interfaz', data: 'Datos', about: 'Acerca de',
        volume_initial: 'Volumen inicial al abrir', repeat_all_default: 'Repetir todo por defecto',
        shuffle_default: 'Shuffle por defecto al abrir', skip_zero: 'Saltar canciones con duración 0',
        default_folder: 'Carpeta de música por defecto', import_on_open: 'Importar al abrir la app',
        confirm_delete: 'Confirmar antes de eliminar canciones', rescan_tags: 'Re-escanear metadatos al iniciar',
        theme: 'Tema', accent_color: 'Color de acento', language: 'Idioma', animations: 'Animaciones',
        show_sliders: 'Mostrar sliders de recientes y artistas',
        export_library: 'Exportar biblioteca (JSON)', import_library: 'Importar biblioteca (JSON)',
        export_logs: 'Exportar logs (wave.log)', clear_library: 'Borrar toda la biblioteca',
        report_bug: 'Reportar bug', view_changelog: 'Ver changelog',
        first_run_title: 'Bienvenido a Wave', first_run_msg: 'Selecciona tu idioma para empezar:',
        no_song: 'Canción', no_artist: 'Artista'
    },
    en: {
        app_title: 'Wave', add_music: '+ Add music', search_placeholder: 'Search songs, artists, albums...',
        all_songs: 'All songs', playlists: 'Playlists', create_playlist: 'Create playlist',
        recent_label: 'Recent', recent_played: 'Recently played', title: 'Title', artist: 'Artist', duration: 'Duration',
        no_music: 'No music yet', no_music_desc: 'Drag files here or use "Add music"',
        select_folder: 'Select folder', artists_section: 'Artists',
        recent_section_title: 'Recently played', cancel: 'Cancel', accept: 'OK', close: 'Close',
        add_to_playlist: 'Add to playlist...', remove_from_playlist: 'Remove from', edit_info: 'Edit info',
        add_to_favorites: 'Add to favorites', remove_from_favorites: 'Remove from favorites',
        delete_song: 'Delete song from library', delete_from_playlist: 'Remove from playlist',
        create_new_playlist: 'Create new playlist', import_folder_to_playlist: 'Import folder to this playlist',
        edit_name: 'Edit name', delete_playlist: 'Delete playlist', cannot_delete_favorites: 'Cannot delete Favorites',
        settings: 'Settings', playback: 'Playback', library: 'Library', interface: 'Interface', data: 'Data', about: 'About',
        volume_initial: 'Initial volume on open', repeat_all_default: 'Repeat all by default',
        shuffle_default: 'Shuffle by default on open', skip_zero: 'Skip songs with duration 0',
        default_folder: 'Default music folder', import_on_open: 'Import on app open',
        confirm_delete: 'Confirm before deleting songs', rescan_tags: 'Re-scan metadata on startup',
        theme: 'Theme', accent_color: 'Accent color', language: 'Language', animations: 'Animations',
        show_sliders: 'Show recent and artists sliders',
        export_library: 'Export library (JSON)', import_library: 'Import library (JSON)',
        export_logs: 'Export logs (wave.log)', clear_library: 'Clear entire library',
        report_bug: 'Report bug', view_changelog: 'View changelog',
        first_run_title: 'Welcome to Wave', first_run_msg: 'Select your language to start:',
        no_song: 'Song', no_artist: 'Artist'
    },
    fr: {
        app_title: 'Wave', add_music: '+ Ajouter musique', search_placeholder: 'Rechercher chansons, artistes, albums...',
        all_songs: 'Toutes les chansons', playlists: 'Playlists', create_playlist: 'Créer playlist',
        recent_label: 'Récents', recent_played: 'Récemment écoutées', title: 'Titre', artist: 'Artiste', duration: 'Durée',
        no_music: 'Pas de musique', no_music_desc: 'Glissez des fichiers ici ou utilisez "Ajouter musique"',
        select_folder: 'Sélectionner dossier', artists_section: 'Artistes',
        recent_section_title: 'Récemment écoutées', cancel: 'Annuler', accept: 'OK', close: 'Fermer',
        add_to_playlist: 'Ajouter à playlist...', remove_from_playlist: 'Retirer de', edit_info: 'Modifier infos',
        add_to_favorites: 'Ajouter aux favoris', remove_from_favorites: 'Retirer des favoris',
        delete_song: 'Supprimer la chanson de la bibliothèque', delete_from_playlist: 'Retirer de la playlist',
        create_new_playlist: 'Créer nouvelle playlist', import_folder_to_playlist: 'Importer dossier dans cette playlist',
        edit_name: 'Modifier nom', delete_playlist: 'Supprimer playlist', cannot_delete_favorites: 'Impossible de supprimer Favoris',
        settings: 'Paramètres', playback: 'Lecture', library: 'Bibliothèque', interface: 'Interface', data: 'Données', about: 'À propos',
        volume_initial: 'Volume initial à l\'ouverture', repeat_all_default: 'Répéter tout par défaut',
        shuffle_default: 'Aléatoire par défaut à l\'ouverture', skip_zero: 'Passer les chansons de durée 0',
        default_folder: 'Dossier de musique par défaut', import_on_open: 'Importer à l\'ouverture',
        confirm_delete: 'Confirmer avant de supprimer', rescan_tags: 'Re-scanner métadonnées au démarrage',
        theme: 'Thème', accent_color: 'Couleur d\'accent', language: 'Langue', animations: 'Animations',
        show_sliders: 'Afficher sliders récents et artistes',
        export_library: 'Exporter bibliothèque (JSON)', import_library: 'Importer bibliothèque (JSON)',
        export_logs: 'Exporter logs (wave.log)', clear_library: 'Vider toute la bibliothèque',
        report_bug: 'Signaler bug', view_changelog: 'Voir changelog',
        first_run_title: 'Bienvenue dans Wave', first_run_msg: 'Sélectionnez votre langue pour commencer:',
        no_song: 'Chanson', no_artist: 'Artiste'
    },
    de: {
        app_title: 'Wave', add_music: '+ Musik hinzufügen', search_placeholder: 'Lieder, Künstler, Alben suchen...',
        all_songs: 'Alle Lieder', playlists: 'Playlists', create_playlist: 'Playlist erstellen',
        recent_label: 'Neueste', recent_played: 'Zuletzt gespielt', title: 'Titel', artist: 'Künstler', duration: 'Dauer',
        no_music: 'Keine Musik', no_music_desc: 'Dateien hierher ziehen oder "Musik hinzufügen" verwenden',
        select_folder: 'Ordner auswählen', artists_section: 'Künstler',
        recent_section_title: 'Zuletzt gespielt', cancel: 'Abbrechen', accept: 'OK', close: 'Schließen',
        add_to_playlist: 'Zur Playlist hinzufügen...', remove_from_playlist: 'Entfernen aus', edit_info: 'Infos bearbeiten',
        add_to_favorites: 'Zu Favoriten hinzufügen', remove_from_favorites: 'Aus Favoriten entfernen',
        delete_song: 'Lied aus Bibliothek löschen', delete_from_playlist: 'Aus Playlist entfernen',
        create_new_playlist: 'Neue Playlist erstellen', import_folder_to_playlist: 'Ordner in diese Playlist importieren',
        edit_name: 'Name bearbeiten', delete_playlist: 'Playlist löschen', cannot_delete_favorites: 'Favoriten können nicht gelöscht werden',
        settings: 'Einstellungen', playback: 'Wiedergabe', library: 'Bibliothek', interface: 'Oberfläche', data: 'Daten', about: 'Über',
        volume_initial: 'Anfangslautstärke beim Öffnen', repeat_all_default: 'Standardmäßig alles wiederholen',
        shuffle_default: 'Zufallswiedergabe beim Öffnen', skip_zero: 'Lieder mit Dauer 0 überspringen',
        default_folder: 'Standard-Musikordner', import_on_open: 'Beim Öffnen importieren',
        confirm_delete: 'Vor dem Löschen bestätigen', rescan_tags: 'Metadaten beim Start neu scannen',
        theme: 'Design', accent_color: 'Akzentfarbe', language: 'Sprache', animations: 'Animationen',
        show_sliders: 'Slider für Neueste und Künstler anzeigen',
        export_library: 'Bibliothek exportieren (JSON)', import_library: 'Bibliothek importieren (JSON)',
        export_logs: 'Logs exportieren (wave.log)', clear_library: 'Gesamte Bibliothek löschen',
        report_bug: 'Fehler melden', view_changelog: 'Changelog anzeigen',
        first_run_title: 'Willkommen bei Wave', first_run_msg: 'Wählen Sie Ihre Sprache zum Starten:',
        no_song: 'Lied', no_artist: 'Künstler'
    },
    ja: {
        app_title: 'Wave', add_music: '+ 音楽を追加', search_placeholder: '曲、アーティスト、アルバムを検索...',
        all_songs: 'すべての曲', playlists: 'プレイリスト', create_playlist: 'プレイリスト作成',
        recent_label: '最近', recent_played: '最近再生', title: 'タイトル', artist: 'アーティスト', duration: '時間',
        no_music: '曲がありません', no_music_desc: 'ファイルをドラッグするか「音楽を追加」を使用',
        select_folder: 'フォルダーを選択', artists_section: 'アーティスト',
        recent_section_title: '最近再生した曲', cancel: 'キャンセル', accept: 'OK', close: '閉じる',
        add_to_playlist: 'プレイリストに追加...', remove_from_playlist: '削除', edit_info: '情報を編集',
        add_to_favorites: 'お気に入りに追加', remove_from_favorites: 'お気に入りから削除',
        delete_song: 'ライブラリから曲を削除', delete_from_playlist: 'プレイリストから削除',
        create_new_playlist: '新しいプレイリストを作成', import_folder_to_playlist: 'このプレイリストにフォルダーをインポート',
        edit_name: '名前を編集', delete_playlist: 'プレイリストを削除', cannot_delete_favorites: 'お気に入りは削除できません',
        settings: '設定', playback: '再生', library: 'ライブラリ', interface: 'インターフェース', data: 'データ', about: '情報',
        volume_initial: '起動時の音量', repeat_all_default: 'デフォルトで全曲リピート',
        shuffle_default: '起動時にシャッフル', skip_zero: '長さ0の曲をスキップ',
        default_folder: 'デフォルトの音楽フォルダー', import_on_open: '起動時にインポート',
        confirm_delete: '削除前に確認', rescan_tags: '起動時にメタデータを再スキャン',
        theme: 'テーマ', accent_color: 'アクセント色', language: '言語', animations: 'アニメーション',
        show_sliders: '最近とアーティストのスライダーを表示',
        export_library: 'ライブラリをエクスポート (JSON)', import_library: 'ライブラリをインポート (JSON)',
        export_logs: 'ログをエクスポート (wave.log)', clear_library: 'ライブラリをすべて削除',
        report_bug: 'バグを報告', view_changelog: '変更履歴を見る',
        first_run_title: 'Waveへようこそ', first_run_msg: '開始する言語を選択してください:',
        no_song: '曲', no_artist: 'アーティスト'
    },
    ko: {
        app_title: 'Wave', add_music: '+ 음악 추가', search_placeholder: '곡, 아티스트, 앨범 검색...',
        all_songs: '모든 곡', playlists: '재생목록', create_playlist: '재생목록 만들기',
        recent_label: '최근', recent_played: '최근 재생', title: '제목', artist: '아티스트', duration: '길이',
        no_music: '음악 없음', no_music_desc: '파일을 여기로 드래그하거나 "음악 추가" 사용',
        select_folder: '폴더 선택', artists_section: '아티스트',
        recent_section_title: '최근 재생한 곡', cancel: '취소', accept: '확인', close: '닫기',
        add_to_playlist: '재생목록에 추가...', remove_from_playlist: '제거', edit_info: '정보 편집',
        add_to_favorites: '즐겨찾기에 추가', remove_from_favorites: '즐겨찾기에서 제거',
        delete_song: '라이브러리에서 곡 삭제', delete_from_playlist: '재생목록에서 제거',
        create_new_playlist: '새 재생목록 만들기', import_folder_to_playlist: '이 재생목록에 폴더 가져오기',
        edit_name: '이름 편집', delete_playlist: '재생목록 삭제', cannot_delete_favorites: '즐겨찾기는 삭제할 수 없습니다',
        settings: '설정', playback: '재생', library: '라이브러리', interface: '인터페이스', data: '데이터', about: '정보',
        volume_initial: '열 때 초기 볼륨', repeat_all_default: '기본적으로 모두 반복',
        shuffle_default: '열 때 셔플', skip_zero: '길이가 0인 곡 건너뛰기',
        default_folder: '기본 음악 폴더', import_on_open: '열 때 가져오기',
        confirm_delete: '삭제 전 확인', rescan_tags: '시작 시 메타데이터 다시 스캔',
        theme: '테마', accent_color: '강조 색상', language: '언어', animations: '애니메이션',
        show_sliders: '최근 및 아티스트 슬라이더 표시',
        export_library: '라이브러리 내보내기 (JSON)', import_library: '라이브러리 가져오기 (JSON)',
        export_logs: '로그 내보내기 (wave.log)', clear_library: '전체 라이브러리 삭제',
        report_bug: '버그 신고', view_changelog: '변경 로그 보기',
        first_run_title: 'Wave에 오신 것을 환영합니다', first_run_msg: '시작하려면 언어를 선택하세요:',
        no_song: '곡', no_artist: '아티스트'
    }
};

function t(key) {
    const lang = (loadSettings().language || 'es');
    return (I18N[lang] && I18N[lang][key]) || I18N.es[key] || key;
}

function applyLanguage(lang) {
    // Actualizar placeholders y textos hardcoded que no tienen data-i18n
    const search = document.getElementById('search');
    if (search) search.placeholder = t('search_placeholder');
    const btnAdd = document.getElementById('btn-add');
    if (btnAdd) btnAdd.textContent = t('add_music');
    const btnAddPlaylist = document.querySelector('.btn-add-playlist span');
    if (btnAddPlaylist) btnAddPlaylist.textContent = t('create_playlist');
    const navAll = document.querySelector('[data-view="all"] span');
    if (navAll) navAll.textContent = t('all_songs');
    const plTitle = document.querySelector('.sidebar-title');
    if (plTitle) plTitle.textContent = t('playlists');
    const recentTitle = document.querySelector('#recent-section .recent-section-title');
    if (recentTitle) recentTitle.textContent = t('recent_section_title');
    const artistsTitle = document.querySelector('#artists-section .recent-section-title');
    if (artistsTitle) artistsTitle.textContent = t('artists_section');
    const playerTitle = document.getElementById('player-title');
    if (playerTitle && playerTitle.textContent === 'Canción') playerTitle.textContent = t('no_song');
    const playerArtist = document.getElementById('player-artist');
    if (playerArtist && playerArtist.textContent === 'Artista') playerArtist.textContent = t('no_artist');
    const emptyH2 = document.querySelector('#empty-state h2');
    if (emptyH2) emptyH2.textContent = t('no_music');
    const emptyP = document.querySelector('#empty-state p');
    if (emptyP) emptyP.textContent = t('no_music_desc');
    // Sort menu labels (usar el span interno de cada opción)
    const sortOptions = document.querySelectorAll('.sort-option span');
    const sortLabels = [t('recent_label'), t('recent_played'), t('title'), t('artist'), t('duration')];
    sortOptions.forEach((span, i) => { if (sortLabels[i]) span.textContent = sortLabels[i]; });
    // Actualizar el label del sort chip actual
    const sortLabel = document.getElementById('sort-label');
    const sortKey = { 'recentlyAdded': 'recent_label', 'recentlyPlayed': 'recent_played', 'title': 'title', 'artist': 'artist', 'duration': 'duration' }[currentSort];
    if (sortLabel && sortKey) sortLabel.textContent = t(sortKey);
}

// Mostrar modal de selección de idioma en la primera ejecución
async function showFirstRunLanguagePicker() {
    const langs = [
        { code: 'es', name: 'Español', flag: '🇪🇸' },
        { code: 'en', name: 'English', flag: '🇬🇧' },
        { code: 'fr', name: 'Français', flag: '🇫🇷' },
        { code: 'de', name: 'Deutsch', flag: '🇩🇪' },
        { code: 'ja', name: '日本語', flag: '🇯🇵' },
        { code: 'ko', name: '한국어', flag: '🇰🇷' }
    ];
    let bodyHtml = '<div class="lang-picker">';
    for (const l of langs) {
        bodyHtml += `<button class="lang-option" onclick="window._langChoice='${l.code}';closeModal();"><span class="lang-flag">${l.flag}</span><span>${l.name}</span></button>`;
    }
    bodyHtml += '</div>';
    showModal(t('first_run_title'), `<p style="margin-bottom:12px;color:var(--text-dim);font-size:14px;">${t('first_run_msg')}</p>${bodyHtml}`,
        `<button class="modal-btn modal-btn-primary" onclick="window._langChoice='es';closeModal();">OK</button>`);
    window._langChoice = null;
    return new Promise(resolve => {
        const check = setInterval(() => {
            if (document.getElementById('modal-overlay').classList.contains('hidden')) {
                clearInterval(check);
                resolve(window._langChoice || 'es');
            }
        }, 50);
    });
}
