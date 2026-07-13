using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Spectrum {

  // The reverse of Shooting Star: a trigger launches a particle from the
  // current wand/idle aim point in a random direction and the global dome fade
  // turns its successive positions into a streak.
  class LEDDomeSparklerVisualizer : DomeLayerVisualizer {

    private struct Particle {
      public Vector2 pos;
      public Vector2 vel;
      public int hueIndex;
    }

    private const int MAX_PARTICLES = 64;
    private const double CULL_RADIUS = 1.25;

    private readonly DomeLayerEnvironment environment;
    private readonly LayerRendererRuntime runtime;
    private readonly AudioInput audio;
    private readonly OrientationInput orientationInput;
    private readonly LEDDomeOutput dome;
    private readonly DomeFrame buffer;
    private readonly OrientationCenter center;
    private readonly LayerTrigger trigger;
    private readonly FrameClock frameClock = new FrameClock();
    private readonly Random rand = new Random();
    private readonly List<Particle> particles = new List<Particle>();
    private int lastClearCounter = -1;

    public LEDDomeSparklerVisualizer(
      DomeLayerEnvironment environment,
      LayerRendererRuntime runtime,
      AudioInput audio,
      OrientationInput orientationInput,
      OrientationCenter center,
      BeatBroadcaster beat,
      LEDDomeOutput dome
    ) {
      this.environment = environment;
      this.runtime = runtime;
      this.audio = audio;
      this.orientationInput = orientationInput;
      this.center = center;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeFrame();
      this.trigger = new LayerTrigger(
        environment, orientationInput, runtime.InstanceId, beat, audio);
    }

    public int Priority => 2;

    public string LayerKey => "sparkler";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      double frameScale = this.frameClock.Tick();
      double dt = frameScale * 0.0025;
      double level = this.audio.Volume;
      SparklerLayerOptions options =
        this.runtime.GetOptions<SparklerLayerOptions>();
      double speed = options.Speed;
      double size = options.Size;
      int triggerSource = options.Trigger;
      int button = options.Button;
      double levelThreshold = options.Level;
      double interval = options.Interval;
      int paletteBank = options.Palette;

      double frameRetention =
        1 - Math.Pow(5, -this.environment.GlobalFadeSpeed);
      this.buffer.Fade(Math.Pow(frameRetention, frameScale), 0);

      if (this.ClearRequested()) {
        this.particles.Clear();
        for (int i = 0; i < this.buffer.pixels.Length; i++) {
          this.buffer.pixels[i].Clear();
        }
      }

      this.center.Update(level);
      if (this.trigger.Fired(
        button, triggerSource, levelThreshold, interval
      )) {
        this.SpawnParticle(this.AimPoint(), speed);
      }

      this.Advance(dt);
      this.RenderPixels(size, paletteBank);
    }

    private Vector2 AimPoint() {
      Vector3 aimSphere = Vector3.Transform(
        OrientationCenter.Spot,
        Quaternion.Conjugate(this.center.CurrentCenter)
      );
      // The orientation represents an axis. Of its two antipodal endpoints,
      // launch from the one on the dome's positive-Z (top) hemisphere.
      if (aimSphere.Z < 0) {
        aimSphere = -aimSphere;
      }
      return new Vector2(aimSphere.X, -aimSphere.Y);
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
      });
    }

    private void Advance(double dt) {
      for (int i = this.particles.Count - 1; i >= 0; i--) {
        Particle particle = this.particles[i];
        particle.pos += particle.vel * (float)dt;
        if (particle.pos.Length() > CULL_RADIUS) {
          this.particles.RemoveAt(i);
        } else {
          this.particles[i] = particle;
        }
      }
    }

    private void RenderPixels(double size, int bank) {
      if (this.particles.Count == 0) {
        return;
      }
      double sizeSq = size * size;
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        ref var pixel = ref this.buffer.pixels[i];
        DomeTopologyPixel point = this.buffer.Topology.PixelAt(i);
        double u = 2 * point.X - 1;
        double v = 2 * point.Y - 1;
        double bestValue = 0;
        int bestColor = 0;
        for (int j = 0; j < this.particles.Count; j++) {
          Particle particle = this.particles[j];
          double du = u - particle.pos.X;
          double dv = v - particle.pos.Y;
          double dSq = du * du + dv * dv;
          if (dSq >= sizeSq) {
            continue;
          }
          double value = 1 - Math.Sqrt(dSq) / size;
          if (value > bestValue) {
            bestValue = value;
            bestColor = this.dome.GetGradientColor(
              particle.hueIndex, value, 0, true, bank);
          }
        }
        if (bestValue > 0) {
          pixel.color = bestColor;
        }
      }
    }
  }

}
