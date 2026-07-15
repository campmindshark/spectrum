using Spectrum.LEDs;
using System;
using System.Collections.Immutable;
using System.Numerics;

namespace Spectrum {

  // Unit-sphere geometry shared by dome effects. Callers supply normalized
  // vectors; clamping the dot product absorbs normal floating-point drift.
  static class DomeSurfaceGeometry {

    // Build the centered azimuthal-equidistant coordinates used by planar
    // effects whose targets originate in sphere space. These coordinates are
    // intentionally derived from the same normals as the targets rather than
    // from the legacy straight-line strip layout, so both sides of a distance
    // calculation use one invertible projection.
    public static ImmutableArray<Vector2> ProjectNormalsToStrip(
      ImmutableArray<Vector3> normals
    ) {
      if (normals.IsDefault) {
        throw new ArgumentException(
          "Sphere normals must be initialized.", nameof(normals));
      }
      var projected = ImmutableArray.CreateBuilder<Vector2>(normals.Length);
      for (int i = 0; i < normals.Length; i++) {
        projected.Add(StrutLayoutFactory.ProjectSphereToStrip(normals[i]));
      }
      return projected.MoveToImmutable();
    }

    // Intrinsic great-circle distance in radians.
    public static double AngularDistance(Vector3 first, Vector3 second) {
      return Math.Acos(UnitSphereDot(first, second));
    }

    // Great-circle distance normalized so coincident points are 0 and
    // antipodal points are 1.
    public static double NormalizedAngularDistance(
      Vector3 first, Vector3 second
    ) {
      return AngularDistance(first, second) / Math.PI;
    }

    public static double NormalizeAngularDistance(
      double angle, double maximumAngle
    ) => maximumAngle <= 0 ? 0 : Math.Clamp(angle / maximumAngle, 0, 1);

    public static double UnitSphereDot(Vector3 first, Vector3 second) {
      return Math.Clamp((double)Vector3.Dot(first, second), -1, 1);
    }
  }

  // A band between two normalized angular distances on the unit sphere.
  // It precomputes cosine limits once per frame so the pixel loop needs only
  // a dot product rather than an acos for every LED.
  readonly struct AngularRingBand {
    private readonly bool hasCoverage;
    private readonly double minimumDot;
    private readonly double maximumDot;

    private AngularRingBand(
      bool hasCoverage, double minimumDot, double maximumDot
    ) {
      this.hasCoverage = hasCoverage;
      this.minimumDot = minimumDot;
      this.maximumDot = maximumDot;
    }

    public static AngularRingBand FromBounds(
      double minimumDistance, double maximumDistance
    ) {
      if (!double.IsFinite(minimumDistance) ||
          !double.IsFinite(maximumDistance) ||
          minimumDistance > maximumDistance) {
        throw new ArgumentOutOfRangeException(
          nameof(minimumDistance),
          "Angular ring bounds must be finite and ordered.");
      }
      if (maximumDistance < 0 || minimumDistance > 1) {
        return new AngularRingBand(false, 0, 0);
      }

      double lower = Math.Clamp(minimumDistance, 0, 1);
      double upper = Math.Clamp(maximumDistance, 0, 1);
      return new AngularRingBand(
        true,
        Math.Cos(upper * Math.PI),
        Math.Cos(lower * Math.PI));
    }

    public static AngularRingBand Centered(
      double radius, double halfWidth
    ) {
      if (!double.IsFinite(radius) ||
          !double.IsFinite(halfWidth) || halfWidth < 0) {
        throw new ArgumentOutOfRangeException(
          nameof(halfWidth),
          "Angular ring radius and width must be finite and non-negative.");
      }
      return FromBounds(radius - halfWidth, radius + halfWidth);
    }

    public bool Contains(Vector3 first, Vector3 second) {
      return ContainsDot(DomeSurfaceGeometry.UnitSphereDot(first, second));
    }

    public bool ContainsDot(double dot) {
      dot = Math.Clamp(dot, -1, 1);
      return this.hasCoverage &&
        dot >= this.minimumDot && dot <= this.maximumDot;
    }
  }
}
