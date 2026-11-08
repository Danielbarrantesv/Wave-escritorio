// Wave API Bridge - comunica JS con C# via WebView2 postMessage
const WaveAPI = {
    _id: 0,
    _pending: {},

    init() {
        window.chrome.webview.addEventListener('message', (e) => {
            const data = JSON.parse(e.data);
            if (data.event) {
                // Es un evento de C# → JS (ej: playbackChanged)
                this._onEvent(data.event, data.state);
            } else if (data.id && this._pending[data.id]) {
                // Es una respuesta a una llamada RPC
                this._pending[data.id].resolve(data.result);
                delete this._pending[data.id];
            }
        });
    },

    call(method, params = {}) {
        return new Promise((resolve, reject) => {
            const id = ++this._id;
            this._pending[id] = { resolve, reject };
            const msg = JSON.stringify({ id, method, params });
            window.chrome.webview.postMessage(msg);
            // Timeout de 10s
            setTimeout(() => {
                if (this._pending[id]) {
                    this._pending[id].reject(new Error('Timeout: ' + method));
                    delete this._pending[id];
                }
            }, 10000);
        });
    },

    _onEvent(event, state) {
        if (event === 'playbackChanged' && this.onPlaybackChanged) {
            this.onPlaybackChanged(state);
        }
    },

    onPlaybackChanged: null
};
