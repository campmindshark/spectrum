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

  public partial class StageSimulatorWindow : Window {

    private static int[,] points = new int[,] {
      { 75, 130 }, { 150, 0 }, { 0, 0 },
      { 225, 130 }, { 300, 0 }, { 150, 260 },
      { 375, 130 }, { 300, 260 }, { 450, 260 },
      { 450, 0 }, { 525, 130 }, { 600, 0 },
      { 225, 390 }, { 375, 390 }, { 300, 520 },
    };
    private static int[,] sides = new int[,] {
      { 0, 1 }, { 1, 2 }, { 2, 0 },
      { 3, 1 }, { 1, 0 }, { 0, 3 },
      { 3, 4 }, { 4, 1 }, { 1, 3 },
      { 5, 3 }, { 3, 0 }, { 0, 5 },
      { 4, 3 }, { 3, 6 }, { 6, 4 },
      { 3, 5 }, { 5, 7 }, { 7, 3 },
      { 3, 7 }, { 7, 6 }, { 6, 3 },
      { 6, 7 }, { 7, 8 }, { 8, 6 },
      { 6, 9 }, { 9, 4 }, { 4, 6 },
      { 10, 9 }, { 9, 6 }, { 6, 10 },
      { 10, 11 }, { 11, 9 }, { 9, 10 },
      { 8, 10 }, { 10, 6 }, { 6, 8 },
      { 12, 7 }, { 7, 5 }, { 5, 12 },
      { 13, 7 }, { 7, 12 }, { 12, 13 },
      { 13, 8 }, { 8, 7 }, { 7, 13 },
      { 14, 13 }, { 13, 12 }, { 12, 14 },
    };

    private static Tuple<int, int> GetPoint(
      int sideIndex,
      int layerIndex,
      int point // 0: startpoint of line, 1: endpoint of line
    ) {
      var pt = StageSimulatorWindow.sides[sideIndex, point];

      // We need to fetch the three points comprising the triangle in question
      // so we know which direction the layerIndex will translate us to
      var point1 = sideIndex / 3 * 3;
      var point2 = sideIndex / 3 * 3 + 1;
      var point3 = sideIndex / 3 * 3 + 2;
      var trianglePts = (new int[] {
        StageSimulatorWindow.sides[point1, 0],
        StageSimulatorWindow.sides[point2, 0],
        StageSimulatorWindow.sides[point3, 0],
      });
      var otherTrianglePts = Array.FindAll(trianglePts, i => pt != i);

      int xTranslate = 0, yTranslate = 0;
      int ourX = StageSimulatorWindow.points[pt, 0];
      int ourY = StageSimulatorWindow.points[pt, 1];
      int firstOtherX = StageSimulatorWindow.points[otherTrianglePts[0], 0];
      int firstOtherY = StageSimulatorWindow.points[otherTrianglePts[0], 1];
      int secondOtherX = StageSimulatorWindow.points[otherTrianglePts[1], 0];
      int secondOtherY = StageSimulatorWindow.points[otherTrianglePts[1], 1];
      if (firstOtherY == secondOtherY) {
        // If the other points have the same y-coordinate, we know we don't need
        // to translate our x-coordinate
        yTranslate = firstOtherY > ourY ? 6 : -6;
      } else if (firstOtherY == ourY) {
        xTranslate = firstOtherX > ourX ? 6 : -6;
        yTranslate = secondOtherY > ourY ? 3 : -3;
      } else if (secondOtherY == ourY) {
        xTranslate = secondOtherX > ourX ? 6 : -6;
        yTranslate = firstOtherY > ourY ? 3 : -3;
      } else {
        Debug.Assert(false, "two of the y coordinates should match");
      }
      return new Tuple<int, int>(
        StageSimulatorWindow.points[pt, 0] + xTranslate * (layerIndex + 1) + 20,
        StageSimulatorWindow.points[pt, 1] + yTranslate * (layerIndex + 1) + 20
      );
    }

    private Configuration config;
    private WriteableBitmap bitmap;
    private Int32Rect rect;
    private byte[] pixels;
    private bool keyMode;
    private Label[] sideLabels;

    public StageSimulatorWindow(Configuration config) {
      this.InitializeComponent();
      this.config = config;

      this.rect = new Int32Rect(0, 0, 780, 600);
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

      this.sideLabels = new Label[this.config.stageSideLengths.Length];
      var brush = new SolidColorBrush(Colors.White);
      for (int i = 0; i < this.config.stageSideLengths.Length; i++) {
        var pt1 = GetPoint(i, 1, 0);
        var pt2 = GetPoint(i, 1, 1);
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
        label.MouseDown += SideLabelClicked;
        this.sideLabels[i] = label;
        this.canvas.Children.Add(label);
      }
    }

    private void WindowLoaded(object sender, RoutedEventArgs e) {
      this.Draw();
      DispatcherTimer timer = new DispatcherTimer();
      timer.Tick += Update;
      timer.Interval = new TimeSpan(100000); // every 10 milliseconds
      timer.Start();
    }

    private void Draw() {
      uint color = (uint)SimulatorUtils.GetComputerColor(0x000000)
        | (uint)0xFF000000;
      for (int i = 0; i < this.config.stageSideLengths.Length; i++) {
        for (int j = 0; j < 3; j++) {
          var pt1 = GetPoint(i, j, 0);
          var pt2 = GetPoint(i, j, 1);
          int numLEDs = this.config.stageSideLengths[i];
          double deltaX = (pt1.Item1 - pt2.Item1) / (numLEDs + 2.0);
          double deltaY = (pt1.Item2 - pt2.Item2) / (numLEDs + 2.0);
          for (int k = 0; k < numLEDs; k++) {
            int x = pt1.Item1 - (int)(deltaX * (k + 1));
            int y = pt1.Item2 - (int)(deltaY * (k + 1));
            this.SetPixelColor(this.pixels, x, y, color);
          }
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

    private void Update(object sender, EventArgs e) {
      int queueLength = this.config.stageCommandQueue.Count;
      if (queueLength == 0) {
        return;
      }

      //Stopwatch stopwatch = Stopwatch.StartNew();

      bool shouldRedraw = false;
      for (int k = 0; k < queueLength; k++) {
        StageLEDCommand command;
        bool result =
          this.config.stageCommandQueue.TryDequeue(out command);
        if (!result) {
          throw new Exception("Someone else is dequeueing!");
        }
        if (command.isFlush) {
          shouldRedraw = true;
          continue;
        }
        var pt1 = GetPoint(command.sideIndex, command.layerIndex, 0);
        var pt2 = GetPoint(command.sideIndex, command.layerIndex, 1);
        int numLEDs = this.config.stageSideLengths[command.sideIndex];
        double deltaX = (pt1.Item1 - pt2.Item1) / (numLEDs + 2.0);
        double deltaY = (pt1.Item2 - pt2.Item2) / (numLEDs + 2.0);
        int x = pt1.Item1 - (int)(deltaX * (command.ledIndex + 1));
        int y = pt1.Item2 - (int)(deltaY * (command.ledIndex + 1));
        uint color = (uint)SimulatorUtils.GetComputerColor(command.color)
          | (uint)0xFF000000;
        this.SetPixelColor(this.pixels, x, y, color);
      }

      if (shouldRedraw && !this.keyMode) {
        this.bitmap.WritePixels(this.rect, this.pixels, this.rect.Width * 4, 0);
      }

      //Debug.WriteLine("StageSimulator took " + stopwatch.ElapsedMilliseconds + "ms to update");
    }

    private void ShowKey(object sender, RoutedEventArgs e) {
      this.keyMode = !this.keyMode;
      foreach (Label sideLabel in this.sideLabels) {
        sideLabel.Visibility = this.keyMode
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

      var keyPixels = new byte[rect.Width * rect.Height * 4];
      for (int x = 0; x < rect.Width; x++) {
        for (int y = 0; y < rect.Height; y++) {
          this.SetPixelColor(keyPixels, x, y, (uint)0xFF000000);
        }
      }

      uint color = (uint)SimulatorUtils.GetComputerColor(0xFFFFFF)
        | (uint)0xFF000000;
      for (int i = 0; i < this.config.stageSideLengths.Length; i++) {
        var pt1 = GetPoint(i, 1, 0);
        var pt2 = GetPoint(i, 1, 1);
        int numLEDs = this.config.stageSideLengths[i];
        double deltaX = (pt1.Item1 - pt2.Item1) / (numLEDs + 2.0);
        double deltaY = (pt1.Item2 - pt2.Item2) / (numLEDs + 2.0);
        // Only light up the first 3/4 pixels
        for (int k = 0; k < numLEDs * 3 / 4; k++) {
          int x = pt1.Item1 - (int)(deltaX * (k + 1));
          int y = pt1.Item2 - (int)(deltaY * (k + 1));
          this.SetPixelColor(keyPixels, x, y, color);
        }
      }

      this.bitmap.WritePixels(this.rect, keyPixels, this.rect.Width * 4, 0);
    }

    private void PreviewBoxLostFocus(object sender, RoutedEventArgs e) {
      if (String.IsNullOrEmpty(this.previewBox.Text)) {
        this.previewBox.Text = "Click some sides...";
        this.previewBox.Foreground = new SolidColorBrush(Colors.Gray);
        this.previewBox.FontStyle = FontStyles.Italic;
      }
    }

    private void PreviewBoxGotFocus(object sender, RoutedEventArgs e) {
      if (String.Equals(this.previewBox.Text, "Click some sides...")) {
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
        this.previewBox.Text = "Click some sides...";
        this.previewBox.Foreground = new SolidColorBrush(Colors.Gray);
        this.previewBox.FontStyle = FontStyles.Italic;
      } else if (!String.Equals(this.previewBox.Text, "Click some sides...")) {
        this.previewBox.Foreground = new SolidColorBrush(Colors.Black);
        this.previewBox.FontStyle = FontStyles.Normal;
      }
    }

    private void SideLabelClicked(object sender, MouseButtonEventArgs e) {
      string[] listedSides;
      if (
        String.IsNullOrEmpty(this.previewBox.Text) ||
        String.Equals(this.previewBox.Text, "Click some sides...")
      ) {
        listedSides = new string[0];
      } else {
        listedSides = this.previewBox.Text.Split(',');
      }
      string[] newListedSides = new string[listedSides.Length + 1];
      Array.Copy(listedSides, newListedSides, listedSides.Length);
      newListedSides[listedSides.Length] =
        ((Label)e.Source).Content.ToString();
      this.previewBox.Text = String.Join(",", newListedSides);
      this.previewBox.Focus();
      this.previewBox.Select(this.previewBox.Text.Length, 0);
    }

  }

}