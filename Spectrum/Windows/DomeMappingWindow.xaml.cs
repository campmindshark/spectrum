using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Spectrum.Base;
using Spectrum.LEDs;
// Spectrum declares its own Color type in this namespace, which hides a plain
// `using Color = ...` alias, so use a distinct name for the WPF Color used by
// all the brushes/strokes in this window.
using WColor = System.Windows.Media.Color;

namespace Spectrum {

  /**
   * Interactive "Set Dome Mapping" calibration. The dome lights one controller
   * cable at a time (driven via config.domeCalibrationCableIndex, rendered by
   * LEDDomeMappingCalibrationVisualizer); the user clicks the sector + cable on
   * the diagram that physically lit. After all 10 cables are identified the
   * resulting permutation is saved to config.domeCableMapping, which
   * LEDDomeOutput uses to route every pixel to the correct physical endpoint.
   *
   * A controller cable and a dome endpoint are both identified by box*2 + half
   * (half 0 = ethernet A, 1 = B). picks[c] holds the endpoint the user reported
   * for controller cable c.
   */
  public partial class DomeMappingWindow : Window {

    private const double DomeScale = 700;
    private const double DomeOffset = 20;

    private readonly Configuration config;
    private readonly int[] picks = new int[LEDDomeOutput.NumCables];
    // Which controller cable we are currently lighting; equals NumCables once
    // every cable has been answered.
    private int currentStep = 0;
    // The window opens idle; the dome is only driven and endpoints are only
    // pickable once the user clicks "Start Dome Mapping".
    private bool started = false;

    // Per-endpoint diagram pieces, so we can restyle them as picks are recorded.
    private readonly List<Line>[] endpointLines =
      new List<Line>[LEDDomeOutput.NumCables];
    private readonly Button[] endpointLabels =
      new Button[LEDDomeOutput.NumCables];
    private readonly Point[] endpointCentroids = new Point[LEDDomeOutput.NumCables];
    private readonly WColor[] endpointColors = new WColor[LEDDomeOutput.NumCables];

    public DomeMappingWindow(Configuration config) {
      this.InitializeComponent();
      this.config = config;
      this.BuildDiagram();
      this.PopulateSwapCombos();
    }

    // Both swap dropdowns list every controller cable in order, so a combo's
    // SelectedIndex is the cable index it refers to.
    private void PopulateSwapCombos() {
      for (int cable = 0; cable < LEDDomeOutput.NumCables; cable++) {
        this.swapComboA.Items.Add(ControllerLabel(cable));
        this.swapComboB.Items.Add(ControllerLabel(cable));
      }
      this.swapComboA.SelectedIndex = 0;
      this.swapComboB.SelectedIndex = 1;
    }

    // half 0 -> "A", 1 -> "B".
    private static string HalfName(int half) {
      return half == 0 ? "A" : "B";
    }

    private static string ControllerLabel(int cable) {
      return "Box " + (cable / 2 + 1) + " · Cable " + HalfName(cable % 2);
    }

    private static string EndpointLabel(int endpoint) {
      return "S" + (endpoint / 2 + 1) + HalfName(endpoint % 2);
    }

