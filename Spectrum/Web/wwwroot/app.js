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
  const connectionEl = document.getElementById("connection");
  function setStatus(msg, isError, isPending) {
    if (!statusEl) return;
    statusEl.textContent = msg;
    statusEl.className = "status-message " +
      (isError ? "error" : isPending ? "pending" : "success");
  }

  function setConnection(label, state) {
    if (!connectionEl) return;
    connectionEl.textContent = label;
    connectionEl.className = `connection-badge ${state}`;
  }

  function fmt(v) {
    return typeof v === "number" ? (Number.isInteger(v) ? v : v.toFixed(3)) : v;
  }

  function humanize(key) {
    return String(key || "Control")
      .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
      .replace(/[_-]+/g, " ")
      .replace(/^./, (s) => s.toUpperCase());
  }

  function formatValue(p, value) {
    if (p.unit === "%" && typeof value === "number") {
      return `${Math.round(value * 100)}%`;
    }
    return `${fmt(value)}${p.unit ? ` ${p.unit}` : ""}`;
  }

  function setControlState(el, state, message) {
    if (!el) return;
    el.className = `control-state ${state || ""}`.trim();
    el.textContent = message || "";
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
    if (window.spectrumCalibrationPageHide) {
      window.spectrumCalibrationPageHide();
    }
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
  async function putValue(
    key, value, lockResource, feedbackEl, control, displayLabel
  ) {
    const headers = { "Content-Type": "application/json" };
    const name = displayLabel || humanize(key);
    setControlState(feedbackEl, "pending", "Applying…");
    if (control) control.disabled = true;
    if (lockResource) {
      const token = await ensureLock(lockResource);
      if (!token) {
        setControlState(feedbackEl, "error", "Could not acquire the required lock. Try again.");
        if (control) control.disabled = false;
        return null;
      }
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
        const detail = body.error || `request failed (${res.status})`;
        setStatus(`${name}: ${detail}. Retry the control.`, true);
        setControlState(feedbackEl, "error", `${detail}. Retry.`);
        return null;
      }
      const body = await res.json();
      setStatus(`${name} applied.`);
      setControlState(feedbackEl, "success", "Applied");
      return body.value;
    } catch (e) {
      setStatus(`${name}: connection failed. Retry the control.`, true);
      setControlState(feedbackEl, "error", "Connection failed. Retry.");
      return null;
    } finally {
      if (control) control.disabled = false;
    }
  }

  function renderParam(p, labelText) {
    const wrap = document.createElement("div");
    wrap.className = "param";
    wrap.title = `Configuration key: ${p.key}`;

    const label = document.createElement("label");
    const displayLabel = labelText || p.label || humanize(p.key);
    label.textContent = displayLabel;
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

    if (p.description) {
      const description = document.createElement("p");
      description.className = "param-description";
      description.textContent = p.description;
      wrap.appendChild(description);
    }

    const feedback = document.createElement("div");
    feedback.className = "control-state";
    feedback.setAttribute("aria-live", "polite");

    const inputId = `control-${p.key.replace(/[^a-z0-9_-]/gi, "-")}`;

    if (p.type === "double" || p.type === "int") {
      const input = document.createElement("input");
      input.type = "range";
      input.id = inputId;
      label.htmlFor = inputId;
      input.min = p.min;
      input.max = p.max;
      input.step = p.type === "int" ? 1 : (p.max - p.min) / 1000 || 0.001;
      input.value = p.value;
      input.dataset.appliedValue = String(p.value);
      valSpan.textContent = formatValue(p, p.value);
      input.addEventListener("input", () => {
        valSpan.textContent = formatValue(p, parseFloat(input.value));
      });
      input.addEventListener("change", async () => {
        const num = p.type === "int" ? parseInt(input.value, 10) : parseFloat(input.value);
        const applied = await putValue(
          p.key, num, p.lock, feedback, input, displayLabel);
        if (applied != null) {
          input.value = applied;
          input.dataset.appliedValue = String(applied);
          valSpan.textContent = formatValue(p, applied);
        } else {
          input.value = input.dataset.appliedValue;
          valSpan.textContent = formatValue(
            p, parseFloat(input.dataset.appliedValue));
        }
      });
      // Push from another client: move the slider unless the user is actively
      // dragging this one (don't yank the thumb out from under them).
      controlSetters[p.key] = (v) => {
        if (document.activeElement !== input) {
          input.value = v; valSpan.textContent = formatValue(p, v);
          input.dataset.appliedValue = String(v);
        }
      };
      wrap.appendChild(input);
    } else if (p.type === "bool") {
      valSpan.remove();
      const input = document.createElement("input");
      input.type = "checkbox";
      input.id = inputId;
      label.htmlFor = inputId;
      input.checked = p.value;
      input.addEventListener("change", async () => {
        const requested = input.checked;
        const applied = await putValue(
          p.key, requested, p.lock, feedback, input, displayLabel);
        if (applied == null) input.checked = !requested;
        else input.checked = Boolean(applied);
      });
      controlSetters[p.key] = (v) => { input.checked = v; };
      label.insertBefore(input, label.firstChild);
    } else if (p.type === "enum") {
      valSpan.remove();
      const select = document.createElement("select");
      select.id = inputId;
      label.htmlFor = inputId;
      (p.options || []).forEach((opt, i) => {
        const o = document.createElement("option");
        o.value = i; o.textContent = opt;
        if (i === p.value) o.selected = true;
        select.appendChild(o);
      });
      select.dataset.appliedValue = String(p.value);
      select.addEventListener("change", async () => {
        const previous = select.dataset.appliedValue;
        const applied = await putValue(
          p.key, parseInt(select.value, 10), p.lock,
          feedback, select, displayLabel);
        select.value = applied == null ? previous : applied;
        if (applied != null) select.dataset.appliedValue = String(applied);
      });
      controlSetters[p.key] = (v) => {
        select.value = v;
        select.dataset.appliedValue = String(v);
      };
      wrap.appendChild(select);
    } else { // string
      valSpan.remove();
      const input = document.createElement("input");
      input.type = "text";
      input.id = inputId;
      label.htmlFor = inputId;
      input.value = p.value || "";
      input.dataset.appliedValue = input.value;
      input.addEventListener("change", async () => {
        const previous = input.dataset.appliedValue;
        const applied = await putValue(
          p.key, input.value, p.lock, feedback, input, displayLabel);
        input.value = applied == null ? previous : applied;
        if (applied != null) input.dataset.appliedValue = String(applied);
      });
      controlSetters[p.key] = (v) => {
        if (document.activeElement !== input) input.value = v;
        input.dataset.appliedValue = String(v ?? "");
      };
      wrap.appendChild(input);
    }
    wrap.appendChild(feedback);
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
    { id: "brightness", title: "Brightness", controls: [
      { key: "domeBrightness", label: "Brightness" },
      { key: "domeMaxBrightness", label: "Maximum brightness" },
    ] },
    { id: "tempo", title: "BPM source", controls: [
      { key: "beatInput", label: "Tempo source" },
    ], extra: appendTapTempo },
    { id: "testpatterns", title: "Test patterns", controls: [
      { key: "domeTestPattern", label: "Dome test pattern" },
    ] },
    { id: "devices", title: "Devices and windows", controls: [
      { key: "domeEnabled" },
      { key: "midiInputEnabled" },
      { key: "vjHUDEnabled" },
      { key: "domeSimulationEnabled" },
      { key: "domeBeagleboneOPCAddress" },
    ] },
    { id: "advanced", title: "Advanced", controls: [
      { key: "midiInputInSeparateThread" },
      { key: "domeOutputInSeparateThread" },
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
      if (present.length === 0) {
        el.hidden = true;
        return;
      }
      el.hidden = false;
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
        operatorBtn.className = "pending";
        operatorBtn.textContent = target ? "Starting engine" : "Stopping engine";
        operatorBtn.setAttribute("aria-busy", "true");
        setStatus(target ? "Starting engine…" : "Stopping engine…", false, true);
        try {
          const res = await fetch("/api/operator", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ enabled: target }),
          });
          if (!res.ok) {
            setStatus(`Engine action failed (${res.status}). Press the engine control to retry.`, true);
            return;
          }
          const body = await res.json();
          applyOperator(body.enabled); // SSE confirms too; this is immediate
          setStatus(body.enabled ? "Engine started." : "Engine stopped.");
        } catch (e) {
          setStatus("Engine action could not reach Spectrum. Press the engine control to retry.", true);
        } finally {
          operatorBtn.removeAttribute("aria-busy");
          operatorBtn.disabled = false;
          renderOperator();
        }
      });
      operatorEl.appendChild(operatorBtn);
    }
    if (operatorEnabled == null) {
      operatorBtn.textContent = "Loading engine state";
      operatorBtn.className = "pending";
      operatorBtn.disabled = true;
      return;
    }
    operatorBtn.disabled = false;
    operatorBtn.textContent = operatorEnabled ? "Stop engine" : "Start engine";
    operatorBtn.className = operatorEnabled ? "running" : "stopped";
    operatorBtn.setAttribute("aria-pressed", String(operatorEnabled));
  }

  function applyOperator(enabled) {
    operatorEnabled = enabled;
    renderOperator();
  }

  // ---- Read-only telemetry --------------------------------------------------

  const telemetryEl = document.getElementById("telemetry");
  const telemetryRows = {};
  const TELEMETRY_LABELS = {
    operatorFPS: { label: "Engine", unit: "FPS" },
    domeOPCFPS: { label: "Dome output", unit: "FPS" },
    bpm: { label: "Tempo", unit: "BPM" },
  };
  function applyTelemetry(key, value) {
    if (!telemetryEl) return;
    let row = telemetryRows[key];
    if (!row) {
      row = document.createElement("span");
      row.className = "telemetry-item";
      const label = document.createElement("span");
      label.className = "telemetry-label";
      const number = document.createElement("span");
      number.className = "telemetry-value";
      row.appendChild(label);
      row.appendChild(number);
      telemetryEl.appendChild(row);
      telemetryRows[key] = row;
    }
    const meta = TELEMETRY_LABELS[key] || { label: humanize(key), unit: "" };
    row.querySelector(".telemetry-label").textContent = meta.label;
    const raw = String(value ?? "—");
    const alreadyHasUnit = meta.unit && raw.toUpperCase().includes(meta.unit);
    row.querySelector(".telemetry-value").textContent =
      `${raw}${meta.unit && !alreadyHasUnit ? ` ${meta.unit}` : ""}`;
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
    eventSource.onerror = () => setConnection("Reconnecting", "error");
    eventSource.onopen = () => {
      setConnection("Live", "live");
      if (statusEl && statusEl.classList.contains("pending")) {
        setStatus("Controls are live.");
      }
    };
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
        setStatus(`${params.length} controls loaded.`, false, true);
      } else {
        setStatus("Maintenance controls loaded.", false, true);
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
