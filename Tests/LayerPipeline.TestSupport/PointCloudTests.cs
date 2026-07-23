using System;
using System.Collections.Immutable;
using System.Numerics;
using Spectrum.Visualizers;
using static Spectrum.LayerPipeline.Tests.TestAssertions;

namespace Spectrum.LayerPipeline.Tests {

  public static class PointCloudTests {
    public static void Register(Action<string, Action> run) {
      run(nameof(PointCloudUsesVisibleHemisphere),
        PointCloudUsesVisibleHemisphere);
      run(nameof(PointCloudSpatialIndexMatchesBruteForce),
        PointCloudSpatialIndexMatchesBruteForce);
    }

    private static void PointCloudUsesVisibleHemisphere() {
      const int count = 320;
      double previousZ = double.PositiveInfinity;
      for (int i = 0; i < count; i++) {
        Vector3 point =
          LEDDomePointCloudVisualizer.FibonacciHemispherePoint(i, count);
        Assert(point.Z > 0 && point.Z <= 1,
          "Point Cloud seeded a home outside the visible hemisphere");
        Assert(Math.Abs(point.Length() - 1) < .000001,
          "Point Cloud seeded a non-unit home");
        if (i > 0) {
          Assert(Math.Abs((previousZ - point.Z) - 1d / count) < .000001,
            "Point Cloud hemisphere bands are not equal-area");
        }
        previousZ = point.Z;
      }
      Assert(
        LEDDomePointCloudVisualizer.FibonacciHemispherePoint(0, count).Z > .99f &&
        LEDDomePointCloudVisualizer.FibonacciHemispherePoint(
          count - 1, count).Z < .01f,
        "Point Cloud homes do not span crown to rim");

      Vector3 lowerAxis = Vector3.Normalize(new Vector3(1, 2, -3));
      Vector3 folded =
        LEDDomePointCloudVisualizer.FoldAxisToUpperHemisphere(lowerAxis);
      Assert(folded.Z > 0 && Vector3.Distance(folded, -lowerAxis) < .000001,
        "Point Cloud did not fold a lower-hemisphere aim axis");
      Vector3 upperAxis = Vector3.Normalize(new Vector3(-2, 1, 3));
      Assert(Vector3.Distance(
          LEDDomePointCloudVisualizer.FoldAxisToUpperHemisphere(upperAxis),
          upperAxis) < .000001,
        "Point Cloud changed an already-visible aim axis");

      Vector3 crossing = Vector3.Normalize(new Vector3(.4f, .2f, -.1f));
      Vector3 reflected =
        LEDDomePointCloudVisualizer.ReflectAcrossRim(crossing);
      Assert(reflected.Z > 0 && Math.Abs(reflected.Length() - 1) < .000001,
        "Point Cloud rim reflection left the visible hemisphere");
      Assert(reflected.X == crossing.X && reflected.Y == crossing.Y,
        "Point Cloud rim reflection jumped across the dome");
    }

    private static void PointCloudSpatialIndexMatchesBruteForce() {
      const int pixelCount = 1024;
      var positionBuilder = ImmutableArray.CreateBuilder<Vector3>(pixelCount);
      for (int pixel = 0; pixel < pixelCount; pixel++) {
        positionBuilder.Add(
          LEDDomePointCloudVisualizer.FibonacciHemispherePoint(
            pixel, pixelCount));
      }
      ImmutableArray<Vector3> positions = positionBuilder.MoveToImmutable();
      var index =
        new LEDDomePointCloudVisualizer.PixelSpatialIndex(positions);
      var actualValues = new double[pixelCount];
      var actualHues = new double[pixelCount];
      var expectedValues = new double[pixelCount];
      var expectedHues = new double[pixelCount];

      foreach ((int count, double size) in new[] {
          (4, 0.02), (48, 0.14), (320, 0.5),
      }) {
        var spots = new LEDDomePointCloudVisualizer.Spot[count];
        for (int spot = 0; spot < count; spot++) {
          // The coprime permutation keeps spot and pixel lattice indices from
          // lining up while remaining deterministic.
          int position = (spot * 73 + 19) % pixelCount;
          spots[spot] = new LEDDomePointCloudVisualizer.Spot {
            pos = positions[position],
            hue = (spot + 0.25) / count,
          };
        }

        LEDDomePointCloudVisualizer.ResolveNearestSpots(
          index, spots, size, actualValues, actualHues);
        ResolvePointCloudBruteForce(
          positions, spots, size, expectedValues, expectedHues);

        for (int pixel = 0; pixel < pixelCount; pixel++) {
          Assert(actualValues[pixel] == expectedValues[pixel],
            "Point Cloud spatial value differed from brute force at size " +
            size + ", pixel " + pixel);
          if (expectedValues[pixel] > 0) {
            Assert(actualHues[pixel] == expectedHues[pixel],
              "Point Cloud spatial winner differed from brute force at size " +
              size + ", pixel " + pixel);
          }
        }

        // Repeated renders with an unchanged spot size must reuse the index and
        // scratch arrays without adding GC pressure to the operator thread.
        for (int warmup = 0; warmup < 4; warmup++) {
          LEDDomePointCloudVisualizer.ResolveNearestSpots(
            index, spots, size, actualValues, actualHues);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 20; frame++) {
          LEDDomePointCloudVisualizer.ResolveNearestSpots(
            index, spots, size, actualValues, actualHues);
        }
        long allocated =
          GC.GetAllocatedBytesForCurrentThread() - before;
        Assert(allocated == 0,
          "Point Cloud spatial render allocated " + allocated +
          " bytes at size " + size);
      }
    }

    private static void ResolvePointCloudBruteForce(
      ImmutableArray<Vector3> positions,
      LEDDomePointCloudVisualizer.Spot[] spots,
      double spotSize,
      double[] bestValues,
      double[] bestHues
    ) {
      Array.Clear(bestValues, 0, bestValues.Length);
      double cosRadius = Math.Cos(spotSize);
      double radiusSpan = Math.Max(1 - cosRadius, 1e-6);
      for (int pixel = 0; pixel < positions.Length; pixel++) {
        for (int spot = 0; spot < spots.Length; spot++) {
          double cos = Vector3.Dot(positions[pixel], spots[spot].pos);
          if (cos <= cosRadius) {
            continue;
          }
          double value = (cos - cosRadius) / radiusSpan;
          if (value > bestValues[pixel]) {
            bestValues[pixel] = value;
            bestHues[pixel] = spots[spot].hue;
          }
        }
      }
    }
  }
}
