using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;

namespace Spectrum.Visualizers {

  // Crown-born droplets run down the true dome hemisphere under projected
  // spherical gravity. Capture volume scales the amount of rain, moving wand
  // axes form local umbrella-like deflection, dry-region, or motion-driven
  // wind fields, and rim impacts become expanding splash rings. The layer
  // buffer retains old light independently from the particle state, turning
  // the moving droplets into surface trails.
  class LEDDomeRainChamberVisualizer : DomeLayerVisualizer {
    private readonly LayerRendererRuntime runtime;
    private readonly IAudioLevelInput audio;
    private readonly OrientationInput orientationInput;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private readonly ImmutableArray<Vector3> pixelPositions;
    private readonly RainChamberState rain;
    private readonly Stopwatch frameTimer = new Stopwatch();
    private readonly List<Vector3> wandAims = new List<Vector3>();
    private readonly List<Vector3> wandMotions = new List<Vector3>();
    private readonly Dictionary<int, Vector3> previousWandAims =
      new Dictionary<int, Vector3>();
    private readonly HashSet<int> activeWandIds = new HashSet<int>();
    private readonly List<int> staleWandIds = new List<int>();

    public LEDDomeRainChamberVisualizer(
      LayerRendererRuntime runtime,
      IAudioLevelInput audio,
      OrientationInput orientationInput,
      DomeRenderContext dome
    ) {
      this.runtime = runtime;
      this.audio = audio;
      this.orientationInput = orientationInput;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
      this.pixelPositions = this.buffer.BakePixelPositions();
      double initialDropletSize = this.runtime
        .GetOptions<RainChamberLayerOptions>().DropletSize;
      this.rain = new RainChamberState(
        0x5241494E, 10, initialDropletSize);
    }

    public int Priority => 2;
    public string LayerKey => "rain-chamber";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() =>
      this.inputs ?? (this.inputs = new Input[] {
        this.audio, this.orientationInput,
      });

