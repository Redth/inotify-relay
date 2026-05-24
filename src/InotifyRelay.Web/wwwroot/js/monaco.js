// Lightweight Monaco loader + Blazor interop bridge.
//
// Loads Monaco lazily from the jsDelivr CDN. The CDN script is ~1.2MB total
// (loader + json language worker); for an admin tool with internet access this
// is fine. If you want to ship offline, vendor it under wwwroot/lib/monaco/ and
// flip MONACO_BASE below.
const MONACO_VERSION = "0.52.2";
const MONACO_BASE = `https://cdn.jsdelivr.net/npm/monaco-editor@${MONACO_VERSION}/min/vs`;

let _loader = null;
function loadMonaco() {
    if (_loader) return _loader;
    _loader = new Promise((resolve, reject) => {
        // Monaco workers need a stable origin; use the proxy pattern so the CDN
        // worker scripts load cross-origin.
        window.MonacoEnvironment = {
            getWorkerUrl: function (_moduleId, label) {
                const code = [
                    "self.MonacoEnvironment = { baseUrl: '" + MONACO_BASE + "/' };",
                    "importScripts('" + MONACO_BASE + "/base/worker/workerMain.js');"
                ].join("\n");
                const blob = new Blob([code], { type: "text/javascript" });
                return URL.createObjectURL(blob);
            }
        };
        const s = document.createElement("script");
        s.src = `${MONACO_BASE}/loader.js`;
        s.onload = () => {
            require.config({ paths: { vs: MONACO_BASE } });
            require(["vs/editor/editor.main"], () => resolve(window.monaco));
        };
        s.onerror = reject;
        document.head.appendChild(s);
    });
    return _loader;
}

const _editors = new Map();

export async function init(elementId, value, language, dotnetRef) {
    const monaco = await loadMonaco();
    const host = document.getElementById(elementId);
    if (!host) return;

    if (_editors.has(elementId)) {
        const old = _editors.get(elementId);
        old.dispose();
        _editors.delete(elementId);
    }

    const editor = monaco.editor.create(host, {
        value: value ?? "",
        language: language,
        theme: "vs-dark",
        automaticLayout: true,
        minimap: { enabled: false },
        scrollBeyondLastLine: false,
        fontSize: 13,
        tabSize: 2,
        renderWhitespace: "selection",
        wordWrap: "on"
    });

    // Configure JSON diagnostics so users see lint markers immediately.
    if (language === "json" && monaco.languages.json && monaco.languages.json.jsonDefaults) {
        monaco.languages.json.jsonDefaults.setDiagnosticsOptions({
            validate: true,
            allowComments: false,
            schemas: [],
            enableSchemaRequest: false
        });
    }

    let timer = null;
    editor.onDidChangeModelContent(() => {
        if (timer) clearTimeout(timer);
        timer = setTimeout(() => dotnetRef.invokeMethodAsync("OnChange", editor.getValue()), 120);
    });

    _editors.set(elementId, editor);
}

export function setValue(elementId, value) {
    const e = _editors.get(elementId);
    if (e && e.getValue() !== value) e.setValue(value ?? "");
}

export function getValue(elementId) {
    const e = _editors.get(elementId);
    return e ? e.getValue() : "";
}

export function dispose(elementId) {
    const e = _editors.get(elementId);
    if (e) { e.dispose(); _editors.delete(elementId); }
}
