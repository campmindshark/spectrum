// Dedicated dome preview. Geometry is fetched once on this page; live frames
// are fixed-size binary RGB (3 bytes/LED), capped server-side at 60 FPS. The
// main controller never loads this client, so ordinary clients cost nothing.
(() => {
  "use strict";

  const root = document.getElementById("dome-simulator");
  const toggle = document.getElementById("dome-simulator-toggle");
  const projectionToggle = document.getElementById("dome-simulator-projection");
  const state = document.getElementById("dome-simulator-state");
  const canvas = document.getElementById("dome-simulator-canvas");
  if (!root || !toggle || !projectionToggle || !state || !canvas) return;

  const ctx = canvas.getContext("2d", { alpha: false });
  let geometry = null;
  let pixelPositions = null;
  let stripExtentsPositions = null;
  let topDownPositions = null;
  let latestFrame = null;
  let topDown = false;
  let socket = null;
  let running = false;
  let reconnectTimer = null;
  const maxFps = 60;
  const image = ctx.createImageData(canvas.width, canvas.height);

  function setState(text) { state.textContent = text; }

  function positionsFor(points) {
    const positions = new Int32Array(geometry.pixelCount * 2);
    points.forEach((point, i) => {
      positions[i * 2] = Math.round(point[0] * (canvas.width - 5)) + 2;
      positions[i * 2 + 1] = Math.round(point[1] * (canvas.height - 5)) + 2;
    });
    return positions;
  }

  async function ensureGeometry() {
    if (geometry) return true;
    setState("loading layout…");
    const response = await fetch("/api/dome-simulator/geometry");
    if (!response.ok) {
      setState(response.status === 404 ? "disabled on server" : `error ${response.status}`);
      toggle.textContent = response.status === 404 ? "Preview disabled" : "Preview unavailable";
      toggle.disabled = true;
      return false;
    }
    geometry = await response.json();
    stripExtentsPositions = positionsFor(geometry.points);
    topDownPositions = positionsFor(geometry.topDownPoints);
    pixelPositions = stripExtentsPositions;
    geometry.points = null;
    geometry.topDownPoints = null;
    projectionToggle.disabled = false;
    return true;
  }

  function drawFrame(bytes) {
    if (!geometry || bytes.length !== geometry.pixelCount * 3) return;
    latestFrame = bytes;
    image.data.fill(0);
    for (let i = 0, p = 0; i < geometry.pixelCount; i++, p += 3) {
      const x = pixelPositions[i * 2];
      const y = pixelPositions[i * 2 + 1];
      const out = (y * canvas.width + x) * 4;
      image.data[out] = bytes[p];
      image.data[out + 1] = bytes[p + 1];
      image.data[out + 2] = bytes[p + 2];
      image.data[out + 3] = 255;
    }
    ctx.putImageData(image, 0, 0);
  }

  function setProjection(useTopDown) {
    topDown = useTopDown;
    pixelPositions = topDown ? topDownPositions : stripExtentsPositions;
    projectionToggle.textContent = topDown
      ? "View: Real top-down"
      : "View: Strip extents";
    projectionToggle.setAttribute("aria-pressed", String(topDown));
    if (latestFrame) drawFrame(latestFrame);
  }

  function connect() {
    if (!running) return;
    const protocol = location.protocol === "https:" ? "wss:" : "ws:";
    socket = new WebSocket(`${protocol}//${location.host}/api/dome-simulator/frames`);
    socket.binaryType = "arraybuffer";
    socket.onopen = () => setState(`live · ${maxFps} FPS max`);
    socket.onmessage = (event) => drawFrame(new Uint8Array(event.data));
    socket.onerror = () => setState("connection error");
    socket.onclose = () => {
      socket = null;
      if (running) {
        setState("reconnecting…");
        reconnectTimer = setTimeout(connect, 1500);
      }
    };
  }

  function stop() {
    running = false;
    clearTimeout(reconnectTimer);
    reconnectTimer = null;
    if (socket) socket.close();
    socket = null;
    toggle.textContent = "Start preview";
    toggle.classList.remove("running");
    canvas.hidden = true;
    setState("off");
  }

  async function start() {
    if (running) return;
    toggle.disabled = true;
    try {
      if (!await ensureGeometry()) return;
      running = true;
      canvas.hidden = false;
      toggle.textContent = "Stop preview";
      toggle.classList.add("running");
      connect();
    } catch (_) {
      setState("unavailable");
      toggle.textContent = "Retry preview";
      toggle.disabled = false;
    } finally {
      if (geometry) toggle.disabled = false;
    }
  }

  toggle.addEventListener("click", () => {
    if (running) { stop(); return; }
    start();
  });
  projectionToggle.addEventListener("click", () => setProjection(!topDown));

  window.addEventListener("pagehide", stop);
  start();
})();
