using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;

namespace Spectrum.Visualizers {

  // Large persistent bodies move through a spherical thermal field rather
  // than sampling a generic noise/metaball function. Warm bodies rise toward
  // the selected gravity pole, cool there, and sink back toward the opposite
  // side. Nearby bodies are pulled into shared silhouettes by surface tension;
  // audio adds heat and separation, pinching elongated bodies into two lobes.
  class LEDDomeLavaLampSkyVisualizer : DomeLayerVisualizer {
    private readonly LayerRendererRuntime runtime;
    private readonly AudioInput audio;
    private readonly OrientationInput orientationInput;
    private readonly OrientationCenter orientationCenter;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private readonly ImmutableArray<Vector3> pixelPositions;
    private readonly LavaLampSkyState state;
    private readonly Stopwatch frameTimer = new Stopwatch();

    public LEDDomeLavaLampSkyVisualizer(
      LayerRendererRuntime runtime,
      AudioInput audio,
      OrientationInput orientationInput,
      OrientationCenter orientationCenter,
      DomeRenderContext dome
    ) {
      this.runtime = runtime;
      this.audio = audio;
      this.orientationInput = orientationInput;
      this.orientationCenter = orientationCenter;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
      this.pixelPositions = this.buffer.BakePixelPositions();
      this.state = new LavaLampSkyState(
        this.runtime.GetOptions<LavaLampSkyLayerOptions>().BlobCount,
        0x4C415641);
    }

    public int Priority => 2;
    public string LayerKey => "lava-lamp-sky";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() =>
      this.inputs ?? (this.inputs = new Input[] {
        this.audio, this.orientationInput,
      });

    public void Visualize() {
      LavaLampSkyLayerOptions options =
        this.runtime.GetOptions<LavaLampSkyLayerOptions>();
      double elapsed = this.ElapsedSeconds();
      double level = Math.Clamp(this.audio.Volume, 0, 1);

      this.orientationCenter.Update(level);
      Vector3 gravityAxis = GravityAxis(
        this.orientationCenter.CurrentCenter, options.BindGravity);
      this.state.Resize(options.BlobCount);
      this.state.Step(
        elapsed, options.Viscosity, options.Buoyancy,
        options.SurfaceTension, options.Heat, level, gravityAxis);
      this.Paint(options.Palette);
    }

    private double ElapsedSeconds() {
      if (!this.frameTimer.IsRunning) {
        this.frameTimer.Restart();
        return 1.0 / 60;
      }
      double elapsed = this.frameTimer.Elapsed.TotalSeconds;
      this.frameTimer.Restart();
      return Math.Clamp(elapsed, 0, 0.1);
    }

    private void Paint(int selectedPalette) {
      this.buffer.ResetComposite();
      for (int pixelIndex = 0;
          pixelIndex < this.pixelPositions.Length; pixelIndex++) {
        Vector3 point = this.pixelPositions[pixelIndex];
        double uncovered = 1;
        double strongest = 0;
        LavaLampBlob strongestBlob = default;
        for (int blobIndex = 0;
            blobIndex < this.state.Blobs.Count; blobIndex++) {
          LavaLampBlob blob = this.state.Blobs[blobIndex];
          double strength = BlobStrength(point, blob);
          if (strength <= 0) {
            continue;
          }
          uncovered *= 1 - strength;
          if (strength > strongest) {
            strongest = strength;
            strongestBlob = blob;
          }
        }

        double coverage = 1 - uncovered;
        if (coverage <= 0.001) {
          continue;
        }
        double heatGlow = Math.Clamp(strongestBlob.Temperature, 0, 1);
        double brightness = Math.Clamp(
          0.22 + 0.62 * coverage + 0.16 * heatGlow, 0, 1);
        int tint = this.dome.GetGradientColor(
          strongestBlob.PaletteIndex, 1 - strongest, 0, true, selectedPalette);
        ref LEDDomeOutputPixel pixel = ref this.buffer.pixels[pixelIndex];
        pixel.color = LEDColor.ScaleColor(tint, brightness);
        pixel.SetAlpha(Math.Clamp(coverage, 0, 1));
        pixel.hue = (strongestBlob.PaletteIndex & 7) / 8.0;
      }
    }

