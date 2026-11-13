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

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
