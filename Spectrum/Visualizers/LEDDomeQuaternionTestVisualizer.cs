using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Numerics;

namespace Spectrum.Visualizers {
  class LEDDomeQuaternionTestVisualizer : DomeLayerVisualizer {


    private Configuration config;
    private OrientationInput orientation;
    private LEDDomeOutput dome;
    private LEDDomeOutputBuffer buffer;

    // Static per-pixel unit-sphere geometry, baked once (guarded z, shared with
    // the layer visualizers via LEDDomeOutputBuffer.BakePixelPositions).
    private readonly Vector3[] pixelPositions;

    public LEDDomeQuaternionTestVisualizer(
      Configuration config,
      OrientationInput orientation,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.orientation = orientation;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeOutputBuffer();
      this.pixelPositions = this.buffer.BakePixelPositions();
    }

    public int Priority => 2;

    public string LayerKey => "quaternion-test";
    public LEDDomeOutputBuffer LayerBuffer => this.buffer;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientation });
    }

    void Render() {
      for (int i = 0; i < buffer.pixels.Length; i++) {
        Vector3 pixelPoint = this.pixelPositions[i];
        Vector3 pixelPointQuat = Vector3.Transform(pixelPoint, orientation.deviceRotation(config.orientationDeviceSpotlight));
        // Color maxes
        int maxIndex = MaxBy(pixelPointQuat);
        Color color = new Color(0, 0, 0);
        if(maxIndex == 0) {
          color = new Color(255, 0, 0);
        } else if(maxIndex == 1) {
          color = new Color(0, 255, 0);
        } else if(maxIndex == 2) {
          color = new Color(0, 0, 255);
        }
        buffer.pixels[i].color = color.ToInt();
      }
    }

    public void Visualize() {
      this.Render();
    }
    
    // Returns the index of the maximum item in a vector
    public int MaxBy(Vector3 v) {
      if (Math.Abs(v.X) > Math.Abs(v.Y)) {
        return Math.Abs(v.X) > Math.Abs(v.Z) ? 0 : 2;
      }
      return Math.Abs(v.Y) > Math.Abs(v.Z) ? 1 : 2;
    }
  }
}
