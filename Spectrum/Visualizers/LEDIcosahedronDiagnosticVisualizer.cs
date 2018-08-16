using Spectrum.Base;
using Spectrum.LEDs;
using System.Diagnostics;

namespace Spectrum {
  public class LEDIcosahedronDiagnosticVisualizer : Visualizer {
    private Configuration config;
    private LEDGeometryOutput geometryOutput;
    private Stopwatch stopwatch;

    public LEDIcosahedronDiagnosticVisualizer(
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
      for (int i = 0; i < this.geometryOutput.Icosahedrons[0].Strips.Count; i++) {
        var lastPixelOnStripId = this.geometryOutput.Icosahedrons[0].LedsPerStrip - 1;
        this.geometryOutput.SetPixel(0, i, whiteColor);
        //this.geometryOutput.SetPixel(this.geometryOutput.Icosahedrons[0].OPCStartChannel, lastPixelOnStripId, i, whiteColor);
        for(int j= 1; j < lastPixelOnStripId; j++) {
        //  this.geometryOutput.SetPixel(j, i, 0);
        }
      }

      this.geometryOutput.Flush();
    }
  }
}
