using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;

namespace Spectrum {

  // SKETCH — a "shooting star" stack layer.
  //
  // Spawns dots just *outside* the dome's rim and accelerates them inward
  // toward a wand's aim point, leaving a fading streak (the trail comes from
  // the per-frame Fade + redraw, exactly as Splat/Metaball build their trails).
  // When a dot reaches the aim point it's counted as a "hit" and despawned
  // (a burst on arrival is a natural extension — see the arrival block).
  //
  // Everything runs in a centered azimuthal-equidistant frame (u, v), derived
  // by projecting each topology normal through ProjectSphereToStrip. The dome
  // disc is u² + v² <= 1 and "off the dome" is simply radius > 1. Unlike the
  // legacy straight-line strip coordinates used by Splat/Wave, this effect map
  // is the exact inverse of the sphere projection used for the wand target, so
  // a target and the corresponding physical pixel cannot drift apart.
  //
  // Orientation plumbing (OrientationInput + the shared OrientationCenter) is
  // wired exactly like Metaball/Ripple: the shared center gives us a valid aim
  // target every frame, including the idle-drift dummy pointer when no wand is
  // moving — so stars still rain toward the wandering idle point with no wand
  // connected, matching the other orientation layers' idle behavior.
  class LEDDomeShootingStarVisualizer : DomeLayerVisualizer {

    // One in-flight dot. Position/velocity live in centered (u, v) units;
    // hueIndex picks its gradient so a volley reads as multi-colored.
    private struct Star {
      public Vector2 pos;     // centered (u, v)
      public Vector2 vel;     // units / second
      public int hueIndex;    // gradient index passed to GetGradientColor
      // Aim point captured at spawn. Only used when homing is off (ballistic);
      // when homing is on we re-read the live wand aim every frame instead.
      public Vector2 pinnedTarget;
    }

    // Hard cap so a runaway spawnRate can't grow the list without bound; the
    // per-frame cost is pixels * activeStars, and dome pixels number ~4500.
    private const int MAX_STARS = 24;
    // A star within this distance (in u,v units) of its target counts as a hit.
    private const double HIT_RADIUS = 0.04;
    // Where off-rim stars are born: radius drawn from [MIN, MAX] beyond the disc.
    private const double SPAWN_RADIUS_MIN = 1.15;
    private const double SPAWN_RADIUS_MAX = 1.6;

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

    private readonly List<Star> stars = new List<Star>();
    // Fractional star accumulator: spawnRate is stars/sec, so we accrue
    // spawnRate * elapsed each frame and spawn on each whole crossing. Keeps the
    // spawn cadence frame-rate independent (same trick Wave uses for its phase).
    private double spawnAccumulator;

    // Last-seen manual-clear counter (keyed by the stable layer instance ID),
    // edge-detected exactly like LayerTrigger's manual fire. -1 = not yet
    // baselined, so a counter already bumped in a saved config doesn't clear the
    // instant the layer is (re)constructed.
    private int lastClearCounter = -1;

    public LEDDomeShootingStarVisualizer(
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
      // Beat + Audio passed so all four trigger sources are live, like Ripple.
      this.trigger = new LayerTrigger(
        environment, orientationInput, runtime.InstanceId, beat, audio);
    }

    public int Priority => 2;

    public string LayerKey => "shooting-star";
    public DomeFrame LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      double frameScale = this.frameClock.Tick();
      // FrameClock.Tick() is in "nominal frames"; convert to seconds so the
      // physics params read in intuitive units/sec. ~2.5ms per engine tick at
      // the 400Hz cap (see Operator's ThrottleFrame), matching FrameClock's base.
      double dt = frameScale * 0.0025;
      double level = this.audio.Volume;

      ShootingStarLayerOptions options =
        this.runtime.GetOptions<ShootingStarLayerOptions>();
      double spawnRate = options.SpawnRate;
      double accel = options.Acceleration;
      double maxSpeed = options.MaxSpeed;
      double size = options.Size;
      bool homing = options.Homing;
      int triggerSource = options.Trigger;
      int button = options.Button;
      double levelThreshold = options.Level;
      double interval = options.Interval;
      int selectedPalette = options.Palette;

      // Trails come from the global dome fade, the
      // same per-frame retention Metaball/Ripple use — no per-layer trail knob.
      double frameRetention =
        1 - Math.Pow(5, -this.environment.GlobalFadeSpeed);
      this.buffer.Fade(Math.Pow(frameRetention, frameScale), 0);

      // Clear button (native VJ HUD 🧹): drop every live star and blank the
      // buffer immediately — an escape hatch when too many stars are dragging the
      // frame rate. Spawning resumes this same frame from spawnRate/the trigger,
      // so a held-down Clear keeps the field near-empty rather than freezing it.
      if (this.ClearRequested()) {
        this.stars.Clear();
        this.spawnAccumulator = 0;
        for (int i = 0; i < this.buffer.pixels.Length; i++) {
          this.buffer.pixels[i].Clear();
        }
      }

      // Refresh the shared orientation center once (idle-drift + spotlight), so
      // AimPoint() reads a current target. Loudness scales the idle drift, as
      // the other orientation layers pass it.
      this.center.Update(level);
      Vector2 aim = this.AimPoint();

      // A trigger fire (wand button / beat / audio / manual Fire button) launches
      // one extra star on top of the steady spawn rate. Fired() runs every frame
      // so a button edge is never missed.
      if (this.trigger.Fired(button, triggerSource, levelThreshold, interval)) {
        this.SpawnOneStar(aim);
      }

      this.SpawnStars(spawnRate, dt, aim);
      this.Advance(accel, maxSpeed, dt, homing, aim);
      this.RenderPixels(size, selectedPalette);
    }

