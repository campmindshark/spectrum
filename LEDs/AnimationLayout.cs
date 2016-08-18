using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.LEDs {

  public class Strut {

    private static Dictionary<Tuple<int, bool>, Strut> struts;

    public Strut FromIndex(int index) {
      var key = new Tuple<int, bool>(index, false);
      if (!struts.ContainsKey(key)) {
        struts[key] = new Strut(index, false);
      }
      return struts[key];
    }

    public Strut ReversedFromIndex(int index) {
      var key = new Tuple<int, bool>(index, true);
      if (!struts.ContainsKey(key)) {
        struts[key] = new Strut(index, true);
      }
      return struts[key];
    }

    private int index;
    private bool reversed;

    private Strut(int index, bool reversed) {
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

  }

  public class AnimationLayoutSegment {

    private HashSet<Strut> struts;

    public AnimationLayoutSegment(HashSet<Strut> struts) {
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

  }

  public class AnimationLayout {

    private AnimationLayoutSegment[] segments;

    public AnimationLayout(AnimationLayoutSegment[] segments) {
      this.segments = segments;
    }

  }

}
