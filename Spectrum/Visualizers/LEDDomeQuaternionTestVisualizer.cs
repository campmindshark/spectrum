using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Numerics;

namespace Spectrum.Visualizers {
  class LEDDomeQuaternionTestVisualizer : Visualizer{

    private Configuration config;
    private OrientationInput orientation;
    private LEDDomeOutput dome;
    private LEDDomeOutputBuffer buffer;

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
    }

    public int Priority {
      get {
        return this.config.domeActiveVis == 4 ? 2 : 0;
      }
    }

    public bool Enabled { get; set; }

    public Input[] GetInputs() {
      return new Input[] { this.orientation };
    }

    void Render() {
      for (int i = 0; i < buffer.pixels.Length; i++) {
        var p = buffer.pixels[i];
        var x = 2 * p.x - 1; // now centered on (0, 0) and with range [0, 1]
        var y = 1 - 2 * p.y; // this is because in the original mapping x, y come "out of" the top left corner
        float z = (float)Math.Sqrt(1 - x * x - y * y);
        Vector3 pixelPoint = new Vector3((float)x, (float)y, z);
        Vector3 pixelPointQuat = Vector3.Transform(pixelPoint, orientation.rotation);
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
      this.dome.WriteBuffer(buffer);
    }

    public void Visualize() {
      this.Render();

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
