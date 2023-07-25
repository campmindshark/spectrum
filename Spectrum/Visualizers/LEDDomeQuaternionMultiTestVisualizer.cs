using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

namespace Spectrum.Visualizers {
  class LEDDomeQuaternionMultiTestVisualizer : Visualizer {


    private Configuration config;
    private OrientationInput orientation;
    private LEDDomeOutput dome;
    private LEDDomeOutputBuffer buffer;

    Dictionary<int, OrientationDevice> devicesCopy;
    private Vector3 spot = new Vector3(0, 1, 0);
    private readonly object mLock = new object();

    public LEDDomeQuaternionMultiTestVisualizer(
      Configuration config,
      OrientationInput orientation,
      LEDDomeOutput dome
    ) {
      this.config = config;
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
      lock (mLock) {
        devicesCopy = orientation.devices.ToDictionary(entry => entry.Key, entry => entry.Value);
      }
      for (int i = 0; i < buffer.pixels.Length; i++) {
        var p = buffer.pixels[i];
        var x = 2 * p.x - 1; // now centered on (0, 0) and with range [-1, 1]
        var y = 1 - 2 * p.y; // this is because in the original mapping x, y come "out of" the top left corner
        float z = (x * x + y * y) > 1 ? 0 : (float)Math.Sqrt(1 - x * x - y * y);
        Vector3 pixelPoint = new Vector3((float)x, (float)y, z);

        // # Spotlight - orientation sensor dot
        // Calibration assigns (0, 1, 0) to be 'forward'
        // So we want the post-transformed pixel closest to (0, 1, 0)?
        double radius = .2;
        foreach (KeyValuePair<int, OrientationDevice> entry in devicesCopy) {
          Quaternion currentOrientation = entry.Value.currentRotation();
          double distance = Vector3.Distance(Vector3.Transform(pixelPoint, currentOrientation), spot);
          if (distance < radius) {
            double L = (radius - distance) / radius;
            double hue = (double)entry.Key / devicesCopy.Count;
            Color color = new Color(hue, 1, 1);
            buffer.pixels[i].color = color.ToInt();
          }
        }
      }
      // Global effects
      // Fade out
      buffer.Fade(1 - Math.Pow(5, -this.config.domeGlobalFadeSpeed), 0);
      this.dome.WriteBuffer(buffer);
    }

    public void Visualize() {
      this.Render();
      this.dome.Flush();
    }
    private static double Clamp(double x, double a, double b) {
      if (x < a) return a;
      if (x > b) return b;
      return x;
    }
  }
}
