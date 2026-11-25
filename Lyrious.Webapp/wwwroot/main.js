import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

// The login is kept by the browser's own password manager, in every browser, through the real login
// form in index.html: on the login page its fields lie over the ones the app draws (lyriousLogin,
// BrowserLoginForm.cs), so the member types into them, and the browser offers to save the login
// when they press Log in, and fills it in from then on. (A browser only does that for a password
// typed into a real form and submitted by the user; Safari, on iPhone too, has no other way.)
globalThis.lyriousCredentials = (() => {
    const form = () => document.getElementById("lyrious-login");

    return {
        isSupported: () => !!form(),
        // The saved login, for logging in by itself at start: Chromium's password manager through
        // the Credential Management API (no account chooser unless it needs one); else what the
        // browser filled into the form (Firefox does at load).
        load: async () => {
            if (typeof globalThis.PasswordCredential === "function" && navigator.credentials) {
                try {
                    const credential = await navigator.credentials.get({ password: true, mediation: "optional" });
                    if (credential && credential.type === "password") {
                        return JSON.stringify({ id: credential.id, password: credential.password });
                    }
                } catch { /* not available here: the form, below */ }
            }

            const login = form();
            const id = login?.elements.username.value, password = login?.elements.password.value;
            return id && password ? JSON.stringify({ id, password }) : null;
        },
    };
})();

// The login page's fields as the real login form (BrowserLoginForm.cs): its username, password and
// Log in, each laid over the field the app draws (place), in the theme's colours. What's typed (or
// filled in by the browser) is reported as it changes; Log in (or Enter) submits the form into the
// empty frame next to it, so nothing is sent anywhere, but the browser sees a login and offers to
// save it. The app logs in from what was reported.
globalThis.lyriousLogin = (() => {
    const form = () => document.getElementById("lyrious-login");
    let onInput = null, onSubmit = null, listening = false;

    function report() {
        const login = form();
        if (login && onInput) onInput(login.elements.username.value, login.elements.password.value);
    }

    function listen(login) {
        if (listening) return;
        listening = true;
        login.addEventListener("input", report);
        // A login the browser fills in may come as a change only.
        login.addEventListener("change", report);
        // Not prevented: the submission (into the empty frame) is what the browser offers to save.
        login.addEventListener("submit", () => {
            report();
            onSubmit?.();
        });
    }

    return {
        show: (userPlaceholder, passwordPlaceholder, submitText, foreground, placeholder, accent, userName, password, inputCallback, submitCallback) => {
            const login = form();
            if (!login) return;
            listen(login);
            onInput = inputCallback;
            onSubmit = submitCallback;
            login.elements.username.placeholder = userPlaceholder;
            login.elements.password.placeholder = passwordPlaceholder;
            // What the app has (e.g. the saved login, after it failed); else what the browser filled in stays.
            if (userName || password) {
                login.elements.username.value = userName;
                login.elements.password.value = password;
            }
            const button = login.querySelector("button");
            button.textContent = submitText;
            login.style.setProperty("--lyrious-foreground", foreground);
            login.style.setProperty("--lyrious-placeholder", placeholder);
            login.style.setProperty("--lyrious-accent", accent);
            for (const element of [login.elements.username, login.elements.password, button]) element.style.display = "none";
            login.classList.add("shown");
            login.removeAttribute("aria-hidden");
            // With a mouse, ready to type (a touch screen shows its keyboard only on a tap).
            if (matchMedia("(pointer: fine)").matches) {
                setTimeout(() => (userName ? login.elements.password : login.elements.username).focus({ preventScroll: true }), 50);
            }
            // What the browser filled in already (e.g. Firefox at load), for the app; empty fields aren't
            // reported, as that would clear what the app fills in just after.
            if (!userName && !password && (login.elements.username.value || login.elements.password.value)) report();
        },
        // A field ("username", "password" or "button") over the app's, in page pixels; none when its width is 0.
        place: (field, x, y, width, height, fontSize, padding, radius) => {
            const login = form();
            if (!login) return;
            const element = field === "button" ? login.querySelector("button") : login.elements[field];
            if (!(width > 0 && height > 0)) {
                element.style.display = "none";
                return;
            }
            element.style.display = "";
            element.style.left = x + "px";
            element.style.top = y + "px";
            element.style.width = width + "px";
            element.style.height = height + "px";
            element.style.borderRadius = radius + "px";
            if (field !== "button") {
                element.style.fontSize = fontSize + "px";
                element.style.paddingLeft = padding + "px";
                element.style.paddingRight = padding + "px";
            }
        },
        setValues: (userName, password) => {
            const login = form();
            if (!login) return;
            login.elements.username.value = userName;
            login.elements.password.value = password;
        },
        hide: () => {
            const login = form();
            onInput = onSubmit = null;
            if (!login) return;
            login.classList.remove("shown");
            login.setAttribute("aria-hidden", "true");
            login.elements.password.value = "";
            login.elements.username.blur();
            login.elements.password.blur();
        },
    };
})();

