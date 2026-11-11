/*
 * Vivarium — renderer engine runtime
 * MIT License · original implementation for the Vivarium project.
 *
 * Every wallpaper imports this module. It handles:
 *   · desktop icon layout (live, smoothed, host-fed or sample)
 *   · host <-> page message bridge (init / icons / params / pause / resume)
 *   · 30 fps capped render loop with automatic pause
 *   · canvas + DPR + resize management
 *   · seeded randomness, gradient noise / fbm, palettes, day-night helpers
 *   · browser preview mode with a sample desktop
 *
 * Scene contract:
 *   import { Vivarium, TAU, clamp, lerp, makeNoise2D, mulberry32, hash32 } from '../_runtime/engine.js';
 *   const V = await Vivarium.ready;
 *   const view = V.canvas('stage');
 *   V.loop((dt, t) => { ... });
 */

export const TAU = Math.PI * 2;

export const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
export const lerp = (a, b, t) => a + (b - a) * t;
export const smoothstep = (a, b, x) => {
  const t = clamp((x - a) / (b - a || 1e-6), 0, 1);
  return t * t * (3 - 2 * t);
};
export const easeInOut = (t) => (t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2);

export function hash32(str) {
  let h = 0x811c9dc5;
  const s = String(str);
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = Math.imul(h, 0x01000193);
  }
  return h >>> 0;
}

