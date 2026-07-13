using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;

namespace Spectrum.Visualizers {

  // A physically-exact 3-gimbal gyroscope rendered as great-circle rings on the
  // hemisphere. The gimbal angles of a nested 3-ring gimbal set are exactly the
  // Euler angles (Z-Y-X: yaw about dome-up, then pitch, then roll) of the body
  // it carries, so we extract yaw/pitch/roll from a driving orientation and
  // rebuild the nested gimbal frames F0 (outer) < F1 (middle) < F2 (inner):
  //
  //   F0 = Rz(yaw)              outer gimbal, pivots about dome-up (+Z)
  //   F1 = F0 * Ry(pitch)       middle gimbal, carried by the outer
  //   F2 = F1 * Rx(roll) == Q   inner gimbal (the rotor mount), carried by both
  //
  // Each ring is a great circle fixed in its own gimbal's frame, so the inner
  // rings ride the outer ones exactly as physical gimbals do (gimbal lock and
  // all). Its normal is chosen perpendicular to that gimbal's pivot axis so the
  // rotation is actually visible: outer n0 = F0*X (perp the +Z pivot), middle
  // n1 = F1*Z (perp F0's Y pivot), inner n2 = F2*Y (perp F1's X pivot).
  //
  // The driving orientation is the shared OrientationCenter.CurrentCenter: the
  // spotlighted wand's rotation when one is moving, otherwise the idle-drift
  // orientation. So moving a wand tilts the gyroscope; with nothing moving it
  // wanders on its own. The flywheel's own fast spin is the one DOF an
  // orientation can't encode, so the rotor rim's chasing highlight is
  // clock-driven. Tuning comes from the gyroscope definition in LayerCatalog;
  // the three rings (outer/middle/inner)
  // take their colors from the live palette bank's first three slots, each
  // scaled by the ring's cross-section falloff so it still fades to black at the
  // band edges.
  class LEDDomeGyroscopeVisualizer : DomeLayerVisualizer {

    private readonly DomeLayerEnvironment environment;
    private readonly LayerRendererRuntime runtime;
    private readonly OrientationInput orientation;
    private readonly OrientationCenter orientationCenter;
    private readonly LEDDomeOutput dome;
    private readonly DomeFrame buffer;

    // Unit-sphere position of every LED, baked once (guarded z on the rim).
    private readonly ImmutableArray<Vector3> pixelPositions;

    // Wall-clock phase for the flywheel spin (the rotor's own DOF), kept
    // frame-rate independent.
    private readonly System.Diagnostics.Stopwatch clock =
      System.Diagnostics.Stopwatch.StartNew();

    // Fixed idle-drift level fed to the shared OrientationCenter when no wand
    // is moving (the gyroscope has no audio input to modulate it, and it isn't
    // exposed as a per-layer knob). Sets a moderate, constant wander speed.
    private const double IDLE_LEVEL = 0.4;

    public LEDDomeGyroscopeVisualizer(
      DomeLayerEnvironment environment,
      LayerRendererRuntime runtime,
      OrientationInput orientation,
      OrientationCenter orientationCenter,
      LEDDomeOutput dome
    ) {
      this.environment = environment;
      this.runtime = runtime;
      this.orientation = orientation;
      this.orientationCenter = orientationCenter;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeFrame();
      this.pixelPositions = this.buffer.BakePixelPositions();
    }

    public int Priority => 2;