    internal static Vector3 GravityAxis(
      Quaternion orientation, bool bindGravity
    ) {
      if (!bindGravity) {
        return Vector3.UnitZ;
      }
      Vector3 aimed = Vector3.Transform(
        OrientationCenter.Spot, Quaternion.Conjugate(orientation));
      return LavaLampSkyState.FoldToUpperHemisphere(aimed);
    }

    // Evaluates a soft spherical ellipse in the blob center's tangent plane.
    // Split is geometric rather than a field threshold: one ellipse becomes a
    // pinched pair of smaller lobes with preserved approximate area.
    internal static double BlobStrength(Vector3 point, LavaLampBlob blob) {
      Vector3 center = LavaLampSkyState.FoldToUpperHemisphere(blob.Position);
      point = LavaLampSkyState.FoldToUpperHemisphere(point);
      double alignment = Math.Clamp(Vector3.Dot(center, point), -1, 1);
      double angle = Math.Acos(alignment);
      double radius = Math.Max(0.02, blob.Radius);
      double major = radius * (1 + 0.62 * Math.Max(0, blob.Stretch));
      if (angle > major * 1.75) {
        return 0;
      }

      Vector3 axis = LavaLampSkyState.NormalizeTangent(
        center, blob.ShapeAxis);
      if (axis.LengthSquared() < 1e-8) {
        axis = LavaLampSkyState.DeterministicTangent(center, 0);
      }
      Vector3 side = Vector3.Normalize(Vector3.Cross(center, axis));
      Vector3 displacement = Vector3.Zero;
      if (angle > 1e-8) {
        Vector3 direction = LavaLampSkyState.NormalizeOrZero(
          point - center * (float)alignment);
        displacement = direction * (float)angle;
      }
      double x = Vector3.Dot(displacement, axis);
      double y = Vector3.Dot(displacement, side);
      double minor = radius / Math.Sqrt(
        1 + 0.52 * Math.Max(0, blob.Stretch));

      double split = Math.Clamp(blob.Split, 0, 1);
      double lobeScale = 1 - 0.32 * split;
      double offset = radius * 0.78 * split;
      double lobeMajor = Math.Max(0.01, major * lobeScale);
      double lobeMinor = Math.Max(0.01, minor * lobeScale);
      double first = Math.Sqrt(
        Square((x - offset) / lobeMajor) + Square(y / lobeMinor));
      double second = Math.Sqrt(
        Square((x + offset) / lobeMajor) + Square(y / lobeMinor));
      double normalizedDistance = Math.Min(first, second);
      const double SoftInterior = 0.72;
      const double SoftExterior = 1.14;
      double strength = Math.Clamp(
        (SoftExterior - normalizedDistance) /
        (SoftExterior - SoftInterior), 0, 1);
      return strength * strength * (3 - 2 * strength);
    }

    private static double Square(double value) => value * value;
  }

  internal readonly record struct LavaLampBlob(
    Vector3 Position,
    Vector3 Velocity,
    Vector3 ShapeAxis,
    double Radius,
    double Temperature,
    double Phase,
    int PaletteIndex,
    double Stretch,
    double Split);

  // Allocation-stable, deterministic spherical blob dynamics. Pair forces are
  // calculated before integration so no blob observes a partially updated
  // neighbor. Blob count changes preserve the retained bodies and add/remove
  // only the tail, which keeps live slider edits visually continuous.
  internal sealed class LavaLampSkyState {
    private const double GoldenAngle = 2.399963229728653;
    private const double MaximumSpeed = 0.62;
    private LavaLampBlob[] blobs = Array.Empty<LavaLampBlob>();
    private Vector3[] forces = Array.Empty<Vector3>();
    private double[] mergeProximity = Array.Empty<double>();
    private Vector3[] mergeDirections = Array.Empty<Vector3>();
    private readonly double seedPhase;

