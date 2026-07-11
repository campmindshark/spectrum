// Shared front-end for the Spectrum web control surface. Renders the parameter
// list returned by the REST API as appropriate controls and PUTs edits back,
// shows live read-only telemetry (FPS/BPM), and — on the maintenance surface —
// acquires the advisory lease a modal parameter (test patterns) needs before
// writing it.
//
// The page sets window.SPECTRUM_SCOPE to "user" or "maintenance" before loading
// this script; that decides which API base path to use. There is no auth — the
// two scopes differ only in which parameters they expose.

(function () {
  const scope = window.SPECTRUM_SCOPE || "user";
  const apiBase = scope === "maintenance"
    ? "/api/maintenance/parameters"
    : "/api/parameters";
  // A human-ish id so other clients' "locked by …" messages mean something.
  const holderName = "web-" + Math.random().toString(36).slice(2, 7);

  const statusEl = document.getElementById("status");
  function setStatus(msg, isError) {
    statusEl.textContent = msg;
    statusEl.style.color = isError ? "#e66" : "#6c6";
  }

  function fmt(v) {
    return typeof v === "number" ? (Number.isInteger(v) ? v : v.toFixed(3)) : v;
  }

  // ---- Advisory locks (maintenance only; exposed for calibration.js) --------

  const heldLocks = {}; // resource -> { token, timer }

  async function ensureLock(resource) {
    if (heldLocks[resource]) return heldLocks[resource].token;
    try {
      const res = await fetch(`/api/maintenance/locks/${encodeURIComponent(resource)}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ holderName }),
      });
      if (res.status === 423) {
        const body = await res.json().catch(() => ({}));
        setStatus(`${resource} is locked by ${body.holder?.holderName || "someone"}`, true);
        return null;
      }
      if (!res.ok) { setStatus(`lock ${resource}: ${res.status}`, true); return null; }
      const body = await res.json();
      // Renew well inside the server's 15s TTL so a held lease never lapses
      // mid-edit.
      const timer = setInterval(() => heartbeat(resource), 5000);
      heldLocks[resource] = { token: body.token, timer };
      renderLocks();
      return body.token;
    } catch (e) {
      setStatus(`lock ${resource}: ${e}`, true);
      return null;
    }
  }

  async function heartbeat(resource) {
    const held = heldLocks[resource];
    if (!held) return;
    try {
      const res = await fetch(
        `/api/maintenance/locks/${encodeURIComponent(resource)}/heartbeat`,
        { method: "POST", headers: { "X-Spectrum-Lock-Token": held.token } });
      if (!res.ok) forgetLock(resource); // lost the lease
    } catch (_) { /* transient; next tick retries */ }
  }

  async function releaseLock(resource) {
    const held = heldLocks[resource];
    if (!held) return;
    try {
      await fetch(`/api/maintenance/locks/${encodeURIComponent(resource)}`, {
        method: "DELETE", headers: { "X-Spectrum-Lock-Token": held.token },
      });
    } catch (_) { /* ignore; TTL will reap it */ }
    forgetLock(resource);
  }

  function forgetLock(resource) {
    const held = heldLocks[resource];
    if (held) { clearInterval(held.timer); delete heldLocks[resource]; renderLocks(); }
  }

  function lockToken(resource) {
    return heldLocks[resource] ? heldLocks[resource].token : null;
  }

  const locksEl = document.getElementById("locks");
  let foreignLocks = [];
  function renderLocks() {
    if (!locksEl) return;
    locksEl.innerHTML = "";
    const mine = new Set(Object.keys(heldLocks));
    const rows = [];
    mine.forEach((r) => rows.push({ resource: r, holderName: "you", mine: true }));
    foreignLocks.forEach((l) => { if (!mine.has(l.resource)) rows.push(l); });
    if (rows.length === 0) return;
    const h = document.createElement("div");
    h.className = "locks-title";
    h.textContent = "Active locks";
    locksEl.appendChild(h);
    rows.forEach((l) => {
      const row = document.createElement("div");
      row.className = "lock-row";
      row.textContent = `${l.resource} — ${l.mine ? "held by you" : "held by " + (l.holderName || "someone")}`;
      if (l.mine) {
        const btn = document.createElement("button");
        btn.textContent = "Release";
        btn.style.marginLeft = "0.5rem";
        btn.addEventListener("click", () => releaseLock(l.resource));
        row.appendChild(btn);
      }
      locksEl.appendChild(row);
    });
  }

  async function pollForeignLocks() {
    if (!locksEl) return;
    try {
      const res = await fetch("/api/maintenance/locks");
      if (res.ok) { foreignLocks = await res.json(); renderLocks(); }
    } catch (_) { /* ignore */ }
  }

  // Release everything we hold when the tab goes away, so a device that walks
  // off doesn't hold a lease for the full TTL. keepalive lets the DELETE outlive
  // the page (sendBeacon can't set the lock-token header, so it's not usable
  // here); if the browser drops it anyway the lease just lapses on its TTL.
  window.addEventListener("pagehide", () => {
    for (const resource of Object.keys(heldLocks)) {
      fetch(`/api/maintenance/locks/${encodeURIComponent(resource)}`, {
        method: "DELETE",
        headers: { "X-Spectrum-Lock-Token": heldLocks[resource].token },
        keepalive: true,
      }).catch(() => {});
    }
  });

  window.spectrumLocks = { ensureLock, releaseLock, lockToken, heldLocks };

  // ---- Parameter writes -----------------------------------------------------

  // Writes value to key. If lockResource is set (a modal parameter), acquire the
  // lease first and stamp the token on the request; abort if the lease can't be
  // had (someone else holds it).
  async function putValue(key, value, lockResource) {
    const headers = { "Content-Type": "application/json" };
    if (lockResource) {
      const token = await ensureLock(lockResource);
      if (!token) return null;
      headers["X-Spectrum-Lock-Token"] = token;
    }
    try {
      const res = await fetch(`${apiBase}/${encodeURIComponent(key)}`, {
        method: "PUT",
        headers,
        body: JSON.stringify({ value }),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setStatus(`${key}: ${body.error || res.status}`, true);
        return null;
      }
      const body = await res.json();
      setStatus(`${key} = ${fmt(body.value)}`);
      return body.value;
    } catch (e) {
      setStatus(`${key}: ${e}`, true);
      return null;
    }
  }

  function renderParam(p, labelText) {
    const wrap = document.createElement("div");
    wrap.className = "param";

    const label = document.createElement("label");
    label.textContent = labelText || p.key;
    if (p.lock) {
      const tag = document.createElement("span");
      tag.className = "lock-tag";
      tag.textContent = " 🔒";
      tag.title = "acquires the " + p.lock + " lock when changed";
      label.appendChild(tag);
    }
    const valSpan = document.createElement("span");
    valSpan.className = "val";
    label.appendChild(valSpan);
    wrap.appendChild(label);

    if (p.type === "double" || p.type === "int") {
      const input = document.createElement("input");
      input.type = "range";
      input.min = p.min;
      input.max = p.max;
      input.step = p.type === "int" ? 1 : (p.max - p.min) / 1000 || 0.001;
      input.value = p.value;
      valSpan.textContent = fmt(p.value);
      input.addEventListener("input", () => {
        valSpan.textContent = fmt(parseFloat(input.value));
      });
      input.addEventListener("change", async () => {
        const num = p.type === "int" ? parseInt(input.value, 10) : parseFloat(input.value);
        const applied = await putValue(p.key, num, p.lock);
        if (applied != null) { input.value = applied; valSpan.textContent = fmt(applied); }
      });
      // Push from another client: move the slider unless the user is actively
      // dragging this one (don't yank the thumb out from under them).
      controlSetters[p.key] = (v) => {
        if (document.activeElement !== input) {
          input.value = v; valSpan.textContent = fmt(v);
        }
      };
      wrap.appendChild(input);
    } else if (p.type === "bool") {
      valSpan.remove();
      const input = document.createElement("input");
      input.type = "checkbox";
      input.checked = p.value;
      input.addEventListener("change", () => putValue(p.key, input.checked, p.lock));
      controlSetters[p.key] = (v) => { input.checked = v; };
      label.insertBefore(input, label.firstChild);
      input.style.marginRight = "0.5rem";
    } else if (p.type === "enum") {
      valSpan.remove();
      const select = document.createElement("select");
      (p.options || []).forEach((opt, i) => {
        const o = document.createElement("option");
        o.value = i; o.textContent = opt;
        if (i === p.value) o.selected = true;
        select.appendChild(o);
      });
      select.addEventListener("change", () => putValue(p.key, parseInt(select.value, 10), p.lock));
      controlSetters[p.key] = (v) => { select.value = v; };
      wrap.appendChild(select);
    } else { // string
      valSpan.remove();
      const input = document.createElement("input");
      input.type = "text";
      input.value = p.value || "";
      input.style.width = "100%";
      input.addEventListener("change", () => putValue(p.key, input.value, p.lock));
      controlSetters[p.key] = (v) => {
        if (document.activeElement !== input) input.value = v;
      };
      wrap.appendChild(input);
    }
    return wrap;
  }

  // Controls that other clients might change while we're open. Updating the
  // matching input in place keeps two phones coherent under last-write-wins.
  const controlSetters = {};

  function applyPush(key, value) {
    const setter = controlSetters[key];
    if (setter) setter(value);
  }

  // ---- Curated sections -----------------------------------------------------

  // Pages that don't render the flat #params list (the maintenance surface) can
  // still opt individual parameters into their own labeled <section> by id. The
  // dome test pattern is a modal override — it suspends the running visual until
  // set back to "None" — so it gets a dedicated section rather than living in a
  // long flat list. Reusing renderParam keeps its 🔒 advisory-lock handling and
  // cross-client live push working exactly as on the user page.
  const CURATED_SECTIONS = [
    { id: "tempo", title: "BPM source", controls: [
      { key: "beatInput", label: "Source" },
    ], extra: appendTapTempo },
    { id: "testpatterns", title: "Test patterns", controls: [
      { key: "domeTestPattern", label: "Dome" },
    ] },
  ];

  function renderCuratedSections(params) {
    const byKey = {};
    params.forEach((p) => { byKey[p.key] = p; });
    CURATED_SECTIONS.forEach((section) => {
      const el = document.getElementById(section.id);
      if (!el) return;
      el.innerHTML = "";
      const present = section.controls.filter((c) => byKey[c.key]);
      if (present.length === 0) return;
      const h = document.createElement("h2");
      h.textContent = section.title;
      el.appendChild(h);
      present.forEach((c) => el.appendChild(renderParam(byKey[c.key], c.label)));
      if (section.extra) section.extra(el, byKey);
    });
  }

  // ---- Human tap tempo (maintenance) ----------------------------------------

  // The native VJ HUD times taps on the server (BeatBroadcaster.AddTap); we time
  // them in the browser so request latency doesn't skew the intervals, then POST
  // the computed BPM to /api/maintenance/tempo/tap (which also flips the source
  // to Human, matching the native Tap button). The button is only actionable
  // when the "Human" source is selected; its enabled state tracks beatInput live
  // via the wrapped control setter, so picking another source here or on another
  // client greys it out.
  const TAP_CONCLUSION_MS = 2000; // matches BeatBroadcaster's tap timeout
  const TAP_MIN_TAPS = 3;         // needs >=3 taps (2 intervals) to average

  function appendTapTempo(container, byKey) {
    const p = byKey.beatInput;
    if (!p) return;
    let source = p.value; // 0 == Human
    let taps = [];
    let resetTimer = null;

    const row = document.createElement("div");
    row.className = "param";
    const btn = document.createElement("button");
    btn.textContent = "Tap";
    const info = document.createElement("span");
    info.style.marginLeft = "0.5rem";
    info.style.fontSize = "0.8rem";
    info.style.color = "#cba";
    row.appendChild(btn);
    row.appendChild(info);
    container.appendChild(row);

    function syncEnabled() {
      const human = source === 0;
      btn.disabled = !human;
      if (!human) { taps = []; btn.textContent = "Tap"; info.textContent = "select Human to tap"; }
      else if (taps.length === 0) info.textContent = "";
    }

    // Keep source in sync with the dropdown (local edits round-trip via SSE, and
    // other clients' changes arrive the same way), then re-derive enabled state.
    const prevSetter = controlSetters.beatInput;
    controlSetters.beatInput = (v) => {
      if (prevSetter) prevSetter(v);
      source = v;
      syncEnabled();
    };

    btn.addEventListener("click", async () => {
      const now = performance.now();
      if (taps.length && now - taps[taps.length - 1] > TAP_CONCLUSION_MS) taps = [];
      taps.push(now);
      clearTimeout(resetTimer);
      resetTimer = setTimeout(() => {
        taps = []; btn.textContent = "Tap"; info.textContent = "";
      }, TAP_CONCLUSION_MS);
      btn.textContent = String(taps.length);

      if (taps.length < TAP_MIN_TAPS) { info.textContent = "keep tapping…"; return; }
      let sum = 0;
      for (let i = 1; i < taps.length; i++) sum += taps[i] - taps[i - 1];
      const bpm = 60000 / (sum / (taps.length - 1));
      info.textContent = `${Math.round(bpm)} BPM`;
      try {
        const res = await fetch("/api/maintenance/tempo/tap", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ bpm }),
        });
        if (!res.ok) {
          const b = await res.json().catch(() => ({}));
          setStatus(`tap: ${b.error || res.status}`, true);
        } else {
          setStatus(`tap ≈ ${Math.round(bpm)} BPM`);
        }
      } catch (e) {
        setStatus(`tap: ${e}`, true);
      }
    });

    syncEnabled();
  }

  // ---- Global engine on/off (Start/Stop) ------------------------------------

  // Rendered only where a #operator container exists (the user page). Reflects
  // the engine's live Enabled state — seeded from GET /api/operator on load and
  // kept coherent with the native power button and other clients via the SSE
  // "operator" frame. The button sends the explicit desired state (not a
  // toggle) so a stale press can't flip the engine the wrong way.
  const operatorEl = document.getElementById("operator");
  let operatorBtn = null;
  let operatorEnabled = null;

  function renderOperator() {
    if (!operatorEl) return;
    if (!operatorBtn) {
      operatorBtn = document.createElement("button");
      operatorBtn.addEventListener("click", async () => {
        if (operatorEnabled == null) return;
        const target = !operatorEnabled;
        operatorBtn.disabled = true;
        try {
          const res = await fetch("/api/operator", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ enabled: target }),
          });
          if (!res.ok) { setStatus(`operator: ${res.status}`, true); return; }
          const body = await res.json();
          applyOperator(body.enabled); // SSE confirms too; this is immediate
        } catch (e) {
          setStatus(`operator: ${e}`, true);
        } finally {
          operatorBtn.disabled = false;
        }
      });
      operatorEl.appendChild(operatorBtn);
    }
    if (operatorEnabled == null) {
      operatorBtn.textContent = "…";
      operatorBtn.className = "";
      operatorBtn.disabled = true;
      return;
    }
    operatorBtn.disabled = false;
    operatorBtn.textContent = operatorEnabled ? "Stop" : "Start";
    operatorBtn.className = operatorEnabled ? "running" : "stopped";
  }

  function applyOperator(enabled) {
    operatorEnabled = enabled;
    renderOperator();
  }

  // ---- Read-only telemetry --------------------------------------------------

  const telemetryEl = document.getElementById("telemetry");
  const telemetryRows = {};
  const TELEMETRY_LABELS = {
    operatorFPS: "Engine FPS", domeOPCFPS: "Dome OPC",
    bpm: "BPM",
  };
  function applyTelemetry(key, value) {
    if (!telemetryEl) return;
    let row = telemetryRows[key];
    if (!row) {
      row = document.createElement("span");
      row.className = "telemetry-item";
      telemetryEl.appendChild(row);
      telemetryRows[key] = row;
    }
    row.textContent = `${TELEMETRY_LABELS[key] || key}: ${value}`;
  }

  // ---- Change feed ----------------------------------------------------------

  let eventSource = null;
  function openEventStream() {
    if (eventSource) eventSource.close();
    const url = scope === "maintenance" ? "/api/maintenance/events" : "/api/events";
    eventSource = new EventSource(url);
    eventSource.onmessage = (ev) => {
      try {
        const frame = JSON.parse(ev.data);
        if (frame.kind === "telemetry") {
          applyTelemetry(frame.key, frame.value);
        } else if (frame.kind === "operator") {
          applyOperator(frame.value);
        } else if (frame.kind === "layers") {
          if (window.spectrumApplyLayers) window.spectrumApplyLayers(frame.value);
        } else if (frame.kind === "scenes") {
          if (window.spectrumApplyScenes) window.spectrumApplyScenes(frame.value);
        } else if (frame.kind === "palette") {
          if (window.spectrumApplyPalette) window.spectrumApplyPalette(frame.value);
        } else if (frame.kind === "palettes") {
          if (window.spectrumApplyPalettes) window.spectrumApplyPalettes(frame.value);
        } else {
          applyPush(frame.key, frame.value);
        }
      } catch (_) { /* ignore malformed frames */ }
    };
    eventSource.onerror = () => setStatus("live connection lost, retrying…", true);
    eventSource.onopen = () => setStatus("live");
  }

  async function load() {
    // Show the Start/Stop button immediately (disabled placeholder); the SSE
    // stream seeds its real state via the initial "operator" frame.
    renderOperator();
    try {
      const res = await fetch(apiBase);
      if (!res.ok) { setStatus(`load failed: ${res.status}`, true); return; }
      const params = await res.json();
      const container = document.getElementById("params");
      for (const key of Object.keys(controlSetters)) delete controlSetters[key];
      if (container) {
        container.innerHTML = "";
        params.forEach((p) => container.appendChild(renderParam(p)));
        setStatus(`${params.length} controls loaded`);
      } else {
        setStatus("live");
      }
      renderCuratedSections(params);
      openEventStream();
      if (locksEl) { pollForeignLocks(); setInterval(pollForeignLocks, 3000); }
      if (window.spectrumCalibrationInit) window.spectrumCalibrationInit();
      if (window.spectrumWandsInit) window.spectrumWandsInit();
      if (window.spectrumLayersInit) window.spectrumLayersInit();
      if (window.spectrumScenesInit) window.spectrumScenesInit();
      if (window.spectrumPaletteInit) window.spectrumPaletteInit();
    } catch (e) {
      setStatus(`load failed: ${e}`, true);
    }
  }

  window.spectrumReload = load;
  window.spectrumStatus = setStatus;
  load();
})();
