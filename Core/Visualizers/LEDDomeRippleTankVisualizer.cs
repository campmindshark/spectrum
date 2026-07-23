using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;

namespace Spectrum.Visualizers {

  // A damped wave-equation grid over the dome's plan-view square. Each moving
  // orientation sensor acts as a surface object: its projected path presses a
  // continuous wake into the water, and the wake amplitude is derived directly
  // from the sensor's smoothed angular velocity. There are no beat, audio, or
  // trigger inputs; without live orientation motion, the shared idle
  // orientation supplies a gentle screen-saver wake.
  //
  // The surface is shaded with the same thin-lens focus form as Caustics' Lens
  // method. When the layer uses Refract, central-difference gradients are also
  // published through hue and alpha for the displacement blend.
  class LEDDomeRippleTankVisualizer : DomeLayerVisualizer {

    private readonly DomeLayerEnvironment environment;
    private readonly LayerRendererRuntime runtime;
    private readonly OrientationInput orientationInput;
    private readonly OrientationCenter center;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private readonly System.Diagnostics.Stopwatch frameTimer =
      new System.Diagnostics.Stopwatch();

    private const int TankSize = 120;
    // Courant number squared (c·dt/dx)². 0.36 (C = 0.6) is comfortably inside
    // the 2D stability bound C ≤ 1/√2.
    private const double TankC2 = 0.36;
    private const double TankStepsPerSecond = 60;
    private const int TankMaxStepsPerFrame = 8;
    private const double TankBright = 0.2;
    private const double TankFocus = 12;
    private const double TankEps = 0.04;
    private const double TankGradGain = 2.5;
    private const double TankWakeMinTravel = 0.001;
    private const double TankWakeMaxTravel = 0.25;
    private const double TankWakeAmp = 0.12;
    // Fixed at the former Object Size slider's effective minimum. A small
    // contact patch produces fine, closely defined wave fronts.
    private const double TankWakeRadiusCells = 3;
    // Any real surface travel produces a visible wake. This matches the former
    // default wake-strength setting; angular speed then raises the wave height
    // toward the grid's stable maximum instead of deciding whether a wake is
    // drawn at all.
    private const double TankMinWakeStrength = 0.35;
    // 5 rad/s (~286°/s) reaches maximum wake strength, keeping ordinary
    // movement gentler while preserving a visible wake at every speed.
    // The cap prevents a discontinuity or corrupt packet from destabilizing
    // the wave grid.
    private const double TankFullWakeSpeedRadPerSecond = 5;

    private double[] tankCur = Array.Empty<double>();
    private double[] tankPrev = Array.Empty<double>();
    private double[] tankLap = Array.Empty<double>();
    private double[] tankGradX = Array.Empty<double>();
    private double[] tankGradY = Array.Empty<double>();
    private bool tankLapDirty = true, tankGradDirty = true;
    private double tankStepAccumulator;
    private int[] tankCell = Array.Empty<int>();
    private double[] tankWeightX = Array.Empty<double>();
    private double[] tankWeightY = Array.Empty<double>();
    private int lastClearCounter = -1;

    private struct TankObjectState {
      public double X;
      public double Y;
      public long SeenFrame;
    }

    private readonly Dictionary<int, TankObjectState> tankObjects =
      new Dictionary<int, TankObjectState>();
    private readonly List<int> staleTankObjects = new List<int>();
    private long tankObjectFrame;
    // Device ids are non-negative wire values, so this cannot collide with a
    // physical orientation sensor.
    private const int TankIdleObjectId = -1;

    public LEDDomeRippleTankVisualizer(
      DomeLayerEnvironment environment,
      LayerRendererRuntime runtime,
      OrientationInput orientationInput,
      OrientationCenter center,
      DomeRenderContext dome
    ) {
      this.environment = environment;
      this.runtime = runtime;
      this.orientationInput = orientationInput;
      this.center = center;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
      this.TankEnsureAllocated();
    }