    public System.Collections.Generic.IReadOnlyList<LavaLampBlob> Blobs =>
      this.blobs;
    public double Time { get; private set; }

    public LavaLampSkyState(int blobCount, int randomSeed = 0) {
      uint mixed = unchecked((uint)randomSeed * 747796405u + 2891336453u);
      this.seedPhase = mixed / (double)uint.MaxValue * 2 * Math.PI;
      this.Resize(blobCount);
    }

    public void Resize(int blobCount) {
      blobCount = Math.Max(1, blobCount);
      if (blobCount == this.blobs.Length) {
        return;
      }
      var resized = new LavaLampBlob[blobCount];
      int retained = Math.Min(blobCount, this.blobs.Length);
      Array.Copy(this.blobs, resized, retained);
      for (int index = retained; index < blobCount; index++) {
        resized[index] = this.CreateBlob(index, blobCount);
      }
      this.blobs = resized;
      this.forces = new Vector3[blobCount];
      this.mergeProximity = new double[blobCount];
      this.mergeDirections = new Vector3[blobCount];
    }

    private LavaLampBlob CreateBlob(int index, int blobCount) {
      double z = 0.08 + 0.84 *
        ((index * 0.7548776662466927 + 0.31) % 1);
      double azimuth = this.seedPhase + index * GoldenAngle;
      double radial = Math.Sqrt(Math.Max(0, 1 - z * z));
      Vector3 position = Vector3.Normalize(new Vector3(
        (float)(radial * Math.Cos(azimuth)),
        (float)(radial * Math.Sin(azimuth)), (float)z));
      Vector3 axis = DeterministicTangent(position, index);
      double radius = TargetRadius(blobCount, index, this.seedPhase);
      double temperature = 0.28 + 0.52 *
        ((index * 0.6180339887498949 + 0.17) % 1);
      return new LavaLampBlob(
        position, axis * 0.015f, axis, radius, temperature,
        (index * 0.3819660112501051 + this.seedPhase /
          (2 * Math.PI)) % 1,
        index & 7, 0, 0);
    }

    internal void SeedBlob(
      int index,
      Vector3 position,
      Vector3 velocity,
      double temperature,
      double radius = 0.36
    ) {
      if (index < 0 || index >= this.blobs.Length) {
        throw new ArgumentOutOfRangeException(nameof(index));
      }
      position = FoldToUpperHemisphere(position);
      velocity = ProjectTangent(position, velocity);
      Vector3 axis = velocity.LengthSquared() > 1e-8
        ? Vector3.Normalize(velocity)
        : DeterministicTangent(position, index);
      this.blobs[index] = this.blobs[index] with {
        Position = position,
        Velocity = velocity,
        ShapeAxis = axis,
        Temperature = temperature,
        Radius = radius,
        Stretch = 0,
        Split = 0,
      };
    }

