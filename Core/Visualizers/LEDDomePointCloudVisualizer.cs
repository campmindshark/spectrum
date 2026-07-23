using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using static Spectrum.MathUtil;

namespace Spectrum.Visualizers {

  // A "point cloud" layer: a set of glowing spots scattered over the dome that
  // the wands can shove around. Each spot lives on the unit sphere, springs
  // gently back toward a home position, and is pushed away (along the surface)
  // whenever a moving wand's aim point passes near it. Let go of the wands and
  // the cloud settles back into its resting constellation. The simulation is
  // bounded to the physical dome's positive-Z hemisphere; spots reflect at the
  // rim instead of disappearing onto an unrendered lower half.
  //
  // Unlike Metaball/Ripple, live interaction uses every moving wand's individual
  // aim point (so two people can each stir a different part of the cloud), not
  // the single resolved CurrentCenter. When no wand is moving, the shared
  // OrientationCenter supplies its idle screen-saver aim point so the resting
  // cloud continues to drift and react with the rest of the orientation layers.
  //
  // Tunables come from the point-cloud definition in LayerCatalog
  // and are read fresh every frame. `count` reseeds the lattice when it changes;
  // the rest tune the physics and the drawn spot size. Render is O(pixels *
  // spots) — fine for the schema's max of 320 spots; revisit if that grows.
  class LEDDomePointCloudVisualizer : DomeLayerVisualizer {

    private const double IDLE_LEVEL = 0.4;

    // The direction a wand at calibrated rest "aims" at, in dome space. Matches
    // OrientationCenter.Spot: at identity rotation Transform(p, id) == p, so the
    // aim point is the pixel that maps onto this pole. A device's live aim point
    // is this pole pulled back through the inverse of its rotation (see
    // CollectAimPoints).
    private static readonly Vector3 AimPole = new Vector3(-1, 0, 0);

    private readonly DomeLayerEnvironment environment;
    private readonly LayerRendererRuntime runtime;
    private readonly OrientationInput orientationInput;
    private readonly OrientationCenter center;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;

    // Static per-pixel unit-sphere geometry is baked once and bucketed by its
    // coordinates. Spots move, so rendering asks this index for the 3x3x3 cells
    // around each spot instead of testing every pixel against every spot. The
    // exact angular dot-product check still decides coverage; the grid only
    // rejects impossible pairs.
    private readonly PixelSpatialIndex pixelSpatialIndex;
    private readonly double[] bestPixelValues;
    private readonly double[] bestPixelHues;

    private readonly FrameClock frameClock = new FrameClock();

    // One movable point in the cloud.
    internal struct Spot {
      public Vector3 pos;   // current unit-sphere position
      public Vector3 home;  // rest position it springs back toward
      public Vector3 vel;   // velocity in the local tangent plane
      public double hue;    // 0..1, fixed per spot for now
    }

    // Reseeded whenever the `count` param changes; spotCount tracks the length
    // the current array was seeded for so we only rebuild on an actual change.
    private Spot[] spots = Array.Empty<Spot>();
    private int spotCount;

    // Scratch list of this frame's moving-wand aim points, rebuilt each tick to
    // avoid per-frame allocation.
    private readonly List<Vector3> aimPoints = new List<Vector3>();

    public LEDDomePointCloudVisualizer(
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
      ImmutableArray<Vector3> pixelPositions =
        this.buffer.BakePixelPositions();
      this.pixelSpatialIndex = new PixelSpatialIndex(pixelPositions);
      this.bestPixelValues = new double[pixelPositions.Length];
      this.bestPixelHues = new double[pixelPositions.Length];
      Reseed(ParamCount());
    }

    // Current `count` param as an int, clamped defensively to at least one spot.
    private int ParamCount() {
      return Math.Max(
        1, this.runtime.GetOptions<PointCloudLayerOptions>().Count);
    }