    private void BuildDiagram() {
      for (int endpoint = 0; endpoint < LEDDomeOutput.NumCables; endpoint++) {
        int box = endpoint / 2;
        int half = endpoint % 2;
        WColor color = ColorForEndpoint(endpoint);
        this.endpointColors[endpoint] = color;
        var brush = new SolidColorBrush(color);
        var lines = new List<Line>();

        double sumX = 0;
        double sumY = 0;
        int pointCount = 0;
        foreach (int strutIndex in
            LEDDomeOutput.GetControllerCableStruts(box, half)) {
          Point p0 = Project(StrutLayoutFactory.GetProjectedPoint(strutIndex, 0));
          Point p1 = Project(StrutLayoutFactory.GetProjectedPoint(strutIndex, 1));
          var line = new Line() {
            X1 = p0.X,
            Y1 = p0.Y,
            X2 = p1.X,
            Y2 = p1.Y,
            Stroke = brush,
            StrokeThickness = 5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
          };
          this.canvas.Children.Add(line);
          lines.Add(line);
          sumX += p0.X + p1.X;
          sumY += p0.Y + p1.Y;
          pointCount += 2;
        }
        this.endpointLines[endpoint] = lines;
        this.endpointCentroids[endpoint] =
          new Point(sumX / pointCount, sumY / pointCount);
      }

      // Labels are clickable buttons on top of the lines, the pick targets for
      // each endpoint, added after all lines so they render above them.
      for (int endpoint = 0; endpoint < LEDDomeOutput.NumCables; endpoint++) {
        var label = new Button() {
          FontSize = 15,
          FontWeight = FontWeights.Bold,
          Padding = new Thickness(12, 7, 12, 7),
          Cursor = Cursors.Hand,
          Tag = endpoint,
        };
        label.Click += EndpointClicked;
        // Center on the endpoint centroid using the button's real rendered size.
        // SizeChanged fires once the control template is applied, so the layout
        // doesn't shift the first time the diagram is refreshed.
        label.SizeChanged += LabelSizeChanged;
        this.canvas.Children.Add(label);
        this.endpointLabels[endpoint] = label;
      }
    }

    private void LabelSizeChanged(object sender, SizeChangedEventArgs e) {
      var label = (Button)sender;
      Point centroid = this.endpointCentroids[(int)label.Tag];
      Canvas.SetLeft(label, centroid.X - e.NewSize.Width / 2);
      Canvas.SetTop(label, centroid.Y - e.NewSize.Height / 2);
    }

    private static Point Project(Tuple<double, double> normalized) {
      return new Point(
        normalized.Item1 * DomeScale + DomeOffset,
        normalized.Item2 * DomeScale + DomeOffset
      );
    }

    // A distinct color per endpoint: one hue per sector, with cable A brighter
    // than cable B so the two cables of a sector are distinguishable.
    private static WColor ColorForEndpoint(int endpoint) {
      int sector = endpoint / 2;
      int half = endpoint % 2;
      double hue = sector / 5.0 * 360.0;
      double value = half == 0 ? 1.0 : 0.6;
      return ColorFromHSV(hue, 0.85, value);
    }

    private static WColor ColorFromHSV(double hue, double saturation, double value) {
      int hi = (int)(Math.Floor(hue / 60)) % 6;
      double f = hue / 60 - Math.Floor(hue / 60);
      double v = value * 255;
      byte vb = (byte)v;
      byte p = (byte)(v * (1 - saturation));
      byte q = (byte)(v * (1 - f * saturation));
      byte t = (byte)(v * (1 - (1 - f) * saturation));
      switch (hi) {
        case 0: return WColor.FromRgb(vb, t, p);
        case 1: return WColor.FromRgb(q, vb, p);
        case 2: return WColor.FromRgb(p, vb, t);
        case 3: return WColor.FromRgb(p, q, vb);
        case 4: return WColor.FromRgb(t, p, vb);
        default: return WColor.FromRgb(vb, p, q);
      }
    }

    private void WindowLoaded(object sender, RoutedEventArgs e) {
      this.started = false;
      this.config.domeCalibrationActive = false;
      // If a valid mapping is already on file, load it for review/editing
      // (treated as fully recorded) instead of opening empty.
      if (this.TryLoadExistingMapping()) {
        this.currentStep = LEDDomeOutput.NumCables;
      } else {
        this.ResetPicks();
        this.currentStep = 0;
      }
      this.UpdateForStep();
    }

    private void ResetPicks() {
      for (int i = 0; i < this.picks.Length; i++) {
        this.picks[i] = -1;
      }
    }

