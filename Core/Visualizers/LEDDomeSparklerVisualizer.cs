using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;

namespace Spectrum {

  // The reverse of Shooting Star: particles launch continuously from the
  // current wand/idle aim point in random directions, with triggers launching
  // an extra spark. The global dome fade turns successive positions into trails.
  class LEDDomeSparklerVisualizer : DomeLayerVisualizer {

    private struct Particle {
      public Vector2 pos;
      public Vector2 vel;
      public int hueIndex;
      public double remainingLife;
    }

    private const int MAX_PARTICLES = 64;
    private const double CULL_RADIUS = 1.25;
    // Sparks burn out after travelling this far in centered dome coordinates.
    // Tying evaporation to distance keeps the visible flight length stable when
    // the Speed control changes, while the buffer fade retains a short afterglow.
    private const double EVAPORATION_DISTANCE = 1.0;

    private readonly DomeLayerEnvironment environment;
    private readonly LayerRendererRuntime runtime;
    private readonly IAudioLevelInput audio;
    private readonly OrientationInput orientationInput;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private readonly ImmutableArray<Vector2> projectedPixels;
    private readonly OrientationCenter center;
    private readonly LayerTrigger trigger;
    private readonly FrameClock frameClock = new FrameClock();
    private readonly Random rand = new Random();
    private readonly List<Particle> particles = new List<Particle>();
    private double emissionAccumulator;
    private int lastClearCounter = -1;

    public LEDDomeSparklerVisualizer(
      DomeLayerEnvironment environment,
      LayerRendererRuntime runtime,
      IAudioLevelInput audio,
      OrientationInput orientationInput,
      OrientationCenter center,
      BeatBroadcaster beat,
      DomeRenderContext dome
    ) {
      this.environment = environment;
      this.runtime = runtime;
      this.audio = audio;
      this.orientationInput = orientationInput;
      this.center = center;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
      this.projectedPixels =
        DomeSurfaceGeometry.ProjectNormalsToStrip(this.buffer.Normals);
      this.trigger = new LayerTrigger(
        environment, orientationInput, runtime.InstanceId, beat, audio);
    }

    public int Priority => 2;

    public string LayerKey => "sparkler";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[]? inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      double frameScale = this.frameClock.Tick();
      double dt = frameScale * 0.0025;
      double level = this.audio.Volume;
      SparklerLayerOptions options =
        this.runtime.GetOptions<SparklerLayerOptions>();
      double emissionRate = options.EmissionRate;
      double speed = options.Speed;
      double size = options.Size;
      int triggerSource = options.Trigger;
      int button = options.Button;
      double levelThreshold = options.Level;
      double interval = options.Interval;
      int selectedPalette = options.Palette;

      double frameRetention =
        1 - Math.Pow(5, -this.environment.GlobalFadeSpeed);
      this.buffer.Fade(Math.Pow(frameRetention, frameScale), 0);

      if (this.ClearRequested()) {
        this.particles.Clear();
        this.emissionAccumulator = 0;
        for (int i = 0; i < this.buffer.pixels.Length; i++) {
          this.buffer.pixels[i].Clear();
        }
      }

      this.center.Update(level);
      Vector2 aim = this.AimPoint();
      if (this.trigger.Fired(
        button, triggerSource, levelThreshold, interval
      )) {
        this.SpawnParticle(aim, speed);
      }
      this.EmitParticles(emissionRate, dt, aim, speed);

      this.Advance(dt);
      this.RenderPixels(size, selectedPalette);
    }

    private Vector2 AimPoint() {
      Vector3 aimSphere = Vector3.Transform(
        OrientationCenter.Spot,
        Quaternion.Conjugate(this.center.CurrentCenter)
      );
      return StrutLayoutFactory.ProjectSphereToStrip(
        aimSphere, foldAxisToUpperHemisphere: true);
    }

    private bool ClearRequested() {
      int counter = this.environment.ClearGeneration(this.runtime.InstanceId);
      if (this.lastClearCounter == -1) {
        this.lastClearCounter = counter;
        return false;
      }
      bool cleared = counter != this.lastClearCounter;
      this.lastClearCounter = counter;
      return cleared;
    }

    private void EmitParticles(
      double emissionRate, double dt, Vector2 origin, double speed
    ) {
      this.emissionAccumulator += emissionRate * dt;
      while (this.emissionAccumulator >= 1) {
        this.emissionAccumulator -= 1;
        this.SpawnParticle(origin, speed);
      }
    }

    private void SpawnParticle(Vector2 origin, double speed) {
      if (this.particles.Count >= MAX_PARTICLES) {
        return;
      }
      double theta = this.rand.NextDouble() * 2 * Math.PI;
      Vector2 direction = new Vector2(
        (float)Math.Cos(theta), (float)Math.Sin(theta));
      this.particles.Add(new Particle {
        pos = origin,
        vel = direction * (float)speed,
        hueIndex = this.rand.Next(8),
        remainingLife = 1,
      });
    }

    private void Advance(double dt) {
      for (int i = this.particles.Count - 1; i >= 0; i--) {
        Particle particle = this.particles[i];
        Vector2 step = particle.vel * (float)dt;
        particle.pos += step;
        particle.remainingLife -= step.Length() / EVAPORATION_DISTANCE;
        if (
          particle.remainingLife <= 0 ||
          particle.pos.Length() > CULL_RADIUS
        ) {
          this.particles.RemoveAt(i);
        } else {
          this.particles[i] = particle;
        }
      }
    }

    private void RenderPixels(double size, int selectedPalette) {
      if (this.particles.Count == 0) {
        return;
      }
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        ref var pixel = ref this.buffer.pixels[i];
        Vector2 point = this.projectedPixels[i];
        double u = point.X;
        double v = point.Y;
        double bestValue = 0;
        int bestColor = 0;
        for (int j = 0; j < this.particles.Count; j++) {
          Particle particle = this.particles[j];
          // Shrink and dim together as the spark exhausts its travel budget.
          double particleSize = size * particle.remainingLife;
          double particleSizeSq = particleSize * particleSize;
          double du = u - particle.pos.X;
          double dv = v - particle.pos.Y;
          double dSq = du * du + dv * dv;
          if (dSq >= particleSizeSq) {
            continue;
          }
          double radialValue = 1 - Math.Sqrt(dSq) / particleSize;
          double strength = radialValue * particle.remainingLife;
          if (strength > bestValue) {
            bestValue = strength;
            int color = this.dome.GetGradientColor(
              particle.hueIndex, radialValue, 0, true, selectedPalette);
            bestColor = LEDColor.ScaleColor(color, strength);
          }
        }
        if (bestValue > 0) {
          pixel.color = bestColor;
          pixel.SetAlpha(bestValue);
        }
      }
    }
  }

}
