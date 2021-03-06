﻿using Spectrum.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.LEDs {

  public class Strut {

    private static readonly Dictionary<Tuple<int, bool>, Strut> struts =
      new Dictionary<Tuple<int, bool>, Strut>();

    public static Strut FromIndex(Configuration config, int index) {
      var key = new Tuple<int, bool>(index, false);
      if (!struts.ContainsKey(key)) {
        struts[key] = new Strut(config, index, false);
      }
      return struts[key];
    }

    public static Strut ReversedFromIndex(Configuration config, int index) {
      var key = new Tuple<int, bool>(index, true);
      if (!struts.ContainsKey(key)) {
        struts[key] = new Strut(config, index, true);
      }
      return struts[key];
    }

    private readonly Configuration config;
    private readonly int index;
    private readonly bool reversed;

    private Strut(
      Configuration config,
      int index,
      bool reversed
    ) {
      this.config = config;
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

    /**
     * Takes into account Configuration.domeSkipLEDs
     */
    public int Length {
      get {
        int actualLength = LEDDomeOutput.GetNumLEDs(this.index);
        return actualLength - this.config.domeSkipLEDs * 2;
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

    private readonly HashSet<Strut> struts;

    public StrutLayoutSegment(HashSet<Strut> struts) {
      this.struts = struts;
      if (struts.Count > 0) this.AverageStrutLength = struts.Average(strut => strut.Length);
      this.TotalLength = struts.Sum(strut => strut.Length);
    }

    public double AverageStrutLength { get; } = 0;
    public double TotalLength { get; } = 0;

    public HashSet<Strut> GetStruts() {
      return this.struts;
    }

  }

  public class StrutLayout {

    private readonly StrutLayoutSegment[] segments;
    private readonly Dictionary<int, int> strutToSegment;

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