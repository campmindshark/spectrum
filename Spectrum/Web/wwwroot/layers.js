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

  // state.layers is in stack order (index 0 = background). visualizers,
  // blendModes, and blendParams come from the initial GET and don't change.
  // visualizers[i].params is that visualizer's param schema; blendParams[name]
  // is a blend mode's compositor-consumed schema (e.g. Desaturate).
  const state = {
    layers: [],
    visualizers: [],
    blendModes: [],
    blendParams: {},
  };
  let initialized = false;
  // Set while a PUT is in flight so we can ignore our own SSE echo cheaply.
  let pending = null;

  function sameStack(a, b) {
    return JSON.stringify(a) === JSON.stringify(b);
  }

  // The combined param schema for a layer: its visualizer's params followed by
  // its blend mode's compositor-consumed params. Empty for layers with neither.
  function schemaFor(layer) {
    const vis = state.visualizers.find((v) => v.key === layer.visualizerKey);
    const visParams = (vis && vis.params) || [];
    const blendParams = state.blendParams[layer.blendMode] || [];
    return visParams.concat(blendParams);
  }

  // A fresh value bag holding the schema's defaults.
  function defaultsFor(schema) {
    const d = {};
    schema.forEach((p) => {
      d[p.key] = p.default;
    });
    return d;
  }

  function setParam(idx, key, value) {
    if (!state.layers[idx].params) state.layers[idx].params = {};
    state.layers[idx].params[key] = value;
    putLayers();
  }

  async function putLayers() {
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
    }
  }

  // Build one editor row for stack entry `idx`.
  function renderRow(layer, idx) {
    const row = document.createElement("div");
    row.className = "layer-row";

    const top = document.createElement("div");
    top.className = "top";

    const visSel = document.createElement("select");
    state.visualizers.forEach((v) => {
      const o = document.createElement("option");
      o.value = v.key;
      o.textContent = v.label;
      if (v.key === layer.visualizerKey) o.selected = true;
      visSel.appendChild(o);
    });
    visSel.addEventListener("change", () => {
      state.layers[idx].visualizerKey = visSel.value;
      // Schema changed: reset params to the new schema's defaults, then rerender
      // so the editors match.
      state.layers[idx].params = defaultsFor(schemaFor(state.layers[idx]));
      render();
      putLayers();
    });
    top.appendChild(visSel);

    const enabled = document.createElement("input");
    enabled.type = "checkbox";
    enabled.checked = layer.enabled;
    enabled.title = "Enabled";
    enabled.addEventListener("change", () => {
      state.layers[idx].enabled = enabled.checked;
      putLayers();
    });
    top.appendChild(enabled);

    const remove = document.createElement("button");
    remove.textContent = "✕";
    remove.title = "Remove layer";
    remove.addEventListener("click", () => {
      state.layers.splice(idx, 1);
      render();
      putLayers();
    });
    top.appendChild(remove);
    row.appendChild(top);

    const bottom = document.createElement("div");
    bottom.className = "bottom";

    const blendSel = document.createElement("select");
    state.blendModes.forEach((m) => {
      const o = document.createElement("option");
      o.value = m;
      o.textContent = m;
      if (m === layer.blendMode) o.selected = true;
      blendSel.appendChild(o);
    });
    blendSel.title = "Blend mode";
    blendSel.addEventListener("change", () => {
      state.layers[idx].blendMode = blendSel.value;
      // The blend's compositor-consumed schema changed: reset params and
      // rerender so its editors (e.g. Desaturate's) appear/disappear.
      state.layers[idx].params = defaultsFor(schemaFor(state.layers[idx]));
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
    opacity.addEventListener("input", () => {
      state.layers[idx].opacity = parseFloat(opacity.value);
    });
    opacity.addEventListener("change", () => {
      state.layers[idx].opacity = parseFloat(opacity.value);
      putLayers();
    });
    bottom.appendChild(opacity);

    // Front is the last stack entry, so "up" (toward front) means a higher idx.
    const up = document.createElement("button");
    up.textContent = "▲";
    up.title = "Move toward front";
    up.disabled = idx === state.layers.length - 1;
    up.addEventListener("click", () => {
      swap(idx, idx + 1);
    });
    bottom.appendChild(up);

    const down = document.createElement("button");
    down.textContent = "▼";
    down.title = "Move toward back";
    down.disabled = idx === 0;
    down.addEventListener("click", () => {
      swap(idx, idx - 1);
    });
    bottom.appendChild(down);

    row.appendChild(bottom);

    // Generic per-layer param editors, built from the layer's combined schema
    // crossed with its stored values. Absent bag => seed defaults so the sliders
    // start somewhere sensible.
    const schema = schemaFor(layer);
    if (schema.length) {
      if (!layer.params) layer.params = defaultsFor(schema);
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

    const has = layer.params && layer.params[p.key] !== undefined;
    const cur = has ? layer.params[p.key] : p.default;

    let input;
    if (p.type === "Bool") {
      input = document.createElement("input");
      input.type = "checkbox";
      input.checked = cur !== 0;
      input.addEventListener("change", () => {
        setParam(idx, p.key, input.checked ? 1 : 0);
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
        setParam(idx, p.key, parseInt(input.value, 10));
      });
    } else {
      input = document.createElement("input");
      input.type = "range";
      input.min = p.min;
      input.max = p.max;
      input.step = p.step || 0.01;
      input.value = cur;
      input.addEventListener("input", () => {
        if (!state.layers[idx].params) state.layers[idx].params = {};
        state.layers[idx].params[p.key] = parseFloat(input.value);
      });
      input.addEventListener("change", () => {
        setParam(idx, p.key, parseFloat(input.value));
      });
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

    // Display front (last stack entry) first.
    for (let idx = state.layers.length - 1; idx >= 0; idx--) {
      panel.appendChild(renderRow(state.layers[idx], idx));
    }

    const add = document.createElement("button");
    add.className = "add";
    add.textContent = "Add layer";
    add.addEventListener("click", () => {
      const key = state.visualizers.length ? state.visualizers[0].key : "";
      // New layer goes on top (front) = end of the stack array.
      const layer = {
        visualizerKey: key,
        blendMode: "Add",
        opacity: 1.0,
        enabled: true,
      };
      layer.params = defaultsFor(schemaFor(layer));
      state.layers.push(layer);
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
      state.blendModes = body.blendModes || [];
      state.blendParams = body.blendParams || {};
      initialized = true;
      render();
    } catch (e) {
      setStatus(`layers load: ${e}`, true);
    }
  }

  window.spectrumLayersInit = init;
  window.spectrumApplyLayers = applyLayers;
})();
