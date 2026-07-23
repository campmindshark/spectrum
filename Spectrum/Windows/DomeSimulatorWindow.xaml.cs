using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Spectrum.Base;
using System.Threading;
using System.Windows.Threading;
using System.Diagnostics;
using Spectrum.LEDs;

namespace Spectrum {

  public partial class DomeSimulatorWindow : Window {

    private static Tuple<int, int> GetPoint(
      int strutIndex, int point, DomeProjection projection
    ) {
      var p = StrutLayoutFactory.GetProjectedPoint(
        strutIndex, point, projection);
      return new Tuple<int, int>(
        (int)(p.Item1 * 690) + 10,
        (int)(p.Item2 * 690) + 10
      );
    }

    private sealed class ProjectionGeometry {
      public readonly int[] X0;
      public readonly int[] Y0;
      public readonly double[] DeltaX;
      public readonly double[] DeltaY;
      public readonly int[] NumLEDs;

      public ProjectionGeometry(DomeProjection projection) {
        int numStruts = LEDDomeOutput.GetNumStruts();
        this.X0 = new int[numStruts];
        this.Y0 = new int[numStruts];
        this.DeltaX = new double[numStruts];
        this.DeltaY = new double[numStruts];
        this.NumLEDs = new int[numStruts];
        for (int i = 0; i < numStruts; i++) {
          var pt1 = GetPoint(i, 0, projection);
          var pt2 = GetPoint(i, 1, projection);
          int numLEDs = LEDDomeOutput.GetNumLEDs(i);
          this.X0[i] = pt1.Item1;
          this.Y0[i] = pt1.Item2;
          this.DeltaX[i] = (pt1.Item1 - pt2.Item1) / (numLEDs + 2.0);
          this.DeltaY[i] = (pt1.Item2 - pt2.Item2) / (numLEDs + 2.0);
          this.NumLEDs[i] = numLEDs;
        }
      }
    }

    private readonly Configuration config;
    private readonly LEDDomeOutput dome;
    private readonly WriteableBitmap bitmap;
    private Int32Rect rect;
    private readonly byte[] pixels;
    private readonly int[][] ledColors;

    // Both projections are static and cached. Switching views only swaps this
    // reference and reprojects the retained logical LED colors.
    private readonly ProjectionGeometry stripExtentsGeometry;
    private readonly ProjectionGeometry topDownGeometry;
    private ProjectionGeometry geometry;

    private DispatcherTimer? timer;

    public DomeSimulatorWindow(Configuration config, LEDDomeOutput dome) {
      this.InitializeComponent();
      this.config = config;
      this.dome = dome;

      // Announce that a consumer is now draining diagnostic commands and the
      // latest-frame mailbox, so LEDDomeOutput starts feeding them (and stops
      // when we close).
      this.dome.SimulatorHasConsumer = true;

      this.rect = new Int32Rect(0, 0, 750, 750);
      this.bitmap = new WriteableBitmap(
        this.rect.Width,
        this.rect.Height,
        96,
        96,
        PixelFormats.Bgra32,
        null
      );
      this.pixels = new byte[this.rect.Width * this.rect.Height * 4];
      this.ClearPixels(this.pixels);
      RenderOptions.SetBitmapScalingMode(
        this.image,
        BitmapScalingMode.NearestNeighbor
      );
      RenderOptions.SetEdgeMode(
        this.image,
        EdgeMode.Aliased
      );

      int numStruts = LEDDomeOutput.GetNumStruts();
      this.stripExtentsGeometry = new ProjectionGeometry(
        DomeProjection.StripExtents);
      this.topDownGeometry = new ProjectionGeometry(DomeProjection.TopDown);
      this.geometry = this.stripExtentsGeometry;
      this.ledColors = new int[numStruts][];
      for (int i = 0; i < numStruts; i++) {
        this.ledColors[i] = new int[this.geometry.NumLEDs[i]];
      }
    }

    private void WindowLoaded(object sender, RoutedEventArgs e) {
      this.Draw();
      this.timer = new DispatcherTimer();
      this.timer.Tick += Update;
      this.timer.Interval = new TimeSpan(100000); // every 10 milliseconds
      this.timer.Start();
    }

    protected override void OnClosed(EventArgs e) {
      // Stop our update timer. A DispatcherTimer is rooted by the dispatcher, so
      // if we leave it running it keeps this closed window alive and keeps
      // draining SimulatorCommandQueue — a single-consumer queue — stealing
      // commands from any simulator opened later.
      if (this.timer != null) {
        this.timer.Stop();
        this.timer.Tick -= Update;
        this.timer = null;
      }
      // Stop LEDDomeOutput feeding simulator output. The setter also returns a
      // pending pooled normal frame; clear any ordered diagnostic commands.
      this.dome.SimulatorHasConsumer = false;
      this.dome.SimulatorCommandQueue.Clear();
      base.OnClosed(e);
    }

