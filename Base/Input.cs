namespace Spectrum.Base {

  /**
   * An Input represents a device that receives input for Spectrum. Each
   * Visualization has one or more Inputs that it receives data from.
   *
   * A Visualizer will have a reference to the specific Input object it needs,
   * so methods regarding Visualizer's access patterns to Input are not included
   * here. Instead, only the methods that are needed for basic maintenance of
   * the Input device are included here.
   */
  public interface Input {

    /**
     * If a given Input is enabled, then there is a thread running that is
     * updating the Input object in some way.
     */
    bool Enabled { get; set; }

    /**
     * This method will update the input in question. We don't know what it will
     * do exactly; subclasses will implement members that expose this updated
     * input. We need this method in the base interface so that if the Operator
     * is running input on their thread, they know how to trigger it.
     */
    void Update();

  }

}
