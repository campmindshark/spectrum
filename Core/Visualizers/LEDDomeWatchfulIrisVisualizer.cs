using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;

namespace Spectrum.Visualizers {

  // One scene-scale spherical eye whose complete painted anatomy rotates. The
  // globe, iris, pupil, and eyelid meridians pursue the spotlighted wand or
  // idle target together with visible inertia. Scleral color, bold vascular
  // landmarks, and the blink aperture all live in globe-local coordinates, so
  // their sweep makes the entire eyeball roll instead of reading as a flat iris
  // sliding under a stationary mask.
  // The iris is a spherical cap and therefore foreshortens off-axis.
  // Capture level dilates the pupil. The manual action always blinks; the
  // selected source can additionally blink on beats or strong audio onsets.
  class LEDDomeWatchfulIrisVisualizer : DomeLayerVisualizer {
    private static readonly Vector3 GlobeLightDirection = Vector3.Normalize(
      new Vector3(-0.34f, 0.28f, 0.90f));
    private static readonly Vector3 ScleraToneAxis = Vector3.Normalize(
      new Vector3(-0.70f, 0.25f, 0.67f));
    private static readonly Vector3 ScleraSheenAxis = Vector3.Normalize(
      new Vector3(0.24f, -0.36f, 0.90f));
    private static readonly Vector3 ScleraVeinNormalA = Vector3.Normalize(
      new Vector3(0.27f, 0.93f, -0.24f));
    private static readonly Vector3 ScleraVeinNormalB = Vector3.Normalize(
      new Vector3(-0.76f, 0.41f, 0.50f));
    private static readonly Vector3 ScleraVeinNormalC = Vector3.Normalize(
      new Vector3(0.58f, 0.32f, 0.75f));

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

