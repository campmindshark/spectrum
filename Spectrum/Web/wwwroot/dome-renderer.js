// Reusable real-dome canvas renderer. Both the standalone simulator and dome
// calibration use the same geometry endpoint, projection math, binary frame
// stream, and pixel rasterization.
(() => {
  "use strict";

  function create(options) {
    const canvas = options.canvas;
    const stateElement = options.stateElement || null;
    const ctx = canvas.getContext("2d", { alpha: false });
    const image = ctx.createImageData(canvas.width, canvas.height);
    const maxFps = 60;
    let geometry = null;
    let stripPositions = null;
    let topDownPositions = null;
    let positions = null;
    let latestFrame = null;
    let topDown = !!options.defaultTopDown;
    let socket = null;
    let running = false;
    let reconnectTimer = null;

    function setState(text) {
      if (stateElement) stateElement.textContent = text;
      if (options.onState) options.onState(text);
    }

    function positionsFor(points) {
      const result = new Int32Array(geometry.pixelCount * 2);
      points.forEach((point, i) => {
        result[i * 2] = Math.round(point[0] * (canvas.width - 5)) + 2;
        result[i * 2 + 1] =
          Math.round(point[1] * (canvas.height - 5)) + 2;
      });
      return result;
    }

    async function ensureGeometry() {
      if (geometry) return true;
      setState("loading layout…");
      const response = await fetch("/api/dome-simulator/geometry");
      if (!response.ok) {
        setState(response.status === 404
          ? "preview disabled on server"
          : `preview unavailable (${response.status})`);
        return false;
      }
      geometry = await response.json();
      stripPositions = positionsFor(geometry.points);
      topDownPositions = positionsFor(geometry.topDownPoints);
      positions = topDown ? topDownPositions : stripPositions;
      geometry.points = null;
      geometry.topDownPoints = null;
      return true;
    }

    function drawFrame(bytes) {
      if (!geometry || bytes.length !== geometry.pixelCount * 3) return;
      latestFrame = bytes;
      image.data.fill(0);
      for (let i = 0, p = 0; i < geometry.pixelCount; i++, p += 3) {
        const x = positions[i * 2];
        const y = positions[i * 2 + 1];
        const out = (y * canvas.width + x) * 4;
        image.data[out] = bytes[p];
        image.data[out + 1] = bytes[p + 1];
        image.data[out + 2] = bytes[p + 2];
        image.data[out + 3] = 255;
      }
      ctx.putImageData(image, 0, 0);
    }

    function projectionLabel() {
      return topDown ? "View: Real top-down" : "View: Strip extents";
    }

    function setProjection(useTopDown) {
      topDown = !!useTopDown;
      if (geometry) {
        positions = topDown ? topDownPositions : stripPositions;
        if (latestFrame) drawFrame(latestFrame);
      }
      if (options.onProjection) options.onProjection(topDown, projectionLabel());
      return projectionLabel();
    }

    function connect() {
      if (!running || socket) return;
      const protocol = location.protocol === "https:" ? "wss:" : "ws:";
      socket = new WebSocket(
        `${protocol}//${location.host}/api/dome-simulator/frames`);
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

    async function start() {
      if (running) return true;
      if (!await ensureGeometry()) return false;
      running = true;
      canvas.hidden = false;
      setProjection(topDown);
      connect();
      return true;
    }

    function stop() {
      running = false;
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
      if (socket) socket.close();
      socket = null;
      if (options.hideWhenStopped !== false) canvas.hidden = true;
      setState("off");
    }

    setProjection(topDown);
    return {
      start,
      stop,
      setProjection,
      toggleProjection: () => setProjection(!topDown),
      projectionLabel,
      isRunning: () => running,
    };
  }

  window.SpectrumDomeRenderer = { create };
})();
