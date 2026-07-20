using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace Spectrum.Visualizers {

  // A persistent collection of luminous bodies orbiting gravity wells on the
  // true dome surface. Every connected wand contributes a well; a deterministic
  // fallback keeps the layer legible when no hardware is present. Forces and
  // velocities stay in the local tangent plane, so bodies trace spherical arcs
  // and slingshots rather than passing through the dome. Collisions can remain
  // elastic, emit expanding blooms, or additionally launch finite fragments.
  class LEDDomeOrbitalGardenVisualizer : DomeLayerVisualizer {
    private readonly LayerRendererRuntime runtime;
    private readonly OrientationInput orientationInput;
    private readonly LEDDomeOutput dome;
    private readonly DomeFrame buffer;
    private readonly ImmutableArray<Vector3> pixelPositions;
    private readonly OrbitalGardenState garden;
    private readonly Stopwatch frameTimer = new Stopwatch();
    private readonly List<OrbitalGravityWell> gravityWells =
      new List<OrbitalGravityWell>();

    public LEDDomeOrbitalGardenVisualizer(
      LayerRendererRuntime runtime,
      OrientationInput orientationInput,
      LEDDomeOutput dome
    ) {
      this.runtime = runtime;
      this.orientationInput = orientationInput;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeFrame();
      this.pixelPositions = this.buffer.BakePixelPositions();
      int bodyCount = this.runtime
        .GetOptions<OrbitalGardenLayerOptions>().BodyCount;
      this.garden = new OrbitalGardenState(bodyCount, 0x4F524249);
    }

    public int Priority => 2;
    public string LayerKey => "orbital-garden";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() =>
      this.inputs ?? (this.inputs = new Input[] {
        this.orientationInput,
      });

    public void Visualize() {
      OrbitalGardenLayerOptions options =
        this.runtime.GetOptions<OrbitalGardenLayerOptions>();
      double elapsed = this.ElapsedSeconds();
      this.garden.Resize(options.BodyCount);
      this.CollectGravityWells();
      this.garden.Step(
        elapsed, options.Gravity, options.OrbitalDamping,
        options.CollisionBehavior, options.BodySize, this.gravityWells);

      this.buffer.Fade(TrailRetention(options.TrailLength, elapsed), 0);
      this.PaintWells(options.BodySize, options.Palette);
      this.PaintBodies(options.BodySize, options.Palette);
      this.PaintFragments(options.BodySize, options.Palette);
      this.PaintBlooms(options.BodySize, options.Palette);
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

    private void CollectGravityWells() {
      this.gravityWells.Clear();
      IReadOnlyDictionary<int, OrientationDevice> devices =
        this.orientationInput.OperatorFrameDevices;
      foreach (var kvp in devices.OrderBy(item => item.Key)) {
        Vector3 aim = Vector3.Transform(
          OrientationCenter.Spot,
          Quaternion.Conjugate(kvp.Value.currentRotation()));
        this.gravityWells.Add(new OrbitalGravityWell(
          OrbitalGardenState.FoldToUpperHemisphere(aim),
          kvp.Key & 7));
      }
      if (this.gravityWells.Count == 0) {
        this.gravityWells.Add(OrbitalGardenState.FallbackWell);
      }
    }

    private void PaintWells(double bodySize, int selectedPalette) {
      double radius = Math.Min(0.32, bodySize * 2.4 + 0.025);
      for (int index = 0; index < this.gravityWells.Count; index++) {
        OrbitalGravityWell well = this.gravityWells[index];
        this.PaintSpot(
          well.Position, radius, 1, well.PaletteIndex, selectedPalette);
      }
    }

    private void PaintBodies(double bodySize, int selectedPalette) {
      IReadOnlyList<OrbitalBody> bodies = this.garden.Bodies;
      for (int index = 0; index < bodies.Count; index++) {
        OrbitalBody body = bodies[index];
        double pulse = 0.72 + 0.28 * Math.Sin(
          2 * Math.PI * (this.garden.Time * 0.7 + body.Phase));
        this.PaintSpot(
          body.Position, bodySize, Math.Clamp(pulse, 0.35, 1),
          body.PaletteIndex, selectedPalette);
      }
    }

    private void PaintFragments(double bodySize, int selectedPalette) {
      IReadOnlyList<OrbitalFragment> fragments = this.garden.Fragments;
      for (int index = 0; index < fragments.Count; index++) {
        OrbitalFragment fragment = fragments[index];
        double brightness = OrbitalGardenState.FragmentEnvelope(fragment.Age);
        this.PaintSpot(
          fragment.Position, Math.Max(0.012, bodySize * 0.48),
          brightness, fragment.PaletteIndex, selectedPalette);
      }
    }

    private void PaintBlooms(double bodySize, int selectedPalette) {
      IReadOnlyList<OrbitalBloom> blooms = this.garden.Blooms;
      double width = Math.Max(0.012, bodySize * 0.55);
      for (int bloomIndex = 0;
          bloomIndex < blooms.Count; bloomIndex++) {
        OrbitalBloom bloom = blooms[bloomIndex];
        double radius = OrbitalGardenState.BloomRadius(bloom.Age);
        double envelope = OrbitalGardenState.BloomEnvelope(bloom.Age);
        for (int pixelIndex = 0;
            pixelIndex < this.pixelPositions.Length; pixelIndex++) {
          double distance = Math.Acos(Math.Clamp(Vector3.Dot(
            this.pixelPositions[pixelIndex], bloom.Center), -1, 1));
          double line = 1 - Math.Abs(distance - radius) / width;
          if (line <= 0) {
            continue;
          }
          this.PaintPixelIfBrighter(
            pixelIndex, line * envelope, bloom.PaletteIndex, selectedPalette);
        }
      }
    }

    private void PaintSpot(
      Vector3 center,
      double radius,
      double brightness,
      int paletteIndex,
      int selectedPalette
    ) {
      double cosRadius = Math.Cos(radius);
      double radiusSpan = Math.Max(1 - cosRadius, 1e-8);
      for (int pixelIndex = 0;
          pixelIndex < this.pixelPositions.Length; pixelIndex++) {
        double alignment = Vector3.Dot(
          this.pixelPositions[pixelIndex], center);
        if (alignment <= cosRadius) {
          continue;
        }
        double radial = (alignment - cosRadius) / radiusSpan;
        this.PaintPixelIfBrighter(
          pixelIndex, radial * brightness, paletteIndex, selectedPalette);
      }
    }

    private void PaintPixelIfBrighter(
      int pixelIndex,
      double brightness,
      int paletteIndex,
      int selectedPalette
    ) {
      brightness = Math.Clamp(brightness, 0, 1);
      if (brightness <= 0) {
        return;
      }
      int tint = this.dome.GetGradientColor(
        paletteIndex, brightness, 0, true, selectedPalette);
      int color = LEDColor.ScaleColor(tint, brightness);
      ref LEDDomeOutputPixel pixel = ref this.buffer.pixels[pixelIndex];
      if (ColorEnergy(color) >= ColorEnergy(pixel.color)) {
        pixel.color = color;
        pixel.SetAlpha(brightness);
        pixel.hue = (paletteIndex & 7) / 8.0;
      }
    }

    internal static double TrailRetention(
      double halfLife, double elapsedSeconds
    ) => halfLife <= 0
      ? 0
      : Math.Pow(0.5, Math.Max(0, elapsedSeconds) / halfLife);

    private static int ColorEnergy(int color) =>
      ((color >> 16) & 0xFF) +
      ((color >> 8) & 0xFF) +
      (color & 0xFF);
  }

  internal readonly record struct OrbitalGravityWell(
    Vector3 Position, int PaletteIndex);

  internal readonly record struct OrbitalBody(
    Vector3 Position, Vector3 Velocity, int PaletteIndex, double Phase);

  internal readonly record struct OrbitalBloom(
    Vector3 Center, double Age, int PaletteIndex);

  internal readonly record struct OrbitalFragment(
    Vector3 Position, Vector3 Velocity, double Age, int PaletteIndex);

  // Deterministic, bounded simulation state. The body array is resized in
  // place when the live control changes, preserving existing orbits. Gravity
  // and integration are tangent to the unit sphere; rim crossings reflect back
  // into the visible hemisphere. Short collision cooldowns prevent resting
  // overlaps from emitting a bloom every frame.
  internal sealed class OrbitalGardenState {
    private const double GoldenAngle = 2.399963229728653;
    private const double MaximumSpeed = 1.35;
    private const double CollisionCooldown = 0.22;
    private const int MaximumBlooms = 32;
    private const int MaximumFragments = 128;
    private const double BloomLifetime = 0.9;
    private const double FragmentLifetime = 0.65;

    private OrbitalBody[] bodies = Array.Empty<OrbitalBody>();
    private double[] collisionCooldowns = Array.Empty<double>();
    private readonly List<OrbitalBloom> blooms =
      new List<OrbitalBloom>();
    private readonly List<OrbitalFragment> fragments =
      new List<OrbitalFragment>();
    private readonly double seedPhase;

    public static OrbitalGravityWell FallbackWell { get; } =
      new OrbitalGravityWell(
        Vector3.Normalize(new Vector3(0.2f, -0.14f, 0.97f)), 0);

    public IReadOnlyList<OrbitalBody> Bodies => this.bodies;
    public IReadOnlyList<OrbitalBloom> Blooms => this.blooms;
    public IReadOnlyList<OrbitalFragment> Fragments => this.fragments;
    public double Time { get; private set; }

    public OrbitalGardenState(int bodyCount, int randomSeed = 0) {
      uint mixed = unchecked((uint)randomSeed * 747796405u + 2891336453u);
      this.seedPhase = mixed / (double)uint.MaxValue * 2 * Math.PI;
      this.Resize(bodyCount);
    }

    public void Resize(int bodyCount) {
      bodyCount = Math.Max(1, bodyCount);
      if (bodyCount == this.bodies.Length) {
        return;
      }

      var resizedBodies = new OrbitalBody[bodyCount];
      var resizedCooldowns = new double[bodyCount];
      int retained = Math.Min(bodyCount, this.bodies.Length);
      Array.Copy(this.bodies, resizedBodies, retained);
      Array.Copy(this.collisionCooldowns, resizedCooldowns, retained);
      for (int index = retained; index < bodyCount; index++) {
        resizedBodies[index] = this.CreateBody(index, bodyCount);
      }
      this.bodies = resizedBodies;
      this.collisionCooldowns = resizedCooldowns;
    }

    private OrbitalBody CreateBody(int index, int bodyCount) {
      Vector3 well = FallbackWell.Position;
      Vector3 east = NormalizeOrZero(Vector3.Cross(Vector3.UnitZ, well));
      if (east.LengthSquared() < 1e-8) {
        east = Vector3.UnitX;
      }
      Vector3 north = Vector3.Normalize(Vector3.Cross(well, east));
      double ring = 0.16 + 0.50 * Math.Sqrt(
        (index + 0.5) / Math.Max(1, bodyCount));
      double angle = this.seedPhase + index * GoldenAngle;
      Vector3 radial = east * (float)Math.Cos(angle) +
        north * (float)Math.Sin(angle);
      Vector3 position = Vector3.Normalize(
        well * (float)Math.Cos(ring) +
        radial * (float)Math.Sin(ring));
      Vector3 tangent = NormalizeOrZero(Vector3.Cross(well, position));
      double speed = 0.18 + 0.14 * Math.Sqrt(ring / 0.66);
      Vector3 velocity = tangent * (float)speed;
      double phase = (index * 0.6180339887498949 +
        this.seedPhase / (2 * Math.PI)) % 1;
      return new OrbitalBody(position, velocity, index & 7, phase);
    }

    internal void SeedBody(
      int index,
      Vector3 position,
      Vector3 velocity,
      int paletteIndex = 0
    ) {
      if (index < 0 || index >= this.bodies.Length) {
        throw new ArgumentOutOfRangeException(nameof(index));
      }
      position = FoldToUpperHemisphere(position);
      velocity = Tangent(position, velocity);
      this.bodies[index] = this.bodies[index] with {
        Position = position,
        Velocity = velocity,
        PaletteIndex = paletteIndex & 7,
      };
      this.collisionCooldowns[index] = 0;
    }

    public void Step(
      double elapsedSeconds,
      double gravity,
      double orbitalDamping,
      int collisionBehavior,
      double bodySize,
      IReadOnlyList<OrbitalGravityWell> gravityWells
    ) {
      double elapsed = Math.Clamp(elapsedSeconds, 0, 0.1);
      if (elapsed <= 0 || this.bodies.Length == 0) {
        return;
      }
      gravity = Math.Max(0, gravity);
      orbitalDamping = Math.Max(0, orbitalDamping);
      collisionBehavior = Math.Clamp(collisionBehavior, 0, 2);
      gravityWells ??= Array.Empty<OrbitalGravityWell>();

      for (int index = 0; index < this.bodies.Length; index++) {
        OrbitalBody body = this.bodies[index];
        Vector3 acceleration = Vector3.Zero;
        double strongestPull = double.NegativeInfinity;
        int paletteIndex = body.PaletteIndex;

        if (gravityWells.Count == 0) {
          this.AddGravity(
            body.Position, FallbackWell, gravity,
            ref acceleration, ref strongestPull, ref paletteIndex);
        } else {
          for (int wellIndex = 0;
              wellIndex < gravityWells.Count; wellIndex++) {
            this.AddGravity(
              body.Position, gravityWells[wellIndex], gravity,
              ref acceleration, ref strongestPull, ref paletteIndex);
          }
        }

        Vector3 velocity = Tangent(
          body.Position,
          body.Velocity + acceleration * (float)elapsed);
        velocity *= (float)Math.Exp(-orbitalDamping * elapsed);
        double speed = velocity.Length();
        if (speed > MaximumSpeed) {
          velocity *= (float)(MaximumSpeed / speed);
        }

        Vector3 position = Vector3.Normalize(
          body.Position + velocity * (float)elapsed);
        if (position.Z < 0) {
          position = ReflectAcrossRim(position);
          velocity = ReflectAcrossRim(velocity);
        }
        position = Vector3.Normalize(position);
        velocity = Tangent(position, velocity);
        this.bodies[index] = body with {
          Position = position,
          Velocity = velocity,
          PaletteIndex = paletteIndex,
        };
        this.collisionCooldowns[index] = Math.Max(
          0, this.collisionCooldowns[index] - elapsed);
      }

      this.ResolveCollisions(
        bodySize, collisionBehavior);
      this.AdvanceEffects(elapsed, orbitalDamping);
      this.Time += elapsed;
    }

    private void AddGravity(
      Vector3 bodyPosition,
      OrbitalGravityWell well,
      double gravity,
      ref Vector3 acceleration,
      ref double strongestPull,
      ref int paletteIndex
    ) {
      Vector3 wellPosition = FoldToUpperHemisphere(well.Position);
      double alignment = Math.Clamp(
        Vector3.Dot(bodyPosition, wellPosition), -1, 1);
      double angle = Math.Acos(alignment);
      Vector3 direction = NormalizeOrZero(Tangent(
        bodyPosition, wellPosition - bodyPosition * (float)alignment));
      double pull = gravity * 0.026 / (0.035 + angle * angle);
      pull = Math.Min(pull, 3.5);
      acceleration += direction * (float)pull;
      if (pull > strongestPull) {
        strongestPull = pull;
        paletteIndex = well.PaletteIndex & 7;
      }
    }

    private void ResolveCollisions(
      double bodySize, int collisionBehavior
    ) {
      double collisionAngle = Math.Max(0.025, bodySize * 1.8);
      double collisionDistanceSq =
        2 - 2 * Math.Cos(collisionAngle);
      for (int first = 0; first < this.bodies.Length; first++) {
        if (this.collisionCooldowns[first] > 0) {
          continue;
        }
        for (int second = first + 1;
            second < this.bodies.Length; second++) {
          if (this.collisionCooldowns[second] > 0 ||
              Vector3.DistanceSquared(
                this.bodies[first].Position,
                this.bodies[second].Position) > collisionDistanceSq) {
            continue;
          }
          this.Collide(first, second, collisionBehavior);
          break;
        }
      }
    }

    private void Collide(int first, int second, int behavior) {
      OrbitalBody bodyA = this.bodies[first];
      OrbitalBody bodyB = this.bodies[second];
      Vector3 normalA = NormalizeOrZero(Tangent(
        bodyA.Position, bodyA.Position - bodyB.Position));
      if (normalA.LengthSquared() < 1e-8) {
        normalA = DeterministicTangent(bodyA.Position, first + second);
      }
      Vector3 normalB = NormalizeOrZero(Tangent(
        bodyB.Position, bodyB.Position - bodyA.Position));
      if (normalB.LengthSquared() < 1e-8) {
        normalB = -normalA;
      }

      Vector3 averageVelocity =
        (bodyA.Velocity + bodyB.Velocity) * 0.5f;
      double relativeSpeed = Math.Abs(Vector3.Dot(
        bodyA.Velocity - bodyB.Velocity, normalA));
      double rebound = Math.Max(0.10, relativeSpeed * 0.55);
      Vector3 velocityA = Tangent(
        bodyA.Position,
        averageVelocity + normalA * (float)rebound);
      Vector3 velocityB = Tangent(
        bodyB.Position,
        averageVelocity + normalB * (float)rebound);
      this.bodies[first] = bodyA with { Velocity = velocityA };
      this.bodies[second] = bodyB with { Velocity = velocityB };
      this.collisionCooldowns[first] = CollisionCooldown;
      this.collisionCooldowns[second] = CollisionCooldown;

      Vector3 center = NormalizeOrZero(bodyA.Position + bodyB.Position);
      if (center.LengthSquared() < 1e-8) {
        center = bodyA.Position;
      }
      int paletteIndex = bodyA.PaletteIndex;
      if (behavior >= 1) {
        if (this.blooms.Count >= MaximumBlooms) {
          this.blooms.RemoveAt(0);
        }
        this.blooms.Add(new OrbitalBloom(center, 0, paletteIndex));
      }
      if (behavior >= 2) {
        this.LaunchFragments(
          center, averageVelocity, paletteIndex, first + second);
      }
    }

    private void LaunchFragments(
      Vector3 center,
      Vector3 baseVelocity,
      int paletteIndex,
      int phaseIndex
    ) {
      Vector3 east = DeterministicTangent(center, phaseIndex);
      Vector3 north = Vector3.Normalize(Vector3.Cross(center, east));
      for (int fragmentIndex = 0; fragmentIndex < 4; fragmentIndex++) {
        if (this.fragments.Count >= MaximumFragments) {
          this.fragments.RemoveAt(0);
        }
        double angle = this.seedPhase + phaseIndex * GoldenAngle +
          fragmentIndex * Math.PI / 2;
        Vector3 direction = east * (float)Math.Cos(angle) +
          north * (float)Math.Sin(angle);
        Vector3 velocity = Tangent(
          center, baseVelocity * 0.35f + direction * 0.48f);
        this.fragments.Add(new OrbitalFragment(
          center, velocity, 0, (paletteIndex + fragmentIndex) & 7));
      }
    }

    private void AdvanceEffects(
      double elapsed, double orbitalDamping
    ) {
      for (int index = this.blooms.Count - 1; index >= 0; index--) {
        OrbitalBloom bloom = this.blooms[index];
        double age = bloom.Age + elapsed;
        if (age >= BloomLifetime) {
          this.blooms.RemoveAt(index);
        } else {
          this.blooms[index] = bloom with { Age = age };
        }
      }

      for (int index = this.fragments.Count - 1; index >= 0; index--) {
        OrbitalFragment fragment = this.fragments[index];
        double age = fragment.Age + elapsed;
        if (age >= FragmentLifetime) {
          this.fragments.RemoveAt(index);
          continue;
        }
        Vector3 velocity = fragment.Velocity * (float)Math.Exp(
          -(1.2 + orbitalDamping) * elapsed);
        Vector3 position = Vector3.Normalize(
          fragment.Position + velocity * (float)elapsed);
        if (position.Z < 0) {
          position = ReflectAcrossRim(position);
          velocity = ReflectAcrossRim(velocity);
        }
        position = Vector3.Normalize(position);
        velocity = Tangent(position, velocity);
        this.fragments[index] = fragment with {
          Position = position,
          Velocity = velocity,
          Age = age,
        };
      }
    }

    public static double BloomRadius(double age) =>
      0.03 + 0.50 * Math.Clamp(age, 0, BloomLifetime);

    public static double BloomEnvelope(double age) {
      double life = 1 - Math.Clamp(age, 0, BloomLifetime) / BloomLifetime;
      return life * life;
    }

    public static double FragmentEnvelope(double age) {
      double life = 1 - Math.Clamp(
        age, 0, FragmentLifetime) / FragmentLifetime;
      return life * life;
    }

    internal static Vector3 FoldToUpperHemisphere(Vector3 direction) {
      Vector3 normalized = NormalizeOrZero(direction);
      if (normalized.LengthSquared() < 1e-8) {
        return Vector3.UnitZ;
      }
      return normalized.Z < 0 ? -normalized : normalized;
    }

    private static Vector3 DeterministicTangent(
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

    private static Vector3 Tangent(Vector3 position, Vector3 value) =>
      value - position * Vector3.Dot(position, value);

    private static Vector3 NormalizeOrZero(Vector3 value) =>
      value.LengthSquared() > 1e-12 ? Vector3.Normalize(value) : Vector3.Zero;

    private static Vector3 ReflectAcrossRim(Vector3 value) =>
      new Vector3(value.X, value.Y, -value.Z);
  }
}