// The device, for what the app leaves out where there's no room (Program.cs): an iPhone (any browser
// on one says so; an iPad's Safari says it's a Mac, so it isn't taken for one).
globalThis.lyriousDevice = {
    isIPhone: () => /iPhone|iPod/.test(navigator.userAgent),
};

// Recording the band as it plays (BrowserAudioRecorder.cs): the sound input the browser gives
// (it asks the member the first time), two channels, without the processing meant for calls (echo
// cancelling, noise suppression, automatic gain), which would spoil music. An audio worklet hands on
// what comes in; it's kept as 16-bit samples, interleaved (a one-channel input in both), and taken by
// the app a few times a second (take, as base64).
globalThis.lyriousRecorder = (() => {
    let context = null, stream = null, node = null, source = null;
    let chunks = [];

    // The worklet: each block of input (two channels; one given twice) is posted to the page.
    const workletCode = `
        class LyriousTap extends AudioWorkletProcessor {
            process(inputs) {
                const input = inputs[0];
                if (input && input.length > 0) {
                    const left = input[0], right = input.length > 1 ? input[1] : input[0];
                    const block = new Int16Array(left.length * 2);
                    for (let i = 0; i < left.length; i++) {
                        block[2 * i] = Math.max(-1, Math.min(1, left[i])) * 32767;
                        block[2 * i + 1] = Math.max(-1, Math.min(1, right[i])) * 32767;
                    }
                    this.port.postMessage(block, [block.buffer]);
                }
                return true;
            }
        }
        registerProcessor("lyrious-tap", LyriousTap);`;

    function cleanUp() {
        try { source?.disconnect(); node?.disconnect(); } catch { }
        stream?.getTracks().forEach(track => track.stop());
        context?.close().catch(() => { });
        context = stream = node = source = null;
    }

    return {
        isSupported: () => !!(navigator.mediaDevices?.getUserMedia && typeof AudioWorkletNode === "function"),
        // The sound inputs, as JSON ([{ id, name }]). Their names only once the page may use the
        // microphone (until then they're numbered); the browser's own "default" entries are left
        // out, as the app offers the device's own choice itself.
        inputs: async () => {
            if (!navigator.mediaDevices?.enumerateDevices) return "[]";
            const devices = (await navigator.mediaDevices.enumerateDevices())
                .filter(device => device.kind === "audioinput" && device.deviceId && device.deviceId !== "default" && device.deviceId !== "communications");
            return JSON.stringify(devices.map((device, index) => ({ id: device.deviceId, name: device.label || `Sound input ${index + 1}` })));
        },
        // Starts from the input (its id; "" for the device's own choice); the sample rate. Fails with
        // the browser's reason (e.g. not allowed). An input that isn't there any more is left for the
        // device's own choice.
        start: async deviceId => {
            cleanUp();
            chunks = [];
            // Made at once, in answer to the press of play: a browser lets sound start only then.
            context = new AudioContext();
            const audio = { channelCount: { ideal: 2 }, echoCancellation: false, noiseSuppression: false, autoGainControl: false };
            try {
                try {
                    stream = await navigator.mediaDevices.getUserMedia({ audio: deviceId ? { ...audio, deviceId: { exact: deviceId } } : audio });
                } catch (error) {
                    if (!deviceId || (error?.name !== "OverconstrainedError" && error?.name !== "NotFoundError")) throw error;
                    stream = await navigator.mediaDevices.getUserMedia({ audio });
                }
                const url = URL.createObjectURL(new Blob([workletCode], { type: "text/javascript" }));
                await context.audioWorklet.addModule(url);
                URL.revokeObjectURL(url);
                source = context.createMediaStreamSource(stream);
                node = new AudioWorkletNode(context, "lyrious-tap", { channelCount: 2, channelCountMode: "explicit", numberOfOutputs: 0 });
                node.port.onmessage = event => chunks.push(event.data);
                source.connect(node);
                await context.resume();
                return context.sampleRate;
            } catch (error) {
                cleanUp();
                throw new Error(error?.name === "NotAllowedError"
                    ? "the browser wasn't allowed to use the microphone (allow it in the address bar)"
                    : error?.message ?? String(error));
            }
        },
        // What came in since the last time, as base64 (16-bit samples, interleaved); "" when nothing.
        take: () => {
            if (chunks.length === 0) return "";
            const length = chunks.reduce((sum, chunk) => sum + chunk.length, 0);
            const all = new Int16Array(length);
            let at = 0;
            for (const chunk of chunks) { all.set(chunk, at); at += chunk.length; }
            chunks = [];
            const bytes = new Uint8Array(all.buffer);
            let binary = "";
            for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
            return btoa(binary);
        },
        stop: () => cleanUp(),
    };
})();

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
            // With System Exclusive (the transpose sent to Yamaha keyboards): the browser asks for it.
            access ??= navigator.requestMIDIAccess({ sysex: true });
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
        // Sends one message ("F0 43 …", hex bytes) on every output and gathers the System Exclusive
        // messages that come back on the inputs, until one with lastValue at lastIndex (the last of the
        // answer) or timeoutMs without it; they come back as sent ("F0 …;F0 …").
        request: async (message, lastIndex, lastValue, timeoutMs) => {
            if (!navigator.requestMIDIAccess) return "";
            access ??= navigator.requestMIDIAccess({ sysex: true });
            let midi;
            try { midi = await access; } catch (e) { access = null; return ""; }
            const bytes = message.trim().split(/\s+/).map(b => parseInt(b, 16));
            const replies = [];
            let finish;
            const done = new Promise(resolve => { finish = resolve; setTimeout(resolve, timeoutMs); });
            const onMessage = event => {
                const data = event.data;
                if (!data || data[0] !== 0xF0) return;
                replies.push(Array.from(data, b => b.toString(16).padStart(2, "0")).join(" "));
                if (data.length > lastIndex && data[lastIndex] === lastValue) finish();
            };
            const inputs = [...midi.inputs.values()];
            inputs.forEach(input => input.addEventListener("midimessage", onMessage));
            try {
                for (const output of midi.outputs.values()) {
                    try { output.send(bytes); } catch (e) { console.warn(`Couldn't send MIDI to ${output.name}`, e); }
                }
                await done;
            } finally {
                inputs.forEach(input => input.removeEventListener("midimessage", onMessage));
            }
            return replies.join(";");
        },
    };
})();

