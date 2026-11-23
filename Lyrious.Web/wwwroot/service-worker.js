// The service worker of the installed web app (PWA): keeps a copy of the app's own files, so the app
// starts without a connection. The files come from the network whenever it answers, so a new
// version shows up at once; the copy is only the fallback. Calls to the API and the hub are left
// alone: the app has its own offline cache for its data.

const CACHE = "lyrious-app";

// "Lyrious.UILib.25w6gaycsr.wasm": a build gives each such file a new name.
const FINGERPRINTED = /\.[a-z0-9]{10}\.[a-z]+$/;

self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", event => event.waitUntil(self.clients.claim()));

// The app's files, under the address the app is served from (the worker's scope).
function isAppFile(url) {
    const scope = new URL(self.registration.scope);
    if (url.origin !== scope.origin || !url.pathname.startsWith(scope.pathname)) return false;
    const path = url.pathname.slice(scope.pathname.length);
    return path === "" || /^(index\.html|main\.js|splash\.css|favicon\.ico|manifest\.webmanifest|icons\/.+|_framework\/.+)$/.test(path);
}

// Without the query ("?api=..."): the same file either way.
const keyOf = url => url.origin + url.pathname;

self.addEventListener("fetch", event => {
    const request = event.request;
    if (request.method !== "GET") return;
    const url = new URL(request.url);
    if (!isAppFile(url)) return;

    event.respondWith((async () => {
        const cache = await caches.open(CACHE);
        try {
            const response = await fetch(request);
            // Only whole, found files are kept: never a "not found" in a file's place.
            if (response.status === 200) {
                event.waitUntil(cache.put(keyOf(url), response.clone()));
            }
            return response;
        } catch (error) {
            const kept = await cache.match(keyOf(url));
            if (kept) return kept;
            throw error;
        }
    })());
});

// The page tells which files this version of the app loaded (main.js): those fetched before the
// worker took over are kept now, and the files of earlier versions are dropped.
self.addEventListener("message", event => {
    if (event.data?.type === "app-files" && Array.isArray(event.data.urls)) {
        event.waitUntil(keepAppFiles(event.data.urls));
    }
});

async function keepAppFiles(urls) {
    const cache = await caches.open(CACHE);
    const wanted = new Set();
    for (const address of urls) {
        try {
            const url = new URL(address, self.registration.scope);
            if (isAppFile(url)) wanted.add(keyOf(url));
        } catch { /* not an address */ }
    }

    for (const key of wanted) {
        if (await cache.match(key)) continue;
        try {
            const response = await fetch(key);
            if (response.status === 200) await cache.put(key, response);
        } catch { /* offline: next time */ }
    }

    // Only when this version's own files are in the list, so an incomplete one drops nothing.
    if (![...wanted].some(key => FINGERPRINTED.test(key))) return;
    for (const request of await cache.keys()) {
        if (FINGERPRINTED.test(request.url) && !wanted.has(request.url)) {
            await cache.delete(request);
        }
    }
}
