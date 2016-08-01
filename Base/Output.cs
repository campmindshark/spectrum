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
     * If a given Output is enabled, then there is a thread running that is
     * dequeuing commands from the Visualizer and sending them to the device.
     * We only need an Output to be enabled when there exists at least one
     * enabled Visualizer that is active on that Output.
     */
    bool Enabled { get; set; }

    /**
     * This method will flush the cached commands we have to the output device
     * in question. We need this method in the base interface so that if the
     * Operator is running output on their thread, they know how to trigger it.
     */
    void Update();

  }

}