    public void Visualize() {
      RainChamberLayerOptions options =
        this.runtime.GetOptions<RainChamberLayerOptions>();
      double elapsed = this.ElapsedSeconds();
      this.CollectWandAims(elapsed);
      this.rain.Step(
        elapsed, options.RainfallRate, options.Gravity, options.Wind,
        options.DropletSize, options.SplashStrength,
        this.audio.Volume, this.wandAims, options.InteractionMode,
        this.wandMotions);

      this.buffer.Fade(
        TrailRetention(options.TrailRetention, elapsed), 0);
      this.PaintDroplets(options.DropletSize, options.Palette);
      this.PaintSplashes(
        options.DropletSize, options.Palette);
      if (options.InteractionMode == 1) {
        ApplyDryRegions(
          this.buffer, this.pixelPositions, this.wandAims, options.Wind);
      }
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

    private void CollectWandAims(double elapsedSeconds) {
      this.wandAims.Clear();
      this.wandMotions.Clear();
      this.activeWandIds.Clear();
      IReadOnlyDictionary<int, OrientationDevice> devices =
        this.orientationInput.OperatorFrameDevices;
      foreach (var kvp in devices) {
        if (!kvp.Value.isMoving) {
          continue;
        }
        Vector3 aim = Vector3.Transform(
          OrientationCenter.Spot,
          Quaternion.Conjugate(kvp.Value.currentRotation()));
        aim = RainChamberState.FoldToUpperHemisphere(aim);
        Vector3 motion = this.previousWandAims.TryGetValue(
          kvp.Key, out Vector3 previousAim)
          ? InferWandMotion(previousAim, aim, elapsedSeconds)
          : Vector3.Zero;
        this.wandAims.Add(aim);
        this.wandMotions.Add(motion);
        this.previousWandAims[kvp.Key] = aim;
        this.activeWandIds.Add(kvp.Key);
      }

      this.staleWandIds.Clear();
      foreach (int deviceId in this.previousWandAims.Keys) {
        if (!this.activeWandIds.Contains(deviceId)) {
          this.staleWandIds.Add(deviceId);
        }
      }
      foreach (int deviceId in this.staleWandIds) {
        this.previousWandAims.Remove(deviceId);
      }
    }

    internal static Vector3 InferWandMotion(
      Vector3 previousAim, Vector3 currentAim, double elapsedSeconds
    ) {
      if (elapsedSeconds <= 1e-6) {
        return Vector3.Zero;
      }
      previousAim = RainChamberState.FoldToUpperHemisphere(previousAim);
      currentAim = RainChamberState.FoldToUpperHemisphere(currentAim);
      double angle = Math.Acos(Math.Clamp(
        Vector3.Dot(previousAim, currentAim), -1, 1));
      if (angle <= 1e-6) {
        return Vector3.Zero;
      }
      Vector3 direction = RainChamberState.NormalizeOrZero(
        RainChamberState.Tangent(currentAim, currentAim - previousAim));
      if (direction.LengthSquared() < 1e-8) {
        return Vector3.Zero;
      }
      double normalizedSpeed = Math.Clamp(
        angle / elapsedSeconds / 2.0, 0, 1);
      return direction * (float)normalizedSpeed;
    }

    private void PaintDroplets(double dotSize, int selectedPalette) {
      IReadOnlyList<RainDroplet> droplets = this.rain.Droplets;
      for (int pixelIndex = 0;
          pixelIndex < this.buffer.pixels.Length; pixelIndex++) {
        Vector3 pixelPosition = this.pixelPositions[pixelIndex];
        double bestStrength = 0;
        int bestPaletteIndex = 0;
        double bestPhase = 0;
        for (int dropletIndex = 0;
            dropletIndex < droplets.Count; dropletIndex++) {
          RainDroplet droplet = droplets[dropletIndex];
          double radius = dotSize * (0.82 + 0.28 * droplet.SizePhase);
          double cosRadius = Math.Cos(radius);
          double alignment = Vector3.Dot(
            pixelPosition, droplet.Position);
          if (alignment <= cosRadius) {
            continue;
          }
          double strength = (alignment - cosRadius) /
            Math.Max(1 - cosRadius, 1e-8);
          strength *= 0.72 + 0.28 * (1 - droplet.Position.Z);
          if (strength > bestStrength) {
            bestStrength = strength;
            bestPaletteIndex = droplet.PaletteIndex;
            bestPhase = droplet.SizePhase;
          }
        }
        if (bestStrength > 0) {
          this.PaintPixel(
            pixelIndex, bestStrength, bestPaletteIndex,
            bestPhase, selectedPalette);
        }
      }
    }

    private void PaintSplashes(double dropletSize, int selectedPalette) {
      IReadOnlyList<RainSplash> splashes = this.rain.Splashes;
      for (int pixelIndex = 0;
          pixelIndex < this.buffer.pixels.Length; pixelIndex++) {
        Vector3 pixelPosition = this.pixelPositions[pixelIndex];
        double bestStrength = 0;
        int bestPaletteIndex = 0;
        double bestPhase = 0;
        for (int splashIndex = 0;
            splashIndex < splashes.Count; splashIndex++) {
          RainSplash splash = splashes[splashIndex];
          double distance = Math.Acos(Math.Clamp(
            Vector3.Dot(pixelPosition, splash.Center), -1, 1));
          double width = Math.Max(0.025, dropletSize * 0.8);
          double ringDistance = Math.Abs(
            distance - RainChamberState.SplashRadius(splash.Age));
          if (ringDistance >= width) {
            continue;
          }
          double strength = (1 - ringDistance / width) *
            RainChamberState.SplashEnvelope(
              splash.Age, splash.Strength);
          if (strength > bestStrength) {
            bestStrength = strength;
            bestPaletteIndex = splash.PaletteIndex;
            bestPhase = 0.2 + splash.Age * 0.8;
          }
        }
        if (bestStrength > 0) {
          this.PaintPixel(
            pixelIndex, bestStrength, bestPaletteIndex,
            bestPhase, selectedPalette);
        }
      }
    }

    private void PaintPixel(
      int pixelIndex, double brightness, int paletteIndex,
      double phase, int selectedPalette
    ) {
      brightness = Math.Clamp(brightness, 0, 1);
      int tint = this.dome.GetGradientColor(
        paletteIndex & 7, phase, 0, true, selectedPalette);
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

    internal static void ApplyDryRegions(
      DomeFrame frame,
      IReadOnlyList<Vector3> positions,
      IReadOnlyList<Vector3> wandAims,
      double wandStrength
    ) {
      double radius = RainChamberState.DryRadius(wandStrength);
      if (radius <= 0 || wandAims == null || wandAims.Count == 0) {
        return;
      }

      double feather = Math.Min(0.08, radius * 0.3);
      double innerRadius = Math.Max(0, radius - feather);
      int count = Math.Min(frame.pixels.Length, positions.Count);
      for (int pixelIndex = 0; pixelIndex < count; pixelIndex++) {
        double nearestAngle = double.MaxValue;
        for (int aimIndex = 0; aimIndex < wandAims.Count; aimIndex++) {
          Vector3 aim = RainChamberState.FoldToUpperHemisphere(
            wandAims[aimIndex]);
          double angle = Math.Acos(Math.Clamp(
            Vector3.Dot(positions[pixelIndex], aim), -1, 1));
          nearestAngle = Math.Min(nearestAngle, angle);
        }
        if (nearestAngle >= radius) {
          continue;
        }
        if (nearestAngle <= innerRadius || feather <= 0) {
          frame.pixels[pixelIndex].Clear();
          continue;
        }
        double retention =
          (nearestAngle - innerRadius) / feather;
        frame.pixels[pixelIndex].Fade(retention, 0);
      }
    }

    private static int ColorEnergy(int color) =>
      ((color >> 16) & 0xFF) +
      ((color >> 8) & 0xFF) +
      (color & 0xFF);
  }

  internal readonly record struct RainDroplet(
    Vector3 Position, Vector3 Velocity, int PaletteIndex, double SizePhase);

  internal readonly record struct RainSplash(
    Vector3 Center, double Age, double Strength, int PaletteIndex);

  // Deterministic bounded simulation state. Particle positions and velocities
  // stay on the unit hemisphere, and all forces are projected into the local
  // tangent plane before integration. That makes gravity read as water running
  // over the dome rather than points falling through its interior.
  internal sealed class RainChamberState {
    private const int MaximumDroplets = 240;
    private const int MaximumSplashes = 48;
    private const double UmbrellaRadius = 0.52;
    private const double RimHeight = 0.025;
    private const double SplashLifetime = 0.9;
    private const double GoldenAngle = 2.399963229728653;
    private const double MinimumSpawnPolar = 0.065;
    private const double WarmSpawnPolarSpread = 1.15;

    private readonly List<RainDroplet> droplets =
      new List<RainDroplet>();
    private readonly List<RainSplash> splashes =
      new List<RainSplash>();
    private readonly double seedPhase;
    private double spawnAccumulator;
    private int nextDropletIndex;

    public IReadOnlyList<RainDroplet> Droplets => this.droplets;
    public IReadOnlyList<RainSplash> Splashes => this.splashes;
    public double Time { get; private set; }

    public RainChamberState(
      int randomSeed = 0, int initialDroplets = 0,
      double initialDropletSize = 0.045
    ) {
      uint mixed = unchecked((uint)randomSeed * 747796405u + 2891336453u);
      this.seedPhase = mixed / (double)uint.MaxValue * 2 * Math.PI;
      int initialCount = Math.Clamp(
        initialDroplets, 0, MaximumDroplets);
      for (int index = 0; index < initialCount; index++) {
        double progress = (index + 1.0) / (initialCount + 1.0);
        this.SpawnDroplet(progress, initialDropletSize);
      }
    }

    public void Step(
      double elapsedSeconds,
      double rainfallRate,
      double gravity,
      double wind,
      double dropletSize,
      double splashStrength,
      double audioLevel,
      IReadOnlyList<Vector3> wandAims,
      int interactionMode = 0,
      IReadOnlyList<Vector3> wandMotions = null
    ) {
      double elapsed = Math.Clamp(elapsedSeconds, 0, 0.1);
      if (elapsed <= 0) {
        return;
      }
      rainfallRate = Math.Max(0, rainfallRate);
      gravity = Math.Max(0, gravity);
      wind = Math.Max(0, wind);
      dropletSize = Math.Max(0, dropletSize);
      splashStrength = Math.Max(0, splashStrength);
      wandAims ??= Array.Empty<Vector3>();
      wandMotions ??= Array.Empty<Vector3>();

      this.AdvanceSplashes(elapsed);
      this.spawnAccumulator += EffectiveRainfallRate(
        rainfallRate, audioLevel) * elapsed;
      while (this.spawnAccumulator >= 1 &&
          this.droplets.Count < MaximumDroplets) {
        this.SpawnDroplet(0, dropletSize);
        this.spawnAccumulator -= 1;
      }
      if (this.droplets.Count >= MaximumDroplets) {
        this.spawnAccumulator = Math.Min(this.spawnAccumulator, 1);
      }

      for (int index = this.droplets.Count - 1; index >= 0; index--) {
        RainDroplet droplet = this.droplets[index];
        if (interactionMode == 1 && IsInsideDryRegion(
            droplet.Position, wandAims, wind)) {
          this.droplets.RemoveAt(index);
          continue;
        }
        Vector3 force = Tangent(
          droplet.Position, -Vector3.UnitZ) * (float)gravity;

        if (interactionMode == 0) {
          for (int aimIndex = 0; aimIndex < wandAims.Count; aimIndex++) {
            Vector3 aim = FoldToUpperHemisphere(wandAims[aimIndex]);
            double angle = Math.Acos(Math.Clamp(
              Vector3.Dot(droplet.Position, aim), -1, 1));
            if (angle >= UmbrellaRadius) {
              continue;
            }
            Vector3 away = NormalizeOrZero(Tangent(
              droplet.Position, droplet.Position - aim));
            if (away.LengthSquared() < 1e-8) {
              away = EastAt(droplet.Position);
            }
            double proximity = 1 - angle / UmbrellaRadius;
            force += away * (float)(wind * 3.2 * proximity * proximity);
          }
        } else if (interactionMode == 2) {
          double windRadius = DryRadius(wind);
          int fieldCount = Math.Min(wandAims.Count, wandMotions.Count);
          for (int aimIndex = 0; aimIndex < fieldCount; aimIndex++) {
            Vector3 motion = wandMotions[aimIndex];
            if (windRadius <= 0 || motion.LengthSquared() < 1e-8) {
              continue;
            }
            Vector3 aim = FoldToUpperHemisphere(wandAims[aimIndex]);
            double angle = Math.Acos(Math.Clamp(
              Vector3.Dot(droplet.Position, aim), -1, 1));
            if (angle >= windRadius) {
              continue;
            }
            Vector3 localMotion = Tangent(droplet.Position, motion);
            double proximity = 1 - angle / windRadius;
            force += localMotion *
              (float)(wind * 3.2 * proximity * proximity);
          }
        }

        Vector3 velocity = Tangent(
          droplet.Position,
          droplet.Velocity + force * (float)elapsed);
        velocity *= (float)Math.Pow(0.72, elapsed);
        double speed = velocity.Length();
        if (speed > 1.8) {
          velocity *= (float)(1.8 / speed);
        }

        Vector3 nextPosition = NormalizeOrZero(
          droplet.Position + velocity * (float)elapsed);
        if (nextPosition.LengthSquared() < 1e-8) {
          nextPosition = droplet.Position;
        }
        if (nextPosition.Z <= RimHeight) {
          this.AddSplash(
            nextPosition, splashStrength, droplet.PaletteIndex,
            pinToRim: true);
          this.droplets.RemoveAt(index);
          continue;
        }
        velocity = Tangent(nextPosition, velocity);
        this.droplets[index] = droplet with {
          Position = nextPosition,
          Velocity = velocity,
        };
      }

      this.ResolveDropletCollisions(dropletSize, splashStrength);

      this.Time += elapsed;
    }

    public void SeedDroplet(
      Vector3 position, Vector3 velocity,
      int paletteIndex = 0, double sizePhase = 0.5
    ) {
      if (this.droplets.Count >= MaximumDroplets) {
        return;
      }
      Vector3 surfacePosition = FoldToUpperHemisphere(position);
      this.droplets.Add(new RainDroplet(
        surfacePosition, Tangent(surfacePosition, velocity),
        paletteIndex & 7, Math.Clamp(sizePhase, 0, 1)));
    }

    private void SpawnDroplet(
      double warmProgress, double dropletSize
    ) {
      int index = this.nextDropletIndex++;
      double angle = this.seedPhase + index * GoldenAngle;
      double progress = Math.Clamp(warmProgress, 0, 1);
      double polar = SpawnPolar(dropletSize) +
        progress * WarmSpawnPolarSpread;
      double sinPolar = Math.Sin(polar);
      Vector3 position = new Vector3(
        (float)(Math.Cos(angle) * sinPolar),
        (float)(Math.Sin(angle) * sinPolar),
        (float)Math.Cos(polar));
      Vector3 downhill = NormalizeOrZero(Tangent(
        position, -Vector3.UnitZ));
      Vector3 velocity = downhill * (float)(0.08 + progress * 0.46);
      double sizePhase =
        (index * 0.6180339887498949 + this.seedPhase / (2 * Math.PI)) % 1;
      this.droplets.Add(new RainDroplet(
        position, velocity, index & 7, sizePhase));
    }

    internal static double SpawnPolar(double dropletSize) {
      // The largest rendered radius is 1.10 times Droplet Size. Keeping the
      // spawn ring at least one maximum droplet diameter from the crown makes
      // its circumference grow with the collision reach instead of packing
      // larger droplets into the same small center ring.
      double maximumDropletDiameter = Math.Max(0, dropletSize) * 2.2;
      double maximumSpawnPolar =
        Math.Acos(RimHeight) - WarmSpawnPolarSpread;
      return Math.Clamp(
        maximumDropletDiameter, MinimumSpawnPolar, maximumSpawnPolar);
    }

    private void AddSplash(
      Vector3 impact, double strength, int paletteIndex, bool pinToRim
    ) {
      if (strength <= 0) {
        return;
      }
      Vector3 center = pinToRim
        ? NormalizeOrZero(new Vector3(impact.X, impact.Y, 0))
        : FoldToUpperHemisphere(impact);
      if (center.LengthSquared() < 1e-8) {
        return;
      }
      if (this.splashes.Count >= MaximumSplashes) {
        this.splashes.RemoveAt(0);
      }
      this.splashes.Add(new RainSplash(
        center, 0, strength, paletteIndex & 7));
    }

    private void ResolveDropletCollisions(
      double dropletSize, double splashStrength
    ) {
      if (dropletSize <= 0 || splashStrength <= 0 ||
          this.droplets.Count < 2) {
        return;
      }

      // Resolve at most one pair for each surviving first droplet during this
      // step. Coalescing the pair makes the event self-clearing: a stationary
      // overlap cannot emit another ring on the following frame.
      for (int firstIndex = 0;
          firstIndex < this.droplets.Count - 1; firstIndex++) {
        RainDroplet first = this.droplets[firstIndex];
        for (int secondIndex = firstIndex + 1;
            secondIndex < this.droplets.Count; secondIndex++) {
          RainDroplet second = this.droplets[secondIndex];
          double collisionRadius = CollisionRadius(
            dropletSize, first.SizePhase, second.SizePhase);
          if (Vector3.Dot(first.Position, second.Position) <
              Math.Cos(collisionRadius)) {
            continue;
          }

          Vector3 center = NormalizeOrZero(
            first.Position + second.Position);
          if (center.LengthSquared() < 1e-8) {
            center = first.Position;
          }
          Vector3 velocity = Tangent(
            center, (first.Velocity + second.Velocity) * 0.5f);
          RainDroplet paletteSource =
            first.Velocity.LengthSquared() >= second.Velocity.LengthSquared()
              ? first
              : second;
          double mergedPhase = Math.Min(
            1, Math.Max(first.SizePhase, second.SizePhase) + 0.08);
          this.droplets[firstIndex] = new RainDroplet(
            center, velocity, paletteSource.PaletteIndex, mergedPhase);
          this.droplets.RemoveAt(secondIndex);

          double relativeSpeed = (first.Velocity - second.Velocity).Length();
          double collisionStrength = splashStrength * (
            0.55 + 0.25 * Math.Min(relativeSpeed / 1.8, 1));
          this.AddSplash(
            center, collisionStrength, paletteSource.PaletteIndex,
            pinToRim: false);
          break;
        }
      }
    }

    private void AdvanceSplashes(double elapsed) {
      for (int index = this.splashes.Count - 1; index >= 0; index--) {
        RainSplash splash = this.splashes[index];
        double age = splash.Age + elapsed;
        if (age >= SplashLifetime) {
          this.splashes.RemoveAt(index);
        } else {
          this.splashes[index] = splash with { Age = age };
        }
      }
    }

    internal static double EffectiveRainfallRate(
      double rainfallRate, double audioLevel
    ) => Math.Max(0, rainfallRate) * (
      0.15 + 0.85 * Math.Sqrt(Math.Clamp(audioLevel, 0, 1)));

    internal static double DryRadius(double wandStrength) =>
      UmbrellaRadius * Math.Sqrt(
        Math.Clamp(wandStrength, 0, 4) / 4);

    private static bool IsInsideDryRegion(
      Vector3 position,
      IReadOnlyList<Vector3> wandAims,
      double wandStrength
    ) {
      double radius = DryRadius(wandStrength);
      if (radius <= 0) {
        return false;
      }
      double minimumAlignment = Math.Cos(radius);
      for (int aimIndex = 0; aimIndex < wandAims.Count; aimIndex++) {
        Vector3 aim = FoldToUpperHemisphere(wandAims[aimIndex]);
        if (Vector3.Dot(position, aim) > minimumAlignment) {
          return true;
        }
      }
      return false;
    }

    internal static double CollisionRadius(
      double dropletSize, double firstSizePhase, double secondSizePhase
    ) {
      double firstRadius = Math.Max(0, dropletSize) * (
        0.82 + 0.28 * Math.Clamp(firstSizePhase, 0, 1));
      double secondRadius = Math.Max(0, dropletSize) * (
        0.82 + 0.28 * Math.Clamp(secondSizePhase, 0, 1));
      return Math.Min(firstRadius + secondRadius, Math.PI / 2);
    }

    internal static double SplashRadius(double age) =>
      0.035 + Math.Max(0, age) * 0.78;

    internal static double SplashEnvelope(double age, double strength) {
      double remaining = 1 - Math.Max(0, age) / SplashLifetime;
      return Math.Clamp(strength * remaining * remaining, 0, 1);
    }

    internal static Vector3 FoldToUpperHemisphere(Vector3 direction) {
      Vector3 normalized = NormalizeOrZero(direction);
      if (normalized.LengthSquared() < 1e-8) {
        return Vector3.UnitZ;
      }
      return normalized.Z < 0
        ? Vector3.Normalize(new Vector3(
          normalized.X, normalized.Y, -normalized.Z))
        : normalized;
    }

    private static Vector3 EastAt(Vector3 position) {
      Vector3 east = Vector3.Cross(Vector3.UnitZ, position);
      if (east.LengthSquared() < 1e-8) {
        east = Vector3.Cross(Vector3.UnitX, position);
      }
      return NormalizeOrZero(east);
    }

    internal static Vector3 Tangent(Vector3 position, Vector3 value) =>
      value - position * Vector3.Dot(position, value);

    internal static Vector3 NormalizeOrZero(Vector3 value) =>
      value.LengthSquared() > 1e-12 ? Vector3.Normalize(value) : Vector3.Zero;
  }
}
