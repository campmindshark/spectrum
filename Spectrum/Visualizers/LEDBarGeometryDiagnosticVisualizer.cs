using BarGeometry;
using Spectrum.Base;
using Spectrum.LEDs;
using System.Diagnostics;

namespace Spectrum {
  public class LEDBarGeometryDiagnosticVisualizer : Visualizer {
    private Configuration config;
    private LEDGeometryOutput geometryOutput;
    private Stopwatch stopwatch;

    public LEDBarGeometryDiagnosticVisualizer(
      Configuration config,
      LEDGeometryOutput icosahedronOut) {
      this.config = config;
      this.geometryOutput = icosahedronOut;
      this.geometryOutput.RegisterVisualizer(this);
      this.stopwatch = new Stopwatch();
      this.stopwatch.Start();
    }

    public int Priority => 10000;//this.config.icosahedronTestPattern == 1 ? 1000 : 0;

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
          var lastPixelOnStripId = (byte)(geometryOutput.Icosahedrons[h].LedsPerStrip - 1);
          this.geometryOutput.SetPixel(GeometryShapeType.Icosahedron, h, i, 0, whiteColor);
          this.geometryOutput.SetPixel(GeometryShapeType.Icosahedron, h, i, lastPixelOnStripId, whiteColor);
          for (int j = 1; j < lastPixelOnStripId; j++) {
             this.geometryOutput.SetPixel(GeometryShapeType.Icosahedron, h, i, j, 0);
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
  }
}
