using System;
using System.Collections.Generic;
using Spectrum.Base;
using Spectrum.LEDs;

namespace Spectrum.LayerPipeline.Tests {

  internal static class ColorPerformanceTests {
    private static int colorSink;

    public static void Register(Action<string, Action> run) {
      run("packed HSV conversion matches Color at sector boundaries",
        PackedHsvConversionMatchesColor);
      run("packed HSV pixel conversion allocates no managed memory",
        PackedHsvConversionDoesNotAllocate);
      run("multi-slot dome gradients preserve render semantics",
        DomeGradientSemantics);
      run("multi-slot dome gradient sampling allocates no managed memory",
        DomeGradientSamplingDoesNotAllocate);
    }

    private static void PackedHsvConversionMatchesColor() {
      var hues = new List<double>();
      for (int sector = -6; sector <= 12; sector++) {
        double boundary = sector / 6d;
        hues.Add(Math.BitDecrement(boundary));
        hues.Add(boundary);
        hues.Add(Math.BitIncrement(boundary));
      }
      double[] saturations = { 0, 1 / 255d, .2, .5, 1 };
      double[] values = { 0, 1 / 255d, 127 / 255d, .5, 254 / 255d, 1 };
      foreach (double hue in hues) {
        foreach (double saturation in saturations) {
          foreach (double value in values) {
            int expected =
              new global::Spectrum.Color(hue, saturation, value).ToInt();
            int actual = global::Spectrum.MathUtil.HsvToInt(
              hue, saturation, value);
            Assert(actual == expected,
              "HSV mismatch at h=" + hue + ", s=" + saturation +
              ", v=" + value + ": expected 0x" +
              expected.ToString("X6") + ", got 0x" +
              actual.ToString("X6"));
          }
        }
      }

      // Exercise the positive modulo wrapping used by the legacy converter.
      Assert(global::Spectrum.MathUtil.HsvToInt(.25, .7, .9) ==
          global::Spectrum.MathUtil.HsvToInt(1.25, .7, .9),
        "wrapped positive hue changed");

      int[] channels = { 0, 1, 127, 128, 254, 255 };
      foreach (int red in channels) {
        foreach (int green in channels) {
          foreach (int blue in channels) {
            int packed = (red << 16) | (green << 8) | blue;
            var expected = new global::Spectrum.Color(packed);
            global::Spectrum.MathUtil.HsvFromInt(
              packed,
              out double hue,
              out double saturation,
              out double value);
            Assert(hue == expected.H && saturation == expected.S &&
                value == expected.V,
              "packed HSV decode mismatch for 0x" + packed.ToString("X6"));
          }
        }
      }
    }

    private static void PackedHsvConversionDoesNotAllocate() {
      int checksum = 0;
      for (int i = 0; i < 10000; i++) {
        checksum ^= global::Spectrum.MathUtil.HsvToInt(
          (i % 1201) / 600d - .5,
          (i % 101) / 100d,
          (i % 257) / 256d);
      }

      long before = GC.GetAllocatedBytesForCurrentThread();
      for (int frame = 0; frame < 8; frame++) {
        for (int pixel = 0; pixel < 4470; pixel++) {
          checksum ^= global::Spectrum.MathUtil.HsvToInt(
            (pixel % 721) / 720d,
            (pixel % 101) / 100d,
            (pixel % 256) / 255d);
        }
      }
      long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
      colorSink = checksum;
      Assert(allocated == 0,
        "packed HSV pixel loop allocated " + allocated + " bytes");
    }

    private static void DomeGradientSemantics() {
      LEDDomeOutput output = GradientOutput(out var config);
      output.BeginOperatorFrame();

      AssertGradient(output, 0, 1, false, 0xF00000,
        "first endpoint");
      AssertGradient(output, .25, 1, false, 0x786000,
        "first midpoint");
      AssertGradient(output, .5, 1, false, 0x00C000,
        "adjacent-pair boundary");
      AssertGradient(output, 1, 1, false, 0x000090,
        "pixelPos == 1 final endpoint");

      AssertGradient(output, .125, 0, false, 0x3C9000,
        "focus reversal");
      AssertGradient(output, 0, .75, false, 0xB43000,
        "unwrapped focus distance");
      AssertGradient(output, 0, .75, true, 0x786000,
        "wrapped focus distance");

      // Finish the cached frame, then change brightness and prove the packed
      // interpolation is scaled once, after the endpoints are blended.
      output.OperatorUpdate();
      config.domeMaxBrightness = .5;
      config.domeBrightness = .5;
      output.BeginOperatorFrame();
      AssertGradient(output, .25, 1, false, 0x1E1800,
        "single post-interpolation brightness scale");
    }

    private static void DomeGradientSamplingDoesNotAllocate() {
      LEDDomeOutput output = GradientOutput(out _);
      output.BeginOperatorFrame();
      int checksum = 0;
      for (int i = 0; i < 10000; i++) {
        checksum ^= output.GetGradientBetweenColors(
          0, 2, (i % 1001) / 1000d, .35, true);
      }

      long before = GC.GetAllocatedBytesForCurrentThread();
      for (int sample = 0; sample < 50000; sample++) {
        checksum ^= output.GetGradientBetweenColors(
          0, 2, (sample % 1001) / 1000d, .35, true);
      }
      long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
      colorSink = checksum;
      Assert(allocated == 0,
        "multi-slot gradient loop allocated " + allocated + " bytes");
    }

    private static LEDDomeOutput GradientOutput(
      out global::Spectrum.SpectrumConfiguration config
    ) {
      var colors = new LEDColor[DomePalette.SlotCount];
      colors[0] = new LEDColor(0xF00000, 0x010203);
      colors[1] = new LEDColor(0x00C000, 0x040506);
      colors[2] = new LEDColor(0x000090);
      config = new global::Spectrum.SpectrumConfiguration {
        domeMaxBrightness = 1,
        domeBrightness = 1,
        domePalettes = new List<DomePalette> {
          new DomePalette { Name = "Allocation fixture", Colors = colors },
        },
      };
      return new LEDDomeOutput(
        config, new RuntimeTelemetry(), new BeatBroadcaster(config));
    }

    private static void AssertGradient(
      LEDDomeOutput output,
      double pixelPos,
      double focusPos,
      bool wrap,
      int expected,
      string context
    ) {
      int actual = output.GetGradientBetweenColors(
        0, 2, pixelPos, focusPos, wrap);
      Assert(actual == expected,
        context + ": expected 0x" + expected.ToString("X6") +
        ", got 0x" + actual.ToString("X6"));
    }

    private static void Assert(bool condition, string message) {
      if (!condition) {
        throw new InvalidOperationException(message);
      }
    }
  }
}
