using BarGeometry;
using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Diagnostics;

namespace Spectrum {
  public class LEDBarGeometryVolumeVisualizer : Visualizer {
    private Configuration config;
    private AudioInput audio;
    private LEDGeometryOutput geometryOutput;
    private Stopwatch stopwatch;

    public LEDBarGeometryVolumeVisualizer(
      Configuration config,
      LEDGeometryOutput icosahedronOut) {
      this.config = config;
      this.geometryOutput = icosahedronOut;
      this.geometryOutput.RegisterVisualizer(this);
      this.stopwatch = new Stopwatch();
      this.stopwatch.Start();
    }

    public int Priority => 1;//this.config.icosahedronTestPattern == 1 ? 1000 : 0;

    private bool _enabled = false;
    public bool Enabled {
      get => _enabled;
      set {
        if (value == this.Enabled)
          return;
        this._enabled = value;
      }
    }

    public Input[] GetInputs() {
      return new Input[] { };
    }

    public void Visualize() {
      double level = this.audio.LevelForChannel(0);
      //Ocatahedron 
      //Center on Top and Bottom of Pylons

      //Switch Center to Verticies 1 & 3

      //Switch Center to Verticies 4 & 5

      if (this.stopwatch.ElapsedMilliseconds <= 1000) {
        return;
      }
      this.stopwatch.Restart();
      byte brightnessByte = (byte)(0xFF * this.config.barBrightness);
      int whiteColor = brightnessByte << 16
        | brightnessByte << 8
        | brightnessByte;

      //loop through strips
      for (int h= 0; h < geometryOutput.Icosahedrons.Length; h++) {
        for (int i = 0; i < geometryOutput.Icosahedrons[h].Strips.Count; i++) {
          for (int j = 0; j < geometryOutput.Icosahedrons[h].LedsPerStrip; j++) {
             this.geometryOutput.SetPixel(GeometryShapeType.Icosahedron, h, i, j, GetIcosahedronBgColor());
          }
        }
      }

      for (int h = 0; h < geometryOutput.Octahedrons.Length; h++) {
        for (int i = 0; i < geometryOutput.Octahedrons[h].Strips.Count; i++) {
          var lastPixelOnStripId = geometryOutput.Octahedrons[h].Strips[i].LedCount- 1;
          this.geometryOutput.SetPixel(GeometryShapeType.Octahedron, h, i, 0, whiteColor);
          this.geometryOutput.SetPixel(GeometryShapeType.Octahedron, h, i, lastPixelOnStripId, whiteColor);
          for (byte j = 1; j < lastPixelOnStripId; j++) {
            this.geometryOutput.SetPixel(j, i, 0);
          }
        }
      }

      this.geometryOutput.Flush();
    }

    private int GetIcosahedronBgColor() {
      return System.Drawing.Color.Aquamarine.ToArgb();
    }
  }
}
