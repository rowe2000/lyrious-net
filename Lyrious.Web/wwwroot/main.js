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

// The on-screen keyboard on iOS (Safari, and any iOS browser): it only shows for an input focused
// while handling a tap (a user gesture). Avalonia types into a hidden input (.avalonia-input-element)
// that it shows and focuses when a text box gets focus, but only after the tap has been handled, so
// iOS shows no keyboard. So when a tap ends, a hidden input that a text box has shown is focused
// again, now within the tap. Listened to on the document after the app's own handlers (bubbling);
// pointerup, touchend and click all count as the tap's end, in case the text box got focus late.
function focusTextInputOnTap() {
    const input = document.querySelector(".avalonia-input-element");
    if (input && input.style.display !== "none" && document.activeElement !== input) {
        input.focus({ preventScroll: true });
    }
}
document.addEventListener("pointerup", e => { if (e.pointerType === "touch") focusTextInputOnTap(); });
document.addEventListener("touchend", focusTextInputOnTap, { passive: true });
document.addEventListener("click", focusTextInputOnTap);

// MIDI out (BrowserMidiOutput.cs): every output the Web MIDI API offers. Access is asked for at the
// first send (the browser may ask the user), then kept; outputs plugged in later are included, as
// they're listed at each send. Browsers without Web MIDI (Safari) send nothing.
globalThis.lyriousMidi = (() => {
    let access = null;
    return {
        // "C0 14;CF 01": messages split by ';', their bytes (hex) by spaces.
        send: messages => {
            if (!navigator.requestMIDIAccess) return;
            const data = messages.split(";").filter(m => m.trim()).map(m => m.trim().split(/\s+/).map(b => parseInt(b, 16)));
            access ??= navigator.requestMIDIAccess({ sysex: false });
            access.then(midi => {
                for (const output of midi.outputs.values()) {
                    for (const message of data) {
                        try { output.send(message); } catch (e) { console.warn(`Couldn't send MIDI to ${output.name}`, e); }
                    }
                }
            }, e => {
                console.warn("MIDI isn't allowed here", e);
                access = null; // Asked again at the next send.
            });
        },
    };
})();

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

// Settings and the offline cache, kept between visits (BrowserSettingsStore.cs) in IndexedDB, which
// has far more room than local storage. The app reads them without waiting, so they're all loaded
// into memory before it starts (load) and each change is written to the database in the background;
// the other tabs on this address are told of it, to keep their copies the same. Storage can be
// blocked (e.g. private browsing), in which case settings last only while the page is open.
globalThis.lyriousSettings = (() => {
    const values = new Map();
    const channel = typeof BroadcastChannel === "function" ? new BroadcastChannel("lyrious.settings") : null;
    let database = null;

    if (channel) {
        channel.onmessage = event => {
            const { key, value } = event.data ?? {};
            if (typeof key !== "string") return;
            if (value == null) values.delete(key); else values.set(key, value);
        };
    }

    function open() {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open("lyrious.settings", 1);
            request.onupgradeneeded = () => request.result.createObjectStore("settings");
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    }

    function completed(transaction) {
        return new Promise((resolve, reject) => {
            transaction.oncomplete = () => resolve();
            transaction.onerror = () => reject(transaction.error);
            transaction.onabort = () => reject(transaction.error ?? new Error("The settings couldn't be stored (the browser may be out of room)."));
        });
    }

    function write(key, value) {
        if (!database) return;
        try {
            // Written in the order they're made: transactions on the same store run one after another.
            const transaction = database.transaction("settings", "readwrite");
            const store = transaction.objectStore("settings");
            if (value == null) store.delete(key); else store.put(value, key);
            completed(transaction).catch(error => console.warn(`The setting ${key} wasn't kept:`, error));
            transaction.commit?.();
        } catch (error) {
            console.warn(`The setting ${key} wasn't kept:`, error);
        }
    }

    // What earlier versions kept in local storage moves to the database (the database's own value
    // wins), and is removed from local storage once it's stored.
    async function moveFromLocalStorage() {
        let keys;
        try {
            keys = Object.keys(localStorage).filter(key => key.startsWith("lyrious."));
        } catch {
            return; // Blocked: nothing to move.
        }
        if (keys.length === 0) return;

        const transaction = database.transaction("settings", "readwrite");
        const store = transaction.objectStore("settings");
        for (const key of keys) {
            const value = localStorage.getItem(key);
            if (value != null && !values.has(key)) {
                values.set(key, value);
                store.put(value, key);
            }
        }
        await completed(transaction);
        for (const key of keys) localStorage.removeItem(key);
    }

    return {
        // Before the app starts (see the end of this file).
        load: async () => {
            try {
                database = await open();
                const transaction = database.transaction("settings", "readonly");
                const store = transaction.objectStore("settings");
                const keys = store.getAllKeys();
                const all = store.getAll();
                await completed(transaction);
                keys.result.forEach((key, i) => values.set(key, all.result[i]));
                await moveFromLocalStorage();
            } catch (error) {
                console.warn("Settings can't be kept here:", error);
                database = null;
            }
        },
        get: key => values.get(key) ?? null,
        set: (key, value) => {
            if (value == null) values.delete(key); else values.set(key, value);
            channel?.postMessage({ key, value });
            write(key, value);
        },
    };
})();
// Loads while the runtime downloads.
const settingsLoaded = globalThis.lyriousSettings.load();

