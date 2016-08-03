using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;

namespace Spectrum.LEDs {

  /**
   * MultiTeensyOutput is a thin layer that holds multiple SimpleTeensyOutputs.
   * The intent is to expose a single API to multiple Teensies. Note that when
   * enabled, one output thread exists for each Teensy.
   */
  public class MultiTeensyAPI {

    private SimpleTeensyAPI[] teensies;
    private int? teensyLength;
    private bool separateThread;

    /**
     * The only parameter is the names of the USB ports corresponding to each
     * Teensy you want this MultiTeensyOutput to control. Since this constructor
     * does not specify a teensyLength, you will not be able to use the setPixel
     * that takes only a single LED index.
     */
    public MultiTeensyAPI(string[] portNames, bool separateThread) {
      this.teensies = new SimpleTeensyAPI[portNames.Length];
      for (int i = 0; i < portNames.Length; i++) {
        this.teensies[i] = new SimpleTeensyAPI(portNames[i], separateThread);
      }
      this.separateThread = separateThread;
    }

    /**
     * The first parameter is the names of the USB ports corresponding to each
     * Teensy you want this MultiAPI to control. The second parameter corresponds
     * to the number of LEDs that each Teensy addresses, and it is only used in
     * the setPixel method that just takes a single LED index (as opposed to the
     * setPixel method that takes both Teensy index and an LED index). To use
     * this setPixel method, Each Teensy must address the same number of LEDs. 
     */
    public MultiTeensyAPI(
      string[] portNames,
      int teensyLength,
      bool separateThread
    ) : this(portNames, separateThread) {
      this.teensyLength = teensyLength;
    }

    private bool active;
    public bool Active {
      get {
        lock (this.teensies) {
          return this.active;
        }
      }
      set {
        lock (this.teensies) {
          if (this.active == value) {
            return;
          }
          foreach (var teensy in this.teensies) {
            teensy.Active = value;
          }
          this.active = value;
        }
      }
    }

    public void OperatorUpdate() {
      foreach (var teensy in this.teensies) {
        teensy.OperatorUpdate();
      }
    }

    public void Flush() {
      foreach (var teensy in this.teensies) {
        teensy.Flush();
      }
    }

    public void SetPixel(int pixelIndex, int color) {
      int? currentTeensyLength = this.teensyLength;
      if (currentTeensyLength == null) {
        throw new Exception(String.Concat(
          "You can't use this method without ",
          "calling the two-parameter constructor first"
        ));
      }
      int teensyIndex = pixelIndex / currentTeensyLength.Value;
      int subPixelIndex = pixelIndex % currentTeensyLength.Value;
      this.SetPixel(teensyIndex, subPixelIndex, color);
    }

    public void SetPixel(int teensyIndex, int subPixelIndex, int color) {
      this.teensies[teensyIndex].SetPixel(subPixelIndex, color);
    }

  }

}