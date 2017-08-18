using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.ComponentModel;
using System.Timers;

namespace Spectrum.Base {

  public class BeatBroadcaster : INotifyPropertyChanged {

    private static int tapTempoConclusionTime = 2000;

    private Configuration config;
    private List<long> currentTaps = new List<long>();
    private long startingTime = -1;
    private int measureLength = -1;
    private Timer tapTempoConclusionTimer = new Timer(tapTempoConclusionTime);

    public event PropertyChangedEventHandler PropertyChanged;

    public BeatBroadcaster(Configuration config) {
      this.config = config;
      this.tapTempoConclusionTimer.Elapsed += TapTempoConcluded;
    }

    public double ProgressThroughMeasure {
      get {
        return this.ProgressThroughBeat(1.0);
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
      this.tapTempoConclusionTimer.Stop();
      long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      if (
        this.currentTaps.Count > 0 &&
        timestamp - this.currentTaps.Last() > tapTempoConclusionTime
      ) {
        this.currentTaps = new List<long>();
      }
      this.currentTaps.Add(timestamp);
      this.TapTempoConcluded(null, null);
      this.tapTempoConclusionTimer.Start();
      if (this.currentTaps.Count >= 3) {
        this.UpdateBeat();
      }
    }

    private void TapTempoConcluded(object sender, ElapsedEventArgs e) {
      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs("TapCounterBrush")
      );
      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs("TapCounterText")
      );
    }

    public void Reset() {
      this.currentTaps = new List<long>();
      this.startingTime = -1;
      this.measureLength = -1;
      this.TapTempoConcluded(null, null);
      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs("BPMString")
      );
    }

    private void UpdateBeat() {
      int[] measureLengths = new int[this.currentTaps.Count - 1];
      for (int i = 0; i < this.currentTaps.Count - 1; i++) {
        measureLengths[i] =
          (int)(this.currentTaps[i + 1] - this.currentTaps[i]);
      }
      this.measureLength = (int)(measureLengths.Average());
      this.startingTime = this.currentTaps.Last();
      this.PropertyChanged?.Invoke(
        this,
        new PropertyChangedEventArgs("BPMString")
      );
    }

    private bool TapTempoConcluded() {
      if (this.currentTaps.Count == 0) {
        return true;
      }
      long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      return timestamp - this.currentTaps.Last() > tapTempoConclusionTime;
    }

    public Brush TapCounterBrush {
      get {
        if (this.TapTempoConcluded()) {
          return new SolidColorBrush(Colors.Black);
        }
        return new SolidColorBrush(Colors.ForestGreen);
      }
    }

    public string TapCounterText {
      get {
        if (this.TapTempoConcluded()) {
          return "Tap tempo";
        }
        return this.currentTaps.Count.ToString();
      }
    }

    public string BPMString {
      get {
        if (this.measureLength == -1) {
          return "[none]";
        }
        return (60000 / this.measureLength).ToString();
      }
    }

    public bool CurrentlyFlashedOff {
      get {
        return this.config.flashSpeed != 0.0 &&
          this.ProgressThroughBeat(this.config.flashSpeed) >= 0.5;
      }
    }

  }

}
