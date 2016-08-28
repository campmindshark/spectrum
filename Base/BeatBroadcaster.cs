using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.Base {

  public class BeatBroadcaster {

    private List<long> currentTaps = new List<long>();
    private long startingTime = -1;
    private int measureLength = -1;

    public double ProgressThroughMeasure {
      get {
        return ProgressThroughBeat(1.0);
      }
    }

    public double ProgressThroughBeat(double factor) {
      if (
        this.startingTime == -1 ||
        this.measureLength == -1 ||
        factor == 0.0
      ) {
        return 0.0;
      }
      long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      int distance = (int)(timestamp - this.startingTime);
      int beatLength = (int)(this.measureLength / factor);
      int progressThroughMeasure = distance % beatLength;
      return (double)progressThroughMeasure / beatLength;
    }


    public int MeasureLength {
      get {
        return this.measureLength;
      }
    }

    public void AddTap() {
      long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      if (
        this.currentTaps.Count > 0 &&
        timestamp - this.currentTaps.Last() > 2000
      ) {
        this.currentTaps = new List<long>();
      }
      this.currentTaps.Add(timestamp);
      if (this.currentTaps.Count >= 3) {
        this.UpdateBeat();
      }
    }

    public void Reset() {
      this.currentTaps = new List<long>();
      this.startingTime = -1;
      this.measureLength = -1;
    }

    private void UpdateBeat() {
      int[] measureLengths = new int[this.currentTaps.Count - 1];
      for (int i = 0; i < this.currentTaps.Count - 1; i++) {
        measureLengths[i] =
          (int)(this.currentTaps[i + 1] - this.currentTaps[i]);
      }
      this.measureLength = (int)(measureLengths.Average());
      this.startingTime = this.currentTaps.Last();
    }

  }

}