      int lidTint = this.dome.GetSingleColor(7, options.Palette);
      int eyelidColor = MixColor(0x09050D, lidTint, 0.10);
      for (int index = 0; index < this.buffer.pixels.Length; index++) {
        Vector3 position = this.pixelPositions[index];
        Vector3 globeLocal = GlobeLocalPosition(
          position, this.globeRotation);

        // The almond aperture and its blink seam are markings on the turning
        // globe in this scene, rather than a stationary screen-space mask.
        // Sampling them in globe-local coordinates makes a rotated eyeball
        // close along its newly transported meridian.
        double aperture = ApertureCoverage(
          globeLocal.X, globeLocal.Y,
          openness, options.EyelidSoftness);
        double hue = 0;
        int eyeColor = this.EyeColorAt(
          position, globeLocal,
          pupilRatio, options.IrisComplexity,
          options.ScleraBrightness, options.Palette, out hue);

        ref LEDDomeOutputPixel pixel = ref this.buffer.pixels[index];
        pixel.color = MixColor(eyelidColor, eyeColor, aperture);
        pixel.SetAlpha(1);
        pixel.hue = hue;
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

    private int EyeColorAt(
      Vector3 surfacePosition,
      Vector3 globeLocalPosition,
      double pupilRatio,
      int complexity,
      double scleraBrightness,
      int selectedPalette,
      out double hue
    ) {
      Vector3 p = surfacePosition.LengthSquared() > 1e-10f
        ? Vector3.Normalize(surfacePosition)
        : Vector3.UnitZ;
      double x = p.X;
      double y = p.Y;

      // Every large material cue is sampled in the same persistent globe-local
      // frame. Fixed venue light still models the dome's volume, but the broad
      // warm/cool hemispheres, sheen, rear blush, limbus, and vessels now roll
      // together with the eyelid meridians. This makes the sclera read as the
      // surface of the turning eyeball instead of a white backdrop.
      Vector3 globeLocal = NormalizeDirection(globeLocalPosition);
      double diffuse = 0.5 + 0.5 * Math.Clamp(
        Vector3.Dot(p, GlobeLightDirection), -1, 1);
      double eyeRadius = Math.Sqrt(x * x + y * y / 0.42);
      double limb = Math.Sqrt(Math.Clamp(p.Z, 0, 1));
      double rollTone = 0.5 + 0.5 * Math.Clamp(
        Vector3.Dot(globeLocal, ScleraToneAxis), -1, 1);
      double poleLight = SmoothStep(-0.55, 0.92, globeLocal.Z);
      double baseScleraBrightness = Math.Clamp(
        0.29 + 0.14 * diffuse + 0.20 * limb - 0.07 * eyeRadius
          + 0.29 * rollTone + 0.17 * poleLight,
        0.38, 1);
      int scleraTint = MixColor(0xE6C7BA, 0xFFF8E8, rollTone);
      int sclera = LEDColor.ScaleColor(
        scleraTint, baseScleraBrightness);

      double rearBlush = SmoothStep(
        -0.10, 0.82, -globeLocal.X + 0.28 * globeLocal.Y);
      double rearHemisphere = 1 - SmoothStep(
        -0.46, 0.58, globeLocal.Z);
      rearBlush *= 0.34 + 0.66 * rearHemisphere;
      double fineVeinPhase =
        16 * globeLocal.X + 9 * globeLocal.Y
        + 4.5 * Math.Sin(5 * globeLocal.Z - 3 * globeLocal.Y);
      double branchPhase =
        25 * globeLocal.Y - 7 * globeLocal.Z
        + 3 * Math.Sin(8 * globeLocal.X);
      double fineVein = SmoothStep(
        0.76, 0.98,
        Math.Abs(Math.Sin(fineVeinPhase) * Math.Sin(branchPhase)));
      double vascularMeridians = ScleraVascularStrength(globeLocal);
      double blood = Math.Clamp(
        0.31 * rearBlush + 0.18 * fineVein
          + 0.66 * vascularMeridians,
        0, 0.76);
      sclera = MixColor(sclera, 0xA91F38, blood);

      // The dark outer limbus belongs to the corneal pole, not to the socket.
      // Its traveling arc ties the iris position to the rotation of the whole
      // shell, particularly at the strongly foreshortened gaze extremes.
      double irisAlignment = Math.Clamp(globeLocal.Z, -1, 1);
      double irisTangent = Math.Sqrt(Math.Max(
        0, 1 - irisAlignment * irisAlignment)) / IrisRadius;
      double limbus = Math.Exp(-Math.Pow(
        (irisTangent - 1.08) / 0.13, 2));
      limbus *= SmoothStep(0.08, 0.72, irisAlignment);
      sclera = MixColor(sclera, 0x5B273A, 0.44 * limbus);

      // A broad globe-attached sheen is intentionally stronger than a
      // physically neutral sclera. On the sparse LED lattice it provides one
      // coherent highlight that visibly sweeps with the rotating sphere.
      double sheen = Math.Pow(Math.Max(
        0, Vector3.Dot(globeLocal, ScleraSheenAxis)), 7);
      sclera = MixColor(sclera, 0xFFFFFF, 0.42 * sheen);
      sclera = ScaleScleraColor(sclera, scleraBrightness);
      hue = 0;

      // The iris, pupil, fibers, and reflections use the same transported
      // globe axes as the sclera and eyelids. This retains their roll around
      // the gaze pole instead of rebuilding an always-upright tangent frame.
      // A circular cap still foreshortens naturally near the visible rim.
      double localX = globeLocal.X / IrisRadius;
      double localY = globeLocal.Y / IrisRadius;
      double radial = Math.Sqrt(localX * localX + localY * localY);
      if (irisAlignment <= 0 || radial >= 1) {
        return sclera;
      }

      double angle = Math.Atan2(localY, localX);
      double hx = localX + 0.28;
      double hy = localY - 0.31;
      bool highlightPoint = hx * hx + hy * hy < 0.011;
      if (radial <= pupilRatio) {
        int pupil = LEDColor.ScaleColor(
          this.dome.GetSingleColor(0, selectedPalette), 0.025);
        pupil = MixColor(0x000002, pupil, 0.18);
        return highlightPoint ? MixColor(pupil, 0xFFFFFF, 0.95) : pupil;
      }

      double fiber = IrisFilament(radial, angle, complexity);
      double collarette = Math.Exp(
        -Math.Pow((radial - 0.53) / 0.095, 2));
      double outerRing = SmoothStep(0.84, 1, radial);
      double pupilRim = 1 - SmoothStep(
        pupilRatio, Math.Min(1, pupilRatio + 0.10), radial);
      double palettePosition = Fract(
        0.08 + 0.58 * radial + 0.27 * fiber + 0.07 * Math.Sin(3 * angle));
      int irisTint = this.dome.GetGradientBetweenColors(
        0, 7, palettePosition, 0, true, selectedPalette);
      double brightness = Math.Clamp(
        0.32 + 0.58 * fiber + 0.18 * collarette
          - 0.48 * outerRing - 0.30 * pupilRim,
        0.10, 1);
      int iris = LEDColor.ScaleColor(irisTint, brightness);

      // Thin upper-left reflection: a short curved glint plus a pinpoint. The
      // pupil branch above draws the same pinpoint when dilation reaches it.
      double highlightAngle = Math.Abs(WrapAngle(angle - 2.15));
      bool highlightArc = radial > 0.55 && radial < 0.61
        && highlightAngle < 0.42;
      if (highlightArc || highlightPoint) {
        iris = MixColor(iris, 0xFFFFFF, highlightPoint ? 0.95 : 0.78);
      }

      hue = palettePosition;
      return iris;
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
    // transports every scleral landmark around curved gaze paths and retains
    // the subtle torsion that a direction-only reconstruction discards.
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

    // Move a visible dome point back into the eyeball's transported material
    // frame. Every anatomical feature, including the aperture/blink seam, must
    // use this mapping for the whole object to rotate around the sphere.
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
    // stable globe-local coordinates for the rolling scleral landmarks.
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

    // Three broken great-circle vessels are fixed to the eyeball. Unlike fine
    // procedural texture, their width survives the physical LED spacing and
    // gives the audience unmistakable landmarks to watch roll across the dome.
    internal static double ScleraVascularStrength(Vector3 globeLocal) {
      globeLocal = NormalizeDirection(globeLocal);
      double vesselA = VeinBand(Math.Abs(Vector3.Dot(
        globeLocal, ScleraVeinNormalA)));
      double vesselB = 0.86 * VeinBand(Math.Abs(Vector3.Dot(
        globeLocal, ScleraVeinNormalB)));
      double vesselC = 0.72 * VeinBand(Math.Abs(Vector3.Dot(
        globeLocal, ScleraVeinNormalC)));
      double vessel = Math.Max(vesselA, Math.Max(vesselB, vesselC));
      double broken = 0.72 + 0.28 * Math.Pow(
        Math.Sin(11 * globeLocal.X - 7 * globeLocal.Y
          + 5 * globeLocal.Z), 2);
      return Math.Clamp(vessel * broken, 0, 1);
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
      return SmoothStep(-softness, softness, edge);
    }

    internal static double IrisFilament(
      double radial, double angle, int complexity
    ) {
      radial = Math.Clamp(radial, 0, 1);
      complexity = Math.Clamp(complexity, 1, 64);
      double broad = 0.5 + 0.5 * Math.Sin(
        complexity * angle + 17 * radial
          + 1.8 * Math.Sin(3 * angle - 8 * radial));
      double fine = 0.5 + 0.5 * Math.Sin(
        (complexity + 7) * angle - 29 * radial
          + 0.9 * Math.Sin(5 * angle + 11 * radial));
      double crypt = 0.5 + 0.5 * Math.Sin(
        (complexity * 0.5 + 2) * angle + 43 * radial * radial);
      return Math.Clamp(0.52 * broad + 0.30 * fine + 0.18 * crypt, 0, 1);
    }

    private static double SmoothStep(double edge0, double edge1, double x) {
      if (edge1 <= edge0) {
        return x >= edge1 ? 1 : 0;
      }
      double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
      return t * t * (3 - 2 * t);
    }

    private static double Fract(double value) =>
      value - Math.Floor(value);

    private static double WrapAngle(double angle) =>
      angle - 2 * Math.PI * Math.Floor((angle + Math.PI) / (2 * Math.PI));

    private static double VeinBand(double planeDistance) =>
      1 - SmoothStep(0.018, 0.070, planeDistance);

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