    public void Step(
      double elapsedSeconds,
      double viscosity,
      double buoyancy,
      double surfaceTension,
      double heat,
      double audioLevel,
      Vector3 gravityAxis
    ) {
      double elapsed = Math.Clamp(elapsedSeconds, 0, 0.1);
      if (elapsed <= 0 || this.blobs.Length == 0) {
        return;
      }
      viscosity = Math.Max(0.01, viscosity);
      surfaceTension = Math.Max(0, surfaceTension);
      gravityAxis = FoldToUpperHemisphere(gravityAxis);
      double effectiveHeat = EffectiveHeat(heat, audioLevel);
      double effectiveBuoyancy = EffectiveBuoyancy(buoyancy, audioLevel);
      double separation = SeparationResponse(heat, audioLevel);
      Array.Clear(this.forces, 0, this.forces.Length);
      Array.Clear(this.mergeProximity, 0, this.mergeProximity.Length);
      Array.Clear(this.mergeDirections, 0, this.mergeDirections.Length);

      for (int index = 0; index < this.blobs.Length; index++) {
        LavaLampBlob blob = this.blobs[index];
        double height = (1 + Math.Clamp(
          Vector3.Dot(blob.Position, gravityAxis), -1, 1)) * 0.5;
        double thermalTarget = Math.Clamp(
          0.12 + 0.48 * effectiveHeat + 0.50 * (1 - height) +
          0.055 * Math.Sin(
            2 * Math.PI * (blob.Phase + this.Time * 0.075)),
          0, 1.35);
        double thermalResponse = 1 - Math.Exp(
          -elapsed / (0.65 + viscosity * 0.28));
        double temperature = blob.Temperature +
          (thermalTarget - blob.Temperature) * thermalResponse;
        this.blobs[index] = blob with { Temperature = temperature };

        Vector3 vertical = NormalizeTangent(blob.Position, gravityAxis);
        if (vertical.LengthSquared() < 1e-8) {
          vertical = DeterministicTangent(blob.Position, index);
        }
        this.forces[index] += vertical * (float)(
          effectiveBuoyancy * (temperature - 0.58));
        Vector3 drift = DeterministicTangent(
          blob.Position,
          index + (int)Math.Floor(this.Time * 0.16 + blob.Phase * 11));
        this.forces[index] += drift * (float)(
          0.018 * (0.4 + effectiveHeat));
      }

      this.AddPairForces(surfaceTension, separation);

      double damping = Math.Exp(-(0.34 + viscosity * 0.82) * elapsed);
      for (int index = 0; index < this.blobs.Length; index++) {
        LavaLampBlob blob = this.blobs[index];
        Vector3 velocity = ProjectTangent(
          blob.Position,
          blob.Velocity + this.forces[index] * (float)elapsed);
        velocity *= (float)damping;
        double speed = velocity.Length();
        if (speed > MaximumSpeed) {
          velocity *= (float)(MaximumSpeed / speed);
          speed = MaximumSpeed;
        }

        Vector3 position = Vector3.Normalize(
          blob.Position + velocity * (float)elapsed);
        if (position.Z < 0) {
          position = new Vector3(position.X, position.Y, -position.Z);
          velocity = new Vector3(velocity.X, velocity.Y, -velocity.Z);
        }
        position = Vector3.Normalize(position);
        velocity = ProjectTangent(position, velocity);

        Vector3 vertical = NormalizeTangent(position, gravityAxis);
        Vector3 targetAxis = this.mergeProximity[index] > 0.08
          ? NormalizeTangent(position, this.mergeDirections[index])
          : speed > 0.01
            ? NormalizeTangent(position, velocity)
            : vertical;
        if (targetAxis.LengthSquared() < 1e-8) {
          targetAxis = DeterministicTangent(position, index);
        }
        Vector3 oldAxis = NormalizeTangent(position, blob.ShapeAxis);
        double axisResponse = 1 - Math.Exp(
          -elapsed * (0.7 + surfaceTension));
        Vector3 shapeAxis = NormalizeTangent(
          position,
          oldAxis * (float)(1 - axisResponse) +
          targetAxis * (float)axisResponse);
        if (shapeAxis.LengthSquared() < 1e-8) {
          shapeAxis = targetAxis;
        }

        double stretchTarget = Math.Clamp(
          speed * 2.5 / (0.35 + surfaceTension) +
          this.mergeProximity[index] * 0.9, 0, 1.8);
        double splitTarget = Math.Clamp(
          (blob.Temperature - 0.70) * 1.45 + separation * 0.72 -
          surfaceTension * 0.10 - this.mergeProximity[index] * 0.30,
          0, 1);
        double shapeResponse = 1 - Math.Exp(
          -elapsed * (0.75 + surfaceTension * 0.6));
        double radiusTarget = TargetRadius(
          this.blobs.Length, index, this.seedPhase);
        double radiusResponse = 1 - Math.Exp(-elapsed * 0.8);
        this.blobs[index] = blob with {
          Position = position,
          Velocity = velocity,
          ShapeAxis = shapeAxis,
          Radius = blob.Radius +
            (radiusTarget - blob.Radius) * radiusResponse,
          Stretch = blob.Stretch +
            (stretchTarget - blob.Stretch) * shapeResponse,
          Split = blob.Split +
            (splitTarget - blob.Split) * shapeResponse,
        };
      }
      this.Time += elapsed;
    }

