using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Spectrum {

  class LEDDomeRaceVisualizer : DomeLayerVisualizer {
    /**
     * Visualizes spinning bands (racers) going around the dome. Each moves at a speed proportional 
     * to volume, beats, constant, or still. See RacerConfig below to configure the bands.
     */
    private readonly LayerRendererRuntime runtime;
    private readonly IAudioLevelInput audio;
    private readonly BeatBroadcaster beat;
    private readonly DomeRenderContext dome;
    private readonly DomeFrame buffer;

    // Static per-pixel geometry, baked once in the constructor: the racer lookup
    // only needs each pixel's angle around the dome and its vertical height, and
    // both are fixed. Precomputing them avoids the four-Tuple-allocating
    // Atan2/Sqrt call (GetProjectedLEDPointParametric) that used to run for every
    // one of the ~4,500 pixels every frame.
    private readonly double[] pixelAngle;
    private readonly double[] pixelHeight;

    public enum Rotation { Still, Constant, Volume, VolumeSquared, Beat, };
    public enum Size { Small, Medium, Large, Full };
    public enum Coloring { Constant, Fade, FadeExp, Multi };
    public class RacerConfig {
      public Rotation rotation;
      public Size size;
      public Coloring coloring;

      public RacerConfig(Rotation r, Size s, Coloring c) {
        rotation = r;
        size = s;
        coloring = c;
      }
    }

    /**
     * The Configuration Settings. Each item is a "racing" band, you can add as many bands 
     * as you would like. The first will always be closest to the ground, and the last will 
     * be at the north pole. The settings for each band:
     * Rotation: How does the racer rotate around the center.
     * Size: How large will it be, small, medium, large, or full (complete circle)
     * Coloring: How it will be colored, multiple colors, fading out, expoenential fade out, or constant.
     */
    public RacerConfig[] racerConfig = {
      new RacerConfig(Rotation.VolumeSquared, Size.Full, Coloring.Multi),
      new RacerConfig(Rotation.VolumeSquared, Size.Medium, Coloring.FadeExp),
      new RacerConfig(Rotation.Beat, Size.Small, Coloring.FadeExp),
      new RacerConfig(Rotation.Constant, Size.Full, Coloring.Multi),
    };

    // How much spacing should be in between each racer. 
    // Determines racer "padding". 
    // 0 means right next to eachother. 1 means they have a size of 0.
    private double racer_spacing = 0;

    private class Racer {
      public double Angle { get; protected set; } // Location of the Racer in Radians, -pi to pi
      public double Radians { get; protected set; } // Length of the Racer in Radians, 0 to 2*pi
      private int idx; // Length of the Racer in Radians, 0 to 2*pi
      private double AccumulatedSeconds;
      private RacerConfig conf;

      public Racer(int idx, RacerConfig racer_config) {
        conf = racer_config;
        double width = conf.size == Size.Small ? 1.0 / 8 :
          conf.size == Size.Medium ? 1.0 / 4 :
          conf.size == Size.Large ? 3.0 / 4 :
          1;
        this.idx = idx;
        Angle = 0;
        Radians = 2 * Math.PI * width;
        AccumulatedSeconds = 0;
      }

      // `speed` is the layer's speed param (formerly the global
      // domeVolumeRotationSpeed knob this visualizer borrowed); `beat` is the
      // live tempo service.
      public void Move(double numSeconds, IAudioLevelInput audio, BeatBroadcaster beat, double speed) {
        // Could use classes for this, but eh.
        double revsPerSec =
          conf.rotation == Rotation.Volume ? RevsPerSecondVolume(audio, speed) :
          conf.rotation == Rotation.VolumeSquared ? RevsPerSecondVolumeSquared(audio, speed) :
          conf.rotation == Rotation.Beat ? RevsPerSecondBeats(audio, beat) :
          conf.rotation == Rotation.Constant ? RevsPerSecondConstant(audio, speed) :
          0;
        double radsPerSecond = Math.PI * 2 * revsPerSec;
        MoveRads(numSeconds, radsPerSecond);
      }

      // `palette` is the layer's chosen named palette; idx and the Multi
      // gradient remain relative color-slot indices within it.
      public int Color(
        DomeRenderContext dome, int palette, double loc_y, double loc_ang
      ) {
        if (conf.coloring == Coloring.Fade) {
          return LEDColor.ScaleColor(
            dome.GetSingleColor(idx, palette), loc_ang);
        } else if (conf.coloring == Coloring.FadeExp) {
          var s = 4 * loc_ang - 4;
          return LEDColor.ScaleColor(
            dome.GetSingleColor(idx, palette),
            1.0 / (1 + Math.Pow(Math.E, -s))
         );
        } else if (conf.coloring == Coloring.Multi) {
          // The palette's top five slots (relative 3-7).
          return dome.GetGradientBetweenColors(
            3, 7, loc_ang, 0.0, false, palette);
        }
        return dome.GetSingleColor(idx, palette);
      }

      private void MoveRads(double numSeconds, double radsPerSecond) {
        double rads = (numSeconds + AccumulatedSeconds) * radsPerSecond;
        if (rads < .0001) {
          // If radians are too small, we may not have enough precision to move anything.
          // Instead, accumulate the movement and try next time.
          AccumulatedSeconds += numSeconds;
          return;
        }
        Angle += rads;
        if (Angle > Math.PI) {
          Angle -= Math.PI * 2;
        }
        AccumulatedSeconds = 0;
      }

      public double RevsPerSecondVolume(IAudioLevelInput audio, double speed) {
        // Square the volume to give a bigger umph.
        return audio.Volume + speed / 12;
      }

      public double RevsPerSecondVolumeSquared(IAudioLevelInput audio, double speed) {
        // Square the volume to give a bigger umph.
        return audio.Volume * audio.Volume + speed / 12;
      }

      public double RevsPerSecondBeats(IAudioLevelInput audio, BeatBroadcaster beat) {
        double BPS =
          // If we don't have a beet counter, fake it as 60 BPM.
          beat.MeasureLength == -1 ? 1 :
          // Taken from the BPM toString method, and multiply by 60 for seconds.
          1000.0 / beat.MeasureLength;
        // Make a full revolution after 4 beats.
        return 1.0 * BPS / 4;
      }
      public double RevsPerSecondConstant(IAudioLevelInput audio, double speed) {
        return 1.0 * speed / 4;
      }
    }

    private Racer[] Racers { get; set; }
    private long lastTicks { get; set; }

    // Out-params instead of a Tuple return: this runs once per lit pixel per
    // frame (~4,500 times), and the old Tuple<Racer,double,double,Racer>
    // allocated a heap object every call (with the racer redundantly stored
    // twice) just to smuggle three values out.
    private bool TryGetRacer(
      double Y,
      double ang,
      [NotNullWhen(true)] out Racer? racer,
      out double locY,
      out double locAng
    ) {
      // e.g., say 2 racers, racer0 centered on .25; racer1 centered on .75.
      // Y = .4. We should return the first racer if in spacing.
      if (Y > .9999) {
        Y = .9999;
      }
      double racer_loc_y = Y * racerConfig.Length; // Scale from 0 to num_racers
      int racer_idx = (int)(racer_loc_y);
      racer_loc_y -= racer_idx; // Scale from 0 to 1, same as
      racer_loc_y -= .5; // Scale from -.5 to .5
      racer_loc_y = Math.Abs(racer_loc_y);
      racer = null;
      locY = 0;
      locAng = 0;
      // racer_spacing is padding between racers: 0 means bands touch (half
      // width .5, full coverage), 1 means a racer has size 0.
      double half_width = (1 - racer_spacing) / 2;
      if (racer_loc_y > half_width) {
        return false;
      }
      Racer r = this.Racers[racer_idx];
      // Offset is how many radians ahead of the start angle.
      double start_angle = r.Angle;
      if (start_angle < 0) {
        start_angle += 2 * Math.PI;
      }
      if (ang < 0) {
        ang += 2 * Math.PI;
      }
      double offset = ang - start_angle;
      if (offset < 0) {
        offset += Math.PI * 2;
      }
      if (offset < 2 * Math.PI - r.Radians) {
        return false;
      }
      // Some more manipulation to get the percentage in the range
      //   offset > 2*Math.PI - r.Radians
      //   r.Radians > 2*Math.PI - offset
      //   1 > (2*Math.PI - offset) / r.Radians
      racer = r;
      locY = racer_loc_y;
      locAng = 1 - (2 * Math.PI - offset) / r.Radians;
      return true;
    }

    public LEDDomeRaceVisualizer(
     LayerRendererRuntime runtime,
     IAudioLevelInput audio,
     BeatBroadcaster beat,
     DomeRenderContext dome
     ) {
      this.runtime = runtime;
      this.audio = audio;
      this.beat = beat;
      this.dome = dome;
      // Create new racers.
      this.Racers = new Racer[racerConfig.Length];
      for (int i = 0; i < racerConfig.Length; i++) {
        var rconf = racerConfig[i];
        this.Racers[i] = new Racer(i, rconf);
      }
      this.lastTicks = -1;
      // This call is necessary to make sure the Operator considers this
      // Visualizer when comparing priorities for LEDDomeOutput. If you skip it
      // your Visualizer will never run
      this.buffer = this.dome.MakeDomeFrame();

      // Bake each pixel's angle and height from its strut/LED identity once.
      this.pixelAngle = new double[this.buffer.pixels.Length];
      this.pixelHeight = new double[this.buffer.pixels.Length];
      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        DomeTopologyPixel point = this.buffer.Topology.PixelAt(i);
        var parametric = StrutLayoutFactory.GetProjectedLEDPointParametric(
          point.StrutIndex,
          point.LedIndex
        );
        this.pixelAngle[i] = parametric.Item3;
        this.pixelHeight[i] = 1.0 - parametric.Item4;
      }
    }

    public int Priority => 2;

    public string LayerKey => "race";
    public DomeFrame LayerBuffer => this.buffer;

    // When the Operator determines that this Visualizer has the highest
    // priority for LEDDomeOutput, it will set the Enabled property below to
    // true. Conversely, when it determines that this Visualizer no longer has
    // the highest priority, it will set it to false. Note that the Visualize
    // method below will be called if and only if Enabled is set to true.

    // If you don't need to do any sort of preparation when your Visualizer is
    // enabled, you can just use the following line to define the property:
    // public bool Enabled { get; set; }
    // Otherwise, if you want to do something special either when being enabled
    // or disabled, use the following code:
    private bool enabled = false;
    public bool Enabled {
      get {
        return this.enabled;
      }
      set {
        if (value == this.enabled) {
          return;
        }
        // Any code that you want to run when the Visualizer is enabled or when
        // it's disabled should go here
        if (value) {
          // Re-enabling after any gap (layer removed from the stack, operator
          // off, etc.) must not let Visualize() integrate the elapsed gap as
          // motion -- MoveRads only unwinds 2*pi per frame, so a large enough
          // gap would leave racers spinning invisibly for many frames.
          this.lastTicks = -1;
        }
        this.enabled = value;
      }
    }

    private Input[]? inputs;
    public Input[] GetInputs() {
      // In order for the Operator to know which Inputs need to be enabled, you
      // should return the ones you are currently using here. If your Visualizer
      // only needs certain Inputs under certain conditions, you can put
      // conditional logic here -- but then drop the cache below, which assumes
      // the returned set is static.
      return this.inputs ?? (this.inputs = new Input[] { this.audio });
    }

    public void Visualize() {
      //-----------------------------------------------------------------------
      // SETTING THE RACERS
      //-----------------------------------------------------------------------

      // This layer's own tuning, read from its compiled runtime snapshot
      // (formerly the shared domeRadialSize / domeVolumeRotationSpeed knobs).
      RaceLayerOptions options = this.runtime.GetOptions<RaceLayerOptions>();
      this.racer_spacing = options.Spacing;
      double speed = options.Speed;
      int selectedPalette = options.Palette;
      var curTicks = DateTime.Now.Ticks;
      if (this.lastTicks == -1) {
        this.lastTicks = curTicks;
      } else {
        double numSeconds = ((double)(curTicks - this.lastTicks)) / (1000 * 1000 * 10);
        foreach (Racer r in Racers) {
          r.Move(numSeconds, audio, this.beat, speed);
        }
        this.lastTicks = curTicks;
      }

      //-----------------------------------------------------------------------
      // SETTING LEDS
      //-----------------------------------------------------------------------

      for (int i = 0; i < this.buffer.pixels.Length; i++) {
        if (!this.TryGetRacer(
            this.pixelHeight[i], this.pixelAngle[i], out Racer? racer,
            out double locY, out double locAng)) {
          // color = 0 is off. Intentionally opaque black (not Clear): a
          // foreground Race layer under Over occludes with black between racers.
          this.buffer.pixels[i].color = 0;
        } else {
          // Let the racer choose its color
          this.buffer.pixels[i].color = racer.Color(
            dome, selectedPalette, locY, locAng);
        }
      }
    }
  }
}
