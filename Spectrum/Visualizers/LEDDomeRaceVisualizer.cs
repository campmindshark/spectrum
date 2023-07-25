using Spectrum.Base;
using Spectrum.Audio;
using Spectrum.MIDI;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;

namespace Spectrum {

  class LEDDomeRaceVisualizer : Visualizer {
    /**
     * Visualizes spinning bands (racers) going around the dome. Each moves at a speed proportional 
     * to volume, beats, constant, or still. See RacerConfig below to configure the bands.
     */
    private readonly Configuration config;
    private readonly AudioInput audio;
    private readonly MidiInput midi;
    private readonly LEDDomeOutput dome;

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
      public double Y { get; protected set; }  // Location in height 1 is top, 0 is bottom.
      public double Angle { get; protected set; } // Location of the Racer in Radians, -pi to pi
      public double Radians { get; protected set; } // Length of the Racer in Radians, 0 to 2*pi
      private int idx; // Length of the Racer in Radians, 0 to 2*pi
      private double AccumulatedSeconds;
      private RacerConfig conf;

      public Racer(int idx, int num_racers, RacerConfig racer_config) {
        conf = racer_config;
        double width = conf.size == Size.Small ? 1.0 / 8 :
          conf.size == Size.Medium ? 1.0 / 4 :
          conf.size == Size.Large ? 3.0 / 4 :
          1;
        this.idx = idx;
        Y = 1.0 * idx / num_racers + .5;
        Angle = 0;
        Radians = 2 * Math.PI * width;
        AccumulatedSeconds = 0;
      }

      public void Move(double numSeconds, AudioInput audio, Configuration config) {
        // Could use classes for this, but eh.
        double revsPerSec =
          conf.rotation == Rotation.Volume ? RevsPerSecondVolume(audio, config) :
          conf.rotation == Rotation.VolumeSquared ? RevsPerSecondVolumeSquared(audio, config) :
          conf.rotation == Rotation.Beat ? RevsPerSecondBeats(audio, config) :
          conf.rotation == Rotation.Constant ? RevsPerSecondConstant(audio, config) :
          0;
        double radsPerSecond = Math.PI * 2 * revsPerSec;
        MoveRads(numSeconds, radsPerSecond);
      }

      public int Color(LEDDomeOutput dome, Configuration config, double loc_y, double loc_ang) {
        if (conf.coloring == Coloring.Fade) {
          return LEDColor.ScaleColor(dome.GetSingleColor(idx), loc_ang);
        } else if (conf.coloring == Coloring.FadeExp) {
          var s = 4 * loc_ang - 4;
          return LEDColor.ScaleColor(
            dome.GetSingleColor(idx),
            1.0 / (1 + Math.Pow(Math.E, -s))
         );
        } else if (conf.coloring == Coloring.Multi) {
          var end_index = config.colorPalette.colors.Length - 1;
          return dome.GetGradientBetweenColors(end_index - 4, end_index, loc_ang, 0.0, false);
        }
        return dome.GetSingleColor(idx);
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

      public double RevsPerSecondVolume(AudioInput audio, Configuration config) {
        // Square the volume to give a bigger umph.
        return audio.Volume + config.domeVolumeRotationSpeed / 12;
      }

      public double RevsPerSecondVolumeSquared(AudioInput audio, Configuration config) {
        // Square the volume to give a bigger umph.
        return audio.Volume * audio.Volume + config.domeVolumeRotationSpeed / 12;
      }

      public double RevsPerSecondBeats(AudioInput audio, Configuration config) {
        double BPS =
          // If we don't have a beet counter, fake it as 60 BPM.
          config.beatBroadcaster.MeasureLength == -1 ? 1 :
          // Taken from the BPM toString method, and multiply by 60 for seconds.
          1000.0 / config.beatBroadcaster.MeasureLength;
        // Make a full revolution after 4 beats.
        return 1.0 * BPS / 4;
      }
      public double RevsPerSecondConstant(AudioInput audio, Configuration config) {
        return 1.0 * config.domeVolumeRotationSpeed / 4;
      }
    }

    private Racer[] Racers { get; set; }
    private long lastTicks { get; set; }

