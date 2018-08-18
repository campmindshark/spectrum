using BarGeometry;
using Spectrum.Base;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Spectrum.LEDs {
  public class LEDGeometryOutput : Output {
    private TeensyAPI[] teensies;
    private OPCAPI opcAPI;
    private Configuration config;
    private List<Visualizer> visualizers;
    public IcosahedronModel[] Icosahedrons;
    public OctahedronModel[] Octahedrons;

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
        if (!this.config.geometryEnabled) {
          return;
        }
        if (value) {
          if (this.config.geometryHardwareSetup == 0) {
            this.initializeTeensies();
          } else if (this.config.geometryHardwareSetup == 1) {
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
        return this.config.geometryEnabled || this.config.geometrySimulationEnabled;
      }
    }

    public LEDGeometryOutput(Configuration config) {
      this.config = config;
      this.visualizers = new List<Visualizer>();
      this.config.PropertyChanged += this.ConfigUpdated;
      this.Icosahedrons = new[] {
        new IcosahedronModel(8)
      };
      this.Octahedrons = new[] {
        new OctahedronModel(0),
        new OctahedronModel(7)
      };
    }

    public void OperatorUpdate() {
      if (this.config.geometryHardwareSetup == 1 && this.opcAPI != null) {
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
      if (this.config.geometryHardwareSetup == 0 && this.teensies != null) {
        this.teensies[0].Flush();
        this.teensies[1].Flush();
      }
      if (this.config.geometryHardwareSetup == 1 && this.opcAPI != null) {
        this.opcAPI.Flush();
      }
      if (this.config.geometrySimulationEnabled) {
        this.config.geometryCommandQueue.Enqueue(
          new GeometryLEDCommand() { isFlush = true }
        );
      }
    }

    /// <summary>
    ///  Set an led's color by absolute led index
    /// </summary>
    /// <param name="ledIndex"></param>
    /// <param name="color"></param>
    public void SetPixel(byte channel, int ledIndex, int color) {
      //TODO: add bool param for inside or outside 
      //      path when that's added to the geometry
      if (this.config.geometryHardwareSetup != 1 || this.opcAPI == null) {
        //No implementation other than opc at this time
        //Also return if opcApi is null
        return;
      }
      this.opcAPI.SetPixel(channel, ledIndex, color);
    }
    /// <summary>
    ///  Set an led's color by strip and index on strip
    /// </summary>
    /// <param name="ledIndex"></param>
    /// <param name="stripIndex"></param>
    /// <param name="color"></param>
    public void SetPixel(
      GeometryShapeType type,
      int shapeId,
      int stripId,
      int ledIndex,
      int color) {
      switch (type) {
        case GeometryShapeType.Icosahedron:
          SetIcosahedronPixel(shapeId, stripId, ledIndex, color);
          break;
        case GeometryShapeType.Octahedron:
          SetOctahedronPixel(shapeId, stripId, ledIndex, color);
          break;
        default:
          //Unrecognized Shape, set no pixels
          break;
      }
    }

    private void SetIcosahedronPixel(int shapeId, int stripId, int ledIndex, int color) {
      byte channelId = 0;
      int startStripId = 0, absIndex = 0;
      for (int i = 0; i < Icosahedrons[shapeId].Channels.Count; i++) {
        if (stripId >= Icosahedrons[shapeId].Channels[i].StartStripId) {
          channelId = Icosahedrons[shapeId].Channels[i].OPCChannelId;
          startStripId = Icosahedrons[shapeId].Channels[i].StartStripId;
          for (int j = startStripId; j < stripId; j++) {
            absIndex += Icosahedrons[shapeId].Strips[j].LedCount;
          }
          absIndex += ledIndex;
        }
      }
      opcAPI.SetPixel(channelId, absIndex, color);
    }

    private void SetOctahedronPixel(int shapeId, int stripId, int ledIndex, int color) {
      byte channelId = 0;
      int startStripId = 0, absIndex = 0;
      for (int i = 0; i < Octahedrons[shapeId].Channels.Count; i++) {
        if (stripId >= Octahedrons[shapeId].Channels[i].StartStripId) {
          channelId = Octahedrons[shapeId].Channels[i].OPCChannelId;
          startStripId = Octahedrons[shapeId].Channels[i].StartStripId;
          for (int j = startStripId; j < stripId; j++) {
            absIndex += Octahedrons[shapeId].Strips[j].LedCount;
          }
          absIndex += ledIndex;
          break;
        }
      }
      opcAPI.SetPixel(channelId, absIndex, color);
    }

    public int GetSingleColor(int index) {
      if (this.config.beatBroadcaster.CurrentlyFlashedOff) {
        return 0x000000;
      }
      int absoluteIndex = LEDColor.GetAbsoluteColorIndex(index, this.config.colorPaletteIndex);
      return LEDColor.ScaleColor(
        this.config.colorPalette.GetSingleColor(absoluteIndex),
        this.config.barBrightness
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
        this.config.barBrightness
      );
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (!this.active) {
        return;
      }

      if (e.PropertyName == "geometryHardwareSetup") {
        if (!this.config.geometryEnabled) {
        } else if (this.config.geometryHardwareSetup == 0) {
          if (this.opcAPI != null) {
            this.opcAPI.Active = false;
          }
          this.initializeTeensies();
        } else if (this.config.geometryHardwareSetup == 1) {
          if (this.teensies != null) {
            this.teensies[0].Active = false;
            this.teensies[1].Active = false;
          }
          this.initializeOPCAPI();
        } else if (
         e.PropertyName == "geometryTeensyUSBPort1" ||
           e.PropertyName == "geometryTeensyUSBPort2"
       ) {
          if (this.config.geometryHardwareSetup == 0 && this.config.geometryEnabled) {
            this.teensies[0].Active = false;
            this.teensies[1].Active = false;
            this.initializeTeensies();
          }
        } else if (e.PropertyName == "geometryBeagleboneOPCAddress") {
          if (this.config.geometryHardwareSetup == 1 && this.config.geometryEnabled) {
            this.opcAPI.Active = false;
            this.initializeOPCAPI();
          }
        } else if (e.PropertyName == "geometryOutputInSeparateThread") {
          if (!this.config.geometryEnabled) {
          } else if (this.config.geometryHardwareSetup == 0) {
            if (this.teensies != null) {
              this.teensies[0].Active = false;
              this.teensies[1].Active = false;
            }
            this.initializeTeensies();
          } else if (this.config.geometryHardwareSetup == 1) {
            if (this.opcAPI != null) {
              this.opcAPI.Active = false;
            }
            this.initializeOPCAPI();
          }
        }
      }
    }

    private void initializeOPCAPI() {
      var opcAddress = this.config.geometryBeagleboneOPCAddress;
      string[] parts = opcAddress.Split(':');
      if (parts.Length < 3) {
        opcAddress += ":0"; // default to channel 0
      }
      this.opcAPI = new OPCAPI(
        opcAddress,
        this.config.geometryOutputInSeparateThread,
        newFPS => this.config.geometryBeagleboneOPCFPS = newFPS
      ) {
        Active = this.active &&
        this.config.geometryHardwareSetup == 1
      };
    }

    private void initializeTeensies() {
      this.teensies = null;
      try {
        //No teensie implementation at this time
      }
      catch (Exception) {
      }
    }


  }
}
