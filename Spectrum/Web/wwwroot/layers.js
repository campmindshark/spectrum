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

  function sameStack(a, b) {
    return JSON.stringify(a) === JSON.stringify(b);
  }

  function rendererSchemaFor(layer) {
    const vis = state.visualizers.find((v) => v.key === layer.visualizerKey);
    return (vis && vis.params) || [];
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
    putLayers();
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
  async function fireLayer(key) {
    try {
      const res = await fetch(
        `/api/layers/${encodeURIComponent(key)}/fire`, { method: "POST" });
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setStatus(`fire: ${body.error || res.status}`, true);
        return;
      }
      setStatus(`fired ${key}`);
    } catch (e) {
      setStatus(`fire: ${e}`, true);
    }
  }

  // Clear one layer's live state. Same shape as fireLayer, bumping the clear
  // counter instead — a layer holding accumulated particles (Shooting Star)
  // drops them; layers with no such state ignore it.
  async function clearLayer(key) {
    try {
      const res = await fetch(
        `/api/layers/${encodeURIComponent(key)}/clear`, { method: "POST" });
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setStatus(`clear: ${body.error || res.status}`, true);
        return;
      }
      setStatus(`cleared ${key}`);
    } catch (e) {
      setStatus(`clear: ${e}`, true);
    }
  }

  // Build one editor row for stack entry `idx`.
  function renderRow(layer, idx) {
    const row = document.createElement("div");
    row.className = "layer-row";

    const top = document.createElement("div");
    top.className = "top";

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
    row.appendChild(notes);

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
    up.textContent = "Front";
    up.title = "Move toward front";
    up.setAttribute("aria-label", `Move ${visSel.selectedOptions[0]?.textContent || "layer"} toward front`);
    up.disabled = idx === state.layers.length - 1;
    up.addEventListener("click", () => {
      swap(idx, idx + 1);
    });
    bottom.appendChild(up);

    const down = document.createElement("button");
    down.textContent = "Back";
    down.title = "Move toward back";
    down.setAttribute("aria-label", `Move ${visSel.selectedOptions[0]?.textContent || "layer"} toward back`);
    down.disabled = idx === 0;
    down.addEventListener("click", () => {
      swap(idx, idx - 1);
    });
    bottom.appendChild(down);

    // Manual fire: bumps this layer's fire counter so a triggerable layer
    // (OneShot Wave/Metaball, Ripple/Stamp) fires once. Not a stack edit, so it
    // POSTs to a dedicated endpoint rather than PUTing the whole stack.
    const fire = document.createElement("button");
    fire.textContent = "Fire";
    fire.title = "Fire (manual trigger)";
    fire.setAttribute("aria-label", `Fire ${visSel.selectedOptions[0]?.textContent || "layer"}`);
    fire.addEventListener("click", () => {
      fireLayer(layer.instanceId);
    });
    bottom.appendChild(fire);

    // Manual clear: drops the layer's live particles (see clearLayer).
    const clear = document.createElement("button");
    clear.textContent = "Clear";
    clear.title = "Clear (drop this layer's live particles)";
    clear.setAttribute("aria-label", `Clear ${visSel.selectedOptions[0]?.textContent || "layer"}`);
    clear.addEventListener("click", () => {
      clearLayer(layer.instanceId);
    });
    bottom.appendChild(clear);

    row.appendChild(bottom);

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
      row.appendChild(params);
    }

    return row;
  }

  // One param editor: a labeled Slider (Double), CheckBox (Bool), or ComboBox
  // (Enum), matching the descriptor type. The committed change (change event)
  // PUTs the whole stack; the range's live input event updates local state only.
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
        bagFor(state.layers[idx], p)[p.key] = parseFloat(input.value);
        value.textContent = Number(input.value).toFixed(2);
      });
      input.addEventListener("change", () => {
        setParam(idx, p, parseFloat(input.value));
      });
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
