# Vivarium — Architecture

This document describes the overall architecture of the Vivarium project, from the renderer engine through the C# host to the settings UI.

## High-level diagram

```
+---------------------+        +--------------------------+
|  Windows Desktop    |        |  Vivarium host (C#)      |
|  +----------------+ |        |  +--------------------+  |
|  |  Explorer      | |        |  |  AppController     |  |
|  |  WorkerW container|---> |  |  WallpaperService  |  |
|  |  +------------+  |        |  |  TrayController    |  |
|  |  | Icons      | |        |  |  SettingsWindow    |  |
|  +----------------+ |        |  +--------------------+  |
+---------------------+        +--------------------------+
                                   |
                                   v
+---------------------+        +--------------------------+
|  WebView2 renderer  |        |  Settings UI (HTML/JS)   |
|  +----------------+ |        |  +-------------------+   |
|  |  Vivarium.js   | |        |  |  settings.html    |   |
|  |  engine.js     | |        |  +-------------------+   |
|  |  theme.json    | |        |           ^        |
|  +----------------+ |        |           |        |
|       ^            |        |  WebMessage   |  |
|       |  messages  |<-------+  t: 'select'  |
|   init  |  icons   |            t: 'params' |
|       |  params  |            t: 'system' |
|       +--------->|            t: 'media'  |
|               |  |              t: 'pickMedia' |
|               |  |              t: 'clearMedia' |
+---------------+  +---------------+---------------+
```

## Renderer engine (`wallpapers/_runtime/engine.js`)

The renderer is a **single ES module** imported by every wallpaper scene. It provides:

- **`Vivarium`** — main object with `canvas()`, `loop()`, `on()`, `setParams()`, `grain()`, `vignette()`, `bg()`
- **Canvas view** — creates a 2D canvas with device-pixel-ratio management (capped at 1.5×), resize handling
- **Noise functions** — `makeNoise2D(seed)`, `fbm()`, `hash32()`, `mulberry32(seed)` for seeded RNG
- **Gradient palettes** — 11 named palettes (`aurora`, `ember`, `ocean`, `dusk`, `forest`, `neon`, `paper`, `ink`, `lagoon`, plus custom per-scene palettes)
- **Day/night helpers** — `dayInfo()` returns sunrise/sunset times, moon phase, season
- **Icon field** — `IconField` class with spring-smoothed positions, `avoid()` for icon-aware particle spawning, `inside()` and `nearest()` queries
- **Sample desktop** — preview mode renders a sample icon layout with labeled tiles
- **Grain + vignette** — cheap post-processing effects drawn as canvas patterns

Every wallpaper imports only what it needs:

```js
import { Vivarium, TAU, clamp, lerp, makeNoise2D, mulberry32 } from '../_runtime/engine.js';
const V = await Vivarium.ready;
const view = V.canvas('stage');
V.loop((dt, t) => { /* scene code */ });
```

The module contract is **frozen** at `wallpapers/_runtime/engine.js`. No scene may import from any other location.

## C# host (`host/src/`)

The host is a **C# 5.0** executable compiled with the in-box `csc.exe` (no SDK, no NuGet restore required beyond the WebView2 SDK). Key components:

