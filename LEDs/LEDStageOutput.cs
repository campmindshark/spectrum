using Spectrum.Base;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System;

namespace Spectrum.LEDs {

  public class LEDStageOutput : Output {

    private TeensyAPI[] teensies;
    private OPCAPI opcAPI;
    private Configuration config;
    private List<Visualizer> visualizers;
    private int maxTriangleLength;

    public LEDStageOutput(Configuration config) {
      this.config = config;
      this.visualizers = new List<Visualizer>();
      this.config.PropertyChanged += this.ConfigUpdated;
    }

    private void calculateMaxTriangleLength() {
      int maxLength = 0;
      for (int i = 0; i < 48; i += 3) {
        int length = this.config.stageSideLengths[i] +
          this.config.stageSideLengths[i + 1] +
          this.config.stageSideLengths[i + 2];
        if (length > maxLength) {
          maxLength = length;
        }
      }
      this.maxTriangleLength = maxLength * 3;
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (!this.active) {
        return;
      }
      if (e.PropertyName == "stageHardwareSetup") {
        if (!this.config.stageEnabled) {
        } else if (this.config.stageHardwareSetup == 0) {
          if (this.opcAPI != null) {
            this.opcAPI.Active = false;
          }
          this.initializeTeensies();
        } else if (this.config.stageHardwareSetup == 1) {
          if (this.teensies != null) {
            this.teensies[0].Active = false;
            this.teensies[1].Active = false;
          }
          this.initializeOPCAPI();
        }
      } else if (
        e.PropertyName == "stageTeensyUSBPort1" ||
          e.PropertyName == "stageTeensyUSBPort2"
      ) {
        if (this.config.stageHardwareSetup == 0 && this.config.stageEnabled) {
          this.teensies[0].Active = false;
          this.teensies[1].Active = false;
          this.initializeTeensies();
        }
      } else if (e.PropertyName == "stageBeagleboneOPCAddress") {
        if (this.config.stageHardwareSetup == 1 && this.config.stageEnabled) {
          this.opcAPI.Active = false;
          this.initializeOPCAPI();
        }
      } else if (e.PropertyName == "stageOutputInSeparateThread") {
        if (!this.config.stageEnabled) {
        } else if (this.config.stageHardwareSetup == 0) {
          if (this.teensies != null) {
            this.teensies[0].Active = false;
            this.teensies[1].Active = false;
          }
          this.initializeTeensies();
        } else if (this.config.stageHardwareSetup == 1) {
          if (this.opcAPI != null) {
            this.opcAPI.Active = false;
          }
          this.initializeOPCAPI();
        }
      } else if (e.PropertyName == "stageSideLengths") {
        this.calculateMaxTriangleLength();
      }
    }

    private void initializeTeensies() {
      try {
        TeensyAPI api1 = new TeensyAPI(
          this.config.stageTeensyUSBPort1,
          this.config.stageOutputInSeparateThread,
          newFPS => this.config.stageTeensyFPS1 = newFPS
        );
        TeensyAPI api2 = new TeensyAPI(
          this.config.stageTeensyUSBPort2,
          this.config.stageOutputInSeparateThread,
          newFPS => this.config.stageTeensyFPS2 = newFPS
        );
        api1.Active = this.active && this.config.stageHardwareSetup == 0;
        api2.Active = this.active && this.config.stageHardwareSetup == 0;
        this.teensies = new TeensyAPI[] { api1, api2 };
      } catch (Exception) {
        this.teensies = null;
      }
    }

    private void initializeOPCAPI() {
      var opcAddress = this.config.stageBeagleboneOPCAddress;
      string[] parts = opcAddress.Split(':');
      if (parts.Length < 3) {
        opcAddress += ":0"; // default to channel 0
      }
      this.opcAPI = new OPCAPI(
        opcAddress,
        this.config.stageOutputInSeparateThread,
        newFPS => this.config.stageBeagleboneOPCFPS = newFPS
      );
      this.opcAPI.Active = this.active &&
        this.config.stageHardwareSetup == 1;
      this.calculateMaxTriangleLength();
    }

    private bool active = false;
    public bool Active {
      get {
        return this.active;
      }
      set {
        if (value == this.active) {
          return;
        }
        this.active = value;
        if (!this.config.stageEnabled) {
          return;
        }
        if (value) {
          if (this.config.stageHardwareSetup == 0) {
            this.initializeTeensies();
          } else if (this.config.stageHardwareSetup == 1) {
            this.initializeOPCAPI();
          }
        } else {
          if (this.teensies != null) {
            this.teensies[0].Active = false;
            this.teensies[1].Active = false;
          }
          if (this.opcAPI != null) {
            this.opcAPI.Active = false;
          }
        }
      }
    }

    public bool Enabled {
      get {
        return this.config.stageEnabled || this.config.stageSimulationEnabled;
      }
   }

    public void OperatorUpdate() {
      if (this.config.stageHardwareSetup == 0 && this.teensies != null) {
        this.teensies[0].OperatorUpdate();
        this.teensies[1].OperatorUpdate();
      }
      if (this.config.stageHardwareSetup == 1 && this.opcAPI != null) {
        this.opcAPI.OperatorUpdate();
      }
    }

    public void RegisterVisualizer(Visualizer visualizer) {
      this.visualizers.Add(visualizer);
    }

    public Visualizer[] GetVisualizers() {
      return this.visualizers.ToArray();
    }

