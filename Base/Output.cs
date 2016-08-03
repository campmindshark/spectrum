namespace Spectrum.Base {

  /**
   * An Output represents a device that receives output from Spectrum. Each
   * Visualization has one Output that it animates to.
   *
   * A Visualizer will have a reference to the specific Output object it needs,
   * so methods regarding Visualizer's access patterns to Output are not
   * included here. Instead, only the methods that are needed for basic
   * maintenance of the Output device are included here.
   */
  public interface Output {

    /**
     * If a given Output is enabled, then we are dequeuing commands from the
     * Visualizer and sending them to the device. We only need an Output to
     * be active when there exists at least one enabled Visualizer that is
     * active on that Output.
     */
    bool Active { get; set; }

    /**
     * This property reflects whether the Output is enabled by the UI.
     */
    bool Enabled { get; }

    /**
     * This method gets called at the end of the Operator loop (from within the
     * Operator thread) when there exists an Visualizer that is running on this
     * Output. It may be a no-op (if, for instance, the Output is being updated
     * in a separate thread).
     */
    void OperatorUpdate();

    /**
     * Registers a Visualizer with this Output (to be returned via
     * GetVisualizers).
     */
    void RegisterVisualizer(Visualizer visualizer);

    /**
     * Returns all the Visualizers that have called RegisterVisualizer on this
     * Output.
     */
    Visualizer[] GetVisualizers();

  }

}
