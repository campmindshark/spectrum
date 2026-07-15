// Dome-mapping calibration UI for the maintenance surface. Drives the
// server-side flow in DomeCalibrationController: light one controller cable at a
// time, let the operator report which physical endpoint lit, then save the
// discovered permutation to the dome cable mapping.
//
// This is a modal op: the flow is guarded by the "domeCalibration" advisory
// lease, which we acquire (via the shared lock module in app.js) on Start and
// release on Cancel or when leaving the page. Every action carries the lease
// token; the server refuses the flow to anyone who doesn't hold it.

(function () {
  const RESOURCE = "domeCalibration";
  const SVG_NS = "http://www.w3.org/2000/svg";
  const container = document.getElementById("calibration");
  if (!container) return;

  // The static dome-diagram geometry (projected strut segments + label/color per
  // endpoint), fetched once. Same layout as the native DomeMappingWindow canvas.
  let geometry = null;
  async function loadGeometry() {
    if (geometry) return geometry;
    try {
      const res = await fetch("/api/maintenance/calibration/geometry");
      if (res.ok) geometry = await res.json();
    } catch (_) { /* diagram just won't render; buttons below still work */ }
    return geometry;
  }

  function status(msg, isError) {
    if (window.spectrumStatus) window.spectrumStatus(msg, isError);
  }

  function token() {
    return window.spectrumLocks ? window.spectrumLocks.lockToken(RESOURCE) : null;
  }

  async function call(action, body) {
    const headers = {};
    const t = token();
    if (t) headers["X-Spectrum-Lock-Token"] = t;
    if (body) headers["Content-Type"] = "application/json";
    const res = await fetch(`/api/maintenance/calibration/${action}`, {
      method: "POST",
      headers,
      body: body ? JSON.stringify(body) : undefined,
    });
    const payload = await res.json().catch(() => ({}));
    if (!res.ok) {
      status(`calibration ${action}: ${payload.error || res.status}`, true);
      // A save failure still returns the state so the user sees why.
      return payload.state || null;
    }
    return payload;
  }

  async function start() {
    const t = await window.spectrumLocks.ensureLock(RESOURCE);
    if (!t) return; // someone else is calibrating
    render(await call("start"));
  }

  // Load the saved mapping for review/edit (swap/save) without lighting the dome.
  async function edit() {
    const t = await window.spectrumLocks.ensureLock(RESOURCE);
    if (!t) return; // someone else holds the flow
    render(await call("load"));
  }

  async function pick(endpoint) { render(await call("pick", { endpoint })); }
  async function skip() { render(await call("skip")); }
  async function back() { render(await call("back")); }
  async function restart() { render(await call("restart")); }
  async function swap(a, b) { render(await call("swap", { a, b })); }

  // The two cables selected in the swap dropdowns, persisted across re-renders
  // so repeated swaps don't reset the selection.
  let swapA = 0, swapB = 1;

  function validPortMapping(mapping, count) {
    return Array.isArray(mapping)
      && mapping.length === count
      && mapping.every((value) => Number.isInteger(value)
        && value >= 0 && value < count)
      && new Set(mapping).size === count;
  }

  async function savePortMapping(mapping, keepLock) {
    const alreadyHeld = !!token();
    const t = await window.spectrumLocks.ensureLock(RESOURCE);
    if (!t) return;
    try {
      const res = await fetch("/api/maintenance/calibration/ports", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-Spectrum-Lock-Token": t,
        },
        body: JSON.stringify({ mapping }),
      });
      const payload = await res.json().catch(() => ({}));
      if (!res.ok) {
        status(`plug order: ${payload.error || res.status}`, true);
        if (payload.state) render(payload.state);
        return;
      }
      // Port halves determine the colored endpoint regions, so refetch the
      // server geometry after a successful save.
      geometry = null;
      await loadGeometry();
      render(payload);
      status("Dome plug order saved.");
    } finally {
      if (!alreadyHeld && !keepLock) {
        await window.spectrumLocks.releaseLock(RESOURCE);
      }
    }
  }

  async function save() {
    const state = await call("save");
    if (state) { render(state); if (state.complete) status("Dome mapping saved."); }
  }

  async function cancel() {
    const state = await call("cancel");
    await window.spectrumLocks.releaseLock(RESOURCE);
    render(state);
  }

  function button(text, handler, enabled) {
    const b = document.createElement("button");
    b.textContent = text;
    b.disabled = enabled === false;
    b.addEventListener("click", handler);
    return b;
  }

  function buildPortMappingEditor(state, engaged) {
    const count = state.numPorts || 8;
    const initial = validPortMapping(state.portMapping, count)
      ? state.portMapping
      : Array.from({ length: count }, (_, port) => port);
    const wrap = document.createElement("div");
    wrap.className = "calib-port-map";

    const title = document.createElement("h3");
    title.textContent = "LED strip plugs";
    wrap.appendChild(title);
    const help = document.createElement("p");
    help.textContent = "For each physical output port, choose the numbered "
      + "strip cable/path plugged into it. Use each once; this applies to all five boxes.";
    wrap.appendChild(help);

    const grid = document.createElement("div");
    grid.className = "calib-port-grid";
    const selects = [];
    for (let port = 0; port < count; port++) {
      const label = document.createElement("label");
      label.textContent = `Port ${port + 1}`;
      const select = document.createElement("select");
      for (let path = 0; path < count; path++) {
        select.appendChild(new Option(`Cable/path ${path + 1}`, path));
      }
      select.value = String(initial[port]);
      label.appendChild(select);
      grid.appendChild(label);
      selects.push(select);
    }
    wrap.appendChild(grid);

    const actions = document.createElement("div");
    actions.className = "calib-port-actions";
    const feedback = document.createElement("span");
    feedback.className = "calib-port-feedback";
    const saveBtn = button("Save plug order", () => {
      const mapping = selects.map((select) => +select.value);
      savePortMapping(mapping, engaged);
    });
    const refresh = () => {
      const mapping = selects.map((select) => +select.value);
      const valid = validPortMapping(mapping, count);
      saveBtn.disabled = !valid;
      feedback.textContent = valid
        ? ""
        : "Each cable/path must be selected exactly once.";
    };
    selects.forEach((select) => select.addEventListener("change", refresh));
    refresh();
    actions.appendChild(saveBtn);
    actions.appendChild(feedback);
    wrap.appendChild(actions);
    return wrap;
  }

  // Builds the clickable dome diagram from the cached geometry, styled to the
  // flow state: endpoints already assigned are dimmed (as in the native
  // diagram), and endpoints are only clickable while a cable is actively
  // lighting. Returns null if the geometry hasn't loaded.
  function buildDiagram(state) {
    if (!geometry) return null;
    const pickable = !!(state && state.active && !state.done);
    const assigned = new Set();
    if (state) {
      for (let cable = 0; cable < state.currentStep; cable++) {
        if (state.picks[cable] >= 0) assigned.add(state.picks[cable]);
      }
    }

    const svg = document.createElementNS(SVG_NS, "svg");
    svg.setAttribute("class", "calib-diagram");
    svg.setAttribute("viewBox", `0 0 ${geometry.viewSize} ${geometry.viewSize}`);

    geometry.endpoints.forEach((ep) => {
      // Already-assigned endpoints are never re-pickable: the mapping is a
      // bijection, so each endpoint can back exactly one cable. Only unassigned
      // endpoints stay clickable while a cable is lighting.
      const taken = assigned.has(ep.endpoint);
      const epPickable = pickable && !taken;

      const g = document.createElementNS(SVG_NS, "g");
      g.setAttribute("class", "calib-ep" + (taken ? " assigned" : ""));

      // The colored struts are purely visual (identify the endpoint) — not the
      // click target, matching the native diagram where only the label is a
      // button.
      ep.segments.forEach((s) => {
        const seg = document.createElementNS(SVG_NS, "line");
        seg.setAttribute("class", "calib-line");
        seg.setAttribute("stroke", ep.color);
        seg.setAttribute("x1", s[0]); seg.setAttribute("y1", s[1]);
        seg.setAttribute("x2", s[2]); seg.setAttribute("y2", s[3]);
        g.appendChild(seg);
      });

      // The label is the pick target, drawn as an obvious square-cornered
      // button centered on the endpoint's centroid.
      const btn = document.createElementNS(SVG_NS, "g");
      btn.setAttribute("class", "calib-label-btn" + (epPickable ? " pickable" : ""));
      if (epPickable) btn.addEventListener("click", () => pick(ep.endpoint));

      const W = 46, H = 24;
      const bg = document.createElementNS(SVG_NS, "rect");
      bg.setAttribute("class", "calib-label-bg");
      bg.setAttribute("x", ep.cx - W / 2);
      bg.setAttribute("y", ep.cy - H / 2);
      bg.setAttribute("width", W);
      bg.setAttribute("height", H);
      bg.setAttribute("rx", 0);
      btn.appendChild(bg);

      const text = document.createElementNS(SVG_NS, "text");
      text.setAttribute("class", "calib-label");
      text.setAttribute("x", ep.cx);
      text.setAttribute("y", ep.cy);
      text.textContent = ep.label;
      btn.appendChild(text);

      g.appendChild(btn);
      svg.appendChild(g);
    });
    return svg;
  }

  function render(state) {
    container.innerHTML = "";
    const title = document.createElement("h2");
    title.textContent = "Dome mapping calibration";
    container.appendChild(title);

    if (!state) {
      container.appendChild(button("Start calibration", start));
      return;
    }

    // "Engaged" = there are picks to show/edit, whether actively lighting cables
    // (active) or reviewing the loaded saved mapping (reviewing).
    const engaged = state.active || state.reviewing;
    container.appendChild(buildPortMappingEditor(state, engaged));

    const line = document.createElement("div");
    line.className = "calib-status";
    if (state.reviewing) {
      line.textContent = "Reviewing the saved mapping. Swap pairs or Save, "
        + "or Start to re-map from scratch.";
    } else if (!state.active) {
      line.textContent = state.hasSavedMapping
        ? "Not running. Start a new mapping, or edit the saved one."
        : "Not running.";
    } else if (state.done) {
      line.textContent = state.complete
        ? "All cables identified — review and Save."
        : "All cables stepped through, but some are unset/duplicated.";
    } else {
      line.textContent = `Lighting ${state.cableLabels[state.currentStep]} `
        + `(cable ${state.currentStep + 1} of ${state.numCables}). `
        + "Click the endpoint on the dome that lit.";
    }
    container.appendChild(line);

    // The clickable dome diagram — the endpoints are the pick targets while a
    // cable is lighting. Shown whenever geometry is loaded (informative even
    // when idle), matching the native DomeMappingWindow canvas.
    const diagram = buildDiagram(state);
    if (diagram) container.appendChild(diagram);

    // Recorded picks so far.
    if (engaged) {
      const list = document.createElement("div");
      list.className = "calib-picks";
      const rows = state.cableLabels.map((lbl, cable) => {
        let target;
        if (cable === state.currentStep && !state.done) target = "← lighting now";
        else if (cable < state.currentStep) {
          target = state.picks[cable] < 0
            ? "(skipped)" : "→ " + state.endpointLabels[state.picks[cable]];
        } else target = "";
        return `${lbl.padEnd(20)} ${target}`;
      });
      list.textContent = rows.join("\n");
      container.appendChild(list);
    }

    // Swap two cables' recorded endpoints — the native window's Swap control.
    // Enabled once at least two cables are assigned.
    if (engaged) {
      const canSwap = state.currentStep >= 2;
      if (swapA >= state.numCables) swapA = 0;
      if (swapB >= state.numCables) swapB = 1;

      const swapWrap = document.createElement("div");
      swapWrap.className = "calib-swap";
      const lead = document.createElement("div");
      lead.className = "calib-swap-label";
      lead.textContent = "Swap two cables";
      swapWrap.appendChild(lead);

      const row = document.createElement("div");
      row.className = "calib-swap-row";
      const selA = document.createElement("select");
      const selB = document.createElement("select");
      state.cableLabels.forEach((lbl, i) => {
        selA.appendChild(new Option(lbl, i));
        selB.appendChild(new Option(lbl, i));
      });
      selA.value = String(swapA);
      selB.value = String(swapB);
      selA.disabled = !canSwap;
      selB.disabled = !canSwap;

      const swapBtn = button("Swap", () => swap(+selA.value, +selB.value));
      const refresh = () => {
        swapBtn.disabled = !(canSwap && selA.value !== selB.value);
      };
      selA.addEventListener("change", () => { swapA = +selA.value; refresh(); });
      selB.addEventListener("change", () => { swapB = +selB.value; refresh(); });
      refresh();

      const sep = document.createElement("span");
      sep.className = "calib-swap-sep";
      sep.textContent = "↔";
      row.appendChild(selA);
      row.appendChild(sep);
      row.appendChild(selB);
      row.appendChild(swapBtn);
      swapWrap.appendChild(row);
      container.appendChild(swapWrap);
    }

    const controls = document.createElement("div");
    controls.className = "calib-controls";
    if (state.active) {
      controls.appendChild(button("Skip", skip, !state.done ? true : false));
      controls.appendChild(button("Back", back, state.currentStep > 0));
      controls.appendChild(button("Restart", restart));
      controls.appendChild(button("Save mapping", save, state.complete));
      controls.appendChild(button("Cancel", cancel));
    } else if (state.reviewing) {
      // Editing the loaded mapping: re-map from scratch, save edits, or leave.
      controls.appendChild(button("Start calibration", start));
      controls.appendChild(button("Save mapping", save, state.complete));
      controls.appendChild(button("Cancel", cancel));
    } else {
      controls.appendChild(button("Start calibration", start));
      if (state.hasSavedMapping) {
        controls.appendChild(button("Edit saved mapping", edit));
      }
    }
    container.appendChild(controls);
  }

  // app.js calls this after its own load() so the lock module is ready.
  window.spectrumCalibrationInit = async function () {
    try {
      await loadGeometry();
      const res = await fetch("/api/maintenance/calibration");
      render(res.ok ? await res.json() : null);
    } catch (_) {
      render(null);
    }
  };
})();
