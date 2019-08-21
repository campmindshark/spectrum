using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using System.ComponentModel;

namespace Spectrum.LEDs {

  enum LEDDomeStrutTypes { Yellow, Red, Blue, Green, Purple, Orange };

  public class LEDDomeOutput : Output {

    // There are 8 strands coming out of each control box. For each of these
    // strands, this variable represents the sequence of strut types
    private static readonly LEDDomeStrutTypes[][] controlBoxStrutOrder =
      new LEDDomeStrutTypes[][] {
        new LEDDomeStrutTypes[] {
          LEDDomeStrutTypes.Green, LEDDomeStrutTypes.Blue,
          LEDDomeStrutTypes.Orange, LEDDomeStrutTypes.Orange,
          LEDDomeStrutTypes.Yellow,
        },
        new LEDDomeStrutTypes[] {
          LEDDomeStrutTypes.Orange, LEDDomeStrutTypes.Blue,
          LEDDomeStrutTypes.Purple, LEDDomeStrutTypes.Blue,
          LEDDomeStrutTypes.Red,
        },
        new LEDDomeStrutTypes[] {
          LEDDomeStrutTypes.Red, LEDDomeStrutTypes.Blue,
          LEDDomeStrutTypes.Green, LEDDomeStrutTypes.Green,
          LEDDomeStrutTypes.Blue,
        },
        new LEDDomeStrutTypes[] {
          LEDDomeStrutTypes.Green, LEDDomeStrutTypes.Blue,
          LEDDomeStrutTypes.Red, LEDDomeStrutTypes.Yellow,
          LEDDomeStrutTypes.Yellow,
        },
        new LEDDomeStrutTypes[] {
          LEDDomeStrutTypes.Green, LEDDomeStrutTypes.Purple,
          LEDDomeStrutTypes.Blue, LEDDomeStrutTypes.Red,
        },
        new LEDDomeStrutTypes[] {
          LEDDomeStrutTypes.Green, LEDDomeStrutTypes.Purple,
          LEDDomeStrutTypes.Purple, LEDDomeStrutTypes.Green,
          LEDDomeStrutTypes.Green,
        },
        new LEDDomeStrutTypes[] {
          LEDDomeStrutTypes.Orange, LEDDomeStrutTypes.Yellow,
          LEDDomeStrutTypes.Yellow, LEDDomeStrutTypes.Red,
          LEDDomeStrutTypes.Red,
        },
        new LEDDomeStrutTypes[] {
          LEDDomeStrutTypes.Blue, LEDDomeStrutTypes.Blue,
          LEDDomeStrutTypes.Blue, LEDDomeStrutTypes.Yellow,
        },
      };
    private static readonly Dictionary<LEDDomeStrutTypes, int> strutLengths =
      new Dictionary<LEDDomeStrutTypes, int> {
        [LEDDomeStrutTypes.Yellow] = 34,
        [LEDDomeStrutTypes.Red] = 40,
        [LEDDomeStrutTypes.Blue] = 40,
        [LEDDomeStrutTypes.Orange] = 40,
        [LEDDomeStrutTypes.Green] = 42,
        [LEDDomeStrutTypes.Purple] = 44,
      };
    // We assign each strut an index. This variable maps that strut index to
    // a control box, and an index within that control box. The control box
    // index is based on the sequence of the 8 strands and the struts that
    // comprise them. See comment at the top of FindStrutIndex for more details.
    private static readonly Tuple<int, int>[] strutPositions = new Tuple<int, int>[] {
      new Tuple<int, int>(0, 22), new Tuple<int, int>(0, 23),
      new Tuple<int, int>(1, 36), new Tuple<int, int>(1, 21),
      new Tuple<int, int>(1, 22), new Tuple<int, int>(1, 23),
      new Tuple<int, int>(2, 36), new Tuple<int, int>(2, 21),
      new Tuple<int, int>(2, 22), new Tuple<int, int>(2, 23),
      new Tuple<int, int>(3, 36), new Tuple<int, int>(3, 21),
      new Tuple<int, int>(3, 22), new Tuple<int, int>(3, 23),
      new Tuple<int, int>(4, 36), new Tuple<int, int>(4, 21),
      new Tuple<int, int>(4, 22), new Tuple<int, int>(4, 23),
      new Tuple<int, int>(0, 36), new Tuple<int, int>(0, 21),
      new Tuple<int, int>(0, 5), new Tuple<int, int>(0, 19),
      new Tuple<int, int>(1, 30), new Tuple<int, int>(1, 29),
      new Tuple<int, int>(1, 5), new Tuple<int, int>(1, 19),
      new Tuple<int, int>(2, 30), new Tuple<int, int>(2, 29),
      new Tuple<int, int>(2, 5), new Tuple<int, int>(2, 19),
      new Tuple<int, int>(3, 30), new Tuple<int, int>(3, 29),
      new Tuple<int, int>(3, 5), new Tuple<int, int>(3, 19),
      new Tuple<int, int>(4, 30), new Tuple<int, int>(4, 29),
      new Tuple<int, int>(4, 5), new Tuple<int, int>(4, 19),
      new Tuple<int, int>(0, 30), new Tuple<int, int>(0, 29),
      new Tuple<int, int>(0, 11), new Tuple<int, int>(1, 1),
      new Tuple<int, int>(1, 25), new Tuple<int, int>(1, 11),
      new Tuple<int, int>(2, 1), new Tuple<int, int>(2, 25),
      new Tuple<int, int>(2, 11), new Tuple<int, int>(3, 1),
      new Tuple<int, int>(3, 25), new Tuple<int, int>(3, 11),
      new Tuple<int, int>(4, 1), new Tuple<int, int>(4, 25),
      new Tuple<int, int>(4, 11), new Tuple<int, int>(0, 1),
      new Tuple<int, int>(0, 25), new Tuple<int, int>(0, 13),
      new Tuple<int, int>(1, 27), new Tuple<int, int>(1, 13),
      new Tuple<int, int>(2, 27), new Tuple<int, int>(2, 13),
      new Tuple<int, int>(3, 27), new Tuple<int, int>(3, 13),
      new Tuple<int, int>(4, 27), new Tuple<int, int>(4, 13),
      new Tuple<int, int>(0, 27), new Tuple<int, int>(1, 9),
      new Tuple<int, int>(2, 9), new Tuple<int, int>(3, 9),
      new Tuple<int, int>(4, 9), new Tuple<int, int>(0, 9),
      new Tuple<int, int>(0, 15), new Tuple<int, int>(0, 16),
      new Tuple<int, int>(0, 17), new Tuple<int, int>(0, 18),
      new Tuple<int, int>(1, 37), new Tuple<int, int>(1, 33),
      new Tuple<int, int>(1, 35), new Tuple<int, int>(1, 20),
      new Tuple<int, int>(1, 15), new Tuple<int, int>(1, 16),
      new Tuple<int, int>(1, 17), new Tuple<int, int>(1, 18),
      new Tuple<int, int>(2, 37), new Tuple<int, int>(2, 33),
      new Tuple<int, int>(2, 35), new Tuple<int, int>(2, 20),
      new Tuple<int, int>(2, 15), new Tuple<int, int>(2, 16),
      new Tuple<int, int>(2, 17), new Tuple<int, int>(2, 18),
      new Tuple<int, int>(3, 37), new Tuple<int, int>(3, 33),
      new Tuple<int, int>(3, 35), new Tuple<int, int>(3, 20),
      new Tuple<int, int>(3, 15), new Tuple<int, int>(3, 16),
      new Tuple<int, int>(3, 17), new Tuple<int, int>(3, 18),
      new Tuple<int, int>(4, 37), new Tuple<int, int>(4, 33),
      new Tuple<int, int>(4, 35), new Tuple<int, int>(4, 20),
      new Tuple<int, int>(4, 15), new Tuple<int, int>(4, 16),
      new Tuple<int, int>(4, 17), new Tuple<int, int>(4, 18),
      new Tuple<int, int>(0, 37), new Tuple<int, int>(0, 33),
      new Tuple<int, int>(0, 35), new Tuple<int, int>(0, 20),
      new Tuple<int, int>(0, 24), new Tuple<int, int>(0, 6),
      new Tuple<int, int>(0, 10), new Tuple<int, int>(1, 31),
      new Tuple<int, int>(1, 32), new Tuple<int, int>(1, 34),
      new Tuple<int, int>(1, 0), new Tuple<int, int>(1, 24),
      new Tuple<int, int>(1, 6), new Tuple<int, int>(1, 10),
      new Tuple<int, int>(2, 31), new Tuple<int, int>(2, 32),
      new Tuple<int, int>(2, 34), new Tuple<int, int>(2, 0),
      new Tuple<int, int>(2, 24), new Tuple<int, int>(2, 6),
      new Tuple<int, int>(2, 10), new Tuple<int, int>(3, 31),
      new Tuple<int, int>(3, 32), new Tuple<int, int>(3, 34),
      new Tuple<int, int>(3, 0), new Tuple<int, int>(3, 24),
      new Tuple<int, int>(3, 6), new Tuple<int, int>(3, 10),
      new Tuple<int, int>(4, 31), new Tuple<int, int>(4, 32),
      new Tuple<int, int>(4, 34), new Tuple<int, int>(4, 0),
      new Tuple<int, int>(4, 24), new Tuple<int, int>(4, 6),
      new Tuple<int, int>(4, 10), new Tuple<int, int>(0, 31),
      new Tuple<int, int>(0, 32), new Tuple<int, int>(0, 34),
      new Tuple<int, int>(0, 0), new Tuple<int, int>(0, 7),
      new Tuple<int, int>(0, 12), new Tuple<int, int>(1, 2),
      new Tuple<int, int>(1, 28), new Tuple<int, int>(1, 26),
      new Tuple<int, int>(1, 7), new Tuple<int, int>(1, 12),
      new Tuple<int, int>(2, 2), new Tuple<int, int>(2, 28),
      new Tuple<int, int>(2, 26), new Tuple<int, int>(2, 7),
      new Tuple<int, int>(2, 12), new Tuple<int, int>(3, 2),
      new Tuple<int, int>(3, 28), new Tuple<int, int>(3, 26),
      new Tuple<int, int>(3, 7), new Tuple<int, int>(3, 12),
      new Tuple<int, int>(4, 2), new Tuple<int, int>(4, 28),
      new Tuple<int, int>(4, 26), new Tuple<int, int>(4, 7),
      new Tuple<int, int>(4, 12), new Tuple<int, int>(0, 2),
      new Tuple<int, int>(0, 28), new Tuple<int, int>(0, 26),
      new Tuple<int, int>(0, 14), new Tuple<int, int>(1, 3),
      new Tuple<int, int>(1, 8), new Tuple<int, int>(1, 14),
      new Tuple<int, int>(2, 3), new Tuple<int, int>(2, 8),
      new Tuple<int, int>(2, 14), new Tuple<int, int>(3, 3),
      new Tuple<int, int>(3, 8), new Tuple<int, int>(3, 14),
      new Tuple<int, int>(4, 3), new Tuple<int, int>(4, 8),
      new Tuple<int, int>(4, 14), new Tuple<int, int>(0, 3),
      new Tuple<int, int>(0, 8), new Tuple<int, int>(1, 4),
      new Tuple<int, int>(2, 4), new Tuple<int, int>(3, 4),
      new Tuple<int, int>(4, 4), new Tuple<int, int>(0, 4),
    };

