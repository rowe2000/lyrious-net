import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

// The browser's password manager (Credential Management API), used by BrowserCredentialStore.cs.
// Only Chromium browsers have PasswordCredential; elsewhere nothing is offered.
globalThis.lyriousCredentials = {
    isSupported: () => typeof globalThis.PasswordCredential === "function" && !!navigator.credentials,
    // The browser shows its own "Save password?" prompt.
    store: async (id, password) => {
        await navigator.credentials.store(new PasswordCredential({ id, password, name: id }));
    },
    // May show the browser's account chooser; resolves to null when nothing is saved or chosen.
    load: async () => {
        const credential = await navigator.credentials.get({ password: true, mediation: "optional" });
        return credential && credential.type === "password"
            ? JSON.stringify({ id: credential.id, password: credential.password })
            : null;
    },
};

// PBKDF2-SHA256 with the browser's Web Crypto (BrowserPasswordHasher.cs): native and asynchronous,
// so the offline login's password hash doesn't freeze the page. Web Crypto needs a secure context
// (https, or http://localhost); elsewhere this throws and the app falls back to a slower hash.
globalThis.lyriousCrypto = {
    pbkdf2: async (password, saltBase64, iterations, length) => {
        if (!globalThis.crypto?.subtle) throw new Error("Web Crypto isn't available here.");
        const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(password), "PBKDF2", false, ["deriveBits"]);
        const salt = Uint8Array.from(atob(saltBase64), c => c.charCodeAt(0));
        const bits = await crypto.subtle.deriveBits({ name: "PBKDF2", hash: "SHA-256", salt, iterations }, key, length * 8);
        return btoa(String.fromCharCode(...new Uint8Array(bits)));
    },
};

// Settings kept between visits (BrowserSettingsStore.cs). Storage can be blocked (e.g. private
// browsing), in which case settings simply aren't kept.
globalThis.lyriousSettings = {
    get: (key) => {
        try { return localStorage.getItem(key); } catch { return null; }
    },
    set: (key, value) => {
        try {
            if (value == null) localStorage.removeItem(key); else localStorage.setItem(key, value);
        } catch { /* not kept */ }
    },
};

// The app's other tabs and windows on this address (BrowserInstanceLink.cs). They share the local
// storage, so they pass each other their changes over a BroadcastChannel to stay the same without
// the server, and the one holding the Web Lock (the owner) sends the changes to the server: the
// browser gives the lock to the next one waiting when the owner's tab is closed. Both are needed
// (Web Locks only on https or localhost); without them the app is alone and its own owner.
globalThis.lyriousInstances = (() => {
    const supported = typeof BroadcastChannel === "function" && !!navigator.locks;
    let channel = null;
    let release = null;
    let owner = !supported;
    let joins = 0;

    function leave() {
        joins++; // A lock still being waited for is given back when it comes.
        channel?.close();
        channel = null;
        release?.();
        release = null;
        owner = !supported;
    }

    return {
        join: (name, received, becameOwner) => {
            leave();
            if (!supported) return;
            const mine = joins;
            channel = new BroadcastChannel("lyrious." + name);
            channel.onmessage = event => {
                if (typeof event.data === "string") received(event.data);
            };
            // Held until leaving (or the tab is closed): the promise is what keeps the lock.
            navigator.locks.request("lyrious." + name, () => new Promise(resolve => {
                if (mine !== joins) {
                    resolve();
                    return;
                }
                release = resolve;
                owner = true;
                becameOwner();
            }));
        },
        leave,
        isOwner: () => owner,
        post: message => channel?.postMessage(message),
    };
})();

// Sound files for songs' media links (BrowserSoundStore.cs): kept in IndexedDB (local storage is
// far too small for them), played in the page with an <audio> element.
globalThis.lyriousSounds = (() => {
    let database = null;
    function open() {
        database ??= new Promise((resolve, reject) => {
            const request = indexedDB.open("lyrious.sounds", 1);
            request.onupgradeneeded = () => request.result.createObjectStore("sounds");
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
        return database;
    }

    async function run(mode, action) {
        const db = await open();
        return new Promise((resolve, reject) => {
            const transaction = db.transaction("sounds", mode);
            const request = action(transaction.objectStore("sounds"));
            transaction.oncomplete = () => resolve(request.result);
            transaction.onerror = () => reject(transaction.error);
            transaction.onabort = () => reject(transaction.error ?? new Error("The sound couldn't be stored (the browser may be out of room)."));
        });
    }

    let audio = null;
    let playingId = null;
    let url = null;
    let changed = () => { };

    function stop() {
        audio?.pause();
        audio = null;
        playingId = null;
        if (url) URL.revokeObjectURL(url);
        url = null;
    }

    return {
        // The bytes are only valid during this call: copied first, stored after.
        save: (id, bytes, type) => {
            const blob = new Blob([bytes.slice()], { type });
            return run("readwrite", store => store.put(blob, id));
        },
        play: async id => {
            const blob = await run("readonly", store => store.get(id));
            if (!blob) return false;
            stop();
            url = URL.createObjectURL(blob);
            audio = new Audio(url);
            playingId = id;
            audio.onended = () => { stop(); changed(); };
            await audio.play();
            return true;
        },
        stop,
        playing: () => playingId,
        onChanged: callback => { changed = callback; },
    };
})();

// The installed web app (PWA, see manifest.webmanifest): the service worker keeps a copy of the
// app's files, so it starts without a connection. Service workers need a secure context (https,
// or http://localhost); elsewhere the app simply isn't installable.
const serviceWorker = "serviceWorker" in navigator
    ? navigator.serviceWorker.register("./service-worker.js").catch(() => null)
    : null;
// Room for all the app's files in the list of what the page loaded (250 by default).
performance.setResourceTimingBufferSize?.(3000);

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

// Everything is loaded now: the worker is told which files, to keep those it didn't see being
// fetched (the first visit, before it took over) and to drop those of earlier versions.
serviceWorker?.then(registered => registered && navigator.serviceWorker.ready).then(registration => {
    if (!registration) return;
    const urls = performance.getEntriesByType("resource").map(entry => entry.name);
    urls.push("./", "./manifest.webmanifest", "./favicon.ico",
        "./icons/icon-192.png", "./icons/icon-512.png", "./icons/icon-maskable-512.png", "./icons/apple-touch-icon.png");
    registration.active?.postMessage({ type: "app-files", urls });
}).catch(() => { /* no worker: the app still runs */ });

const config = dotnetRuntime.getConfig();

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
