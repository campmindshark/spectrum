// Live wand/orientation-device status for the maintenance surface — the web
// port of WandStatusWindow. Polls the read-only snapshot and renders one row
// per connected device with connection-quality coloring, plus a Calibrate All
// button (the same global recalibration the native window fires).
//
// Unlike the parameter controls and telemetry (which arrive over the SSE change
// feed), this is pull-based on a timer, mirroring the native window's 400ms
// DispatcherTimer poll of OrientationInput.

(function () {
  const container = document.getElementById("wands");
  if (!container) return;

  // Poll cadence. OrientationInput forgets a silent device after ~1s, so a ~1s
  // poll surfaces drops about as fast as the server times them out.
  const POLL_MS = 1000;

  // The serial receiver is alive when the most recent heartbeat OR data frame
  // arrived within this window (~3 missed 500ms heartbeats). Mirrors
  // WandSerialReceiver.RECEIVER_ALIVE_MS — the two surfaces must agree.
  const RECEIVER_ALIVE_MS = 1500;

  // Quality heuristic, mirroring WandRow: staleness, high jitter, or high
  // packet loss → Poor.
  const STALE_MS = 400, JITTER_FAIR_MS = 8, JITTER_POOR_MS = 20;
  const LOSS_FAIR_FRACTION = 0.01, LOSS_POOR_FRACTION = 0.05;
  // The wand radios transmit at a hard 400 Hz ceiling; a rate that has
  // collapsed well below it is a degraded link even when jitter/loss read fine.
  // Mirrors OrientationInput.WandMaxTransmitRateHz and WandRow's RateFair/Poor
  // fractions — the surfaces must agree.
  const WAND_MAX_RATE_HZ = 400;
  const RATE_FAIR_FRACTION = 0.6, RATE_POOR_FRACTION = 0.3;
  const ROW_COLORS =
    { good: "#ddd", fair: "#ffd24d", poor: "#ff6b6b", wait: "#ddd" };
  const QUALITY_LABEL = { good: "Good", fair: "Fair", poor: "Poor", wait: "…" };

  const COLUMNS = [
    "ID", "Type", "Button", "Motion", "Quality", "Rate (Hz, ≤400)", "Jitter (ms)",
    "Loss (%)", "Data (kB/s)", "Packets", "Last (ms)",
    "Orientation (W X Y Z)", "Speed",
  ];

  let summaryEl = null, tbodyEl = null, timer = null;
  let receiverSelect = null, receiverStatusEl = null;

  function status(msg, isError) {
    if (window.spectrumStatus) window.spectrumStatus(msg, isError);
  }

  function quality(row) {
    if (row.packetCount < 2) return "wait";
    const rateFraction = row.updateRateHz / WAND_MAX_RATE_HZ;
    if (row.millisSinceLastPacket > STALE_MS || row.jitterMs > JITTER_POOR_MS ||
        row.packetLossFraction > LOSS_POOR_FRACTION ||
        rateFraction < RATE_POOR_FRACTION) {
      return "poor";
    }
    if (row.jitterMs > JITTER_FAIR_MS ||
        row.packetLossFraction > LOSS_FAIR_FRACTION ||
        rateFraction < RATE_FAIR_FRACTION) {
      return "fair";
    }
    return "good";
  }

  const f = (v, digits) => v.toFixed(digits);

  function cells(row) {
    return [
      row.deviceId,
      row.typeName,
      row.actionFlag === 0 ? "—" : "Button " + row.actionFlag,
      // "Still" = transmitting but not physically moving, so excluded from
      // the visualization (mirrors the native Motion column).
      row.isMoving ? "Moving" : "Still",
      QUALITY_LABEL[quality(row)],
      row.updateRateHz > 0 ? f(row.updateRateHz, 1) : "—",
      row.packetCount > 1 ? f(row.jitterMs, 2) : "—",
      row.packetCount > 1 ? f(row.packetLossFraction * 100, 1) : "—",
      row.dataRateBytesPerSec > 0 ? f(row.dataRateBytesPerSec / 1000, 2) : "—",
      row.packetCount,
      f(row.millisSinceLastPacket, 0),
      [row.w, row.x, row.y, row.z].map((n) => f(n, 2).padStart(6)).join(" "),
      row.hasSpeed ? f(row.speed, 3) : "—",
    ];
  }

  async function calibrateAll() {
    try {
      const res = await fetch("/api/maintenance/wands/calibrate", { method: "POST" });
      status(res.ok ? "Calibrated all wands." : `calibrate: ${res.status}`, !res.ok);
    } catch (e) {
      status(`calibrate: ${e}`, true);
    }
  }

  // Genuine user pick of a receiver port. Writes the real port value (never the
  // label) through the same param path as every other maintenance control.
  // app.js putValue sends exactly { "value": ... } with a JSON content-type
  // (app.js:150,160); this small duplication is faithful to that.
  async function onReceiverChange() {
    const value = receiverSelect.value;
    try {
      const res = await fetch(
        "/api/maintenance/parameters/wandSerialPort",
        {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ value }),
        }
      );
      status(
        res.ok ? `Receiver port: ${value || "(none)"}` : `port: ${res.status}`,
        !res.ok
      );
    } catch (e) {
      status(`port: ${e}`, true);
    }
  }

  // Rebuild the <select> from the server snapshot. The server value is
  // authoritative for the displayed selection and always keeps the
  // configured-but-missing port as an option. Setting options/value here is
  // programmatic and never fires 'change'. Skip the rebuild while the element is
  // focused so it doesn't fight a user mid-selection.
  function rebuildReceiver(serial) {
    if (document.activeElement === receiverSelect) return;
    const ports = serial.availablePorts || [];
    const selected = serial.selectedPort || "";
    const opts = [""].concat(ports);
    if (selected && !ports.includes(selected)) opts.push(selected);

    receiverSelect.innerHTML = "";
    opts.forEach((p) => {
      const o = document.createElement("option");
      o.value = p;
      o.textContent = p === ""
        ? "(none)"
        : (ports.includes(p) ? p : `${p} (missing)`);
      receiverSelect.appendChild(o);
    });
    receiverSelect.value = selected;
  }

  function updateReceiverStatus(serial) {
    const r = serial.receiver || {};
    const selected = serial.selectedPort || "";
    let text, color;
    if (!selected) {
      text = "No port selected"; color = "#cba";
    } else if (r.lastError) {
      text = `Error: ${r.lastError}`; color = "#ff6b6b";
    } else if (!r.portOpen) {
      text = "Opening…"; color = "#cba";
    } else {
      const since = Math.min(
        r.millisSinceLastHeartbeat, r.millisSinceLastFrame);
      if (since < RECEIVER_ALIVE_MS) {
        text = `Receiver connected (${(since / 1000).toFixed(1)} s ago)`;
        color = "#6fdf6f";
      } else {
        text = "Port open — no data"; color = "#ff6b6b";
      }
    }
    receiverStatusEl.textContent = text;
    receiverStatusEl.style.color = color;
  }

  // Build the static structure once so the poll only swaps table rows — the
  // Calibrate All button and header keep their identity across refreshes.
  function mount() {
    container.innerHTML = "";
    const title = document.createElement("h2");
    title.textContent = "Wand status";
    container.appendChild(title);

    // Wand receiver (USB-CDC ESP-NOW) port selector + liveness, above the table.
    const receiverRow = document.createElement("div");
    receiverRow.className = "calib-status";
    const receiverLabel = document.createElement("label");
    receiverLabel.textContent = "Wand receiver: ";
    receiverSelect = document.createElement("select");
    receiverSelect.addEventListener("change", onReceiverChange);
    receiverLabel.appendChild(receiverSelect);
    receiverRow.appendChild(receiverLabel);
    receiverStatusEl = document.createElement("span");
    receiverStatusEl.style.marginLeft = "0.6rem";
    receiverStatusEl.textContent = "…";
    receiverRow.appendChild(receiverStatusEl);
    container.appendChild(receiverRow);

    summaryEl = document.createElement("div");
    summaryEl.className = "calib-status";
    summaryEl.textContent = "Listening for devices…";
    container.appendChild(summaryEl);

    const table = document.createElement("table");
    table.className = "wand-table";
    const thead = document.createElement("thead");
    const hr = document.createElement("tr");
    COLUMNS.forEach((c) => {
      const th = document.createElement("th");
      th.textContent = c;
      hr.appendChild(th);
    });
    thead.appendChild(hr);
    table.appendChild(thead);
    tbodyEl = document.createElement("tbody");
    table.appendChild(tbodyEl);
    container.appendChild(table);

    const controls = document.createElement("div");
    controls.className = "calib-controls";
    const calib = document.createElement("button");
    calib.textContent = "Calibrate All";
    calib.addEventListener("click", calibrateAll);
    controls.appendChild(calib);
    container.appendChild(controls);
  }

  function update(rows) {
    const moving = rows.filter((r) => r.isMoving).length;
    summaryEl.textContent = rows.length === 0
      ? "No wands connected."
      : `${rows.length} wand${rows.length === 1 ? "" : "s"} connected, ` +
        `${moving} moving.`;
    tbodyEl.innerHTML = "";
    rows.forEach((row) => {
      const tr = document.createElement("tr");
      tr.style.color = ROW_COLORS[quality(row)];
      cells(row).forEach((val) => {
        const td = document.createElement("td");
        td.textContent = val;
        tr.appendChild(td);
      });
      tbodyEl.appendChild(tr);
    });
  }

  async function poll() {
    try {
      const res = await fetch("/api/maintenance/wands");
      if (!res.ok) { status(`wands: ${res.status}`, true); }
      else update(await res.json());
    } catch (e) {
      status(`wands: ${e}`, true);
    }

    try {
      const res = await fetch("/api/maintenance/wands/serial");
      if (!res.ok) { status(`wands serial: ${res.status}`, true); }
      else {
        const serial = await res.json();
        rebuildReceiver(serial);
        updateReceiverStatus(serial);
      }
    } catch (e) {
      status(`wands serial: ${e}`, true);
    }
  }

  // app.js calls this after its own load(), alongside the calibration init.
  window.spectrumWandsInit = function () {
    mount();
    poll();
    if (timer) clearInterval(timer);
    timer = setInterval(poll, POLL_MS);
  };
})();