    public int Priority => 2;
    public string LayerKey => "ripple-tank";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[]? inputs;
    public Input[] GetInputs() {
      return this.inputs
        ?? (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      RippleTankLayerOptions options =
        this.runtime.GetOptions<RippleTankLayerOptions>();
      double elapsed = 0;
      if (!this.frameTimer.IsRunning) {
        this.frameTimer.Restart();
      } else {
        elapsed = this.frameTimer.Elapsed.TotalSeconds;
        this.frameTimer.Restart();
      }

      bool refracting =
        this.runtime.Snapshot.OperationId == DomeBlend.Refract.Id;
      // Keep the shared idle orientation advancing even when Ripple Tank is
      // the only orientation-driven layer. Zero retains its quiet baseline
      // drift without adding an audio dependency to this layer.
      this.center.Update(0);
      this.TankAdvance(
        options.Speed, options.Damping, elapsed, refracting);

      int tint = options.Color;
      double tr = (tint >> 16) & 0xFF;
      double tg = (tint >> 8) & 0xFF;
      double tb = tint & 0xFF;

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        ref var pixel = ref this.buffer.pixels[i];
        double lap = this.TankSampleAt(this.tankLap, i);
        double v = TankBright / (Math.Abs(1 - TankFocus * lap) + TankEps);
        if (v > 1) {
          v = 1;
        }
        double lum = Math.Pow(v, options.Sharpness) * options.Brightness;
        lum = Math.Clamp(lum, 0, 1);
        int r = (int)(tr * lum);
        int g = (int)(tg * lum);
        int b = (int)(tb * lum);
        pixel.color = (r << 16) | (g << 8) | b;

        if (refracting) {
          double gx = this.TankSampleAt(this.tankGradX, i);
          double gy = this.TankSampleAt(this.tankGradY, i);
          double angle = Math.Atan2(gy, gx);
          double huePart = angle / (2 * Math.PI);
          pixel.hue = huePart - Math.Floor(huePart);
          double mag = Math.Sqrt(gx * gx + gy * gy) * TankGradGain;
          pixel.SetAlpha(mag > 1 ? 1 : mag);
        }
      }
    }

    private void TankAdvance(
      double speed, double damping, double elapsed, bool refracting
    ) {
      if (this.TankClearRequested()) {
        Array.Clear(this.tankCur, 0, this.tankCur.Length);
        Array.Clear(this.tankPrev, 0, this.tankPrev.Length);
        this.tankObjects.Clear();
        this.tankLapDirty = true;
        this.tankGradDirty = true;
      }

      this.TankMoveObjects(elapsed);

      this.tankStepAccumulator += elapsed * TankStepsPerSecond * speed;
      int steps = (int)this.tankStepAccumulator;
      if (steps > TankMaxStepsPerFrame) {
        steps = TankMaxStepsPerFrame;
        this.tankStepAccumulator = 0;
      } else {
        this.tankStepAccumulator -= steps;
      }
      for (int s = 0; s < steps; s++) {
        this.TankStep(damping);
      }

      this.TankRefreshDerived(refracting);
    }

    private void TankMoveObjects(double elapsed) {
      this.tankObjectFrame++;
      IReadOnlyDictionary<int, OrientationDevice> devices =
        this.orientationInput.OperatorFrameDevices;
      int spotlight = this.environment.SpotlightDeviceId;
      bool spotlightConnected = spotlight >= 0
        && devices.ContainsKey(spotlight);

      if (spotlight != -2) {
        foreach (var kvp in devices) {
          OrientationDevice device = kvp.Value;
          if (spotlightConnected && kvp.Key != spotlight) {
            continue;
          }

          Vector3 aim = Vector3.Transform(
            OrientationCenter.Spot,
            Quaternion.Conjugate(device.currentRotation())
          );
          Vector2 stripAim = StrutLayoutFactory.ProjectSphereToStrip(
            aim, foldAxisToUpperHemisphere: true);
          this.TankMoveObject(
            kvp.Key,
            Math.Clamp((stripAim.X + 1) / 2, 0, 1),
            Math.Clamp((stripAim.Y + 1) / 2, 0, 1),
            device.MotionSpeedRadPerSecond,
            elapsed);
        }
      }

      // When no physical wand is moving (or the spotlight explicitly forces
      // idle), sweep the shared screen-saver orientation through the tank as a
      // gentle virtual object. Zero angular speed selects the visible minimum
      // wake strength; the idle path itself determines when waves are emitted.
      if (this.center.Idle) {
        Vector3 idleAim = Vector3.Transform(
          OrientationCenter.Spot,
          Quaternion.Conjugate(this.center.CurrentCenter)
        );
        Vector2 idleStripAim = StrutLayoutFactory.ProjectSphereToStrip(
          idleAim, foldAxisToUpperHemisphere: true);
        this.TankMoveObject(
          TankIdleObjectId,
          Math.Clamp((idleStripAim.X + 1) / 2, 0, 1),
          Math.Clamp((idleStripAim.Y + 1) / 2, 0, 1),
          0,
          elapsed);
      }

      // Keep stationary sensors at their last projected position so even
      // motion below the shared isMoving threshold can accumulate into a wake.
      // Forget only disconnected and spotlight-filtered sensors; one that
      // becomes eligible again starts from a clean baseline rather than
      // drawing a stale cross-tank wake.
      this.staleTankObjects.Clear();
      foreach (var kvp in this.tankObjects) {
        if (kvp.Value.SeenFrame != this.tankObjectFrame) {
          this.staleTankObjects.Add(kvp.Key);
        }
      }
      for (int i = 0; i < this.staleTankObjects.Count; i++) {
        this.tankObjects.Remove(this.staleTankObjects[i]);
      }
    }

