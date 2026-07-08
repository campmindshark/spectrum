using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace Spectrum.Visualizers {

  // Paints the dome surface from the orientation of the dome "wands" (and an
  // idle screen-saver when no wand is moving). Each frame splits into two
  // phases: UpdateFrameState() advances the effect state machines and snapshots
  // the devices once, then RenderPixels() runs the single per-pixel loop.
  //
  // Performance notes:
  //  - Pixel positions are static (baked into the buffer at creation), so the
  //    unit-sphere coordinate for every pixel is precomputed once in the
  //    constructor instead of every frame.
  //  - Each device's currentRotation() (a quaternion inverse + multiply) is
  //    computed once per frame into the activeDevices list, not once per pixel.
  //  - The per-pixel loop is allocation-free: every blend in it is "keep the
  //    brighter color" (the old BlendLightPaint), so we track the winning
  //    (hue,sat,val) in locals and pack to an int once, instead of allocating
  //    several Color objects per drawn pixel.
  class LEDDomeQuaternionPaintbrushVisualizer : DomeLayerVisualizer {

    // The "forward" direction calibration assigns to a wand. Opposite ends of
    // the hemisphere are identified, so we measure distance to both spot and
    // -spot (negSpot) and treat them as the same point.
    private static readonly Vector3 spot = new Vector3(-1, 0, 0);
    private static readonly Vector3 negSpot = new Vector3(1, 0, 0);

    // Idle screen-saver tuning. (Whether a wand counts as moving is decided
    // per device by OrientationDevice's motion detection; the screen-saver
    // engages when no connected device is moving.)
    private const double IDLE_NOISE = 0.0001;
    private const double IDLE_MOMENTUM_LIMIT = .001;

    // Stamp tuning.
    private const int STAMP_INTERVAL = 1000;   // frames between possible stamps
    private const double STAMP_LEVEL = .3;     // loudness required to fire
    private const int STAMP_COOLDOWN = 10;     // render frames a stamp lasts
    private const int GRID_STAMP_CUTOFF = 7;   // grid stamp clears once below this

    // Ripple tuning.
    private const int RIPPLE_PERIOD = 1000;
    private const double RIPPLE_COOLDOWN_RESET = 100;

    // Poi speed-scaling (see firmware note in the metaball loop).
    private const double POI_MIN_SCALE = 0.5;
    private const double POI_MAX_SCALE = 5;

    // Frame-rate independence. Every per-frame state advance below is multiplied
    // by frameScale = (real elapsed seconds) / NOMINAL_FRAME_SECONDS, so the
    // animation evolves at a consistent wall-clock speed regardless of how fast
    // the Operator loop happens to tick (it varies with CPU load and hardware).
    // The tuning constants/sliders were dialed in at NOMINAL_FPS, so frameScale
    // == 1 there reproduces the original behavior exactly. MAX_FRAME_SCALE caps
    // the catch-up after a stall (or the very first frame) so one long gap can't
    // jolt the animation forward.
    private const double NOMINAL_FPS = 120;
    private const double NOMINAL_FRAME_SECONDS = 1 / NOMINAL_FPS;
    private const double MAX_FRAME_SCALE = 5;

    // One wand's contribution for this frame, with its rotation and scaling
    // factors resolved once so the pixel loop does no per-device bookkeeping.
    private struct DeviceFrame {
      public Quaternion rotation;  // currentRotation(), computed once this frame
      public double bonus;         // button-press multiplier (1 or 4)
      public bool isPoi;           // device is a poi and only poi are connected
      public double poiK;          // poi speed coefficient (avgDistance * range)
    }

    private readonly Configuration config;
    private readonly AudioInput audio;
    private readonly OrientationInput orientationInput;
    private readonly BeatBroadcaster beat;
    private readonly LEDDomeOutput dome;
    private readonly LEDDomeOutputBuffer buffer;
    private readonly Random rand;

    // Static per-pixel geometry, baked once: the unit-sphere position and
    // whether the pixel is high enough on the dome to twinkle (z > .2).
    private readonly Vector3[] pixelPositions;
    private readonly bool[] twinkleEligible;

    // Reused each frame to avoid per-frame allocation in the hot path.
    private readonly List<DeviceFrame> activeDevices = new List<DeviceFrame>();
    // Reused each frame: the connected devices currently scored as moving.
    // Still-but-transmitting wands stay connected (and listed in Wand Status)
    // but are excluded from the visualization.
    private readonly List<KeyValuePair<int, OrientationDevice>> movingDevices =
      new List<KeyValuePair<int, OrientationDevice>>();

    // Wall-clock frame timing. frameTimer measures the gap since the previous
    // Visualize() call; frameScale converts that into nominal-frame units.
    private readonly Stopwatch frameTimer = new Stopwatch();
    private double frameScale = 1;

    // Idle drift state.
    private Quaternion currentOrientation = new Quaternion(0, 0, 0, 1);
    private bool idle = false;
    private double yaw = 0, pitch = -.25, roll = 0;
    private double yawMomentum = 0, pitchMomentum = 0.0005, rollMomentum = 0;

    // Spotlight / effect center resolved per frame.
    private int spotlightId = -1;
    private Quaternion spotlightCenter = new Quaternion(0, 0, 0, 1);
    private Quaternion currentCenter = new Quaternion(0, 0, 0, 1);

    // Stamp state.
    private Quaternion stampCenter = new Quaternion(0, 0, 0, 1);
    private double counter = 0;
    private int cooldown = 7;
    private double lastProgress = 0;
    private bool stampFired = false;
    private int stampEffect = 0; // 1 - grid of rings; 2 - rhythm stamp

    // Ripple state.
    private int rippleType = 0; // 0 - 'static' ripple; 1 - 'follower' ripple
    private Quaternion rippleCenter = new Quaternion(0, 0, 0, 1);
    private double rippleCounter = 0;
    private bool rippleFiring = false;
    private double rippleCooldown = RIPPLE_COOLDOWN_RESET;

    // Contour state.
    private double contourCounter = 0;

    public LEDDomeQuaternionPaintbrushVisualizer(
      Configuration config,
      AudioInput audio,
      OrientationInput orientationInput,
      BeatBroadcaster beat,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.audio = audio;
      this.orientationInput = orientationInput;
      this.beat = beat;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeOutputBuffer();
      this.rand = new Random();

      // Bake the static unit-sphere position of every pixel once. The original
      // mapping has x,y come "out of" the top-left corner, so y is flipped.
      this.pixelPositions = new Vector3[buffer.pixels.Length];
      this.twinkleEligible = new bool[buffer.pixels.Length];
      for (int i = 0; i < buffer.pixels.Length; i++) {
        var p = buffer.pixels[i];
        float x = (float)(2 * p.x - 1);
        float y = (float)(1 - 2 * p.y);
        float z = (x * x + y * y) > 1 ? 0 : (float)Math.Sqrt(1 - x * x - y * y);
        this.pixelPositions[i] = new Vector3(x, y, z);
        this.twinkleEligible[i] = z > .2;
      }
    }

    public int Priority {
      get {
        return DomeLayerSettings.StackActivates(
          this.config.domeLayerStack, "quaternion-paintbrush"
        ) ? 2 : 0;
      }
    }

    public string LayerKey => "quaternion-paintbrush";
    public LEDDomeOutputBuffer LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientationInput });
    }

    public void Visualize() {
      UpdateFrameScale();
      double progress = this.beat.ProgressThroughMeasure;
      double level = this.audio.Volume;

      // This layer's own tuning, resolved once per frame from its stack entry
      // (fade/hue speed stay global — shared scene state, not layer params).
      IList<DomeLayerSettings> stack = this.config.domeLayerStack;
      double size = DomeLayerSettings.ParamValue(stack, this.LayerKey, "size");
      double twinkle =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "twinkleDensity");
      double rippleCDStep =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "rippleCDStep");
      double rippleStep =
        DomeLayerSettings.ParamValue(stack, this.LayerKey, "rippleStep");

      ApplyGlobalEffects(progress);
      UpdateDeviceFrame(level);
      UpdateStamp(progress, level);
      UpdateRipple(rippleCDStep, rippleStep);
      UpdateContour(level);
      RenderPixels(level, size, twinkle);

      lastProgress = progress;
    }

    // Measures the real time elapsed since the previous frame and converts it
    // into nominal-frame units (frameScale), the factor every per-frame state
    // advance is multiplied by so animation speed tracks wall-clock time rather
    // than the Operator loop rate.
    private void UpdateFrameScale() {
      if (!frameTimer.IsRunning) {
        // First frame: no previous timestamp to diff against, so assume a single
        // nominal frame and start the clock.
        frameTimer.Restart();
        frameScale = 1;
        return;
      }
      double elapsedSeconds = frameTimer.Elapsed.TotalSeconds;
      frameTimer.Restart();
      frameScale = Clamp(elapsedSeconds / NOMINAL_FRAME_SECONDS, 0, MAX_FRAME_SCALE);
    }

    // Whole-buffer fade and beat-synced hue rotation.
    private void ApplyGlobalEffects(double progress) {
      // Fade is multiplicative per frame: each frame keeps the fraction
      // retention = 1 - 5^-fadeSpeed of the previous brightness. To hold the
      // same fade per unit of wall-clock time at any frame rate, that whole
      // retention factor is compounded over frameScale frames as a power
      // (retention^frameScale), which reduces to the nominal value at
      // frameScale == 1. (Scaling the inner 5^ exponent instead would compound
      // 5^-fadeSpeed rather than the retention factor, a different quantity.)
      double frameRetention = 1 - Math.Pow(5, -this.config.domeGlobalFadeSpeed);
      buffer.Fade(Math.Pow(frameRetention, frameScale), 0);
      buffer.HueRotate((3 * progress * progress - 3 * progress + 1) * Math.Pow(10, -this.config.domeGlobalHueSpeed) * frameScale);
      counter += frameScale;
    }

    // Snapshots the wands once, filters them down to the ones actually moving,
    // and resolves the spotlight and effect center for this frame. Each wand's
    // currentRotation() is computed exactly once here, into activeDevices.
    private void UpdateDeviceFrame(double level) {
      // Snapshot device state so we don't race the receive thread mid-frame.
      Dictionary<int, OrientationDevice> devices = orientationInput.DevicesSnapshot();
      activeDevices.Clear();
      movingDevices.Clear();

      // A wand that keeps transmitting but isn't physically moving is excluded
      // from the visualization (it stays connected, so the Wand Status views
      // still list it). OrientationDevice scores the motion per device on the
      // receive thread; the screen-saver engages when nothing is moving.
      foreach (var kvp in devices) {
        if (kvp.Value.isMoving) {
          movingDevices.Add(kvp);
        }
      }

      idle = movingDevices.Count == 0;

      // Hack to temporarily ignore all wands if the spotlight ID is -2.
      if (config.orientationDeviceSpotlight == -2) {
        idle = true;
      }

      if (idle) {
        DriftIdleOrientation(level);
        spotlightId = -1;
      } else {
        BuildActiveDevices();
      }

      currentCenter = spotlightId == -1 ? currentOrientation : spotlightCenter;
    }

    // No wand is moving: randomly nudge the dummy pointer around the dome.
    private void DriftIdleOrientation(double level) {
      yawMomentum = Clamp(yawMomentum + Nudge(IDLE_NOISE) * frameScale, -IDLE_MOMENTUM_LIMIT, IDLE_MOMENTUM_LIMIT);
      rollMomentum = Clamp(rollMomentum + Nudge(IDLE_NOISE) * frameScale, -IDLE_MOMENTUM_LIMIT, IDLE_MOMENTUM_LIMIT);
      pitchMomentum = Clamp(pitchMomentum + Nudge(IDLE_NOISE) * frameScale, -IDLE_MOMENTUM_LIMIT, IDLE_MOMENTUM_LIMIT);

      yaw += 4 * (level + .25) * yawMomentum * frameScale;
      pitch += 4 * (level + .25) * pitchMomentum * frameScale;
      roll += 4 * (level + .25) * rollMomentum * frameScale;

      Quaternion dummy = Quaternion.CreateFromYawPitchRoll(
        (float)(2 * Math.PI * yaw),
        (float)(2 * Math.PI * pitch),
        (float)(2 * Math.PI * roll)
      );
      currentOrientation = Quaternion.Normalize(dummy);
    }

    // Resolve each moving wand's rotation and scaling once, and pick the
    // spotlight (the configured one if it's moving, else the first moving wand
    // seen). Only called when movingDevices is non-empty.
    private void BuildActiveDevices() {
      // If a specific wand is spotlighted and it is currently moving, only that
      // wand contributes to the visualization; every other device is ignored.
      // When the spotlight is -1 (or the chosen wand isn't moving), every moving
      // wand renders.
      int spotlight = config.orientationDeviceSpotlight;
      bool spotlightMoving = false;
      if (spotlight >= 0) {
        foreach (var kvp in movingDevices) {
          if (kvp.Key == spotlight) {
            spotlightMoving = true;
            break;
          }
        }
      }

      // Poi take over the dome only when every *moving* device we render is a
      // poi — a wand lying still with its transmitter on shouldn't veto poi
      // mode, and a spotlighted wand decides poi mode on its own.
      bool onlyPoi = true;
      foreach (var kvp in movingDevices) {
        if (spotlightMoving && kvp.Key != spotlight) {
          continue;
        }
        if (kvp.Value.deviceType != 2) {
          onlyPoi = false;
          break;
        }
      }

      spotlightId = -1;
      foreach (var kvp in movingDevices) {
        // Spotlight: when one wand is selected, skip every other device so only
        // it renders.
        if (spotlightMoving && kvp.Key != spotlight) {
          continue;
        }

        OrientationDevice device = kvp.Value;
        DeviceFrame frame = new DeviceFrame();
        frame.rotation = device.currentRotation();

        // 'Bonus' from a button press; dial this in later.
        int flag = device.actionFlag;
        frame.bonus = (flag == 1 || flag == 2 || flag == 3) ? 4 : 1;

        // If only poi are moving, their visualization takes over the dome;
        // otherwise they are wands on strings. Numbers track the poi firmware
        // (tested against commit 'a194981' of the dome-poi control repo) and
        // will be tweaked as those calculations change.
        frame.isPoi = device.deviceType == 2 && onlyPoi;
        if (frame.isPoi) {
          frame.poiK = device.currentAverageDistance() * (POI_MAX_SCALE - POI_MIN_SCALE);
        }

        activeDevices.Add(frame);

        if (kvp.Key == config.orientationDeviceSpotlight || spotlightId == -1) {
          spotlightId = kvp.Key;
          spotlightCenter = frame.rotation;
        }
      }
    }

    // Stamp state machine: a shape that appears when something loud enough
    // happens, alternating between the grid and rhythm effects.
    private void UpdateStamp(double progress, double level) {
      // A beat has wrapped while we are still rendering a stamp.
      if (cooldown > 0 && lastProgress > progress) {
        cooldown--;
        if (cooldown <= 0) {
          stampFired = false;
        }
      }
      // Enough time has passed and something loud enough happened: fire.
      if (counter > STAMP_INTERVAL && level > STAMP_LEVEL) {
        stampFired = true;
        counter = 0;
        cooldown = STAMP_COOLDOWN;
        stampEffect = stampEffect == 1 ? 2 : 1; // alternate grid / rhythm
        stampCenter = currentCenter;
      }
    }

    // Ripple state machine: a color wave expanding from a center, optionally
    // following the wand. Both step rates are this layer's params.
    private void UpdateRipple(double rippleCDStep, double rippleStep) {
      if (rippleCounter > RIPPLE_PERIOD) {
        rippleCounter = 0;
        rippleFiring = false;
      }

      if (!rippleFiring) {
        rippleCooldown -= rippleCDStep * frameScale;
      }

      if (rippleCooldown < 0) {
        rippleFiring = true;
        rippleType = (rippleType + 1) % 2;
        rippleCenter = currentCenter;
        rippleCooldown = RIPPLE_COOLDOWN_RESET;
      }

      if (rippleFiring) {
        rippleCounter += rippleStep * frameScale;
        if (rippleType == 1) {
          rippleCenter = currentCenter; // follower ripple tracks the wand
        }
      }
    }

    // Pulses the contour lines over time, scaled by loudness.
    private void UpdateContour(double level) {
      contourCounter += 4 * level * frameScale;
      if (contourCounter >= 100) {
        contourCounter = 0;
      }
    }

    // The single per-pixel loop. Composites twinkle, the metaball field, its
    // contour lines, the ripple wave, and stamps. Everything but twinkle and
    // stamps is a "keep the brighter color" blend, so we accumulate the winning
    // (hue,sat,val) and pack to an int once per pixel.
    private void RenderPixels(double level, double size, double twinkle) {
      double thresholdFactor = (size / 4) + level + .01;
      double threshold = 2 / thresholdFactor;
      // High volumes desaturate the metaball.
      double metaballSaturation = Clamp(1.3 / level - 1, .2, 1);
      bool showContours = config.orientationShowContours;
      // Twinkle is a per-pixel chance each frame, so scale the probability by
      // frameScale to hold the twinkle rate (dots per second) steady.
      double twinkleDensity = twinkle * frameScale;

      // Per-frame ripple geometry (constant across pixels).
      double rippleRadius = rippleCounter / 300d;
      double rippleSaturation = Clamp(1 - rippleCounter / 600d, 0, 1);
      double rippleValue = Clamp(1 - rippleCounter / 800d, 0, 1);

      // Per-frame stamp geometry.
      double stampHue = (1 + stampCenter.W) / 2;
      double ringDistance = 2.4 - Clamp(1.8d / (4 - (cooldown / 2d)), 0, 2.4);
      double ringHalfWidth = .003 * cooldown * cooldown;

      for (int i = 0; i < buffer.pixels.Length; i++) {
        Vector3 pixelPoint = pixelPositions[i];

        int best = buffer.pixels[i].color;
        double bestValue = ValueFromInt(best);
        bool drawn = false;

        // Twinkle - dense bright dots at random, high on the dome only.
        if (twinkleEligible[i] && rand.NextDouble() < twinkleDensity) {
          best = 0xFFFFFF;
          bestValue = 1;
          drawn = true;
        }

        // Metaball - sum each wand's contribution to this point; opposite ends
        // of the hemisphere are identified, so we sum both 'ends' at once.
        double potential;
        Quaternion colorCenter;
        if (idle) {
          Vector3 t = Vector3.Transform(pixelPoint, currentOrientation);
          double distance = Vector3.Distance(t, spot);
          double negadistance = Vector3.Distance(t, negSpot);
          potential = 1 / (distance * negadistance);
          colorCenter = currentOrientation;
        } else {
          potential = 0;
          colorCenter = new Quaternion(0, 0, 0, 0);
          for (int d = 0; d < activeDevices.Count; d++) {
            DeviceFrame dev = activeDevices[d];
            Vector3 t = Vector3.Transform(pixelPoint, dev.rotation);
            double distance = Vector3.Distance(t, spot);
            double negadistance = Vector3.Distance(t, negSpot);
            double scale = dev.bonus / (distance * negadistance);
            if (dev.isPoi) {
              scale = scale * dev.poiK + POI_MIN_SCALE;
            }
            if (distance < negadistance) {
              colorCenter += Quaternion.Multiply(dev.rotation, (float)scale);
            } else {
              colorCenter -= Quaternion.Multiply(dev.rotation, (float)scale);
            }
            potential += scale;
          }
          colorCenter = Quaternion.Normalize(colorCenter);
          potential /= activeDevices.Count;
        }

        double metaballHue = (1 + colorCenter.W) / 2;

        // Crisp metaball: a hard cutoff at the threshold, always full value.
        if (potential - threshold > 0) {
          drawn = true;
          if (1 > bestValue) {
            best = HsvToInt(metaballHue, metaballSaturation, 1);
            bestValue = 1;
          }
        }

        // Contour - highlight level curves of the potential field.
        if (showContours) {
          double potentialContours = Math.Log(1000 * (potential - .5)) + contourCounter / 100;
          double contourBracket = Math.Truncate(potentialContours);
          double contourValue = potentialContours - contourBracket;
          if (contourValue < .2) {
            drawn = true;
            double value = .8 - Clamp(1 - contourBracket / 10, 0, .8);
            if (value > bestValue) {
              best = HsvToInt(metaballHue, .4, value);
              bestValue = value;
            }
          }
        }

        // Ripple - a color wave that follows (or sits at) a center.
        if (CloseTo(Vector3.Distance(Vector3.Transform(pixelPoint, rippleCenter), spot), rippleRadius, .01)) {
          drawn = true;
          if (rippleValue > bestValue) {
            best = HsvToInt(metaballHue, rippleSaturation, rippleValue);
            bestValue = rippleValue;
          }
        }

        // Stamps - shapes that appear based on the wand facing. These overwrite
        // rather than blend.
        if (stampFired) {
          double stampDistance = Vector3.Distance(Vector3.Transform(pixelPoint, stampCenter), spot);
          if (stampEffect == 1) {
            // Evenly spaced grid of rings.
            if (stampDistance % .4 < .05) {
              best = HsvToInt(stampHue, .2, 1);
              bestValue = 1;
              drawn = true;
            }
          } else if (stampEffect == 2) {
            // Time-delayed band that contracts to the beat.
            if (Between(stampDistance, ringDistance - ringHalfWidth, ringDistance + ringHalfWidth)) {
              best = HsvToInt(stampHue, .2, 1);
              bestValue = 1;
              drawn = true;
            }
          }
        }

        // Only write when an effect touched this pixel, so untouched pixels keep
        // the sub-integer precision their faded RGB accumulators carry.
        if (drawn) {
          buffer.pixels[i].color = best;
        }
      }

      // Grid stamp is a single quick flash; clear it once the cooldown winds
      // down (the rhythm stamp persists for the full cooldown instead).
      if (cooldown < GRID_STAMP_CUTOFF && stampEffect == 1) {
        stampFired = false;
      }
    }

    // HSV -> packed RGB int, matching Color(h,s,v).ToInt() exactly (truncating
    // casts, i % 6 wrap) but without allocating a Color.
    private static int HsvToInt(double h, double s, double v) {
      double r = 0, g = 0, b = 0;
      int i = (int)Math.Floor(h * 6);
      double f = h * 6 - i;
      double p = v * (1 - s);
      double q = v * (1 - f * s);
      double t = v * (1 - (1 - f) * s);
      switch (i % 6) {
        case 0: r = v; g = t; b = p; break;
        case 1: r = q; g = v; b = p; break;
        case 2: r = p; g = v; b = t; break;
        case 3: r = p; g = q; b = v; break;
        case 4: r = t; g = p; b = v; break;
        case 5: r = v; g = p; b = q; break;
      }
      int R = (byte)(255 * r);
      int G = (byte)(255 * g);
      int B = (byte)(255 * b);
      return 256 * 256 * R + 256 * G + B;
    }

    // V channel (max of RGB, normalized) of a packed color, matching Color.V.
    private static double ValueFromInt(int color) {
      int r = (color >> 16) & 0xFF;
      int g = (color >> 8) & 0xFF;
      int b = color & 0xFF;
      int max = Math.Max(r, Math.Max(g, b));
      return max / 255d;
    }

    private static bool Between(double x, double a, double b) {
      return x >= a && x <= b; // closed intervals
    }

    private static double Clamp(double x, double a, double b) {
      if (x < a) return a;
      if (x > b) return b;
      return x;
    }

    private static bool CloseTo(double x, double y, double tolerance) {
      return Math.Abs(x - y) < tolerance;
    }

    private float Nudge(double scale) {
      return (float)((this.rand.NextDouble() - .5) * 2 * scale);
    }
  }
}