    private void AddPairForces(
      double surfaceTension, double separation
    ) {
      for (int first = 0; first < this.blobs.Length; first++) {
        LavaLampBlob a = this.blobs[first];
        for (int second = first + 1;
            second < this.blobs.Length; second++) {
          LavaLampBlob b = this.blobs[second];
          double angle = Math.Acos(Math.Clamp(
            Vector3.Dot(a.Position, b.Position), -1, 1));
          double combinedRadius = a.Radius + b.Radius;
          double interactionRadius = combinedRadius * 1.28;
          if (angle >= interactionRadius) {
            continue;
          }
          Vector3 towardB = NormalizeTangent(a.Position, b.Position);
          Vector3 towardA = NormalizeTangent(b.Position, a.Position);
          double proximity = 1 - angle / interactionRadius;
          double attraction = surfaceTension * 0.20 * proximity *
            (1 - 0.38 * Math.Min(1, separation));
          double repulsionRadius = combinedRadius * 0.68;
          double repulsion = angle < repulsionRadius
            ? separation * 0.72 * (1 - angle / repulsionRadius)
            : 0;
          double interaction = attraction - repulsion;
          this.forces[first] += towardB * (float)interaction;
          this.forces[second] += towardA * (float)interaction;

          if (proximity > this.mergeProximity[first]) {
            this.mergeProximity[first] = proximity;
            this.mergeDirections[first] = towardB;
          }
          if (proximity > this.mergeProximity[second]) {
            this.mergeProximity[second] = proximity;
            this.mergeDirections[second] = towardA;
          }
        }
      }
    }

    internal static double EffectiveHeat(double heat, double audioLevel) =>
      Math.Clamp(heat, 0, 1) + 0.65 * Math.Clamp(audioLevel, 0, 1);

    internal static double EffectiveBuoyancy(
      double buoyancy, double audioLevel
    ) => Math.Max(0, buoyancy) *
      (1 + 0.8 * Math.Clamp(audioLevel, 0, 1));

    internal static double SeparationResponse(
      double heat, double audioLevel
    ) => Math.Clamp(
      0.35 * Math.Clamp(heat, 0, 1) +
      1.05 * Math.Clamp(audioLevel, 0, 1), 0, 1.4);

    private static double TargetRadius(
      int blobCount, int index, double seedPhase
    ) {
      double baseRadius = 0.41 * Math.Sqrt(9.0 / Math.Max(1, blobCount));
      double variation = 0.88 + 0.18 * Math.Sin(
        seedPhase + index * GoldenAngle * 0.63);
      return Math.Clamp(baseRadius * variation, 0.20, 0.58);
    }

    internal static Vector3 FoldToUpperHemisphere(Vector3 direction) {
      Vector3 normalized = NormalizeOrZero(direction);
      if (normalized.LengthSquared() < 1e-8) {
        return Vector3.UnitZ;
      }
      return normalized.Z < 0 ? -normalized : normalized;
    }

    internal static Vector3 NormalizeTangent(
      Vector3 position, Vector3 value
    ) => NormalizeOrZero(ProjectTangent(position, value));

    private static Vector3 ProjectTangent(
      Vector3 position, Vector3 value
    ) => value - position * Vector3.Dot(position, value);

    internal static Vector3 DeterministicTangent(
      Vector3 position, int phaseIndex
    ) {
      Vector3 east = Vector3.Cross(Vector3.UnitZ, position);
      if (east.LengthSquared() < 1e-8) {
        east = Vector3.Cross(Vector3.UnitX, position);
      }
      east = Vector3.Normalize(east);
      Vector3 north = Vector3.Normalize(Vector3.Cross(position, east));
      double angle = phaseIndex * GoldenAngle;
      return east * (float)Math.Cos(angle) +
        north * (float)Math.Sin(angle);
    }

    internal static Vector3 NormalizeOrZero(Vector3 value) =>
      value.LengthSquared() > 1e-12 ? Vector3.Normalize(value) : Vector3.Zero;
  }
}
