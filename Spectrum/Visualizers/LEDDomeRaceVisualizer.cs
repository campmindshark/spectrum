using Spectrum.Base;
using Spectrum.Audio;
using Spectrum.MIDI;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;

namespace Spectrum
{

  class LEDDomeRaceVisualizer : Visualizer
  {
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
     * The Configuration Settings. Each item is an can add as many bands as you would like.
     * The first will always be closest to the ground, and the last will be at the north pole.
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
    // Determines racer size. 
    // 0 means right next to eachother. 1 means they have a size of 0.
    private double racer_spacing = 0;

    private class Racer
    {
      public double Y { get; protected set; }  // Location in height 1 is top, 0 is bottom.
      public double Angle { get; protected set; } // Location of the Racer in Radians, -pi to pi
      public double Radians { get; protected set; } // Length of the Racer in Radians, 0 to 2*pi
      private int idx; // Length of the Racer in Radians, 0 to 2*pi
      private double AccumulatedSeconds;
      private RacerConfig conf;
      public Racer(int idx, int num_racers, RacerConfig racer_config)
      {
        conf = racer_config;
        double width = conf.size == Size.Small ? 1.0 / 8: 
          conf.size == Size.Medium ? 1.0 / 4:
          conf.size == Size.Large ? 3.0 / 4:
          1;
        this.Y = 1.0 * idx / num_racers + .5;
        this.Angle = 0;
        this.Radians = 2*Math.PI * width;
        AccumulatedSeconds = 0;
      }
      public void Move(double numSeconds, AudioInput audio, Configuration config) {
        // Could use classes for this, but eh.
        double revsPerSec = 
          conf.rotation == Rotation.Volume ? RevsPerSecondVolume(audio, config):
          conf.rotation == Rotation.VolumeSquared ? RevsPerSecondVolumeSquared(audio, config):
          conf.rotation == Rotation.Beat ? RevsPerSecondBeats(audio, config):
          conf.rotation == Rotation.Constant ? RevsPerSecondConstant(audio, config):
          0;
        double radsPerSecond = Math.PI * 2 * revsPerSec;
        MoveRads(numSeconds, radsPerSecond);
      }
      public int Color(LEDDomeOutput dome, Configuration config, double loc_y, double loc_ang) {
        if(conf.coloring == Coloring.Fade) {
          return dome.GetSingleColor(this.idx, loc_ang);
        } else if(conf.coloring == Coloring.FadeExp) {
          var s = 4*loc_ang - 4;
          return dome.GetSingleColor(this.idx, 1.0 / (1 + Math.Pow(Math.E, -s)));
        } else if(conf.coloring == Coloring.Multi) {
          var end_index = config.colorPalette.colors.Length - 1;
          return dome.GetGradientBetweenColors(end_index - 4, end_index, loc_ang, 0.0, false);
        }
        return dome.GetSingleColor(this.idx);
      }

