// Named palettes are the live color sources selected by layers. This panel
// chooses one palette to edit; it never copies colors through a bank or Apply.

(function () {
  const panel = document.getElementById("palette");
  if (!panel) return;

  const setStatus = window.spectrumStatus || function () {};
  const SLOTS = 8;
  let palettes = [];
  let selectedName = "";
  let liveEl = null;
  let nameInput = null;
  let pendingPalettes = null;

  function normalizeColors(colors) {
    const result = [];
    for (let i = 0; i < SLOTS; i++) {
      const color = colors && colors[i] ? colors[i] : {};
      result.push({
        start: color.start || "#000000",
        end: color.end || null,
      });
    }
    return result;
  }

  function normalizePalettes(next) {
    return (next || []).map((palette) => ({
      name: palette.name,
      colors: normalizeColors(palette.colors),
    }));
  }

  function selectedPalette() {
    return palettes.find((palette) => palette.name === selectedName) ||
      palettes[0] || null;
  }

  function copyName(source) {
    const base = `${source ? source.name : "Palette"} copy`;
    const existing = new Set(
      palettes.map((palette) => palette.name.toLocaleLowerCase()));
    let candidate = base;
    let suffix = 2;
    while (existing.has(candidate.toLocaleLowerCase())) {
      candidate = `${base} ${suffix++}`;
    }
    return candidate;
  }

  function swatchBackground(slot) {
    if (!slot || !slot.start) return "transparent";
    return slot.end
      ? `linear-gradient(90deg, ${slot.start}, ${slot.end})`
      : slot.start;
  }

  async function putColors(palette) {
    if (!palette) return;
    try {
      const res = await fetch(
        `/api/palettes/${encodeURIComponent(palette.name)}`,
        {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ colors: palette.colors }),
        });
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setStatus(`palette: ${body.error || res.status}`, true);
        return;
      }
      applyPalettes((await res.json()).palettes || []);
      setStatus(`updated "${palette.name}"`);
    } catch (e) {
      setStatus(`palette: ${e}`, true);
    }
  }

  function renderColors() {
    liveEl.innerHTML = "";
    const palette = selectedPalette();
    if (!palette) return;

    palette.colors.forEach((slot, index) => {
      const row = document.createElement("div");
      row.className = "palette-row";

      const idx = document.createElement("span");
      idx.className = "palette-idx";
      idx.textContent = index;
      row.appendChild(idx);

      const start = document.createElement("input");
      start.type = "color";
      start.value = slot.start;
      start.title = "Start color";
      start.setAttribute("aria-label", `Palette slot ${index + 1} start color`);
      row.appendChild(start);

      const gradWrap = document.createElement("label");
      gradWrap.className = "palette-grad";
      const grad = document.createElement("input");
      grad.type = "checkbox";
      grad.checked = !!slot.end;
      grad.title = "Gradient (blend to an end color)";
      grad.setAttribute("aria-label", `Use a gradient for palette slot ${index + 1}`);
      gradWrap.appendChild(grad);
      row.appendChild(gradWrap);

      const end = document.createElement("input");
      end.type = "color";
      end.value = slot.end || slot.start;
      end.title = "End color";
      end.disabled = !slot.end;
      end.setAttribute("aria-label", `Palette slot ${index + 1} end color`);
      row.appendChild(end);

      const preview = document.createElement("span");
      preview.className = "palette-preview";
      preview.style.background = swatchBackground(slot);
      row.appendChild(preview);

      start.addEventListener("change", () => {
        slot.start = start.value;
        preview.style.background = swatchBackground(slot);
        putColors(palette);
      });
      end.addEventListener("change", () => {
        slot.end = end.value;
        preview.style.background = swatchBackground(slot);
        putColors(palette);
      });
      grad.addEventListener("change", () => {
        slot.end = grad.checked ? (end.value || slot.start) : null;
        end.disabled = !grad.checked;
        preview.style.background = swatchBackground(slot);
        putColors(palette);
      });

      liveEl.appendChild(row);
    });
  }

  async function addPalette() {
    const source = selectedPalette();
    const typedName = nameInput.value.trim();
    // The shared name field normally contains the selected palette's name for
    // Rename. A plain "Add copy" click should still work without first
    // overwriting that field, while a genuinely different typed name wins.
    const name = !typedName || (source &&
      typedName.toLocaleLowerCase() === source.name.toLocaleLowerCase())
      ? copyName(source)
      : typedName;
    await mutate(
      "/api/palettes",
      { name, sourceName: source ? source.name : null },
      `added "${name}"`,
      () => { selectedName = name; });
  }

  async function renamePalette() {
    const selected = selectedPalette();
    const newName = nameInput.value.trim();
    if (!selected || !newName) {
      setStatus("palettes: select a palette and type its new name", true);
      return;
    }
    const oldName = selected.name;
    await mutate(
      `/api/palettes/${encodeURIComponent(oldName)}/rename`,
      { newName },
      `renamed "${oldName}" to "${newName}"`,
      () => { selectedName = newName; });
  }

  async function deletePalette(name) {
    if (!confirm(`Delete palette "${name}"? Layers using it will switch to the first palette.`)) {
      return;
    }
    try {
      const res = await fetch(
        `/api/palettes/${encodeURIComponent(name)}`, { method: "DELETE" });
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setStatus(`palettes: ${body.error || res.status}`, true);
        return;
      }
      if (selectedName === name) selectedName = "";
      applyPalettes((await res.json()).palettes || []);
      setStatus(`deleted "${name}"`);
    } catch (e) {
      setStatus(`palettes: ${e}`, true);
    }
  }

  async function mutate(url, body, message, beforeApply) {
    try {
      const res = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      if (!res.ok) {
        const response = await res.json().catch(() => ({}));
        setStatus(`palettes: ${response.error || res.status}`, true);
        return;
      }
      beforeApply();
      applyPalettes((await res.json()).palettes || []);
      setStatus(message);
    } catch (e) {
      setStatus(`palettes: ${e}`, true);
    }
  }

  function render() {
    panel.innerHTML = "";
    const title = document.createElement("h2");
    title.textContent = "Palettes";
    panel.appendChild(title);

    const list = document.createElement("div");
    list.className = "palette-presets";
    palettes.forEach((palette) => {
      const item = document.createElement("div");
      item.className = "palette-preset";
      if (palette.name === selectedPalette()?.name) {
        item.classList.add("selected");
      }
      item.title = `Edit "${palette.name}"`;

      const strip = document.createElement("span");
      strip.className = "palette-strip";
      palette.colors.forEach((slot) => {
        const cell = document.createElement("span");
        cell.className = "palette-cell";
        cell.style.background = swatchBackground(slot);
        strip.appendChild(cell);
      });
      item.appendChild(strip);

      const label = document.createElement("span");
      label.className = "palette-name";
      label.textContent = palette.name;
      item.appendChild(label);

      const del = document.createElement("button");
      del.textContent = "Delete";
      del.className = "action-danger";
      del.addEventListener("click", (event) => {
        event.stopPropagation();
        deletePalette(palette.name);
      });
      item.appendChild(del);

      item.addEventListener("click", () => {
        selectedName = palette.name;
        render();
      });
      list.appendChild(item);
    });
    panel.appendChild(list);

    const nameRow = document.createElement("div");
    nameRow.className = "palette-saverow";
    nameInput = document.createElement("input");
    nameInput.type = "text";
    nameInput.placeholder = "Palette name";
    nameInput.value = selectedPalette()?.name || "";
    nameRow.appendChild(nameInput);

    const rename = document.createElement("button");
    rename.textContent = "Rename";
    rename.addEventListener("click", renamePalette);
    nameRow.appendChild(rename);

    const add = document.createElement("button");
    add.textContent = "Add copy";
    add.title = "Duplicate the selected colors under this name";
    add.addEventListener("click", addPalette);
    nameRow.appendChild(add);
    panel.appendChild(nameRow);

    const subtitle = document.createElement("div");
    subtitle.className = "palette-subtitle";
    subtitle.textContent = selectedPalette()
      ? `Editing ${selectedPalette().name}`
      : "No palette selected";
    panel.appendChild(subtitle);

    liveEl = document.createElement("div");
    liveEl.className = "palette-live";
    liveEl.addEventListener("focusout", () => {
      setTimeout(() => {
        if (pendingPalettes && !liveEl.contains(document.activeElement)) {
          const pending = pendingPalettes;
          pendingPalettes = null;
          applyPalettes(pending);
        }
      }, 0);
    });
    panel.appendChild(liveEl);
    renderColors();
  }

  function applyPalettes(next) {
    if (liveEl && liveEl.contains(document.activeElement)) {
      pendingPalettes = next;
      return;
    }
    palettes = normalizePalettes(next);
    if (!palettes.some((palette) => palette.name === selectedName)) {
      selectedName = palettes[0]?.name || "";
    }
    render();
    if (window.spectrumApplyPaletteOptions) {
      window.spectrumApplyPaletteOptions(
        palettes.map((palette) => palette.name));
    }
  }

  async function init() {
    render();
    try {
      const res = await fetch("/api/palettes");
      if (res.ok) applyPalettes((await res.json()).palettes || []);
    } catch (e) {
      setStatus(`palette load: ${e}`, true);
    }
  }

  window.spectrumPaletteInit = init;
  window.spectrumApplyPalettes = applyPalettes;
})();
