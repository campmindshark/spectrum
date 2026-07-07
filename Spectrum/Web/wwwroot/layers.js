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

  // state.layers is in stack order (index 0 = background). visualizers and
  // blendModes come from the initial GET and don't change.
  const state = { layers: [], visualizers: [], blendModes: [] };
  let initialized = false;
  // Set while a PUT is in flight so we can ignore our own SSE echo cheaply.
  let pending = null;

  function sameStack(a, b) {
    return JSON.stringify(a) === JSON.stringify(b);
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
    return row;
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
      state.layers.push({
        visualizerKey: key,
        blendMode: "Add",
        opacity: 1.0,
        enabled: true,
      });
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
      initialized = true;
      render();
    } catch (e) {
      setStatus(`layers load: ${e}`, true);
    }
  }

  window.spectrumLayersInit = init;
  window.spectrumApplyLayers = applyLayers;
})();
