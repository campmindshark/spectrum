using Spectrum.Base;
using Spectrum.Audio;
using Spectrum.MIDI;
using Spectrum.LEDs;
using System.Collections.Generic;

namespace Spectrum {

  class LEDDomeExampleVisualizer : Visualizer {

    private readonly Configuration config;
    private readonly AudioInput audio;
    private readonly MidiInput midi;
    private readonly LEDDomeOutput dome;

    public LEDDomeExampleVisualizer(
      Configuration config,
      AudioInput audio,
      MidiInput midi,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.audio = audio;
      this.midi = midi;
      this.dome = dome;
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
        if (this.config.domeActiveVis != 3) {
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
      return new Input[] { this.audio, this.midi };
    }

    // This is where the magic happens. As long as this Visualizer has the
    // highest priority for LEDDomeOutput, the Operator thread will call
    // Visualize once per tick
    public void Visualize() {

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
      //     consists of three concatenated bytes (red, green, and blue).
      // (2) These RGB values should not be thought of in the same way as RGB
      //     values in CSS or HTML. The important distinction is that point LEDs
      //     cannot display composite colors such as brown and pink. Each LED
      //     can only display a single wavelength of light at a given moment.
      // (3) An easy way to imagine how a given RGB value will look: scale each
      //     component (red, green, and blue) up equally until at least one of
      //     the components is maxed out at 255. The scale factor represents the
      //     brightness, and the scaled-up color is what you will see on the
      //     LED. SimulatorUtils.GetComputerColor can take an RGB int and return
      //     the scaled-up color.
      // (4) The VJ is able to configure 8 distinct color palettes, each
      //     consisting of 8 pairs of colors. Each pair represents a gradient,
      //     meaning that for each color palette, the VJ has 16 color inputs.
      // (5) By calling the methods below on this.dome instead of on
      //     this.config.colorPalette, we make sure that the brightness is
      //     scaled according to Spectrum's config. We never run the dome at
      //     100% brightness, and our power supplies are not provisioned for it.

      // GetSingleColor retrieves the color pair at the given index for the
      // current color palette. It then discards the second color in the pair,
      // returning only the first color in the pair.
      int firstSingleColor = this.dome.GetSingleColor(0);

      // GetGradientColor is a bit more complicated. It retrieves the color pair
      // at the given index for the current color palette, and then blends that
      // pair according to the rest of the parameters.
      // Imagine a strut that is being colored according to a gradient.
      // (*) The second parameter (0.5 below) is a double from 0.0 to 1.0 that
      //     represents the position along that strut. So ignoring the last two
      //     parameters, the second parameter will determine how much of each
      //     color to mix. 0.5 means half of each, whereas 0.0 means just the
      //     first color, and 1.0 means just the second color.
      // (*) The third parameter (0.0 below) is a double from 0.0 to 1.0 that
      //     represents an offset we add to the second parameter. Its purpose is
      //     to allow the Visualizer author to animate the gradient along a
      //     strut. If you set it to 0.25, then the LED one quarter of the way
      //     through the strut will be the first color, and the one right before
      //     it will be the second color. Correspondingly, the first LED on the
      //     strut would be 75% second color, 25% first color.
      // (*) The final parameter (false below) is a boolean that represents
      //     whether we want the gradient to "wrap" or not. If you set this to
      //     true, in the above case where the third parameter is 0.25, instead
      //     of seeing a sharp transition a quarter of the way in, the gradient
      //     will be represented all the way through. In the 0.25 case, the LED
      //     one quarter of the way in would be the first color, and the LED
      //     three quarters of the way in would be the second color. The first
      //     LED as well as the last LED on the strut would both be a 50/50
      //     blend.
      int middleOfStrutColor = this.dome.GetGradientColor(0, 0.5, 0.0, false);

      //-----------------------------------------------------------------------
      // STRUT LAYOUTS
      //-----------------------------------------------------------------------

      // Notes on strut layout:
      // (1) Each LED on the dome is uniquely identified by a pair of a strut
      //     index and an LED index.
      // (2) The "key" that shows how strut indices are assigned can be viewed
      //     in the dome simulator window. To open the dome simulator window,
      //     first run Spectrum, then go to the "LED Dome" tab and check the box
      //     labelled "Simulate". The simulator window should pop up. Click
      //     "Show Key" to see the key.
      // (3) Each strut has a number of LEDs, as well as a direction that
      //     determines how its LED indices are assigned. The key provides this
      //     information.
      // (4) If you want to create a list of struts that you want to animate
      //     together, you can press a series of struts in the key view. The
      //     text box on the upper right will populate with a comma-separated
      //     list of strut indices.

      // StrutLayoutFactory contains utilities for generating collections of
      // Struts, known as StrutLayouts.
      // (*) The Strut class simply represents a strut by a given strut index,
      //     as well as including a boolean for whether or not to reverse the
      //     direction.
      // (*) The StrutLayoutSegment class represents an unordered set of Struts.
      //     There is no order within a StrutLayoutSegment, as all the Struts
      //     within are expected to be animated together, at the same time.
      // (*) A StrutLayout is simply an array of StrutLayoutSegments.
      // (*) StrutLayoutFactory also assigns each vertex an index (calling them
      //     "points"), which it uses in certain cases as user inputs.

      // If you've ever seen the default dome animation in past years, it's
      // basically using this layout. It's pretty domain-specific, and probably
      // not worth getting into too much. You won't be calling this method, but
      // if you're finding yourself making a complex strut layout, you might end
      // up writing someting similar.
      StrutLayout[] layouts = StrutLayoutFactory.ConcentricFromStartingPoints(
        this.config,
        // This list of points was determined manually
        new HashSet<int>(new int[] { 22, 26, 30, 34, 38, 70 }),
        4
      );

      //-----------------------------------------------------------------------
      // SETTING LEDS
      //-----------------------------------------------------------------------

      // This method is used to set an LED to a color. You identify the LED
      // using a pair of a strut index and an LED index (see STRUT LAYOUTS
      // above), and then provide a color to set that LED to.
      this.dome.SetPixel(15, 20, firstSingleColor);

      // No messages will be sent to the Beaglebone until Flush is called. This
      // makes it imperative that you call Flush at the end of Visualize, or
      // whenever you want to update the LEDs. The LEDs themselves are stateful,
      // and will maintain whatever color you set them to until they lose power.
      this.dome.Flush();
    }

  }

}
