using Spectrum.Base;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Spectrum.Visualizers {

  // Shared orientation-center tracking for every effect derived from wand
  // orientation. Lifted out of Quaternion Paintbrush so a second orientation-
  // driven layer (Ripple) doesn't have to reinvent it — see the "two
  // orientation abstractions" section of docs/layers_inventory.md.
  //
  // Owns: per-frame device snapshotting + motion filtering, the idle
  // screen-saver drift (no wand moving -> a dummy pointer wanders the dome),
  // spotlight resolution, and the orientation-derived palette (a proximity-
  // weighted sum of every active device's rotation, sampled per pixel — the
  // "color is a continuous function of the collective orientation" idea).
  //
  // A visualizer that wants a *position* to draw around reads CurrentCenter;
  // one that wants a *color* calls ColorCenterAt(pixelPoint) (metaball hue is
  // HueFromColorCenter(colorCenter) -- see that method for the sign-invariance
  // that keeps it continuous through the double-cover flip).
  //
  // One instance is shared across every orientation-driven layer (wired up
  // once in Operator's constructor) so there is a single idle-drift dummy
  // pointer and a single spotlight resolution for the whole stack — two
  // layers active at once (e.g. Metaball + Ripple) must idle-drift around
  // the same wandering point rather than each running its own independent
  // random walk. Update() owns its own FrameClock rather than taking a
  // frameScale from the caller, so calling it more than once per engine tick
  // (once per active layer) is harmless: the second and later calls see
  // near-zero elapsed wall time and contribute negligible extra drift,
  // instead of compounding idle-drift speed by the number of active layers.
  class OrientationCenter {

    public static readonly Vector3 Spot = new Vector3(-1, 0, 0);
    public static readonly Vector3 NegSpot = new Vector3(1, 0, 0);

    private const double IDLE_NOISE = 0.0001;
    private const double IDLE_MOMENTUM_LIMIT = .001;
    private const double POI_MIN_SCALE = 0.5;
    private const double POI_MAX_SCALE = 5;

    // Floor on the product of the two pole distances before dividing, so a
    // pixel landing exactly on Spot/NegSpot can't turn the field into
    // Infinity (which would propagate to NaN through Quaternion.Normalize and
    // flash a garbage-colored pixel). Small enough not to soften the intended
    // spotlight blowup anywhere short of exact alignment.
    private const double MIN_POLE_PRODUCT = 1e-9;

    // One wand's contribution for this frame, with its rotation and scaling
    // factors resolved once so the per-pixel field sample does no per-device
    // bookkeeping.
    private struct DeviceFrame {
      public Quaternion rotation;
      public bool isPoi;
      public double poiK;
    }

    private readonly Configuration config;
    private readonly OrientationInput orientationInput;
    private readonly Random rand = new Random();

    // Owns the wall-clock timing for idle drift, so repeat calls to Update()
    // within the same engine tick (from other active layers sharing this
    // instance) see near-zero elapsed time rather than each applying a full
    // frame's worth of drift.
    private readonly FrameClock frameClock = new FrameClock();

    // Reused each frame to avoid per-frame allocation.
    private readonly List<DeviceFrame> activeDevices = new List<DeviceFrame>();
    private readonly List<KeyValuePair<int, OrientationDevice>> movingDevices =
      new List<KeyValuePair<int, OrientationDevice>>();

    // Idle drift state.
    private Quaternion currentOrientation = new Quaternion(0, 0, 0, 1);
    private bool idle = false;
    private double yaw = 0, pitch = -.25, roll = 0;
    private double yawMomentum = 0, pitchMomentum = 0.0005, rollMomentum = 0;

    // Spotlight / effect center resolved per frame.
    private int spotlightId = -1;
    private Quaternion spotlightCenter = new Quaternion(0, 0, 0, 1);
    private Quaternion currentCenter = new Quaternion(0, 0, 0, 1);

    public OrientationCenter(
      Configuration config,
      OrientationInput orientationInput
    ) {
      this.config = config;
      this.orientationInput = orientationInput;
    }

    // Whether no wand is currently moving (the idle screen-saver dummy
    // pointer is driving CurrentCenter instead of a real device).
    public bool Idle => this.idle;

    // The position effects should draw around this frame: the spotlighted (or
    // first moving) wand's rotation, or the idle dummy pointer's orientation.
    public Quaternion CurrentCenter => this.currentCenter;

    // Snapshots devices, filters to the moving ones, resolves the spotlight,
    // and advances the idle drift. Every active layer calls this once per
    // frame before reading CurrentCenter/ColorCenterAt; frameScale is derived
    // from this instance's own clock (not the caller's), so a second call in
    // the same engine tick is a cheap near-no-op rather than double drift.
    // level scales the idle drift's speed, as the original inline code did.
    public void Update(double level) {
      double frameScale = this.frameClock.Tick();
      Dictionary<int, OrientationDevice> devices =
        this.orientationInput.DevicesSnapshot();
      this.activeDevices.Clear();
      this.movingDevices.Clear();

      // A wand that keeps transmitting but isn't physically moving is
      // excluded from the visualization (it stays connected, so the Wand
      // Status views still list it).
      foreach (var kvp in devices) {
        if (kvp.Value.isMoving) {
          this.movingDevices.Add(kvp);
        }
      }

      this.idle = this.movingDevices.Count == 0;

      // Hack to temporarily ignore all wands if the spotlight ID is -2.
      if (this.config.orientationDeviceSpotlight == -2) {
        this.idle = true;
      }

      if (this.idle) {
        DriftIdleOrientation(frameScale, level);
        this.spotlightId = -1;
      } else {
        BuildActiveDevices();
      }

      this.currentCenter =
        this.spotlightId == -1 ? this.currentOrientation : this.spotlightCenter;
    }

    // No wand is moving: randomly nudge the dummy pointer around the dome.
    private void DriftIdleOrientation(double frameScale, double level) {
      this.yawMomentum = Math.Clamp(
        this.yawMomentum + Nudge(IDLE_NOISE) * frameScale,
        -IDLE_MOMENTUM_LIMIT, IDLE_MOMENTUM_LIMIT);
      this.rollMomentum = Math.Clamp(
        this.rollMomentum + Nudge(IDLE_NOISE) * frameScale,
        -IDLE_MOMENTUM_LIMIT, IDLE_MOMENTUM_LIMIT);
      this.pitchMomentum = Math.Clamp(
        this.pitchMomentum + Nudge(IDLE_NOISE) * frameScale,
        -IDLE_MOMENTUM_LIMIT, IDLE_MOMENTUM_LIMIT);

      this.yaw += 4 * (level + .25) * this.yawMomentum * frameScale;
      this.pitch += 4 * (level + .25) * this.pitchMomentum * frameScale;
      this.roll += 4 * (level + .25) * this.rollMomentum * frameScale;

      Quaternion dummy = Quaternion.CreateFromYawPitchRoll(
        (float)(2 * Math.PI * this.yaw),
        (float)(2 * Math.PI * this.pitch),
        (float)(2 * Math.PI * this.roll)
      );
      this.currentOrientation = Quaternion.Normalize(dummy);
    }

    // Resolve each moving wand's rotation and scaling once, and pick the
    // spotlight (the configured one if it's moving, else the first moving
    // wand seen). Only called when movingDevices is non-empty.
    private void BuildActiveDevices() {
      // If a specific wand is spotlighted and it is currently moving, only
      // that wand contributes; every other device is ignored. When the
      // spotlight is -1 (or the chosen wand isn't moving), every moving wand
      // renders.
      int spotlight = this.config.orientationDeviceSpotlight;
      bool spotlightMoving = false;
      if (spotlight >= 0) {
        foreach (var kvp in this.movingDevices) {
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
      foreach (var kvp in this.movingDevices) {
        if (spotlightMoving && kvp.Key != spotlight) {
          continue;
        }
        if (kvp.Value.deviceType != 2) {
          onlyPoi = false;
          break;
        }
      }

      this.spotlightId = -1;
      foreach (var kvp in this.movingDevices) {
        if (spotlightMoving && kvp.Key != spotlight) {
          continue;
        }

        OrientationDevice device = kvp.Value;
        DeviceFrame frame = new DeviceFrame();
        frame.rotation = device.currentRotation();

        // If only poi are moving, their visualization takes over the dome;
        // otherwise they are wands on strings. Numbers track the poi
        // firmware (tested against commit 'a194981' of the dome-poi control
        // repo) and will be tweaked as those calculations change.
        frame.isPoi = device.deviceType == 2 && onlyPoi;
        if (frame.isPoi) {
          frame.poiK =
            device.currentAverageDistance() * (POI_MAX_SCALE - POI_MIN_SCALE);
        }

        this.activeDevices.Add(frame);

        if (kvp.Key == this.config.orientationDeviceSpotlight
            || this.spotlightId == -1) {
          this.spotlightId = kvp.Key;
          this.spotlightCenter = frame.rotation;
        }
      }
    }

    // The orientation-derived palette and the metaball's potential field at
    // one unit-sphere pixel position: sums each active wand's contribution
    // (opposite ends of the hemisphere are identified, so both 'ends' count
    // at once), or falls back to the idle dummy pointer's single-device field
    // when nothing is moving. colorCenter is the proximity-weighted
    // collective rotation; its hue is HueFromColorCenter(colorCenter).
    public double PotentialAt(Vector3 pixelPoint, out Quaternion colorCenter) {
      if (this.idle) {
        Vector3 t = Vector3.Transform(pixelPoint, this.currentOrientation);
        double distance = Vector3.Distance(t, Spot);
        double negadistance = Vector3.Distance(t, NegSpot);
        colorCenter = this.currentOrientation;
        return 1 / Math.Max(distance * negadistance, MIN_POLE_PRODUCT);
      }

      double potential = 0;
      Quaternion cc = new Quaternion(0, 0, 0, 0);
      for (int d = 0; d < this.activeDevices.Count; d++) {
        DeviceFrame dev = this.activeDevices[d];
        Vector3 t = Vector3.Transform(pixelPoint, dev.rotation);
        double distance = Vector3.Distance(t, Spot);
        double negadistance = Vector3.Distance(t, NegSpot);
        double scale = 1 / Math.Max(distance * negadistance, MIN_POLE_PRODUCT);
        if (dev.isPoi) {
          scale = scale * dev.poiK + POI_MIN_SCALE;
        }
        // Which pole (Spot vs NegSpot) a pixel is nearer to for this device
        // flips the sign of its contribution to cc (so the two poles of a
        // wand's dipole aren't averaged into a canceling color). A hard
        // boolean flip at distance == negadistance would jump cc by
        // 2*scale*rotation right at that plane, tearing the hue field in
        // two along a great circle per device. Using a continuous sign that
        // passes through zero at the boundary keeps cc - and therefore the
        // hue sampled from it - continuous everywhere.
        double sign = (negadistance - distance) / (negadistance + distance);
        cc += Quaternion.Multiply(dev.rotation, (float)(scale * sign));
        potential += scale;
      }
      colorCenter = Quaternion.Normalize(cc);
      return potential / this.activeDevices.Count;
    }

    // Just the orientation-derived color at a pixel, for effects (like
    // Ripple) that sample the field's hue without needing the metaball
    // potential itself.
    public Quaternion ColorCenterAt(Vector3 pixelPoint) {
      PotentialAt(pixelPoint, out Quaternion colorCenter);
      return colorCenter;
    }

    // colorCenter is only defined up to a sign: a quaternion q and -q are the
    // same physical rotation (the SU(2) -> SO(3) double cover), so any hue read
    // from a raw component flips when the sign does. For a single/dominant wand,
    // Normalize collapses PotentialAt's continuous-sign magnitude to a bare
    // +-rotation, so an isolated wand dipping through the equator snaps its hue.
    // W*W is even in the quaternion (W and -W give the same value), so it stays
    // continuous across that flip. Geometrically it's (1 + cos theta) / 2, where
    // theta is the summed rotation's angle; it folds +theta and -theta together
    // and so spans hue [0,1] over a smaller swing than the old (1 + W)/2 did.
    public static double HueFromColorCenter(Quaternion colorCenter) {
      return colorCenter.W * colorCenter.W;
    }

    private float Nudge(double scale) {
      return (float)((this.rand.NextDouble() - .5) * 2 * scale);
    }
  }
}
