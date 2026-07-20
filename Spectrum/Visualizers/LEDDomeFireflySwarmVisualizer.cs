using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;

namespace Spectrum.Visualizers {

  // A persistent flock of luminous agents moving over the true dome surface.
  // The agents share local heading and cohesion, push apart at close range,
  // and receive deterministic tangent wander so their collective silhouette
  // remains alive without devolving into independent random particles. Moving
  // wand axes attract or repel the flock. A sharp audio rise startles every
  // agent away from the flock centroid, after which cohesion regroups it.
  class LEDDomeFireflySwarmVisualizer : DomeLayerVisualizer {
    private readonly LayerRendererRuntime runtime;
    private readonly AudioInput audio;
    private readonly OrientationInput orientationInput;
    private readonly LEDDomeOutput dome;
    private readonly DomeFrame buffer;
    private readonly ImmutableArray<Vector3> pixelPositions;
    private readonly FireflySwarmState swarm;
    private readonly FireflyStartleDetector transientDetector =
      new FireflyStartleDetector();
    private readonly Stopwatch frameTimer = new Stopwatch();
    private readonly List<Vector3> wandAims = new List<Vector3>();

    public LEDDomeFireflySwarmVisualizer(
      LayerRendererRuntime runtime,
      AudioInput audio,
      OrientationInput orientationInput,
      LEDDomeOutput dome
    ) {
      this.runtime = runtime;
      this.audio = audio;
      this.orientationInput = orientationInput;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeFrame();
      this.pixelPositions = this.buffer.BakePixelPositions();
      int population = this.runtime
        .GetOptions<FireflySwarmLayerOptions>().Population;
      this.swarm = new FireflySwarmState(population, 0x464C59);
    }

    public int Priority => 2;
    public string LayerKey => "firefly-swarm";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() =>
      this.inputs ?? (this.inputs = new Input[] {
        this.audio, this.orientationInput,
      });

