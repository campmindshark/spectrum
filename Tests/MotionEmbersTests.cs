using System;
using System.Collections.Generic;
using Spectrum.Base;

namespace Spectrum.LayerPipeline.Tests {

  internal static class MotionEmbersTests {
    public static void Register(Action<string, Action> run) {
      run("Motion Embers exposes normalized composite controls",
        ControlsCompileAndClamp);
      run("Motion Embers isolates changes and retains their glow",
        ChangesBecomeRetainedEmbers);
      run("Motion Embers optionally detects fading and hue changes",
        OptionalChangeChannels);
      run("Motion Embers color modes and masks preserve frame metadata",
        ColorModesAndMasking);
    }

    private static void ControlsCompileAndClamp() {
      Assert(ReferenceEquals(
          DomeBlend.FromId("MotionEmbers"), DomeBlend.MotionEmbers),
        "Motion Embers was not registered");
      Assert(DomeBlend.MotionEmbers.Params.Count == 6,
        "Motion Embers did not expose all requested controls");
      foreach (DomeLayerParam parameter in DomeBlend.MotionEmbers.Params) {
        Assert(parameter.CompositorConsumed,
          parameter.Key + " was not compositor-owned");
      }
      Assert((DomeBlend.MotionEmbers.Requirements &
          CompositeRequirements.ReadsHistory) != 0,
        "Motion Embers did not request isolated temporal state");

      MotionEmbersOptions defaults = Compile(null);
      Assert(defaults.ChangeThreshold == .08 &&
          defaults.EmberBrightness == 1.5 && defaults.Retention == .75 &&
          defaults.ColorMode == 1 && !defaults.CountFading &&
          !defaults.CountHueChanges,
        "unexpected Motion Embers defaults");
      MotionEmbersOptions clamped = Compile(new Dictionary<string, double> {
        ["changeThreshold"] = double.PositiveInfinity,
        ["emberBrightness"] = -2,
        ["retention"] = 99,
        ["colorMode"] = 99,
        ["countFading"] = -1,
        ["countHueChanges"] = 2,
      });
      Assert(clamped.ChangeThreshold == 1 &&
          clamped.EmberBrightness == 0 && clamped.Retention == 5 &&
          clamped.ColorMode == 2 && clamped.CountFading &&
          clamped.CountHueChanges,
        "Motion Embers controls did not clamp or coerce");
    }

    private static void ChangesBecomeRetainedEmbers() {
      var options = new MotionEmbersOptions(0, 1, .5, 0, false, false);
      var fixture = new EmberFixture(options);
      DomeFrame initial = fixture.Render(0, .1, hue: .37);
      Assert(initial.pixels[0].color == 0,
        "Motion Embers showed a static first frame");
      AssertClose(1, initial.pixels[0].a,
        "Motion Embers changed destination coverage");
      AssertClose(.37, initial.pixels[0].hue,
        "Motion Embers changed destination published hue");

      Assert(fixture.Render(0xFF0000, .2).pixels[0].color == 0xFF0000,
        "a full rising change did not become a source-colored ember");
      AssertClose(127.5, fixture.Render(0xFF0000, .7).pixels[0].r,
        "ember retention was not a frame-rate-independent half-life");
      AssertClose(63.75, fixture.Render(0xFF0000, 1.2).pixels[0].r,
        "a static ember did not continue fading");

      var crisp = new EmberFixture(options with { Retention = 0 });
      crisp.Render(0, .1);
      Assert(crisp.Render(0xFF0000, .2).pixels[0].r == 255,
        "zero retention discarded the current change");
      Assert(crisp.Render(0xFF0000, .3).pixels[0].color == 0,
        "zero retention kept a static change");
    }