      private void MoveRads(double numSeconds, double radsPerSecond) {
        double rads = (numSeconds + AccumulatedSeconds) * radsPerSecond;
        if(rads < .0001) {
          // If radians are too small, we may not have enough precision to move anything.
          // Instead, accumulate the movement and try next time.
          AccumulatedSeconds += numSeconds;
          return;
        }
        Angle += rads;
        if(Angle > Math.PI) {
          Angle -= Math.PI*2;
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
        if(config.beatBroadcaster.MeasureLength == -1) {
          return 0;
        }
        // Taken from the BPM toString method, and multiply by 60 for seconds.
        double BPS = 1000.0 / config.beatBroadcaster.MeasureLength;
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
      if(Y > .9999) {
         Y = .9999;
      }
      double racer_loc_y = Y * racerConfig.Length; // Scale from 0 to num_racers
      int racer_idx = (int)(racer_loc_y);
      racer_loc_y -= racer_idx; // Scale from 0 to 1, same as  
      racer_loc_y -= .5; // Scale from -.5 to .5
      racer_loc_y = Math.Abs(racer_loc_y);
      if(racer_loc_y > racer_spacing) {
        return null;
      }
      Racer r = this.Racers[racer_idx];
      // Offset is how many radians ahead of the start angle.
      double start_angle = r.Angle;
      if(start_angle < 0) {
        start_angle += 2*Math.PI;
      }
      if(ang < 0) {
        ang += 2*Math.PI;
      }
      double offset = ang - start_angle;
      if(offset < 0) {
        offset += Math.PI*2;
      }
      if(offset < 2*Math.PI - r.Radians) {
        return null;
      }
      // Some more manipulation to get the percentage in the range
      //   offset > 2*Math.PI - r.Radians
      //   r.Radians > 2*Math.PI - offset
      //   1 > (2*Math.PI - offset) / r.Radians
      double racer_loc_ang = 1 - (2*Math.PI - offset) / r.Radians;
      return new Tuple<Racer, double, double, Racer>(r, racer_loc_y, racer_loc_ang, r);
    }

    public LEDDomeRaceVisualizer(
     Configuration config,
     AudioInput audio,
     MidiInput midi,
     LEDDomeOutput dome
     )
    {
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

    public int Priority
    {
      get
      {
        // The mapping between Visualizers and their corresponding domeActiveVis
        // integer value is determined in the condition below, as well as in the
        // Bind call for domeActiveVis in MainWindow.xaml.cs
        if (this.config.domeActiveVis != 2)
        {
          // By setting the priority to 0, we guarantee that this Visualizer
          // will not run
          return 0;
        }
        // There is a "screensaver" Visualizer with priority 1 associated with
        // LEDDomeOutput (LEDDomeTVStaticVisualizer). The intention is to run
        // that Visualizer when there is no audio input. If you are writing a
        // Visualizer that depends on audio input, consider disabling it when
        // the audio is off using the following condition:
        if (this.audio.isQuiet)
        {
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
    public bool Enabled
    {
      get
      {
        return this.enabled;
      }
      set
      {
        if (value == this.enabled)
        {
          return;
        }
        // Any code that you want to run when the Visualizer is enabled or when
        // it's disabled should go here
        this.enabled = value;
      }
    }

    public Input[] GetInputs()
    {
      // In order for the Operator to know which Inputs need to be enabled, you
      // should return the ones you are currently using here. If your Visualizer
      // only needs certain Inputs under certain conditions, you can put
      // conditional logic here
      return new Input[] { this.audio };
    }

    // This is where the magic happens. As long as this Visualizer has the
    // highest priority for LEDDomeOutput, the Operator thread will call
    // Visualize once per tick
    public void Visualize()
    {

      //-----------------------------------------------------------------------
      // TRACKING THE BEAT
      //-----------------------------------------------------------------------

      // progressThroughMeasure is a double from 0.0 to 1.0 that represents the
      // progress through the current musical measure at this instant in time
      double progressThroughMeasure =
        this.config.beatBroadcaster.ProgressThroughMeasure;

      // ProgressThroughBeat can be used to get the progress through any
      // multiple of a musical measure. Note that the measure is DIVIDED by the
      // parameter you pass to ProgressThroughBeat, so eg. if you want the
      // return value to hit 1.0 on every quarter note, you should pass in 4.0
      double progressThroughHalfMeasure =
        this.config.beatBroadcaster.ProgressThroughBeat(2.0);

      //-----------------------------------------------------------------------
      // TRACKING THE LEVEL (VOLUME)
      //-----------------------------------------------------------------------

      // volume is a float from 0.0 to 1.0 that represents the current total
      // output "level", aka volume
      float volume = this.audio.Volume;

      // The VJ has the ability to configure a "channel" that represents the
      // level within a specific frequency range. They can configure up to 8
      // distinct channels. Besides configuring a frequency range applied to the
      // audio input, it is also possible for the VJ to associate a channel with
      // a MIDI note coming from a MIDI input. In general, it's probably better
      // to use channel0Level instead of Volume, since it gives the VJ more
      // flexibility to configure the Visualizer. Channel 0 is usually
      // configured to be the same as Volume
      double channel0Level = this.audio.LevelForChannel(0);

      //-----------------------------------------------------------------------
      // RESPONDING TO A KICK OR SNARE
      //-----------------------------------------------------------------------

      // An AudioEvent is queued up when Spectrum detects either a Kick or a
      // Snare. On every call to Visualize, audioEventsSinceLastTick will
      // contain a list of detected Kick or Snare events that have occured since
      // the last call to Visualize.
      // public enum AudioDetectorType : byte { Kick, Snare }
      // public class AudioEvent {
      //   public AudioDetectorType type;
      //   public double significance;
      // }
      List<AudioEvent> audioEventsSinceLastTick =
        this.audio.GetEventsSinceLastTick();

      //-----------------------------------------------------------------------
      // MIDI DEVICES
      //-----------------------------------------------------------------------

      // knobValue is a double from 0.0 to 1.0 that represents the current
      // state of knob #15 on MIDI device #0. You can assume that the active
      // MIDI deviceIndex is always 0. Which knob corresponds to #15 will depend
      // on the MIDI board in question.
      double knobValue = this.midi.GetKnobValue(0, 15);

      // noteVelocity is a double from 0.0 to 1.0 that represents the velocity
      // with which note #15 on MIDI device #0 was hit. The velocity is set to a
      // non-zero value when the note is pressed, and is reset to zero when the
      // note is released. If noteVelocity is non-zero, that means the note in
      // question is currently depressed. You can assume that the active MIDI
      // deviceIndex is always 0. Which note corresponds to #15 will depend on
      // the MIDI board in question.
      double noteVelocity = this.midi.GetNoteVelocity(0, 15);

      // An MidiCommand is queued up whenever input is detected from a MIDI
      // device. On every call to Visualize, midiCommandsSinceLastTick will
      // contain an array of MIDI events that have occured since the last call
      // to Visualize. The index distinguishes which Knob, Note, or Program
      // button was involved. Note that when pressing and releasing a Note, two
      // events will be created. The first will be issued when the note is
      // pressed, and its value will be the non-zero velocity with which the
      // press occurred. The second will be issued when the note is released,
      // and its value will be zero.
      // public enum MidiCommandType : byte { Knob, Note, Program }
      // public struct MidiCommand {
      //   public int deviceIndex;
      //   public MidiCommandType type;
      //   public int index;
      //   public double value;
      // }
      MidiCommand[] midiCommandsSinceLastTick =
        this.midi.GetCommandsSinceLastTick();

      //-----------------------------------------------------------------------
      // COLORS
      //-----------------------------------------------------------------------

      // Notes on colors:
      // (1) We represent colors using RGB values stored as ints. Each int
      //   consists of three concatenated bytes (red, green, and blue).
      // (2) These RGB values should not be thought of in the same way as RGB
      //   values in CSS or HTML. The important distinction is that point LEDs
      //   cannot display composite colors such as brown and pink. Each LED
      //   can only display a single wavelength of light at a given moment.
      // (3) An easy way to imagine how a given RGB value will look: scale each
      //   component (red, green, and blue) up equally until at least one of
      //   the components is maxed out at 255. The scale factor represents the
      //   brightness, and the scaled-up color is what you will see on the
      //   LED. SimulatorUtils.GetComputerColor can take an RGB int and return
      //   the scaled-up color.
      // (4) The VJ is able to configure 8 distinct color palettes, each
      //   consisting of 8 pairs of colors. Each pair represents a gradient,
      //   meaning that for each color palette, the VJ has 16 color inputs.
      // (5) By calling the methods below on this.dome instead of on
      //   this.config.colorPalette, we make sure that the brightness is
      //   scaled according to Spectrum's config. We never run the dome at
      //   100% brightness, and our power supplies are not provisioned for it.

      // GetSingleColor retrieves the color pair at the given index for the
      // current color palette. It then discards the second color in the pair,
      // returning only the first color in the pair.
      int firstSingleColor = this.dome.GetSingleColor(0);

      // GetGradientColor is a bit more complicated. It retrieves the color pair
      // at the given index for the current color palette, and then blends that
      // pair according to the rest of the parameters.
      // Imagine a strut that is being colored according to a gradient.
      // (*) The second parameter (0.5 below) is a double from 0.0 to 1.0 that
      //   represents the position along that strut. So ignoring the last two
      //   parameters, the second parameter will determine how much of each
      //   color to mix. 0.5 means half of each, whereas 0.0 means just the
      //   first color, and 1.0 means just the second color.
      // (*) The third parameter (0.0 below) is a double from 0.0 to 1.0 that
      //   represents an offset we add to the second parameter. Its purpose is
      //   to allow the Visualizer author to animate the gradient along a
      //   strut. If you set it to 0.25, then the LED one quarter of the way
      //   through the strut will be the first color, and the one right before
      //   it will be the second color. Correspondingly, the first LED on the
      //   strut would be 75% second color, 25% first color.
      // (*) The final parameter (false below) is a boolean that represents
      //   whether we want the gradient to "wrap" or not. If you set this to
      //   true, in the above case where the third parameter is 0.25, instead
      //   of seeing a sharp transition a quarter of the way in, the gradient
      //   will be represented all the way through. In the 0.25 case, the LED
      //   one quarter of the way in would be the first color, and the LED
      //   three quarters of the way in would be the second color. The first
      //   LED as well as the last LED on the strut would both be a 50/50
      //   blend.
      int middleOfStrutColor = this.dome.GetGradientColor(0, 0.5, 0.0, false);
      
      //-----------------------------------------------------------------------
      // SETTING THE RACERS
      //-----------------------------------------------------------------------
      
      this.racer_spacing = this.config.domeRadialSize / 4.0;
      var curTicks = DateTime.Now.Ticks;
      if(this.lastTicks == -1) { 
        this.lastTicks = curTicks;
      } else { 
        double numSeconds = ((double)(curTicks - this.lastTicks)) / (1000 * 1000 * 10);
        foreach(Racer r in Racers) {
          r.Move(numSeconds, audio, config);
        }
        this.lastTicks = curTicks;
      }

      //-----------------------------------------------------------------------
      // SETTING LEDS
      //-----------------------------------------------------------------------

      // This method is used to set an LED to a color. You identify the LED
      // using a pair of a strut index and an LED index (see STRUT LAYOUTS
      // above), and then provide a color to set that LED to.
      this.dome.SetPixel(15, 20, firstSingleColor);
      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        Strut strut = Strut.FromIndex(this.config, i);
        var leds = LEDDomeOutput.GetNumLEDs(i);
        for (int j = 0; j < leds; j++) {
          var pixel_p = StrutLayoutFactory.GetProjectedLEDPointParametric(i, j);
          // Vertical height
          var y = 1.0 - pixel_p.Item4;
          var rad = pixel_p.Item3;
          Tuple<Racer, double, double, Racer> loc = this.GetRacer(y, rad);
          if(loc == null) {
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
