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

    private static LEDDomeStrutTypes[] teensyStrutOrder =
      new LEDDomeStrutTypes[] {
        LEDDomeStrutTypes.Green, LEDDomeStrutTypes.Blue,
        LEDDomeStrutTypes.Orange, LEDDomeStrutTypes.Orange,
        LEDDomeStrutTypes.Yellow, LEDDomeStrutTypes.Orange,
        LEDDomeStrutTypes.Blue, LEDDomeStrutTypes.Purple,
        LEDDomeStrutTypes.Blue, LEDDomeStrutTypes.Red, LEDDomeStrutTypes.Red,
        LEDDomeStrutTypes.Blue, LEDDomeStrutTypes.Green,
        LEDDomeStrutTypes.Green, LEDDomeStrutTypes.Blue,
        LEDDomeStrutTypes.Green, LEDDomeStrutTypes.Blue, LEDDomeStrutTypes.Red,
        LEDDomeStrutTypes.Yellow, LEDDomeStrutTypes.Yellow,
        LEDDomeStrutTypes.Green, LEDDomeStrutTypes.Purple,
        LEDDomeStrutTypes.Blue, LEDDomeStrutTypes.Red, LEDDomeStrutTypes.Green,
        LEDDomeStrutTypes.Purple, LEDDomeStrutTypes.Purple,
        LEDDomeStrutTypes.Green, LEDDomeStrutTypes.Green,
        LEDDomeStrutTypes.Orange, LEDDomeStrutTypes.Yellow,
        LEDDomeStrutTypes.Yellow, LEDDomeStrutTypes.Red, LEDDomeStrutTypes.Red,
        LEDDomeStrutTypes.Blue, LEDDomeStrutTypes.Blue, LEDDomeStrutTypes.Blue,
        LEDDomeStrutTypes.Yellow,
      };
    private static Dictionary<LEDDomeStrutTypes, int> strutLengths =
      new Dictionary<LEDDomeStrutTypes, int> {
        [LEDDomeStrutTypes.Yellow] = 34,
        [LEDDomeStrutTypes.Red] = 40,
        [LEDDomeStrutTypes.Blue] = 40,
        [LEDDomeStrutTypes.Orange] = 40,
        [LEDDomeStrutTypes.Green] = 42,
        [LEDDomeStrutTypes.Purple] = 44,
      };
    private static Tuple<int, int>[] strutPositions = new Tuple<int, int>[] {
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

    private SimpleTeensyAPI[] teensies;
    private Configuration config;
    private List<Visualizer> visualizers;
    private HashSet<int> reservedStruts;

    public LEDDomeOutput(Configuration config) {
      this.config = config;
      this.visualizers = new List<Visualizer>();
      this.reservedStruts = new HashSet<int>();
      bool domeEnabled = this.config.domeEnabled;
      lock (this.visualizers) {
        if (domeEnabled) {
          this.teensies = this.getNewTeensies();
        }
      }
      this.config.PropertyChanged += ConfigUpdated;
    }

    public void RegisterVisualizer(Visualizer visualizer) {
      this.visualizers.Add(visualizer);
    }

    public Visualizer[] GetVisualizers() {
      return this.visualizers.ToArray();
    }

    private void ConfigUpdated(object sender, PropertyChangedEventArgs e) {
      if (e.PropertyName != "domeEnabled") {
        return;
      }
      bool domeEnabled = this.config.domeEnabled;
      lock (this.visualizers) {
        if (domeEnabled) {
          this.teensies = this.getNewTeensies();
        }
        if (this.Active) {
          foreach (var teensy in this.teensies) {
            teensy.Active = domeEnabled;
          }
        }
        if (!domeEnabled) {
          this.teensies = null;
        }
      }
    }

    private SimpleTeensyAPI[] getNewTeensies() {
      return new SimpleTeensyAPI[] {
        new SimpleTeensyAPI(
          this.config.domeTeensyUSBPort1,
          this.config.domeOutputInSeparateThread
        ),
        new SimpleTeensyAPI(
          this.config.domeTeensyUSBPort2,
          this.config.domeOutputInSeparateThread
        ),
        new SimpleTeensyAPI(
          this.config.domeTeensyUSBPort3,
          this.config.domeOutputInSeparateThread
        ),
        new SimpleTeensyAPI(
          this.config.domeTeensyUSBPort4,
          this.config.domeOutputInSeparateThread
        ),
        new SimpleTeensyAPI(
          this.config.domeTeensyUSBPort5,
          this.config.domeOutputInSeparateThread
        ),
      };
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
        lock (this.visualizers) {
          if (this.teensies != null) {
            foreach (var teensy in this.teensies) {
              teensy.Active = value;
            }
          }
          this.active = value;
        }
      }
    }

    public bool Enabled {
      get {
        return this.config.domeEnabled || this.config.domeSimulationEnabled;
      }
    }

    public void OperatorUpdate() {
      lock (this.visualizers) {
        if (this.teensies == null) {
          return;
        }
        foreach (var teensy in this.teensies) {
          teensy.OperatorUpdate();
        }
      }
    }

    public void Flush() {
      lock (this.visualizers) {
        if (this.teensies != null) {
          foreach (var teensy in this.teensies) {
            teensy.Flush();
          }
        }
      }
      this.config.domeCommandQueue.Enqueue(new LEDCommand() { isFlush = true });
    }

    private void SetTeensyPixel(int teensyIndex, int pixelIndex, int color) {
      lock (this.visualizers) {
        if (this.teensies != null) {
          this.teensies[teensyIndex].SetPixel(pixelIndex, color);
        }
      }
    }

    public void SetPixel(int strutIndex, int ledIndex, int color) {
      Tuple<int, int> tuple = strutPositions[strutIndex];
      ledIndex += this.config.domeSkipLEDs;
      int pixelIndex = ledIndex;
      for (int i = 0; i < tuple.Item2; i++) {
        pixelIndex += strutLengths[teensyStrutOrder[i]];
      }
      this.SetTeensyPixel(tuple.Item1, pixelIndex, color);
      this.config.domeCommandQueue.Enqueue(new LEDCommand() {
        strutIndex = strutIndex,
        ledIndex = ledIndex,
        color = color,
      });
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

    public static int FindStrutIndex(int teensyIndex, int teensyStrutIndex) {
      for (int i = 0; i < strutPositions.Length; i++) {
        var strutPosition = strutPositions[i];
        if (
          teensyIndex == strutPosition.Item1 &&
          teensyStrutIndex == strutPosition.Item2
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
      return strutLengths[teensyStrutOrder[strutPosition.Item2]];
    }

    public int GetSingleColor(int index) {
      return LEDColor.ScaleColor(
        this.config.domeColorPalette.GetSingleColor(index),
        this.config.domeMaxBrightness * this.config.domeBrightness
      );
    }

    public int GetGradientColor(
      int index,
      double pixelPos,
      double focusPos,
      bool wrap
    ) {
      if (this.config.domeColorPalette.colors[index] == null) {
        return 0x000000;
      }
      if (!this.config.domeColorPalette.colors[index].IsGradient) {
        return GetSingleColor(index);
      }
      return LEDColor.ScaleColor(
        this.config.domeColorPalette.GetGradientColor(
          index,
          pixelPos,
          focusPos,
          wrap
        ),
        this.config.domeMaxBrightness * this.config.domeBrightness
      );
    }

    /**
     * This method's different from GetSingleColor is that it uses an "enabled
     * index", ie. "the nth color that is computer-enabled". Visualizers that
     * are algorithmically driven should use this method, so that the user is
     * able to decide which colors they are allowed to use.
     */
    public int GetSingleComputerColor(int colorIndex) {
      return LEDColor.ScaleColor(
        this.config.domeColorPalette.GetSingleComputerColor(colorIndex),
        this.config.domeMaxBrightness * this.config.domeBrightness
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
      int? index = this.config.domeColorPalette.GetIndexOfEnabledIndex(
        colorIndex
      );
      if (
        !index.HasValue ||
        this.config.domeColorPalette.colors[index.Value] == null
      ) {
        return 0x000000;
      }
      if (!this.config.domeColorPalette.colors[index.Value].IsGradient) {
        return this.GetSingleColor(index.Value);
      }
      return LEDColor.ScaleColor(
        this.config.domeColorPalette.GetGradientColor(
          index.Value,
          pixelPos,
          focusPos,
          wrap
        ),
        this.config.domeMaxBrightness * this.config.domeBrightness
      );
    }

  }

}