    // Scatter the cloud roughly evenly over the visible dome with an equal-area
    // Fibonacci-hemisphere lattice, so every home has LEDs on which to render.
    // Hue runs around the color wheel with the index.
    private void Reseed(int count) {
      var result = new Spot[count];
      for (int i = 0; i < count; i++) {
        Vector3 p = FibonacciHemispherePoint(i, count);
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

    internal static Vector3 FibonacciHemispherePoint(int index, int count) {
      if (count <= 0 || index < 0 || index >= count) {
        throw new ArgumentOutOfRangeException(nameof(index));
      }
      double golden = Math.PI * (3 - Math.Sqrt(5)); // ~2.399963 rad
      double z = 1 - (index + 0.5) / count; // equal-area bands, crown to rim
      double r = Math.Sqrt(Math.Max(0, 1 - z * z));
      double theta = golden * index;
      return Vector3.Normalize(new Vector3(
        (float)(Math.Cos(theta) * r),
        (float)(Math.Sin(theta) * r),
        (float)z));
    }

    public int Priority => 2;

    public string LayerKey => "point-cloud";
    public DomeFrame LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[]? inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      double frameScale = this.frameClock.Tick();

      int count = ParamCount();
      if (count != this.spotCount) {
        Reseed(count);
      }
      PointCloudLayerOptions options =
        this.runtime.GetOptions<PointCloudLayerOptions>();
      double spotSize = options.SpotSize;
      double pushRadius = options.PushRadius;
      double pushStrength = options.PushStrength;
      double springStrength = options.SpringStrength;
      double damping = options.Damping;

      // Advance the shared center even when Point Cloud is the only active
      // orientation layer. The fixed level affects only idle wander.
      this.center.Update(IDLE_LEVEL);
      CollectAimPoints();
      StepPhysics(
        frameScale, pushRadius, pushStrength, springStrength, damping);
      Render(frameScale, spotSize);
    }

    // Each moving wand's aim axis on the dome: its rest pole pulled back
    // through the inverse of the device's current rotation, then folded to the
    // endpoint on the visible hemisphere. When the shared center is idle
    // (including the operator's force-idle mode), use its wandering virtual aim
    // instead of physical devices.
    private void CollectAimPoints() {
      this.aimPoints.Clear();
      if (this.center.Idle) {
        this.aimPoints.Add(FoldAxisToUpperHemisphere(Vector3.Transform(
          AimPole, Quaternion.Conjugate(this.center.CurrentCenter))));
        return;
      }

      IReadOnlyDictionary<int, OrientationDevice> devices =
        this.orientationInput.OperatorFrameDevices;
      foreach (var kvp in devices) {
        if (!kvp.Value.isMoving) {
          continue;
        }
        Quaternion inv = Quaternion.Inverse(kvp.Value.currentRotation());
        this.aimPoints.Add(FoldAxisToUpperHemisphere(
          Vector3.Transform(AimPole, inv)));
      }
    }

    internal static Vector3 FoldAxisToUpperHemisphere(Vector3 axis) {
      axis = Vector3.Normalize(axis);
      return axis.Z < 0 ? -axis : axis;
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
        Vector3 next =
          Vector3.Normalize(spot.pos + spot.vel * (float)frameScale);
        if (next.Z < 0) {
          // Reflect both position and velocity across the rim plane. Folding
          // the entire vector antipodally would jump the spot to the opposite
          // side of the dome; reflecting Z gives the hemisphere a local,
          // continuous boundary instead.
          next = ReflectAcrossRim(next);
          spot.vel = ReflectAcrossRim(spot.vel);
        }
        spot.pos = next;
        spot.vel = Tangent(spot.pos, spot.vel);

        this.spots[s] = spot;
      }
    }

    // Component of v in the tangent plane at unit-sphere point p (drops the
    // radial part). Returns the raw tangent vector (not normalized) so callers
    // that want a magnitude — the velocity integrator — keep it.
    private static Vector3 Tangent(Vector3 p, Vector3 v) {
      return v - p * Vector3.Dot(v, p);
    }

    internal static Vector3 ReflectAcrossRim(Vector3 value) {
      return new Vector3(value.X, value.Y, -value.Z);
    }

    // Draw the cloud: short trails via a fade, then each pixel takes the
    // brightest nearby spot. Angular distance uses the dot product against the
    // baked pixel positions.
    private void Render(double frameScale, double spotSize) {
      double frameRetention =
        1 - Math.Pow(5, -this.environment.GlobalFadeSpeed);
      this.buffer.Fade(Math.Pow(frameRetention, frameScale), 0);

      ResolveNearestSpots(
        this.pixelSpatialIndex, this.spots, spotSize,
        this.bestPixelValues, this.bestPixelHues);
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        if (this.bestPixelValues[i] > 0) {
          this.buffer.pixels[i].color =
            HsvToInt(
              this.bestPixelHues[i], 1, this.bestPixelValues[i]);
        }
      }
    }

