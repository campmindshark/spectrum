using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Numerics;

namespace Spectrum.Visualizers {
  class LEDDomeQuaternionFocusVisualizer : Visualizer {


    private Configuration config;
    private AudioInput audio;
    private OrientationInput orientation;
    private LEDDomeOutput dome;
    private Vector3 spot = new Vector3(0, 1, 0);

    public LEDDomeQuaternionFocusVisualizer(
      Configuration config,
      AudioInput audio,
      OrientationInput orientation,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.audio = audio;
      this.orientation = orientation;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
    }

    public int Priority {
      get {
        return this.config.domeActiveVis == 6 ? 2 : 0;
      }
    }

    public bool Enabled { get; set; }

    public Input[] GetInputs() {
      return new Input[] { this.orientation };
    }

    void Render() {
      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        var leds = LEDDomeOutput.GetNumLEDs(i);
        for (int j = 0; j < leds; j++) {
          var p = StrutLayoutFactory.GetProjectedLEDPoint(i, j); // centered on (.5, .5), [0, 1] x [0, 1]
          var x = 2 * p.Item1 - 1; // now centered on (0, 0) and with range [0, 1]
          var y = 1 - 2 * p.Item2; // this is because in the original mapping x, y come "out of" the top left corner
          float z = (float)Math.Sqrt(1 - x * x - y * y);
          Vector3 pixelPoint = new Vector3((float)x, (float)y, z);
          // Calibration assigns (0, 1, 0) to be 'forward'
          // So we want the post-transformed pixel closest to (0, 1, 0)?
          int color = 0;
          if (Vector3.Distance(Vector3.Transform(pixelPoint, orientation.rotation), spot) < .25) {
            color = 0xFFFFFF;
          }

          this.dome.SetPixel(i, j, color);
        }
      }
    }
    public void Visualize() {
      this.Render();

      this.dome.Flush();
    }
  }
}
