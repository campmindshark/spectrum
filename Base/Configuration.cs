using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.Base {

  /**
   * A reference to a single shared Configuration is passed around all over
   * the Spectrum projects. It is updated by the user through the UI.
   */
  public interface Configuration {

    // If lightsOff is true then we turn off the lights
    bool lightsOff { get; set; }
    // If redAlert is true then we just display red
    bool redAlert { get; set; }
    // If controlLights is false then we just display white
    bool controlLights { get; set; }
    // If controlLights is false, these can modify the color
    int brighten { get; set; }
    int colorslide { get; set; }
    int sat { get; set; }
    // These parameters come from the sliders in the UI and affect audio
    // processing
    float peakC { get; set; }
    float dropQ { get; set; }
    float dropT { get; set; }
    float kickQ { get; set; }
    float kickT { get; set; }
    float snareQ { get; set; }
    float snareT { get; set; }

  }

}
