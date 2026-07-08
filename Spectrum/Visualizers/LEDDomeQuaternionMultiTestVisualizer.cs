using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;

namespace Spectrum.Visualizers {
  class LEDDomeQuaternionMultiTestVisualizer : DomeLayerVisualizer {


    private Configuration config;
    private OrientationInput orientation;
    private LEDDomeOutput dome;
    private LEDDomeOutputBuffer buffer;

    private Vector3 spot = new Vector3(0, 1, 0);
    private readonly object mLock = new object();

    // Static per-pixel unit-sphere geometry, baked once (shared with the layer
    // visualizers via LEDDomeOutputBuffer.BakePixelPositions).
    private readonly Vector3[] pixelPositions;

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
      this.pixelPositions = this.buffer.BakePixelPositions();
    }

    public int Priority {
      get {
        return DomeLayerSettings.StackActivates(
          this.config.domeLayerStack, "quaternion-multi-test"
        ) ? 2 : 0;
      }
    }

    public string LayerKey => "quaternion-multi-test";
    public LEDDomeOutputBuffer LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientation });
    }

    void Render() {

      // Global effects
      // Fade out
      buffer.Fade(1 - Math.Pow(5, -this.config.domeGlobalFadeSpeed), 0);

      // Store the device states as of this frame; this avoids problems when the devices get updated
      // in another thread
      Dictionary<int, OrientationDevice> devices = orientation.DevicesSnapshot();

      for (int i = 0; i < buffer.pixels.Length; i++) {
        Vector3 pixelPoint = this.pixelPositions[i];

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
    }

    public void Visualize() {
      Render();
    }
  }
}