    // Copies config.domeCableMapping into picks if it is a valid permutation of
    // 0..NumCables-1 (the same validity test LEDDomeOutput applies). Returns
    // false (leaving picks untouched) when there is no usable mapping on file.
    private bool TryLoadExistingMapping() {
      int[] mapping = this.config.domeCableMapping;
      if (mapping == null || mapping.Length != LEDDomeOutput.NumCables) {
        return false;
      }
      var seen = new bool[LEDDomeOutput.NumCables];
      foreach (int endpoint in mapping) {
        if (endpoint < 0 || endpoint >= LEDDomeOutput.NumCables || seen[endpoint]) {
          return false;
        }
        seen[endpoint] = true;
      }
      Array.Copy(mapping, this.picks, LEDDomeOutput.NumCables);
      return true;
    }

    private void StartClicked(object sender, RoutedEventArgs e) {
      this.ResetPicks();
      this.currentStep = 0;
      this.started = true;
      this.config.domeCalibrationActive = true;
      this.UpdateForStep();
    }

    private void WindowClosed(object sender, EventArgs e) {
      this.config.domeCalibrationCableIndex = -1;
      this.config.domeCalibrationActive = false;
    }

    // Pushes the current step to the dome (which cable to light) and refreshes
    // all of the UI to match the recorded picks.
    private void UpdateForStep() {
      bool done = this.started && this.currentStep >= LEDDomeOutput.NumCables;
      this.config.domeCalibrationCableIndex =
        (this.started && !done) ? this.currentStep : -1;

      if (!this.started) {
        bool hasMapping = this.currentStep >= LEDDomeOutput.NumCables;
        this.currentCableLabel.Text =
          hasMapping ? "Existing mapping loaded" : "Not started";
        this.progressLabel.Text = hasMapping
          ? "Swap pairs below, or Start Dome Mapping to re-map from scratch."
          : "Click \"Start Dome Mapping\" to begin lighting cables.";
      } else if (done) {
        this.currentCableLabel.Text = "All cables identified";
        this.progressLabel.Text =
          "Review below, then Save Mapping (or Back to revise).";
      } else {
        this.currentCableLabel.Text = ControllerLabel(this.currentStep);
        this.progressLabel.Text =
          "Cable " + (this.currentStep + 1) + " of " + LEDDomeOutput.NumCables;
      }

      this.startButton.IsEnabled = !this.started;
      this.backButton.IsEnabled = this.started && this.currentStep > 0;
      this.skipButton.IsEnabled = this.started && !done;
      this.restartButton.IsEnabled = this.started;
      // Save applies to a complete mapping whether freshly calibrated or loaded
      // from file and tweaked with swaps.
      this.saveButton.IsEnabled = this.MappingComplete(out _);
      // Swapping operates on already-recorded picks, so it needs at least two
      // assigned cables.
      bool canSwap = this.currentStep >= 2;
      this.swapButton.IsEnabled = canSwap;
      this.swapComboA.IsEnabled = canSwap;
      this.swapComboB.IsEnabled = canSwap;

      this.RefreshRecorded();
      this.RefreshDiagram();
      this.statusLabel.Text = "";
    }

    private void RefreshRecorded() {
      var lines = new List<string>();
      for (int cable = 0; cable < LEDDomeOutput.NumCables; cable++) {
        string target;
        if (this.started && cable == this.currentStep) {
          target = "← lighting now";
        } else if (cable < this.currentStep) {
          target = "→ " +
            (this.picks[cable] < 0 ? "(skipped)" : EndpointLabel(this.picks[cable]));
        } else {
          target = "";
        }
        lines.Add(ControllerLabel(cable).PadRight(18) + target);
      }
      this.recordedLabel.Text = string.Join("\n", lines);
    }