    private OPCAPI opcAPI;
    private readonly Configuration config;
    private readonly List<Visualizer> visualizers;
    private readonly HashSet<int> reservedStruts;
    private static readonly int maxStripLength;

    private static int calculateMaxStripLength() {
      int maxLength = 0;
      foreach (LEDDomeStrutTypes[] struts in controlBoxStrutOrder) {
        int length = 0;
        foreach (LEDDomeStrutTypes type in struts) {
          length += strutLengths[type];
        }
        if (length > maxLength) {
          maxLength = length;
        }
      }
      return maxLength;
    }

    static LEDDomeOutput() {
      maxStripLength = calculateMaxStripLength();
    }

    public LEDDomeOutput(Configuration config) {
      this.config = config;
      this.visualizers = new List<Visualizer>();
      this.reservedStruts = new HashSet<int>();
      this.config.PropertyChanged += ConfigUpdated;
    }

    public void RegisterVisualizer(Visualizer visualizer) {
      this.visualizers.Add(visualizer);
    }

    public Visualizer[] GetVisualizers() {
      return this.visualizers.ToArray();
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (
        e.PropertyName != "domeBeagleboneOPCAddress" &&
        e.PropertyName != "domeOutputInSeparateThread" &&
        e.PropertyName != "domeEnabled"
      ) {
        return;
      }
      if (this.opcAPI != null) {
        this.opcAPI.Active = false;
      }
      if (this.active && this.config.domeEnabled) {
        this.initializeOPCAPI();
      }
    }

