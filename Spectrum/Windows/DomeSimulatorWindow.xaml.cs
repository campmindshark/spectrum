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

    private static Tuple<int, int> GetPoint(int strutIndex, int point) {
      var p = StrutLayoutFactory.GetProjectedPoint(strutIndex, point);
      int index = StrutLayoutFactory.lines[strutIndex, point];
      return new Tuple<int, int>(
        (int)(p.Item1 * 690) + 10,
        (int)(p.Item2 * 690) + 10
      );
    }

    private readonly Configuration config;
    private readonly LEDDomeOutput dome;
    private readonly WriteableBitmap bitmap;
    private Int32Rect rect;
    private readonly byte[] pixels;
    private bool keyMode;
    private readonly Label[] strutLabels;

    // Per-strut screen geometry, precomputed once since the dome projection is
    // static. The screen position of led j on strut s is
    //   (strutX0[s] - strutDeltaX[s] * (j + 1),
    //    strutY0[s] - strutDeltaY[s] * (j + 1)).
    // Caching this keeps the per-frame command loop from re-running GetPoint
    // (x2) + GetNumLEDs + the divisions for every pixel it draws.
    private readonly int[] strutX0;
    private readonly int[] strutY0;
    private readonly double[] strutDeltaX;
    private readonly double[] strutDeltaY;
    private readonly int[] strutNumLEDs;

    private DispatcherTimer timer;

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
      for (int x = 0; x < this.rect.Width; x++) {
        for (int y = 0; y < this.rect.Height; y++) {
          this.SetPixelColor(this.pixels, x, y, (uint)0xFF000000);
        }
      }
      RenderOptions.SetBitmapScalingMode(
        this.image,
        BitmapScalingMode.NearestNeighbor
      );
      RenderOptions.SetEdgeMode(
        this.image,
        EdgeMode.Aliased
      );

      int numStruts = LEDDomeOutput.GetNumStruts();
      this.strutX0 = new int[numStruts];
      this.strutY0 = new int[numStruts];
      this.strutDeltaX = new double[numStruts];
      this.strutDeltaY = new double[numStruts];
      this.strutNumLEDs = new int[numStruts];
      for (int i = 0; i < numStruts; i++) {
        var pt1 = GetPoint(i, 0);
        var pt2 = GetPoint(i, 1);
        int numLEDs = LEDDomeOutput.GetNumLEDs(i);
        this.strutX0[i] = pt1.Item1;
        this.strutY0[i] = pt1.Item2;
        this.strutDeltaX[i] = (pt1.Item1 - pt2.Item1) / (numLEDs + 2.0);
        this.strutDeltaY[i] = (pt1.Item2 - pt2.Item2) / (numLEDs + 2.0);
        this.strutNumLEDs[i] = numLEDs;
      }

      this.strutLabels = new Label[numStruts];
      var brush = new SolidColorBrush(Colors.White);
      for (int i = 0; i < numStruts; i++) {
        var pt1 = GetPoint(i, 0);
        var pt2 = GetPoint(i, 1);
        var centerX = (pt1.Item1 + pt2.Item1) / 2;
        var centerY = (pt1.Item2 + pt2.Item2) / 2;
        Label label = new Label();
        label.Content = i;
        label.FontSize = 12;
        label.Visibility = Visibility.Collapsed;
        label.Foreground = brush;
        label.Margin = new Thickness(
          centerX - 10,
          centerY - 10,
          0,
          0
        );
        label.MouseDown += StrutLabelClicked;
        this.strutLabels[i] = label;
        this.canvas.Children.Add(label);
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
      uint color = (uint)SimulatorUtils.GetComputerColor(0x000000)
        | (uint)0xFF000000;
      for (int i = 0; i < this.strutNumLEDs.Length; i++) {
        int numLEDs = this.strutNumLEDs[i];
        for (int j = 0; j < numLEDs; j++) {
          int x = this.strutX0[i] - (int)(this.strutDeltaX[i] * (j + 1));
          int y = this.strutY0[i] - (int)(this.strutDeltaY[i] * (j + 1));
          this.SetPixelColor(this.pixels, x, y, color);
        }
      }
      this.bitmap.WritePixels(this.rect, this.pixels, this.rect.Width * 4, 0);
      this.image.Source = this.bitmap;
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
      int x = this.strutX0[strutIndex]
        - (int)(this.strutDeltaX[strutIndex] * (ledIndex + 1));
      int y = this.strutY0[strutIndex]
        - (int)(this.strutDeltaY[strutIndex] * (ledIndex + 1));
      uint color =
        (uint)SimulatorUtils.GetComputerColor(ledColor) | (uint)0xFF000000;
      this.SetPixelColor(this.pixels, x, y, color);
    }

    // Paints a whole-frame color snapshot. frame is in canonical buffer order
    // (strut 0..N, led 0..len), matching LEDDomeOutput.MakeDomeOutputBuffer, so
    // we walk struts/leds in that order and index frame in lockstep.
    private void DrawFrame(int[] frame) {
      int idx = 0;
      for (int s = 0; s < this.strutNumLEDs.Length && idx < frame.Length; s++) {
        int numLEDs = this.strutNumLEDs[s];
        for (int j = 0; j < numLEDs && idx < frame.Length; j++) {
          this.SetLEDColor(s, j, frame[idx++]);
        }
      }
    }

    private void Update(object sender, EventArgs e) {
      bool hasFrame = this.dome.TryTakeSimulatorFrame(out int[] latestFrame);
      int queueLength = this.dome.SimulatorCommandQueue.Count;
      if (queueLength == 0 && !hasFrame) {
        return;
      }

      //Stopwatch stopwatch = Stopwatch.StartNew();

      bool shouldRedraw = false;
      if (hasFrame) {
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

      if (shouldRedraw && !this.keyMode) {
        this.bitmap.WritePixels(this.rect, this.pixels, this.rect.Width * 4, 0);
      }

      //Debug.WriteLine("DomeSimulator took " + stopwatch.ElapsedMilliseconds + "ms to update");
    }

    private void ShowKey(object sender, RoutedEventArgs e) {
      this.keyMode = !this.keyMode;
      foreach (Label strutLabel in this.strutLabels) {
        strutLabel.Visibility = this.keyMode
          ? Visibility.Visible
          : Visibility.Collapsed;
      }
      this.showKey.Content = this.keyMode
        ? "Hide Key"
        : "Show Key";
      this.directionLabel.Visibility = this.keyMode
        ? Visibility.Visible
        : Visibility.Collapsed;
      this.previewBox.Visibility = this.keyMode
        ? Visibility.Visible
        : Visibility.Collapsed;

      if (!this.keyMode) {
        this.bitmap.WritePixels(this.rect, this.pixels, this.rect.Width * 4, 0);
        return;
      }

      var strutPixels = new byte[rect.Width * rect.Height * 4];
      for (int x = 0; x < rect.Width; x++) {
        for (int y = 0; y < rect.Height; y++) {
          this.SetPixelColor(strutPixels, x, y, (uint)0xFF000000);
        }
      }

      uint color = (uint)SimulatorUtils.GetComputerColor(0xFFFFFF)
        | (uint)0xFF000000;
      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        var pt1 = GetPoint(i, 0);
        var pt2 = GetPoint(i, 1);
        int numLEDs = LEDDomeOutput.GetNumLEDs(i);
        double deltaX = (pt1.Item1 - pt2.Item1) / (numLEDs + 2.0);
        double deltaY = (pt1.Item2 - pt2.Item2) / (numLEDs + 2.0);
        for (int j = 0; j < numLEDs * 3 / 4; j++) {
          int x = pt1.Item1 - (int)(deltaX * (j + 1));
          int y = pt1.Item2 - (int)(deltaY * (j + 1));
          this.SetPixelColor(strutPixels, x, y, color);
        }
      }

      this.bitmap.WritePixels(this.rect, strutPixels, this.rect.Width * 4, 0);
    }

    private void PreviewBoxLostFocus(object sender, RoutedEventArgs e) {
      if (String.IsNullOrEmpty(this.previewBox.Text)) {
        this.previewBox.Text = "Click some struts...";
        this.previewBox.Foreground = new SolidColorBrush(Colors.Gray);
        this.previewBox.FontStyle = FontStyles.Italic;
      }
    }

    private void PreviewBoxGotFocus(object sender, RoutedEventArgs e) {
      if (String.Equals(this.previewBox.Text, "Click some struts...")) {
        this.previewBox.Text = "";
        this.previewBox.Foreground = new SolidColorBrush(Colors.Black);
        this.previewBox.FontStyle = FontStyles.Normal;
      }
    }

    private void PreviewBoxTextChanged(object sender, TextChangedEventArgs e) {
      if (this.previewBox.IsFocused) {
        return;
      }
      if (String.IsNullOrEmpty(this.previewBox.Text)) {
        this.previewBox.Text = "Click some struts...";
        this.previewBox.Foreground = new SolidColorBrush(Colors.Gray);
        this.previewBox.FontStyle = FontStyles.Italic;
      } else if (!String.Equals(this.previewBox.Text, "Click some struts...")) {
        this.previewBox.Foreground = new SolidColorBrush(Colors.Black);
        this.previewBox.FontStyle = FontStyles.Normal;
      }
    }

    private void StrutLabelClicked(object sender, MouseButtonEventArgs e) {
      string[] listedStruts;
      if (
        String.IsNullOrEmpty(this.previewBox.Text) ||
        String.Equals(this.previewBox.Text, "Click some struts...")
      ) {
        listedStruts = new string[0];
      } else {
        listedStruts = this.previewBox.Text.Split(',');
      }
      string[] newListedStruts = new string[listedStruts.Length + 1];
      Array.Copy(listedStruts, newListedStruts, listedStruts.Length);
      newListedStruts[listedStruts.Length] =
        ((Label)e.Source).Content.ToString();
      this.previewBox.Text = String.Join(",", newListedStruts);
      this.previewBox.Focus();
      this.previewBox.Select(this.previewBox.Text.Length, 0);
    }

  }

}
