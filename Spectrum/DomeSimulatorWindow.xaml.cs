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

    // What has become of my life
    private static int[,] points = new int[,] {
      { 395, 86 }, { 477, 107 }, { 545, 157 }, { 591, 229 }, { 623, 319 },  // 1
      { 627, 404 }, { 599, 484 }, { 546, 551 }, { 471, 606 }, { 390, 637 },
      { 304, 637 }, { 226, 605 }, { 149, 551 }, { 95, 485 }, { 70, 403 },
      { 74, 319 }, { 103, 229 }, { 149, 157 }, { 218, 107 }, { 299, 87 },
      { 348, 139 }, { 425, 149 }, { 487, 165 }, { 524, 219 }, { 555, 290 }, // 2
      { 572, 366 }, { 575, 431 }, { 535, 482 }, { 477, 534 }, { 409, 574 },
      { 348, 595 }, { 286, 573 }, { 220, 536 }, { 163, 483 }, { 123, 431 },
      { 125, 366 }, { 141, 292 }, { 172, 220 }, { 209, 166 }, { 270, 148 },
      { 386, 199 }, { 456, 210 }, { 487, 272 }, { 511, 346 }, { 523, 414 }, // 3
      { 473, 463 }, { 410, 509 }, { 348, 541 }, { 286, 509 }, { 223, 464 },
      { 173, 415 }, { 185, 347 }, { 209, 273 }, { 240, 209 }, { 310, 199 },
      { 348, 259 }, { 418, 262 }, { 442, 327 }, { 461, 394 }, { 406, 437 }, // 4
      { 348, 476 }, { 290, 438 }, { 236, 394 }, { 253, 327 }, { 278, 262 },
      { 379, 314 }, { 400, 375 }, { 349, 412 }, { 297, 375 }, { 316, 314 }, // 5
      { 348, 358 }                                                          // 6
    };

    // How could a benevolent God will this
    private static int[,] lines = new int[,] {
      { 0, 1 }, { 1, 2, }, { 3, 2 }, { 3, 4 }, { 4, 5 }, { 5, 6 }, { 7, 6 },
      { 7, 8 }, { 8, 9 }, { 9, 10 }, { 11, 10 }, { 11, 12 }, { 12, 13 },
      { 13, 14 }, { 15, 14 }, { 15, 16 }, { 16, 17 }, { 17, 18 }, { 19, 18 },
      { 19, 0 }, { 20, 21 }, { 22, 21 }, { 23, 22 }, { 24, 23 }, { 24, 25 },
      { 26, 25 }, { 27, 26 }, { 28, 27 }, { 28, 29 }, { 30, 29 }, { 31, 30 },
      { 32, 31 }, { 32, 33 }, { 34, 33 }, { 35, 34 }, { 36, 35 }, { 36, 37 },
      { 38, 37 }, { 39, 38 }, { 20, 39 }, { 41, 40 }, { 42, 41 }, { 43, 42 },
      { 44, 43 }, { 45, 44 }, { 46, 45 }, { 47, 46 }, { 48, 47 }, { 49, 48 },
      { 50, 49 }, { 51, 50 }, { 52, 51 }, { 53, 52 }, { 54, 53 }, { 40, 54 },
      { 56, 55 }, { 57, 56 }, { 58, 57 }, { 59, 58 }, { 60, 59 }, { 61, 60 },
      { 62, 61 }, { 63, 62 }, { 64, 63 }, { 55, 64 }, { 65, 66 }, { 66, 67 },
      { 67, 68 }, { 68, 69 }, { 69, 65 }, { 20, 0 }, { 0, 21 }, { 21, 1 },
      { 1, 22 }, { 2, 22 }, { 23, 2 }, { 23, 3 }, { 24, 3 }, { 24, 4 },
      { 4, 25 }, { 25, 5 }, { 5, 26 }, { 6, 26 }, { 27, 6 }, { 27, 7 },
      { 28, 7 }, { 28, 8 }, { 8, 29 }, { 29, 9 }, { 9, 30 }, { 10, 30 },
      { 31, 10 }, { 31, 11 }, { 32, 11 }, { 32, 12 }, { 12, 33 }, { 33, 13 },
      { 13, 34 }, { 14, 34 }, { 35, 14 }, { 35, 15 }, { 36, 15 }, { 36, 16 },
      { 16, 37 }, { 37, 17 }, { 17, 38 }, { 18, 38 }, { 39, 18 }, { 39, 19 },
      { 20, 19 }, { 20, 40 }, { 21, 40 }, { 21, 41 }, { 22, 41 }, { 41, 23 },
      { 42, 23 }, { 24, 42 }, { 24, 43 }, { 25, 43 }, { 25, 44 }, { 26, 44 },
      { 44, 27 }, { 45, 27 }, { 28, 45 }, { 28, 46 }, { 29, 46 }, { 29, 47 },
      { 30, 47 }, { 47, 31 }, { 48, 31 }, { 32, 48 }, { 32, 49 }, { 33, 49 },
      { 33, 50 }, { 34, 50 }, { 50, 35 }, { 51, 35 }, { 36, 51 }, { 36, 52 },
      { 37, 52 }, { 37, 53 }, { 38, 53 }, { 53, 39 }, { 54, 39 }, { 20, 54 },
      { 40, 55 }, { 40, 56 }, { 41, 56 }, { 56, 42 }, { 42, 57 }, { 43, 57 },
      { 43, 58 }, { 44, 58 }, { 58, 45 }, { 45, 59 }, { 46, 59 }, { 46, 60 },
      { 47, 60 }, { 60, 48 }, { 48, 61 }, { 49, 61 }, { 49, 62 }, { 50, 62 },
      { 62, 51 }, { 51, 63 }, { 52, 63 }, { 52, 64 }, { 53, 64 }, { 64, 54 },
      { 54, 55 }, { 55, 65 }, { 56, 65 }, { 57, 65 }, { 57, 66 }, { 58, 66 },
      { 59, 66 }, { 59, 67 }, { 60, 67 }, { 61, 67 }, { 61, 68 }, { 62, 68 },
      { 63, 68 }, { 63, 69 }, { 64, 69 }, { 55, 69 }, { 65, 70 }, { 66, 70 },
      { 67, 70 }, { 68, 70 }, { 69, 70 }
    };

    private static Tuple<int, int>[] smallOffsets;
    private static Tuple<int, int>[] mediumOffsets;
    private static Tuple<int, int>[] largeOffsets;
    private static Tuple<int, int>[] allOffsets;

    static DomeSimulatorWindow() {
      smallOffsets = GetPixelOffsets(0);
      mediumOffsets = GetPixelOffsets(1);
      largeOffsets = GetPixelOffsets(2);
      allOffsets = smallOffsets
          .Concat(mediumOffsets)
          .Concat(largeOffsets)
          .ToArray();
    }

    private static Tuple<int, int> GetPoint(int index) {
      var point = points[index, 0];
      return new Tuple<int, int>(
        (int)((double)(points[index, 0] - 12) / 673 * 750),
        (int)((double)(points[index, 1] - 21) / 673 * 750)
      );
    }

    /**
     * Okay, so the thing is that computers think of 0x000000 as black, but the
     * LEDs think of it as off. That means, for instance, 0x110000 is basically
     * black on a computer, but is clearly red on the LEDs. To make stuff appear
     * correctly on the computer, we're gonna start by scaling the color up so
     * its highest component is 0xFF.
     */
    private static uint[] GetComputerColors(int ledColor) {
      byte red = (byte)(ledColor >> 16);
      byte green = (byte)(ledColor >> 8);
      byte blue = (byte)ledColor;
      double ratio;
      if (red >= green && red >= blue) {
        ratio = (double)0xFF / red;
      } else if (green >= red && green >= blue) {
        ratio = (double)0xFF / green;
      } else {
        ratio = (double)0xFF / blue;
      }
      uint smallColor = (uint)0xFF << 24 |
        (uint)(red * ratio) << 16 |
        (uint)(green * ratio) << 8 |
        (uint)(blue * ratio);
      uint mediumColor = (uint)(255.0 / ratio) << 24 |
        (uint)(red * ratio) << 16 |
        (uint)(green * ratio) << 8 |
        (uint)(blue * ratio);
      uint largeColor = (uint)(Math.Min(255.0, 510.0 / ratio)) << 24 |
        (uint)(red * ratio) << 16 |
        (uint)(green * ratio) << 8 |
        (uint)(blue * ratio);
      return new uint[] { smallColor, mediumColor, largeColor };
    }

    /**
     * Size is 0, 1, or 2
     * It refers to the size of concentric circle we are painting
     *
     * This function returns the set of points that need to be painted for a
     * given LED and a specified size of the circle we're painting.
     */
    private static Tuple<int, int>[] GetPixelOffsets(byte size) {
      if (size == 0) {
        return new Tuple<int, int>[] {
          new Tuple<int, int>(0, 0), new Tuple<int, int>(0, 1),
          new Tuple<int, int>(1, 0), new Tuple<int, int>(1, 1),
        };
      } else if (size == 1) {
         return new Tuple<int, int>[] {
           new Tuple<int, int>(-2, 0), new Tuple<int, int>(-2, 1),
           new Tuple<int, int>(-1, -1), new Tuple<int, int>(-1, 0),
           new Tuple<int, int>(-1, 1), new Tuple<int, int>(-1, 2),
           new Tuple<int, int>(0, -2), new Tuple<int, int>(0, -1),
           new Tuple<int, int>(0, 2), new Tuple<int, int>(0, 3),
           new Tuple<int, int>(1, -2), new Tuple<int, int>(1, -1),
           new Tuple<int, int>(1, 2), new Tuple<int, int>(1, 3),
           new Tuple<int, int>(2, -1), new Tuple<int, int>(2, 0),
           new Tuple<int, int>(2, 1), new Tuple<int, int>(2, 2),
           new Tuple<int, int>(3, 0), new Tuple<int, int>(3, 1),
         };
      } else if (size == 2) {
        return new Tuple<int, int>[] {
          new Tuple<int, int>(-4, 0), new Tuple<int, int>(-4, 1),
          new Tuple<int, int>(-3, -2), new Tuple<int, int>(-3, -1),
          new Tuple<int, int>(-3, 0), new Tuple<int, int>(-3, 1),
          new Tuple<int, int>(-3, 2), new Tuple<int, int>(-3, 3),
          new Tuple<int, int>(-2, -3), new Tuple<int, int>(-2, -2),
          new Tuple<int, int>(-2, -1), new Tuple<int, int>(-2, 2),
          new Tuple<int, int>(-2, 3), new Tuple<int, int>(-2, 4),
          new Tuple<int, int>(-1, -3), new Tuple<int, int>(-1, -2),
          new Tuple<int, int>(-1, 3), new Tuple<int, int>(-1, 4),
          new Tuple<int, int>(0, -4), new Tuple<int, int>(0, -3),
          new Tuple<int, int>(0, 4), new Tuple<int, int>(0, 5),
          new Tuple<int, int>(1, -4), new Tuple<int, int>(1, -3),
          new Tuple<int, int>(1, 4), new Tuple<int, int>(1, 5),
          new Tuple<int, int>(2, -3), new Tuple<int, int>(2, -2),
          new Tuple<int, int>(2, 3), new Tuple<int, int>(2, 4),
          new Tuple<int, int>(3, -3), new Tuple<int, int>(3, -2),
          new Tuple<int, int>(3, -1), new Tuple<int, int>(3, 2),
          new Tuple<int, int>(3, 3), new Tuple<int, int>(3, 4),
          new Tuple<int, int>(4, -2), new Tuple<int, int>(4, -1),
          new Tuple<int, int>(4, 0), new Tuple<int, int>(4, 1),
          new Tuple<int, int>(4, 2), new Tuple<int, int>(4, 3),
          new Tuple<int, int>(5, 0), new Tuple<int, int>(5, 1),
        };
      }
      throw new Exception("Unknown size");
    }


    // constructed image of a single LED created in Draw()
    private WriteableBitmap ledBitmap;
    private WriteableBitmap bitmap;
    private Configuration config;
    // ledColors[strutIndex][ledIndex] = color
    private int[][] ledColors;
    // fuck[tuple(x, y)] = list(tuple(tuple(strutIndex, ledIndex), circleSize))
    private Dictionary<Tuple<int, int>, List<Tuple<Tuple<int, int>, byte>>>
      fuck;
    private List<LEDCommand> commandsSinceFlush;
    private Stopwatch stopwatch;
    private HashSet<Tuple<int, int>> pointsToUpdateCache;

    public DomeSimulatorWindow(Configuration config) {
      this.InitializeComponent();
      this.config = config;
      this.fuck =
        new Dictionary<Tuple<int, int>, List<Tuple<Tuple<int, int>, byte>>>();
      this.commandsSinceFlush = new List<LEDCommand>();
      this.stopwatch = new Stopwatch();
      this.stopwatch.Start();

      this.ledColors = new int[lines.GetLength(0)][];
      for (int i = 0; i < lines.GetLength(0); i++) {
        this.ledColors[i] = new int[LEDDomeOutput.GetNumLEDs(i)];
      }

      this.pointsToUpdateCache = new HashSet<Tuple<int, int>>();
      for (int j = 0; j < 100000; j++) {
        this.pointsToUpdateCache.Add(new Tuple<int, int>(j, j));
      }
      this.pointsToUpdateCache.Clear();

      RenderOptions.SetBitmapScalingMode(
        this.image,
        BitmapScalingMode.NearestNeighbor
      );
      RenderOptions.SetEdgeMode(
        this.image,
        EdgeMode.Aliased
      );
    }

    private void WindowLoaded(object sender, RoutedEventArgs e) {
      this.Draw();
      DispatcherTimer timer = new DispatcherTimer();
      timer.Tick += Update;
      timer.Interval = new TimeSpan(100000); // every 10 milliseconds
      timer.Start();
    }

    private void Draw2() {
      this.ledBitmap = new WriteableBitmap(
        9,
        9,
        96,
        96,
        PixelFormats.Bgra32,
        null
      );
      //this.ledBitmap.FillEllipseCentered(4, 4, 4, 4, )
    }

    /**
     *         + +
     *     + + + + + +
     *   + + + - - + + +
     *   + + - - - - + +
     * + + - - x x - - + +
     * + + - - x x - - + +
     *   + + - - - - + +
     *   + + + - - + + +
     *     + + + + + +
     *         + +
     */
    private void Draw() {
      // okay, what we're gonna do:
      // (1) draw a WriteableBitmap representation of a single pixel
      // (2) draw a WriteableBitmap representation of the background grid
      // (3) keep a mapping of 

      // currently, what are we doing?
      // - for each LED that needs to get updated, we figure out the pixels it
      //   affects
      // - for each of those pixels, we redraw by looking at all the constituent
      //   parts

      // what are we going to do with writeablebitmapex?
      // - we need a way to redraw a circle on just a subset of its pixels. we
      //   have that with "blit"
      // - typedef LED to Tuple<strut_index, led_index>
      // - when drawing, we make a map from LED to Tuple<LED, Point>
      //   contains all information needed to redraw a given LED
      // - okay, so when we're redrawing an LED we figure out its x, y and from
      //   that and a constant pixelSize we figure out its Int32Rect
      // - next we look update the LEDs that affect us, get their points, and
      //   their Int32Rect
      // - foreach bordering LED, intersect Int32Rect for that LED to draw.
      // - "blit" all bordering LEDs, and then "blit" the main LED

      // so the hard part for our new draw method is that we'll need to generate
      // a writeablebitmap for a single LED, and a map of LEDs to LEDs that
      // intersect with them. how do we generate that map? we can simply look at
      // the center of the pixel, and lookup all pixels that are n away

      Stopwatch stopwatch = Stopwatch.StartNew();

      this.bitmap = new WriteableBitmap(
        750,
        750,
        96,
        96,
        PixelFormats.Bgra32,
        null
      );
      Int32Rect rect = new Int32Rect(0, 0, 730, 730);
      byte[] pixels = new byte[rect.Width * rect.Height * 4];

      uint[][][] colors = new uint[this.ledColors.Length][][];
      for (int i = 0; i < this.ledColors.Length; i++) {
        colors[i] = new uint[this.ledColors[i].Length][];
        for (int j = 0; j < this.ledColors[i].Length; j++) {
          colors[i][j] = GetComputerColors(this.ledColors[i][j]);
        }
      }

      for (int i = 0; i < this.ledColors.Length; i++) {
        var pt1 = GetPoint(lines[i, 0]);
        var pt2 = GetPoint(lines[i, 1]);
        int numLEDs = this.ledColors[i].Length;
        double deltaX = (pt1.Item1 - pt2.Item1) / (numLEDs + 2.0);
        double deltaY = (pt1.Item2 - pt2.Item2) / (numLEDs + 2.0);
        for (int j = 0; j < numLEDs; j++) {
          int x = pt1.Item1 - (int)(deltaX * (j + 1));
          int y = pt1.Item2 - (int)(deltaY * (j + 1));
          this.DrawPixel(
            pixels,
            x,
            y,
            i,
            j,
            rect.Width,
            colors[i][j][0],
            0
          );
        }
      }

      //for (int i = 0; i < this.ledColors.Length; i++) {
      //  var pt1 = GetPoint(lines[i, 0]);
      //  var pt2 = GetPoint(lines[i, 1]);
      //  int numLEDs = this.ledColors[i].Length;
      //  double deltaX = (pt1.Item1 - pt2.Item1) / (numLEDs + 2.0);
      //  double deltaY = (pt1.Item2 - pt2.Item2) / (numLEDs + 2.0);
      //  for (int j = 0; j < numLEDs; j++) {
      //    int x = pt1.Item1 - (int)(deltaX * (j + 1));
      //    int y = pt1.Item2 - (int)(deltaY * (j + 1));
      //    foreach (var offset in smallOffsets) {
      //      this.DrawPixel(
      //        pixels,
      //        x + offset.Item1,
      //        y + offset.Item2,
      //        i,
      //        j,
      //        rect.Width,
      //        colors[i][j][0],
      //        0
      //      );
      //    }
      //  }
      //}

      //for (int i = 0; i < this.ledColors.Length; i++) {
      //  var pt1 = GetPoint(lines[i, 0]);
      //  var pt2 = GetPoint(lines[i, 1]);
      //  int numLEDs = this.ledColors[i].Length;
      //  double deltaX = (pt1.Item1 - pt2.Item1) / (numLEDs + 2.0);
      //  double deltaY = (pt1.Item2 - pt2.Item2) / (numLEDs + 2.0);
      //  for (int j = 0; j < numLEDs; j++) {
      //    int x = pt1.Item1 - (int)(deltaX * (j + 1));
      //    int y = pt1.Item2 - (int)(deltaY * (j + 1));
      //    foreach (var offset in mediumOffsets) {
      //      this.DrawPixel(
      //        pixels,
      //        x + offset.Item1,
      //        y + offset.Item2,
      //        i,
      //        j,
      //        rect.Width,
      //        colors[i][j][1],
      //        1
      //      );
      //    }
      //  }
      //}

      //for (int i = 0; i < this.ledColors.Length; i++) {
      //  var pt1 = GetPoint(lines[i, 0]);
      //  var pt2 = GetPoint(lines[i, 1]);
      //  int numLEDs = this.ledColors[i].Length;
      //  double deltaX = (pt1.Item1 - pt2.Item1) / (numLEDs + 2.0);
      //  double deltaY = (pt1.Item2 - pt2.Item2) / (numLEDs + 2.0);
      //  for (int j = 0; j < numLEDs; j++) {
      //    int x = pt1.Item1 - (int)(deltaX * (j + 1));
      //    int y = pt1.Item2 - (int)(deltaY * (j + 1));
      //    foreach (var offset in largeOffsets) {
      //      this.DrawPixel(
      //        pixels,
      //        x + offset.Item1,
      //        y + offset.Item2,
      //        i,
      //        j,
      //        rect.Width,
      //        colors[i][j][2],
      //        2
      //      );
      //    }
      //  }
      //}

      this.bitmap.WritePixels(rect, pixels, rect.Width * 4, 0);

      Debug.WriteLine(stopwatch.ElapsedMilliseconds + "ms for initial draw");

      this.image.Source = this.bitmap;
    }

    /**
     * Size is 0, 1, or 2
     * It refers to the size of concentric circle we are painting
     *
     * This function fills out this.fuck, and then calls MixPixelColor
     */
    private void DrawPixel(
      byte[] pixels,
      int x,
      int y,
      int strutIndex,
      int ledIndex,
      int width,
      uint color,
      byte size 
    ) {
      var point = new Tuple<int, int>(x, y);
      if (!this.fuck.ContainsKey(point)) {
        this.fuck[point] = new List<Tuple<Tuple<int, int>, byte>>();
      }
      var led = new Tuple<int, int>(strutIndex, ledIndex);
      var ledAndSource = new Tuple<Tuple<int, int>, byte>(led, size);
      this.fuck[point].Add(ledAndSource);
      this.MixPixelColor(pixels, x, y, width, color);
    }

    /**
     * This function updates the 4 bytes for the pixel at [x, y] in the pixels
     * array. Given an ARGB color, it applies alpha compositing to update any
     * existing data in the pixel.
     */
    private void MixPixelColor(
      byte[] pixels,
      int x,
      int y,
      int width,
      uint color
     ) {
      int pos = (y * width + x) * 4;

      byte curBlue = pixels[pos];
      byte curGreen = pixels[pos + 1];
      byte curRed = pixels[pos + 2];
      byte curAlpha = pixels[pos + 3];

      byte newBlue = (byte)color;
      byte newGreen = (byte)(color >> 8);
      byte newRed = (byte)(color >> 16);
      byte newAlpha = (byte)(color >> 24);

      pixels[pos] = (byte)((curBlue * (curAlpha / 255.0)) +
        ((newBlue / 255.0) * (newAlpha / 255.0) * (255.0 - curAlpha)));
      pixels[pos + 1] = (byte)((curGreen * (curAlpha / 255.0)) +
        ((newGreen / 255.0) * (newAlpha / 255.0) * (255.0 - curAlpha)));
      pixels[pos + 2] = (byte)((curRed * (curAlpha / 255.0)) +
        ((newRed / 255.0) * (newAlpha / 255.0) * (255.0 - curAlpha)));
      pixels[pos + 3] = (byte)(curAlpha +
        (newAlpha * (255.0 - curAlpha) / 255.0));
    }

    private void Update(object sender, EventArgs e) {
      int queueLength = this.config.domeCommandQueue.Count;
      if (queueLength == 0) {
        return;
      }

      bool shouldRedraw = false;
      for (int k = 0; k < queueLength; k++) {
        LEDCommand newCommand;
        bool result =
          this.config.domeCommandQueue.TryDequeue(out newCommand);
        if (!result) {
          throw new Exception("Someone else is dequeueing!");
        }
        if (newCommand.isFlush) {
          shouldRedraw = true;
        } else {
          this.ledColors[newCommand.strutIndex][newCommand.ledIndex]
            = newCommand.color;
          this.commandsSinceFlush.Add(newCommand);
        }
      }

      if (!shouldRedraw) {
        return;
      }

      Debug.WriteLine(stopwatch.ElapsedMilliseconds + "ms since last draw");
      stopwatch.Restart();
      Stopwatch four = new Stopwatch();

      this.pointsToUpdateCache.Clear();
      foreach (LEDCommand command in this.commandsSinceFlush) {
        var pt1 = GetPoint(lines[command.strutIndex, 0]);
        var pt2 = GetPoint(lines[command.strutIndex, 1]);
        int numLEDs = this.ledColors[command.strutIndex].Length;
        double deltaX = (pt1.Item1 - pt2.Item1) / (numLEDs + 2.0);
        double deltaY = (pt1.Item2 - pt2.Item2) / (numLEDs + 2.0);
        int x = pt1.Item1 - (int)(deltaX * (command.ledIndex + 1));
        int y = pt1.Item2 - (int)(deltaY * (command.ledIndex + 1));
        four.Start();
        foreach (var pixelOffset in allOffsets) {
          this.pointsToUpdateCache.Add(new Tuple<int, int>(
            x + pixelOffset.Item1,
            y + pixelOffset.Item2
          ));
        }
        four.Stop();
      }

      long stepOne = stopwatch.ElapsedMilliseconds;
      Debug.WriteLine(stepOne + "ms to determine pixels to redraw");
      Debug.WriteLine(this.pointsToUpdateCache.Count + " total pixels to update");
      Debug.WriteLine(four.ElapsedMilliseconds + "ms to add things to hashset");

      Stopwatch anotherOne = new Stopwatch();
      Stopwatch secondOne = new Stopwatch();

      foreach (var pixel in this.pointsToUpdateCache) {
        var ledsAndSizesToUpdate = this.fuck[pixel];
        byte[] pixels = new byte[4];
        foreach (var ledAndSize in ledsAndSizesToUpdate) {
          var led = ledAndSize.Item1;
          var size = ledAndSize.Item2;
          uint[] computerColors =
            GetComputerColors(this.ledColors[led.Item1][led.Item2]);
          uint color = computerColors[size];
          anotherOne.Start();
          this.MixPixelColor(pixels, 0, 0, 1, color);
          anotherOne.Stop();
        }
        Int32Rect rect = new Int32Rect(pixel.Item1, pixel.Item2, 1, 1);
        secondOne.Start();
        this.bitmap.WritePixels(rect, pixels, 4, 0);
        secondOne.Stop();
      }

      Debug.WriteLine((stopwatch.ElapsedMilliseconds - stepOne) + "ms to redraw");
      Debug.WriteLine(anotherOne.ElapsedMilliseconds + "ms alpha blending");
      Debug.WriteLine(secondOne.ElapsedMilliseconds + "ms actually drawing");

      this.commandsSinceFlush.Clear();
    }

    //private void UpdateProcessor() {
    //  try {
    //    while (true) {
    //      this.Update();
    //    }
    //  } catch (ThreadAbortException) { }
    //}

    //private void Redraw(object sender, EventArgs e) {
    //  int queueLength = this.drawQueue.Count;
    //  if (queueLength == 0) {
    //    return;
    //  }

    //  for (int i = 0; i < queueLength; i++) {
    //    Tuple<Int32Rect, byte[]> drawOperation;
    //    bool result = this.drawQueue.TryDequeue(out drawOperation);
    //    if (!result) {
    //      throw new Exception("Someone else is dequeueing!");
    //    }
    //    this.bitmap.WritePixels(
    //      drawOperation.Item1,
    //      drawOperation.Item2,
    //      drawOperation.Item1.Width * 4,
    //      0
    //    );
    //  }
    //}

  }

}