    private static void OptionalChangeChannels() {
      var baseOptions = new MotionEmbersOptions(
        0, 1, 0, 0, false, false);
      Assert(TwoFrameColor(0xFF0000, 0, baseOptions) == 0,
        "a brightness fall counted as motion while fading was disabled");
      Assert(TwoFrameColor(
          0xFF0000, 0, baseOptions with { CountFading = true }) ==
          0xFF0000,
        "an enabled brightness fall did not retain its departing color");
      Assert(TwoFrameColor(0xFF0000, 0x0000FF, baseOptions) == 0,
        "a hue-only change counted as motion while hue detection was disabled");
      Assert(TwoFrameColor(
          0xFF0000, 0x0000FF,
          baseOptions with { CountHueChanges = true }) == 0x0000FF,
        "an enabled hue-only change did not become an ember");

      var thresholded = new EmberFixture(
        baseOptions with { ChangeThreshold = .25 });
      thresholded.Render(0, .1);
      Assert(thresholded.Render(0x330000, .2).pixels[0].color == 0,
        "a sub-threshold change became an ember");
    }

    private static void ColorModesAndMasking() {
      var options = new MotionEmbersOptions(0, 1, 0, 0, false, true);
      Assert(TwoFrameColor(0xFF0000, 0x00FF00, options) == 0x00FF00,
        "Source mode did not use the arriving color");
      Assert(TwoFrameColor(
          0xFF0000, 0x00FF00, options with { ColorMode = 1 }) ==
          0xFFFFFF,
        "Ember Heat mode did not map a strong change to white heat");
      Assert(TwoFrameColor(
          0xFF0000, 0x00FF00, options with { ColorMode = 2 }) ==
          0xFFFF00,
        "Difference mode did not preserve the RGB change signature");

      var masked = new EmberFixture(options, opacity: .5, maskAlpha: .5);
      DomeFrame result = masked.Render(0xFF0000, .1, hue: .62);
      AssertClose(191.25, result.pixels[0].r,
        "Motion Embers did not combine source alpha and layer opacity");
      AssertClose(1, result.pixels[0].a,
        "masked Motion Embers changed destination coverage");
      AssertClose(.62, result.pixels[0].hue,
        "masked Motion Embers changed destination hue");
    }

    private static int TwoFrameColor(
      int first, int second, MotionEmbersOptions options
    ) {
      var fixture = new EmberFixture(options);
      fixture.Render(first, .1);
      return fixture.Render(second, .2).pixels[0].color;
    }

    private static MotionEmbersOptions Compile(
      Dictionary<string, double> parameters
    ) {
      var layer = new DomeLayerSettings {
        InstanceId = "motion-embers-options",
        VisualizerKey = "background",
        BlendMode = DomeBlend.MotionEmbers.Id,
        Opacity = 1,
        Enabled = true,
        OperationParams = parameters,
      };
      (LayerStackSnapshot snapshot, string error) =
        new LayerStackService(DomeLayerCatalog.Metadata).CreateSnapshot(new[] { layer });
      Assert(error == null, error);
      return (MotionEmbersOptions)DomeBlend.MotionEmbers.CompileOptions(
        snapshot.Layers[0].OperationParameters);
    }

    private static void Assert(bool condition, string message) {
      if (!condition) {
        throw new InvalidOperationException(message);
      }
    }

    private static void AssertClose(
      double expected, double actual, string message
    ) {
      if (Math.Abs(expected - actual) > 1e-6) {
        throw new InvalidOperationException(
          message + " (expected " + expected + ", got " + actual + ")");
      }
    }

    private sealed class EmberFixture {
      private readonly DomeFrame destination;
      private readonly DomeFrame mask;
      private readonly CompositeFrameHistory history = new();
      private readonly MotionEmbersOptions options;
      private readonly double opacity;

      public EmberFixture(
        MotionEmbersOptions options, double opacity = 1,
        double maskAlpha = 1
      ) {
        var topology = new DomeTopology(new[] {
          new DomeTopologyPixel(0, 0, .5, .5),
        });
        this.destination = new DomeFrame(topology);
        this.mask = new DomeFrame(topology);
        this.mask.pixels[0].color = 0xFFFFFF;
        this.mask.pixels[0].SetAlpha(maskAlpha);
        this.options = options;
        this.opacity = opacity;
      }

      public DomeFrame Render(int color, double seconds, double hue = 0) {
        this.destination.pixels[0].color = color;
        this.destination.pixels[0].hue = hue;
        DomeBlend.MotionEmbers.Execute(new DomeBlendContext(
          this.destination, this.mask, null, this.options,
          this.opacity, seconds, null, this.history));
        return this.destination;
      }
    }
  }
}
