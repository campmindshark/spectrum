using Spectrum.Base;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System;

namespace Spectrum.LEDs {

  public class LEDStageOutput : Output {

    private OPCAPI opcAPI;
    private readonly Configuration config;
    private readonly List<Visualizer> visualizers;
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
      if (
        this.Active &&
        this.config.stageEnabled &&
        (e.PropertyName == "stageBeagleboneOPCAddress" ||
          e.PropertyName == "stageOutputInSeparateThread")
      ) {
        if (this.opcAPI != null) {
          this.opcAPI.Active = false;
        }
        this.initializeOPCAPI();
      } else if (e.PropertyName == "stageSideLengths") {
        this.calculateMaxTriangleLength();
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
      this.opcAPI.Active = this.active;
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
          this.initializeOPCAPI();
        } else if (this.opcAPI != null) {
          this.opcAPI.Active = false;
        }
      }
    }

    public bool Enabled {
      get {
        return this.config.stageEnabled || this.config.stageSimulationEnabled;
      }
    }

    public void OperatorUpdate() {
      if (this.opcAPI != null) {
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
      if (this.opcAPI != null) {
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
      int pixelIndex = this.maxTriangleLength * (sideIndex / 3) + ledIndex;
      var baseSideIndex = (sideIndex / 3) * 3;
      for (int i = 0; i < layerIndex; i++) {
        // We increment pixelIndex for every complete layer on the target triangle
        pixelIndex += this.config.stageSideLengths[baseSideIndex] +
          this.config.stageSideLengths[baseSideIndex + 1] +
          this.config.stageSideLengths[baseSideIndex + 2];
      }
      for (int i = baseSideIndex; i < sideIndex; i++) {
        // We increment pixelIndex for every complete side on the
        // target triangle and layer
        pixelIndex += this.config.stageSideLengths[i];
      }
      if (this.opcAPI != null) {
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
      int absoluteIndex = LEDColor.GetAbsoluteColorIndex(
        index,
        this.config.colorPaletteIndex
      );
      return LEDColor.ScaleColor(
        this.config.colorPalette.GetSingleColor(absoluteIndex),
        this.config.stageBrightness
      );
    }

    public int GetGradientColor(
      int index,
      double pixelPos,
      double focusPos,
      bool wrap
    ) {
      if (this.config.beatBroadcaster.CurrentlyFlashedOff) {
        return 0x000000;
      }
      int absoluteIndex = LEDColor.GetAbsoluteColorIndex(
        index,
        this.config.colorPaletteIndex
      );
      if (this.config.colorPalette.colors[absoluteIndex] == null) {
        return 0x000000;
      }
      if (!this.config.colorPalette.colors[absoluteIndex].IsGradient) {
        return this.GetSingleColor(absoluteIndex);
      }
      return LEDColor.ScaleColor(
        this.config.colorPalette.GetGradientColor(
          absoluteIndex,
          pixelPos,
          focusPos,
          wrap
        ),
        this.config.stageBrightness
      );
    }

  }

}