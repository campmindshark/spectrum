using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Immutable;
using System.Numerics;

namespace Spectrum.Visualizers {
  class LEDDomeQuaternionTestVisualizer : Visualizer {

    private readonly Configuration config;
    private readonly OrientationInput orientation;
    private readonly LEDDomeOutput dome;
    private readonly DomeFrame buffer;

    // Static per-pixel unit-sphere geometry, baked once (guarded z, shared with
    // the layer visualizers via DomeFrame.BakePixelPositions).
    private readonly ImmutableArray<Vector3> pixelPositions;

    public LEDDomeQuaternionTestVisualizer(
      Configuration config,
      OrientationInput orientation,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.orientation = orientation;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      this.buffer = this.dome.MakeDomeFrame();
      this.pixelPositions = this.buffer.BakePixelPositions();
    }

    public int Priority => this.config.domeTestPattern == 5 ? 1000 : 0;

    public bool Enabled { get; set; }

    private Input[] inputs;
    public Input[] GetInputs() {
      return this.inputs ?? (this.inputs = new Input[] { this.orientation });
    }

    void Render() {
      for (int i = 0; i < buffer.pixels.Length; i++) {
        Vector3 pixelPoint = this.pixelPositions[i];
        Vector3 pixelPointQuat = Vector3.Transform(
          pixelPoint,
          orientation.deviceRotation(this.config.orientationDeviceSpotlight));
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
      this.dome.WriteBuffer(this.buffer);
      this.dome.Flush();
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