| Component | Responsibility |
|---|---|
| `Program.cs` | Process entry point; single-instance mutex; DPI awareness; `--exit` / `--settings` CLI flags; finds app root by walking up for `wallpapers/` directory |
| `AppController.cs` | Orchestrates the engine: loads theme catalog, manages settings persistence, WebView2 environment creation, per-media mapping (`vivarium-video.local`, `vivarium-image.local`), message bridge between host and every renderer, tray/ startup / system command handling |
| `WallpaperService.cs` | Per-monitor wallpaper window management: WorkerW discovery / attachment, monitor reconcilation on display changes, fullscreen detection / auto-pause, icon reading via `IconLayoutReader`, tick timer (every 500 ms), pause/resume propagation |
| `WallpaperWindow.cs` | One window per monitor; reparented into Explorer's `WorkerW` via `SetParent`; hosts a `WebView2` control; sends/receives JSON messages (`init`, `hello`, `icons`, `params`, `paused`, `resumed`, `media`) |
| `SettingsWindow.cs` | WebView2-based settings UI; hosts `https://vivarium.local/app/settings.html`; message protocol mirrors the renderer bridge (`hello`, `select`, `params`, `resetParams`, `system`, `startup`, `pickMedia`, `clearMedia`, `next`, `pause`, `resume`, `reload`, `openData`, `openLog`, `quit`) |
| `TrayController.cs` | `NotifyIcon` with context menu: open settings, next wallpaper, pause/resume, start-with-Windows, view log, exit; dynamic wallpaper submenu populated from the theme catalog |
| `AppIcon.cs` | Embedded app icon: 64×64 gradient-logo with leaf/sprout mark, built at runtime from `System.Drawing` |
| `SettingsStore.cs` | Reads/writes `%LOCALAPPDATA%\Vivarium\settings.json`; tracks `Existed` flag for first-run detection; `SystemSettings` and `ThemeManifest` classes |
| `Interop.cs` | P/Invoke definitions: `EnumDisplayMonitors`, `SetProcessDpiAwarenessContext`, `DwmSetWindowAttribute`, `SetParent`, `SetWindowLongPtr`, `FindWindowEx`, `GetWindowRect`, `MonitorFromWindow`, `Cloaked` check, plus per-monitor DPI v2 awareness |
| `Native.cs` | Extra P/Invoke: `GetForegroundWindow`, `IsCloaked`, `ClassNameOf`, `IsWindow`, `IsWindowVisible`, `IsIconic`, `GetParent` |
| `BridgeWindow.cs` | Hidden top-level window (`STATIC` class) that receives `WM_VIVARIUM_SETTINGS`, `WM_VIVARIUM_EXIT`, `WM_VIVARIUM_NEXT` messages from `Program.cs` and fires events |
| `StartupManager.cs` | Registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` management for the "Start with Windows" toggle |
| `Json.cs` | Minimal JSON serializer/deserializer using `JavaScriptSerializer` (C# 5 compatible, 16 MB max length) |
| `IconLayout.cs` | Cross-process `SysListView32` icon reader: enumerates explorer's icon view, extracts icon handles, computes FNV-1a hashes of icon names **in memory only**; never writes icon names or hashes to disk or logs |

The host writes **zero** dependencies aside from the WebView2 SDK (downloaded from NuGet on first build into `host/.deps`). The C# compiler is the one shipped with Windows (.NET Framework 4.x), which satisfies the "in-box" requirement.

## WebView2 environment

- SDK version pinned to **1.0.4314.35** (downloaded from NuGet into `host/.deps` on first build)
- Two custom scheme registrations: `vivarium.local` → host root (serves wallpaper HTML/JS), `vivarium-video.local` → folder of the selected video file, `vivarium-image.local` → folder of the selected image file
- Creation parameters include: `AreDevToolsEnabled` (controlled by `--devtools` CLI flag), `AreDefaultContextMenusEnabled = false`, `IsStatusBarEnabled = false`, `ZoomFactor = 1.0`, `IsZoomControlEnabled = false`
- Virtual host name mapping allows the renderer to reference local files as `https://vivarium-video.local/filename.mp4` and `https://vivarium-image.local/filename.jpg` without copying the files

## Message bridge (host ↔ renderer)

All communication uses **JSON messages** with a `t` (type) field. The handshake is:

1. **Host → Renderer**: `{ t: 'init', params, fps, reduced, icons, media, monitor }`
2. **Renderer → Host** (first message after init): `{ t: 'hello' }`
3. **Renderer → Host**: `{ t: 'icons', icons: [...] }`, `{ t: 'params', params: {...} }`, `{ t: 'media', media: {...} }`, `{ t: 'paused' }`, `{ t: 'resumed' }`
4. **Host → Renderer** (in response): same message types, echo + updated state