    public void Visualize() {
      FireflySwarmLayerOptions options =
        this.runtime.GetOptions<FireflySwarmLayerOptions>();
      double elapsed = this.ElapsedSeconds();

      this.swarm.Resize(options.Population);
      this.CollectWandAims();
      if (this.transientDetector.Sample(this.audio.Volume, elapsed)) {
        this.swarm.Startle();
      }
      this.swarm.Step(
        elapsed, options.Cohesion, options.Separation, options.Wander,
        options.InteractionMode, this.wandAims);

      this.buffer.Fade(TrailRetention(options.TrailLength, elapsed), 0);
      this.PaintSwarm(options.DotSize, options.Palette);
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

    private void CollectWandAims() {
      this.wandAims.Clear();
      IReadOnlyDictionary<int, OrientationDevice> devices =
        this.orientationInput.OperatorFrameDevices;
      foreach (var kvp in devices) {
        if (!kvp.Value.isMoving) {
          continue;
        }
        Vector3 aim = Vector3.Transform(
          OrientationCenter.Spot,
          Quaternion.Conjugate(kvp.Value.currentRotation()));
        this.wandAims.Add(FireflySwarmState.FoldToUpperHemisphere(aim));
      }
    }

    private void PaintSwarm(double dotSize, int selectedPalette) {
      IReadOnlyList<FireflyAgent> agents = this.swarm.Agents;
      double cosRadius = Math.Cos(dotSize);
      double radiusSpan = Math.Max(1 - cosRadius, 1e-8);
      for (int pixelIndex = 0;
          pixelIndex < this.buffer.pixels.Length; pixelIndex++) {
        Vector3 pixelPosition = this.pixelPositions[pixelIndex];
        double bestStrength = 0;
        int bestPaletteIndex = 0;
        for (int agentIndex = 0; agentIndex < agents.Count; agentIndex++) {
          FireflyAgent agent = agents[agentIndex];
          double alignment = Vector3.Dot(pixelPosition, agent.Position);
          if (alignment <= cosRadius) {
            continue;
          }
          double radial = (alignment - cosRadius) / radiusSpan;
          double pulse = 0.62 + 0.38 * Math.Sin(
            2 * Math.PI * (this.swarm.Time * 0.85 + agent.Phase));
          double strength = radial * Math.Clamp(pulse, 0.24, 1);
          if (strength > bestStrength) {
            bestStrength = strength;
            bestPaletteIndex = agent.PaletteIndex;
          }
        }
        if (bestStrength <= 0) {
          continue;
        }

        int tint = this.dome.GetGradientColor(
          bestPaletteIndex, bestStrength, 0, true, selectedPalette);
        int color = LEDColor.ScaleColor(tint, bestStrength);
        ref LEDDomeOutputPixel pixel =
          ref this.buffer.pixels[pixelIndex];
        if (ColorEnergy(color) >= ColorEnergy(pixel.color)) {
          pixel.color = color;
          pixel.SetAlpha(bestStrength);
          pixel.hue = (bestPaletteIndex & 7) / 8.0;
        }
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

  internal readonly record struct FireflyAgent(
    Vector3 Position, Vector3 Velocity, int PaletteIndex, double Phase);

  // Deterministic, allocation-stable boid state. Forces are calculated for
  // every agent before any position is advanced, so iteration order cannot
  // leak partially updated neighbors into the same simulation step.
  internal sealed class FireflySwarmState {
    private const double NeighborRadius = 0.7;
    private const double SeparationRadius = 0.14;
    private const double AlignmentStrength = 0.75;
    private const double InteractionStrength = 2.4;
    private const double StartleStrength = 6.5;
    private const double BaseMaximumSpeed = 0.75;
    private const double StartleMaximumSpeed = 1.35;
    private const double VelocityDamping = 0.65;
    private const double StartleHalfLife = 0.32;
    private const double GoldenAngle = 2.399963229728653;

    private FireflyAgent[] agents = Array.Empty<FireflyAgent>();
    private Vector3[] forces = Array.Empty<Vector3>();
    private readonly double seedPhase;
    private double startleEnergy;

    public IReadOnlyList<FireflyAgent> Agents => this.agents;
    public double Time { get; private set; }
    public double StartleEnergy => this.startleEnergy;

    public FireflySwarmState(int population, int randomSeed = 0) {
      uint mixed = unchecked((uint)randomSeed * 747796405u + 2891336453u);
      this.seedPhase = mixed / (double)uint.MaxValue * 2 * Math.PI;
      this.Resize(population);
    }

    public void Resize(int population) {
      population = Math.Max(1, population);
      if (population == this.agents.Length) {
        return;
      }

      var resized = new FireflyAgent[population];
      int retained = Math.Min(population, this.agents.Length);
      Array.Copy(this.agents, resized, retained);
      for (int index = retained; index < population; index++) {
        resized[index] = this.CreateAgent(index, population);
      }
      this.agents = resized;
      this.forces = new Vector3[population];
    }

    private FireflyAgent CreateAgent(int index, int population) {
      Vector3 center = Vector3.Normalize(new Vector3(0.18f, -0.12f, 0.97f));
      Vector3 east = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, center));
      Vector3 north = Vector3.Normalize(Vector3.Cross(center, east));
      double radial = 0.04 + 0.24 * Math.Sqrt((index + 0.5) / population);
      double angle = this.seedPhase + index * GoldenAngle;
      Vector3 offset = east * (float)(Math.Cos(angle) * radial) +
        north * (float)(Math.Sin(angle) * radial);
      Vector3 position = FoldToUpperHemisphere(
        Vector3.Normalize(center + offset));
      Vector3 velocity = Tangent(position,
        east * (float)(-Math.Sin(angle)) +
        north * (float)Math.Cos(angle));
      velocity = NormalizeOrZero(velocity) * 0.08f;
      return new FireflyAgent(
        position, velocity, index & 7,
        (index * 0.6180339887498949 + this.seedPhase / (2 * Math.PI)) % 1);
    }

    public void Startle() {
      this.startleEnergy = 1;
    }

    public void Step(
      double elapsedSeconds,
      double cohesion,
      double separation,
      double wander,
      int interactionMode,
      IReadOnlyList<Vector3> wandAims
    ) {
      double elapsed = Math.Clamp(elapsedSeconds, 0, 0.1);
      if (elapsed <= 0 || this.agents.Length == 0) {
        return;
      }
      cohesion = Math.Max(0, cohesion);
      separation = Math.Max(0, separation);
      wander = Math.Max(0, wander);
      wandAims ??= Array.Empty<Vector3>();

      Vector3 centroid = this.Centroid();
      double neighborDistanceSq =
        2 - 2 * Math.Cos(NeighborRadius);
      double separationDistanceSq =
        2 - 2 * Math.Cos(SeparationRadius);

      for (int index = 0; index < this.agents.Length; index++) {
        FireflyAgent agent = this.agents[index];
        Vector3 velocitySum = Vector3.Zero;
        Vector3 separationForce = Vector3.Zero;
        int neighbors = 0;

        for (int otherIndex = 0;
            otherIndex < this.agents.Length; otherIndex++) {
          if (otherIndex == index) {
            continue;
          }
          FireflyAgent other = this.agents[otherIndex];
          double distanceSq = Vector3.DistanceSquared(
            agent.Position, other.Position);
          if (distanceSq <= neighborDistanceSq) {
            velocitySum += other.Velocity;
            neighbors++;
          }
          if (distanceSq > 1e-10 &&
              distanceSq < separationDistanceSq) {
            double distance = Math.Sqrt(distanceSq);
            Vector3 away = NormalizeOrZero(Tangent(
              agent.Position, agent.Position - other.Position));
            double weight = 1 - distance /
              Math.Sqrt(separationDistanceSq);
            separationForce += away * (float)weight;
          }
        }

        Vector3 force = Vector3.Zero;
        // Cohesion retains a group-centroid pull even after a startle opens
        // gaps wider than NeighborRadius; otherwise disconnected sub-flocks
        // have no information that lets them reform. Heading alignment stays
        // local so the regrouped swarm still turns organically.
        force += NormalizeOrZero(Tangent(
          agent.Position, centroid - agent.Position)) *
          (float)cohesion;
        if (neighbors > 0) {
          Vector3 meanVelocity = velocitySum / neighbors;
          force += Tangent(
            agent.Position, meanVelocity - agent.Velocity) *
            (float)AlignmentStrength;
        }
        force += separationForce * (float)separation;

        Vector3 wanderDirection = this.WanderDirection(index, agent.Position);
        force += wanderDirection * (float)wander;

        for (int aimIndex = 0; aimIndex < wandAims.Count; aimIndex++) {
          Vector3 aim = FoldToUpperHemisphere(wandAims[aimIndex]);
          Vector3 toward = NormalizeOrZero(Tangent(
            agent.Position, aim - agent.Position));
          double angle = Math.Acos(Math.Clamp(
            Vector3.Dot(agent.Position, aim), -1, 1));
          double weight = 0.45 + 0.55 / (0.25 + angle);
          double direction = interactionMode == 1 ? -1 : 1;
          force += toward * (float)(
            direction * InteractionStrength * Math.Min(weight, 2.5));
        }

        if (this.startleEnergy > 0.001) {
          Vector3 outward = NormalizeOrZero(Tangent(
            agent.Position, agent.Position - centroid));
          if (outward.LengthSquared() < 1e-8) {
            outward = wanderDirection;
          }
          force += outward * (float)(
            StartleStrength * this.startleEnergy);
        }
        this.forces[index] = Tangent(agent.Position, force);
      }

      double maximumSpeed = BaseMaximumSpeed +
        StartleMaximumSpeed * this.startleEnergy;
      double damping = Math.Pow(VelocityDamping, elapsed);
      for (int index = 0; index < this.agents.Length; index++) {
        FireflyAgent agent = this.agents[index];
        Vector3 velocity = Tangent(
          agent.Position,
          agent.Velocity + this.forces[index] * (float)elapsed);
        velocity *= (float)damping;
        double speed = velocity.Length();
        if (speed > maximumSpeed) {
          velocity *= (float)(maximumSpeed / speed);
        }

        Vector3 position = Vector3.Normalize(
          agent.Position + velocity * (float)elapsed);
        if (position.Z < 0) {
          position = ReflectAcrossRim(position);
          velocity = ReflectAcrossRim(velocity);
        }
        position = Vector3.Normalize(position);
        velocity = Tangent(position, velocity);
        this.agents[index] = agent with {
          Position = position,
          Velocity = velocity,
        };
      }

      this.Time += elapsed;
      this.startleEnergy *= Math.Pow(
        0.5, elapsed / StartleHalfLife);
    }

    public double MeanAngularSpread() {
      Vector3 center = this.Centroid();
      double total = 0;
      for (int index = 0; index < this.agents.Length; index++) {
        total += Math.Acos(Math.Clamp(
          Vector3.Dot(center, this.agents[index].Position), -1, 1));
      }
      return total / this.agents.Length;
    }

    private Vector3 Centroid() {
      Vector3 sum = Vector3.Zero;
      for (int index = 0; index < this.agents.Length; index++) {
        sum += this.agents[index].Position;
      }
      Vector3 center = NormalizeOrZero(sum);
      return center.LengthSquared() > 1e-8 ? center : Vector3.UnitZ;
    }

    private Vector3 WanderDirection(int index, Vector3 position) {
      Vector3 east = Vector3.Cross(Vector3.UnitZ, position);
      if (east.LengthSquared() < 1e-8) {
        east = Vector3.Cross(Vector3.UnitX, position);
      }
      east = Vector3.Normalize(east);
      Vector3 north = Vector3.Normalize(Vector3.Cross(position, east));
      double angle = this.seedPhase + index * GoldenAngle +
        this.Time * (1.15 + (index % 5) * 0.09);
      return east * (float)Math.Cos(angle) +
        north * (float)Math.Sin(angle);
    }

    internal static Vector3 FoldToUpperHemisphere(Vector3 direction) {
      Vector3 normalized = NormalizeOrZero(direction);
      if (normalized.LengthSquared() < 1e-8) {
        return Vector3.UnitZ;
      }
      return normalized.Z < 0 ? -normalized : normalized;
    }

    private static Vector3 Tangent(Vector3 position, Vector3 value) =>
      value - position * Vector3.Dot(position, value);

    private static Vector3 NormalizeOrZero(Vector3 value) =>
      value.LengthSquared() > 1e-12 ? Vector3.Normalize(value) : Vector3.Zero;

    private static Vector3 ReflectAcrossRim(Vector3 value) =>
      new Vector3(value.X, value.Y, -value.Z);
  }

  // Detects a startle-worthy onset against a slow recent-level envelope. The
  // cooldown and rise comparison mean a sustained loud passage fires once;
  // the envelope must settle before a later onset can startle the flock again.
  internal sealed class FireflyStartleDetector {
    private const double MinimumLevel = 0.28;
    private const double MinimumRise = 0.16;
    private const double CooldownSeconds = 0.45;
    private double envelope;
    private double cooldown;
    private bool initialized;

    public double Envelope => this.envelope;

    public bool Sample(double level, double elapsedSeconds) {
      level = Math.Clamp(level, 0, 1);
      double elapsed = Math.Clamp(elapsedSeconds, 0, 0.1);
      this.cooldown = Math.Max(0, this.cooldown - elapsed);
      if (!this.initialized) {
        this.initialized = true;
        this.envelope = level;
        return false;
      }

      bool fired = this.cooldown <= 0 &&
        level >= MinimumLevel && level - this.envelope >= MinimumRise;
      if (fired) {
        this.cooldown = CooldownSeconds;
      }

      double timeConstant = level > this.envelope ? 0.18 : 0.65;
      double response = elapsed <= 0
        ? 0 : 1 - Math.Exp(-elapsed / timeConstant);
      this.envelope += (level - this.envelope) * response;
      return fired;
    }
  }
}
