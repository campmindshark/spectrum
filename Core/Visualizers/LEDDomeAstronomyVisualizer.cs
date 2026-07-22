using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;

namespace Spectrum.Visualizers {

  // A low-frequency, approximate planetarium for the LED dome. The dome's
  // center is the zenith and its rim is the horizon. Midnight on the selected
  // Black Rock City date plus the time scrubber places the Sun, Moon, Mercury,
  // Venus, and Mars; Earth is the observer. A small real bright-star catalog and a
  // deterministic faint-star field make the rest of the hemisphere read as
  // sky rather than isolated dots.
  //
  // North Heading is the physical calibration: 0 degrees puts north along the
  // projected dome's +Y axis, and increasing values rotate north clockwise.
  // Orbital elements are intentionally low precision. This is a lighting look,
  // not a navigation instrument, but the bodies rise, set, and move through the
  // seasons in the right parts of the sky.
  class LEDDomeAstronomyVisualizer : DomeLayerVisualizer {

    private const int NightSkyColor = 0x000006;
    private const int DaySkyColor = 0x082040;
    internal const double PlaybackHoursPerSecond = 1;
    internal const double PlaybackRangeHours = 168;
    internal const double MaxPlaybackSpeed = 8;
    internal const double MinInterpolationFramesPerSecond = 10;
    internal const double MaxInterpolationFramesPerSecond = 60;

    private readonly LayerRendererRuntime runtime;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;
    private DomeFrame interpolationStart;
    private DomeFrame interpolationEnd;
    private readonly ImmutableArray<Vector3> pixelPositions;
    private readonly LayerTrigger playTrigger;
    private readonly Stopwatch playbackClock = new Stopwatch();

    // Idle astronomy changes slowly, while playback benefits from visibly
    // smooth motion. Throttle the expensive sky rebuild to 1 FPS when idle and
    // 10 FPS at normal playback. Above 1x, the keyframe rate ramps to 60 FPS at
    // 8x, and adjacent sky keyframes are blended on every engine frame.
    private long lastRenderBucket = long.MinValue;
    private AstronomyLayerOptions lastOptions;
    private int configuredStartDate = int.MinValue;
    private double configuredTimeOffset = double.NaN;
    private double configuredPlaybackSpeed = double.NaN;
    private double playbackStartOffset;
    private bool playbackActive;
    private bool resumeFromStoppedOffset;
    private DateTime playbackAnchorUtc;
    private long interpolationSegment = long.MinValue;
    private AstronomyLayerOptions interpolationOptions;

    public LEDDomeAstronomyVisualizer(
      DomeLayerEnvironment environment,
      LayerRendererRuntime runtime,
      DomeRenderContext dome
    ) {
      this.runtime = runtime;
      this.dome = dome;
      this.buffer = this.dome.MakeDomeFrame();
      this.interpolationStart = new DomeFrame(this.buffer.Topology);
      this.interpolationEnd = new DomeFrame(this.buffer.Topology);
      this.pixelPositions = this.buffer.BakePixelPositions();
      this.playTrigger = new LayerTrigger(
        environment, null, runtime.InstanceId);
    }

    public int Priority => 2;