// The app's other tabs and windows on this address (BrowserInstanceLink.cs). They share the stored
// settings and cache, so they pass each other their changes over a BroadcastChannel to stay the same without
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
// far too small for them), played in the page with an <audio> element. Its playbackRate, with the
// pitch kept, changes the speed; a pitch shifter in the Web Audio API changes the pitch, without
// the speed. That needs an AudioWorklet, only in a secure context (https or localhost).
globalThis.lyriousSounds = (() => {
    // A pitch shifter: two read heads glide through a short delay line, faster (higher) or slower
    // (lower) than it's written, each faded in and out as the other wraps around, so the jumps
    // aren't heard. The speed stays: as much comes out as goes in.
    const shifterCode = `
        class LyriousPitchShifter extends AudioWorkletProcessor {
            static get parameterDescriptors() {
                return [{ name: "ratio", defaultValue: 1, minValue: 0.25, maxValue: 4, automationRate: "k-rate" }];
            }
            constructor() {
                super();
                this.size = 1 << 15;
                this.window = Math.round(sampleRate * 0.07);
                this.buffers = [];
                this.write = 0;
                this.phase = 0;
            }
            read(buffer, delay) {
                const position = this.write - delay;
                const index = Math.floor(position);
                const fraction = position - index;
                const mask = this.size - 1;
                const a = buffer[index & mask], b = buffer[(index + 1) & mask];
                return a + (b - a) * fraction;
            }
            process(inputs, outputs, parameters) {
                const input = inputs[0], output = outputs[0];
                const ratio = parameters.ratio[0];
                const frames = output[0] ? output[0].length : 128;
                while (this.buffers.length < output.length) this.buffers.push(new Float32Array(this.size));
                const step = (1 - ratio) / this.window;
                for (let i = 0; i < frames; i++) {
                    for (let c = 0; c < output.length; c++) {
                        const channel = input[c] || input[0];
                        this.buffers[c][this.write] = channel ? channel[i] : 0;
                    }
                    if (ratio === 1) {
                        for (let c = 0; c < output.length; c++) output[c][i] = this.buffers[c][this.write];
                    } else {
                        this.phase = (this.phase + step) % 1;
                        if (this.phase < 0) this.phase += 1;
                        const first = this.phase, second = (this.phase + 0.5) % 1;
                        const gainFirst = Math.sin(Math.PI * first) ** 2, gainSecond = Math.sin(Math.PI * second) ** 2;
                        for (let c = 0; c < output.length; c++) {
                            output[c][i] = this.read(this.buffers[c], 1 + first * this.window) * gainFirst
                                + this.read(this.buffers[c], 1 + second * this.window) * gainSecond;
                        }
                    }
                    this.write = (this.write + 1) & (this.size - 1);
                }
                return true;
            }
        }
        registerProcessor("lyrious-pitch-shifter", LyriousPitchShifter);`;

    const canShiftPitch = typeof AudioWorkletNode === "function" && globalThis.isSecureContext;
    let context = null;
    let shifterReady = null;
    let shifter = null;
    let source = null;
    let speed = 1;
    let pitch = 0;

    async function connect(element) {
        if (!canShiftPitch) return;
        try {
            context ??= new AudioContext();
            shifterReady ??= context.audioWorklet.addModule(URL.createObjectURL(new Blob([shifterCode], { type: "text/javascript" })));
            await shifterReady;
            await context.resume();
            shifter ??= new AudioWorkletNode(context, "lyrious-pitch-shifter");
            shifter.connect(context.destination);
            shifter.parameters.get("ratio").value = 2 ** (pitch / 12);
            source = context.createMediaElementSource(element);
            source.connect(shifter);
        } catch (error) {
            console.warn("The pitch can't be changed here:", error);
        }
    }

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
        source?.disconnect();
        source = null;
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
        // The sound's bytes (to send to the server), or null when it isn't here; copyTo hands them over.
        load: async id => {
            const blob = await run("readonly", store => store.get(id));
            return blob ? new Uint8Array(await blob.arrayBuffer()) : null;
        },
        length: bytes => bytes.length,
        copyTo: (bytes, view) => view.set(bytes),
        // [{ id, size }] as JSON.
        list: async () => {
            const sounds = [];
            const db = await open();
            await new Promise((resolve, reject) => {
                const request = db.transaction("sounds", "readonly").objectStore("sounds").openCursor();
                request.onsuccess = () => {
                    const cursor = request.result;
                    if (!cursor) return resolve();
                    sounds.push({ id: String(cursor.key), size: cursor.value?.size ?? 0 });
                    cursor.continue();
                };
                request.onerror = () => reject(request.error);
            });
            return JSON.stringify(sounds);
        },
        remove: async id => {
            if (playingId === id) { stop(); changed(); }
            await run("readwrite", store => store.delete(id));
        },
        play: async id => {
            const blob = await run("readonly", store => store.get(id));
            if (!blob) return false;
            stop();
            url = URL.createObjectURL(blob);
            const element = new Audio(url);
            audio = element;
            playingId = id;
            element.preservesPitch = true;
            element.playbackRate = speed;
            element.onended = () => { if (audio === element) { stop(); changed(); } };
            await connect(element);
            await element.play();
            return true;
        },
        stop,
        playing: () => playingId,
        // Seconds from the sound's start, and its length; -1 when none plays or it isn't known yet.
        position: () => audio ? audio.currentTime : -1,
        duration: () => audio && Number.isFinite(audio.duration) ? audio.duration : -1,
        seek: seconds => {
            if (audio && Number.isFinite(audio.duration)) audio.currentTime = Math.min(Math.max(seconds, 0), audio.duration);
        },
        onChanged: callback => { changed = callback; },
        canChangePitch: () => canShiftPitch,
        setSpeed: value => {
            speed = value;
            if (audio) audio.playbackRate = speed;
        },
        setPitch: value => {
            pitch = value;
            shifter?.parameters.get("ratio").setValueAtTime(2 ** (pitch / 12), context.currentTime);
        },
    };
})();