    private void initializeOPCAPI() {
      var opcAddress = this.config.domeBeagleboneOPCAddress;
      string[] parts = opcAddress.Split(':');
      if (parts.Length < 3) {
        opcAddress += ":0"; // default to channel 0
      }
      this.opcAPI = new OPCAPI(
        opcAddress,
        this.config.domeOutputInSeparateThread,
        newFPS => this.config.domeBeagleboneOPCFPS = newFPS
      );
      this.opcAPI.Active = this.active;
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
        if (value && this.config.domeEnabled) {
          this.initializeOPCAPI();
        } else if (this.opcAPI != null) {
          this.opcAPI.Active = false;
        }
      }
    }

    public bool Enabled {
      get {
        return this.config.domeEnabled || this.config.domeSimulationEnabled;
      }
    }

    public void OperatorUpdate() {
      if (this.opcAPI != null) {
         this.opcAPI.OperatorUpdate();
      }
    }

    public void Flush() {
      if (this.opcAPI != null) {
         this.opcAPI.Flush();
      }
      if (this.config.domeSimulationEnabled) {
        this.config.domeCommandQueue.Enqueue(
          new DomeLEDCommand() { isFlush = true }
        );
      }
    }

    private void SetDevicePixel(int controlBoxIndex, int pixelIndex, int color) {
      lock (this.visualizers) {
        if (this.opcAPI != null) {
          int totalPixelIndex = controlBoxIndex * (maxStripLength * 8) + pixelIndex;
          if (controlBoxIndex > 3) {
            // NatShip rev A seems to have a design defect where "channels" 34
            // and 45 are broken. To get around this, we are skipping 4 channels
            totalPixelIndex += maxStripLength * 4;
          }
          this.opcAPI.SetPixel(totalPixelIndex, color);
        }
      }
    }