    private void TankMoveObject(
      int id, double x, double y, double angularSpeedRadPerSecond,
      double elapsed
    ) {
      if (!this.tankObjects.TryGetValue(id, out TankObjectState state)) {
        this.tankObjects[id] = new TankObjectState {
          X = x, Y = y, SeenFrame = this.tankObjectFrame,
        };
        return;
      }

      double dx = x - state.X, dy = y - state.Y;
      double travel = Math.Sqrt(dx * dx + dy * dy);
      if (elapsed > 0.25 || travel > TankWakeMaxTravel) {
        state.X = x;
        state.Y = y;
      } else if (travel >= TankWakeMinTravel) {
        this.TankSweepWake(
          state.X, state.Y, x, y, travel, angularSpeedRadPerSecond);
        state.X = x;
        state.Y = y;
      }
      // Retain the old position for sub-threshold travel so slow motion
      // accumulates until it is distinguishable from sensor/frame jitter.
      state.SeenFrame = this.tankObjectFrame;
      this.tankObjects[id] = state;
    }

    private void TankSweepWake(
      double x0, double y0, double x1, double y1, double travel,
      double angularSpeedRadPerSecond
    ) {
      double wakeStrength =
        WakeStrengthForAngularSpeed(angularSpeedRadPerSecond);
      if (wakeStrength <= 0) {
        return;
      }
      double spacing = Math.Max(
        0.05 * TankWakeRadiusCells / (TankSize - 1), 0.001);
      int samples = Math.Max(1, (int)Math.Ceiling(travel / spacing));
      double amp = TankWakeAmp * wakeStrength
        * Math.Min(1, travel / spacing);
      for (int i = 1; i <= samples; i++) {
        double p = (double)i / samples;
        this.TankDrop(
          x0 + (x1 - x0) * p,
          y0 + (y1 - y0) * p,
          TankWakeRadiusCells,
          amp);
      }
    }

    internal static double WakeStrengthForAngularSpeed(
      double angularSpeedRadPerSecond
    ) {
      if (double.IsNaN(angularSpeedRadPerSecond)) {
        angularSpeedRadPerSecond = 0;
      }
      double speedAmount = Math.Clamp(
        angularSpeedRadPerSecond / TankFullWakeSpeedRadPerSecond, 0, 1);
      return TankMinWakeStrength
        + (1 - TankMinWakeStrength) * speedAmount;
    }

    private void TankEnsureAllocated() {
      int cells = TankSize * TankSize;
      this.tankCur = new double[cells];
      this.tankPrev = new double[cells];
      this.tankLap = new double[cells];
      this.tankGradX = new double[cells];
      this.tankGradY = new double[cells];

      int n = this.buffer.pixels.Length;
      this.tankCell = new int[n];
      this.tankWeightX = new double[n];
      this.tankWeightY = new double[n];
      ImmutableArray<Vector2> projectedPixels =
        DomeSurfaceGeometry.ProjectNormalsToStrip(this.buffer.Normals);
      for (int i = 0; i < n; i++) {
        Vector2 point = projectedPixels[i];
        double gx = Math.Clamp((point.X + 1) / 2, 0, 1) * (TankSize - 1);
        double gy = Math.Clamp((point.Y + 1) / 2, 0, 1) * (TankSize - 1);
        int ix = Math.Min((int)gx, TankSize - 2);
        int iy = Math.Min((int)gy, TankSize - 2);
        this.tankCell[i] = iy * TankSize + ix;
        this.tankWeightX[i] = gx - ix;
        this.tankWeightY[i] = gy - iy;
      }
    }