    public void Flush() {
      if (this.config.stageHardwareSetup == 0 && this.teensies != null) {
        this.teensies[0].Flush();
        this.teensies[1].Flush();
      }
      if (this.config.stageHardwareSetup == 1 && this.opcAPI != null) {
        this.opcAPI.Flush();
      }
      if (this.config.stageSimulationEnabled) {
        this.config.stageCommandQueue.Enqueue(
          new StageLEDCommand() { isFlush = true }
        );
      }
    }

    /**
     * sideIndex: each side of each triangle has an index (0-47)
     * ledIndex: each side has a set of LEDs, individually identifiable by their
     *   ledIndex
     * layerIndex: each side has three layers of LEDs
     * color: duh the color???
     */
    public void SetPixel(
      int sideIndex,
      int ledIndex,
      int layerIndex,
      int color
    ) {
      int pixelIndex = ledIndex;
      // Two Teensies split all the triangles and each handles half
      int startingSide = this.config.stageHardwareSetup == 0 && sideIndex >= 24
        ? 24
        : 0;
      // We increment pixelIndex for each complete triangle/layer here
      for (int i = startingSide; i + 2 < sideIndex; i += 3) {
        // On Teensies, each strip can be a dynamic length, so we increment
        // pixelIndex by the precise number of preceeding pixels
        if (this.config.stageHardwareSetup == 0) {
          pixelIndex += 3 * (
            this.config.stageSideLengths[i] +
            this.config.stageSideLengths[i + 1] +
            this.config.stageSideLengths[i + 2]
          );
        } else {
          // On OPC/CAMP, each strip has to be the same length, which means we
          // need to use the max
          pixelIndex += this.maxTriangleLength;
        }
      }
      var baseSideIndex = (sideIndex / 3) * 3;
      for (int i = 0; i < layerIndex; i++) {
        // Now we increment pixelIndex for every complete layer on the target
        // triangle
        pixelIndex += this.config.stageSideLengths[baseSideIndex] +
          this.config.stageSideLengths[baseSideIndex + 1] +
          this.config.stageSideLengths[baseSideIndex + 2];
      }
      for (int i = baseSideIndex; i < sideIndex; i++) {
        // Finally, we increment pixelIndex for every complete side on the
        // target triangle and layer
        pixelIndex += this.config.stageSideLengths[i];
      }
      if (this.config.stageHardwareSetup == 0 && this.teensies != null) {
        if (sideIndex >= 24) {
          this.teensies[1].SetPixel(pixelIndex, color);
        } else {
          this.teensies[0].SetPixel(pixelIndex, color);
        }
      }
      if (this.config.stageHardwareSetup == 1 && this.opcAPI != null) {
        this.opcAPI.SetPixel(pixelIndex, color);
      }
      if (this.config.stageSimulationEnabled) {
        this.config.stageCommandQueue.Enqueue(new StageLEDCommand() {
          sideIndex = sideIndex,
          ledIndex = ledIndex,
          layerIndex = layerIndex,
          color = color,
        });
      }
    }

    public int GetSingleColor(int index) {
      if (this.config.beatBroadcaster.CurrentlyFlashedOff) {
        return 0x000000;
      }
      return LEDColor.ScaleColor(
        this.config.colorPalette.GetSingleColor(index),
        this.config.stageBrightness
      );
    }

    public int GetGradientColor(
      int index,
      double pixelPos,
      double focusPos,
      bool wrap
    ) {
      if (
        this.config.beatBroadcaster.CurrentlyFlashedOff ||
        this.config.colorPalette.colors[index] == null
      ) {
        return 0x000000;
      }
      if (!this.config.colorPalette.colors[index].IsGradient) {
        return this.GetSingleColor(index);
      }
      return LEDColor.ScaleColor(
        this.config.colorPalette.GetGradientColor(
          index,
          pixelPos,
          focusPos,
          wrap
        ),
        this.config.stageBrightness
      );
    }

    /**
     * This method's different from GetSingleColor is that it uses an "enabled
     * index", ie. "the nth color that is computer-enabled". Visualizers that
     * are algorithmically driven should use this method, so that the user is
     * able to decide which colors they are allowed to use.
     */
    public int GetSingleComputerColor(int colorIndex) {
      if (this.config.beatBroadcaster.CurrentlyFlashedOff) {
        return 0x000000;
      }
      return LEDColor.ScaleColor(
        this.config.colorPalette.GetSingleComputerColor(colorIndex),
        this.config.stageBrightness
      );
    }

    /**
     * This method's difference from GetGradientColor is that it uses an
     * "enabled index", ie. "the nth color that is computer-enabled".
     * Visualizers that are algorithmically driven should use this method,so
     * that the user is able to decide which colors they are allowed to use.
     */
    public int GetGradientComputerColor(
      int colorIndex,
      double pixelPos,
      double focusPos,
      bool wrap
    ) {
      if (this.config.beatBroadcaster.CurrentlyFlashedOff) {
        return 0x000000;
      }
      int? index = this.config.colorPalette.GetIndexOfEnabledIndex(
        colorIndex
      );
      if (
        !index.HasValue ||
        this.config.colorPalette.colors[index.Value] == null
      ) {
        return 0x000000;
      }
      if (!this.config.colorPalette.colors[index.Value].IsGradient) {
        return this.GetSingleColor(index.Value);
      }
      return LEDColor.ScaleColor(
        this.config.colorPalette.GetGradientColor(
          index.Value,
          pixelPos,
          focusPos,
          wrap
        ),
        this.config.stageBrightness
      );
    }

  }

}