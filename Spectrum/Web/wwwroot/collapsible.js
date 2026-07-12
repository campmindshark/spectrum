// Makes the labeled sections on the web surfaces collapsible so the control
// stack fits a phone screen — tap a section's <h2> header to fold it away.
//
// A collapsible section is a persistent container div carrying the `collapsible`
// class whose first child is its <h2> header. Clicking the header toggles a
// `collapsed` class on the container; the rest is CSS (hide every child except
// the header, rotate the chevron). Two properties make this robust:
//   - The class lives on the container, which each section's renderer keeps
//     across its innerHTML rebuilds (only the inner content is replaced), so the
//     collapsed state survives live updates.
//   - The click is caught by delegation on document, so a header recreated by a
//     re-render is still wired up without re-binding anything.
// Collapsed state is remembered per section id in localStorage so a phone keeps
// your choices across reloads (and across the user/maintenance pages, same
// origin).

(function () {
  const KEY = "spectrum.collapsed";

  function loadSet() {
    try {
      return new Set(JSON.parse(localStorage.getItem(KEY) || "[]"));
    } catch (_) {
      return new Set();
    }
  }
  function saveSet(set) {
    try {
      localStorage.setItem(KEY, JSON.stringify([...set]));
    } catch (_) { /* private mode / quota — collapsing still works this session */ }
  }

  const collapsed = loadSet();

  // Restore remembered state. The containers are static markup, so they exist
  // now even though each section fills in its content asynchronously; applying
  // `collapsed` up front means the content renders already folded.
  document.querySelectorAll(".collapsible").forEach((el) => {
    if (el.id && collapsed.has(el.id)) el.classList.add("collapsed");
  });

  document.addEventListener("click", (e) => {
    const h2 = e.target.closest("h2");
    if (!h2) return;
    const section = h2.parentElement;
    if (!section || !section.classList.contains("collapsible")) return;
    if (section.firstElementChild !== h2) return; // only the header toggles
    const nowCollapsed = section.classList.toggle("collapsed");
    if (section.id) {
      if (nowCollapsed) collapsed.add(section.id);
      else collapsed.delete(section.id);
      saveSet(collapsed);
    }
  });
})();
