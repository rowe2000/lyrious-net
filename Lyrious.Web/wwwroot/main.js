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
