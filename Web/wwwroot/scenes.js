// The saved-scene bar for the user surface. A scene is a named snapshot of the
// whole layer stack plus the two cross-layer globals (fade/hue speed). This
// panel lists saved scenes in a dropdown and offers Save (under a typed name),
// Load (apply the selected scene), and Delete.
//
// Save/Delete POST/DELETE to /api/scenes*; the "scenes" SSE frame carries the
// updated name list so every client's dropdown converges. Applying a scene
// (Load) replaces the layer stack + globals server-side, which flow back through
// the existing "layers" and parameter frames — so the layers panel re-renders on
// its own with no special handling here.

(function () {
  const panel = document.getElementById("scenes");
  if (!panel) return;

  const setStatus = window.spectrumStatus || function () {};

  // The saved scene names, in stored order. Seeded by the initial GET and kept
  // current by the "scenes" SSE frame.
  let names = [];
  let select = null;
  let nameInput = null;

  function selectedName() {
    // Prefer the typed name; fall back to the dropdown selection.
    const typed = nameInput ? nameInput.value.trim() : "";
    if (typed) return typed;
    return select && select.value ? select.value : "";
  }

  async function saveScene() {
    const name = selectedName();
    if (!name) {
      setStatus("scenes: type a name to save", true);
      return;
    }
    if (names.some((n) => n.toLowerCase() === name.toLowerCase())) {
      if (!confirm(`Overwrite scene "${name}"?`)) return;
    }
    await post("/api/scenes", { name }, `saved "${name}"`);
  }

  async function loadScene() {
    const name = select && select.value ? select.value : selectedName();
    if (!name) {
      setStatus("scenes: pick a scene to load", true);
      return;
    }
    await post(
      `/api/scenes/${encodeURIComponent(name)}/apply`, null, `loaded "${name}"`);
  }

  async function deleteScene() {
    const name = select && select.value ? select.value : selectedName();
    if (!name) {
      setStatus("scenes: pick a scene to delete", true);
      return;
    }
    if (!confirm(`Delete scene "${name}"?`)) return;
    try {
      const res = await fetch(`/api/scenes/${encodeURIComponent(name)}`, {
        method: "DELETE",
      });
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        setStatus(`scenes: ${body.error || res.status}`, true);
        return;
      }
      const body = await res.json();
      applyScenes(body.scenes || []);
      setStatus(`deleted "${name}"`);
    } catch (e) {
      setStatus(`scenes: ${e}`, true);
    }
  }

  // POST helper shared by save/apply: sends optional JSON body, refreshes the
  // dropdown from the response, and reports status.
  async function post(url, body, okMsg) {
    try {
      const res = await fetch(url, {
        method: "POST",
        headers: body ? { "Content-Type": "application/json" } : undefined,
        body: body ? JSON.stringify(body) : undefined,
      });
      if (!res.ok) {
        const b = await res.json().catch(() => ({}));
        setStatus(`scenes: ${b.error || res.status}`, true);
        return;
      }
      const b = await res.json();
      applyScenes(b.scenes || []);
      setStatus(okMsg);
    } catch (e) {
      setStatus(`scenes: ${e}`, true);
    }
  }

  function render() {
    panel.innerHTML = "";
    const h = document.createElement("h2");
    h.textContent = "Scenes";
    panel.appendChild(h);

    if (names.length === 0) {
      const empty = document.createElement("p");
      empty.className = "empty-state";
      empty.textContent = "No scenes saved yet. Name the current look below to make it available during the show.";
      panel.appendChild(empty);
    }

    const row = document.createElement("div");
    row.className = "scene-row";

    select = document.createElement("select");
    const blank = document.createElement("option");
    blank.value = "";
    blank.textContent = names.length ? "— pick a scene —" : "— no saved scenes —";
    select.appendChild(blank);
    names.forEach((n) => {
      const o = document.createElement("option");
      o.value = n;
      o.textContent = n;
      select.appendChild(o);
    });
    // Selecting a saved scene fills the name box so Save-over is one click.
    select.addEventListener("change", () => {
      if (select.value) nameInput.value = select.value;
    });
    row.appendChild(select);

    const load = document.createElement("button");
    load.textContent = "Load";
    load.title = "Apply the selected scene";
    load.addEventListener("click", loadScene);
    row.appendChild(load);

    const del = document.createElement("button");
    del.textContent = "Delete";
    del.className = "action-danger";
    del.title = "Delete the selected scene";
    del.addEventListener("click", deleteScene);
    row.appendChild(del);

    panel.appendChild(row);

    const saveRow = document.createElement("div");
    saveRow.className = "scene-row";

    nameInput = document.createElement("input");
    nameInput.type = "text";
    nameInput.placeholder = "Scene name";
    nameInput.addEventListener("keydown", (e) => {
      if (e.key === "Enter") saveScene();
    });
    saveRow.appendChild(nameInput);

    const save = document.createElement("button");
    save.textContent = "Save";
    save.title = "Save the current look under this name";
    save.addEventListener("click", saveScene);
    saveRow.appendChild(save);

    panel.appendChild(saveRow);
  }

  // "scenes" SSE frame (or a mutating response): the full name list. Preserve
  // whatever the user has typed/selected across a re-render.
  function applyScenes(next) {
    names = next || [];
    const typed = nameInput ? nameInput.value : "";
    const picked = select ? select.value : "";
    render();
    if (typed) nameInput.value = typed;
    if (picked && names.includes(picked)) select.value = picked;
  }

  async function init() {
    render();
    try {
      const res = await fetch("/api/scenes");
      if (!res.ok) {
        setStatus(`scenes load: ${res.status}`, true);
        return;
      }
      const body = await res.json();
      applyScenes(body.scenes || []);
    } catch (e) {
      setStatus(`scenes load: ${e}`, true);
    }
  }

  window.spectrumScenesInit = init;
  window.spectrumApplyScenes = applyScenes;
})();
