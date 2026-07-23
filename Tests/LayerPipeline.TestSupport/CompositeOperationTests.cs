using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.LayerPipeline.Tests {

  public static class CompositeOperationTests {
    public static void Register(Action<string, Action> run) {
      run(nameof(ZeroOpacityIdentity), ZeroOpacityIdentity);
      run(nameof(KernelMatrix), KernelMatrix);
      run(
        nameof(OperationOptionsAreNormalized),
        OperationOptionsAreNormalized);
      run(
        nameof(KaleidoscopeFoldsCompositeCoordinates),
        KaleidoscopeFoldsCompositeCoordinates);
      run(
        nameof(EchoRetainsDelayedTransformedComposites),
        EchoRetainsDelayedTransformedComposites);
      run(nameof(HalftoneBuildsPaletteCells), HalftoneBuildsPaletteCells);
      run(nameof(SpatialRequirements), SpatialRequirements);
      run(nameof(SpatialPassSnapshots), SpatialPassSnapshots);
      run(nameof(MaskedAdjustmentFixtures), MaskedAdjustmentFixtures);
      run(nameof(PrismFixtures), PrismFixtures);
    }
    private static void ZeroOpacityIdentity() {
      DomeTopology topology = OnePixelTopology();
      foreach (DomeBlend operation in DomeBlend.All) {
        var dest = new DomeFrame(topology);
        dest.pixels[0].color = 0x123456;
        dest.pixels[0].SetAlpha(.35);
        dest.pixels[0].hue = .25;
        var source = new DomeFrame(topology);
        source.pixels[0].color = 0xFEDCBA;
        source.pixels[0].hue = .75;
        var snapshot = new DomeFrame(topology);
        snapshot.CopyFrom(dest);
        operation.Execute(new DomeBlendContext(
          dest, source,
          (operation.Requirements &
            CompositeRequirements.ReadsDestinationNeighbors) != 0
              ? snapshot : null,
          operation.CompileOptions(
            ImmutableDictionary<string, ParameterValue>.Empty),
          0, 0, null));
        Assert(dest.pixels[0].color == 0x123456 &&
          dest.pixels[0].a == .35 && dest.pixels[0].hue == .25,
          operation.Id + " changed the destination");
      }
    }

    private static void KernelMatrix() {
      var expectedHalf = new Dictionary<DomeBlend, int[]> {
        [DomeBlend.Over] = new[] {
          0x000000, 0x7F7F7F, 0x7F7F00, 0x00FF00, 0x3F00BF, 0x504040,
        },
        [DomeBlend.Add] = new[] {
          0x7F7F7F, 0xFFFFFF, 0xFF7F00, 0x00FF7F, 0x7F00FF, 0x606070,
        },
        [DomeBlend.Screen] = new[] {
          0x7F7F7F, 0xFFFFFF, 0xFF7F00, 0x00FF7F, 0x7F00FF, 0x575769,
        },
        [DomeBlend.Lighten] = new[] {
          0x7F7F7F, 0xFFFFFF, 0xFF7F00, 0x00FF7F, 0x7F00FF, 0x404060,
        },
        [DomeBlend.Multiply] = new[] {
          0x000000, 0x7F7F7F, 0x7F0000, 0x007F00, 0x00007F, 0x182836,
        },
        [DomeBlend.Desaturate] = new[] {
          0x000000, 0xFFFFFF, 0xA52626, 0x00FF00, 0x0707C6, 0x2D3D4D,
        },
        [DomeBlend.Hue] = new[] {
          0x000000, 0x7F7F7F, 0xE57F00, 0x00FF00, 0x003FD8, 0x106070,
        },
      };
      var expectedFull = new Dictionary<DomeBlend, int[]> {
        [DomeBlend.Over] = new[] {
          0x000000, 0x000000, 0x00FF00, 0x00FF00, 0x7F007F, 0x804020,
        },
        [DomeBlend.Add] = new[] {
          0xFFFFFF, 0xFFFFFF, 0xFFFF00, 0x00FFFF, 0xFF00FF, 0xA08080,
        },
        [DomeBlend.Screen] = new[] {
          0xFFFFFF, 0xFFFFFF, 0xFFFF00, 0x00FFFF, 0xFF00FF, 0x8F6F73,
        },
        [DomeBlend.Lighten] = new[] {
          0xFFFFFF, 0xFFFFFF, 0xFFFF00, 0x00FFFF, 0xFF00FF, 0x804060,
        },
        [DomeBlend.Multiply] = new[] {
          0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x10100C,
        },
        [DomeBlend.Desaturate] = new[] {
          0x000000, 0xFFFFFF, 0x4C4C4C, 0x00FF00, 0x0E0E8E, 0x3A3A3A,
        },
        [DomeBlend.Hue] = new[] {
          0x000000, 0x000000, 0xCBFF00, 0x00FF00, 0x007FB2, 0x008080,
        },
      };

      foreach (DomeBlend operation in expectedHalf.Keys) {
        DomeFrame half = ExecuteKernel(operation, .5);
        DomeFrame full = ExecuteKernel(operation, 1);
        AssertColors(operation.Id + " at 0.5", half,
          expectedHalf[operation]);
        AssertColors(operation.Id + " at 1", full,
          expectedFull[operation]);
        AssertKernelChannels(operation, half, .5);
        AssertKernelChannels(operation, full, 1);
      }
    }

    private static DomeFrame ExecuteKernel(DomeBlend operation, double opacity) {
      int[] destColors = {
        0x000000, 0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF, 0x204060,
      };
      int[] sourceColors = {
        0xFFFFFF, 0x000000, 0x00FF00, 0x0000FF, 0xFF0000, 0x804020,
      };
      double[] sourceAlpha = { 0, 1, 1, 0, .5, 1 };
      DomeTopology topology = LinearTopology(destColors.Length);
      var dest = new DomeFrame(topology);
      var source = new DomeFrame(topology);
      for (int i = 0; i < destColors.Length; i++) {
        dest.pixels[i].color = destColors[i];
        dest.pixels[i].SetAlpha(.25);
        dest.pixels[i].hue = i / 10d;
        source.pixels[i].color = sourceColors[i];
        source.pixels[i].SetAlpha(sourceAlpha[i]);
        source.pixels[i].hue = .6 + i / 100d;
      }
      operation.Execute(new DomeBlendContext(
        dest, source, null, EmptyCompositeOptions.Instance,
        opacity, 0, null));
      return dest;
    }

    private static void AssertKernelChannels(
      DomeBlend operation, DomeFrame frame, double opacity
    ) {
      double[] expectedAlpha = operation == DomeBlend.Over
        ? (opacity == .5
          ? new[] { .25, .625, .625, .25, .4375, .625 }
          : new[] { .25, 1, 1, .25, .625, 1 })
        : new[] { .25, .25, .25, .25, .25, .25 };
      double[] expectedHue;
      if (operation == DomeBlend.Over) {
        expectedHue = new[] { 0, .61, .62, .3, .64, .65 };
      } else if ((operation.Requirements &
          CompositeRequirements.PublishesHue) != 0) {
        expectedHue = new[] { .6, .61, .62, .63, .64, .65 };
      } else {
        expectedHue = new[] { 0, .1, .2, .3, .4, .5 };
      }
      for (int i = 0; i < frame.pixels.Length; i++) {
        AssertClose(expectedAlpha[i], frame.pixels[i].a,
          operation.Id + " alpha " + i);
        AssertClose(expectedHue[i], frame.pixels[i].hue,
          operation.Id + " hue " + i);
      }
    }

    private static void OperationOptionsAreNormalized() {
      ChromaticFringeOptions fringe = (ChromaticFringeOptions)
        CompileOptions(DomeBlend.ChromaticFringe, new Dictionary<string, double> {
          ["offset"] = double.NaN,
          ["spin"] = double.PositiveInfinity,
          ["follow"] = -1,
        });
      AssertClose(.045, fringe.Offset, "fringe NaN default");
      AssertClose(2, fringe.Spin, "fringe spin clamp");
      Assert(fringe.FollowOrientation, "fringe bool coercion failed");

      EdgeSpectrumOptions edge = (EdgeSpectrumOptions)
        CompileOptions(DomeBlend.EdgeSpectrum, new Dictionary<string, double> {
          ["strength"] = double.NegativeInfinity,
          ["offset"] = double.PositiveInfinity,
        });
      AssertClose(0, edge.Strength, "edge strength clamp");
      AssertClose(.12, edge.Offset, "edge offset clamp");

      RefractOptions refract = (RefractOptions)
        CompileOptions(DomeBlend.Refract, new Dictionary<string, double> {
          ["strength"] = double.NaN,
        });
      AssertClose(.05, refract.Strength, "refract NaN default");

      IridescenceOptions iridescence = (IridescenceOptions)
        CompileOptions(DomeBlend.Iridescence, new Dictionary<string, double> {
          ["strength"] = -1,
          ["bands"] = 99,
          ["spin"] = double.NaN,
          ["follow"] = 0,
        });
      AssertClose(0, iridescence.Strength, "iridescence strength clamp");
      AssertClose(8, iridescence.Bands, "iridescence bands clamp");
      AssertClose(.2, iridescence.Spin, "iridescence spin default");
      Assert(!iridescence.FollowOrientation,
        "iridescence bool coercion failed");

      HalftoneOptions halftone = (HalftoneOptions)
        CompileOptions(DomeBlend.Halftone, new Dictionary<string, double> {
          ["cellType"] = 99,
          ["scale"] = double.NaN,
          ["threshold"] = double.PositiveInfinity,
          ["dotMin"] = -1,
          ["dotMax"] = double.NaN,
          ["rotation"] = double.PositiveInfinity,
          ["palette"] = 99,
        });
      Assert(halftone.CellType == 2 &&
          halftone.Palette == PaletteService.MaxPalettes - 1,
        "halftone enum clamp failed");
      AssertClose(.14, halftone.Scale, "halftone scale default");
      AssertClose(.95, halftone.Threshold, "halftone threshold clamp");
      AssertClose(0, halftone.DotMinimum, "halftone minimum clamp");
      AssertClose(.94, halftone.DotMaximum, "halftone maximum default");
      AssertClose(Math.PI, halftone.Rotation, "halftone rotation clamp");
    }

    private static void KaleidoscopeFoldsCompositeCoordinates() {
      Assert(ReferenceEquals(
          DomeBlend.FromId("Kaleidoscope"), DomeBlend.Kaleidoscope),
        "Kaleidoscope was not registered");
      Assert(DomeBlend.Kaleidoscope.Params.Count == 6 &&
          DomeBlend.Kaleidoscope.Params.All(p => p.CompositorConsumed),
        "Kaleidoscope controls are not compositor-owned");

      KaleidoscopeOptions defaults = (KaleidoscopeOptions)
        CompileOptions(DomeBlend.Kaleidoscope, null);
      Assert(defaults.SectorCount == 8 && defaults.MirrorSectors &&
          defaults.Spin == .05 && defaults.FocalAngle == 0 &&
          defaults.FocalDistance == 0 && !defaults.FollowOrientation,
        "unexpected Kaleidoscope defaults");

      KaleidoscopeOptions clamped = (KaleidoscopeOptions)
        CompileOptions(DomeBlend.Kaleidoscope,
          new Dictionary<string, double> {
            ["sectors"] = 99,
            ["mirror"] = -1,
            ["spin"] = double.PositiveInfinity,
            ["focalAngle"] = double.NegativeInfinity,
            ["focalDistance"] = 99,
            ["follow"] = -1,
          });
      Assert(clamped.SectorCount == 24 && !clamped.MirrorSectors &&
          clamped.Spin == 2 && clamped.FocalAngle == -Math.PI &&
          clamped.FocalDistance == .8 && clamped.FollowOrientation,
        "Kaleidoscope controls did not clamp or coerce");

      DomeTopology topology = RingTopology(16, .6);
      var lookupFrame = new DomeFrame(topology);
      Assert(lookupFrame.NearestTopDownPixel(.8, .5) == 0,
        "top-down lookup missed an exact projected pixel");
      Assert(lookupFrame.NearestTopDownPixel(.79, .49) == 0,
        "top-down lookup did not return the nearest projected pixel");

      var repeatOptions = new KaleidoscopeOptions(
        4, false, 0, 0, 0, false);
      DomeFrame repeat = ExecuteKaleidoscope(
        topology, repeatOptions, 1, 0, null);
      var repeatExpected = new int[16];
      for (int i = 0; i < repeatExpected.Length; i++) {
        repeatExpected[i] = ((i % 4) + 1) << 16;
      }
      AssertColors("Kaleidoscope repeat", repeat, repeatExpected);

      var mirrorOptions = repeatOptions with { MirrorSectors = true };
      DomeFrame mirror = ExecuteKaleidoscope(
        topology, mirrorOptions, 1, 0, null);
      var mirrorExpected = new int[16];
      for (int i = 0; i < mirrorExpected.Length; i++) {
        int sector = i / 4;
        int local = i % 4;
        int sample = (sector & 1) == 0 ? local : 4 - local;
        mirrorExpected[i] = (sample + 1) << 16;
      }
      AssertColors("Kaleidoscope mirror", mirror, mirrorExpected);
      AssertClose(.25, mirror.pixels[7].a,
        "Kaleidoscope changed destination coverage");
      AssertClose(7 / 16d, mirror.pixels[7].hue,
        "Kaleidoscope changed destination hue");

      DomeFrame half = ExecuteKaleidoscope(
        topology, repeatOptions, .5, 0, null);
      for (int i = 0; i < half.pixels.Length; i++) {
        double original = (i + 1) << 16;
        double transformed = repeatExpected[i];
        AssertClose(
          (((int)original >> 16) + ((int)transformed >> 16)) / 2d,
          half.pixels[i].r,
          "Kaleidoscope opacity did not interpolate pixel " + i);
      }

      DomeFrame spun = ExecuteKaleidoscope(
        topology, repeatOptions with { Spin = 1d / 16 }, 1, 1, null);
      Assert(ColorSignature(spun) != ColorSignature(repeat),
        "Kaleidoscope spin did not rotate the sectors");

      var fixedFocal = repeatOptions with {
        FocalAngle = Math.PI / 2, FocalDistance = .35,
      };
      DomeFrame fixedFocalFrame = ExecuteKaleidoscope(
        topology, fixedFocal, 1, 0, null);
      DomeFrame followedFocalFrame = ExecuteKaleidoscope(
        topology,
        fixedFocal with { FocalAngle = 0, FollowOrientation = true },
        1, 0, new FixedOrientation(Math.PI / 2));
      Assert(ColorSignature(fixedFocalFrame) ==
          ColorSignature(followedFocalFrame),
        "Kaleidoscope orientation did not replace the focal angle");
      Assert(ColorSignature(fixedFocalFrame) != ColorSignature(repeat),
        "Kaleidoscope focal point did not affect coordinate sampling");

      var liveOrientation = new FixedOrientation(Math.PI / 2);
      var bottom = new DomeFrame(topology);
      var mask = new DomeFrame(topology);
      for (int i = 0; i < bottom.pixels.Length; i++) {
        bottom.pixels[i].color = (i + 1) << 16;
        mask.pixels[i].color = 0xFFFFFF;
      }
      var livePlan = new RenderPlan(ImmutableArray.Create(
        Compiled(
          new FakeRenderer("kaleidoscope-bottom", bottom),
          DomeBlend.Add, 1,
          ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(
          new FakeRenderer("kaleidoscope-mask", mask),
          DomeBlend.Kaleidoscope, 1,
          Parameters(
            ("sectors", 4), ("mirror", 0), ("spin", 0),
            ("focalAngle", 0), ("focalDistance", .35),
            ("follow", 1)))));
      var liveCompositor = new DomeCompositor(
        () => new DomeFrame(topology), liveOrientation,
        elapsedSeconds: () => 0);
      liveCompositor.Publish(livePlan);
      liveCompositor.Compose();
      Assert(liveOrientation.UpdateCount == 1,
        "Kaleidoscope did not refresh standalone orientation state");
    }

    private static DomeFrame ExecuteKaleidoscope(
      DomeTopology topology, KaleidoscopeOptions options,
      double opacity, double seconds, OrientationAngleProvider? orientation
    ) {
      var dest = new DomeFrame(topology);
      var source = new DomeFrame(topology);
      var snapshot = new DomeFrame(topology);
      for (int i = 0; i < dest.pixels.Length; i++) {
        dest.pixels[i].color = (i + 1) << 16;
        dest.pixels[i].SetAlpha(.25);
        dest.pixels[i].hue = i / 16d;
        source.pixels[i].color = 0xFFFFFF;
      }
      snapshot.CopyFrom(dest);
      DomeBlend.Kaleidoscope.Execute(new DomeBlendContext(
        dest, source, snapshot, options, opacity, seconds, orientation));
      return dest;
    }

    private static void EchoRetainsDelayedTransformedComposites() {
      Assert(ReferenceEquals(DomeBlend.FromId("Echo"), DomeBlend.Echo),
        "Echo was not registered");
      Assert(DomeBlend.Echo.Params.Count == 9 &&
          DomeBlend.Echo.Params.All(p => p.CompositorConsumed),
        "Echo controls are not compositor-owned");
      Assert((DomeBlend.Echo.Requirements &
          CompositeRequirements.ReadsHistory) != 0,
        "Echo did not declare retained history");

      EchoOptions defaults = (EchoOptions)CompileOptions(DomeBlend.Echo, null);
      Assert(defaults.CopyCount == 4 && defaults.Delay == .2 &&
          defaults.Rotation == .12 && defaults.Scale == .94 &&
          defaults.Drift == .025 && defaults.DriftDirection == 0 &&
          defaults.Decay == .65 && defaults.HueShift == .04 &&
          defaults.Saturation == 1,
        "unexpected Echo defaults");
      EchoOptions clamped = (EchoOptions)CompileOptions(
        DomeBlend.Echo, new Dictionary<string, double> {
          ["copies"] = double.NaN,
          ["delay"] = double.NegativeInfinity,
          ["rotation"] = double.PositiveInfinity,
          ["scale"] = 99,
          ["drift"] = -1,
          ["direction"] = double.NegativeInfinity,
          ["decay"] = 99,
          ["hueShift"] = double.PositiveInfinity,
          ["saturation"] = -1,
        });
      Assert(clamped.CopyCount == 4 && clamped.Delay == .05 &&
          clamped.Rotation == Math.PI && clamped.Scale == 1.3 &&
          clamped.Drift == 0 && clamped.DriftDirection == -Math.PI &&
          clamped.Decay == 1 && clamped.HueShift == .5 &&
          clamped.Saturation == 0,
        "Echo controls did not clamp or coerce");

      ImmutableDictionary<string, ParameterValue> neutral = Parameters(
        ("copies", 1), ("delay", .1), ("rotation", 0),
        ("scale", 1), ("drift", 0), ("direction", 0),
        ("decay", 1), ("hueShift", 0));
      Assert(((EchoOptions)DomeBlend.Echo.CompileOptions(neutral)).Saturation == 1,
        "Echo changed legacy parameter bags that predate saturation control");
      DomeTopology one = OnePixelTopology();
      var bottom = new DomeFrame(one);
      var mask = new DomeFrame(one);
      bottom.pixels[0].color = 0xC00000;
      bottom.pixels[0].hue = .37;
      mask.pixels[0].color = 0xFFFFFF;
      var firstPlan = new RenderPlan(ImmutableArray.Create(
        Compiled(new FakeRenderer("echo-bottom", bottom), DomeBlend.Over, 1,
          ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(new FakeRenderer("echo-mask", mask), DomeBlend.Echo, 1,
          neutral)));
      var retained = new DomeCompositor(
        () => new DomeFrame(one), elapsedSeconds: () => .1);
      retained.Publish(firstPlan);
      retained.Compose();
      bottom.pixels[0].color = 0;
      bottom.pixels[0].hue = .81;
      // Recompile/publish the same stable layer IDs: history must belong to the
      // compositor layer instance, not the transient compiled-plan objects.
      var replacementPlan = new RenderPlan(ImmutableArray.Create(
        Compiled(new FakeRenderer("echo-bottom", bottom), DomeBlend.Over, 1,
          ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(new FakeRenderer("echo-mask", mask), DomeBlend.Echo, 1,
          neutral)));
      retained.Publish(replacementPlan);
      DomeFrame recalled = RequireFrame(
        retained.Compose(), "Echo replacement frame");
      Assert(recalled.pixels[0].r == 192,
        "Echo lost its delayed frame across plan replacement");
      AssertClose(1, recalled.pixels[0].a,
        "Echo changed destination coverage");
      AssertClose(.81, recalled.pixels[0].hue,
        "Echo changed destination published hue");

      // Removing the operation must release its delay line; reusing the same
      // ID later starts clean rather than resurrecting an old composition.
      retained.Publish(new RenderPlan(ImmutableArray.Create(
        Compiled(new FakeRenderer("echo-bottom", bottom), DomeBlend.Over, 1,
          ImmutableDictionary<string, ParameterValue>.Empty))));
      retained.Compose();
      Assert(retained.HistoryStateCount == 0,
        "Echo retained state after its layer was removed");
      retained.Publish(replacementPlan);
      Assert(RequireFrame(
          retained.Compose(), "Echo re-added frame").pixels[0].color == 0,
        "Echo resurrected history after layer removal");

      // Each duplicate Echo layer sees and retains the composite at its own
      // stack position. A singleton-owned history would cross-contaminate them.
      DomeTopology pair = TwoPixelTopology();
      var pairBottom = new DomeFrame(pair);
      var firstMask = new DomeFrame(pair);
      var middle = new DomeFrame(pair);
      var secondMask = new DomeFrame(pair);
      pairBottom.pixels[0].color = 0xFF0000;
      pairBottom.pixels[1].color = 0x0000FF;
      firstMask.pixels[0].color = 0xFFFFFF;
      middle.pixels[0].color = 0x00FF00;
      middle.pixels[1].color = 0x00FF00;
      secondMask.pixels[1].color = 0xFFFFFF;
      var isolatedPlan = new RenderPlan(ImmutableArray.Create(
        Compiled(new FakeRenderer("echo-pair-bottom", pairBottom),
          DomeBlend.Add, 1, ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(new FakeRenderer("echo-first-mask", firstMask),
          DomeBlend.Echo, 1, neutral),
        Compiled(new FakeRenderer("echo-middle", middle),
          DomeBlend.Add, 1, ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(new FakeRenderer("echo-second-mask", secondMask),
          DomeBlend.Echo, 1, neutral)));
      var isolated = new DomeCompositor(
        () => new DomeFrame(pair), elapsedSeconds: () => .1);
      isolated.Publish(isolatedPlan);
      isolated.Compose();
      pairBottom.ResetComposite();
      middle.ResetComposite();
      DomeFrame isolatedResult = RequireFrame(
        isolated.Compose(), "isolated Echo frame");
      Assert(isolatedResult.pixels[0].color == 0xFF0000 &&
          isolatedResult.pixels[1].color == 0x00FFFF,
        "duplicate Echo layers shared or captured the wrong history");
      Assert(isolated.HistoryStateCount == 2,
        "duplicate Echo layers did not receive isolated history state");

      // Rotation, scaling, and drift are cumulative per copy and resolve
      // through the shared arbitrary top-down lookup.
      AssertEchoMovesPoint(
        RingTopology(4, .5), 0, 1,
        Parameters(
          ("copies", 1), ("delay", .1),
          ("rotation", Math.PI / 2), ("scale", 1),
          ("drift", 0), ("direction", 0),
          ("decay", 1), ("hueShift", 0)),
        "rotation");
      AssertEchoMovesPoint(
        ProjectedTopology((.5, 0), (.4, 0)), 0, 1,
        Parameters(
          ("copies", 1), ("delay", .1),
          ("rotation", 0), ("scale", .8),
          ("drift", 0), ("direction", 0),
          ("decay", 1), ("hueShift", 0)),
        "scale");
      AssertEchoMovesPoint(
        ProjectedTopology((0, 0), (.2, 0)), 0, 1,
        Parameters(
          ("copies", 1), ("delay", .1),
          ("rotation", 0), ("scale", 1),
          ("drift", .2), ("direction", 0),
          ("decay", 1), ("hueShift", 0)),
        "drift");

      var colored = new DomeFrame(one);
      var coloredMask = new DomeFrame(one);
      coloredMask.pixels[0].color = 0xFFFFFF;
      var coloredPlan = new RenderPlan(ImmutableArray.Create(
        Compiled(new FakeRenderer("echo-colored-bottom", colored),
          DomeBlend.Add, 1, ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(new FakeRenderer("echo-colored-mask", coloredMask),
          DomeBlend.Echo, 1,
          Parameters(
            ("copies", 2), ("delay", .1), ("rotation", 0),
            ("scale", 1), ("drift", 0), ("direction", 0),
            ("decay", .5), ("hueShift", 0)))));
      var coloredEcho = new DomeCompositor(
        () => new DomeFrame(one), elapsedSeconds: () => .1);
      coloredEcho.Publish(coloredPlan);
      colored.pixels[0].color = 0x640000;
      coloredEcho.Compose();
      colored.pixels[0].color = 0x006400;
      coloredEcho.Compose();
      colored.pixels[0].color = 0;
      DomeFrame decayed = RequireFrame(
        coloredEcho.Compose(), "decayed Echo frame");
      AssertClose(50, decayed.pixels[0].r,
        "Echo did not decay the older copy");
      AssertClose(100, decayed.pixels[0].g,
        "Echo did not retain the newest delayed copy");

      var hueOptions = Parameters(
        ("copies", 1), ("delay", .1), ("rotation", 0),
        ("scale", 1), ("drift", 0), ("direction", 0),
        ("decay", 1), ("hueShift", 1d / 3));
      DomeFrame hueShifted = TwoFrameEcho(
        one, 0xFF0000, 0, hueOptions, 1, 1);
      Assert(hueShifted.pixels[0].color == 0x00FF00,
        "Echo did not apply cumulative hue shift");
      var saturationOptions = Parameters(
        ("copies", 1), ("delay", .1), ("rotation", 0),
        ("scale", 1), ("drift", 0), ("direction", 0),
        ("decay", 1), ("hueShift", 0), ("saturation", .5));
      DomeFrame desaturated = TwoFrameEcho(
        one, 0xFF0000, 0, saturationOptions, 1, 1);
      AssertClose(255, desaturated.pixels[0].r,
        "Echo saturation scaling changed HSV value");
      AssertClose(127.5, desaturated.pixels[0].g,
        "Echo did not scale the delayed copy's saturation");
      AssertClose(127.5, desaturated.pixels[0].b,
        "Echo saturation scaling did not preserve hue");

      var compoundBottom = new DomeFrame(one);
      var compoundMask = new DomeFrame(one);
      compoundMask.pixels[0].color = 0xFFFFFF;
      var compoundPlan = new RenderPlan(ImmutableArray.Create(
        Compiled(new FakeRenderer("echo-compound-bottom", compoundBottom),
          DomeBlend.Over, 1, ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(new FakeRenderer("echo-compound-mask", compoundMask),
          DomeBlend.Echo, 1,
          Parameters(
            ("copies", 2), ("delay", .1), ("rotation", 0),
            ("scale", 1), ("drift", 0), ("direction", 0),
            ("decay", 1), ("hueShift", 0), ("saturation", .5)))));
      var compoundEcho = new DomeCompositor(
        () => new DomeFrame(one), elapsedSeconds: () => .1);
      compoundEcho.Publish(compoundPlan);
      compoundBottom.pixels[0].color = 0xFF0000;
      compoundEcho.Compose();
      compoundBottom.pixels[0].color = 0;
      compoundEcho.Compose();
      compoundBottom.pixels[0].color = 0;
      compoundBottom.pixels[0].hue = .73;
      DomeFrame compounded = RequireFrame(
        compoundEcho.Compose(), "compound Echo frame");
      AssertClose(191.25, compounded.pixels[0].g,
        "Echo did not compound saturation loss across older copies");
      AssertClose(191.25, compounded.pixels[0].b,
        "Echo compounded saturation unevenly");
      AssertClose(1, compounded.pixels[0].a,
        "Echo saturation scaling changed destination coverage");
      AssertClose(.73, compounded.pixels[0].hue,
        "Echo saturation scaling changed destination published hue");
      DomeFrame masked = TwoFrameEcho(
        one, 0xFF0000, 0, saturationOptions, .5, .5);
      AssertClose(63.75, masked.pixels[0].r,
        "Echo did not combine source alpha with layer opacity");
      AssertClose(31.875, masked.pixels[0].g,
        "Echo saturation scaling bypassed the combined mask");

      // The RGB delay line stays bounded to the configured temporal horizon.
      for (int i = 0; i < 100; i++) {
        coloredEcho.Compose();
      }
      Assert(coloredEcho.RetainedHistoryFrameCount <= 4,
        "Echo history grew beyond its configured delay horizon");
    }

    private static void AssertEchoMovesPoint(
      DomeTopology topology, int sourceIndex, int targetIndex,
      ImmutableDictionary<string, ParameterValue> options, string transform
    ) {
      var bottom = new DomeFrame(topology);
      var mask = new DomeFrame(topology);
      bottom.pixels[sourceIndex].color = 0xFF0000;
      for (int i = 0; i < mask.pixels.Length; i++) {
        mask.pixels[i].color = 0xFFFFFF;
      }
      var plan = new RenderPlan(ImmutableArray.Create(
        Compiled(new FakeRenderer("echo-move-bottom-" + transform, bottom),
          DomeBlend.Add, 1, ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(new FakeRenderer("echo-move-mask-" + transform, mask),
          DomeBlend.Echo, 1, options)));
      var compositor = new DomeCompositor(
        () => new DomeFrame(topology), elapsedSeconds: () => .1);
      compositor.Publish(plan);
      compositor.Compose();
      bottom.ResetComposite();
      DomeFrame moved = RequireFrame(
        compositor.Compose(), "transformed Echo frame");
      Assert(moved.pixels[targetIndex].r == 255,
        "Echo " + transform + " transformed the delayed copy incorrectly");
    }

    private static DomeFrame TwoFrameEcho(
      DomeTopology topology, int firstColor, int secondColor,
      ImmutableDictionary<string, ParameterValue> options,
      double opacity, double maskAlpha
    ) {
      var bottom = new DomeFrame(topology);
      var mask = new DomeFrame(topology);
      bottom.pixels[0].color = firstColor;
      mask.pixels[0].color = 0xFFFFFF;
      mask.pixels[0].SetAlpha(maskAlpha);
      var plan = new RenderPlan(ImmutableArray.Create(
        Compiled(new FakeRenderer("echo-two-bottom", bottom),
          DomeBlend.Add, 1, ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(new FakeRenderer("echo-two-mask", mask),
          DomeBlend.Echo, opacity, options)));
      var compositor = new DomeCompositor(
        () => new DomeFrame(topology), elapsedSeconds: () => .1);
      compositor.Publish(plan);
      compositor.Compose();
      bottom.pixels[0].color = secondColor;
      return RequireFrame(compositor.Compose(), "two-frame Echo result");
    }

    private static ICompositeOptions CompileOptions(
      DomeBlend operation, Dictionary<string, double>? parameters
    ) {
      DomeLayerSettings layer = Layer("background", "options-fixture");
      layer.BlendMode = operation.Id;
      layer.OperationParams = parameters;
      (LayerStackSnapshot? snapshot, string? error) =
        new LayerStackService(DomeLayerCatalog.Metadata).CreateSnapshot(new[] { layer });
      Assert(snapshot != null && error == null, error);
      return operation.CompileOptions(snapshot.Layers[0].OperationParameters);
    }

    private static void HalftoneBuildsPaletteCells() {
      Assert(ReferenceEquals(
          DomeBlend.FromId("Halftone"), DomeBlend.Halftone),
        "Halftone was not registered");
      Assert(DomeBlend.Halftone.Params.Count == 7 &&
          DomeBlend.Halftone.Params.All(p => p.CompositorConsumed),
        "Halftone controls are not compositor-owned");

      DomeTopology topology = ProjectedTopology(
        (.10, .10), (.13, .10), (.19, .10));
      var dest = new DomeFrame(topology);
      var mask = new DomeFrame(topology);
      for (int i = 0; i < dest.pixels.Length; i++) {
        dest.pixels[i].color = 0x808080;
        dest.pixels[i].SetAlpha(.25);
        dest.pixels[i].hue = i / 10d;
        mask.pixels[i].color = 0xFFFFFF;
      }
      var snapshot = new DomeFrame(topology);
      snapshot.CopyFrom(dest);
      var options = new HalftoneOptions(
        0, .2, 0, 0, 1, 0, 3);
      int paletteCalls = 0;
      DomeBlend.Halftone.Execute(new DomeBlendContext(
        dest, mask, snapshot, options, 1, 0, null,
        paletteColor: (palette, position) => {
          Assert(palette == 3, "Halftone used the wrong palette");
          AssertClose(128d / 255, position,
            "Halftone did not palette-map sampled brightness");
          paletteCalls++;
          return 0xFF0000;
        }));
      Assert(dest.pixels[0].r == 255 && dest.pixels[0].g == 0 &&
          dest.pixels[0].b == 0,
        "Halftone did not light the center of a dot");
      Assert(dest.pixels[2].color == 0,
        "Halftone did not replace the gap between dots with black");
      Assert(dest.pixels[0].a == .25 && dest.pixels[0].hue == 0 &&
          dest.pixels[1].hue == .1,
        "Halftone changed destination side channels");
      Assert(paletteCalls == 3,
        "Halftone did not resolve one palette color per masked pixel");

      DomeTopology triangleTopology = ProjectedTopology(
        (.10, .2 / 3 * Math.Sqrt(3) / 2), (.19, .01));
      var triangleDest = new DomeFrame(triangleTopology);
      var triangleMask = new DomeFrame(triangleTopology);
      for (int i = 0; i < triangleDest.pixels.Length; i++) {
        triangleDest.pixels[i].color = 0x808080;
        triangleMask.pixels[i].color = 0xFFFFFF;
      }
      var triangleSnapshot = new DomeFrame(triangleTopology);
      triangleSnapshot.CopyFrom(triangleDest);
      DomeBlend.Halftone.Execute(new DomeBlendContext(
        triangleDest, triangleMask, triangleSnapshot,
        options with { CellType = 1 }, 1, 0, null));
      Assert(triangleDest.pixels[0].color == 0xFFFFFF &&
          triangleDest.pixels[1].color == 0,
        "Halftone did not size an equilateral triangle around its centroid");

      // Exercise the topology-native mode on a short final segment: its sample
      // must clamp to the physical strut rather than indexing beyond the frame.
      DomeTopology strutTopology = LinearTopology(5);
      var strutDest = new DomeFrame(strutTopology);
      var strutMask = new DomeFrame(strutTopology);
      for (int i = 0; i < strutDest.pixels.Length; i++) {
        strutDest.pixels[i].color = 0x404040;
        strutMask.pixels[i].color = 0xFFFFFF;
      }
      var strutSnapshot = new DomeFrame(strutTopology);
      strutSnapshot.CopyFrom(strutDest);
      DomeBlend.Halftone.Execute(new DomeBlendContext(
        strutDest, strutMask, strutSnapshot,
        options with { CellType = 2, Scale = .052 },
        1, 0, null));
      Assert(strutDest.pixels.Any(p => p.color != 0),
        "Halftone strut segments produced no luminous cells");
    }

    private static void SpatialRequirements() {
      foreach (DomeBlend operation in new[] {
        DomeBlend.ChromaticFringe, DomeBlend.EdgeSpectrum, DomeBlend.Refract,
        DomeBlend.Kaleidoscope, DomeBlend.Echo, DomeBlend.Halftone,
      }) {
        Assert((operation.Requirements &
          CompositeRequirements.ReadsDestinationNeighbors) != 0,
          operation.Id + " omitted neighbor requirement");
      }
      Assert((DomeBlend.Iridescence.Requirements &
        CompositeRequirements.ReadsDestinationNeighbors) == 0,
        "Iridescence requested unnecessary scratch");
    }

    private static void SpatialPassSnapshots() {
      DomeTopology topology = TwoPixelTopology();
      var bottomFrame = new DomeFrame(topology);
      bottomFrame.pixels[0].color = 0x110000;
      bottomFrame.pixels[1].color = 0x002200;
      var maskFrame = new DomeFrame(topology);
      var firstOperation = new SwapSnapshotOperation("swap-1");
      var secondOperation = new SwapSnapshotOperation("swap-2");
      var plan = new RenderPlan(ImmutableArray.Create(
        Compiled(
          new FakeRenderer("bottom", bottomFrame), DomeBlend.Add, 1,
          ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(
          new FakeRenderer("mask-1", maskFrame), firstOperation, 1,
          ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(
          new FakeRenderer("mask-2", maskFrame), secondOperation, 1,
          ImmutableDictionary<string, ParameterValue>.Empty)));
      int frameAllocations = 0;
      var compositor = new DomeCompositor(
        () => {
          frameAllocations++;
          return new DomeFrame(topology);
        },
        elapsedSeconds: () => 0);
      compositor.Publish(plan);

      DomeFrame result = RequireFrame(
        compositor.Compose(), "spatial-pass result");
      Assert(firstOperation.FirstSeen == 0x110000 &&
        firstOperation.SecondSeen == 0x002200,
        "the first spatial pass did not see the pre-pass destination");
      Assert(secondOperation.FirstSeen == 0x002200 &&
        secondOperation.SecondSeen == 0x110000,
        "the second spatial pass received a stale snapshot");
      Assert(ReferenceEquals(
        firstOperation.SeenSnapshot, secondOperation.SeenSnapshot),
        "spatial passes did not reuse scratch storage");
      Assert(frameAllocations == 2,
        "expected one destination and one scratch frame");
      Assert(result.pixels[0].color == 0x110000 &&
        result.pixels[1].color == 0x002200,
        "spatial writes smeared instead of reading the snapshot");
    }

    private static void MaskedAdjustmentFixtures() {
      AssertFixture(
        DomeBlend.Desaturate,
        ImmutableDictionary<string, ParameterValue>.Empty,
        "000000 FFFFFF A52626 959595 0707C6 333B43 4F4F4F 507040 102030");
      AssertFixture(
        DomeBlend.Hue,
        ImmutableDictionary<string, ParameterValue>.Empty,
        "000000 000000 E57F00 33FF00 003FD8 087078 0066FF 234020 102030");
    }

    private static void PrismFixtures() {
      AssertFixture(
        DomeBlend.ChromaticFringe,
        Parameters(("offset", .02), ("spin", 0), ("follow", 0)),
        "000000 FFFF00 FF007F 00FF00 0800BF 2040D7 404020 288020 102030");
      AssertFixture(
        DomeBlend.EdgeSpectrum,
        Parameters(("strength", .35), ("offset", .02)),
        "000000 FFFFFF FF0000 16FF25 0F0AFF 294E60 955920 45802E 102030");
      AssertFixture(
        DomeBlend.Iridescence,
        Parameters(
          ("strength", .6), ("bands", 2), ("spin", 0), ("follow", 0)),
        "000000 66FF6F B24C27 11FF00 0026DB 114E4B 3B660C 2C8018 102030");
      AssertFixture(
        DomeBlend.Refract,
        Parameters(("strength", .02)),
        "000000 204060 8F2030 804020 003FBF C7CFD7 00FF00 306040 102030");
    }

    private static void AssertFixture(
      DomeBlend operation,
      ImmutableDictionary<string, ParameterValue> parameters,
      string expectedFull
    ) {
      DomeFrame full = ComposeFixture(operation, parameters, 1);
      string actual = ColorSignature(full);
      Assert(actual == expectedFull,
        operation.Id + " fixture expected " + expectedFull +
        " but got " + actual);

      DomeFrame half = ComposeFixture(operation, parameters, .5);
      int[] bottomColors = FixtureBottomColors();
      for (int i = 0; i < bottomColors.Length; i++) {
        double br = (bottomColors[i] >> 16) & 0xFF;
        double bg = (bottomColors[i] >> 8) & 0xFF;
        double bb = bottomColors[i] & 0xFF;
        AssertClose((br + full.pixels[i].r) / 2, half.pixels[i].r,
          operation.Id + " half-opacity red " + i);
        AssertClose((bg + full.pixels[i].g) / 2, half.pixels[i].g,
          operation.Id + " half-opacity green " + i);
        AssertClose((bb + full.pixels[i].b) / 2, half.pixels[i].b,
          operation.Id + " half-opacity blue " + i);
        AssertClose(0, full.pixels[i].a,
          operation.Id + " changed the blank destination alpha " + i);
        AssertClose(i / 10d, full.pixels[i].hue,
          operation.Id + " changed destination hue " + i);
      }
    }

    private static DomeFrame ComposeFixture(
      DomeBlend operation,
      ImmutableDictionary<string, ParameterValue> parameters,
      double opacity
    ) {
      DomeTopology topology = FixtureTopology();
      int[] bottomColors = FixtureBottomColors();
      int[] sourceColors = {
        0xFFFFFF, 0x000000, 0x00FF00,
        0x0000FF, 0xFF0000, 0x804020,
        0xFFFFFF, 0x202020, 0xFFFFFF,
      };
      double[] sourceAlpha = { 0, 1, .5, 1, .25, .75, 1, .5, 0 };
      var bottom = new DomeFrame(topology);
      var source = new DomeFrame(topology);
      for (int i = 0; i < bottomColors.Length; i++) {
        bottom.pixels[i].color = bottomColors[i];
        bottom.pixels[i].hue = i / 10d;
        source.pixels[i].color = sourceColors[i];
        source.pixels[i].SetAlpha(sourceAlpha[i]);
        source.pixels[i].hue = i / 8d;
      }
      var plan = new RenderPlan(ImmutableArray.Create(
        Compiled(
          new FakeRenderer("fixture-bottom", bottom), DomeBlend.Add, 1,
          ImmutableDictionary<string, ParameterValue>.Empty),
        Compiled(
          new FakeRenderer("fixture-mask", source), operation, opacity,
          parameters)));
      var compositor = new DomeCompositor(
        () => new DomeFrame(topology), elapsedSeconds: () => 0);
      compositor.Publish(plan);
      return RequireFrame(compositor.Compose(), "fixture result");
    }

    private static int[] FixtureBottomColors() => new[] {
      0x000000, 0xFFFFFF, 0xFF0000,
      0x00FF00, 0x0000FF, 0x204060,
      0x804020, 0x408020, 0x102030,
    };

    private static DomeTopology FixtureTopology() => new(new[] {
      new DomeTopologyPixel(0, 0, .48, .48),
      new DomeTopologyPixel(0, 1, .50, .48),
      new DomeTopologyPixel(0, 2, .52, .48),
      new DomeTopologyPixel(0, 3, .48, .50),
      new DomeTopologyPixel(0, 4, .50, .50),
      new DomeTopologyPixel(0, 5, .52, .50),
      new DomeTopologyPixel(0, 6, .48, .52),
      new DomeTopologyPixel(0, 7, .50, .52),
      new DomeTopologyPixel(0, 8, .52, .52),
    });

    private static ImmutableDictionary<string, ParameterValue> Parameters(
      params (string Key, double Value)[] values
    ) {
      var result = ImmutableDictionary.CreateBuilder<string, ParameterValue>(
        StringComparer.Ordinal);
      foreach ((string key, double value) in values) {
        result[key] = new ParameterValue(DomeLayerParamType.Double, value);
      }
      return result.ToImmutable();
    }

    private static string ColorSignature(DomeFrame frame) {
      var colors = new string[frame.pixels.Length];
      for (int i = 0; i < colors.Length; i++) {
        colors[i] = frame.pixels[i].color.ToString("X6");
      }
      return string.Join(" ", colors);
    }

    private static CompiledLayer Compiled(
      ILayerRenderer renderer, ICompositeOperation operation, double opacity,
      ImmutableDictionary<string, ParameterValue> parameters
    ) {
      var snapshot = new LayerSnapshot(
        new LayerInstanceId(renderer.RendererId), renderer.RendererId,
        operation.Id, opacity, true, parameters, parameters, null);
      return new CompiledLayer(
        snapshot, renderer, ImmutableArray<Input>.Empty, operation,
        operation.CompileOptions(parameters));
    }

    private static DomeLayerSettings Layer(string key, string? id) => new() {
      InstanceId = id,
      VisualizerKey = key,
      BlendMode = DomeBlend.Add.Id,
      Opacity = 1,
      Enabled = true,
    };

    private static DomeTopology OnePixelTopology() => new(new[] {
      new DomeTopologyPixel(0, 0, .5, .5),
    });

    private static DomeTopology TwoPixelTopology() => new(new[] {
      new DomeTopologyPixel(0, 0, .45, .5),
      new DomeTopologyPixel(1, 0, .55, .5),
    });

    private static DomeTopology LinearTopology(int count) {
      var pixels = new DomeTopologyPixel[count];
      for (int i = 0; i < count; i++) {
        pixels[i] = new DomeTopologyPixel(0, i, .4 + i * .02, .5);
      }
      return new DomeTopology(pixels);
    }

    private static DomeTopology GridTopology(
      int width, int height, double spacing
    ) {
      var pixels = new DomeTopologyPixel[width * height];
      double left = 0.5 - (width - 1) * spacing / 2;
      double top = 0.5 - (height - 1) * spacing / 2;
      for (int y = 0; y < height; y++) {
        for (int x = 0; x < width; x++) {
          int i = y * width + x;
          pixels[i] = new DomeTopologyPixel(
            0, i, left + x * spacing, top + y * spacing);
        }
      }
      return new DomeTopology(pixels);
    }

    private static DomeTopology RingTopology(int count, double radius) {
      var pixels = new DomeTopologyPixel[count];
      for (int i = 0; i < count; i++) {
        double angle = 2 * Math.PI * i / count;
        double x = radius * Math.Cos(angle);
        double y = radius * Math.Sin(angle);
        pixels[i] = new DomeTopologyPixel(
          0, i, (x + 1) * .5, (1 - y) * .5);
      }
      return new DomeTopology(pixels);
    }

    private static DomeTopology ProjectedTopology(
      params (double X, double Y)[] points
    ) {
      var pixels = new DomeTopologyPixel[points.Length];
      for (int i = 0; i < points.Length; i++) {
        double topDownX = (points[i].X + 1) * .5;
        double topDownY = (1 - points[i].Y) * .5;
        pixels[i] = new DomeTopologyPixel(
          i, 0, topDownX, topDownY, topDownX, topDownY);
      }
      return new DomeTopology(pixels);
    }

    private static void SetPaletteColors(
      global::Spectrum.SpectrumConfiguration config,
      Func<int, int> colorAt
    ) {
      var colors = new LEDColor[DomePalette.SlotCount];
      for (int color = 0; color < colors.Length; color++) {
        colors[color] = new LEDColor(colorAt(color));
      }
      config.ReplaceDomePalettes(new List<DomePalette> {
        new DomePalette { Name = "Test", Colors = colors },
      });
    }

    private static void AssertColors(
      string name, DomeFrame frame, int[] expected
    ) {
      Assert(expected.Length == frame.pixels.Length,
        name + " has the wrong fixture length");
      for (int i = 0; i < expected.Length; i++) {
        Assert(frame.pixels[i].color == expected[i],
          name + " pixel " + i + " expected 0x" +
          expected[i].ToString("X6") + " but got 0x" +
          frame.pixels[i].color.ToString("X6"));
      }
    }

    private static void AssertClose(
      double expected, double actual, string message
    ) {
      Assert(Math.Abs(expected - actual) < 0.000000001,
        message + " expected " + expected + " but got " + actual);
    }

    private static DomeFrame RequireFrame(
      DomeFrame? frame, string context
    ) => frame ?? throw new InvalidOperationException(
      context + " produced no frame");

    private sealed class FakeRenderer : ILayerRenderer {
      public string RendererId { get; }
      public DomeFrame Frame { get; }
      public bool IsAvailable => true;
      public IReadOnlyList<Input> RequiredInputs { get; }
      public FakeRenderer(
        string id, DomeFrame frame,
        IReadOnlyList<Input>? requiredInputs = null
      ) {
        this.RendererId = id;
        this.Frame = frame;
        this.RequiredInputs = requiredInputs ?? Array.Empty<Input>();
      }
    }

    private sealed class FixedOrientation : OrientationAngleProvider {
      private readonly double angle;
      public int UpdateCount { get; private set; }

      public FixedOrientation(double angle) {
        this.angle = angle;
      }

      public bool TryGetAngle(out double angle) {
        angle = this.angle;
        return true;
      }

      public void Update() => this.UpdateCount++;
    }

    private sealed class SwapSnapshotOperation : ICompositeOperation {
      public string Id { get; }
      public CompositeRequirements Requirements =>
        CompositeRequirements.ReadsDestination |
        CompositeRequirements.ReadsDestinationNeighbors;
      public DomeFrame? SeenSnapshot { get; private set; }
      public int FirstSeen { get; private set; }
      public int SecondSeen { get; private set; }

      public SwapSnapshotOperation(string id) {
        this.Id = id;
      }

      public ICompositeOptions CompileOptions(
        ImmutableDictionary<string, ParameterValue> parameters
      ) => EmptyCompositeOptions.Instance;

      public void Execute(in DomeBlendContext context) {
        DomeFrame snapshot = context.Snapshot ??
          throw new InvalidOperationException(
            "spatial pass received no snapshot");
        this.SeenSnapshot = snapshot;
        this.FirstSeen = snapshot.pixels[0].color;
        this.SecondSeen = snapshot.pixels[1].color;
        context.Dest.pixels[0].color = this.SecondSeen;
        context.Dest.pixels[1].color = this.FirstSeen;
      }
    }

  }
}