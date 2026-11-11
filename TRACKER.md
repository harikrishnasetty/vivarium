# Vivarium — Build Tracker

Live status of everything built, in progress, and planned.

Legend: `[x]` done · `[~]` in progress · `[ ]` pending

---

## v1.0.0 — first public release

### Foundation
- [x] Repository scaffold (MIT license, gitignore, docs layout)
- [x] Renderer engine runtime (`wallpapers/_runtime/engine.js`)
  - [x] Icon layout field with spring smoothing + add/remove detection
  - [x] Host message bridge (init / icons / params / pause / resume)
  - [x] 30 fps capped loop with auto-pause (hidden / fullscreen)
  - [x] Canvas + DPR management, resize handling
  - [x] Gradient noise / fbm, seeded RNG, color helpers, grain
  - [x] Day-night + season + moon-phase helpers
  - [x] Browser preview mode with sample desktop icons
- [x] Browser preview harness + zero-dependency dev server

### Wallpapers (16 total, all original designs)
Landscapes
- [x] Moss Vale — misty forest valley, fireflies orbit icons, day-night cycle
- [x] Emberfall — autumn woods, leaf vortices around icons, wind gusts
- [x] Snowlight — aurora night, layered snowfall, snowbanks settle around icons
- [x] Glow Shores — bioluminescent coast, tide pools form around icons
- [x] Coral Drift — reef scene, fish school avoids icons, swaying coral
- [x] Isles — alpine lake, icons become forested islets, mist and peaks
- [x] Rain City — neon skyline, icons become lit towers, rain + lightning
- [x] Nebula Drift — deep space, icons become planets with atmospheres
- [x] Paper Sky — origami world, paper cranes glide between icon islands
Generative
- [x] Flow Field — silk particle flow that bends around icons
- [x] Kaleido Bloom — mirrored generative blooms, icon-seeded symmetry
- [x] Lava Lamp — slow metaballs, color cycling, film grain
- [x] Zen Garden — raked sand rings adapt to icon positions, petals, stones
Utility
- [x] Chrono — minimal ink clock, date, moon phase, day-reactive palette
Media
- [x] Video wallpaper — play any local video, looped and muted
- [x] Image wallpaper — any local image with slow Ken Burns motion

### Windows host (C# 5, in-box compiler, no SDK)
- [x] WorkerW wallpaper attachment (behind desktop icons)
- [x] Multi-monitor rendering (one WebView2 per monitor)
- [x] Desktop icon layout reader (cross-process ListView inspection, hashed names only)
- [x] Fullscreen app auto-pause / resume
- [x] Explorer-restart recovery (re-attach automatically)
- [x] Tray icon: settings, next wallpaper, pause, start-with-Windows, exit
- [x] Single instance + `--exit` / `--settings` CLI flags
- [x] Settings persistence in `%LOCALAPPDATA%\Vivarium\settings.json`
- [x] WebView2 virtual host mapping + local media streaming
- [x] Startup-on-sign-in toggle (explicit user action only)
- [x] Logging for diagnostics

### Settings app (WebView2 UI)
- [x] Gallery with category filters and CSS-art thumbnails
- [x] Live animated preview of the selected wallpaper
- [x] Auto-generated per-wallpaper controls from `theme.json`
- [x] System page: startup, fullscreen pause, FPS, monitor selection, reduced motion
- [x] Media page: pick local video / image
- [x] About page: version, license, links
- [x] Dark glass UI, responsive layout

### Build & QA
- [x] `host/build.ps1` — one command build (fetches WebView2 SDK, compiles with in-box csc)
- [x] Icon generation script + embedded app icon
- [x] `install.ps1` / `uninstall.ps1` (portable per-user install)
- [x] Browser QA pass: every wallpaper screenshotted, console clean
- [x] Live desktop smoke test (WorkerW attach, icon detection, pause)
- [x] README with screenshots, architecture, wallpaper catalog, privacy notes
- [x] TRACKER.md published

### Ship
- [x] Git history with clean commits
- [ ] GitHub repo created (public) + pushed
- [ ] Release `v1.0.0` with portable ZIP + SHA256
- [ ] Topics, description, license detection

---

## Post-1.0 roadmap
- [ ] Audio-reactive wallpaper (WASAPI loopback capture in host → spectrum to renderer)
- [ ] Weather-reactive scenes (opt-in, keyless Open-Meteo)
- [ ] Playlists: rotate wallpapers on a schedule with crossfade
- [ ] Per-monitor different wallpapers
- [ ] Custom web/URL wallpaper (sandboxed)
- [ ] True crossfade transitions between wallpapers
- [ ] Localization (Simplified Chinese, more)
- [ ] NSIS installer + signed builds (reduces SmartScreen/AV false positives)
- [ ] Auto-update channel
- [ ] Linux/macOS exploration (separate host backends)
- [ ] Theme authoring guide + in-app scene importer