// The installed web app (PWA, see manifest.webmanifest): the service worker keeps a copy of the
// app's files, so it starts without a connection. Service workers need a secure context (https,
// or http://localhost); elsewhere the app simply isn't installable.
// "?nosw" in the address leaves it out (and removes one installed before), e.g. while developing.
const noServiceWorker = new URLSearchParams(location.search).has("nosw");
if (noServiceWorker && "serviceWorker" in navigator) {
    for (const registration of await navigator.serviceWorker.getRegistrations()) await registration.unregister();
}
const serviceWorker = "serviceWorker" in navigator && !noServiceWorker
    ? navigator.serviceWorker.register("./service-worker.js").catch(() => null)
    : null;
// Whether the address the page came from is a Lyrious server itself (it answers "Pong"): the API
// serving the web app, or a desktop app sharing it on the network, which passes everything on to
// its server. Then the web app talks to that address, whatever host was saved here before (see
// Program.cs). Not the dev server, which only serves the app's files. Asked while the app loads.
const ownServer = (async () => {
    try {
        const response = await fetch("./api/ping", { method: "POST", cache: "no-store", signal: AbortSignal.timeout(3000) });
        return response.ok && (await response.text()).includes("Pong");
    } catch {
        return false;
    }
})();

// Room for all the app's files in the list of what the page loaded (250 by default).
performance.setResourceTimingBufferSize?.(3000);

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    // No debug symbols (.pdb): they're only for stepping through C# in the browser's developer
    // tools, and the dev server sometimes answers "not found" for them while the page loads, which
    // stopped the app from starting (stuck at the splash). A published app has none anyway.
    .withConfig({ debugLevel: 0 })
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

await settingsLoaded;

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href, (await ownServer) ? "own-server" : ""]);
