using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.LayerPipeline.Tests {

  public static class DomeTopologyTests {
    public static void Register(Action<string, Action> run) {
      run(nameof(SimulatorTopDownProjection), SimulatorTopDownProjection);
      run(nameof(DomeTopologyUsesTopDownNormals),
        DomeTopologyUsesTopDownNormals);
      run(nameof(InstalledWiringLayoutIsTotalAndCollisionFree),
        InstalledWiringLayoutIsTotalAndCollisionFree);
      run(nameof(SphereDirectionsProjectToStripExtents),
        SphereDirectionsProjectToStripExtents);
      run(nameof(TargetedPlanarCoordinatesRoundTripNormals),
        TargetedPlanarCoordinatesRoundTripNormals);
      run(nameof(TunnelFixedModeMatchesCrownAxis),
        TunnelFixedModeMatchesCrownAxis);
    }

    private static void SimulatorTopDownProjection() {
      const int strut = 65;
      Tuple<double, double> stripPoint =
        StrutLayoutFactory.GetProjectedPoint(strut, 0);
      Tuple<double, double> explicitStripPoint =
        StrutLayoutFactory.GetProjectedPoint(
          strut, 0, DomeProjection.StripExtents);
      Tuple<double, double> topDownPoint =
        StrutLayoutFactory.GetProjectedPoint(
          strut, 0, DomeProjection.TopDown);

      AssertClose(stripPoint.Item1, explicitStripPoint.Item1,
        "the default simulator projection changed x");
      AssertClose(stripPoint.Item2, explicitStripPoint.Item2,
        "the default simulator projection changed y");

      double stripX = stripPoint.Item1 - .5;
      double stripY = stripPoint.Item2 - .5;
      double topDownX = topDownPoint.Item1 - .5;
      double topDownY = topDownPoint.Item2 - .5;
      double stripRadius = Math.Sqrt(stripX * stripX + stripY * stripY);
      double topDownRadius = Math.Sqrt(
        topDownX * topDownX + topDownY * topDownY);
      Assert(topDownRadius > stripRadius,
        "the top-down projection did not spread the dome crown");
      Assert(topDownRadius <= .5000000001,
        "the top-down projection escaped the dome silhouette");
      AssertClose(0, stripX * topDownY - stripY * topDownX,
        "the top-down projection changed azimuth");

      int ledIndex = 3;
      Tuple<double, double> endpoint0 =
        StrutLayoutFactory.GetProjectedPoint(
          strut, 0, DomeProjection.TopDown);
      Tuple<double, double> endpoint1 =
        StrutLayoutFactory.GetProjectedPoint(
          strut, 1, DomeProjection.TopDown);
      Tuple<double, double> led = StrutLayoutFactory.GetProjectedLEDPoint(
        strut, ledIndex, DomeProjection.TopDown);
      double d = (ledIndex + 1.0) /
        (LEDDomeOutput.GetNumLEDs(strut) + 2.0);
      AssertClose(endpoint0.Item1 + (endpoint1.Item1 - endpoint0.Item1) * d,
        led.Item1, "the top-down LED left its physical strut (x)");
      AssertClose(endpoint0.Item2 + (endpoint1.Item2 - endpoint0.Item2) * d,
        led.Item2, "the top-down LED left its physical strut (y)");

      double expectedTheta = Math.Min(stripRadius * 2, 1) * Math.PI / 2;
      double projectedTheta = Math.Asin(Math.Min(topDownRadius * 2, 1));
      AssertClose(expectedTheta, projectedTheta,
        "the top-down projection changed the physical polar angle");
    }

    private static void DomeTopologyUsesTopDownNormals() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var output = new LEDDomeOutput(
        config, new RuntimeTelemetry(), new BeatBroadcaster(config));
      DomeFrame frame = output.MakeDomeFrame();

      Assert(frame.Topology.PixelCount == frame.pixels.Length,
        "the topology and frame pixel counts differ");
      for (int i = 0; i < frame.Topology.PixelCount; i++) {
        DomeTopologyPixel pixel = frame.Topology.PixelAt(i);
        Tuple<double, double> strip =
          StrutLayoutFactory.GetProjectedLEDPoint(
            pixel.StrutIndex, pixel.LedIndex, DomeProjection.StripExtents);
        Tuple<double, double> topDown =
          StrutLayoutFactory.GetProjectedLEDPoint(
            pixel.StrutIndex, pixel.LedIndex, DomeProjection.TopDown);
        AssertClose(strip.Item1, pixel.StripX,
          "topology strip x differs at pixel " + i);
        AssertClose(strip.Item2, pixel.StripY,
          "topology strip y differs at pixel " + i);
        Assert(pixel.X == pixel.StripX && pixel.Y == pixel.StripY,
          "the planar compatibility coordinates changed at pixel " + i);
        AssertClose(topDown.Item1, pixel.TopDownX,
          "topology top-down x differs at pixel " + i);
        AssertClose(topDown.Item2, pixel.TopDownY,
          "topology top-down y differs at pixel " + i);

        double x = 2 * pixel.TopDownX - 1;
        double y = 1 - 2 * pixel.TopDownY;
        Assert(x * x + y * y <= 1.000000001,
          "top-down pixel escaped the dome silhouette at pixel " + i);

        Vector3 normal = frame.Normals[i];
        Assert(float.IsFinite(normal.X) && float.IsFinite(normal.Y) &&
          float.IsFinite(normal.Z),
          "topology normal is not finite at pixel " + i);
        Assert(normal.Z >= 0,
          "topology normal points below the rim at pixel " + i);
        Assert(Math.Abs(normal.Length() - 1) < .000001,
          "topology normal is not unit length at pixel " + i);
      }

      var explicitProjections = new DomeTopology(new[] {
        new DomeTopologyPixel(0, 0, .5, .5, .75, .5),
        new DomeTopologyPixel(1, 0, .5, .5, 1.1, .5),
      });
      Vector3 midDome = explicitProjections.Normals[0];
      Assert(Math.Abs(midDome.X - .5) < .000001 &&
        Math.Abs(midDome.Z - Math.Sqrt(.75)) < .000001,
        "normal construction used strip coordinates instead of top-down");
      Vector3 clampedRim = explicitProjections.Normals[1];
      Assert(Math.Abs(clampedRim.X - 1) < .000001 &&
        Math.Abs(clampedRim.Z) < .000001 &&
        Math.Abs(clampedRim.Length() - 1) < .000001,
        "top-down overshoot was not clamped to a unit rim normal");
    }

    private static void InstalledWiringLayoutIsTotalAndCollisionFree() {
      Assert(DomeWiringLayout.StrutCount == 190,
        "the installed wiring layout changed its strut count");
      Assert(LEDDomeOutput.GetNumStruts() == DomeWiringLayout.StrutCount,
        "the output facade differs from the installed wiring layout");

      var indexedStruts = new HashSet<int>();
      for (int box = 0; box < LEDDomeOutput.NumDomeBoxes; box++) {
        var sequentialStruts = new HashSet<int>();
        for (int localIndex = 0; localIndex < 38; localIndex++) {
          int strut = DomeWiringLayout.FindStrutIndex(box, localIndex);
          Assert(strut >= 0,
            "the installed box has a missing sequential strut");
          sequentialStruts.Add(strut);
          indexedStruts.Add(strut);
        }

        var cableStruts = new HashSet<int>(
          DomeWiringLayout.GetControllerCableStruts(box, 0));
        cableStruts.UnionWith(
          DomeWiringLayout.GetControllerCableStruts(box, 1));
        Assert(cableStruts.SetEquals(sequentialStruts),
          "controller cable regions do not cover the installed box");

        var pathStruts = new HashSet<int>();
        for (int path = 0; path < LEDDomeOutput.NumPortsPerBox; path++) {
          pathStruts.UnionWith(
            DomeWiringLayout.GetStripPathStruts(box, path));
        }
        Assert(pathStruts.SetEquals(sequentialStruts),
          "controller paths do not cover the installed box");
      }
      Assert(indexedStruts.Count == DomeWiringLayout.StrutCount,
        "installed box indexing does not cover every logical strut");

      var rawPixels = new HashSet<(int Box, int Pixel)>();
      int pixelCount = 0;
      for (int strut = 0; strut < DomeWiringLayout.StrutCount; strut++) {
        int ledCount = DomeWiringLayout.GetLedCount(strut);
        Assert(ledCount == LEDDomeOutput.GetNumLEDs(strut),
          "the output LED-count facade differs from installed wiring");
        for (int led = 0; led < ledCount; led++) {
          Tuple<int, int> address =
            DomeWiringLayout.GetRawAddress(strut, led);
          Assert(address.Item1 >= 0 &&
            address.Item1 < LEDDomeOutput.NumDomeBoxes,
            "a raw pixel address uses an invalid control box");
          Assert(address.Item2 >= 0 &&
            address.Item2 < DomeWiringLayout.ControlBoxPixelCount,
            "a raw pixel address escapes its control-box frame");
          Assert(rawPixels.Add((address.Item1, address.Item2)),
            "two logical pixels share one raw device address");
          pixelCount++;
        }
      }

      DomeFrame first = DomeWiringLayout.MakeFrame();
      DomeFrame second = DomeWiringLayout.MakeFrame();
      Assert(first.Topology.PixelCount == pixelCount,
        "the projected topology differs from raw wiring pixel count");
      Assert(ReferenceEquals(first.Topology, second.Topology),
        "the immutable installed topology was rebuilt per frame");
    }

    private static void SphereDirectionsProjectToStripExtents() {
      Vector2 crown = StrutLayoutFactory.ProjectSphereToStrip(Vector3.UnitZ);
      Assert(crown.Length() < .000001,
        "the crown did not project to the strip origin");

      double theta = Math.PI / 4;
      double azimuth = Math.PI / 6;
      var direction = new Vector3(
        (float)(Math.Sin(theta) * Math.Cos(azimuth)),
        (float)(Math.Sin(theta) * Math.Sin(azimuth)),
        (float)Math.Cos(theta));
      Vector2 midDome = StrutLayoutFactory.ProjectSphereToStrip(
        direction * 3);
      Assert(Math.Abs(midDome.Length() - .5) < .000001,
        "mid-dome direction has the wrong strip radius");
      Assert(Math.Abs(midDome.X - .5 * Math.Cos(azimuth)) < .000001 &&
        Math.Abs(midDome.Y + .5 * Math.Sin(azimuth)) < .000001,
        "mid-dome direction changed azimuth");

      Vector2 rim = StrutLayoutFactory.ProjectSphereToStrip(Vector3.UnitY);
      Assert(Math.Abs(rim.X) < .000001 && Math.Abs(rim.Y + 1) < .000001,
        "rim direction did not reach the strip silhouette");
      Vector2 foldedAxis = StrutLayoutFactory.ProjectSphereToStrip(
        new Vector3(1, 0, -1), foldAxisToUpperHemisphere: true);
      Assert(Math.Abs(foldedAxis.X + .5) < .000001 &&
        Math.Abs(foldedAxis.Y) < .000001,
        "axis projection did not select the upper-hemisphere endpoint");

      foreach (Vector3 expected in new[] {
        Vector3.UnitZ, direction, Vector3.UnitY,
      }) {
        Vector2 strip = StrutLayoutFactory.ProjectSphereToStrip(expected);
        double radius = strip.Length();
        double roundTripTheta = radius * Math.PI / 2;
        Vector3 actual = radius < .0000001
          ? Vector3.UnitZ
          : Vector3.Normalize(new Vector3(
              (float)(Math.Sin(roundTripTheta) * strip.X / radius),
              (float)(-Math.Sin(roundTripTheta) * strip.Y / radius),
              (float)Math.Cos(roundTripTheta)));
        Assert(Vector3.Distance(expected, actual) < .000001,
          "sphere-to-strip round trip changed a canonical direction");
      }

      bool rejectedZero = false;
      try {
        StrutLayoutFactory.ProjectSphereToStrip(Vector3.Zero);
      } catch (ArgumentException) {
        rejectedZero = true;
      }
      Assert(rejectedZero, "sphere-to-strip accepted a zero direction");

      bool rejectedLowerHemisphere = false;
      try {
        StrutLayoutFactory.ProjectSphereToStrip(
          new Vector3(1, 0, -1));
      } catch (ArgumentOutOfRangeException) {
        rejectedLowerHemisphere = true;
      }
      Assert(rejectedLowerHemisphere,
        "sphere-to-strip silently flattened a lower-hemisphere direction");
    }

    private static void TargetedPlanarCoordinatesRoundTripNormals() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var output = new LEDDomeOutput(
        config, new RuntimeTelemetry(), new BeatBroadcaster(config));
      DomeFrame frame = output.MakeDomeFrame();
      ImmutableArray<Vector2> projected =
        DomeSurfaceGeometry.ProjectNormalsToStrip(frame.Normals);

      Assert(projected.Length == frame.Topology.PixelCount,
        "targeted planar projection changed the pixel count");
      double maximumRoundTripError = 0;
      for (int i = 0; i < projected.Length; i++) {
        Vector2 strip = projected[i];
        double radius = strip.Length();
        Assert(float.IsFinite(strip.X) && float.IsFinite(strip.Y) &&
          radius <= 1.000001,
          "targeted planar coordinate left the dome at pixel " + i);

        double theta = radius * Math.PI / 2;
        Vector3 roundTrip = radius < .0000001
          ? Vector3.UnitZ
          : Vector3.Normalize(new Vector3(
              (float)(Math.Sin(theta) * strip.X / radius),
              (float)(-Math.Sin(theta) * strip.Y / radius),
              (float)Math.Cos(theta)));
        double roundTripError = Vector3.Distance(frame.Normals[i], roundTrip);
        maximumRoundTripError = Math.Max(
          maximumRoundTripError, roundTripError);
      }
      Assert(maximumRoundTripError < .00005,
        "targeted planar round trip exceeded float tolerance: " +
        maximumRoundTripError);
    }

    private static void TunnelFixedModeMatchesCrownAxis() {
      var config = new global::Spectrum.SpectrumConfiguration();
      var output = new LEDDomeOutput(
        config, new RuntimeTelemetry(), new BeatBroadcaster(config));
      ImmutableArray<Vector3> positions =
        output.MakeDomeFrame().BakePixelPositions();
      double[] fixedRadii =
        LEDDomeTunnelVisualizer.BuildFixedRadii(positions);
      var crownRadii = new double[positions.Length];
      LEDDomeTunnelVisualizer.BuildNormalizedAngularRadii(
        positions, Vector3.UnitZ, crownRadii);

      Assert(fixedRadii.Length == positions.Length,
        "Tunnel fixed radius count differs from the topology");
      for (int i = 0; i < positions.Length; i++) {
        AssertClose(fixedRadii[i], crownRadii[i],
          "Tunnel fixed and crown-bound radii differ at pixel " + i);
      }
      AssertClose(1, fixedRadii.Max(),
        "Tunnel fixed field does not reach the farthest LED");
    }

    private static void AssertClose(
      double expected,
      double actual,
      string message
    ) {
      Assert(Math.Abs(expected - actual) < 0.000001,
        message + ": expected " + expected + ", actual " + actual);
    }
  }
}
