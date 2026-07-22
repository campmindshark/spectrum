// Audio capture-device selection and live input health for maintenance.
(function () {
  const container = document.getElementById("audio");
  if (!container) return;

  const POLL_MS = 1000;
  let select = null, statusEl = null, meter = null, timer = null;

  function status(message, isError) {
    if (window.spectrumStatus) window.spectrumStatus(message, isError);
  }

  async function selectDevice() {
    const value = select.value;
    try {
      const response = await fetch(
        "/api/maintenance/parameters/audioDeviceID",
        {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ value }),
        }
      );
      status(
        response.ok
          ? `Audio device: ${value || "(none)"}`
          : `audio device: ${response.status}`,
        !response.ok
      );
      await poll();
    } catch (error) {
      status(`audio device: ${error}`, true);
    }
  }

  function mount() {
    container.innerHTML = "";
    const title = document.createElement("h2");
    title.textContent = "Audio input";
    container.appendChild(title);

    const row = document.createElement("div");
    row.className = "param";
    const label = document.createElement("label");
    label.textContent = "Capture device";
    select = document.createElement("select");
    select.addEventListener("change", selectDevice);
    label.appendChild(select);
    row.appendChild(label);
    container.appendChild(row);

    statusEl = document.createElement("div");
    statusEl.className = "calib-status";
    statusEl.textContent = "Discovering audio devices…";
    container.appendChild(statusEl);

    meter = document.createElement("progress");
    meter.min = 0;
    meter.max = 1;
    meter.value = 0;
    meter.setAttribute("aria-label", "Audio input level");
    meter.style.width = "100%";
    container.appendChild(meter);
  }

  function rebuild(state) {
    const devices = state.availableDevices || [];
    const selected = state.selectedDeviceId || "";
    if (document.activeElement !== select) {
      select.innerHTML = "";
      const none = document.createElement("option");
      none.value = "";
      none.textContent = "(none)";
      select.appendChild(none);
      devices.forEach((device) => {
        const option = document.createElement("option");
        option.value = device.id;
        option.textContent = device.name === device.id
          ? device.id
          : `${device.name} (${device.id})`;
        select.appendChild(option);
      });
      if (selected && !devices.some((device) => device.id === selected)) {
        const missing = document.createElement("option");
        missing.value = selected;
        missing.textContent = `${selected} (missing)`;
        select.appendChild(missing);
      }
      select.value = selected;
    }

    const volume = Math.max(0, Math.min(1, Number(state.volume) || 0));
    meter.value = volume;
    if (state.lastError) {
      statusEl.textContent = `${state.backend}: ${state.lastError}`;
      statusEl.style.color = "#ff6b6b";
    } else if (!selected) {
      statusEl.textContent = `${state.backend}: no device selected`;
      statusEl.style.color = "#cba";
    } else {
      statusEl.textContent =
        `${state.backend}: ${state.active ? "capturing" : "stopped"} · ` +
        `${Math.round(volume * 100)}% peak`;
      statusEl.style.color = state.active ? "#6fdf6f" : "#cba";
    }
  }

  async function poll() {
    try {
      const response = await fetch("/api/maintenance/audio");
      if (!response.ok) {
        status(`audio: ${response.status}`, true);
        return;
      }
      rebuild(await response.json());
    } catch (error) {
      status(`audio: ${error}`, true);
    }
  }

  window.spectrumAudioInit = function () {
    mount();
    poll();
    if (timer) clearInterval(timer);
    timer = setInterval(poll, POLL_MS);
  };
})();
