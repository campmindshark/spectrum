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

  // Quality heuristic, mirroring WandRow: staleness, high jitter, or high
  // packet loss → Poor.
  const STALE_MS = 400, JITTER_FAIR_MS = 8, JITTER_POOR_MS = 20;
  const LOSS_FAIR_FRACTION = 0.01, LOSS_POOR_FRACTION = 0.05;
  const ROW_COLORS =
    { good: "#ddd", fair: "#ffd24d", poor: "#ff6b6b", wait: "#ddd" };
  const QUALITY_LABEL = { good: "Good", fair: "Fair", poor: "Poor", wait: "…" };

  const COLUMNS = [
    "ID", "Type", "Button", "Quality", "Rate (Hz)", "Jitter (ms)",
    "Loss (%)", "Data (kB/s)", "Packets", "Last (ms)",
    "Orientation (W X Y Z)", "Speed",
  ];

  let summaryEl = null, tbodyEl = null, timer = null;

  function status(msg, isError) {
    if (window.spectrumStatus) window.spectrumStatus(msg, isError);
  }

  function quality(row) {
    if (row.packetCount < 2) return "wait";
    if (row.millisSinceLastPacket > STALE_MS || row.jitterMs > JITTER_POOR_MS ||
        row.packetLossFraction > LOSS_POOR_FRACTION) {
      return "poor";
    }
    if (row.jitterMs > JITTER_FAIR_MS ||
        row.packetLossFraction > LOSS_FAIR_FRACTION) {
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

  // Build the static structure once so the poll only swaps table rows — the
  // Calibrate All button and header keep their identity across refreshes.
  function mount() {
    container.innerHTML = "";
    const title = document.createElement("h2");
    title.textContent = "Wand status";
    container.appendChild(title);

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
    summaryEl.textContent = rows.length === 0
      ? "No wands connected."
      : `${rows.length} wand${rows.length === 1 ? "" : "s"} connected.`;
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
      if (!res.ok) { status(`wands: ${res.status}`, true); return; }
      update(await res.json());
    } catch (e) {
      status(`wands: ${e}`, true);
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
