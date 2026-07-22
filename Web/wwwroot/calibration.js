// Two-stage dome mapping calibration. The server owns every draft pick and
// returns a fresh snapshot after each intent; this client only renders it.
(() => {
  "use strict";

  const RESOURCE = "domeCalibration";
  const container = document.getElementById("calibration");
  if (!container) return;

  function status(message, isError) {
    if (window.spectrumStatus) window.spectrumStatus(message, isError);
  }

  // A match intent advances to the next physical output. If the operator
  // double-clicks while the first request is still in flight, a second request
  // would otherwise confirm that unseen next output too. Serialize every
  // calibration intent in the browser and visibly disable the current controls
  // until its fresh server snapshot arrives.
  let actionInFlight = false;

  function setActionBusy(busy) {
    actionInFlight = busy;
    if (busy) {
      container.setAttribute("aria-busy", "true");
      container.querySelectorAll("button").forEach((element) => {
        element.dataset.calibrationBusyDisabled = String(element.disabled);
        element.disabled = true;
      });
      return;
    }
    container.removeAttribute("aria-busy");
    container.querySelectorAll(
      "button[data-calibration-busy-disabled]").forEach((element) => {
      element.disabled = element.dataset.calibrationBusyDisabled === "true";
      delete element.dataset.calibrationBusyDisabled;
    });
  }

  async function runAction(action) {
    if (actionInFlight) return;
    setActionBusy(true);
    try {
      await action();
    } finally {
      // A successful action normally replaces the controls in render(). On a
      // transport failure, restore the exact enabled/disabled state we saved.
      setActionBusy(false);
    }
  }

  function token() {
    return window.spectrumLocks
      ? window.spectrumLocks.lockToken(RESOURCE)
      : null;
  }

  async function call(action, body) {
    const headers = {};
    const held = token();
    if (held) headers["X-Spectrum-Lock-Token"] = held;
    if (body) headers["Content-Type"] = "application/json";
    try {
      const response = await fetch(`/api/maintenance/calibration/${action}`, {
        method: "POST",
        headers,
        body: body ? JSON.stringify(body) : undefined,
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) {
        status(`Calibration: ${payload.error || response.status}`, true);
        return payload.state || null;
      }
      return payload;
    } catch (_) {
      status("Calibration request could not reach Spectrum. Retry.", true);
      return null;
    }
  }

  async function start() {
    await runAction(async () => {
      const held = await window.spectrumLocks.ensureLock(RESOURCE);
      if (!held) return;
      render(await call("start"));
    });
  }

  async function navigate(direction) {
    await runAction(async () => {
      render(await call("navigate", { direction }));
    });
  }

  async function confirm() {
    await runAction(async () => { render(await call("confirm")); });
  }
  async function back() {
    await runAction(async () => { render(await call("back")); });
  }
  async function selectBox(box) {
    await runAction(async () => {
      render(await call("select-box", { box }));
    });
  }
  async function applyBoxOne() {
    await runAction(async () => {
      render(await call("apply-box-one"));
    });
  }
  async function recalibrateBox(box) {
    await runAction(async () => {
      render(await call("recalibrate-box", { box }));
    });
  }

  async function save() {
    await runAction(async () => {
      const state = await call("save");
      if (!state) return;
      if (state.stage === "idle") {
        await window.spectrumLocks.releaseLock(RESOURCE);
        status("All dome cable and strip mappings saved.");
      }
      render(state);
    });
  }

  async function cancel() {
    await runAction(async () => {
      const state = await call("cancel");
      await window.spectrumLocks.releaseLock(RESOURCE);
      render(state);
    });
  }

  function button(text, handler, enabled = true, className = "") {
    const element = document.createElement("button");
    element.type = "button";
    element.textContent = text;
    element.disabled = !enabled;
    if (className) element.className = className;
    element.addEventListener("click", handler);
    return element;
  }

  // The preview DOM and WebSocket survive state re-renders. Re-appending the
  // same node avoids reconnecting on every Previous/Next click.
  const preview = document.createElement("div");
  preview.className = "calib-preview dome-simulator";
  const previewHeading = document.createElement("h3");
  previewHeading.textContent = "Logical simulator candidate";
  const previewHelp = document.createElement("p");
  previewHelp.textContent =
    "This preview changes as you navigate. The illuminated physical OPC " +
    "cable or port remains fixed until you confirm, go back, or change boxes.";
  const previewToolbar = document.createElement("div");
  previewToolbar.className = "dome-simulator-toolbar";
  const projectionButton = button("View: Real top-down", () => {
    renderer.toggleProjection();
  });
  const previewState = document.createElement("span");
  previewState.className = "dome-simulator-state";
  previewState.setAttribute("role", "status");
  previewState.setAttribute("aria-live", "polite");
  const previewCanvas = document.createElement("canvas");
  previewCanvas.width = 420;
  previewCanvas.height = 420;
  previewCanvas.hidden = true;
  previewCanvas.setAttribute(
    "aria-label", "Logical dome calibration candidate");
  previewToolbar.appendChild(projectionButton);
  previewToolbar.appendChild(previewState);
  preview.appendChild(previewHeading);
  preview.appendChild(previewHelp);
  preview.appendChild(previewToolbar);
  preview.appendChild(previewCanvas);

  const renderer = window.SpectrumDomeRenderer
    ? window.SpectrumDomeRenderer.create({
        canvas: previewCanvas,
        stateElement: previewState,
        defaultTopDown: true,
        onProjection: (topDown, label) => {
          projectionButton.textContent = label;
          projectionButton.setAttribute("aria-pressed", String(topDown));
        },
      })
    : {
        start: async () => false,
        stop: () => {},
        toggleProjection: () => {},
      };

  function buildHardwareSummary(state) {
    const panel = document.createElement("div");
    panel.className = "calib-frame-summary";
    const hardware = document.createElement("div");
    const simulator = document.createElement("div");
    hardware.className = "calib-frame hardware";
    simulator.className = "calib-frame simulator";

    if (state.stage === "cables" &&
        state.currentStep < state.numCables) {
      hardware.textContent =
        `Physical output held: ${state.cableLabels[state.rawControllerCable]}`;
      simulator.textContent =
        `Simulator candidate: Dome endpoint ${state.endpointLabels[state.simulatorEndpoint]}`;
    } else if (state.stage === "strips" && state.rawControllerPort >= 0) {
      hardware.textContent =
        `Physical output held: Controller Box ${state.rawControllerBox + 1}, ` +
        `Port ${state.rawControllerPort + 1}`;
      simulator.textContent =
        `Simulator candidate: Box ${state.simulatorBox + 1}, ` +
        `Strip path ${state.simulatorPath + 1}`;
    } else {
      hardware.textContent = "Physical output: blank for review";
      simulator.textContent = "Simulator candidate: blank for review";
    }
    panel.appendChild(hardware);
    panel.appendChild(simulator);
    return panel;
  }

  function buildReadouts(state) {
    const section = document.createElement("div");
    section.className = "calib-readouts";
    const cable = document.createElement("div");
    const strips = document.createElement("div");
    const cableTitle = document.createElement("h3");
    const stripTitle = document.createElement("h3");
    const cableText = document.createElement("pre");
    const stripText = document.createElement("pre");
    cableTitle.textContent = "Controller cables → dome endpoints";
    stripTitle.textContent = "Physical ports → logical strip paths";
    cableText.textContent = state.cableReadout || "";
    stripText.textContent = state.stripReadout || "";
    cable.appendChild(cableTitle);
    cable.appendChild(cableText);
    strips.appendChild(stripTitle);
    strips.appendChild(stripText);
    section.appendChild(cable);
    section.appendChild(strips);
    return section;
  }

  function buildBoxSelector(state, ownsLock) {
    const selector = document.createElement("div");
    selector.className = "calib-box-selector";
    state.boxStatuses.forEach((boxStatus, box) => {
      const item = button(
        `Box ${box + 1} · ${boxStatus}`,
        () => selectBox(box),
        ownsLock,
        box === state.selectedBox ? "selected" : "");
      item.setAttribute("aria-pressed", String(box === state.selectedBox));
      selector.appendChild(item);
    });
    return selector;
  }

  function actionRow() {
    const row = document.createElement("div");
    row.className = "calib-controls";
    return row;
  }

  function renderCableStage(state, ownsLock) {
    const progress = document.createElement("p");
    progress.className = "calib-status";
    const controls = actionRow();
    if (state.currentStep < state.numCables) {
      progress.textContent =
        `Stage 1 of 2 · Controller cable ${state.currentStep + 1} of ` +
        `${state.numCables}. Navigate until the simulator matches the area ` +
        "lit on the real dome.";
      controls.appendChild(button(
        "Previous candidate", () => navigate(-1), ownsLock));
      controls.appendChild(button(
        "Next candidate", () => navigate(1), ownsLock));
      controls.appendChild(button(
        "Matches actual dome", confirm, ownsLock, "primary"));
      controls.appendChild(button(
        "Back one output", back, ownsLock && state.currentStep > 0));
    } else {
      progress.textContent =
        "Stage 1 complete. Review the cable mapping below before continuing.";
      controls.appendChild(button(
        "Continue to strip calibration", confirm, ownsLock, "primary"));
      controls.appendChild(button("Back one output", back, ownsLock));
    }
    controls.appendChild(button("Cancel", cancel, ownsLock, "action-danger"));
    container.appendChild(progress);
    container.appendChild(controls);
  }

  function nextUnfinishedBox(state) {
    for (let offset = 1; offset <= state.boxStatuses.length; offset++) {
      const box = (state.selectedBox + offset) % state.boxStatuses.length;
      if (state.stripSteps[box] < state.stripMappings[box].length) return box;
    }
    return -1;
  }

  function renderStripStage(state, ownsLock) {
    container.appendChild(buildBoxSelector(state, ownsLock));
    const progress = document.createElement("p");
    progress.className = "calib-status";
    const controls = actionRow();
    const box = state.selectedBox;
    const step = state.stripSteps[box];
    const count = state.stripMappings[box].length;
    if (step < count) {
      progress.textContent =
        `Stage 2 of 2 · Box ${box + 1}, physical port ${step + 1} of ` +
        `${count}. Match the simulator path to the fixed physical output.`;
      controls.appendChild(button(
        "Previous candidate", () => navigate(-1), ownsLock));
      controls.appendChild(button(
        "Next candidate", () => navigate(1), ownsLock));
      controls.appendChild(button(
        "Matches actual dome", confirm, ownsLock, "primary"));
      controls.appendChild(button(
        "Back one output", back, ownsLock && step > 0));
    } else {
      progress.textContent = `Box ${box + 1} is ${state.boxStatuses[box]}.`;
      const next = nextUnfinishedBox(state);
      if (next >= 0) {
        controls.appendChild(button(
          `Continue with Box ${next + 1}`,
          () => selectBox(next), ownsLock, "primary"));
      }
      controls.appendChild(button(
        "Recalibrate this box", () => recalibrateBox(box), ownsLock));
    }
    if (state.canApplyBoxOne) {
      controls.appendChild(button(
        "Apply Box 1 mapping to every box",
        applyBoxOne, ownsLock, "copy-action"));
    }
    controls.appendChild(button("Cancel", cancel, ownsLock, "action-danger"));
    container.appendChild(progress);
    container.appendChild(controls);
  }

  function renderReview(state, ownsLock) {
    container.appendChild(buildBoxSelector(state, ownsLock));
    const progress = document.createElement("p");
    progress.className = "calib-status";
    progress.textContent = state.saveable
      ? "Both stages are complete. Review the live mappings, then save them together."
      : "The draft is not complete; recalibrate an unfinished box.";
    const controls = actionRow();
    controls.appendChild(button(
      "Save all mappings", save, ownsLock && state.saveable, "primary"));
    controls.appendChild(button(
      `Recalibrate Box ${state.selectedBox + 1}`,
      () => recalibrateBox(state.selectedBox), ownsLock));
    controls.appendChild(button("Cancel", cancel, ownsLock, "action-danger"));
    container.appendChild(progress);
    container.appendChild(controls);
  }

  function render(state) {
    if (!state) return;
    container.innerHTML = "";
    const title = document.createElement("h2");
    title.textContent = "Dome mapping calibration";
    container.appendChild(title);

    const ownsLock = !!token();
    if (!state.active) {
      renderer.stop();
      const help = document.createElement("p");
      help.className = "calib-status";
      help.textContent = state.hasSavedMapping
        ? "Saved mappings are loaded as the initial guesses for a new calibration."
        : "Missing or invalid mappings will start from identity guesses.";
      container.appendChild(help);
      container.appendChild(button(
        "Start two-stage calibration", start, true, "primary"));
      container.appendChild(buildReadouts(state));
      return;
    }

    if (!ownsLock) {
      const warning = document.createElement("p");
      warning.className = "calib-lock-warning";
      warning.textContent =
        "Calibration is active in another client. This view is read-only.";
      container.appendChild(warning);
    }
    container.appendChild(buildHardwareSummary(state));
    container.appendChild(preview);
    renderer.start().catch(() => {
      previewState.textContent = "preview unavailable";
    });

    if (state.stage === "cables") renderCableStage(state, ownsLock);
    else if (state.stage === "strips") renderStripStage(state, ownsLock);
    else if (state.stage === "review") renderReview(state, ownsLock);
    container.appendChild(buildReadouts(state));
  }

  // app.js invokes this hook before releasing the lease on page exit, giving
  // the server a chance to blank diagnostics immediately. The watchdog remains
  // the fallback if the browser drops the keepalive request.
  window.spectrumCalibrationPageHide = () => {
    const held = token();
    renderer.stop();
    if (!held) return;
    fetch("/api/maintenance/calibration/cancel", {
      method: "POST",
      headers: { "X-Spectrum-Lock-Token": held },
      keepalive: true,
    }).catch(() => {});
  };

  window.spectrumCalibrationInit = async () => {
    try {
      const response = await fetch("/api/maintenance/calibration");
      if (!response.ok) throw new Error(String(response.status));
      render(await response.json());
    } catch (_) {
      container.innerHTML = "";
      const title = document.createElement("h2");
      title.textContent = "Dome mapping calibration";
      const error = document.createElement("p");
      error.textContent = "Calibration state is unavailable. Reload to retry.";
      container.appendChild(title);
      container.appendChild(error);
    }
  };
})();
