// Standalone wrapper around the shared real-dome canvas renderer.
(() => {
  "use strict";

  const root = document.getElementById("dome-simulator");
  const toggle = document.getElementById("dome-simulator-toggle");
  const projection = document.getElementById("dome-simulator-projection");
  const state = document.getElementById("dome-simulator-state");
  const canvas = document.getElementById("dome-simulator-canvas");
  if (!root || !toggle || !projection || !state || !canvas ||
      !window.SpectrumDomeRenderer) return;

  const renderer = window.SpectrumDomeRenderer.create({
    canvas,
    stateElement: state,
    defaultTopDown: false,
    onProjection: (topDown, label) => {
      projection.textContent = label;
      projection.setAttribute("aria-pressed", String(topDown));
      projection.disabled = false;
    },
  });

  async function start() {
    toggle.disabled = true;
    try {
      if (!await renderer.start()) {
        toggle.textContent = "Preview unavailable";
        return;
      }
      toggle.textContent = "Stop preview";
      toggle.classList.add("running");
    } catch (_) {
      state.textContent = "unavailable";
      toggle.textContent = "Retry preview";
    } finally {
      toggle.disabled = false;
    }
  }

  function stop() {
    renderer.stop();
    toggle.textContent = "Start preview";
    toggle.classList.remove("running");
  }

  toggle.addEventListener("click", () => {
    if (renderer.isRunning()) stop();
    else start();
  });
  projection.addEventListener("click", () => renderer.toggleProjection());
  window.addEventListener("pagehide", stop);
  start();
})();
