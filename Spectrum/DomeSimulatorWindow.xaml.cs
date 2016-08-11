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
      { 395, 86  }, { 477, 107 }, { 545, 157 }, { 591, 229 }, { 623, 319 }, // 1
      { 627, 404 }, { 599, 484 }, { 546, 551 }, { 471, 606 }, { 390, 637 },
      { 304, 637 }, { 226, 605 }, { 149, 551 }, { 95,  485 }, { 70,  403 },
      { 74,  319 }, { 103, 229 }, { 149, 157 }, { 218, 107 }, { 299, 87  },
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

    private static Tuple<int, int> GetPoint(int index) {
      var point = points[index, 0];
      return new Tuple<int, int>(
        (int)((double)(points[index, 0] - 70) / 557 * 700) + 10,
        (int)((double)(points[index, 1] - 86) / 551 * 700) + 10
      );
    }

    /**
     * Okay, so the thing is that computers think of 0x000000 as black, but the
     * LEDs think of it as off. That means, for instance, 0x110000 is basically
     * black on a computer, but is clearly red on the LEDs. To make stuff appear
     * correctly on the computer, we're gonna start by scaling the color up so
     * its highest component is 0xFF.
     */
    private static int GetComputerColor(int ledColor) {
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
      double factor = Math.Sqrt(ratio);
      return (int)(red * factor) << 16 |
        (int)(green * factor) << 8 |
        (int)(blue * factor);
    }

    private Configuration config;
    private WriteableBitmap bitmap;
    private Int32Rect rect;
    private byte[] pixels;

    public DomeSimulatorWindow(Configuration config) {
      this.InitializeComponent();
      this.config = config;
      this.bitmap = new WriteableBitmap(
        750,
        750,
        96,
        96,
        PixelFormats.Bgra32,
        null
      );
      this.rect = new Int32Rect(0, 0, 750, 750);
      this.pixels = new byte[rect.Width * rect.Height * 4];
      for (int x = 0; x < rect.Width; x++) {
        for (int y = 0; y < rect.Height; y++) {
          this.SetPixelColor(x, y, (uint)0xFF000000);
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
    }

    private void WindowLoaded(object sender, RoutedEventArgs e) {
      this.Draw();
      DispatcherTimer timer = new DispatcherTimer();
      timer.Tick += Update;
      timer.Interval = new TimeSpan(100000); // every 10 milliseconds
      timer.Start();
    }

    private void Draw() {
      for (int i = 0; i < LEDDomeOutput.GetNumStruts(); i++) {
        var pt1 = GetPoint(lines[i, 0]);
        var pt2 = GetPoint(lines[i, 1]);
        int numLEDs = LEDDomeOutput.GetNumLEDs(i);
        double deltaX = (pt1.Item1 - pt2.Item1) / (numLEDs + 2.0);
        double deltaY = (pt1.Item2 - pt2.Item2) / (numLEDs + 2.0);
        for (int j = 0; j < numLEDs; j++) {
          int x = pt1.Item1 - (int)(deltaX * (j + 1));
          int y = pt1.Item2 - (int)(deltaY * (j + 1));
          uint color = (uint)GetComputerColor(0x000000) | (uint)0xFF000000;
          this.SetPixelColor(x, y, color);
        }
      }
      this.bitmap.WritePixels(this.rect, this.pixels, this.rect.Width * 4, 0);
      this.image.Source = this.bitmap;
    }

    private void SetPixelColor(int x, int y, uint color) {
      int pos = (y * this.rect.Width + x) * 4;
      this.pixels[pos] = (byte)color;
      this.pixels[pos + 1] = (byte)(color >> 8);
      this.pixels[pos + 2] = (byte)(color >> 16);
      this.pixels[pos + 3] = (byte)(color >> 24);
    }

    private void Update(object sender, EventArgs e) {
      int queueLength = this.config.domeCommandQueue.Count;
      if (queueLength == 0) {
        return;
      }

      Stopwatch stopwatch = Stopwatch.StartNew();

      bool shouldRedraw = false;
      for (int k = 0; k < queueLength; k++) {
        LEDCommand command;
        bool result =
          this.config.domeCommandQueue.TryDequeue(out command);
        if (!result) {
          throw new Exception("Someone else is dequeueing!");
        }
        if (command.isFlush) {
          shouldRedraw = true;
          continue;
        }
        var pt1 = GetPoint(lines[command.strutIndex, 0]);
        var pt2 = GetPoint(lines[command.strutIndex, 1]);
        int numLEDs = LEDDomeOutput.GetNumLEDs(command.strutIndex);
        double deltaX = (pt1.Item1 - pt2.Item1) / (numLEDs + 2.0);
        double deltaY = (pt1.Item2 - pt2.Item2) / (numLEDs + 2.0);
        int x = pt1.Item1 - (int)(deltaX * (command.ledIndex + 1));
        int y = pt1.Item2 - (int)(deltaY * (command.ledIndex + 1));
        uint color = (uint)GetComputerColor(command.color) | (uint)0xFF000000;
        this.SetPixelColor(x, y, color);
      }

      if (shouldRedraw) {
        this.bitmap.WritePixels(this.rect, this.pixels, this.rect.Width * 4, 0);
      }

      Debug.WriteLine(stopwatch.ElapsedMilliseconds + "ms to update");
    }

  }

}