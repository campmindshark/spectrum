using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.Base {

  /**
   * A Visualizer processes data from Inputs and then draws something pretty on
   * an Output. It is executed on the Operator thread. Visualize() is what the
   * Operator calls to make the Visualizer do work.
   * 
   * Only the Visualizers with the highest nonzero priority for their Output
   * actually get run. If there's a tie, all of the tied Visualizers will run.
   */
  public interface Visualizer {

    int Priority { get; }

    /**
     * The Operator will turn us on if our Priority calls for it.
     */
    bool Enabled { get; set; }

    void Visualize();

    /**
     * Gets the list of Inputs this Visualizer uses. You probably passed these
     * in via the constructor, but keeping track of the dependencies on the
     * Visualizer is convenient for the Operator.
     */
    Input[] GetInputs();

  }

}
