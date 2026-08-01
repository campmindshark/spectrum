using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using Spectrum.Base;
using Spectrum.LEDs;

namespace Spectrum.LayerPipeline.Tests {

  [TestClass]
  [DoNotParallelize]
  public sealed class ColorPerformanceTests {
    private static int colorSink;


    [TestMethod]
    public void PackedHsvConversionMatchesColor() {
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
            Assert.IsTrue(actual == expected,
              "HSV mismatch at h=" + hue + ", s=" + saturation +
              ", v=" + value + ": expected 0x" +
              expected.ToString("X6") + ", got 0x" +
              actual.ToString("X6"));
          }
        }
      }

      // Exercise the positive modulo wrapping used by the legacy converter.
      Assert.IsTrue(global::Spectrum.MathUtil.HsvToInt(.25, .7, .9) ==
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
            Assert.IsTrue(hue == expected.H && saturation == expected.S &&
                value == expected.V,
              "packed HSV decode mismatch for 0x" + packed.ToString("X6"));
          }
        }
      }
    }

    [TestMethod]
    public void PackedHsvConversionDoesNotAllocate() {
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
      Assert.IsTrue(allocated == 0,
        "packed HSV pixel loop allocated " + allocated + " bytes");
    }

    [TestMethod]
    public void DeferredPixelPackingPreservesSemantics() {
      var pixel = new LEDDomeOutputPixel {
        color = 0x102030,
        hue = .42,
      };
      pixel.SetRGB(17.9, 34.9, 51.9);

      // Copy before either pixel's packed getter runs. The copy must retain the
      // pending channel state, not the stale packed value from the color setter.
      var copy = new LEDDomeOutputPixel();
      copy.CopyChannelsFrom(pixel);
      Assert.IsTrue(copy.color == 0x112233 && pixel.color == 0x112233,
        "deferred channel state did not pack after a copy");
      Assert.IsTrue(copy.r == 17.9 && copy.g == 34.9 && copy.b == 51.9 &&
          copy.a == 1 && copy.hue == .42,
        "deferred channel copy changed mutable pixel state");

      copy.SetRGB(-10, 300, 127.9);
      Assert.IsTrue(copy.color == 0x00FF7F,
        "deferred packing changed channel clamping");
    }

    [TestMethod]
    public void GlobalHueRotationPreservesSemantics() {
      int[] channels = { 0, 1, 17, 127, 128, 254, 255 };
      double[] rates = { -.5, -1 / 6d, -.01, .01, 1 / 6d, .5 };
      foreach (double rate in rates) {
        foreach (int red in channels) {
          foreach (int green in channels) {
            foreach (int blue in channels) {
              int input = (red << 16) | (green << 8) | blue;
              var pixel = new LEDDomeOutputPixel { color = input };
              pixel.HueRotate(rate);
              int expected = LegacyHueRotate(input, rate);
              Assert.IsTrue(pixel.color == expected,
                "hue rotation mismatch for 0x" + input.ToString("X6") +
                " at " + rate + " turns: expected 0x" +
                expected.ToString("X6") + ", got 0x" +
                pixel.color.ToString("X6"));
            }
          }
        }
      }

      var topology = new DomeTopology(new[] {
        new DomeTopologyPixel(0, 0, 0, 0),
        new DomeTopologyPixel(0, 1, 1, 1),
      });
      var frame = new DomeFrame(topology);
      frame.pixels[0].color = 0x123456;
      frame.pixels[1].color = 0xABCDEF;
      frame.HueRotate(1.25);
      Assert.IsTrue(frame.pixels[0].color == LegacyHueRotate(0x123456, .25) &&
          frame.pixels[1].color == LegacyHueRotate(0xABCDEF, .25),
        "frame hue rotation did not reduce whole turns once");

      int unchanged = frame.pixels[0].color;
      frame.HueRotate(-2);
      Assert.IsTrue(frame.pixels[0].color == unchanged,
        "whole-turn hue rotation changed a pixel");
    }

    private static int LegacyHueRotate(int color, double rate) {
      var hsv = new global::Spectrum.Color(color);
      if (color == 0 || hsv.S == 0) {
        return color;
      }
      double shiftedHue = (hsv.H + rate) % 1;
      if (shiftedHue > 1) {
        shiftedHue -= 1;
      }
      if (shiftedHue < 0) {
        shiftedHue += 1;
      }
      return new global::Spectrum.Color(
        shiftedHue, hsv.S, hsv.V).ToInt();
    }

    [TestMethod]
    public void DomeGradientSemantics() {
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

    [TestMethod]
    public void DomeGradientSamplingDoesNotAllocate() {
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
      Assert.IsTrue(allocated == 0,
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
      };
      config.ReplaceDomePalettes(new List<DomePalette> {
        new DomePalette { Name = "Allocation fixture", Colors = colors },
      });
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
      Assert.IsTrue(actual == expected,
        context + ": expected 0x" + expected.ToString("X6") +
        ", got 0x" + actual.ToString("X6"));
    }

  }
}
