namespace Spectrum.Base {

  // Supplies the compositor's prism blends (docs/prism.md) with a live
  // wand-orientation angle, so ChromaticFringe's split axis and Iridescence's
  // light can follow the spotlighted wand. Implemented by OrientationCenter
  // (Spectrum project) and injected into LEDDomeOutput (LEDs project), which
  // can't reference OrientationCenter directly — hence this small interface in
  // Base, which every project references.
  public interface OrientationAngleProvider {

    // The spotlighted wand's aim as an angle (radians) in the dome's projected
    // plane. Returns false when no wand is currently the orientation center
    // (idle, or none moving), in which case a following blend keeps its static
    // angle. The value is only as fresh as the last OrientationCenter.Update(),
    // so it tracks the wand live while an orientation-driven layer is also
    // active in the stack.
    bool TryGetAngle(out double angle);
  }
}