    private Tuple<Racer, double, double, Racer> GetRacer(double Y, double ang) {
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
      if (racer_loc_y > racer_spacing) {
        return null;
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
        return null;
      }
      // Some more manipulation to get the percentage in the range
      //   offset > 2*Math.PI - r.Radians
      //   r.Radians > 2*Math.PI - offset
      //   1 > (2*Math.PI - offset) / r.Radians
      double racer_loc_ang = 1 - (2 * Math.PI - offset) / r.Radians;
      return new Tuple<Racer, double, double, Racer>(r, racer_loc_y, racer_loc_ang, r);
    }

    public LEDDomeRaceVisualizer(
     Configuration config,
     AudioInput audio,
     MidiInput midi,
     LEDDomeOutput dome
     ) {
      this.config = config;
      this.audio = audio;
      this.midi = midi;
      this.dome = dome;
      // Create new racers.
      this.Racers = new Racer[racerConfig.Length];
      for (int i = 0; i < racerConfig.Length; i++) {
        var rconf = racerConfig[i];
        this.Racers[i] = new Racer(i, racerConfig.Length, rconf);
      }
      this.lastTicks = -1;
      // This call is necessary to make sure the Operator considers this
      // Visualizer when comparing priorities for LEDDomeOutput. If you skip it
      // your Visualizer will never run
      this.dome.RegisterVisualizer(this);
    }

    public int Priority {
      get {
        // The mapping between Visualizers and their corresponding domeActiveVis
        // integer value is determined in the condition below, as well as in the
        // Bind call for domeActiveVis in MainWindow.xaml.cs
        if (this.config.domeActiveVis != 2) {
          // By setting the priority to 0, we guarantee that this Visualizer
          // will not run
          return 0;
        }
        // There is a "screensaver" Visualizer with priority 1 associated with
        // LEDDomeOutput (LEDDomeTVStaticVisualizer). The intention is to run
        // that Visualizer when there is no audio input. If you are writing a
        // Visualizer that depends on audio input, consider disabling it when
        // the audio is off using the following condition:
        if (this.audio.isQuiet) {
          return 0;
        }
        // You can return any number higher than 1 here to make sure this
        // Visualizer runs and the screensaver Visualizer doesn't
        return 2;
      }
    }

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
        this.enabled = value;
      }
    }

    public Input[] GetInputs() {
      // In order for the Operator to know which Inputs need to be enabled, you
      // should return the ones you are currently using here. If your Visualizer
      // only needs certain Inputs under certain conditions, you can put
      // conditional logic here
      return new Input[] { this.audio };
    }

    public void Visualize() {
      //-----------------------------------------------------------------------
      // SETTING THE RACERS
      //-----------------------------------------------------------------------

      this.racer_spacing = this.config.domeRadialSize > 1 ? 1.0 : this.config.domeRadialSize;
      var curTicks = DateTime.Now.Ticks;
      if (this.lastTicks == -1) {
        this.lastTicks = curTicks;
      } else {
        double numSeconds = ((double)(curTicks - this.lastTicks)) / (1000 * 1000 * 10);
        foreach (Racer r in Racers) {
          r.Move(numSeconds, audio, config);
        }
        this.lastTicks = curTicks;
      }

      //-----------------------------------------------------------------------
      // SETTING LEDS
      //-----------------------------------------------------------------------

      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        Strut strut = Strut.FromIndex(this.config, i);
        var leds = LEDDomeOutput.GetNumLEDs(i);
        for (int j = 0; j < leds; j++) {
          var pixel_p = StrutLayoutFactory.GetProjectedLEDPointParametric(i, j);
          // Vertical height
          var y = 1.0 - pixel_p.Item4;
          var rad = pixel_p.Item3;
          Tuple<Racer, double, double, Racer> loc = this.GetRacer(y, rad);
          if (loc == null) {
            // color = 0 is off.
            this.dome.SetPixel(i, j, 0);
          } else {
            // Let the racer choose its color
            var color = loc.Item4.Color(dome, config, loc.Item2, loc.Item3);
            this.dome.SetPixel(i, j, color);
          }
        }
      }

      // No messages will be sent to the Beaglebone until Flush is called. This
      // makes it imperative that you call Flush at the end of Visualize, or
      // whenever you want to update the LEDs. The LEDs themselves are stateful,
      // and will maintain whatever color you set them to until they lose power.
      this.dome.Flush();
    }
  }
}