    public void SetPixel(int strutIndex, int ledIndex, int color) {
      ledIndex += this.config.domeSkipLEDs;
      int pixelIndex = ledIndex;
      Tuple<int, int> strutPosition = strutPositions[strutIndex];
      int strutsLeft = strutPosition.Item2;
      int i = 0;
      while (controlBoxStrutOrder[i].Length <= strutsLeft) {
        strutsLeft -= controlBoxStrutOrder[i].Length;
        i++;
        pixelIndex += maxStripLength;
      }
      for (int j = 0; j < strutsLeft; j++) {
        pixelIndex += strutLengths[controlBoxStrutOrder[i][j]];
      }
      this.SetDevicePixel(strutPosition.Item1, pixelIndex, color);
      if (this.config.domeSimulationEnabled) {
        this.config.domeCommandQueue.Enqueue(new DomeLEDCommand() {
          strutIndex = strutIndex,
          ledIndex = ledIndex,
          color = color,
        });
      }
    }

    public void SetBarPixel(int pixelIndex, int color) {
      this.SetDevicePixel(5, pixelIndex, color);
    }

    public void ReserveStrut(int strutIndex) {
      if (this.reservedStruts.Contains(strutIndex)) {
        throw new Exception("User attempted to reserve unavailable strut");
      }
      this.reservedStruts.Add(strutIndex);
    }

    public void ReleaseStrut(int strutIndex) {
      if (!this.reservedStruts.Contains(strutIndex)) {
        throw new Exception("User attempted to release available strut");
      }
      this.reservedStruts.Remove(strutIndex);
    }

    public HashSet<int> ReservedStruts() {
      return this.reservedStruts;
    }