export function mulberry32(seed) {
  let a = seed >>> 0;
  return function () {
    a |= 0;
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/* Gradient noise, permutation based. Returns values roughly in [-1, 1]. */
export function makeNoise2D(seed) {
  const rnd = mulberry32(seed >>> 0);
  const p = new Uint8Array(256);
  for (let i = 0; i < 256; i++) p[i] = i;
  for (let i = 255; i > 0; i--) {
    const j = (rnd() * (i + 1)) | 0;
    const t = p[i];
    p[i] = p[j];
    p[j] = t;
  }
  const perm = new Uint8Array(512);
  for (let i = 0; i < 512; i++) perm[i] = p[i & 255];
  const gx = new Float32Array(256);
  const gy = new Float32Array(256);
  for (let i = 0; i < 256; i++) {
    const a = rnd() * TAU;
    gx[i] = Math.cos(a);
    gy[i] = Math.sin(a);
  }
  const fade = (t) => t * t * t * (t * (t * 6 - 15) + 10);
  return function noise2(x, y) {
    const xi = Math.floor(x);
    const yi = Math.floor(y);
    const xf = x - xi;
    const yf = y - yi;
    const X = xi & 255;
    const Y = yi & 255;
    const grad = (ix, iy, dx, dy) => {
      const h = perm[(perm[ix] + iy) & 255];
      return gx[h] * dx + gy[h] * dy;
    };
    const u = fade(xf);
    const v = fade(yf);
    const n00 = grad(X, Y, xf, yf);
    const n10 = grad((X + 1) & 255, Y, xf - 1, yf);
    const n01 = grad(X, (Y + 1) & 255, xf, yf - 1);
    const n11 = grad((X + 1) & 255, (Y + 1) & 255, xf - 1, yf - 1);
    return lerp(lerp(n00, n10, u), lerp(n01, n11, u), v);
  };
}

export function fbm(noise, x, y, octaves = 4, lacunarity = 2, gain = 0.5) {
  let amp = 1;
  let freq = 1;
  let sum = 0;
  let norm = 0;
  for (let i = 0; i < octaves; i++) {
    sum += noise(x * freq, y * freq) * amp;
    norm += amp;
    amp *= gain;
    freq *= lacunarity;
  }
  return sum / (norm || 1);
}

/* Small hex/rgba helpers used by scenes. */
export function hexToRgb(hex) {
  let h = String(hex).replace('#', '');
  if (h.length === 3) h = h[0] + h[0] + h[1] + h[1] + h[2] + h[2];
  const n = parseInt(h, 16) || 0;
  return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255 };
}
export function rgba(hex, a) {
  const c = hexToRgb(hex);
  return 'rgba(' + c.r + ',' + c.g + ',' + c.b + ',' + a + ')';
}
export function mixHex(a, b, t) {
  const A = hexToRgb(a);
  const B = hexToRgb(b);
  const r = Math.round(lerp(A.r, B.r, t));
  const g = Math.round(lerp(A.g, B.g, t));
  const bl = Math.round(lerp(A.b, B.b, t));
  return 'rgb(' + r + ',' + g + ',' + bl + ')';
}
export function hsl(h, s, l, a) {
  return a === undefined
    ? 'hsl(' + h + ',' + s + '%,' + l + '%)'
    : 'hsla(' + h + ',' + s + '%,' + l + '%,' + a + ')';
}

/* Named palettes shared by scenes. */
export const palettes = {
  aurora: ['#061621', '#0b2f3d', '#126b63', '#2fd3a8', '#c8ffe9'],
  ember: ['#1a0906', '#4a1a10', '#a83f1f', '#e8823f', '#ffd9a3'],
  ocean: ['#02101d', '#07293f', '#0f5f74', '#2ab3ad', '#c4f5ec'],
  dusk: ['#1b1230', '#3d1f52', '#8a3d63', '#e2705f', '#ffc98a'],
  forest: ['#0a1510', '#16301f', '#2f5d3a', '#7fae6a', '#e3f2c4'],
  neon: ['#08060f', '#1a0f2e', '#5c2a8a', '#d64fa3', '#4fd8e8'],
  paper: ['#e8dfc9', '#d9cdb2', '#c2b393', '#9a8c72', '#6b5f4c'],
  ink: ['#f2efe6', '#dcd7c8', '#a9a294', '#57544c', '#1d1c19'],
  lagoon: ['#03242b', '#0a4a52', '#117d78', '#3fc0a0', '#d4fff0'],
};

/* ------------------------------------------------------------------ */
/* Day / season helpers                                               */
/* ------------------------------------------------------------------ */
export function dayInfo(date) {
  const now = date || new Date();
  const start = new Date(now.getFullYear(), 0, 0);
  const dayOfYear = Math.floor((now - start) / 86400000);
  const hours = now.getHours() + now.getMinutes() / 60 + now.getSeconds() / 3600;
  const dayFrac = hours / 24;
  const seasonAngle = (TAU * (dayOfYear - 80)) / 365.25;
  const daylight = clamp(12 + 3.4 * Math.sin(seasonAngle), 7.5, 16.5);
  const sunrise = 12 - daylight / 2;
  const sunset = 12 + daylight / 2;
  const isNight = hours < sunrise || hours > sunset;
  const synodic = 29.530588853;
  const ref = Date.UTC(2000, 0, 6, 18, 14);
  const days = (now.getTime() - ref) / 86400000;
  const moonPhase = (((days % synodic) + synodic) % synodic) / synodic;
  const m = now.getMonth();
  const season = m <= 1 || m === 11 ? 3 : m <= 4 ? 0 : m <= 7 ? 1 : 2;
  return { dayFrac, sunrise, sunset, isNight, moonPhase, season, dayOfYear };
}

/* ------------------------------------------------------------------ */
/* Icon field                                                         */
/* ------------------------------------------------------------------ */
export class IconField {
  constructor() {
    this.list = [];
    this._byId = new Map();
    this._clock = 0;
  }

  setRaw(raw) {
    const now = this._clock;
    const seen = new Set();
    const incoming = Array.isArray(raw) ? raw : [];
    for (let k = 0; k < incoming.length; k++) {
      const r = incoming[k];
      if (!r) continue;
      const id = String(r.id != null ? r.id : k);
      seen.add(id);
      let ic = this._byId.get(id);
      if (!ic) {
        ic = {
          id: id,
          seed: r.seed != null ? r.seed >>> 0 : hash32(id),
          x: r.x,
          y: r.y,
          tx: r.x,
          ty: r.y,
          vx: 0,
          vy: 0,
          w: r.w || 80,
          h: r.h || 90,
          born: now,
          gone: 0,
          fresh: 1,
        };
        this._byId.set(id, ic);
        this.list.push(ic);
      } else {
        ic.tx = r.x;
        ic.ty = r.y;
        ic.w = r.w || ic.w;
        ic.h = r.h || ic.h;
        ic.gone = 0;
      }
    }
    for (let k = 0; k < this.list.length; k++) {
      const ic = this.list[k];
      if (!seen.has(ic.id) && !ic.gone) ic.gone = now;
    }
  }

  step(dt) {
    this._clock += dt;
    const now = this._clock;
    const stiffness = 46;
    const damping = 2 * Math.sqrt(stiffness) * 0.82;
    for (let k = this.list.length - 1; k >= 0; k--) {
      const ic = this.list[k];
      const ax = (ic.tx - ic.x) * stiffness - ic.vx * damping;
      const ay = (ic.ty - ic.y) * stiffness - ic.vy * damping;
      ic.vx += ax * dt;
      ic.vy += ay * dt;
      ic.x += ic.vx * dt;
      ic.y += ic.vy * dt;
      ic.fresh = Math.max(0, ic.fresh - dt * 0.7);
      ic.cx = ic.x + ic.w / 2;
      ic.cy = ic.y + ic.h / 2;
      if (ic.gone && now - ic.gone > 1.6) {
        this.list.splice(k, 1);
        this._byId.delete(ic.id);
      }
    }
  }

  get active() {
    return this.list;
  }

  inside(x, y, pad = 0) {
    for (let k = 0; k < this.list.length; k++) {
      const ic = this.list[k];
      if (x > ic.x - pad && x < ic.x + ic.w + pad && y > ic.y - pad && y < ic.y + ic.h + pad) return true;
    }
    return false;
  }

  /* Softly pushes a point out of icon rectangles; returns a new point. */
  avoid(x, y, pad = 0, strength = 0.55) {
    let ox = 0;
    let oy = 0;
    for (let k = 0; k < this.list.length; k++) {
      const ic = this.list[k];
      const cx = ic.x + ic.w / 2;
      const cy = ic.y + ic.h / 2;
      const hw = ic.w / 2 + pad;
      const hh = ic.h / 2 + pad;
      const dx = x + ox - cx;
      const dy = y + oy - cy;
      if (Math.abs(dx) < hw && Math.abs(dy) < hh) {
        const px = hw - Math.abs(dx);
        const py = hh - Math.abs(dy);
        if (px < py) ox += (dx < 0 ? -px : px) * strength;
        else oy += (dy < 0 ? -py : py) * strength;
      }
    }
    return { x: x + ox, y: y + oy };
  }

  nearest(x, y, maxDist = Infinity) {
    let best = null;
    let bestD = maxDist * maxDist;
    for (let k = 0; k < this.list.length; k++) {
      const ic = this.list[k];
      const dx = x - (ic.x + ic.w / 2);
      const dy = y - (ic.y + ic.h / 2);
      const d = dx * dx + dy * dy;
      if (d < bestD) {
        bestD = d;
        best = ic;
      }
    }
    return best;
  }
}

/* ------------------------------------------------------------------ */
/* Canvas view                                                        */
/* ------------------------------------------------------------------ */
function makeView(canvasEl, opts) {
  const ctx = canvasEl.getContext('2d', { alpha: false });
  const cap = (opts && opts.maxDpr) || 1.5;
  const state = { w: 0, h: 0, dpr: 1 };
  const callbacks = [];
  let raf = 0;
  function apply() {
    const rect = canvasEl.getBoundingClientRect();
    const w = Math.max(1, Math.round(rect.width || window.innerWidth));
    const h = Math.max(1, Math.round(rect.height || window.innerHeight));
    const dpr = Math.min(window.devicePixelRatio || 1, cap);
    if (w === state.w && h === state.h && dpr === state.dpr) return;
    state.w = w;
    state.h = h;
    state.dpr = dpr;
    canvasEl.width = Math.round(w * dpr);
    canvasEl.height = Math.round(h * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    for (let i = 0; i < callbacks.length; i++) callbacks[i](state);
  }
  function schedule() {
    if (raf) return;
    raf = requestAnimationFrame(function () {
      raf = 0;
      apply();
    });
  }
  apply();
  window.addEventListener('resize', schedule);
  if (window.ResizeObserver) new ResizeObserver(schedule).observe(canvasEl);
  return {
    ctx: ctx,
    get w() {
      return state.w;
    },
    get h() {
      return state.h;
    },
    get dpr() {
      return state.dpr;
    },
    onResize(fn) {
      callbacks.push(fn);
      fn(state);
      return this;
    },
  };
}

/* ------------------------------------------------------------------ */
/* Preview sample desktop                                             */
/* ------------------------------------------------------------------ */
const SAMPLE_NAMES = [
  'Projects', 'Design', 'Documents', 'Photos', 'Music', 'Videos',
  'Archive', 'Notes', 'Games', 'Tools', 'Cloud', 'Scratch',
  'Recipes', 'Travel', 'Finance', 'Reads', 'Studio', 'Inbox',
];

function sampleLayout(w, h) {
  const out = [];
  const size = Math.round(clamp(Math.min(w, h) * 0.072, 58, 92));
  const stepX = size * 1.16;
  const stepY = size * 1.34;
  const rows = Math.max(3, Math.floor((h - 56) / stepY));
  const cols = Math.max(2, Math.min(3, Math.floor((w * 0.2) / stepX)));
  let n = 0;
  for (let c = 0; c < cols; c++) {
    for (let r = 0; r < rows; r++) {
      if (n >= SAMPLE_NAMES.length) break;
      out.push({
        id: SAMPLE_NAMES[n],
        x: 26 + c * stepX,
        y: 24 + r * stepY,
        w: size,
        h: size + 18,
      });
      n++;
    }
  }
  const rightRows = Math.max(2, rows - 3);
  for (let r = 0; r < rightRows && n < SAMPLE_NAMES.length; r++) {
    out.push({
      id: SAMPLE_NAMES[n],
      x: w - 26 - size,
      y: 24 + r * stepY,
      w: size,
      h: size + 18,
    });
    n++;
  }
  return out;
}

let sampleOverlay = null;
function renderSampleOverlay(icons) {
  if (!sampleOverlay) {
    sampleOverlay = document.createElement('div');
    sampleOverlay.className = 'vv-sample-icons';
    const css = document.createElement('style');
    css.textContent =
      '.vv-sample-icons{position:fixed;inset:0;pointer-events:none;z-index:5;font-family:"Segoe UI",system-ui,sans-serif}' +
      '.vv-sample-icons .vv-tile{position:absolute;display:flex;flex-direction:column;align-items:center;gap:6px;filter:drop-shadow(0 2px 6px rgba(0,0,0,.35))}' +
      '.vv-sample-icons .vv-glyph{width:100%;aspect-ratio:1/1;border-radius:22%;display:grid;place-items:center;color:rgba(255,255,255,.92);font-weight:600;box-shadow:inset 0 1px 0 rgba(255,255,255,.25),0 4px 12px rgba(0,0,0,.28);backdrop-filter:blur(2px)}' +
      '.vv-sample-icons .vv-label{font-size:11px;color:rgba(255,255,255,.88);text-shadow:0 1px 3px rgba(0,0,0,.8);max-width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}' +
      '.vv-sample-icons.vv-hidden{display:none}';
    document.head.appendChild(css);
    document.body.appendChild(sampleOverlay);
  }
  sampleOverlay.innerHTML = '';
  for (let i = 0; i < icons.length; i++) {
    const ic = icons[i];
    const hue = hash32(ic.id) % 360;
    const tile = document.createElement('div');
    tile.className = 'vv-tile';
    tile.style.left = ic.x + 'px';
    tile.style.top = ic.y + 'px';
    tile.style.width = ic.w + 'px';
    const glyph = document.createElement('div');
    glyph.className = 'vv-glyph';
    glyph.style.background =
      'linear-gradient(145deg, hsl(' + hue + ',62%,58%), hsl(' + ((hue + 42) % 360) + ',68%,42%))';
    glyph.textContent = ic.id.charAt(0).toUpperCase();
    const label = document.createElement('div');
    label.className = 'vv-label';
    label.textContent = ic.id;
    tile.appendChild(glyph);
    tile.appendChild(label);
    sampleOverlay.appendChild(tile);
  }
}

/* ------------------------------------------------------------------ */
/* Main object                                                        */
/* ------------------------------------------------------------------ */
const host = (typeof window !== 'undefined' && window.chrome && window.chrome.webview) || null;
const params0 = new URLSearchParams(typeof location !== 'undefined' ? location.search : '');
const mode = host ? 'host' : 'preview';
const pathParts = (typeof location !== 'undefined' ? location.pathname : '').split('/').filter(Boolean);
const wpIndex = pathParts.indexOf('wallpapers');
const themeId =
  (wpIndex >= 0 && pathParts[wpIndex + 1]) ||
  params0.get('theme') ||
  'unknown';

const listeners = {};
const readyResolve = { fn: null };

export const Vivarium = {
  id: themeId,
  mode: mode,
  params: {},
  icons: new IconField(),
  size: { w: 0, h: 0, dpr: 1 },
  monitor: { index: 0, x: 0, y: 0, w: 0, h: 0 },
  media: null,
  fps: Number(params0.get('fps')) || 30,
  paused: false,
  reduced: params0.get('reduced') === '1',
  time: dayInfo(),
  ready: new Promise(function (resolve) {
    readyResolve.fn = resolve;
  }),

  on(evt, fn) {
    (listeners[evt] || (listeners[evt] = [])).push(fn);
    return this;
  },

  off(evt, fn) {
    const arr = listeners[evt];
    if (arr) {
      const i = arr.indexOf(fn);
      if (i >= 0) arr.splice(i, 1);
    }
    return this;
  },

  emit(evt, payload) {
    const arr = listeners[evt];
    if (arr) for (let i = 0; i < arr.length; i++) arr[i](payload);
  },

  setParams(p) {
    if (!p) return;
    const next = Object.assign({}, this.params, p);
    this.params = next;
    this.emit('params', next);
    if (mode === 'preview') {
      try {
        localStorage.setItem('vivarium.params.' + this.id, JSON.stringify(next));
      } catch (e) {
        /* storage can be unavailable; ignore */
      }
    }
  },

  canvas(id, opts) {
    const el = typeof id === 'string' ? document.getElementById(id) : id;
    if (!el) throw new Error('Vivarium.canvas: no element "' + id + '"');
    const view = makeView(el, opts);
    const self = this;
    const wrap = {
      ctx: view.ctx,
      get w() {
        self.size.w = view.w;
        return view.w;
      },
      get h() {
        self.size.h = view.h;
        return view.h;
      },
      get dpr() {
        self.size.dpr = view.dpr;
        return view.dpr;
      },
      onResize(fn) {
        view.onResize(fn);
        return wrap;
      },
    };
    return wrap;
  },

  loop(fn) {
    let raf = 0;
    let last = 0;
    let acc = 0;
    let t = 0;
    let running = true;
    const step = 1 / (this.fps || 30);
    const self = this;
    function tick(now) {
      if (!running) return;
      raf = requestAnimationFrame(tick);
      if (!last) last = now;
      const raw = (now - last) / 1000;
      last = now;
      if (self.paused || document.hidden) return;
      acc += Math.min(raw, 0.25);
      if (acc < step * 0.85) return;
      const dt = Math.min(acc, 0.1);
      acc = 0;
      t += dt;
      self.time = dayInfo();
      self.icons.step(dt);
      fn(dt, t);
    }
    raf = requestAnimationFrame(tick);
    return {
      stop() {
        running = false;
        cancelAnimationFrame(raf);
      },
    };
  },

  grain(ctx, w, h, alpha) {
    drawGrain(ctx, w, h, alpha);
  },

  vignette(ctx, w, h, strength) {
    drawVignette(ctx, w, h, strength);
  },

  bg(ctx, w, h, stops) {
    const g = ctx.createLinearGradient(0, 0, 0, h);
    for (let i = 0; i < stops.length; i++) g.addColorStop(i / (stops.length - 1), stops[i]);
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, w, h);
  },
};

/* Grain + vignette caches */
let grainTile = null;
function drawGrain(ctx, w, h, alpha) {
  if (!grainTile) {
    grainTile = document.createElement('canvas');
    grainTile.width = 160;
    grainTile.height = 160;
    const g = grainTile.getContext('2d');
    const img = g.createImageData(160, 160);
    const rnd = mulberry32(0x9e3779b9);
    for (let i = 0; i < img.data.length; i += 4) {
      const v = 90 + Math.floor(rnd() * 90);
      img.data[i] = v;
      img.data[i + 1] = v;
      img.data[i + 2] = v;
      img.data[i + 3] = Math.floor(rnd() * 70);
    }
    g.putImageData(img, 0, 0);
  }
  const pattern = ctx.createPattern(grainTile, 'repeat');
  const prev = ctx.globalAlpha;
  ctx.globalAlpha = alpha == null ? 0.05 : alpha;
  ctx.fillStyle = pattern;
  ctx.fillRect(0, 0, w, h);
  ctx.globalAlpha = prev;
}

function drawVignette(ctx, w, h, strength) {
  const s = strength == null ? 0.5 : strength;
  const g = ctx.createRadialGradient(w / 2, h / 2, Math.min(w, h) * 0.35, w / 2, h / 2, Math.max(w, h) * 0.75);
  g.addColorStop(0, 'rgba(0,0,0,0)');
  g.addColorStop(1, 'rgba(0,0,0,' + s + ')');
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, w, h);
}