    // The wand's aim point, projected into the centered (u, v) screen frame.
    //
    // OrientationCenter measures a pixel's proximity to the aim by transforming
    // the pixel's sphere position by CurrentCenter and comparing to Spot=(-1,0,0)
    // (see PotentialAt). So the sphere point that lands exactly on Spot — the aim
    // — is Spot rotated by the inverse of CurrentCenter. The orientation is an
    // axis, so its antipode is the same aim direction; select the endpoint on
    // the dome's positive-Z (top) hemisphere before projecting it. Project that
    // sphere point back through the azimuthal-equidistant projection used by
    // the strip coordinates.
    private Vector2 AimPoint() {
      Vector3 aimSphere = Vector3.Transform(
        OrientationCenter.Spot,
        Quaternion.Conjugate(this.center.CurrentCenter)
      );
      return StrutLayoutFactory.ProjectSphereToStrip(
        aimSphere, foldAxisToUpperHemisphere: true);
    }

    // Whether the layer's clear counter changed since the last frame (the 🧹
    // button bumped it). Same edge-detect + first-frame-baseline idiom as
    // LayerTrigger.ManualFired, against the shared clear generation.
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

    // Accrue fractional spawns and birth whole stars at the rim, frame-rate
    // independent. The MAX_STARS guard lives in SpawnOneStar, so the accumulator
    // still drains while the field is full — a full dome doesn't build a backlog
    // that bursts the instant a slot frees.
    private void SpawnStars(double spawnRate, double dt, Vector2 aim) {
      this.spawnAccumulator += spawnRate * dt;
      while (this.spawnAccumulator >= 1) {
        this.spawnAccumulator -= 1;
        this.SpawnOneStar(aim);
      }
    }

    // Births one star at a random off-rim point, aimed at `aim` with a small
    // inward nudge so acceleration (not a big initial velocity) does the work.
    // Shared by the steady spawn rate and the trigger. No-op at MAX_STARS so
    // neither source can grow the list without bound.
    private void SpawnOneStar(Vector2 aim) {
      if (this.stars.Count >= MAX_STARS) {
        return;
      }
      double theta = this.rand.NextDouble() * 2 * Math.PI;
      double r = SPAWN_RADIUS_MIN
        + this.rand.NextDouble() * (SPAWN_RADIUS_MAX - SPAWN_RADIUS_MIN);
      Vector2 pos = new Vector2(
        (float)(Math.Cos(theta) * r), (float)(Math.Sin(theta) * r));

      Vector2 toAim = aim - pos;
      Vector2 dir = toAim.LengthSquared() > 1e-6
        ? Vector2.Normalize(toAim) : Vector2.Zero;

      this.stars.Add(new Star {
        pos = pos,
        vel = dir * 0.1f,          // gentle initial creep; accel ramps it up
        hueIndex = this.rand.Next(8),
        pinnedTarget = aim,
      });
    }

    // Integrate every star toward its target, cull the ones that arrive or
    // sail off. Homing re-reads the live aim (curves toward a moving wand);
    // otherwise the star is ballistic toward the aim captured at spawn.
    private void Advance(
      double accel, double maxSpeed, double dt, bool homing, Vector2 aim
    ) {
      for (int i = this.stars.Count - 1; i >= 0; i--) {
        Star s = this.stars[i];
        Vector2 target = homing ? aim : s.pinnedTarget;

        Vector2 toTarget = target - s.pos;
        double dist = toTarget.Length();
        if (dist < HIT_RADIUS) {
          // ARRIVAL: for the sketch, just despawn. A burst layer could hook in
          // here — e.g. seed a Ripple/expanding flash centered on target.
          this.stars.RemoveAt(i);
          continue;
        }

        Vector2 dir = toTarget / (float)dist;
        s.vel += dir * (float)(accel * dt);
        // Cap speed so a long fall doesn't blow past the target in one step.
        double speed = s.vel.Length();
        if (speed > maxSpeed && speed > 1e-6) {
          s.vel *= (float)(maxSpeed / speed);
        }
        s.pos += s.vel * (float)dt;

        // Cull anything that has wandered well beyond the rim (e.g. a ballistic
        // star that overshot a target the wand has since moved away from).
        if (s.pos.Length() > SPAWN_RADIUS_MAX + 0.5) {
          this.stars.RemoveAt(i);
          continue;
        }
        this.stars[i] = s;
      }
    }

    // Paint each star as a soft dot into the buffer. Outer loop over pixels so
    // every pixel is written at most once (brightest contributing star wins);
    // inner loop over the small star list. Untouched pixels keep their faded
    // trail from Fade() above.
    private void RenderPixels(double size, int selectedPalette) {
      if (this.stars.Count == 0) {
        return;
      }
      double sizeSq = size * size;
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        ref var pixel = ref this.buffer.pixels[i];
        Vector2 point = this.projectedPixels[i];
        double u = point.X;
        double v = point.Y;

        double bestValue = 0;
        int bestColor = 0;
        for (int j = 0; j < this.stars.Count; j++) {
          Star s = this.stars[j];
          double du = u - s.pos.X;
          double dv = v - s.pos.Y;
          double dSq = du * du + dv * dv;
          if (dSq >= sizeSq) {
            continue;
          }
          // Radial falloff: full at the core, 0 at the dot's edge.
          double value = 1 - Math.Sqrt(dSq) / size;
          if (value > bestValue) {
            bestValue = value;
            bestColor = this.dome.GetGradientColor(
              s.hueIndex, value, 0, true, selectedPalette);
          }
        }
        if (bestValue > 0) {
          pixel.color = bestColor;
        }
      }
    }
  }

}
