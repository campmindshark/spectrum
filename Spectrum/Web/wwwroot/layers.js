// The dome layer-stack editor for the user surface. Replaces the old
// single-visualizer dropdown: one row per layer (top row = front) with a
// visualizer picker, blend-mode picker, opacity slider, mute toggle, remove and
// reorder buttons, plus "Add layer". Every edit PUTs the whole stack to
// /api/layers (whole-stack last-write-wins); the "layers" SSE frame re-renders
// the panel so multiple clients and the native UI stay converged.
//
// The wire format (GET/PUT/SSE) is stack order: layers[0] is the background
// (bottom), the last entry is the front. The panel displays them reversed so
// the top row is the front, matching the native GUI.

(function () {
  const panel = document.getElementById("layers");
  if (!panel) return;

  const setStatus = window.spectrumStatus || function () {};

  // state.layers is in stack order (index 0 = background). The visualizer and
  // operation descriptors come from the initial GET and don't change.
  const state = {
    layers: [],
    visualizers: [],
    operations: [],
  };
  let initialized = false;
  // Set while a PUT is in flight so we can ignore our own SSE echo cheaply.
  let pending = null;
  // Astronomy playback is runtime state, not a persisted stack edit. Mirror
  // its clock locally so the time slider and readout show the running position.
  const astronomyPlayback = new Map();
  const astronomyStoppedOffsets = new Map();
  let astronomyPlaybackTimer = null;
  // Disclosure is UI-only, just like DomeLayersController.IsExpanded in the
  // native editor. Key it by stable instance ID so a reorder or SSE re-render
  // does not unexpectedly reopen a layer.
  const expandedByInstanceId = new Map();

  function sameStack(a, b) {
    return JSON.stringify(a) === JSON.stringify(b);
  }

  function rendererSchemaFor(layer) {
    const vis = visualizerFor(layer);
    return (vis && vis.params) || [];
  }

  function visualizerFor(layer) {
    return state.visualizers.find((v) => v.key === layer.visualizerKey);
  }

  function operationSchemaFor(layer) {
    const operation = state.operations.find((o) => o.id === layer.blendMode);
    return (operation && operation.params) || [];
  }

  function schemaFor(layer) {
    return rendererSchemaFor(layer).concat(operationSchemaFor(layer));
  }

  // A fresh value bag holding the schema's defaults.
  function defaultsFor(schema) {
    const d = {};
    schema.forEach((p) => {
      d[p.key] = p.default;
    });
    return d;
  }

  // A value bag for the schema, keeping values that remain valid after an
  // operation change and defaulting newly introduced parameters.
  function seededParamsFor(schema, existing) {
    const d = {};
    schema.forEach((p) => {
      d[p.key] =
        existing && Object.prototype.hasOwnProperty.call(existing, p.key)
          ? existing[p.key]
          : p.default;
    });
    return d;
  }

  function bagFor(layer, descriptor) {
    const property = descriptor.compositorConsumed
      ? "operationParams"
      : "rendererParams";
    if (!layer[property]) layer[property] = {};
    return layer[property];
  }

  function setParam(idx, descriptor, value) {
    bagFor(state.layers[idx], descriptor)[descriptor.key] = value;
    rebaseAstronomyPlayback(state.layers[idx], descriptor.key, value);
    putLayers();
  }

  function rendererParamValue(layer, key) {
    const descriptor = rendererSchemaFor(layer).find((p) => p.key === key);
    const bag = layer.rendererParams || {};
    return Object.prototype.hasOwnProperty.call(bag, key)
      ? Number(bag[key])
      : Number(descriptor ? descriptor.default : 0);
  }

  function astronomyOffset(playback, now, speed, loop) {
    const elapsedSeconds = (now - playback.startedAt) / 1000;
    const offset = playback.startOffset + elapsedSeconds * speed;
    if (loop) return ((offset % 168) + 168) % 168;
    return Math.min(offset, 168);
  }

  function currentAstronomyOffset(instanceId, now = performance.now()) {
    const playback = astronomyPlayback.get(instanceId);
    if (!playback) return null;
    const layer = state.layers.find((candidate) =>
      candidate.instanceId === instanceId);
    if (!layer || layer.visualizerKey !== "astronomy") return null;
    return astronomyOffset(
      playback, now,
      rendererParamValue(layer, "playbackSpeed"),
      rendererParamValue(layer, "loop") !== 0);
  }

  function rebaseAstronomyPlayback(layer, key, value) {
    if (layer.visualizerKey !== "astronomy") return;
    if (key === "timeOffsetHours") {
      astronomyStoppedOffsets.delete(layer.instanceId);
    }
    const playback = astronomyPlayback.get(layer.instanceId);
    if (!playback) return;
    const now = performance.now();
    if (key === "timeOffsetHours") {
      playback.startOffset = Number(value);
      playback.configuredTimeOffset = Number(value);
      playback.startedAt = now;
      return;
    }
    if (key === "playbackSpeed" && Number(value) !== playback.speed) {
      playback.startOffset = astronomyOffset(
        playback, now, playback.speed,
        rendererParamValue(layer, "loop") !== 0);
      playback.startedAt = now;
      playback.speed = Number(value);
    }
  }

  function startAstronomyPlayback(instanceId) {
    const layer = state.layers.find((candidate) =>
      candidate.instanceId === instanceId);
    if (!layer || layer.visualizerKey !== "astronomy") return;
    const configuredTimeOffset = rendererParamValue(
      layer, "timeOffsetHours");
    const stopped = astronomyStoppedOffsets.get(instanceId);
    const startOffset = stopped &&
        stopped.configuredTimeOffset === configuredTimeOffset
      ? stopped.offset
      : configuredTimeOffset;
    astronomyStoppedOffsets.delete(instanceId);
    astronomyPlayback.set(instanceId, {
      startOffset,
      configuredTimeOffset,
      startedAt: performance.now(),
      speed: rendererParamValue(layer, "playbackSpeed"),
    });
    updateAstronomyPlayback();
    if (astronomyPlaybackTimer == null) {
      astronomyPlaybackTimer = setInterval(updateAstronomyPlayback, 100);
    }
  }

  function updateAstronomyPlayback() {
    const now = performance.now();
    astronomyPlayback.forEach((playback, instanceId) => {
      const layer = state.layers.find((candidate) =>
        candidate.instanceId === instanceId);
      if (!layer || layer.visualizerKey !== "astronomy") {
        astronomyPlayback.delete(instanceId);
        return;
      }

      const speed = rendererParamValue(layer, "playbackSpeed");
      const loop = rendererParamValue(layer, "loop") !== 0;
      const configuredTimeOffset = rendererParamValue(
        layer, "timeOffsetHours");
      if (configuredTimeOffset !== playback.configuredTimeOffset) {
        playback.startOffset = configuredTimeOffset;
        playback.configuredTimeOffset = configuredTimeOffset;
        playback.startedAt = now;
        playback.speed = speed;
      } else if (speed !== playback.speed) {
        playback.startOffset = astronomyOffset(
          playback, now, playback.speed, loop);
        playback.startedAt = now;
        playback.speed = speed;
      }
      const offset = astronomyOffset(playback, now, speed, loop);
      panel.querySelectorAll("[data-astronomy-time]").forEach((element) => {
        if (element.dataset.astronomyTime !== instanceId) return;
        if (element.matches("input")) element.value = offset;
        else element.textContent = offset.toFixed(2);
      });
      if (!loop && offset >= 168) astronomyPlayback.delete(instanceId);
    });
    if (astronomyPlayback.size === 0 && astronomyPlaybackTimer != null) {
      clearInterval(astronomyPlaybackTimer);
      astronomyPlaybackTimer = null;
    }
  }

  function stopAstronomyPlayback(instanceId) {
    const offset = currentAstronomyOffset(instanceId);
    const layer = state.layers.find((candidate) =>
      candidate.instanceId === instanceId);
    if (offset != null && layer) {
      astronomyStoppedOffsets.set(instanceId, {
        offset,
        configuredTimeOffset: rendererParamValue(
          layer, "timeOffsetHours"),
      });
    }
    updateAstronomyPlayback();
    astronomyPlayback.delete(instanceId);
    if (astronomyPlayback.size === 0 && astronomyPlaybackTimer != null) {
      clearInterval(astronomyPlaybackTimer);
      astronomyPlaybackTimer = null;
    }
  }

  function formatDateParam(value) {
    const encoded = String(Math.round(Number(value))).padStart(8, "0");
    return encoded.length === 8
      ? `${encoded.slice(0, 4)}-${encoded.slice(4, 6)}-${encoded.slice(6, 8)}`
      : "";
  }

  function parseDateParam(text) {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(text.trim());
    if (!match) return null;
    const year = Number(match[1]);
    const month = Number(match[2]);
    const day = Number(match[3]);
    const candidate = new Date(Date.UTC(year, month - 1, day));
    if (candidate.getUTCFullYear() !== year ||
        candidate.getUTCMonth() !== month - 1 ||
        candidate.getUTCDate() !== day) {
      return null;
    }
    return year * 10000 + month * 100 + day;
  }

  async function putLayers() {
    panel.setAttribute("aria-busy", "true");
    setStatus("Applying layer changes…", false, true);
    try {
      const res = await fetch("/api/layers", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ layers: state.layers }),
      });
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setStatus(`layers: ${body.error || res.status}`, true);
        return;
      }
      const body = await res.json();
      state.layers = body.layers;
      setStatus(`layers updated (${state.layers.length})`);
    } catch (e) {
      setStatus(`layers: ${e}`, true);
    } finally {
      panel.removeAttribute("aria-busy");
    }
  }

  // Fire one layer's manual trigger. POSTs to the instance-addressed endpoint
  // rather than PUTing the stack — firing bumps a counter, not the stack.
  async function fireLayer(key, action = "fire") {
    try {
      const res = await fetch(
        `/api/layers/${encodeURIComponent(key)}/fire`, { method: "POST" });
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setStatus(`${action}: ${body.error || res.status}`, true);
        return false;
      }
      setStatus(`${action === "play" ? "playing" : "fired"} ${key}`);
      return true;
    } catch (e) {
      setStatus(`${action}: ${e}`, true);
      return false;
    }
  }

  // Clear one layer's live state. Same shape as fireLayer, bumping the clear
  // counter instead — a layer holding accumulated particles (Shooting Star)
  // drops them; layers with no such state ignore it.
  async function clearLayer(key, action = "clear") {
    try {
      const res = await fetch(
        `/api/layers/${encodeURIComponent(key)}/clear`, { method: "POST" });
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setStatus(`${action}: ${body.error || res.status}`, true);
        return false;
      }
      setStatus(`${action === "stop" ? "stopped" : "cleared"} ${key}`);
      return true;
    } catch (e) {
      setStatus(`${action}: ${e}`, true);
      return false;
    }
  }

  // Build one editor row for stack entry `idx`.
  function renderRow(layer, idx) {
    const row = document.createElement("div");
    row.className = "layer-row";

    const details = document.createElement("div");
    details.className = "layer-details";
    details.id = `layer-${idx}-settings`;

    const top = document.createElement("div");
    top.className = "top";

    const disclosure = document.createElement("button");
    disclosure.type = "button";
    disclosure.className = "layer-toggle";
    disclosure.title = "Expand or collapse layer settings";
    disclosure.setAttribute("aria-controls", details.id);
    const disclosureIcon = document.createElement("span");
    disclosureIcon.setAttribute("aria-hidden", "true");
    disclosure.appendChild(disclosureIcon);
    top.appendChild(disclosure);

    const visSel = document.createElement("select");
    visSel.setAttribute("aria-label", `Visualizer for layer ${idx + 1}`);
    state.visualizers.forEach((v) => {
      const o = document.createElement("option");
      o.value = v.key;
      o.textContent = v.label;
      if (v.key === layer.visualizerKey) o.selected = true;
      visSel.appendChild(o);
    });
    visSel.addEventListener("change", () => {
      state.layers[idx].visualizerKey = visSel.value;
      // Renderer schema changed: reset only its namespace. The compositing
      // operation and its values remain independent.
      state.layers[idx].rendererParams = defaultsFor(
        rendererSchemaFor(state.layers[idx]));
      render();
      putLayers();
    });
    top.appendChild(visSel);

    let expanded = !expandedByInstanceId.has(layer.instanceId) ||
      expandedByInstanceId.get(layer.instanceId);
    const syncDisclosure = () => {
      const name = visSel.selectedOptions[0]?.textContent || "layer";
      row.classList.toggle("layer-collapsed", !expanded);
      details.hidden = !expanded;
      disclosureIcon.textContent = expanded ? "\u2212" : "+";
      disclosure.setAttribute("aria-expanded", String(expanded));
      disclosure.setAttribute(
        "aria-label", `${expanded ? "Collapse" : "Expand"} ${name} layer settings`);
    };
    disclosure.addEventListener("click", () => {
      expanded = !expanded;
      expandedByInstanceId.set(layer.instanceId, expanded);
      syncDisclosure();
    });
    syncDisclosure();

    const enabled = document.createElement("input");
    enabled.type = "checkbox";
    enabled.checked = layer.enabled;
    enabled.title = "Enable this layer";
    enabled.setAttribute("aria-label", `Enable ${visSel.selectedOptions[0]?.textContent || "layer"}`);
    enabled.addEventListener("change", () => {
      state.layers[idx].enabled = enabled.checked;
      putLayers();
    });
    const enabledWrap = document.createElement("label");
    enabledWrap.className = "checkbox-target";
    enabledWrap.appendChild(enabled);
    enabledWrap.appendChild(document.createTextNode(" On"));
    top.appendChild(enabledWrap);

    const remove = document.createElement("button");
    remove.textContent = "Remove";
    remove.className = "action-danger";
    remove.title = "Remove layer";
    remove.setAttribute("aria-label", `Remove ${visSel.selectedOptions[0]?.textContent || "layer"}`);
    remove.addEventListener("click", () => {
      state.layers.splice(idx, 1);
      render();
      putLayers();
    });
    top.appendChild(remove);
    row.appendChild(top);

    const notes = document.createElement("input");
    notes.type = "text";
    notes.className = "layer-notes";
    notes.placeholder = "Notes to yourself…";
    notes.setAttribute("aria-label", `Notes for ${visSel.selectedOptions[0]?.textContent || "layer"}`);
    notes.value = layer.notes || "";
    notes.addEventListener("change", () => {
      state.layers[idx].notes = notes.value;
      putLayers();
    });
    details.appendChild(notes);

    const bottom = document.createElement("div");
    bottom.className = "bottom";

    const blendSel = document.createElement("select");
    state.operations.forEach((operation) => {
      const o = document.createElement("option");
      o.value = operation.id;
      o.textContent = operation.label;
      if (operation.id === layer.blendMode) o.selected = true;
      blendSel.appendChild(o);
    });
    blendSel.title = "Blend mode";
    blendSel.setAttribute("aria-label", `Blend mode for ${visSel.selectedOptions[0]?.textContent || "layer"}`);
    blendSel.addEventListener("change", () => {
      state.layers[idx].blendMode = blendSel.value;
      state.layers[idx].operationParams = seededParamsFor(
        operationSchemaFor(state.layers[idx]),
        state.layers[idx].operationParams);
      render();
      putLayers();
    });
    bottom.appendChild(blendSel);

    const opacity = document.createElement("input");
    opacity.type = "range";
    opacity.min = 0;
    opacity.max = 1;
    opacity.step = 0.01;
    opacity.value = layer.opacity;
    opacity.title = "Opacity";
    opacity.setAttribute("aria-label", `Opacity for ${visSel.selectedOptions[0]?.textContent || "layer"}`);
    const opacityValue = document.createElement("span");
    opacityValue.className = "param-value";
    opacityValue.textContent = `${Math.round(layer.opacity * 100)}%`;
    opacity.addEventListener("input", () => {
      state.layers[idx].opacity = parseFloat(opacity.value);
      opacityValue.textContent = `${Math.round(parseFloat(opacity.value) * 100)}%`;
    });
    opacity.addEventListener("change", () => {
      state.layers[idx].opacity = parseFloat(opacity.value);
      putLayers();
    });
    bottom.appendChild(opacity);
    bottom.appendChild(opacityValue);

    // Front is the last stack entry, so "up" (toward front) means a higher idx.
    const up = document.createElement("button");
    up.textContent = "↑";
    up.className = "layer-reorder";
    up.title = "Move up toward front";
    up.setAttribute("aria-label", `Move ${visSel.selectedOptions[0]?.textContent || "layer"} up toward front`);
    up.disabled = idx === state.layers.length - 1;
    up.addEventListener("click", () => {
      swap(idx, idx + 1);
    });
    top.insertBefore(up, enabledWrap);

    const down = document.createElement("button");
    down.textContent = "↓";
    down.className = "layer-reorder";
    down.title = "Move down toward back";
    down.setAttribute("aria-label", `Move ${visSel.selectedOptions[0]?.textContent || "layer"} down toward back`);
    down.disabled = idx === 0;
    down.addEventListener("click", () => {
      swap(idx, idx - 1);
    });
    top.insertBefore(down, enabledWrap);

    // Manual fire: bumps this layer's fire counter so a triggerable layer
    // (OneShot Wave/Metaball, Ripple/Stamp) fires once. Not a stack edit, so it
    // POSTs to a dedicated endpoint rather than PUTing the whole stack.
    const visualizer = visualizerFor(layer);
    const fireAction = visualizer && visualizer.fireAction;
    const isAstronomy = layer.visualizerKey === "astronomy";
    if (fireAction) {
      const fire = document.createElement("button");
      fire.textContent = fireAction.label;
      fire.title = fireAction.toolTip;
      fire.setAttribute("aria-label", `${fireAction.label} ${visSel.selectedOptions[0]?.textContent || "layer"}`);
      fire.addEventListener("click", async () => {
        const fired = await fireLayer(
          layer.instanceId, fireAction.label.toLowerCase());
        if (fired && isAstronomy) {
          startAstronomyPlayback(layer.instanceId);
        }
      });
      top.insertBefore(fire, up);
    }

    const clearAction = visualizer && visualizer.clearAction;
    if (clearAction) {
      const clear = document.createElement("button");
      clear.textContent = clearAction.label;
      clear.title = clearAction.toolTip;
      clear.setAttribute("aria-label", `${clearAction.label} ${visSel.selectedOptions[0]?.textContent || "layer"}`);
      clear.addEventListener("click", async () => {
        const cleared = await clearLayer(
          layer.instanceId, clearAction.label.toLowerCase());
        if (cleared && isAstronomy) {
          stopAstronomyPlayback(layer.instanceId);
        }
      });
      top.insertBefore(clear, up);
    }

    details.appendChild(bottom);

    // Generic per-layer param editors, built from the layer's combined schema
    // crossed with its namespaced stored values.
    const schema = schemaFor(layer);
    if (schema.length) {
      if (!layer.rendererParams) {
        layer.rendererParams = defaultsFor(rendererSchemaFor(layer));
      }
      if (!layer.operationParams) {
        layer.operationParams = defaultsFor(operationSchemaFor(layer));
      }
      const params = document.createElement("div");
      params.className = "layer-params";
      schema.forEach((p) => {
        params.appendChild(renderParam(layer, idx, p));
      });
      details.appendChild(params);
    }

    row.appendChild(details);

    return row;
  }

  // One generic param editor matching the descriptor type. The committed
  // change PUTs the whole stack; a range's live input updates local state only.
  function renderParam(layer, idx, p) {
    const wrap = document.createElement("label");
    wrap.className = "param";
    const name = document.createElement("span");
    name.className = "param-label";
    name.textContent = p.label;
    wrap.appendChild(name);

    const bag = bagFor(layer, p);
    const has = bag[p.key] !== undefined;
    const cur = has ? bag[p.key] : p.default;

    let input;
    if (p.type === "Color") {
      input = document.createElement("input");
      input.type = "color";
      input.value = "#" + Math.round(cur).toString(16).padStart(6, "0");
      input.addEventListener("input", () => {
        setParam(idx, p, parseInt(input.value.slice(1), 16));
      });
    } else if (p.type === "Bool") {
      input = document.createElement("input");
      input.type = "checkbox";
      input.checked = cur !== 0;
      input.addEventListener("change", () => {
        setParam(idx, p, input.checked ? 1 : 0);
      });
    } else if (p.type === "Enum") {
      input = document.createElement("select");
      (p.options || []).forEach((opt, i) => {
        const o = document.createElement("option");
        o.value = i;
        o.textContent = opt;
        if (i === Math.round(cur)) o.selected = true;
        input.appendChild(o);
      });
      input.addEventListener("change", () => {
        setParam(idx, p, parseInt(input.value, 10));
      });
    } else if (p.type === "Date") {
      input = document.createElement("input");
      input.type = "text";
      input.placeholder = "YYYY-MM-DD";
      input.value = formatDateParam(cur);
      input.addEventListener("input", () => input.setCustomValidity(""));
      input.addEventListener("change", () => {
        const encoded = parseDateParam(input.value);
        if (encoded == null) {
          input.setCustomValidity("Use a valid date in YYYY-MM-DD format.");
          input.reportValidity();
          return;
        }
        input.setCustomValidity("");
        setParam(idx, p, encoded);
      });
    } else {
      input = document.createElement("input");
      input.type = "range";
      input.min = p.min;
      input.max = p.max;
      input.step = p.step || 0.01;
      input.value = cur;
      const value = document.createElement("span");
      value.className = "param-value";
      value.textContent = Number(cur).toFixed(2);
      input.addEventListener("input", () => {
        const parsed = parseFloat(input.value);
        bagFor(state.layers[idx], p)[p.key] = parsed;
        rebaseAstronomyPlayback(state.layers[idx], p.key, parsed);
        value.textContent = Number(parsed).toFixed(2);
      });
      input.addEventListener("change", () => {
        setParam(idx, p, parseFloat(input.value));
      });
      if (layer.visualizerKey === "astronomy" &&
          p.key === "timeOffsetHours") {
        input.dataset.astronomyTime = layer.instanceId;
        value.dataset.astronomyTime = layer.instanceId;
        const playbackOffset = currentAstronomyOffset(layer.instanceId);
        if (playbackOffset != null) {
          input.value = playbackOffset;
          value.textContent = playbackOffset.toFixed(2);
        }
      }
      wrap.appendChild(input);
      wrap.appendChild(value);
      return wrap;
    }
    wrap.appendChild(input);
    return wrap;
  }

  function swap(i, j) {
    const tmp = state.layers[i];
    state.layers[i] = state.layers[j];
    state.layers[j] = tmp;
    render();
    putLayers();
  }

  function render() {
    panel.innerHTML = "";
    const h = document.createElement("h2");
    h.textContent = "Layers";
    panel.appendChild(h);
    const hint = document.createElement("p");
    hint.className = "hint";
    hint.textContent = "Top row is the front; layers blend bottom to top.";
    panel.appendChild(hint);

    const disclosureActions = document.createElement("div");
    disclosureActions.className = "layer-disclosure-actions";
    const collapseAll = document.createElement("button");
    collapseAll.type = "button";
    collapseAll.textContent = "Collapse All";
    collapseAll.disabled = state.layers.length === 0;
    collapseAll.addEventListener("click", () => {
      state.layers.forEach((layer) => {
        expandedByInstanceId.set(layer.instanceId, false);
      });
      render();
    });
    disclosureActions.appendChild(collapseAll);
    const expandAll = document.createElement("button");
    expandAll.type = "button";
    expandAll.textContent = "Expand All";
    expandAll.disabled = state.layers.length === 0;
    expandAll.addEventListener("click", () => {
      state.layers.forEach((layer) => {
        expandedByInstanceId.set(layer.instanceId, true);
      });
      render();
    });
    disclosureActions.appendChild(expandAll);
    panel.appendChild(disclosureActions);

    if (state.layers.length === 0) {
      const empty = document.createElement("p");
      empty.className = "empty-state";
      empty.textContent = "No layers are active. Add a layer to create a dome look.";
      panel.appendChild(empty);
    }

    // Display front (last stack entry) first.
    for (let idx = state.layers.length - 1; idx >= 0; idx--) {
      panel.appendChild(renderRow(state.layers[idx], idx));
    }

    const add = document.createElement("button");
    add.className = "add";
    add.textContent = "Add layer";
    add.addEventListener("click", () => {
      const key = state.visualizers.length ? state.visualizers[0].key : "";
      // New layer goes on the bottom (background) = the start of the stack array
      // (index 0 is the background, the last entry is the front).
      const defaultOperation = state.operations.find((o) => o.id === "Add") ||
        state.operations[0];
      const layer = {
        instanceId: crypto.randomUUID().replaceAll("-", ""),
        visualizerKey: key,
        blendMode: defaultOperation ? defaultOperation.id : "",
        opacity: 1.0,
        enabled: true,
      };
      layer.rendererParams = defaultsFor(rendererSchemaFor(layer));
      layer.operationParams = defaultsFor(operationSchemaFor(layer));
      state.layers.unshift(layer);
      render();
      putLayers();
    });
    panel.appendChild(add);
  }

  // SSE "layers" frame: the full stack in wire order. Ignore our own echo, and
  // don't rebuild the DOM out from under an active editor (defer until focus
  // leaves the panel).
  function applyLayers(layers) {
    if (!initialized) {
      state.layers = layers;
      return;
    }
    if (sameStack(layers, state.layers)) return;
    if (panel.contains(document.activeElement)) {
      pending = layers;
      return;
    }
    state.layers = layers;
    render();
  }

  panel.addEventListener("focusout", () => {
    // A moment after focus leaves a control, apply any stack a remote client
    // pushed while we were editing.
    setTimeout(() => {
      if (pending && !panel.contains(document.activeElement)) {
        state.layers = pending;
        pending = null;
        render();
      }
    }, 0);
  });

  async function init() {
    try {
      const res = await fetch("/api/layers");
      if (!res.ok) {
        setStatus(`layers load: ${res.status}`, true);
        return;
      }
      const body = await res.json();
      state.layers = body.layers || [];
      state.visualizers = body.visualizers || [];
      state.operations = body.operations || [];
      initialized = true;
      render();
    } catch (e) {
      setStatus(`layers load: ${e}`, true);
    }
  }

  window.spectrumLayersInit = init;
  window.spectrumApplyLayers = applyLayers;
})();