    private bool TankClearRequested() {
      int counter = this.environment.ClearGeneration(this.runtime.InstanceId);
      if (this.lastClearCounter == -1) {
        this.lastClearCounter = counter;
        return false;
      }
      bool cleared = counter != this.lastClearCounter;
      this.lastClearCounter = counter;
      return cleared;
    }

    private void TankStep(double damping) {
      double[] prev = this.tankPrev;
      double[] cur = this.tankCur;
      // Damping is expressed as energy removed per simulation step so moving
      // the slider upward makes waves dissipate faster.
      double retention = 1 - Math.Clamp(damping, 0, 1);
      for (int y = 1; y < TankSize - 1; y++) {
        int row = y * TankSize;
        for (int x = 1; x < TankSize - 1; x++) {
          int i = row + x;
          double lap = cur[i - 1] + cur[i + 1]
            + cur[i - TankSize] + cur[i + TankSize] - 4 * cur[i];
          prev[i] = retention * (2 * cur[i] - prev[i] + TankC2 * lap);
        }
      }
      for (int x = 0; x < TankSize; x++) {
        prev[x] = prev[x + TankSize];
        prev[(TankSize - 1) * TankSize + x] = prev[(TankSize - 2) * TankSize + x];
      }
      for (int y = 0; y < TankSize; y++) {
        prev[y * TankSize] = prev[y * TankSize + 1];
        prev[y * TankSize + TankSize - 1] = prev[y * TankSize + TankSize - 2];
      }
      this.tankPrev = cur;
      this.tankCur = prev;
      this.tankLapDirty = true;
      this.tankGradDirty = true;
    }

    private void TankDrop(double cx, double cy, double radiusCells, double amp) {
      double gx = cx * (TankSize - 1), gy = cy * (TankSize - 1);
      int reach = (int)(3 * radiusCells);
      double s2 = 2 * radiusCells * radiusCells;
      int x0 = Math.Max(1, (int)gx - reach);
      int x1 = Math.Min(TankSize - 2, (int)gx + reach);
      int y0 = Math.Max(1, (int)gy - reach);
      int y1 = Math.Min(TankSize - 2, (int)gy + reach);
      for (int y = y0; y <= y1; y++) {
        for (int x = x0; x <= x1; x++) {
          double dx = x - gx, dy = y - gy;
          this.tankCur[y * TankSize + x] -=
            amp * Math.Exp(-(dx * dx + dy * dy) / s2);
        }
      }
      this.tankLapDirty = true;
      this.tankGradDirty = true;
    }

    private void TankRefreshDerived(bool wantGrad) {
      bool needLap = this.tankLapDirty;
      bool needGrad = wantGrad && this.tankGradDirty;
      if (!needLap && !needGrad) {
        return;
      }
      double[] u = this.tankCur;
      for (int y = 1; y < TankSize - 1; y++) {
        int row = y * TankSize;
        for (int x = 1; x < TankSize - 1; x++) {
          int i = row + x;
          this.tankLap[i] = u[i - 1] + u[i + 1]
            + u[i - TankSize] + u[i + TankSize] - 4 * u[i];
          if (needGrad) {
            this.tankGradX[i] = (u[i + 1] - u[i - 1]) * 0.5;
            this.tankGradY[i] = (u[i + TankSize] - u[i - TankSize]) * 0.5;
          }
        }
      }
      CopyEdgesFromInterior(this.tankLap);
      if (needGrad) {
        CopyEdgesFromInterior(this.tankGradX);
        CopyEdgesFromInterior(this.tankGradY);
        this.tankGradDirty = false;
      }
      this.tankLapDirty = false;
    }

    private static void CopyEdgesFromInterior(double[] f) {
      for (int x = 0; x < TankSize; x++) {
        f[x] = f[x + TankSize];
        f[(TankSize - 1) * TankSize + x] = f[(TankSize - 2) * TankSize + x];
      }
      for (int y = 0; y < TankSize; y++) {
        f[y * TankSize] = f[y * TankSize + 1];
        f[y * TankSize + TankSize - 1] = f[y * TankSize + TankSize - 2];
      }
    }

    private double TankSampleAt(double[] field, int i) {
      int c = this.tankCell[i];
      double wx = this.tankWeightX[i], wy = this.tankWeightY[i];
      double top = field[c] + (field[c + 1] - field[c]) * wx;
      double bottom = field[c + TankSize]
        + (field[c + TankSize + 1] - field[c + TankSize]) * wx;
      return top + (bottom - top) * wy;
    }
  }
}
