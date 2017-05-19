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

  public partial class BarSimulatorWindow : Window {

    private Configuration config;
    private WriteableBitmap bitmap;
    private Int32Rect rect;
    private byte[] pixels;

    public BarSimulatorWindow(Configuration config) {
      this.InitializeComponent();
      this.config = config;

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
      for (int i = 0; i < this.config.barInfinityLength; i++) {
        this.SetPixelColor(this.pixels, i + 10, 10, color);
        this.SetPixelColor(this.pixels, i + 10, 10 + this.config.barInfinityWidth, color);
      }
      for (int i = 0; i < this.config.barInfinityWidth; i++) {
        this.SetPixelColor(this.pixels, 10, i + 10, color);
        this.SetPixelColor(this.pixels, 10 + this.config.barInfinityLength, i + 10, color);
      }
      for (int i = 0; i < this.config.barRunnerLength; i++) {
        this.SetPixelColor(this.pixels, i + 10, 20 + this.config.barInfinityWidth, color);
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
      int queueLength = this.config.barCommandQueue.Count;
      if (queueLength == 0) {
        return;
      }

      Stopwatch stopwatch = Stopwatch.StartNew();

      bool shouldRedraw = false;
      for (int k = 0; k < queueLength; k++) {
        BarLEDCommand command;
        bool result =
          this.config.barCommandQueue.TryDequeue(out command);
        if (!result) {
          throw new Exception("Someone else is dequeueing!");
        }
        if (command.isFlush) {
          shouldRedraw = true;
          continue;
        }
        uint color = (uint)SimulatorUtils.GetComputerColor(command.color)
          | (uint)0xFF000000;
        if (command.isRunner) {
          this.SetPixelColor(this.pixels, command.ledIndex + 10, 20 + this.config.barInfinityWidth, color);
        } else if (command.ledIndex < this.config.barInfinityLength) {
          this.SetPixelColor(this.pixels, command.ledIndex + 10, 10, color);
        } else if (command.ledIndex < this.config.barInfinityWidth + this.config.barInfinityLength) {
          var pixelPos = (command.ledIndex - this.config.barInfinityLength) + 10;
          this.SetPixelColor(this.pixels, 10 + this.config.barInfinityLength, pixelPos, color);
        } else if (command.ledIndex < this.config.barInfinityWidth + 2 * this.config.barInfinityLength) {
          var pixelPos = 2 * this.config.barInfinityLength + this.config.barInfinityWidth - command.ledIndex + 10;
          this.SetPixelColor(this.pixels, pixelPos, 10 + this.config.barInfinityWidth, color);
        } else if (command.ledIndex < 2 * this.config.barInfinityWidth + 2 * this.config.barInfinityLength) {
          var pixelPos = 2 * this.config.barInfinityWidth + 2 * this.config.barInfinityLength - command.ledIndex + 10;
          this.SetPixelColor(this.pixels, 10, pixelPos, color);
        }
      }

      if (shouldRedraw) {
        this.bitmap.WritePixels(this.rect, this.pixels, this.rect.Width * 4, 0);
      }
    }

  }

}