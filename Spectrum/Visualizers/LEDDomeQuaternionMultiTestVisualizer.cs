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
      dome.RegisterVisualizer(this);
      buffer = dome.MakeDomeOutputBuffer();
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

      // Global effects
      // Fade out
      buffer.Fade(1 - Math.Pow(5, -this.config.domeGlobalFadeSpeed), 0);

      // Store the device states as of this frame; this avoids problems when the devices get updated
      // in another thread
      Dictionary<int, OrientationDevice> devices;
      devices = new Dictionary<int, OrientationDevice>(orientation.devices);

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

        foreach (int deviceId in devices.Keys) {
          Quaternion currentOrientation = devices[deviceId].currentRotation();
          double distance = Vector3.Distance(Vector3.Transform(pixelPoint, currentOrientation), spot);
          int sat = 1;
          if (devices[deviceId].actionFlag == 1) {
            radius = .4;
            sat = 0;
          } else {
            radius = .2;
            sat = 1;
          }
          if (distance < radius) {
            double L = (radius - distance) / radius;
            double hue = (double)Array.IndexOf(devices.Keys.ToArray(), deviceId) / devices.Count;
            Color color = new Color(hue, sat, 1);
            buffer.pixels[i].color = Color.BlendLightPaint(new Color(buffer.pixels[i].color), color).ToInt();
          }
        }
      }
      dome.WriteBuffer(buffer);
    }

    public void Visualize() {
      Render();
      dome.Flush();
    }
  }
}