    public string LayerKey => "gyroscope";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientation });
    }

    public void Visualize() {
      GyroscopeLayerOptions options =
        this.runtime.GetOptions<GyroscopeLayerOptions>();
      double ringWidth = options.RingWidth; // band thickness (dot units)
      double rotorRate = options.RotorRate; // highlight orbit, rev/s
      int paletteBank = options.Palette;

      // Per-ring colors (outer/middle/inner), pulled from the live palette bank's
      // first three relative slots and decoded once per frame. Kept as Color so
      // the render loop reads their H/S and scales V by the ring's cross-section
      // falloff — indexed to match n[0]=outer, n[1]=middle, n[2]=inner.
      Color[] ringColor = {
        new Color(this.dome.GetSingleColor(0, paletteBank)),
        new Color(this.dome.GetSingleColor(1, paletteBank)),
        new Color(this.dome.GetSingleColor(2, paletteBank)),
      };

      // Advance the shared idle-drift/spotlight resolver and take this frame's
      // driving orientation: a moving wand's rotation, else the idle pointer.
      // Update() is idempotent within an engine tick (its own FrameClock), so
      // sharing it with other orientation layers is safe; the first caller's
      // level wins for the tick.
      this.orientationCenter.Update(IDLE_LEVEL);
      Quaternion q = this.orientationCenter.CurrentCenter;

      // Euler Z-Y-X extraction (yaw about +Z = dome-up, pitch about Y, roll
      // about X): the three gimbal angles. Only yaw and pitch are needed to
      // build F0/F1; the inner ring and rotor read the full q directly.
      float x = q.X, y = q.Y, z = q.Z, w = q.W;
      double yaw = Math.Atan2(2 * (w * z + x * y), 1 - 2 * (y * y + z * z));
      double pitch = Math.Asin(Math.Clamp(2 * (w * y - z * x), -1.0, 1.0));

      Quaternion f0 = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)yaw);
      Quaternion f1 = f0 *
        Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)pitch);

      Vector3 n0 = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, f0));
      Vector3 n1 = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, f1));
      Vector3 n2 = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, q));
      Vector3[] n = { n0, n1, n2 };

      // Rotor: a disc whose axle is the inner gimbal's +Z; its rim is the great
      // circle perpendicular to that axle, with a highlight spinning fast. ru/rv
      // span the rim plane so we can measure a pixel's angle around it.
      Vector3 rotorAxis = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, q));
      Vector3 seed = Math.Abs(rotorAxis.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
      Vector3 ru = Vector3.Normalize(Vector3.Cross(rotorAxis, seed));
      Vector3 rv = Vector3.Cross(rotorAxis, ru);
      double rotorPhase =
        2 * Math.PI * rotorRate * this.clock.Elapsed.TotalSeconds;
      double rimHalf = ringWidth * 0.6;

      // Gentle fade so the tumbling arcs leave a faint wake.
      this.buffer.Fade(
        1 - Math.Pow(6, -this.environment.GlobalFadeSpeed), 0);

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        Vector3 p = this.pixelPositions[i];

        // Three gimbal rings.
        for (int r = 0; r < 3; r++) {
          double d = Math.Abs(Vector3.Dot(p, n[r]));
          if (d >= ringWidth) {
            continue;
          }
          double bright = 1.0 - d / ringWidth; // ring cross-section falloff
          Color rc = ringColor[r];
          Color c = new Color(rc.H, rc.S, rc.V * bright);
          this.buffer.pixels[i].color = Color.BlendLightPaint(
            new Color(this.buffer.pixels[i].color), c).ToInt();
        }

        // Rotor rim + chasing highlight (the flywheel).
        double rd = Math.Abs(Vector3.Dot(p, rotorAxis));
        if (rd < rimHalf) {
          double rim = 1.0 - rd / rimHalf;
          double ang = Math.Atan2(Vector3.Dot(p, rv), Vector3.Dot(p, ru));
          double da = Math.Abs(Wrap(ang - rotorPhase, -Math.PI, Math.PI));
          double bright = 0.25 * rim; // dim rotor rim...
          if (da < 0.5) {
            bright = Math.Min(1.0, bright + (1.0 - da / 0.5)); // ...bright chase
          }
          Color c = new Color(0.13, 0.15, bright); // near-white flywheel
          this.buffer.pixels[i].color = Color.BlendLightPaint(
            new Color(this.buffer.pixels[i].color), c).ToInt();
        }
      }
    }

    private static double Wrap(double x, double lo, double hi) {
      double range = hi - lo;
      return x - range * Math.Floor((x - lo) / range);
    }
  }
}
