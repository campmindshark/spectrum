using Spectrum.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.LEDs {

  public class Strut {

    private static Dictionary<Tuple<int, bool>, Strut> struts =
      new Dictionary<Tuple<int, bool>, Strut>();

    public static Strut FromIndex(LEDDomeOutput dome, int index) {
      var key = new Tuple<int, bool>(index, false);
      if (!struts.ContainsKey(key)) {
        struts[key] = new Strut(dome, index, false);
      }
      return struts[key];
    }

    public static Strut ReversedFromIndex(LEDDomeOutput dome, int index) {
      var key = new Tuple<int, bool>(index, true);
      if (!struts.ContainsKey(key)) {
        struts[key] = new Strut(dome, index, true);
      }
      return struts[key];
    }

    private LEDDomeOutput dome;
    private int index;
    private bool reversed;

    private Strut(LEDDomeOutput dome, int index, bool reversed) {
      this.dome = dome;
      this.index = index;
      this.reversed = reversed;
    }

    public int Index {
      get {
        return this.index;
      }
    }

    public bool Reversed {
      get {
        return this.reversed;
      }
    }

    public int Length {
      get {
        return LEDDomeOutput.GetNumLEDs(this.index);
      }
    }

    /**
     * Returning -1.0 means that the pixel in question is off
     */
    public double GetGradientPos(
      double percentageLit,
      double startLitRange,
      double endLitRange,
      int led
    ) {
      int ledIndex = this.Reversed ? this.Length - led : led;
      double step = (endLitRange - startLitRange)
        / (this.Length * percentageLit);
      double gradientPos = startLitRange + ledIndex * step;
      return gradientPos <= 1.0 ? gradientPos : -1.0;
    }

  }

  public class StrutLayoutSegment {

    private HashSet<Strut> struts;

    public StrutLayoutSegment(HashSet<Strut> struts) {
      this.struts = struts;
    }

    private double averageStrutLength = 0;
    public double AverageStrutLength {
      get {
        if (this.averageStrutLength == 0) {
          this.averageStrutLength = struts.Average(strut => strut.Length);
        }
        return this.averageStrutLength;
      }
    }

    public HashSet<Strut> GetStruts() {
      return this.struts;
    }

  }

  public class StrutLayout {

    private StrutLayoutSegment[] segments;
    private Dictionary<int, int> strutToSegment;

    public StrutLayout(StrutLayoutSegment[] segments) {
      this.segments = segments;
      this.strutToSegment = new Dictionary<int, int>();
      for (int i = 0; i < this.segments.Length; i++) {
        foreach (var strut in this.segments[i].GetStruts()) {
          this.strutToSegment[strut.Index] = i;
        }
      }
    }

    public StrutLayoutSegment GetSegment(int index) {
      return segments[index];
    }

    public int NumSegments {
      get {
        return this.segments.Length;
      }
    }

    public int SegmentIndexOfStrutIndex(int strutIndex) {
      return this.strutToSegment[strutIndex];
    }

  }

}