// Low-cost dome preview. Geometry is fetched once on demand; live frames are
// fixed-size binary RGB (3 bytes/LED), capped server-side at 10 FPS. Keeping the
// preview opt-in means ordinary control clients impose no simulation cost.
(() => {
  "use strict";

  const root = document.getElementById("dome-simulator");
  const toggle = document.getElementById("dome-simulator-toggle");
  const state = document.getElementById("dome-simulator-state");
  const canvas = document.getElementById("dome-simulator-canvas");
  if (!root || !toggle || !state || !canvas) return;

  const ctx = canvas.getContext("2d", { alpha: false });
  let geometry = null;
  let pixelPositions = null;
  let socket = null;
  let running = false;
  let reconnectTimer = null;
  const image = ctx.createImageData(canvas.width, canvas.height);

  function setState(text) { state.textContent = text; }

  async function ensureGeometry() {
    if (geometry) return true;
    setState("loading layout…");
    const response = await fetch("/api/dome-simulator/geometry");
    if (!response.ok) {
      setState(response.status === 404 ? "disabled on server" : `error ${response.status}`);
      toggle.disabled = true;
      return false;
    }
    geometry = await response.json();
    pixelPositions = new Int32Array(geometry.pixelCount * 2);
    geometry.points.forEach((point, i) => {
      pixelPositions[i * 2] = Math.round(point[0] * (canvas.width - 5)) + 2;
      pixelPositions[i * 2 + 1] = Math.round(point[1] * (canvas.height - 5)) + 2;
    });
    geometry.points = null;
    return true;
  }

  function drawFrame(bytes) {
    if (!geometry || bytes.length !== geometry.pixelCount * 3) return;
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

  function connect() {
    if (!running) return;
    const protocol = location.protocol === "https:" ? "wss:" : "ws:";
    socket = new WebSocket(`${protocol}//${location.host}/api/dome-simulator/frames`);
    socket.binaryType = "arraybuffer";
    socket.onopen = () => setState("live · 10 FPS max");
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

  toggle.addEventListener("click", async () => {
    if (running) { stop(); return; }
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
    } finally {
      if (!toggle.disabled || geometry) toggle.disabled = false;
    }
  });

  window.addEventListener("pagehide", stop);
})();
