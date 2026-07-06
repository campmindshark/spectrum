// Slimmed-down wand/orientation-device status for the user control surface — a
// compact companion to the maintenance wands.js. Renders one row per connected
// device with just ID / Type / Motion / Quality, plus connection-quality row
// coloring. Read-only: no receiver selector and no Calibrate All button (those
// stay on the maintenance surface).
//
// Like wands.js, this is pull-based on a timer (the SSE change feed carries
// parameters/telemetry, not the wand snapshot). It defines the same
// window.spectrumWandsInit hook app.js calls after load(), so on the user page
// this module fills it and on the maintenance page wands.js does.

(function () {
  const container = document.getElementById("wands");
  if (!container) return;

  // OrientationInput forgets a silent device after ~1s, so a ~1s poll surfaces
  // drops about as fast as the server times them out.
  const POLL_MS = 1000;

  // Quality heuristic — a trimmed mirror of wands.js's (which in turn mirrors
  // the native WandRow). These thresholds and the server-side rate ceiling must
  // stay in sync with wands.js; that file is the canonical copy.
  const STALE_MS = 400, JITTER_FAIR_MS = 8, JITTER_POOR_MS = 20;
  const LOSS_FAIR_FRACTION = 0.01, LOSS_POOR_FRACTION = 0.05;
  const WAND_MAX_RATE_HZ = 200;
  const RATE_FAIR_FRACTION = 0.6, RATE_POOR_FRACTION = 0.3;
  const ROW_COLORS =
    { good: "#ddd", fair: "#ffd24d", poor: "#ff6b6b", wait: "#ddd" };
  const QUALITY_LABEL = { good: "Good", fair: "Fair", poor: "Poor", wait: "…" };

  const COLUMNS = ["ID", "Type", "Motion", "Quality", "Spotlight"];

  let summaryEl = null, tbodyEl = null, timer = null;
  let spotlightAll = null, spotlightIdle = null;

  function status(msg, isError) {
    if (window.spectrumStatus) window.spectrumStatus(msg, isError);
  }

  // Radio group name shared by the "All wands" option and the per-row radios so
  // they act as one mutually-exclusive selection.
  const SPOTLIGHT_GROUP = "wand-spotlight";

  // Make one wand the orientation "spotlight" (only that wand's motion drives
  // the dome), -1 to clear it and render every connected wand, or -2 to force
  // the dome idle (ignore every wand and run the screen-saver). The server also
  // clears it to -1 on its own if the chosen wand disconnects.
  async function setSpotlight(deviceId) {
    try {
      const res = await fetch("/api/wands/spotlight", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ deviceId }),
      });
      status(
        res.ok
          ? (deviceId === -2
              ? "Spotlight: idle (no wands)."
              : deviceId === -1
                ? "Spotlight: all wands."
                : `Spotlight: wand ${deviceId}.`)
          : `spotlight: ${res.status}`,
        !res.ok
      );
    } catch (e) {
      status(`spotlight: ${e}`, true);
    }
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

  function cells(row) {
    return [
      row.deviceId,
      row.typeName,
      // "Still" = transmitting but not physically moving (excluded from the
      // visualization); mirrors the native Motion column.
      row.isMoving ? "Moving" : "Still",
      QUALITY_LABEL[quality(row)],
    ];
  }

  // Build the static structure once so the poll only swaps table rows.
  function mount() {
    container.innerHTML = "";
    const title = document.createElement("h2");
    title.textContent = "Wand status";
    container.appendChild(title);

    summaryEl = document.createElement("div");
    summaryEl.className = "wand-summary";
    summaryEl.textContent = "Listening for devices…";
    container.appendChild(summaryEl);

    // Spotlight selector: an "All wands" option (config -1, the default) and an
    // "Idle" option (config -2 — ignore every wand and run the screen-saver),
    // both sharing a radio group with the per-wand radios rendered in the
    // table's Spotlight column below. Picking a wand makes only that device's
    // orientation drive the dome.
    const spotlightRow = document.createElement("div");
    spotlightRow.className = "wand-summary";

    const allLabel = document.createElement("label");
    allLabel.style.marginRight = "1rem";
    spotlightAll = document.createElement("input");
    spotlightAll.type = "radio";
    spotlightAll.name = SPOTLIGHT_GROUP;
    spotlightAll.checked = true;
    spotlightAll.addEventListener("change", () => setSpotlight(-1));
    allLabel.appendChild(spotlightAll);
    allLabel.appendChild(document.createTextNode(" Spotlight: all wands"));
    spotlightRow.appendChild(allLabel);

    const idleLabel = document.createElement("label");
    spotlightIdle = document.createElement("input");
    spotlightIdle.type = "radio";
    spotlightIdle.name = SPOTLIGHT_GROUP;
    spotlightIdle.addEventListener("change", () => setSpotlight(-2));
    idleLabel.appendChild(spotlightIdle);
    idleLabel.appendChild(document.createTextNode(" Idle (no wands)"));
    spotlightRow.appendChild(idleLabel);

    container.appendChild(spotlightRow);

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
  }

  function update(rows, spotlight) {
    const moving = rows.filter((r) => r.isMoving).length;
    summaryEl.textContent = rows.length === 0
      ? "No wands connected."
      : `${rows.length} wand${rows.length === 1 ? "" : "s"} connected, ` +
        `${moving} moving.`;
    // With no connected wand as the spotlight, the choice is one of the two
    // no-wand options: "Idle" for -2, otherwise "All wands" (-1, or a stale
    // device id the server is about to reset to -1). When a wand is spotlit its
    // own per-row radio below is checked instead, so neither of these is.
    const spotWand = rows.some((r) => r.isSpotlight);
    spotlightIdle.checked = !spotWand && spotlight === -2;
    spotlightAll.checked = !spotWand && spotlight !== -2;
    tbodyEl.innerHTML = "";
    rows.forEach((row) => {
      const tr = document.createElement("tr");
      tr.style.color = ROW_COLORS[quality(row)];
      cells(row).forEach((val) => {
        const td = document.createElement("td");
        td.textContent = val;
        tr.appendChild(td);
      });
      // Spotlight column: a radio that makes this wand the sole dome driver.
      const spotTd = document.createElement("td");
      const radio = document.createElement("input");
      radio.type = "radio";
      radio.name = SPOTLIGHT_GROUP;
      radio.checked = row.isSpotlight;
      radio.addEventListener("change", () => setSpotlight(row.deviceId));
      spotTd.appendChild(radio);
      tr.appendChild(spotTd);
      tbodyEl.appendChild(tr);
    });
  }

  async function poll() {
    try {
      const res = await fetch("/api/wands");
      if (!res.ok) { status(`wands: ${res.status}`, true); }
      else {
        const data = await res.json();
        update(data.rows, data.spotlight);
      }
    } catch (e) {
      status(`wands: ${e}`, true);
    }
  }

  // app.js calls this after its own load().
  window.spectrumWandsInit = function () {
    mount();
    poll();
    if (timer) clearInterval(timer);
    timer = setInterval(poll, POLL_MS);
  };
})();
