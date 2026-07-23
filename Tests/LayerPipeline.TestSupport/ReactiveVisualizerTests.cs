using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using static Spectrum.LayerPipeline.Tests.TestAssertions;
using static Spectrum.LayerPipeline.Tests.LayerPipelineTestFixtures;

namespace Spectrum.LayerPipeline.Tests {

  public static class ReactiveVisualizerTests {
    public static void Register(Action<string, Action> run) {
      run(nameof(MagneticFieldUsesSignedCharges), MagneticFieldUsesSignedCharges);
      run(nameof(RippleTankIsOrientationOnly), RippleTankIsOrientationOnly);
      run(nameof(WatchfulIrisBehavesAsSceneCharacter), WatchfulIrisBehavesAsSceneCharacter);
      run(nameof(LivingSkinUsesReactionDiffusion), LivingSkinUsesReactionDiffusion);
    }
    private static void MagneticFieldUsesSignedCharges() {
      LayerDefinition? definition = DomeLayerCatalog.Metadata.Get("magnetic-field");
      Assert(definition != null && definition.DisplayName == "Magnetic Field",
        "Magnetic Field was not registered");

      double positive = OrientationCenter.SignedUnitChargePotential(
        new System.Numerics.Vector3(-0.8f, 0.6f, 0));
      double negative = OrientationCenter.SignedUnitChargePotential(
        new System.Numerics.Vector3(0.8f, -0.6f, 0));
      double neutral = OrientationCenter.SignedUnitChargePotential(
        System.Numerics.Vector3.UnitY);
      Assert(positive > 0, "the +1 pole did not produce positive potential");
      Assert(negative < 0, "the -1 pole did not produce negative potential");
      AssertClose(0, neutral, "the equidistant plane did not cancel");
      AssertClose(positive, -negative,
        "the two charges are not antipodally symmetric");

      const int LINE_COUNT = 8;
      const double LINE_WIDTH = 0.05;
      AssertClose(1, OrientationCenter.UnitFieldLineStrength(
        System.Numerics.Vector3.UnitY, LINE_COUNT, LINE_WIDTH),
        "a field-line meridian was not fully lit");
      AssertClose(1, OrientationCenter.UnitFieldLineStrength(
        OrientationCenter.Spot, LINE_COUNT, LINE_WIDTH),
        "field lines did not converge at the +1 pole");
      AssertClose(1, OrientationCenter.UnitFieldLineStrength(
        OrientationCenter.NegSpot, LINE_COUNT, LINE_WIDTH),
        "field lines did not converge at the -1 pole");
      var betweenLines = new System.Numerics.Vector3(
        0,
        (float)Math.Cos(Math.PI / LINE_COUNT),
        (float)Math.Sin(Math.PI / LINE_COUNT));
      AssertClose(0, OrientationCenter.UnitFieldLineStrength(
        betweenLines, LINE_COUNT, LINE_WIDTH),
        "the space between field lines was painted");

      DomeLayerSettings layer = Layer(
        "magnetic-field", "magnetic-field-options");
      layer.RendererParams = new Dictionary<string, double> {
        ["strength"] = 2.25,
        ["positiveColor"] = 0x112233,
        ["negativeColor"] = 0x445566,
        ["lineCount"] = 10.9,
        ["lineWidth"] = 0.045,
      };
      MagneticFieldLayerOptions options =
        BuiltInOptions<MagneticFieldLayerOptions>(layer);
      AssertClose(2.25, options.Strength,
        "field strength did not compile into renderer options");
      Assert(options.PositiveColor == 0x112233 &&
        options.NegativeColor == 0x445566,
        "charge colors did not compile into renderer options");
      Assert(options.LineCount == 10,
        "field-line count did not preserve integer truncation");
      AssertClose(0.045, options.LineWidth,
        "field-line width did not compile into renderer options");
    }

