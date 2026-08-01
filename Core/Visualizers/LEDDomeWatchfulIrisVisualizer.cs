using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Spectrum.Visualizers {

  // One scene-scale spherical eye built from flat, inexpensive regions. The
  // eyelid, sclera, iris, pupil, and highlight pursue the spotlighted wand or
  // idle target together with visible inertia. All shapes live in globe-local
  // coordinates, so the iris naturally foreshortens off-axis.
  // Capture level dilates the pupil. The manual action always blinks; the
  // selected source can additionally blink on beats or strong audio onsets.
  class LEDDomeWatchfulIrisVisualizer : DomeLayerVisualizer {
    private const double IrisRadius = 0.34;
    private const double GlobeFollowTimeSeconds = 0.34;
    private const double BlinkDurationSeconds = 0.46;
    private const double BlinkAudioThreshold = 0.48;
    private const double BlinkAudioRise = 0.14;

    private readonly LayerRendererRuntime runtime;
    private readonly IAudioLevelInput audio;
    private readonly OrientationInput orientation;
    private readonly OrientationCenter orientationCenter;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private readonly ImmutableArray<Vector3> pixelPositions;
    private readonly LayerTrigger trigger;
    private readonly IrisTransientDetector transientDetector =
      new IrisTransientDetector(BlinkAudioThreshold, BlinkAudioRise);
    private readonly Stopwatch frameTimer = new Stopwatch();

    private double blinkAge = double.PositiveInfinity;
    private double dilationEnvelope;
    private Quaternion globeRotation = Quaternion.Identity;

    public LEDDomeWatchfulIrisVisualizer(
      DomeLayerEnvironment environment,
      LayerRendererRuntime runtime,
      IAudioLevelInput audio,
      OrientationInput orientation,
      OrientationCenter orientationCenter,
      BeatBroadcaster beats,
      DomeRenderContext dome
    ) {
      this.runtime = runtime;
      this.audio = audio;
      this.orientation = orientation;
      this.orientationCenter = orientationCenter;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
      this.pixelPositions = this.buffer.BakePixelPositions();
      this.trigger = new LayerTrigger(
        environment, orientation, runtime.InstanceId, beats, audio);
    }

    public int Priority => 2;
    public string LayerKey => "watchful-iris";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }

    private Input[]? inputs;
    public Input[] GetInputs() =>
      this.inputs ?? (this.inputs = new Input[] {
        this.audio, this.orientation,
      });

    public void Visualize() {
      WatchfulIrisLayerOptions options =
        this.runtime.GetOptions<WatchfulIrisLayerOptions>();
      double elapsed = this.ElapsedSeconds();

      // LayerTrigger supplies the manual action in every mode and Beat when
      // selected. A dedicated onset detector supplies Audio Transient; sampling
      // it every frame keeps its envelope fresh if the operator changes modes.
      int triggerSource = options.BlinkTrigger == 1 ? 1 : 0;
      bool fired = this.trigger.Fired(0, triggerSource, 1, double.MaxValue);
      bool audioTransient = this.transientDetector.Sample(
        this.audio.Volume, elapsed);
      if (options.BlinkTrigger == 2 && audioTransient) {
        fired = true;
      }
      if (fired) {
        this.blinkAge = 0;
      } else if (!double.IsPositiveInfinity(this.blinkAge)) {
        this.blinkAge += elapsed;
      }

      // The capture API currently exposes a broadband peak. A short attack and
      // slower release turn it into the heavy-pulse envelope used for dilation,
      // while preserving the intended bass-like movement on dance material.
      this.dilationEnvelope = SmoothDilationEnvelope(
        this.dilationEnvelope, this.audio.Volume, elapsed);
      double pupilRatio = EffectivePupilRatio(
        options.PupilSize, options.DilationGain, this.dilationEnvelope);
      double openness = BlinkOpenness(this.blinkAge);

      this.orientationCenter.Update(0.3);
      Vector2 gaze = TrackingOffset(this.orientationCenter.CurrentCenter);
      Vector3 targetFacing = FacingFromGaze(gaze);
      this.globeRotation = SmoothGlobeRotation(
        this.globeRotation, targetFacing,
        elapsed, GlobeFollowTimeSeconds);

      Quaternion inverseGlobeRotation = Quaternion.Conjugate(
        NormalizeRotation(this.globeRotation));
      // The inverse is fixed for the whole frame. Vector3's matrix transform
      // is substantially cheaper than expanding the same quaternion rotation
      // independently for every dome pixel.
      Matrix4x4 inverseGlobeTransform =
        Matrix4x4.CreateFromQuaternion(inverseGlobeRotation);

      int lidTint = this.dome.GetSingleColor(7, options.Palette);
      int eyelidColor = MixColor(0x09050D, lidTint, 0.10);
      int scleraColor = ScaleScleraColor(
        0xFFF4E8, options.ScleraBrightness);
      int irisColor = this.dome.GetSingleColor(0, options.Palette);
      const int pupilColor = 0x010104;
      for (int index = 0; index < this.buffer.pixels.Length; index++) {
        Vector3 position = this.pixelPositions[index];
        // Baked topology positions and the inverse rotation are normalized.
        // Quaternion rotation therefore preserves the vector length; avoiding
        // another square root for every pixel only gives up float-roundoff
        // correction far below the physical LED lattice's resolution.
        Vector3 globeLocal = Vector3.Transform(
          position, inverseGlobeTransform);

        // The almond aperture and its blink seam are markings on the turning
        // globe in this scene, rather than a stationary screen-space mask.
        // Sampling them in globe-local coordinates makes a rotated eyeball
        // close along its newly transported meridian.
        double aperture = ApertureCoverage(
          globeLocal.X, globeLocal.Y,
          openness, options.EyelidSoftness);
        ref LEDDomeOutputPixel pixel = ref this.buffer.pixels[index];
        if (aperture <= 0) {
          pixel.color = eyelidColor;
          pixel.hue = 0;
          continue;
        }

        int eyeColor = EyeColorAt(
          globeLocal, pupilRatio,
          scleraColor, irisColor, pupilColor);

        // SmoothStep returns exactly one away from the narrow antialiased
        // boundary, so most visible pixels can also skip the final blend.
        pixel.color = aperture >= 1
          ? eyeColor
          : MixColor(eyelidColor, eyeColor, aperture);
        pixel.hue = 0;
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

    private static int EyeColorAt(
      Vector3 globeLocal,
      double pupilRatio,
      int scleraColor,
      int irisColor,
      int pupilColor
    ) {
      if (globeLocal.Z <= 0) {
        return scleraColor;
      }

      double localX = globeLocal.X / IrisRadius;
      double localY = globeLocal.Y / IrisRadius;
      double radialSquared = localX * localX + localY * localY;
      if (radialSquared >= 1) {
        return scleraColor;
      }

      int color = radialSquared <= pupilRatio * pupilRatio
        ? pupilColor : irisColor;
      double hx = localX + 0.28;
      double hy = localY - 0.31;
      if (hx * hx + hy * hy < 0.011) {
        return 0xFFFFFF;
      }
      return color;
    }

    internal static Vector2 TrackingOffset(Quaternion orientation) {
      if (orientation.LengthSquared() < 1e-10f) {
        orientation = Quaternion.Identity;
      } else {
        orientation = Quaternion.Normalize(orientation);
      }
      Vector3 aim = Vector3.Transform(
        OrientationCenter.Spot, Quaternion.Conjugate(orientation));
      if (aim.Z < 0) {
        aim = -aim;
      }
      return new Vector2(0.31f * aim.X, 0.18f * aim.Y);
    }

    // Lift the deliberately bounded screen-space gaze into a forward direction
    // on the eye sphere. The gain makes the globe's turn substantially larger
    // than the old flat iris translation while keeping the iris inside the
    // resting eyelid aperture at the extreme corners.
    internal static Vector3 FacingFromGaze(Vector2 gaze) {
      double x = Math.Clamp(gaze.X * 1.80, -0.56, 0.56);
      double y = Math.Clamp(gaze.Y * 2.20, -0.40, 0.40);
      double radiusSquared = x * x + y * y;
      const double maximumRadiusSquared = 0.47;
      if (radiusSquared > maximumRadiusSquared) {
        double scale = Math.Sqrt(maximumRadiusSquared / radiusSquared);
        x *= scale;
        y *= scale;
        radiusSquared = maximumRadiusSquared;
      }
      return Vector3.Normalize(new Vector3(
        (float)x, (float)y,
        (float)Math.Sqrt(Math.Max(0, 1 - radiusSquared))));
    }

    // Exponential pursuit gives the eyeball weight: the iris target can jump
    // with the wand, but the globe closes the angular gap over several frames.
    internal static Vector3 SmoothFacing(
      Vector3 current,
      Vector3 target,
      double elapsedSeconds,
      double timeConstantSeconds
    ) {
      current = NormalizeDirection(current);
      target = NormalizeDirection(target);
      double elapsed = Math.Clamp(elapsedSeconds, 0, 0.1);
      if (timeConstantSeconds <= 1e-6 || elapsed <= 0) {
        return elapsed <= 0 ? current : target;
      }
      double response = 1 - Math.Exp(-elapsed / timeConstantSeconds);
      Vector3 blended = Vector3.Lerp(current, target, (float)response);
      return NormalizeDirection(blended);
    }

    // Preserve the globe's complete orientation while its forward pole chases
    // the gaze. Applying each shortest-arc delta to the existing quaternion
    // transports the complete eye around curved gaze paths and retains the
    // subtle torsion that a direction-only reconstruction discards.
    internal static Quaternion SmoothGlobeRotation(
      Quaternion currentRotation,
      Vector3 targetFacing,
      double elapsedSeconds,
      double timeConstantSeconds
    ) {
      currentRotation = NormalizeRotation(currentRotation);
      Vector3 currentFacing = NormalizeDirection(Vector3.Transform(
        Vector3.UnitZ, currentRotation));
      Vector3 nextFacing = SmoothFacing(
        currentFacing, targetFacing,
        elapsedSeconds, timeConstantSeconds);
      Quaternion delta = RotationBetween(currentFacing, nextFacing);
      return NormalizeRotation(Quaternion.Concatenate(
        currentRotation, delta));
    }

    // Move a visible dome point back into the eye's transported shape frame.
    // The aperture and color regions use this mapping so the whole object
    // rotates around the sphere.
    internal static Vector3 GlobeLocalPosition(
      Vector3 surfacePosition, Quaternion globeRotation
    ) {
      Vector3 surface = NormalizeDirection(surfacePosition);
      Quaternion inverse = Quaternion.Conjugate(
        NormalizeRotation(globeRotation));
      return NormalizeDirection(Vector3.Transform(surface, inverse));
    }

    // Minimal rotation taking the viewer-facing pole (+Z) to the globe's
    // lagged facing direction. Applying its conjugate to a surface point gives
    // stable globe-local coordinates for all of the eye's shapes.
    internal static Quaternion RotationFromForward(Vector3 facing) {
      return RotationBetween(Vector3.UnitZ, facing);
    }

    private static Quaternion RotationBetween(Vector3 from, Vector3 to) {
      from = NormalizeDirection(from);
      to = NormalizeDirection(to);
      double dot = Math.Clamp(Vector3.Dot(from, to), -1, 1);
      if (dot > 0.999999) {
        return Quaternion.Identity;
      }
      if (dot < -0.999999) {
        Vector3 seed = Math.Abs(from.X) < 0.8f
          ? Vector3.UnitX : Vector3.UnitY;
        Vector3 oppositeAxis = Vector3.Normalize(Vector3.Cross(from, seed));
        return Quaternion.CreateFromAxisAngle(
          oppositeAxis, (float)Math.PI);
      }
      Vector3 axis = Vector3.Normalize(
        Vector3.Cross(from, to));
      return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(
        axis, (float)Math.Acos(dot)));
    }

    internal static int ScaleScleraColor(int color, double brightness) {
      brightness = double.IsFinite(brightness)
        ? Math.Max(0, brightness) : 0;
      int red = (int)Math.Clamp(
        ((color >> 16) & 0xFF) * brightness, 0, 255);
      int green = (int)Math.Clamp(
        ((color >> 8) & 0xFF) * brightness, 0, 255);
      int blue = (int)Math.Clamp(
        (color & 0xFF) * brightness, 0, 255);
      return (red << 16) | (green << 8) | blue;
    }

    internal static double EffectivePupilRatio(
      double pupilSize, double dilationGain, double audioLevel
    ) => Math.Clamp(
      pupilSize + dilationGain * Math.Sqrt(Math.Clamp(audioLevel, 0, 1)),
      0.06, 0.84);

    internal static double SmoothDilationEnvelope(
      double current, double level, double elapsedSeconds
    ) {
      current = Math.Clamp(current, 0, 1);
      level = Math.Clamp(level, 0, 1);
      double elapsed = Math.Clamp(elapsedSeconds, 0, 0.1);
      double timeConstant = level > current ? 0.045 : 0.20;
      double response = elapsed <= 0
        ? 0 : 1 - Math.Exp(-elapsed / timeConstant);
      return current + (level - current) * response;
    }

    internal static double BlinkOpenness(double ageSeconds) {
      if (!double.IsFinite(ageSeconds) || ageSeconds < 0
          || ageSeconds >= BlinkDurationSeconds) {
        return 1;
      }
      double phase = ageSeconds / BlinkDurationSeconds;
      double closed = Math.Sin(Math.PI * phase);
      return 1 - closed * closed;
    }

    internal static double ApertureCoverage(
      double x, double y, double openness, double softness
    ) {
      double almond = 0.64 * Math.Sqrt(Math.Max(0, 1 - x * x));
      double edge = almond * Math.Clamp(openness, 0, 1) - Math.Abs(y);
      softness = Math.Max(0, softness);
      if (softness <= 1e-9) {
        return edge >= 0 ? 1 : 0;
      }
      // SmoothStep clamps to these exact values. Most LEDs are well away from
      // the narrow antialiased lid boundary, so classify them before doing its
      // divide and cubic interpolation.
      if (edge <= -softness) {
        return 0;
      }
      if (edge >= softness) {
        return 1;
      }
      return SmoothStep(-softness, softness, edge);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double SmoothStep(double edge0, double edge1, double x) {
      if (edge1 <= edge0) {
        return x >= edge1 ? 1 : 0;
      }
      double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
      return t * t * (3 - 2 * t);
    }

    private static Vector3 NormalizeDirection(Vector3 direction) =>
      direction.LengthSquared() > 1e-10f
        ? Vector3.Normalize(direction)
        : Vector3.UnitZ;

    private static Quaternion NormalizeRotation(Quaternion rotation) {
      float lengthSquared = rotation.LengthSquared();
      return float.IsFinite(lengthSquared) && lengthSquared > 1e-10f
        ? Quaternion.Normalize(rotation)
        : Quaternion.Identity;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int MixColor(int from, int to, double amount) {
      amount = Math.Clamp(amount, 0, 1);
      double inverse = 1 - amount;
      int red = (int)(((from >> 16) & 0xFF) * inverse
        + ((to >> 16) & 0xFF) * amount);
      int green = (int)(((from >> 8) & 0xFF) * inverse
        + ((to >> 8) & 0xFF) * amount);
      int blue = (int)((from & 0xFF) * inverse
        + (to & 0xFF) * amount);
      return (red << 16) | (green << 8) | blue;
    }
  }

  // Rise-over-envelope detector for blinks. A high threshold rejects ordinary
  // ambience; cooldown and hysteresis make a sustained loud passage blink once
  // instead of once per frame.
  internal sealed class IrisTransientDetector {
    private const double CooldownSeconds = 0.55;
    private readonly double threshold;
    private readonly double requiredRise;
    private double envelope;
    private double cooldown;
    private bool initialized;

    public IrisTransientDetector(double threshold, double requiredRise) {
      this.threshold = Math.Clamp(threshold, 0, 1);
      this.requiredRise = Math.Clamp(requiredRise, 0, 1);
    }

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

      bool fired = this.cooldown <= 0
        && level >= this.threshold
        && level - this.envelope >= this.requiredRise;
      if (fired) {
        this.cooldown = CooldownSeconds;
      }

      double timeConstant = level > this.envelope ? 0.20 : 0.65;
      double response = elapsed <= 0
        ? 0 : 1 - Math.Exp(-elapsed / timeConstant);
      this.envelope += (level - this.envelope) * response;
      return fired;
    }
  }
}
