using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LEDome {

  /**
   * MultiAPI is a thin layer that holds multiple SimpleAPIs. The intent is to
   * expose a single API to multiple Teensies.
   */
  public class MultiAPI {

    private SimpleAPI[] teensies;
    private int? teensyLength;
    private bool[] needsFlushing;

    /**
     * The only parameter is the names of the USB ports corresponding to each
     * Teensy you want this MultiAPI to control. Since this constructor does not
     * specify a teensyLength, you will not be able to use the setPixel that
     * takes only a single LED index.
     */
    public MultiAPI(string[] portNames) {
      this.teensies = new SimpleAPI[portNames.Length];
      for (int i = 0; i < portNames.Length; i++) {
        this.teensies[i] = new SimpleAPI(portNames[i]);
      }
      this.needsFlushing = new bool[portNames.Length];
    }

    /**
     * The first parameter is the names of the USB ports corresponding to each
     * Teensy you want this MultiAPI to control. The second parameter corresponds
     * to the number of LEDs that each Teensy addresses, and it is only used in
     * the setPixel method that just takes a single LED index (as opposed to the
     * setPixel method that takes both Teensy index and an LED index). To use
     * this setPixel method, Each Teensy must address the same number of LEDs. 
     */
    public MultiAPI(
      string[] portNames,
      int teensyLength
    ) : this(portNames) {
      this.teensyLength = teensyLength;
    }

    public void Open() {
      foreach (SimpleAPI teensy in this.teensies) {
        teensy.Open();
      }
    }

    public void Close() {
      foreach (SimpleAPI teensy in this.teensies) {
        teensy.Close();
      }
    }

    public void Flush() {
      for (int i = 0; i < this.teensies.Length; i++) {
        if (!this.needsFlushing[i]) {
          continue;
        }
        this.teensies[i].Flush();
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
      this.needsFlushing[teensyIndex] = true;
    }

  }

}