// Full screen (BrowserFullScreen.cs): the page on the whole screen while the member wants it. A
// browser only allows it in answer to a tap or a key, so when it's asked for otherwise (e.g. the
// setting was on when the app started) it starts at the next one. Leaving it otherwise (Escape, the
// browser's own) is reported, so the setting goes off instead of full screen coming back.
globalThis.lyriousFullScreen = (() => {
    let wanted = false;
    let exited = null;
    const page = document.documentElement;
    const active = () => !!document.fullscreenElement;
    function enter() {
        if (wanted && !active() && page.requestFullscreen) {
            page.requestFullscreen().catch(() => { /* not allowed now: at the next tap or key */ });
        }
    }
    document.addEventListener("pointerup", enter);
    document.addEventListener("keyup", enter);
    document.addEventListener("fullscreenchange", () => {
        if (!active() && wanted) {
            wanted = false;
            exited?.();
        }
    });
    return {
        // Not in Safari on iPhone, which only lets a video be full screen.
        isSupported: () => typeof page.requestFullscreen === "function",
        set: on => {
            wanted = on;
            if (on) {
                enter();
            } else if (active()) {
                document.exitFullscreen().catch(() => { });
            }
        },
        onExited: callback => { exited = callback; },
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
        // How loud the sound is over time: the loudest sample (all channels) in each 1/rate second, as
        // the bytes of 32-bit floats (copyTo hands them over); null when it isn't here or the browser
        // can't decode it.
        wave: async (id, rate) => {
            const blob = await run("readonly", store => store.get(id));
            if (!blob) return null;
            try {
                const decoder = new OfflineAudioContext(1, 1, 44100);
                const buffer = await decoder.decodeAudioData(await blob.arrayBuffer());
                const perSlice = buffer.sampleRate / rate;
                const levels = new Float32Array(Math.ceil(buffer.length / perSlice));
                for (let c = 0; c < buffer.numberOfChannels; c++) {
                    const data = buffer.getChannelData(c);
                    for (let i = 0; i < data.length; i++) {
                        const size = Math.abs(data[i]);
                        const slice = Math.floor(i / perSlice);
                        if (size > levels[slice]) levels[slice] = size;
                    }
                }
                return new Uint8Array(levels.buffer);
            } catch (error) {
                console.warn("The browser couldn't decode the sound:", error);
                return null;
            }
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
// The Lyrious app, when it's installed here: tried once per browser tab session as the web app
// starts, through its lyrious:// link (registered by the desktop, Android and iOS apps). Where
// it's installed the browser opens it (often asking first); where it isn't, nothing shows and the
// web app just goes on. Not tried in the installed web app (PWA), nor with ?web in the address.
// iOS isn't tried: Safari shows an error for a link no app takes, and there's no way to ask first.
(function openInstalledApp() {
    const standalone = matchMedia("(display-mode: standalone)").matches || navigator.standalone === true;
    const ios = /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
    let tried = false;
    try { tried = sessionStorage.getItem("lyrious-app-tried") === "1"; sessionStorage.setItem("lyrious-app-tried", "1"); } catch { /* no storage: try anyway */ }
    if (standalone || ios || tried || new URLSearchParams(location.search).has("web")) return;

    if (/Android/.test(navigator.userAgent)) {
        // Chrome on Android opens the app, or, without it, this page again with ?web (no retry).
        const back = new URL(location.href);
        back.searchParams.set("web", "");
        location.href = "intent://open#Intent;scheme=lyrious;package=com.lyrious.app;S.browser_fallback_url="
            + encodeURIComponent(back.href) + ";end";
        return;
    }

    // In a hidden frame, so a browser without the app (e.g. Firefox) shows no error page instead of the web app.
    const frame = document.createElement("iframe");
    frame.style.display = "none";
    frame.src = "lyrious://open";
    document.body.appendChild(frame);
    setTimeout(() => frame.remove(), 3000);
})();

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
