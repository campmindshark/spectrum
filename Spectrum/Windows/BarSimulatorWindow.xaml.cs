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

    private readonly Configuration config;
    private readonly WriteableBitmap bitmap;
    private Int32Rect rect;

    public BarSimulatorWindow(Configuration config) {
      this.InitializeComponent();
      this.config = config;

      this.rect = new Int32Rect(0, 0, 760, 280);
      this.bitmap = new WriteableBitmap(
        this.rect.Width,
        this.rect.Height,
        96,
        96,
        PixelFormats.Bgra32,
        null
      );
      byte[] pixels = new byte[this.rect.Width * this.rect.Height * 4];
      for (int x = 0; x < this.rect.Width; x++) {
        for (int y = 0; y < this.rect.Height; y++) {
          int pos = (y * this.rect.Width + x) * 4;
          pixels[pos] = 0x00;
          pixels[pos + 1] = 0x00;
          pixels[pos + 2] = 0x00;
          pixels[pos + 3] = 0xFF;
        }
      }
      this.bitmap.WritePixels(this.rect, pixels, this.rect.Width * 4, 0);
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
      var max = 2 * this.config.barInfinityLength + 2 * this.config.barInfinityWidth;
      for (int i = 0; i < max; i++) {
        this.SetInfinityPixel(i, color);
      }
      for (int i = 0; i < this.config.barRunnerLength; i++) {
        this.SetRunnerPixel(i, color);
      }
      this.image.Source = this.bitmap;
    }

    private void SetInfinityPixel(int index, uint color) {
      int x = -1, y = -1;
      if (index < this.config.barInfinityLength) {
        x = index;
        y = 0;
      } else if (index < this.config.barInfinityWidth + this.config.barInfinityLength) {
        x = this.config.barInfinityLength;
        y = index - this.config.barInfinityLength;
      } else if (index < this.config.barInfinityWidth + 2 * this.config.barInfinityLength) {
        x = 2 * this.config.barInfinityLength + this.config.barInfinityWidth - index;
        y = this.config.barInfinityWidth;
      } else if (index < 2 * this.config.barInfinityWidth + 2 * this.config.barInfinityLength) {
        x = 0;
        y = 2 * this.config.barInfinityWidth + 2 * this.config.barInfinityLength - index;
      } else {
        return;
      }
      x += 5;
      y += 2;
      this.SetPixelColor(x, y, color);
    }

    private void SetRunnerPixel(int index, uint color) {
      int x = index + 2;
      int y = this.config.barInfinityWidth + 4;
      this.SetPixelColor(x, y, color);
    }

    private void SetPixelColor(int x, int y, uint color) {
      this.bitmap.FillEllipseCentered(
        x * 10,
        y * 10,
        3,
        3,
        Color.FromArgb(
          (byte)(color >> 24),
          (byte)(color >> 16),
          (byte)(color >> 8),
          (byte)color
        )
      );
    }

    private void Update(object sender, EventArgs e) {
      int queueLength = this.config.barCommandQueue.Count;
      if (queueLength == 0) {
        return;
      }

      //Stopwatch stopwatch = Stopwatch.StartNew();

      for (int k = 0; k < queueLength; k++) {
        BarLEDCommand command;
        bool result =
          this.config.barCommandQueue.TryDequeue(out command);
        if (!result) {
          throw new Exception("Someone else is dequeueing!");
        }
        if (command.isFlush) {
          continue;
        }
        uint color = (uint)SimulatorUtils.GetComputerColor(command.color)
          | (uint)0xFF000000;
        if (command.isRunner) {
          this.SetRunnerPixel(command.ledIndex, color);
        } else {
          this.SetInfinityPixel(command.ledIndex, color);
        }
      }

      //Debug.WriteLine("BarSimulator took " + stopwatch.ElapsedMilliseconds + "ms to update");
    }

  }

}