    public string LayerKey => "astronomy";
    public DomeFrame LayerBuffer => this.buffer;
    public bool Enabled { get; set; }
    internal bool PlaybackActive => this.playbackActive;
    internal double PlaybackStartOffset => this.playbackStartOffset;

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = Array.Empty<Input>());
    }

    public void Visualize() {
      AstronomyLayerOptions options =
        this.runtime.GetOptions<AstronomyLayerOptions>();
      DateTime utc = DateTime.UtcNow;
      bool stopRequested = this.playTrigger.Cleared();

      bool startDateChanged = options.StartDate != this.configuredStartDate;
      bool sliderChanged = options.TimeOffsetHours != this.configuredTimeOffset;
      bool timelineChanged = startDateChanged || sliderChanged;
      if (timelineChanged) {
        this.configuredStartDate = options.StartDate;
        this.configuredTimeOffset = options.TimeOffsetHours;
        this.playbackStartOffset = options.TimeOffsetHours;
        this.resumeFromStoppedOffset = false;
        if (this.playbackActive) {
          this.RestartPlaybackClock(utc);
        }
      }

      bool speedChanged =
        options.PlaybackSpeed != this.configuredPlaybackSpeed;
      if (speedChanged) {
        // Rebase an active run at its current position before adopting the new
        // rate, so dragging the speed slider does not jump the simulated time.
        if (this.playbackActive &&
            !double.IsNaN(this.configuredPlaybackSpeed)) {
          this.playbackStartOffset = PlaybackOffset(
            this.playbackStartOffset,
            this.playbackClock.Elapsed.TotalSeconds,
            this.configuredPlaybackSpeed,
            options.Loop,
            out bool completedAtOldSpeed);
          this.playbackActive = !completedAtOldSpeed;
          this.resumeFromStoppedOffset = false;
          if (this.playbackActive) {
            this.RestartPlaybackClock(utc);
          } else {
            this.playbackClock.Stop();
          }
        }
        this.configuredPlaybackSpeed = options.PlaybackSpeed;
        this.interpolationSegment = long.MinValue;
      }

      bool playRequested = this.playTrigger.Fired(0);
      if (playRequested) {
        // A stopped run resumes exactly where Stop froze it. Otherwise Play
        // starts from the slider's selected position, so pressing it during an
        // active run remains a deterministic restart.
        if (!this.resumeFromStoppedOffset) {
          this.playbackStartOffset = options.TimeOffsetHours;
        }
        this.playbackActive = true;
        this.resumeFromStoppedOffset = false;
        this.RestartPlaybackClock(utc);
      }

      double effectiveOffset = this.playbackStartOffset;
      if (this.playbackActive) {
        effectiveOffset = PlaybackOffset(
          this.playbackStartOffset,
          this.playbackClock.Elapsed.TotalSeconds,
          options.PlaybackSpeed,
          options.Loop,
          out bool completed);
        if (completed) {
          this.playbackActive = false;
          this.playbackClock.Stop();
          this.playbackStartOffset = effectiveOffset;
          this.resumeFromStoppedOffset = false;
        }
      }
      if (stopRequested && this.playbackActive) {
        this.playbackActive = false;
        this.playbackClock.Stop();
        this.playbackStartOffset = effectiveOffset;
        this.resumeFromStoppedOffset = true;
        this.interpolationSegment = long.MinValue;
      }

      if (this.playbackActive &&
          UsesPlaybackInterpolation(options.PlaybackSpeed)) {
        this.RenderInterpolatedPlayback(options);
        // Force a direct render when playback ends or returns to 1x/below.
        this.lastRenderBucket = long.MinValue;
        this.lastOptions = options;
        return;
      }

      long bucketSize = this.playbackActive
        ? TimeSpan.TicksPerSecond / 10
        : TimeSpan.TicksPerSecond;
      long renderBucket = utc.Ticks / bucketSize;
      if (renderBucket == this.lastRenderBucket &&
          options == this.lastOptions &&
          !playRequested && !stopRequested &&
          !timelineChanged && !speedChanged) {
        return;
      }
      this.lastRenderBucket = renderBucket;
      this.lastOptions = options;
      this.RenderAt(utc, options, effectiveOffset);
    }

    private void RestartPlaybackClock(DateTime utc) {
      this.playbackAnchorUtc = utc;
      this.playbackClock.Restart();
      this.interpolationSegment = long.MinValue;
    }

    private void RenderInterpolatedPlayback(AstronomyLayerOptions options) {
      double elapsed = this.playbackClock.Elapsed.TotalSeconds;
      double keyframeSeconds = 1 /
        InterpolationFramesPerSecond(options.PlaybackSpeed);
      double segmentPosition = elapsed / keyframeSeconds;
      long segment = (long)Math.Floor(segmentPosition);
      double amount = segmentPosition - segment;

      if (segment != this.interpolationSegment ||
          options != this.interpolationOptions) {
        DateTime segmentUtc = this.playbackAnchorUtc.AddSeconds(
          segment * keyframeSeconds);
        bool advanceCachedFrame =
          this.interpolationSegment != long.MinValue &&
          segment == this.interpolationSegment + 1 &&
          options == this.interpolationOptions;

        if (advanceCachedFrame) {
          DomeFrame swap = this.interpolationStart;
          this.interpolationStart = this.interpolationEnd;
          this.interpolationEnd = swap;
        } else {
          double startOffset = PlaybackOffset(
            this.playbackStartOffset,
            segment * keyframeSeconds,
            options.PlaybackSpeed,
            options.Loop,
            out _);
          this.RenderAt(
            segmentUtc, options, startOffset, this.interpolationStart);
        }

        double endOffset = PlaybackOffset(
          this.playbackStartOffset,
          (segment + 1) * keyframeSeconds,
          options.PlaybackSpeed,
          options.Loop,
          out _);
        this.RenderAt(
          segmentUtc.AddSeconds(keyframeSeconds),
          options, endOffset, this.interpolationEnd);
        this.interpolationSegment = segment;
        this.interpolationOptions = options;
      }

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        this.buffer.pixels[i].color = InterpolateColor(
          this.interpolationStart.pixels[i].color,
          this.interpolationEnd.pixels[i].color,
          amount);
      }
    }

    // Kept internal so the regression runner can render a stable instant.
    internal void RenderAt(DateTime utc, AstronomyLayerOptions options) {
      this.RenderAt(utc, options, options.TimeOffsetHours);
    }

    private void RenderAt(
      DateTime utc,
      AstronomyLayerOptions options,
      double effectiveOffsetHours
    ) => this.RenderAt(utc, options, effectiveOffsetHours, this.buffer);

    private void RenderAt(
      DateTime utc,
      AstronomyLayerOptions options,
      double effectiveOffsetHours,
      DomeFrame target
    ) {
      DateTime selectedUtc = AstronomySky.StartDateUtc(
        options.StartDate, utc).AddHours(effectiveOffsetHours);
      double julianDay = AstronomySky.JulianDay(selectedUtc);
      AstronomyBody[] bodies = AstronomySky.Bodies(julianDay);

      Vector3 sunLocal = AstronomySky.ToBlackRockHorizontal(
        bodies[0].Equatorial, julianDay);
      // Civil/nautical twilight crossfade: stars reach full strength once the
      // Sun is roughly twelve degrees below the horizon.
      double night = Clamp((-sunLocal.Z - 0.04) / 0.18, 0, 1);
      int skyColor = SkyColor(
        night, options.ShowDaytimeSky, options.ShowNighttimeSky);
      for (int i = 0; i < target.pixels.Length; i++) {
        target.pixels[i].color = skyColor;
      }

      // Stars remain tied to the celestial sphere and therefore share the same
      // sidereal rotation, observer transform, and heading calibration as the
      // Solar System bodies.
      if (StarsVisible(night, options.ShowNighttimeSky)) {
        foreach (AstronomyStar star in AstronomySky.Stars) {
          Vector3 local = AstronomySky.ToBlackRockHorizontal(
            star.Equatorial, julianDay);
          if (local.Z <= 0) {
            continue;
          }
          Vector3 center = AstronomySky.ToDome(
            local, options.NorthHeading);
          this.PaintSoftDisc(
            target, center, star.Radius, star.Color,
            star.Brightness * night);
        }
      }

      foreach (AstronomyBody body in bodies) {
        Vector3 local = AstronomySky.ToBlackRockHorizontal(
          body.Equatorial, julianDay);
        // Let a disc clip naturally against the rim while rising or setting.
        if (local.Z < -Math.Sin(body.Radius)) {
          continue;
        }
        Vector3 center = AstronomySky.ToDome(
          local, options.NorthHeading);
        this.PaintSoftDisc(
          target, center, body.Radius, body.Color, body.Brightness);
      }
    }

    internal static double PlaybackOffset(
      double startOffsetHours,
      double elapsedSeconds,
      double playbackSpeed,
      bool loop,
      out bool completed
    ) {
      double offset = startOffsetHours +
        elapsedSeconds * PlaybackHoursPerSecond * playbackSpeed;
      if (loop) {
        completed = false;
        offset %= PlaybackRangeHours;
        return offset < 0 ? offset + PlaybackRangeHours : offset;
      }
      completed = offset >= PlaybackRangeHours;
      return completed ? PlaybackRangeHours : offset;
    }

    internal static bool UsesPlaybackInterpolation(double playbackSpeed) =>
      playbackSpeed > 1;

    internal static double InterpolationFramesPerSecond(
      double playbackSpeed
    ) {
      double amount = Clamp(
        (playbackSpeed - 1) / (MaxPlaybackSpeed - 1), 0, 1);
      return MinInterpolationFramesPerSecond + amount *
        (MaxInterpolationFramesPerSecond -
          MinInterpolationFramesPerSecond);
    }

    internal static int InterpolateColor(
      int start, int end, double amount
    ) => MixColor(start, end, amount);

    internal static int SkyColor(
      double night, bool showDaytimeSky, bool showNighttimeSky
    ) => MixColor(
      showDaytimeSky ? DaySkyColor : 0,
      showNighttimeSky ? NightSkyColor : 0,
      night);

    internal static bool StarsVisible(
      double night, bool showNighttimeSky
    ) => showNighttimeSky && night > 0.01;

    private void PaintSoftDisc(
      DomeFrame target,
      Vector3 center,
      double radius,
      int color,
      double brightness
    ) {
      double outerCos = Math.Cos(radius * 1.55);
      double span = 1 - outerCos;
      for (int i = 0; i < this.pixelPositions.Length; i++) {
        double dot = Vector3.Dot(this.pixelPositions[i], center);
        if (dot <= outerCos) {
          continue;
        }
        double value = brightness * Math.Sqrt((dot - outerCos) / span);
        int painted = ScaleColor(color, value);
        target.pixels[i].color = MaxChannels(
          target.pixels[i].color, painted);
      }
    }

    private static int ScaleColor(int color, double value) {
      value = Clamp(value, 0, 1);
      int r = (int)(((color >> 16) & 0xFF) * value);
      int g = (int)(((color >> 8) & 0xFF) * value);
      int b = (int)((color & 0xFF) * value);
      return (r << 16) | (g << 8) | b;
    }

    private static int MixColor(int a, int b, double amount) {
      amount = Clamp(amount, 0, 1);
      int r = (int)(((a >> 16) & 0xFF) * (1 - amount) +
        ((b >> 16) & 0xFF) * amount);
      int g = (int)(((a >> 8) & 0xFF) * (1 - amount) +
        ((b >> 8) & 0xFF) * amount);
      int blue = (int)((a & 0xFF) * (1 - amount) +
        (b & 0xFF) * amount);
      return (r << 16) | (g << 8) | blue;
    }

    private static int MaxChannels(int a, int b) =>
      (Math.Max((a >> 16) & 0xFF, (b >> 16) & 0xFF) << 16) |
      (Math.Max((a >> 8) & 0xFF, (b >> 8) & 0xFF) << 8) |
      Math.Max(a & 0xFF, b & 0xFF);

    private static double Clamp(double value, double min, double max) =>
      value < min ? min : (value > max ? max : value);
  }

  internal readonly record struct AstronomyBody(
    string Name,
    Vector3 Equatorial,
    double Radius,
    int Color,
    double Brightness
  );

  internal readonly record struct AstronomyStar(
    Vector3 Equatorial,
    double Radius,
    int Color,
    double Brightness
  );

  // Low-precision celestial mechanics and coordinate transforms. All public
  // vectors are normalized: equatorial uses +X at RA 0, +Y at RA 6h, +Z at the
  // north celestial pole; horizontal uses (+X east, +Y north, +Z up); dome uses
  // the topology's (+X right, +Y projected up, +Z zenith) frame.
  internal static class AstronomySky {

    private const double DegreesToRadians = Math.PI / 180;
    internal const double BlackRockCityLatitude = 40.7864;
    internal const double BlackRockCityLongitude = -119.2065;
    internal const string BlackRockTimeZoneId =
      DomeLayerDate.PacificTimeZoneId;

    private readonly record struct OrbitalElements(
      double AscendingNode,
      double Inclination,
      double Perihelion,
      double SemiMajorAxis,
      double Eccentricity,
      double MeanAnomaly
    );

    internal static IReadOnlyList<AstronomyStar> Stars { get; } = BuildStars();

    internal static double JulianDay(DateTime time) {
      DateTime utc = time.Kind == DateTimeKind.Utc
        ? time : time.ToUniversalTime();
      return 2440587.5 + (utc - DateTime.UnixEpoch).TotalDays;
    }

    internal static DateTime StartDateUtc(
      int encodedDate, DateTime referenceUtc
    ) {
      int resolvedDate = encodedDate != 0
        ? encodedDate
        : DomeLayerDate.CurrentDate(referenceUtc, BlackRockTimeZoneId);
      return DomeLayerDate.MidnightUtc(
        resolvedDate, BlackRockTimeZoneId);
    }

    internal static AstronomyBody[] Bodies(double julianDay) {
      // The compact orbital-element set is expressed in days since 2000 Jan
      // 0.0. Accuracy is on the order of arcminutes for the planets and a little
      // looser for the Moon, far below the LED dome's angular resolution.
      double d = julianDay - 2451543.5;
      Vector3 sunEcliptic = SunEcliptic(d);
      Vector3 mercuryEcliptic = PlanetEcliptic(Mercury(d)) + sunEcliptic;
      Vector3 venusEcliptic = PlanetEcliptic(Venus(d)) + sunEcliptic;
      Vector3 marsEcliptic = PlanetEcliptic(Mars(d)) + sunEcliptic;
      Vector3 moonEcliptic = MoonEcliptic(d);

      Vector3 sun = EclipticToEquatorial(sunEcliptic, d);
      Vector3 moon = EclipticToEquatorial(moonEcliptic, d);
      double moonPhase = (1 - Vector3.Dot(sun, moon)) * 0.5;
      double moonBrightness = 0.18 + 0.82 * Math.Sqrt(
        Math.Clamp(moonPhase, 0, 1));

      return new[] {
        new AstronomyBody("Sun", sun, 0.075, 0xFFF0A0, 1),
        new AstronomyBody("Moon", moon, 0.060, 0xDCE9FF, moonBrightness),
        new AstronomyBody(
          "Mercury", EclipticToEquatorial(mercuryEcliptic, d),
          0.028, 0xB8AAA0, 0.72),
        new AstronomyBody(
          "Venus", EclipticToEquatorial(venusEcliptic, d),
          0.038, 0xFFF4C5, 1),
        new AstronomyBody(
          "Mars", EclipticToEquatorial(marsEcliptic, d),
          0.034, 0xFF5A2A, 0.90),
      };
    }

    internal static Vector3 ToHorizontal(
      Vector3 equatorial,
      double julianDay,
      double latitudeDegrees,
      double longitudeDegrees
    ) {
      double t = (julianDay - 2451545.0) / 36525.0;
      double siderealDegrees = 280.46061837 +
        360.98564736629 * (julianDay - 2451545.0) +
        0.000387933 * t * t - t * t * t / 38710000.0 +
        longitudeDegrees;
      double sidereal = NormalizeDegrees(siderealDegrees) * DegreesToRadians;
      double latitude = latitudeDegrees * DegreesToRadians;
      double sinSidereal = Math.Sin(sidereal);
      double cosSidereal = Math.Cos(sidereal);
      double sinLatitude = Math.Sin(latitude);
      double cosLatitude = Math.Cos(latitude);

      // cos(dec) * cos(hour angle) and cos(dec) * sin(hour angle), expanded
      // directly from the equatorial vector to avoid an atan2/asin pair.
      double cosDecCosHour =
        equatorial.X * cosSidereal + equatorial.Y * sinSidereal;
      double cosDecSinHour =
        equatorial.X * sinSidereal - equatorial.Y * cosSidereal;
      var local = new Vector3(
        (float)-cosDecSinHour,
        (float)(cosLatitude * equatorial.Z -
          sinLatitude * cosDecCosHour),
        (float)(sinLatitude * equatorial.Z +
          cosLatitude * cosDecCosHour));
      return Vector3.Normalize(local);
    }

    internal static Vector3 ToBlackRockHorizontal(
      Vector3 equatorial,
      double julianDay
    ) => ToHorizontal(
      equatorial, julianDay,
      BlackRockCityLatitude, BlackRockCityLongitude);

    internal static Vector3 ToDome(
      Vector3 eastNorthUp,
      double northHeadingDegrees
    ) {
      double heading = northHeadingDegrees * DegreesToRadians;
      double sinHeading = Math.Sin(heading);
      double cosHeading = Math.Cos(heading);
      var dome = new Vector3(
        (float)(eastNorthUp.Y * sinHeading +
          eastNorthUp.X * cosHeading),
        (float)(eastNorthUp.Y * cosHeading -
          eastNorthUp.X * sinHeading),
        eastNorthUp.Z);
      return Vector3.Normalize(dome);
    }

    private static Vector3 SunEcliptic(double d) {
      double perihelion = 282.9404 + 4.70935e-5 * d;
      double eccentricity = 0.016709 - 1.151e-9 * d;
      double meanAnomaly = 356.0470 + 0.9856002585 * d;
      double eccentricAnomaly = SolveEccentricAnomaly(
        meanAnomaly, eccentricity);
      double x = Math.Cos(eccentricAnomaly) - eccentricity;
      double y = Math.Sqrt(1 - eccentricity * eccentricity) *
        Math.Sin(eccentricAnomaly);
      double trueAnomaly = Math.Atan2(y, x);
      double radius = Math.Sqrt(x * x + y * y);
      double longitude = trueAnomaly + perihelion * DegreesToRadians;
      return new Vector3(
        (float)(radius * Math.Cos(longitude)),
        (float)(radius * Math.Sin(longitude)), 0);
    }

    private static OrbitalElements Mercury(double d) => new(
      48.3313 + 3.24587e-5 * d,
      7.0047 + 5.00e-8 * d,
      29.1241 + 1.01444e-5 * d,
      0.387098,
      0.205635 + 5.59e-10 * d,
      168.6562 + 4.0923344368 * d);

    private static OrbitalElements Venus(double d) => new(
      76.6799 + 2.46590e-5 * d,
      3.3946 + 2.75e-8 * d,
      54.8910 + 1.38374e-5 * d,
      0.723330,
      0.006773 - 1.302e-9 * d,
      48.0052 + 1.6021302244 * d);

    private static OrbitalElements Mars(double d) => new(
      49.5574 + 2.11081e-5 * d,
      1.8497 - 1.78e-8 * d,
      286.5016 + 2.92961e-5 * d,
      1.523688,
      0.093405 + 2.516e-9 * d,
      18.6021 + 0.5240207766 * d);

    private static Vector3 PlanetEcliptic(OrbitalElements elements) {
      double node = elements.AscendingNode * DegreesToRadians;
      double inclination = elements.Inclination * DegreesToRadians;
      double perihelion = elements.Perihelion * DegreesToRadians;
      double eccentricAnomaly = SolveEccentricAnomaly(
        elements.MeanAnomaly, elements.Eccentricity);
      double x = elements.SemiMajorAxis *
        (Math.Cos(eccentricAnomaly) - elements.Eccentricity);
      double y = elements.SemiMajorAxis *
        Math.Sqrt(1 - elements.Eccentricity * elements.Eccentricity) *
        Math.Sin(eccentricAnomaly);
      double trueAnomaly = Math.Atan2(y, x);
      double radius = Math.Sqrt(x * x + y * y);
      double longitude = trueAnomaly + perihelion;
      double cosNode = Math.Cos(node), sinNode = Math.Sin(node);
      double cosLongitude = Math.Cos(longitude);
      double sinLongitude = Math.Sin(longitude);
      double cosInclination = Math.Cos(inclination);
      double sinInclination = Math.Sin(inclination);
      return new Vector3(
        (float)(radius * (cosNode * cosLongitude -
          sinNode * sinLongitude * cosInclination)),
        (float)(radius * (sinNode * cosLongitude +
          cosNode * sinLongitude * cosInclination)),
        (float)(radius * sinLongitude * sinInclination));
    }

    private static Vector3 MoonEcliptic(double d) {
      double nodeDegrees = 125.1228 - 0.0529538083 * d;
      double inclinationDegrees = 5.1454;
      double perihelionDegrees = 318.0634 + 0.1643573223 * d;
      double meanAnomalyDegrees = 115.3654 + 13.0649929509 * d;
      var elements = new OrbitalElements(
        nodeDegrees, inclinationDegrees, perihelionDegrees,
        60.2666, 0.054900, meanAnomalyDegrees);
      Vector3 unperturbed = PlanetEcliptic(elements);

      double longitude = Math.Atan2(unperturbed.Y, unperturbed.X) /
        DegreesToRadians;
      double latitude = Math.Atan2(
        unperturbed.Z,
        Math.Sqrt(unperturbed.X * unperturbed.X +
          unperturbed.Y * unperturbed.Y)) / DegreesToRadians;

      double sunMean = NormalizeDegrees(356.0470 + 0.9856002585 * d);
      double sunPerihelion = NormalizeDegrees(282.9404 + 4.70935e-5 * d);
      double sunLongitude = NormalizeDegrees(sunMean + sunPerihelion);
      double moonMean = NormalizeDegrees(meanAnomalyDegrees);
      double moonLongitude = NormalizeDegrees(
        moonMean + perihelionDegrees + nodeDegrees);
      double elongation = NormalizeDegrees(moonLongitude - sunLongitude);
      double argumentLatitude = NormalizeDegrees(moonLongitude - nodeDegrees);

      longitude +=
        -1.274 * SinDegrees(moonMean - 2 * elongation) +
         0.658 * SinDegrees(2 * elongation) -
         0.186 * SinDegrees(sunMean) -
         0.059 * SinDegrees(2 * moonMean - 2 * elongation) -
         0.057 * SinDegrees(moonMean - 2 * elongation + sunMean) +
         0.053 * SinDegrees(moonMean + 2 * elongation) +
         0.046 * SinDegrees(2 * elongation - sunMean) +
         0.041 * SinDegrees(moonMean - sunMean) -
         0.035 * SinDegrees(elongation) -
         0.031 * SinDegrees(moonMean + sunMean) -
         0.015 * SinDegrees(2 * argumentLatitude - 2 * elongation) +
         0.011 * SinDegrees(moonMean - 4 * elongation);
      latitude +=
        -0.173 * SinDegrees(argumentLatitude - 2 * elongation) -
         0.055 * SinDegrees(
           moonMean - argumentLatitude - 2 * elongation) -
         0.046 * SinDegrees(
           moonMean + argumentLatitude - 2 * elongation) +
         0.033 * SinDegrees(argumentLatitude + 2 * elongation) +
         0.017 * SinDegrees(2 * moonMean + argumentLatitude);

      double longitudeRadians = longitude * DegreesToRadians;
      double latitudeRadians = latitude * DegreesToRadians;
      double cosLatitude = Math.Cos(latitudeRadians);
      return new Vector3(
        (float)(cosLatitude * Math.Cos(longitudeRadians)),
        (float)(cosLatitude * Math.Sin(longitudeRadians)),
        (float)Math.Sin(latitudeRadians));
    }

    private static Vector3 EclipticToEquatorial(Vector3 ecliptic, double d) {
      double obliquity = (23.4393 - 3.563e-7 * d) * DegreesToRadians;
      double cosObliquity = Math.Cos(obliquity);
      double sinObliquity = Math.Sin(obliquity);
      var equatorial = new Vector3(
        ecliptic.X,
        (float)(ecliptic.Y * cosObliquity -
          ecliptic.Z * sinObliquity),
        (float)(ecliptic.Y * sinObliquity +
          ecliptic.Z * cosObliquity));
      return Vector3.Normalize(equatorial);
    }

    private static double SolveEccentricAnomaly(
      double meanAnomalyDegrees,
      double eccentricity
    ) {
      double mean = NormalizeDegrees(meanAnomalyDegrees) * DegreesToRadians;
      double eccentric = mean + eccentricity * Math.Sin(mean) *
        (1 + eccentricity * Math.Cos(mean));
      for (int i = 0; i < 4; i++) {
        eccentric -= (eccentric - eccentricity * Math.Sin(eccentric) - mean) /
          (1 - eccentricity * Math.Cos(eccentric));
      }
      return eccentric;
    }

    private static IReadOnlyList<AstronomyStar> BuildStars() {
      var stars = new List<AstronomyStar>();
      // The brightest, most recognizable stars anchor the otherwise procedural
      // field. Coordinates are intentionally rounded; one dome pixel is much
      // wider than the rounding error.
      AddStar(stars, 6.75, -16.7, 1.00); // Sirius
      AddStar(stars, 6.40, -52.7, 0.95); // Canopus
      AddStar(stars, 14.26, 19.2, 0.92); // Arcturus
      AddStar(stars, 18.62, 38.8, 0.92); // Vega
      AddStar(stars, 5.28, 46.0, 0.88); // Capella
      AddStar(stars, 5.24, -8.2, 0.86); // Rigel
      AddStar(stars, 7.66, 5.2, 0.82); // Procyon
      AddStar(stars, 5.92, 7.4, 0.80); // Betelgeuse
      AddStar(stars, 19.85, 8.9, 0.79); // Altair
      AddStar(stars, 4.60, 16.5, 0.74); // Aldebaran
      AddStar(stars, 16.49, -26.4, 0.74); // Antares
      AddStar(stars, 13.42, -11.2, 0.72); // Spica
      AddStar(stars, 7.76, 28.0, 0.70); // Pollux
      AddStar(stars, 22.96, -29.6, 0.70); // Fomalhaut
      AddStar(stars, 20.69, 45.3, 0.68); // Deneb
      AddStar(stars, 10.14, 12.0, 0.66); // Regulus
      AddStar(stars, 7.58, 31.9, 0.62); // Castor
      AddStar(stars, 5.42, 6.3, 0.58); // Bellatrix
      AddStar(stars, 5.44, 28.6, 0.56); // Elnath
      AddStar(stars, 5.60, -1.2, 0.54); // Alnilam

      var random = new Random(0xA57);
      for (int i = 0; i < 88; i++) {
        double rightAscension = random.NextDouble() * 2 * Math.PI;
        double z = 2 * random.NextDouble() - 1;
        double radius = Math.Sqrt(1 - z * z);
        double brightness = 0.12 + 0.42 * Math.Pow(random.NextDouble(), 3);
        stars.Add(new AstronomyStar(
          new Vector3(
            (float)(radius * Math.Cos(rightAscension)),
            (float)(radius * Math.Sin(rightAscension)),
            (float)z),
          0.009 + brightness * 0.014,
          i % 5 == 0 ? 0xAFC8FF : 0xFFFFFF,
          brightness));
      }
      return stars.ToArray();
    }

    private static void AddStar(
      List<AstronomyStar> stars,
      double rightAscensionHours,
      double declinationDegrees,
      double brightness
    ) {
      double rightAscension = rightAscensionHours * 15 * DegreesToRadians;
      double declination = declinationDegrees * DegreesToRadians;
      double cosDeclination = Math.Cos(declination);
      stars.Add(new AstronomyStar(
        new Vector3(
          (float)(cosDeclination * Math.Cos(rightAscension)),
          (float)(cosDeclination * Math.Sin(rightAscension)),
          (float)Math.Sin(declination)),
        0.013 + brightness * 0.016,
        0xF5F7FF,
        brightness));
    }

    private static double SinDegrees(double value) =>
      Math.Sin(value * DegreesToRadians);

    private static double NormalizeDegrees(double value) {
      value %= 360;
      return value < 0 ? value + 360 : value;
    }
  }
}
