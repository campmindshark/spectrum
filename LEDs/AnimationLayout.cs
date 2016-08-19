using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.LEDs {

  public class Strut {

    private static Dictionary<Tuple<int, bool>, Strut> struts =
      new Dictionary<Tuple<int, bool>, Strut>();

    public static Strut FromIndex(int index) {
      var key = new Tuple<int, bool>(index, false);
      if (!struts.ContainsKey(key)) {
        struts[key] = new Strut(index, false);
      }
      return struts[key];
    }

    public static Strut ReversedFromIndex(int index) {
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
    private Strut[] strutsArray;

    public AnimationLayoutSegment(HashSet<Strut> struts) {
      this.struts = struts;
      this.strutsArray = struts.ToArray();
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

    public Strut[] GetStruts() {
      return this.strutsArray;
    }

  }

  public class AnimationLayout {

    private AnimationLayoutSegment[] segments;
    private Dictionary<int, int> strutToSegment;

    public AnimationLayout(AnimationLayoutSegment[] segments) {
      this.segments = segments;
      this.strutToSegment = new Dictionary<int, int>();
      for (int i = 0; i < this.segments.Length; i++) {
        foreach (var strut in this.segments[i].GetStruts()) {
          this.strutToSegment[strut.Index] = i;
        }
      }
    }

    public AnimationLayoutSegment GetSegment(int index) {
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
