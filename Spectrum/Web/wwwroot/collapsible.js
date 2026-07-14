// Accessible, persistent disclosure sections. Renderers may replace a panel's
// children at any time; the observer re-establishes the button/content
// structure after each render without requiring every feature module to know
// about disclosure behavior.
(function () {
  const KEY = "spectrum.section-state.v2";

  function loadState() {
    try {
      const parsed = JSON.parse(localStorage.getItem(KEY) || "{}");
      return parsed && !Array.isArray(parsed) ? parsed : {};
    } catch (_) {
      return {};
    }
  }

  const state = loadState();

  function saveState() {
    try {
      localStorage.setItem(KEY, JSON.stringify(state));
    } catch (_) { /* Disclosure still works when storage is unavailable. */ }
  }

  function contentId(section) {
    return `${section.id || "spectrum-section"}-content`;
  }

  function sync(section, button) {
    const collapsed = section.classList.contains("collapsed");
    button.setAttribute("aria-expanded", String(!collapsed));
  }

  function enhance(section) {
    if (!section.classList.contains("collapsible")) return;
    const heading = section.firstElementChild;
    if (!heading || heading.tagName !== "H2") return;

    let button = heading.querySelector(":scope > .section-toggle");
    if (!button) {
      const title = heading.textContent.trim() || "Section";
      heading.textContent = "";
      button = document.createElement("button");
      button.type = "button";
      button.className = "section-toggle";
      button.textContent = title;
      heading.appendChild(button);
    }

    let content = section.querySelector(":scope > .collapsible-content");
    if (!content) {
      content = document.createElement("div");
      content.className = "collapsible-content";
      content.id = contentId(section);
      const children = Array.from(section.children).filter((el) => el !== heading);
      children.forEach((el) => content.appendChild(el));
      section.appendChild(content);
    }

    button.setAttribute("aria-controls", content.id);
    if (!button.dataset.disclosureBound) {
      button.dataset.disclosureBound = "true";
      const toggle = () => {
        const collapsed = section.classList.toggle("collapsed");
        if (section.id) {
          state[section.id] = collapsed;
          saveState();
        }
        sync(section, button);
      };
      button.addEventListener("click", toggle);
      // Buttons provide this natively, but an explicit path keeps the control
      // reliable in embedded browsers and remote-control webviews too.
      button.addEventListener("keydown", (event) => {
        if (event.key !== "Enter" && event.key !== " ") return;
        event.preventDefault();
        toggle();
      });
    }

    if (!section.dataset.disclosureRestored) {
      section.dataset.disclosureRestored = "true";
      const remembered = section.id &&
        Object.prototype.hasOwnProperty.call(state, section.id)
        ? state[section.id]
        : section.dataset.defaultCollapsed === "true" ||
          (section.dataset.defaultCollapsedMobile === "true" &&
           window.matchMedia("(max-width: 760px)").matches);
      section.classList.toggle("collapsed", Boolean(remembered));
    }
    sync(section, button);
  }

  function enhanceAll(root) {
    if (root.nodeType !== Node.ELEMENT_NODE && root !== document) return;
    if (root.matches && root.matches(".collapsible")) enhance(root);
    root.querySelectorAll?.(".collapsible").forEach(enhance);
  }

  enhanceAll(document);
  new MutationObserver((mutations) => {
    mutations.forEach((mutation) => {
      enhance(mutation.target.closest?.(".collapsible") || mutation.target);
      mutation.addedNodes.forEach(enhanceAll);
    });
  }).observe(document.body, { childList: true, subtree: true });
})();
