// Wave API Bridge - comunica JS con C# via WebView2 postMessage
const NO_TIMEOUT_METHODS = new Set([
    'library.importFromFolder',
    'library.importFromFolderToPlaylist',
    'library.importFolderWithSubfolderPlaylists',
    'library.importFiles',
    'library.importBackup',
    'library.renameSongsByMetadata',
    'library.deleteAll',
    'library.selectArtImage',
    'window.selectFolder',
    'window.exportLogs',
    'backup.export'
]);

const WaveAPI = {
    _id: 0,
    _pending: {},

    init() {
        window.chrome.webview.addEventListener('message', (e) => {
            const data = JSON.parse(e.data);
            if (data.event) {
                this._onEvent(data.event, data.state);
            } else if (data.id && this._pending[data.id]) {
                const p = this._pending[data.id];
                delete this._pending[data.id];
                if (data.error) p.reject(new Error(data.error));
                else p.resolve(data.result);
            }
        });
    },

    call(method, params = {}, timeoutMs) {
        const effectiveTimeout = timeoutMs !== undefined
            ? timeoutMs
            : (NO_TIMEOUT_METHODS.has(method) ? 0 : 10000);
        return new Promise((resolve, reject) => {
            const id = ++this._id;
            this._pending[id] = { resolve, reject };
            const msg = JSON.stringify({ id, method, params });
            window.chrome.webview.postMessage(msg);
            if (effectiveTimeout > 0) {
                setTimeout(() => {
                    if (this._pending[id]) {
                        this._pending[id].reject(new Error('Timeout: ' + method));
                        delete this._pending[id];
                    }
                }, effectiveTimeout);
            }
        });
    },

    _onEvent(event, state) {
        if (event === 'playbackChanged' && this.onPlaybackChanged) {
            this.onPlaybackChanged(state);
        } else if (event === 'importProgress' && this.onImportProgress) {
            this.onImportProgress(state);
        }
    },

    onPlaybackChanged: null,
    onImportProgress: null
};
