namespace Spectrum.Base {

  // Supplies compositor operations with a live wand-orientation angle.
  // Implemented by OrientationCenter (Spectrum project) and injected into
  // LEDDomeOutput (LEDs project), which can't reference OrientationCenter
  // directly — hence this small interface in Base, which every project
  // references.
  public interface OrientationAngleProvider {

    // Refresh the provider from the current operator-frame device snapshot.
    // The compositor calls this before the first operation that declares an
    // orientation read, allowing an adjustment blend to follow a wand even
    // when its selecting renderer does not use orientation itself.
    void Update();

    // The spotlighted wand's aim as an angle (radians) in the dome's projected
    // plane. Returns false when no wand is currently the orientation center
    // (idle, or none moving), in which case a following blend keeps its static
    // angle. The value is refreshed once per compositor frame before the first
    // operation that declares ReadsOrientation.
    bool TryGetAngle(out double angle);

  }
}