    // Resolve the brightest covering spot for every pixel. Exposed internally
    // so the executable regression suite can compare the indexed result with a
    // brute-force reference and verify steady-state allocation behavior.
    internal static void ResolveNearestSpots(
      PixelSpatialIndex index,
      Spot[] spots,
      double spotSize,
      double[] bestValues,
      double[] bestHues
    ) {
      index.Ensure(spotSize);
      Array.Clear(bestValues, 0, bestValues.Length);

      double cosRadius = Math.Cos(spotSize);
      // As in StepPhysics: keep the falloff normalization finite for a tiny
      // spotSize where cosRadius rounds to 1.
      double radiusSpan = Math.Max(1 - cosRadius, 1e-6);

      for (int s = 0; s < spots.Length; s++) {
        Spot spot = spots[s];
        PixelSpatialIndex.Cell center = index.CellFor(spot.pos);
        for (int dz = -1; dz <= 1; dz++) {
          for (int dy = -1; dy <= 1; dy++) {
            for (int dx = -1; dx <= 1; dx++) {
              int pixel = index.HeadAt(center, dx, dy, dz);
              while (pixel >= 0) {
                double cos = Vector3.Dot(
                  index.PositionAt(pixel), spot.pos);
                if (cos > cosRadius) {
                  // Soft round falloff: full at the center, 0 at the edge.
                  double value = (cos - cosRadius) / radiusSpan;
                  // Spots are visited in their original order and equal values
                  // do not replace an earlier winner, matching the old loop.
                  if (value > bestValues[pixel]) {
                    bestValues[pixel] = value;
                    bestHues[pixel] = spot.hue;
                  }
                }
                pixel = index.Next(pixel);
              }
            }
          }
        }
      }
    }

    internal sealed class PixelSpatialIndex {
      internal readonly record struct Cell(int X, int Y, int Z);

      private const double MinimumCellSize = 1e-6;
      private readonly ImmutableArray<Vector3> positions;
      private readonly Dictionary<Cell, int> cellHeads;
      private readonly int[] nextPixel;
      private double indexedSpotSize = double.NaN;
      private double inverseCellSize;

      internal PixelSpatialIndex(ImmutableArray<Vector3> positions) {
        if (positions.IsDefault) {
          throw new ArgumentException(
            "Pixel positions must be initialized.", nameof(positions));
        }
        this.positions = positions;
        this.cellHeads = new Dictionary<Cell, int>(positions.Length);
        this.nextPixel = new int[positions.Length];
      }

      // Angular distance theta between two unit vectors corresponds to chord
      // distance 2*sin(theta/2). A grid cell one chord wide guarantees that any
      // covered pixel differs from its spot by at most one cell on each axis.
      internal void Ensure(double spotSize) {
        if (spotSize == this.indexedSpotSize) {
          return;
        }
        double cellSize = Math.Max(
          MinimumCellSize, 2 * Math.Sin(spotSize / 2));
        this.inverseCellSize = 1 / cellSize;
        this.cellHeads.Clear();
        for (int pixel = 0; pixel < this.positions.Length; pixel++) {
          Cell cell = this.CellFor(this.positions[pixel]);
          this.nextPixel[pixel] =
            this.cellHeads.TryGetValue(cell, out int head) ? head : -1;
          this.cellHeads[cell] = pixel;
        }
        this.indexedSpotSize = spotSize;
      }

      internal Cell CellFor(Vector3 position) => new Cell(
        (int)Math.Floor(position.X * this.inverseCellSize),
        (int)Math.Floor(position.Y * this.inverseCellSize),
        (int)Math.Floor(position.Z * this.inverseCellSize));

      internal int HeadAt(Cell center, int dx, int dy, int dz) =>
        this.cellHeads.TryGetValue(
          new Cell(center.X + dx, center.Y + dy, center.Z + dz),
          out int head) ? head : -1;

      internal int Next(int pixel) => this.nextPixel[pixel];

      internal Vector3 PositionAt(int pixel) => this.positions[pixel];
    }
  }
}