    // Marks endpoints already chosen (dimmed + showing which controller maps to
    // them) so duplicates are obvious and progress is visible.
    private void RefreshDiagram() {
      var assignedTo = new int[LEDDomeOutput.NumCables];
      for (int i = 0; i < assignedTo.Length; i++) {
        assignedTo[i] = -1;
      }
      for (int cable = 0; cable < this.currentStep; cable++) {
        int endpoint = this.picks[cable];
        if (endpoint >= 0) {
          assignedTo[endpoint] = cable;
        }
      }

      for (int endpoint = 0; endpoint < LEDDomeOutput.NumCables; endpoint++) {
        bool assigned = assignedTo[endpoint] >= 0;
        WColor baseColor = this.endpointColors[endpoint];
        byte alpha = (byte)(assigned ? 0x55 : 0xFF);
        var brush = new SolidColorBrush(
          WColor.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
        foreach (Line line in this.endpointLines[endpoint]) {
          line.Stroke = brush;
        }
        Button label = this.endpointLabels[endpoint];
        label.Content = EndpointLabel(endpoint);
        label.Foreground = new SolidColorBrush(
          assigned ? WColor.FromRgb(0x1A, 0x7A, 0x1A) : Colors.Black);
        label.Opacity = assigned ? 0.7 : 1.0;
        // Endpoints are only pickable once mapping has started.
        label.IsEnabled = this.started;
      }
    }

    private void EndpointClicked(object sender, RoutedEventArgs e) {
      if (!this.started || this.currentStep >= LEDDomeOutput.NumCables) {
        return;
      }
      int endpoint = (int)((FrameworkElement)sender).Tag;
      this.picks[this.currentStep] = endpoint;
      this.currentStep++;
      this.UpdateForStep();
      e.Handled = true;
    }

    private void SkipClicked(object sender, RoutedEventArgs e) {
      if (this.currentStep >= LEDDomeOutput.NumCables) {
        return;
      }
      this.picks[this.currentStep] = -1;
      this.currentStep++;
      this.UpdateForStep();
    }

    private void BackClicked(object sender, RoutedEventArgs e) {
      if (this.currentStep == 0) {
        return;
      }
      this.currentStep--;
      this.picks[this.currentStep] = -1;
      this.UpdateForStep();
    }

    private void RestartClicked(object sender, RoutedEventArgs e) {
      this.ResetPicks();
      this.currentStep = 0;
      this.UpdateForStep();
    }

    // Exchanges the endpoints recorded for the two selected controller cables,
    // for fixing a specific pair the user knows is swapped without redoing the
    // whole calibration.
    private void SwapClicked(object sender, RoutedEventArgs e) {
      int a = this.swapComboA.SelectedIndex;
      int b = this.swapComboB.SelectedIndex;
      if (a < 0 || b < 0 || a == b) {
        this.ShowError("Pick two different cables to swap.");
        return;
      }
      if (a >= this.currentStep || b >= this.currentStep) {
        this.ShowError("Both cables must be assigned before they can be swapped.");
        return;
      }
      int tmp = this.picks[a];
      this.picks[a] = this.picks[b];
      this.picks[b] = tmp;
      this.UpdateForStep();
    }

    private void ShowError(string message) {
      this.statusLabel.Foreground =
        new SolidColorBrush(WColor.FromRgb(0xFF, 0x6B, 0x6B));
      this.statusLabel.Text = message;
    }

    // The mapping is complete when every controller cable has been assigned a
    // distinct endpoint (a full permutation). Skipped (-1) entries make it
    // incomplete.
    private bool MappingComplete(out string error) {
      var seen = new bool[LEDDomeOutput.NumCables];
      for (int cable = 0; cable < LEDDomeOutput.NumCables; cable++) {
        int endpoint = this.picks[cable];
        if (endpoint < 0) {
          error = "Every cable must be assigned before saving "
            + "(" + ControllerLabel(cable) + " is unset).";
          return false;
        }
        if (seen[endpoint]) {
          error = "Two cables map to " + EndpointLabel(endpoint)
            + " - each sector/cable can only be picked once.";
          return false;
        }
        seen[endpoint] = true;
      }
      error = null;
      return true;
    }

    private void SaveClicked(object sender, RoutedEventArgs e) {
      if (!this.MappingComplete(out string error)) {
        this.ShowError(error);
        return;
      }
      this.config.domeCableMapping = (int[])this.picks.Clone();
      this.statusLabel.Foreground =
        new SolidColorBrush(WColor.FromRgb(0x88, 0xFF, 0x88));
      this.statusLabel.Text = "Mapping saved.";
    }

    private void CloseClicked(object sender, RoutedEventArgs e) {
      this.Close();
    }

  }

}
