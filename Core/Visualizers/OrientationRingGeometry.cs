namespace Spectrum.Visualizers {

  // Shared surface-distance tuning for the standalone Ripple/Stamp layers and
  // the copies still composited inside Quaternion Paintbrush. The old chord
  // domain ran from 0 to 2; normalized angular distance runs from 0 to 1, so
  // the corresponding radii and widths are halved while effect lifetimes and
  // crown/antipode endpoints remain unchanged.
  static class OrientationRingGeometry {
    private const double RIPPLE_RADIUS_DIVISOR = 600;
    private const double RIPPLE_HALF_WIDTH = .005;
    private const double STAMP_GRID_SPACING = .2;
    private const double STAMP_GRID_WIDTH = .025;
    private const double STAMP_HALF_WIDTH_FACTOR = .0015;

    private static readonly AngularRingBand[] stampGrid =
      new AngularRingBand[] {
        AngularRingBand.FromBounds(0, STAMP_GRID_WIDTH),
        AngularRingBand.FromBounds(
          STAMP_GRID_SPACING, STAMP_GRID_SPACING + STAMP_GRID_WIDTH),
        AngularRingBand.FromBounds(
          2 * STAMP_GRID_SPACING, 2 * STAMP_GRID_SPACING + STAMP_GRID_WIDTH),
        AngularRingBand.FromBounds(
          3 * STAMP_GRID_SPACING, 3 * STAMP_GRID_SPACING + STAMP_GRID_WIDTH),
        AngularRingBand.FromBounds(
          4 * STAMP_GRID_SPACING, 4 * STAMP_GRID_SPACING + STAMP_GRID_WIDTH),
      };

    public static AngularRingBand RippleBand(double rippleCounter) {
      return AngularRingBand.Centered(
        rippleCounter / RIPPLE_RADIUS_DIVISOR, RIPPLE_HALF_WIDTH);
    }

    public static AngularRingBand StampBand(
      double normalizedRadius, int cooldown
    ) {
      return AngularRingBand.Centered(
        normalizedRadius,
        STAMP_HALF_WIDTH_FACTOR * cooldown * cooldown);
    }

    public static bool StampGridContains(double dot) {
      for (int i = 0; i < stampGrid.Length; i++) {
        if (stampGrid[i].ContainsDot(dot)) {
          return true;
        }
      }
      return false;
    }
  }
}