/* ------------------------------------------------------------------ */
/* Boot                                                               */
/* ------------------------------------------------------------------ */
function applyDefaultsFromManifest() {
  return fetch(new URL('theme.json', document.baseURI), { cache: 'no-store' })
    .then(function (r) {
      return r.ok ? r.json() : null;
    })
    .then(function (manifest) {
      if (!manifest || !manifest.params) return {};
      const out = {};
      for (let i = 0; i < manifest.params.length; i++) {
        const p = manifest.params[i];
        if (p && p.key != null) out[p.key] = p.default;
      }
      return out;
    })
    .catch(function () {
      return {};
    });
}

function finishInit() {
  Vivarium.emit('init', Vivarium);
  if (readyResolve.fn) readyResolve.fn(Vivarium);
}

if (mode === 'preview') {
  const showIcons = params0.get('icons') !== '0';
  let defaults = {};
  const saved = (function () {
    try {
      return JSON.parse(localStorage.getItem('vivarium.params.' + themeId) || 'null') || {};
    } catch (e) {
      return {};
    }
  })();
  function layoutNow() {
    const w = window.innerWidth;
    const h = window.innerHeight;
    const icons = sampleLayout(w, h);
    Vivarium.icons.setRaw(icons);
    if (showIcons) renderSampleOverlay(icons);
  }
  applyDefaultsFromManifest().then(function (d) {
    defaults = d;
    Vivarium.params = Object.assign({}, defaults, saved);
    Vivarium.monitor = { index: 0, x: 0, y: 0, w: window.innerWidth, h: window.innerHeight };
    layoutNow();
    window.addEventListener('resize', layoutNow);
    finishInit();
  });
  window.addEventListener('message', function (e) {
    const d = e.data;
    if (!d || typeof d !== 'object') return;
    if (d.t === 'params') Vivarium.setParams(d.params);
    else if (d.t === 'paused') Vivarium.paused = true;
    else if (d.t === 'resumed') Vivarium.paused = false;
  });
} else {
  let booted = false;
  function bootFromInit(msg) {
    if (booted) return;
    booted = true;
    Vivarium.params = msg.params || {};
    Vivarium.fps = msg.fps || Vivarium.fps || 30;
    Vivarium.reduced = !!msg.reduced;
    Vivarium.media = msg.media || null;
    if (msg.monitor) Vivarium.monitor = msg.monitor;
    if (Array.isArray(msg.icons)) Vivarium.icons.setRaw(msg.icons);
    finishInit();
  }
  host.addEventListener('message', function (e) {
    const msg = typeof e.data === 'string' ? JSON.parse(e.data) : e.data;
    if (!msg || !msg.t) return;
    if (msg.t === 'init') bootFromInit(msg);
    else if (msg.t === 'icons') Vivarium.icons.setRaw(msg.icons || []);
    else if (msg.t === 'params') Vivarium.setParams(msg.params);
    else if (msg.t === 'paused') Vivarium.paused = true;
    else if (msg.t === 'resumed') Vivarium.paused = false;
    else if (msg.t === 'media') {
      Vivarium.media = msg.media;
      Vivarium.emit('media', msg.media);
    }
  });
  document.addEventListener('visibilitychange', function () {
    if (!document.hidden) Vivarium.emit('resume', null);
    else Vivarium.emit('pause', null);
  });
  try {
    host.postMessage({ t: 'hello', id: themeId });
  } catch (e) {
    /* bridge unavailable; fall back below */
  }
  setTimeout(function () {
    if (!booted) {
      Vivarium.params = {};
      finishInit();
    }
  }, 4000);
}

export default Vivarium;
