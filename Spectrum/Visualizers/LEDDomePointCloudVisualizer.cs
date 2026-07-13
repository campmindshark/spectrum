using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Spectrum.Visualizers {

  // A "point cloud" layer: a set of glowing spots scattered over the dome that
  // the wands can shove around. Each spot lives on the unit sphere, springs
  // gently back toward a home position, and is pushed away (along the surface)
  // whenever a moving wand's aim point passes near it. Let go of the wands and
  // the cloud settles back into its resting constellation.
  //
  // Unlike Metaball/Ripple this layer does NOT go through OrientationCenter: it
  // wants every moving wand's individual aim point (so two people can each stir
  // a different part of the cloud), not the single resolved CurrentCenter. It
  // reads the device snapshot directly, the way OrientationCenter itself does.
  // A consequence is that this layer has no idle screen-saver drift — at rest
  // the cloud simply settles and holds; only a moving wand disturbs it.
  //
  // Tunables come from the point-cloud definition in LayerCatalog
  // and are read fresh every frame. `count` reseeds the lattice when it changes;
  // the rest tune the physics and the drawn spot size. Render is O(pixels *
  // spots) — fine for the schema's max of 160 spots; revisit if that grows.
  class LEDDomePointCloudVisualizer : DomeLayerVisualizer {

    // The direction a wand at calibrated rest "aims" at, in dome space. Matches
    // OrientationCenter.Spot: at identity rotation Transform(p, id) == p, so the
    // aim point is the pixel that maps onto this pole. A device's live aim point
    // is this pole pulled back through the inverse of its rotation (see
    // CollectAimPoints).
    private static readonly Vector3 AimPole = new Vector3(-1, 0, 0);

    private readonly Configuration config;
    private readonly LayerRendererRuntime runtime;
    private readonly OrientationInput orientationInput;
    private readonly LEDDomeOutput dome;
    private readonly LEDDomeOutputBuffer buffer;

    // Static per-pixel unit-sphere geometry, baked once (as every orientation
    // layer does).
    private readonly Vector3[] pixelPositions;

    private readonly FrameClock frameClock = new FrameClock();

    // One movable point in the cloud.
    private struct Spot {
      public Vector3 pos;   // current unit-sphere position
      public Vector3 home;  // rest position it springs back toward
      public Vector3 vel;   // velocity in the local tangent plane
      public double hue;    // 0..1, fixed per spot for now
    }

    // Reseeded whenever the `count` param changes; spotCount tracks the length
    // the current array was seeded for so we only rebuild on an actual change.
    private Spot[] spots;
    private int spotCount;

    // Scratch list of this frame's moving-wand aim points, rebuilt each tick to
    // avoid per-frame allocation.
    private readonly List<Vector3> aimPoints = new List<Vector3>();

    public LEDDomePointCloudVisualizer(
      Configuration config,
      OrientationInput orientationInput,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.runtime = config.GetLayerRuntime();
      this.orientationInput = orientationInput;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeOutputBuffer();
      this.pixelPositions = this.buffer.BakePixelPositions();
      Reseed(ParamCount());
    }

    // Current `count` param as an int, clamped defensively to at least one spot.
    private int ParamCount() {
      return Math.Max(1, (int)this.runtime.Parameter("count"));
    }

    // Scatter the cloud roughly evenly over the sphere with a Fibonacci-sphere
    // lattice, so the resting constellation looks deliberate rather than clumpy.
    // Hue runs around the color wheel with the index.
    private void Reseed(int count) {
      var result = new Spot[count];
      double golden = Math.PI * (3 - Math.Sqrt(5)); // ~2.399963 rad
      for (int i = 0; i < count; i++) {
        double y = 1 - (i + 0.5) / count * 2; // 1 .. -1
        double r = Math.Sqrt(Math.Max(0, 1 - y * y));
        double theta = golden * i;
        var p = Vector3.Normalize(new Vector3(
          (float)(Math.Cos(theta) * r),
          (float)y,
          (float)(Math.Sin(theta) * r)
        ));
        result[i] = new Spot {
          pos = p,
          home = p,
          vel = Vector3.Zero,
          hue = (double)i / count,
        };
      }
      this.spots = result;
      this.spotCount = count;
    }

    public int Priority => 2;

    public string LayerKey => "point-cloud";
    public LEDDomeOutputBuffer LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      double frameScale = this.frameClock.Tick();

      int count = ParamCount();
      if (count != this.spotCount) {
        Reseed(count);
      }
      double spotSize = this.runtime.Parameter("spotSize");
      double pushRadius = this.runtime.Parameter("pushRadius");
      double pushStrength = this.runtime.Parameter("pushStrength");
      double springStrength = this.runtime.Parameter("springStrength");
      double damping = this.runtime.Parameter("damping");

      CollectAimPoints();
      StepPhysics(
        frameScale, pushRadius, pushStrength, springStrength, damping);
      Render(frameScale, spotSize);
    }

    // Each moving wand's aim point on the dome: its rest pole pulled back
    // through the inverse of the device's current rotation. A wand sitting
    // still (not isMoving) contributes nothing, so a cloud at rest only settles.
    private void CollectAimPoints() {
      this.aimPoints.Clear();
      IReadOnlyDictionary<int, OrientationDevice> devices =
        this.orientationInput.OperatorFrameDevices;
      foreach (var kvp in devices) {
        if (!kvp.Value.isMoving) {
          continue;
        }
        Quaternion inv = Quaternion.Inverse(kvp.Value.currentRotation());
        this.aimPoints.Add(Vector3.Transform(AimPole, inv));
      }
    }

    // Integrate every spot one frame: wand repulsion + home spring, projected
    // to keep motion (and position) on the sphere.
    private void StepPhysics(
      double frameScale,
      double pushRadius,
      double pushStrength,
      double springStrength,
      double damping
    ) {
      double cosCut = Math.Cos(pushRadius);
      // Guard the (cos - cosCut) / (1 - cosCut) normalization against a
      // pushRadius so small that cosCut rounds to 1.
      double cutSpan = Math.Max(1 - cosCut, 1e-6);

      for (int s = 0; s < this.spots.Length; s++) {
        Spot spot = this.spots[s];
        Vector3 force = Vector3.Zero;

        // Push away from every nearby wand aim point.
        for (int a = 0; a < this.aimPoints.Count; a++) {
          Vector3 aim = this.aimPoints[a];
          double cos = Vector3.Dot(spot.pos, aim);
          if (cos <= cosCut) {
            continue; // aim point is outside this spot's push range
          }
          // Tangent direction at the spot pointing away from the aim point,
          // weighted by how deep inside the radius the aim point is.
          Vector3 away = Tangent(spot.pos, spot.pos - aim);
          double weight = (cos - cosCut) / cutSpan;
          force += away * (float)(pushStrength * weight);
        }

        // Spring back toward home along the surface.
        Vector3 toHome = Tangent(spot.pos, spot.home - spot.pos);
        force += toHome * (float)springStrength;

        // Integrate in the tangent plane, damp, then walk the position along the
        // surface and re-normalize so the spot never leaves the sphere.
        spot.vel = Tangent(spot.pos, spot.vel + force * (float)frameScale);
        spot.vel *= (float)Math.Pow(damping, frameScale);
        spot.pos = Vector3.Normalize(spot.pos + spot.vel * (float)frameScale);

        this.spots[s] = spot;
      }
    }

    // Component of v in the tangent plane at unit-sphere point p (drops the
    // radial part). Returns the raw tangent vector (not normalized) so callers
    // that want a magnitude — the velocity integrator — keep it.
    private static Vector3 Tangent(Vector3 p, Vector3 v) {
      return v - p * Vector3.Dot(v, p);
    }

    // Draw the cloud: short trails via a fade, then each pixel takes the
    // brightest nearby spot. Angular distance uses the dot product against the
    // baked pixel positions.
    private void Render(double frameScale, double spotSize) {
      double frameRetention = 1 - Math.Pow(5, -this.config.domeGlobalFadeSpeed);
      this.buffer.Fade(Math.Pow(frameRetention, frameScale), 0);

      double cosRadius = Math.Cos(spotSize);
      // As in StepPhysics: keep the falloff normalization finite for a tiny
      // spotSize where cosRadius rounds to 1.
      double radiusSpan = Math.Max(1 - cosRadius, 1e-6);

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        Vector3 pixel = this.pixelPositions[i];

        double bestValue = 0;
        double bestHue = 0;
        for (int s = 0; s < this.spots.Length; s++) {
          double cos = Vector3.Dot(pixel, this.spots[s].pos);
          if (cos <= cosRadius) {
            continue;
          }
          // Soft round falloff: full at the center, 0 at the edge of the spot.
          double value = (cos - cosRadius) / radiusSpan;
          if (value > bestValue) {
            bestValue = value;
            bestHue = this.spots[s].hue;
          }
        }

        if (bestValue > 0) {
          this.buffer.pixels[i].color =
            new Color(bestHue, 1, bestValue).ToInt();
        }
      }
    }
  }
}
