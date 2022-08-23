using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Numerics;

namespace Spectrum.Visualizers {
  class LEDDomeQuaternionPaintbrushVisualizer : Visualizer {


    private Configuration config;
    private AudioInput audio;
    private OrientationInput orientation;
    private LEDDomeOutput dome;
    private LEDDomeOutputBuffer buffer;
    private Vector3 spot = new Vector3(0, 1, 0);

    public LEDDomeQuaternionPaintbrushVisualizer(
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
      this.buffer = this.dome.MakeDomeOutputBuffer();
    }

    public int Priority {
      get {
        return this.config.domeActiveVis == 5 ? 2 : 0;
      }
    }

    public bool Enabled { get; set; }

    public Input[] GetInputs() {
      return new Input[] { this.orientation };
    }

    void Render() {
      buffer.Fade(1 - Math.Pow(10, -this.config.domeGlobalFadeSpeed), 0);
      buffer.HueRotate(Math.Pow(10, -this.config.domeGlobalHueSpeed));
      for (int i = 0; i < buffer.pixels.Length; i++) {
        var p = buffer.pixels[i];
        var x = 2 * p.x - 1; // now centered on (0, 0) and with range [0, 1]
        var y = 1 - 2 * p.y; // this is because in the original mapping x, y come "out of" the top left corner
        float z = (float)Math.Sqrt(1 - x * x - y * y);
        Vector3 pixelPoint = new Vector3((float)x, (float)y, z);
        // Calibration assigns (0, 1, 0) to be 'forward'
        // So we want the post-transformed pixel closest to (0, 1, 0)?
        if(Vector3.Distance(Vector3.Transform(pixelPoint, orientation.rotation), spot) < .25) {
          Color color = new Color((256 * (orientation.rotation.W + 1) / 2) / 256d, 1, 1);
          buffer.pixels[i].color = color.ToInt();
        }
      }
      this.dome.WriteBuffer(buffer);

    }

    public void Visualize() {
      this.Render();

      this.dome.Flush();
    }
  }
}