    /**
     * This function takes as input a controlBoxIndex, and a special index
     * called controlBoxStrutIndex. This second index goes from 0-37, and it
     * identifies a strut uniquely for a given control box. (There are currently
     * 190 struts and 5 control boxes). The order is such so that the struts
     * appear in the order they are plugged into the control box, eg. all of the
     * struts plugged into the first of the eight outputs on the control box,
     * and then all of the struts plugged into the second, etc. This method is
     * primarily useful for debugging.
     */
    public static int FindStrutIndex(
      int controlBoxIndex,
      int controlBoxStrutIndex
    ) {
      for (int i = 0; i < strutPositions.Length; i++) {
        var strutPosition = strutPositions[i];
        if (
          controlBoxIndex == strutPosition.Item1 &&
          controlBoxStrutIndex == strutPosition.Item2
        ) {
          return i;
        }
      }
      return -1;
    }

    public static int GetNumStruts() {
      return strutPositions.Length;
    }

    /**
     * Doesn't take into account Configuration.domeSkipLEDs. Don't use this
     * unless you are Strut. Use Strut.Length instead.
     */
    public static int GetNumLEDs(int strutIndex) {
      var strutPosition = strutPositions[strutIndex];
      int strutsLeft = strutPosition.Item2;
      int i = 0;
      while (controlBoxStrutOrder[i].Length <= strutsLeft) {
        strutsLeft -= controlBoxStrutOrder[i].Length;
        i++;
      }
      return strutLengths[controlBoxStrutOrder[i][strutsLeft]];
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
        this.config.domeMaxBrightness * this.config.domeBrightness
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
      if (
        this.config.colorPalette.colors == null ||
        this.config.colorPalette.colors.Length <= absoluteIndex ||
        this.config.colorPalette.colors[absoluteIndex] == null
      ) {
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
        this.config.domeMaxBrightness * this.config.domeBrightness
      );
    }

    public int GetGradientBetweenColors(
      int minIndex,
      int maxIndex,
      double pixelPos,
      double focusPos,
      bool wrap
    ) {
      // Return a color evenly scaled between min index and max index, based on the pixel position.
      if (pixelPos < 0 || pixelPos > 1) {
        throw new ArgumentException("Pixel Position out of range: " + pixelPos.ToString());
      }
      if (this.config.beatBroadcaster.CurrentlyFlashedOff) {
        return 0x000000;
      }
      var num_colors = maxIndex - minIndex;
      int minColorIdx = (int)(pixelPos * num_colors);
      int maxColorIdx = (int)(pixelPos * num_colors + 1);
      // Rescale the position, so it's between the colors
      // (pixelPos - min_color_idx/num_colors)*num_colors
      double scaledPixelPos = (pixelPos - 1.0 * minColorIdx / num_colors) * num_colors;
      if (scaledPixelPos < 0 || scaledPixelPos > 1) {
        throw new ArgumentException("Pixel Position out of range: " + pixelPos.ToString());
      }
      int absoluteIndexMin = LEDColor.GetAbsoluteColorIndex(
        minColorIdx, this.config.colorPaletteIndex
      );
      int absoluteIndexMax = LEDColor.GetAbsoluteColorIndex(
        maxColorIdx, this.config.colorPaletteIndex
      );
      if (
        this.config.colorPalette.colors == null ||
        this.config.colorPalette.colors.Length <= absoluteIndexMin ||
        this.config.colorPalette.colors[absoluteIndexMin] == null
      ) {
        return 0x000000;
      }
      if (
        this.config.colorPalette.colors == null ||
        this.config.colorPalette.colors.Length <= absoluteIndexMax ||
        this.config.colorPalette.colors[absoluteIndexMax] == null
      ) {
        return 0x000000;
      }
      if (!this.config.colorPalette.colors[absoluteIndexMin].IsGradient) {
        return this.GetSingleColor(absoluteIndexMin);
      }
      LEDColor color = new LEDColor(
        this.GetSingleColor(minColorIdx),
        this.GetSingleColor(maxColorIdx));
      return LEDColor.ScaleColor(
        color.GradientColor(scaledPixelPos, focusPos, wrap),
        this.config.domeMaxBrightness * this.config.domeBrightness
      );
    }
  }

}
