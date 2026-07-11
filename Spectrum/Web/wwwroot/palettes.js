// The color-palette panel for the user surface, the web port of the native VJ
// HUD palette column. Two parts:
//
//   - the live palette: the eight gradient slots (colorPalette 0-7) every
//     visualizer reads. Each row is a start color, an optional gradient end, and
//     a preview swatch. Any edit PUTs the whole eight-slot palette to
//     /api/palette (whole-palette last-write-wins, like the layer stack); the
//     "palette" SSE frame carries the winning palette back so every client
//     converges.
//   - named presets: parallel to scenes. Save the live palette under a typed
//     name, Apply/Delete by name. Save/Delete POST/DELETE to /api/palettes*, and
//     the "palettes" SSE frame carries the updated list (with per-preset colors
//     for the previews) so every client stays in sync. Applying a preset
//     rewrites the live slots server-side, which flow back through the "palette"
//     frame — so the editor re-renders on its own.

(function () {
  const panel = document.getElementById("palette");
  if (!panel) return;

  const setStatus = window.spectrumStatus || function () {};
  const SLOTS = 8;

  // The live palette, always exactly 8 slots: { start: "#rrggbb", end: hex|null }.
  let liveColors = normalizeLive([]);
  // Saved presets: [{ name, colors: [{start, end}] }].
  let presets = [];
  let nameInput = null;

  let liveEl = null;
  let presetsEl = null;

  // Coerce whatever the server sent into 8 editable slots. A null/absent slot or
  // a null start becomes solid black (the web editor always writes concrete
  // slots; an empty slot renders black on the dome anyway).
  function normalizeLive(colors) {
    const out = [];
    for (let i = 0; i < SLOTS; i++) {
      const c = colors && colors[i] ? colors[i] : {};
      out.push({ start: c.start || "#000000", end: c.end || null });
    }
    return out;
  }

  // The CSS background for a slot's preview: a left-to-right gradient when it has
  // an end, else a solid fill.
  function swatchBackground(slot) {
    if (!slot || !slot.start) return "transparent";
    if (slot.end) return `linear-gradient(90deg, ${slot.start}, ${slot.end})`;
    return slot.start;
  }

  async function putLive() {
    try {
      const res = await fetch("/api/palette", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ colors: liveColors }),
      });
      if (!res.ok) {
        const b = await res.json().catch(() => ({}));
        setStatus(`palette: ${b.error || res.status}`, true);
        return;
      }
      setStatus("palette updated");
    } catch (e) {
      setStatus(`palette: ${e}`, true);
    }
  }

  function renderLive() {
    liveEl.innerHTML = "";
    for (let i = 0; i < SLOTS; i++) {
      const slot = liveColors[i];
      const row = document.createElement("div");
      row.className = "palette-row";

      const idx = document.createElement("span");
      idx.className = "palette-idx";
      idx.textContent = i;
      row.appendChild(idx);

      const start = document.createElement("input");
      start.type = "color";
      start.value = slot.start;
      start.title = "Start color";
      start.addEventListener("change", () => {
        slot.start = start.value;
        preview.style.background = swatchBackground(slot);
        putLive();
      });
      row.appendChild(start);

      const gradWrap = document.createElement("label");
      gradWrap.className = "palette-grad";
      const grad = document.createElement("input");
      grad.type = "checkbox";
      grad.checked = !!slot.end;
      grad.title = "Gradient (blend to an end color)";
      gradWrap.appendChild(grad);
      row.appendChild(gradWrap);

      const end = document.createElement("input");
      end.type = "color";
      end.value = slot.end || slot.start;
      end.title = "End color";
      end.disabled = !slot.end;
      end.addEventListener("change", () => {
        slot.end = end.value;
        preview.style.background = swatchBackground(slot);
        putLive();
      });
      row.appendChild(end);

      grad.addEventListener("change", () => {
        slot.end = grad.checked ? (end.value || slot.start) : null;
        end.disabled = !grad.checked;
        preview.style.background = swatchBackground(slot);
        putLive();
      });

      const preview = document.createElement("span");
      preview.className = "palette-preview";
      preview.style.background = swatchBackground(slot);
      row.appendChild(preview);

      liveEl.appendChild(row);
    }
  }

  // ---- Presets (mirrors scenes.js) ----

  function selectedName() {
    return nameInput ? nameInput.value.trim() : "";
  }

  async function savePreset() {
    const name = selectedName();
    if (!name) {
      setStatus("palettes: type a name to save", true);
      return;
    }
    if (presets.some((p) => p.name.toLowerCase() === name.toLowerCase())) {
      if (!confirm(`Overwrite palette "${name}"?`)) return;
    }
    await postPreset("/api/palettes", { name }, `saved "${name}"`);
  }

  async function applyPreset(name) {
    const target = name || selectedName();
    if (!target) {
      setStatus("palettes: pick a palette to apply", true);
      return;
    }
    await postPreset(
      `/api/palettes/${encodeURIComponent(target)}/apply`, null,
      `applied "${target}"`);
  }

  async function deletePreset(name) {
    const target = name || selectedName();
    if (!target) {
      setStatus("palettes: pick a palette to delete", true);
      return;
    }
    if (!confirm(`Delete palette "${target}"?`)) return;
    try {
      const res = await fetch(
        `/api/palettes/${encodeURIComponent(target)}`, { method: "DELETE" });
      if (!res.ok) {
        const b = await res.json().catch(() => ({}));
        setStatus(`palettes: ${b.error || res.status}`, true);
        return;
      }
      const b = await res.json();
      applyPalettes(b.palettes || []);
      setStatus(`deleted "${target}"`);
    } catch (e) {
      setStatus(`palettes: ${e}`, true);
    }
  }

  async function postPreset(url, body, okMsg) {
    try {
      const res = await fetch(url, {
        method: "POST",
        headers: body ? { "Content-Type": "application/json" } : undefined,
        body: body ? JSON.stringify(body) : undefined,
      });
      if (!res.ok) {
        const b = await res.json().catch(() => ({}));
        setStatus(`palettes: ${b.error || res.status}`, true);
        return;
      }
      const b = await res.json();
      applyPalettes(b.palettes || []);
      setStatus(okMsg);
    } catch (e) {
      setStatus(`palettes: ${e}`, true);
    }
  }

  function renderPresets() {
    presetsEl.innerHTML = "";

    const list = document.createElement("div");
    list.className = "palette-presets";
    if (presets.length === 0) {
      const empty = document.createElement("div");
      empty.className = "palette-empty";
      empty.textContent = "— no saved palettes —";
      list.appendChild(empty);
    }
    presets.forEach((p) => {
      const item = document.createElement("div");
      item.className = "palette-preset";
      item.title = `Apply "${p.name}"`;

      const strip = document.createElement("span");
      strip.className = "palette-strip";
      (p.colors || []).forEach((slot) => {
        const cell = document.createElement("span");
        cell.className = "palette-cell";
        cell.style.background = swatchBackground(slot);
        strip.appendChild(cell);
      });
      item.appendChild(strip);

      const label = document.createElement("span");
      label.className = "palette-name";
      label.textContent = p.name;
      item.appendChild(label);

      // Tap the row to select (fill the name box); the Apply button applies.
      item.addEventListener("click", () => {
        if (nameInput) nameInput.value = p.name;
      });
      const apply = document.createElement("button");
      apply.textContent = "Apply";
      apply.title = "Load this palette into the live palette";
      apply.addEventListener("click", (e) => {
        e.stopPropagation();
        applyPreset(p.name);
      });
      item.appendChild(apply);

      const del = document.createElement("button");
      del.textContent = "✕";
      del.title = "Delete this palette";
      del.addEventListener("click", (e) => {
        e.stopPropagation();
        deletePreset(p.name);
      });
      item.appendChild(del);

      list.appendChild(item);
    });
    presetsEl.appendChild(list);

    const saveRow = document.createElement("div");
    saveRow.className = "palette-saverow";
    nameInput = document.createElement("input");
    nameInput.type = "text";
    nameInput.placeholder = "Palette name";
    nameInput.addEventListener("keydown", (e) => {
      if (e.key === "Enter") savePreset();
    });
    saveRow.appendChild(nameInput);

    const save = document.createElement("button");
    save.textContent = "Save";
    save.title = "Save the live palette under this name";
    save.addEventListener("click", savePreset);
    saveRow.appendChild(save);

    presetsEl.appendChild(saveRow);
  }

  // ---- Full render + SSE appliers ----

  function render() {
    panel.innerHTML = "";
    const h = document.createElement("h2");
    h.textContent = "Palette";
    panel.appendChild(h);

    const liveTitle = document.createElement("div");
    liveTitle.className = "palette-subtitle";
    liveTitle.textContent = "Live palette";
    panel.appendChild(liveTitle);

    liveEl = document.createElement("div");
    liveEl.className = "palette-live";
    panel.appendChild(liveEl);
    renderLive();

    const presetTitle = document.createElement("div");
    presetTitle.className = "palette-subtitle";
    presetTitle.textContent = "Presets";
    panel.appendChild(presetTitle);

    presetsEl = document.createElement("div");
    panel.appendChild(presetsEl);
    renderPresets();
  }

  // "palette" SSE frame (or a PUT response): the winning eight-slot palette.
  // Skip the re-render while the user is editing a color here, so an in-flight
  // picker isn't yanked shut by another client's echo.
  function applyPalette(colors) {
    liveColors = normalizeLive(colors);
    if (liveEl && liveEl.contains(document.activeElement)) return;
    if (liveEl) renderLive();
  }

  // "palettes" SSE frame (or a mutating response): the full preset list.
  // Preserve whatever name the user has typed across the re-render.
  function applyPalettes(next) {
    presets = next || [];
    const typed = nameInput ? nameInput.value : "";
    if (presetsEl) renderPresets();
    if (typed && nameInput) nameInput.value = typed;
  }

  async function init() {
    render();
    try {
      const [pRes, psRes] = await Promise.all([
        fetch("/api/palette"),
        fetch("/api/palettes"),
      ]);
      if (pRes.ok) applyPalette((await pRes.json()).colors || []);
      if (psRes.ok) applyPalettes((await psRes.json()).palettes || []);
    } catch (e) {
      setStatus(`palette load: ${e}`, true);
    }
  }

  window.spectrumPaletteInit = init;
  window.spectrumApplyPalette = applyPalette;
  window.spectrumApplyPalettes = applyPalettes;
})();