    private void Draw() {
      this.ClearPixels(this.pixels);
      for (int i = 0; i < this.geometry.NumLEDs.Length; i++) {
        int numLEDs = this.geometry.NumLEDs[i];
        for (int j = 0; j < numLEDs; j++) {
          this.PaintLED(i, j, this.ledColors[i][j]);
        }
      }
      this.bitmap.WritePixels(this.rect, this.pixels, this.rect.Width * 4, 0);
      this.image.Source = this.bitmap;
    }

    private void ClearPixels(byte[] target) {
      for (int x = 0; x < this.rect.Width; x++) {
        for (int y = 0; y < this.rect.Height; y++) {
          this.SetPixelColor(target, x, y, (uint)0xFF000000);
        }
      }
    }

    private void SetPixelColor(byte[] pixels, int x, int y, uint color) {
      int pos = (y * this.rect.Width + x) * 4;
      pixels[pos] = (byte)color;
      pixels[pos + 1] = (byte)(color >> 8);
      pixels[pos + 2] = (byte)(color >> 16);
      pixels[pos + 3] = (byte)(color >> 24);
    }

    // Paints a single dome LED into the pixel buffer using the precomputed
    // per-strut screen geometry.
    private void SetLEDColor(int strutIndex, int ledIndex, int ledColor) {
      this.ledColors[strutIndex][ledIndex] = ledColor;
      this.PaintLED(strutIndex, ledIndex, ledColor);
    }

    private void PaintLED(int strutIndex, int ledIndex, int ledColor) {
      int x = this.geometry.X0[strutIndex]
        - (int)(this.geometry.DeltaX[strutIndex] * (ledIndex + 1));
      int y = this.geometry.Y0[strutIndex]
        - (int)(this.geometry.DeltaY[strutIndex] * (ledIndex + 1));
      uint color =
        (uint)SimulatorUtils.GetComputerColor(ledColor) | (uint)0xFF000000;
      this.SetPixelColor(this.pixels, x, y, color);
    }

    // Paints a whole-frame color snapshot. frame is in canonical buffer order
    // (strut 0..N, led 0..len), matching LEDDomeOutput.MakeDomeFrame, so
    // we walk struts/leds in that order and index frame in lockstep.
    private void DrawFrame(int[] frame) {
      int idx = 0;
      for (int s = 0; s < this.geometry.NumLEDs.Length && idx < frame.Length; s++) {
        int numLEDs = this.geometry.NumLEDs[s];
        for (int j = 0; j < numLEDs && idx < frame.Length; j++) {
          this.SetLEDColor(s, j, frame[idx++]);
        }
      }
    }

    private void Update(object? sender, EventArgs e) {
      bool hasFrame = this.dome.TryTakeSimulatorFrame(out int[]? latestFrame);
      int queueLength = this.dome.SimulatorCommandQueue.Count;
      if (queueLength == 0 && !hasFrame) {
        return;
      }

      //Stopwatch stopwatch = Stopwatch.StartNew();

      bool shouldRedraw = false;
      if (hasFrame && latestFrame != null) {
        try {
          // Normal buffer output is a latest-value mailbox: render only the
          // newest frame available on this UI tick. Ordered diagnostic commands
          // are applied afterward, so a mode switch cannot let an older normal
          // frame overwrite newer diagnostic pixels.
          this.DrawFrame(latestFrame);
          shouldRedraw = true;
        } finally {
          this.dome.ReturnSimulatorFrame(latestFrame);
        }
      }

      for (int k = 0; k < queueLength; k++) {
        DomeLEDCommand command;
        bool result =
          this.dome.SimulatorCommandQueue.TryDequeue(out command);
        if (!result) {
          // The producer trims the queue from the front when it overflows its
          // cap (LEDDomeOutput.EnqueueSimulatorCommand), so a failed dequeue
          // here just means an old command we were about to read got dropped —
          // stop draining for this tick rather than treating it as an error.
          break;
        }
        if (command.isFlush) {
          shouldRedraw = true;
          continue;
        }
        this.SetLEDColor(command.strutIndex, command.ledIndex, command.color);
      }

      if (shouldRedraw) {
        this.bitmap.WritePixels(this.rect, this.pixels, this.rect.Width * 4, 0);
      }

      //Debug.WriteLine("DomeSimulator took " + stopwatch.ElapsedMilliseconds + "ms to update");
    }

    private void ProjectionChanged(object sender, RoutedEventArgs e) {
      bool topDown = this.projectionToggle.IsChecked == true;
      this.geometry = topDown
        ? this.topDownGeometry
        : this.stripExtentsGeometry;
      this.projectionToggle.Content = topDown
        ? "View: Real top-down"
        : "View: Strip extents";
      this.projectionToggle.ToolTip = topDown
        ? "Show the full LED strip extents"
        : "Foreshorten the dome as it appears from directly above";
      this.Draw();
    }

  }

}