    private static void RippleTankIsOrientationOnly() {
      LayerDefinition? caustics = DomeLayerCatalog.Metadata.Get("caustics");
      LayerDefinition? rippleTank = DomeLayerCatalog.Metadata.Get("ripple-tank");
      Assert(caustics != null && rippleTank != null,
        "standalone Ripple Tank was not registered");
      foreach (DomeLayerParam parameter in caustics.Parameters) {
        Assert(parameter.Key != "wakeSize" &&
          parameter.Key != "wakeStrength" && parameter.Key != "trigger",
          "Caustics retained Ripple Tank parameter " + parameter.Key);
      }
      DomeLayerParam? speed = null;
      DomeLayerParam? damping = null;
      foreach (DomeLayerParam parameter in rippleTank.Parameters) {
        if (parameter.Key == "speed") {
          speed = parameter;
        }
        if (parameter.Key == "damping") {
          damping = parameter;
        }
        Assert(parameter.Key != "wakeSize" &&
          parameter.Key != "wakeStrength" && parameter.Key != "trigger",
          "Ripple Tank exposes removable wake tuning or a trigger control");
      }
      Assert(speed != null && speed.Max == 3,
        "Ripple Tank wave-speed slider does not cap at 3");
      Assert(damping != null && damping.Min == 0.02 &&
        damping.Max == 0.05 && damping.Step == 0.01 &&
        damping.Default == 0.02,
        "Ripple Tank damping slider is missing or malformed");

      DomeLayerSettings dampingLayer = Layer(
        "ripple-tank", "tank-damping-options");
      dampingLayer.RendererParams = new Dictionary<string, double> {
        ["damping"] = 0.04,
      };
      AssertClose(0.04,
        BuiltInOptions<RippleTankLayerOptions>(dampingLayer).Damping,
        "Ripple Tank damping did not compile into renderer options");

      double baseline =
        LEDDomeRippleTankVisualizer.WakeStrengthForAngularSpeed(0);
      double slow =
        LEDDomeRippleTankVisualizer.WakeStrengthForAngularSpeed(1);
      double fast =
        LEDDomeRippleTankVisualizer.WakeStrengthForAngularSpeed(2);
      Assert(baseline > 0,
        "slow tank motion does not have a visible wake baseline");
      Assert(slow > baseline && fast > slow,
        "wake height does not increase with sensor angular speed");
      AssertClose(1,
        LEDDomeRippleTankVisualizer.WakeStrengthForAngularSpeed(99),
        "wake strength does not cap at the stable maximum");

      var config = ConfigurationWithLayers(
        Layer("caustics", "caustics-inputs"),
        Layer("ripple-tank", "tank-inputs"));
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer? causticsRenderer = null;
      DomeLayerVisualizer? tankRenderer = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is not DomeLayerVisualizer layer) {
          continue;
        }
        if (layer.LayerKey == "caustics") {
          causticsRenderer = layer;
        } else if (layer.LayerKey == "ripple-tank") {
          tankRenderer = layer;
        }
      }
      Assert(causticsRenderer != null &&
        causticsRenderer.GetInputs().Length == 0,
        "Caustics still activates an input");
      Assert(tankRenderer != null && tankRenderer.GetInputs().Length == 1 &&
        ReferenceEquals(
          tankRenderer.GetInputs()[0], runtime.OrientationInput),
        "Ripple Tank is not bound exclusively to OrientationInput");
    }

    private static void WatchfulIrisBehavesAsSceneCharacter() {
      LayerDefinition? definition = DomeLayerCatalog.Metadata.Get("watchful-iris");
      Assert(definition != null && definition.DisplayName == "Watchful Iris",
        "Watchful Iris was not registered");
      Assert(definition.FireAction?.Label == "Blink",
        "Watchful Iris manual blink action was not registered");

      WatchfulIrisLayerOptions defaults =
        BuiltInOptions<WatchfulIrisLayerOptions>(
          Layer("watchful-iris", "watchful-iris-defaults"));
      Assert(defaults.IrisComplexity == 14 &&
          defaults.PupilSize == 0.28 && defaults.DilationGain == 0.28 &&
          defaults.BlinkTrigger == 2 && defaults.EyelidSoftness == 0.035 &&
          defaults.ScleraBrightness == 1 && defaults.Palette == 0,
        "unexpected Watchful Iris defaults");
      Assert(definition.Parameters.All(
          parameter => parameter.Key != "trackingMode"),
        "Watchful Iris still exposes a tracking mode");

      DomeLayerSettings configured = Layer(
        "watchful-iris", "watchful-iris-clamped");
      configured.RendererParams = new Dictionary<string, double> {
        ["irisComplexity"] = 99,
        ["pupilSize"] = -1,
        ["dilationGain"] = 99,
        ["blinkTrigger"] = 99,
        ["eyelidSoftness"] = 99,
        ["scleraBrightness"] = 99,
        ["palette"] = 99,
      };
      WatchfulIrisLayerOptions clamped =
        BuiltInOptions<WatchfulIrisLayerOptions>(configured);
      Assert(clamped.IrisComplexity == 32 && clamped.PupilSize == 0.08 &&
          clamped.DilationGain == 0.8 && clamped.BlinkTrigger == 2 &&
          clamped.EyelidSoftness == 0.18 && clamped.ScleraBrightness == 2 &&
          clamped.Palette == PaletteService.MaxPalettes - 1,
        "Watchful Iris controls did not clamp");

      AssertClose(0.28,
        LEDDomeWatchfulIrisVisualizer.EffectivePupilRatio(0.28, 0.28, 0),
        "quiet Watchful Iris pupil changed its configured size");
      AssertClose(0.56,
        LEDDomeWatchfulIrisVisualizer.EffectivePupilRatio(0.28, 0.28, 1),
        "Watchful Iris dilation did not respond to audio");
      AssertClose(1, LEDDomeWatchfulIrisVisualizer.BlinkOpenness(0),
        "Watchful Iris blink did not begin open");
      AssertClose(0, LEDDomeWatchfulIrisVisualizer.BlinkOpenness(0.23),
        "Watchful Iris blink did not fully close");
      AssertClose(1, LEDDomeWatchfulIrisVisualizer.BlinkOpenness(0.46),
        "Watchful Iris blink did not reopen");
      Assert(LEDDomeWatchfulIrisVisualizer.ApertureCoverage(
          0, 0.1, 0, 0.01) == 0 &&
          LEDDomeWatchfulIrisVisualizer.ApertureCoverage(
            0, 0, 1, 0.01) == 1,
        "Watchful Iris eyelids did not mask the eye aperture");

      double simpleFiber = LEDDomeWatchfulIrisVisualizer.IrisFilament(
        0.64, 0.37, 5);
      double complexFiber = LEDDomeWatchfulIrisVisualizer.IrisFilament(
        0.64, 0.37, 25);
      Assert(simpleFiber >= 0 && simpleFiber <= 1 &&
          complexFiber >= 0 && complexFiber <= 1 &&
          Math.Abs(simpleFiber - complexFiber) > 0.01,
        "Watchful Iris complexity did not alter its bounded filament field");
      Assert(LEDDomeWatchfulIrisVisualizer.TrackingOffset(
          Quaternion.Identity).X < -0.3 &&
          LEDDomeWatchfulIrisVisualizer.TrackingOffset(
            Quaternion.CreateFromAxisAngle(
              Vector3.UnitZ, (float)Math.PI)).X > 0.3,
        "Watchful Iris did not follow the shared orientation center");

      Vector3 dramaticFacing =
        LEDDomeWatchfulIrisVisualizer.FacingFromGaze(
          new Vector2(0.31f, 0.18f));
      Assert(dramaticFacing.X > 0.5 && dramaticFacing.Y > 0.35 &&
          dramaticFacing.Z < 0.75 &&
          Math.Abs(dramaticFacing.Length() - 1) < 0.000001,
        "Watchful Iris did not lift its gaze into a dramatic spherical turn");
      Vector3 laggedFacing = LEDDomeWatchfulIrisVisualizer.SmoothFacing(
        Vector3.UnitZ, dramaticFacing, 0.1, 0.42);
      double laggedTurn = Math.Acos(Math.Clamp(laggedFacing.Z, -1, 1));
      double targetTurn = Math.Acos(Math.Clamp(dramaticFacing.Z, -1, 1));
      Assert(laggedTurn > 0 && laggedTurn < targetTurn,
        "Watchful Iris globe did not pursue its iris with inertia");
      Quaternion globeRotation =
        LEDDomeWatchfulIrisVisualizer.RotationFromForward(laggedFacing);
      Assert(Vector3.Distance(
          Vector3.Transform(Vector3.UnitZ, globeRotation),
          laggedFacing) < 0.000001,
        "Watchful Iris globe rotation did not face its lagged gaze");
      Quaternion pursuedRotation =
        LEDDomeWatchfulIrisVisualizer.SmoothGlobeRotation(
          Quaternion.Identity, dramaticFacing, 0.1, 0.34);
      Vector3 pursuedFacing = Vector3.Transform(
        Vector3.UnitZ, pursuedRotation);
      Vector3 expectedPursuit =
        LEDDomeWatchfulIrisVisualizer.SmoothFacing(
          Vector3.UnitZ, dramaticFacing, 0.1, 0.34);
      Assert(Vector3.Distance(pursuedFacing, expectedPursuit) < 0.000001,
        "Watchful Iris full globe orientation did not pursue its gaze");

      // Turning through two different axes must carry the first turn's
      // orientation forward. Rebuilding a minimal forward rotation would map
      // the pole correctly but discard this spherical transport/torsion.
      Vector3 rightFacing =
        LEDDomeWatchfulIrisVisualizer.FacingFromGaze(
          new Vector2(0.31f, 0));
      Vector3 upperFacing =
        LEDDomeWatchfulIrisVisualizer.FacingFromGaze(
          new Vector2(0, 0.18f));
      Quaternion transported =
        LEDDomeWatchfulIrisVisualizer.SmoothGlobeRotation(
          Quaternion.Identity, rightFacing, 0.1, 0);
      transported = LEDDomeWatchfulIrisVisualizer.SmoothGlobeRotation(
        transported, upperFacing, 0.1, 0);
      Quaternion rebuilt =
        LEDDomeWatchfulIrisVisualizer.RotationFromForward(upperFacing);
      Assert(Vector3.Distance(
          Vector3.Transform(Vector3.UnitZ, transported),
          upperFacing) < 0.000001 &&
          Vector3.Distance(
            Vector3.Transform(Vector3.UnitX, transported),
            Vector3.Transform(Vector3.UnitX, rebuilt)) > 0.01,
        "Watchful Iris sclera discarded its transported globe orientation");

      // At a nearly closed openness, the thin visible seam must travel to the
      // rotated forward pole. Evaluating the same pole in fixed dome axes
      // would incorrectly leave it behind the old horizontal eyelids.
      Vector3 transportedPole = Vector3.Transform(
        Vector3.UnitZ, transported);
      Vector3 localPole =
        LEDDomeWatchfulIrisVisualizer.GlobeLocalPosition(
          transportedPole, transported);
      double transportedClosure =
        LEDDomeWatchfulIrisVisualizer.ApertureCoverage(
          localPole.X, localPole.Y, 0.15, 0.01);
      double fixedClosure =
        LEDDomeWatchfulIrisVisualizer.ApertureCoverage(
          transportedPole.X, transportedPole.Y, 0.15, 0.01);
      Assert(Vector3.Distance(localPole, Vector3.UnitZ) < 0.000001 &&
          transportedClosure > 0.99 && fixedClosure < 0.01,
        "Watchful Iris blink seam did not rotate to the globe's new meridian");
      Vector3 vesselPoint = Vector3.Normalize(
        new Vector3(0.93f, -0.27f, 0));
      Assert(LEDDomeWatchfulIrisVisualizer.ScleraVascularStrength(
          vesselPoint) > 0.65 &&
          LEDDomeWatchfulIrisVisualizer.ScleraVascularStrength(
            Vector3.UnitZ) < 0.01,
        "Watchful Iris lacks visible globe-anchored rotation landmarks");
      Assert(LEDDomeWatchfulIrisVisualizer.ScaleScleraColor(
          0x804020, 0) == 0 &&
          LEDDomeWatchfulIrisVisualizer.ScaleScleraColor(
            0x804020, 0.5) == 0x402010 &&
          LEDDomeWatchfulIrisVisualizer.ScaleScleraColor(
            0xC08040, 2) == 0xFFFF80,
        "Watchful Iris sclera brightness did not scale and clip RGB");

      var onset = new IrisTransientDetector(0.48, 0.14);
      Assert(!onset.Sample(0.1, 0.016) && onset.Sample(0.8, 0.016) &&
          !onset.Sample(0.8, 0.016),
        "Watchful Iris audio onset did not emit one blink edge");

      var config = ConfigurationWithLayers(
        Layer("watchful-iris", "watchful-iris-render"));
      SetPaletteColors(config, color => 0x205090 + color * 0x160C02);
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer? iris = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "watchful-iris") {
          iris = layer;
          break;
        }
      }
      Assert(iris != null, "Watchful Iris renderer was not created");
      Input[] inputs = iris.GetInputs();
      Assert(inputs.Length == 2 &&
          ReferenceEquals(inputs[0], runtime.AudioInput) &&
          ReferenceEquals(inputs[1], runtime.OrientationInput),
        "Watchful Iris did not declare audio and orientation inputs");
      ((Visualizer)iris).Visualize();
      Assert(iris.LayerBuffer.pixels.Any(pixel => pixel.color != 0) &&
          iris.LayerBuffer.pixels
            .Select(pixel => pixel.color).Distinct().Count() > 12,
        "Watchful Iris did not render a patterned eye");
      Assert(iris.LayerBuffer.pixels.Any(pixel => {
          int color = pixel.color;
          return ((color >> 16) & 0xFF) + ((color >> 8) & 0xFF)
            + (color & 0xFF) < 30;
        }) && iris.LayerBuffer.pixels.Any(pixel => {
          int color = pixel.color;
          return ((color >> 16) & 0xFF) + ((color >> 8) & 0xFF)
            + (color & 0xFF) > 560;
        }),
        "Watchful Iris did not separate its dark pupil/lids and light sclera");
    }

    private static void LivingSkinUsesReactionDiffusion() {
      LayerDefinition? definition = DomeLayerCatalog.Metadata.Get("living-skin");
      Assert(definition != null && definition.DisplayName == "Living Skin",
        "Living Skin was not registered");
      Assert(definition.FireAction?.Label == "Fire" &&
          definition.ClearAction?.Label == "Clear",
        "Living Skin seed/clear actions were not registered");

      LivingSkinLayerOptions defaults =
        BuiltInOptions<LivingSkinLayerOptions>(
          Layer("living-skin", "living-skin-defaults"));
      AssertClose(0.0367, defaults.FeedRate,
        "unexpected Living Skin feed rate");
      AssertClose(0.0649, defaults.KillRate,
        "unexpected Living Skin kill rate");
      AssertClose(2, defaults.DiffusionScale,
        "unexpected Living Skin diffusion scale");
      AssertClose(1, defaults.SimulationSpeed,
        "unexpected Living Skin simulation speed");
      Assert(defaults.SeedSource == 1 && defaults.EdgeContrast == 3 &&
          defaults.FeedButton == 1 && defaults.PoisonButton == 2 &&
          defaults.EraseButton == 3 && defaults.BrushRadius == 2 &&
          defaults.BrushStrength == 0.35 && defaults.Palette == 0,
        "unexpected Living Skin seed, edge, brush, or palette default");

      DomeLayerSettings configured = Layer(
        "living-skin", "living-skin-clamped");
      configured.RendererParams = new Dictionary<string, double> {
        ["feedRate"] = -1,
        ["killRate"] = 1,
        ["diffusionScale"] = 99,
        ["simulationSpeed"] = -1,
        ["seedSource"] = 99,
        ["edgeContrast"] = 99,
        ["feedButton"] = 99,
        ["poisonButton"] = -1,
        ["eraseButton"] = 99,
        ["brushRadius"] = 99,
        ["brushStrength"] = -1,
        ["palette"] = 99,
      };
      LivingSkinLayerOptions clamped =
        BuiltInOptions<LivingSkinLayerOptions>(configured);
      AssertClose(0.01, clamped.FeedRate,
        "Living Skin feed rate did not clamp");
      AssertClose(0.08, clamped.KillRate,
        "Living Skin kill rate did not clamp");
      Assert(clamped.DiffusionScale == 4 &&
          clamped.SimulationSpeed == 0 && clamped.SeedSource == 1 &&
          clamped.EdgeContrast == 8 && clamped.FeedButton == 3 &&
          clamped.PoisonButton == 0 && clamped.EraseButton == 3 &&
          clamped.BrushRadius == 4 && clamped.BrushStrength == 0.05 &&
          clamped.Palette == PaletteService.MaxPalettes - 1,
        "Living Skin controls did not clamp");

      Assert(LEDDomeLivingSkinVisualizer.BrushModeForButton(1, defaults) ==
          LivingSkinBrushMode.Feed &&
          LEDDomeLivingSkinVisualizer.BrushModeForButton(2, defaults) ==
          LivingSkinBrushMode.Poison &&
          LEDDomeLivingSkinVisualizer.BrushModeForButton(3, defaults) ==
          LivingSkinBrushMode.Erase &&
          LEDDomeLivingSkinVisualizer.BrushModeForButton(0, defaults) == null,
        "Living Skin button bindings did not select the configured brushes");

      const int size = 15;
      var simulation = new LivingSkinSimulation(
        new DomeFrame(GridTopology(size, size, 0.02)));
      simulation.Initialize(0);
      int center = size / 2 * size + size / 2;
      int frontier = center + 3;
      AssertClose(1, simulation.ChemicalAAt(center),
        "Living Skin clear state did not restore chemical A");
      AssertClose(0, simulation.ChemicalBAt(center),
        "Living Skin clear state retained chemical B");

      simulation.SeedAt(center);
      Assert(simulation.ChemicalBAt(center) > 0.9,
        "Living Skin seed did not inject chemical B");
      AssertClose(0, simulation.ChemicalBAt(frontier),
        "Living Skin seed escaped its injection patch");

      simulation.Clear();
      simulation.BrushAt(center, 1, 1, LivingSkinBrushMode.Feed);
      AssertClose(1, simulation.ChemicalAAt(center),
        "Living Skin feed brush did not restore substrate");
      Assert(simulation.ChemicalBAt(center) > 0,
        "Living Skin feed brush did not paint an activator trace");
      AssertClose(0, simulation.ChemicalBAt(frontier),
        "Living Skin feed brush escaped its bounded radius");
      simulation.BrushAt(center, 1, 1, LivingSkinBrushMode.Poison);
      AssertClose(0, simulation.ChemicalAAt(center),
        "Living Skin poison brush did not starve substrate");
      Assert(simulation.ChemicalBAt(center) < 0.05,
        "Living Skin poison brush did not suppress the activator");
      simulation.BrushAt(center, 1, 1, LivingSkinBrushMode.Erase);
      AssertClose(1, simulation.ChemicalAAt(center),
        "Living Skin erase brush did not restore dormant substrate");
      AssertClose(0, simulation.ChemicalBAt(center),
        "Living Skin erase brush retained activator");

      simulation.SeedAt(center);
      simulation.Step(0.0367, 0.0649, 1);
      Assert(simulation.ChemicalBAt(frontier) > 0,
        "Living Skin did not diffuse chemical B to a neighbor");

      for (int step = 0; step < 100; step++) {
        simulation.Step(0.0367, 0.0649, 1);
      }
      int livingPixels = 0;
      for (int i = 0; i < size * size; i++) {
        double a = simulation.ChemicalAAt(i);
        double b = simulation.ChemicalBAt(i);
        Assert(double.IsFinite(a) && double.IsFinite(b) &&
            a >= 0 && a <= 1 && b >= 0 && b <= 1,
          "Living Skin chemical state left its finite unit bounds");
        if (b > 0.01) {
          livingPixels++;
        }
      }
      Assert(livingPixels > 1,
        "Living Skin reaction collapsed instead of persisting");
      simulation.Clear();
      simulation.Step(0.0367, 0.0649, 1);
      for (int i = 0; i < size * size; i++) {
        AssertClose(1, simulation.ChemicalAAt(i),
          "Living Skin dormant A field evolved after clear");
        AssertClose(0, simulation.ChemicalBAt(i),
          "Living Skin dormant B field evolved after clear");
      }

      var config = ConfigurationWithLayers(
        Layer("living-skin", "living-skin-inputs"));
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer? livingSkin = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "living-skin") {
          livingSkin = layer;
          break;
        }
      }
      Assert(livingSkin != null, "Living Skin renderer was not created");
      Input[] livingSkinInputs = livingSkin.GetInputs();
      Assert(livingSkinInputs.Length == 1 &&
          ReferenceEquals(livingSkinInputs[0], runtime.OrientationInput),
        "Living Skin did not declare its wand-orientation input");
      ((Visualizer)livingSkin).Visualize();
    }

  }
}