using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace Spectrum.Visualizers {
  class LEDDomeQuaternionTestVisualizer : Visualizer{


    private Configuration config;
    private OrientationInput orientation;
    private LEDDomeOutput dome;
    private Vector4 spot = new Vector4(0, 1, 0, 0);

    public LEDDomeQuaternionTestVisualizer(
      Configuration config,
      OrientationInput orientation,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.orientation = orientation;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
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
      Vector4 newSpot = Vector4.Transform(spot, orientation.rotation);
      Tuple<double, double> projectedSpot = Convert3D(newSpot);

      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        var leds = LEDDomeOutput.GetNumLEDs(i);
        for (int j = 0; j < leds; j++) {
          var p = StrutLayoutFactory.GetProjectedLEDPoint(i, j);

          double xdiff = p.Item1 - projectedSpot.Item1;
          double ydiff = p.Item2 - projectedSpot.Item2;

          double val = xdiff * xdiff + ydiff * ydiff;
          if(val < .25) {
            this.dome.SetPixel(i, j, 0xFFFFFF);
          } else {
            this.dome.SetPixel(i, j, 0);
          }
        }
      }
    }

    private static Tuple<double, double> Convert3D(Vector4 vector) {
      // Lambert azimuthal equal-area projection
      double x = Math.Sqrt(2 / (1 - vector.X)) * vector.Z * -1;
      double y = Math.Sqrt(2 / (1 - vector.X)) * vector.Y;
      // Dome coordinate space is [0, 1] x [0, 1] centered at (.5, .5)
      x = (x + 1) / 2;
      y = (y + 1) / 2;
      return new Tuple<double, double>(x, y);
    }

    public void Visualize() {
      this.Render();

      this.dome.Flush();
    }
  }
}