Message types (both directions):

| Type | Payload | Direction |
|---|---|---|
| `init` | `params`, `fps`, `reduced`, `icons`, `media`, `monitor` | Host → Renderer (first) |
| `hello` | `id` (theme id) | Renderer → Host (handshake) |
| `icons` | list of `{id, seed, x, y, w, h}` | Renderer → Host (live layout) |
| `params` | `{ key: value }` | Both directions |
| `paused` | `true` / `false` | Both directions |
| `resumed` | `null` | Both directions |
| `media` | `{ video: url?, image: url? }` | Renderer → Host |
| `resetParams` | `id` | Host → Renderer (reset to defaults) |
| `system` | `{ startup, pauseFullscreen, fps, monitors, reducedMotion }` | Renderer → Host |
| `startup` | `enabled` | Host ↔ Renderer |
| `pickMedia` | `kind: 'video' \| 'image'` | Host → Renderer (open picker) |
| `clearMedia` | `kind: 'video' \| 'image'` | Host → Renderer (clear) |
| `next` | `null` | Host → Renderer (next wallpaper) |
| `reload` | `null` | Host → Renderer (reload current) |
| `openData` | `null` | Host → Renderer (open data folder) |
| `openLog` | `null` | Host → Renderer (open log) |
| `quit` | `null` | Host → Renderer (quit) |

The host sends `BuildInit` to every new / re-attached wallpaper window. The renderer's `bootFromInit` boot sequence sets `Vivarium.params`, `Vivarium.fps`, `Vivarium.reduced`, `Vivarium.media`, and `Vivarium.monitor` from the init payload, then emits `init` to signal readiness.

## Theme catalog

The catalog is loaded from `wallpapers/*/theme.json` on startup. Each manifest defines:

- `id` — unique identifier (used as theme key)
- `name` — display name
- `category` — Landscapes / Generative / Ambient / Media
- `tagline` — one-line summary
- `desc` — longer description
- `tags` — array of keyword tags
- `thumb` → `{ from, to, accent }` — CSS gradient for the UI thumbnail
- `params` → array of `{ group, key, label, type, min, max, step, default }` — per-wallpaper controls

The host's `ThemeCatalog` class reads all `theme.json` files, deserializes them, sorts by category then name, and provides `Get(id)`, `IndexOf(id)`, and `MergedParams(id, settings)` (defaults + saved overrides).

Per-wallpaper `theme.json` examples:

```json
{
  "id": "moss-vale",
  "name": "Moss Vale",
  "category": "Landscapes",
  "tagline": "A misty forest that breathes with your icons.",
  "desc": "Layered pine ridges, drifting fog and fireflies that gather around your desktop icons.",
  "tags": ["adaptive", "day-night", "nature", "forest"],
  "thumb": { "from": "#071a18", "to": "#7fae6a", "accent": "#d9f2a0" },
  "params": [
    { "group": "Life", "key": "fireflies", "label": "Fireflies", "type": "range", "min": 0, "max": 200, "step": 5, "default": 80 },
    { "group": "Life", "key": "deer", "label": "Wandering deer", "type": "toggle", "default": true },
    { "group": "Atmosphere", "key": "mist", "label": "Mist", "type": "range", "min": 0, "max": 1, "step": 0.05, "default": 0.6 },
    { "group": "Atmosphere", "key": "sky", "label": "Sky", "type": "select", "options": [["auto","Auto (local time)"],["day","Day"],["dusk","Dusk"],["night","Night"]], "default": "auto" },
    { "group": "Motion", "key": "sway", "label": "Tree sway", "type": "range", "min": 0, "max": 2, "step": 0.1, "default": 1 }
  ]
}
```

## Settings UI (`app/settings.html`)

A single-page application served from `https://vivarium.local/app/settings.html`. Uses **no external dependencies** — plain HTML + CSS + vanilla JavaScript. Features:

- **Gallery grid** — 16 cards, category filter chips, live preview iframe, search box
- **Detail panel** — per-wallpaper param controls (range, toggle, select), live update, reset to defaults
- **System page** — startup toggle, FPS selector, pause in fullscreen, reduce motion, monitor selection
- **Media page** — choose local video or image wallpaper, clear selections, preview of file name
- **About page** — version, license, source link, issues link, privacy policy
- **Message bridge** — sends/receives the same JSON message types as the renderer host bridge
- **Toast notifications** — for user actions (wallpaper changed, settings reset, media picked)
- **Dev fallback** — when opened in a bare browser (no WebView2 host), fetches `/api/themes` from the dev server (`tools/dev-server.mjs`)

The UI communicates with the host via `window.chrome.webview.postMessage()` / `window.addEventListener('message', ...)`. All messages follow the same protocol as the renderer bridge.

## Build script (`host/build.ps1`)

A single PowerShell command that:

1. Finds the in-box `csc.exe` (C# 5 compiler from .NET Framework 4.x)
2. Downloads the WebView2 SDK (pinned to `1.0.4314.35`) from NuGet into `host/.deps` if not already present
3. Compiles all `host/src/*.cs` into `host/bin/vivarium.exe` with `/target:winexe`, `/platform:anycpu`, `/optimize+`, `/langversion:5`, and the `app.manifest` (per-monitor DPI v2, asInvoker)
4. Copies WebView2 runtime DLLs (`Microsoft.Web.WebView2.Core.dll`, `Microsoft.Web.WebView2.WinForms.dll`, `WebView2Loader.dll`) and manifest XML into the same directory
5. Assembles a portable ZIP (`dist/Vivarium-1.0.0-win-x64.zip`) containing `vivarium.exe`, all wallpaper folders, the `app/` UI, `LICENSE`, `README.md`, `TRACKER.md`, and `docs/`

Switches:
- `-NoDist` — stops after compilation, does not create the ZIP
- `-Clean` — removes the `bin/` directory before rebuilding

## Install / uninstall

- **install.ps1** — portable per-user install: creates data folder, copies host + wallpapers + docs, creates a desktop shortcut, writes default `settings.json` if missing
- **uninstall.ps1** — removes the desktop shortcut, optionally deletes the data folder and the dist package; does **not** delete personal settings; offers to clean the registry `Run` key

Both scripts require no admin rights and run under `Bypass` execution policy.

## Directory layout

```
vivarium/
├─ LICENSE
├─ .gitignore
├─ .gitattributes
├─ TRACKER.md
├─ README.md
├─ ARCHITECTURE.md
├─ install.ps1
├─ uninstall.ps1
├─ host/
│  ├─ app.manifest
│  ├─ build.ps1
│  ├─ .deps/                (WebView2 SDK downloaded by build.ps1)
│  ├─ src/                  (15 .cs files, all C# 5.0 compatible)
│  └─ bin/                  (created by build.ps1 → vivarium.exe + DLLs)
├─ wallpapers/
│  ├─ _runtime/
│  │  ├─ engine.js          (frozen renderer API)
│  │  └─ preview.html
│  ├─ moss-vale/            (id: moss-vale, theme + index.html)
│  ├─ zen-garden/
│  ├─ chrono/
│  ├─ flow-field/
│  ├─ emberfall/
│  ├─ snowlight/
│  ├─ glow-shores/
│  ├─ coral-drift/
│  ├─ rain-city/
│  ├─ nebula-drift/
│  ├─ isles/
│  ├─ kaleido-bloom/
│  ├─ lava-lamp/
│  ├─ paper-sky/
│  ├─ video/                (video wallpaper theme + index.html)
│  └─ image/                (image wallpaper theme + index.html)
├─ app/
│  └─ settings.html         (WebView2 settings UI, vanilla JS)
└─ tools/
   └─ dev-server.mjs       (port 5179, /api/themes endpoint)
```