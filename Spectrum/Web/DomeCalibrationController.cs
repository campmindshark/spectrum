using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.LEDs;

namespace Spectrum.Web {

  /**
   * The server-side dome-mapping calibration flow for the web
   * (docs/web_architecture.md — the modal op the native DomeMappingWindow drives
   * in-process). It is the exact same state machine: light one controller cable
   * at a time via the shared DomeCalibrationState (rendered by
   * LEDDomeMappingCalibrationVisualizer, which takes over the dome while
   * Active is true), let the operator report which physical endpoint lit, and
   * on completion write the discovered permutation to config.domeCableMapping.
   *
   * This holds the transient picks/step state server-side (the dome mapping is a
   * compound int[] write, kept maintenance-only and off the field-level
   * last-write-wins path). It is a modal resource: every mutating call requires
   * the caller to hold the "domeCalibration" advisory lease, so two operators
   * can't drive the flow into each other. The domeCableMapping write goes
   * through the ControlGateway (it's persisted config with PropertyChanged
   * subscribers); the calibration selection is plain shared state the
   * visualizer polls, written directly.
   *
   * Cable and endpoint are both identified by box*2 + half (half 0 = ethernet A,
   * 1 = B), matching DomeMappingWindow and LEDDomeOutput.
   */
  public sealed class DomeCalibrationController {

    // A JSON-friendly snapshot of the flow, returned by every endpoint so the
    // client always re-renders from server truth.
    public sealed class CalibrationState {
      public bool active { get; set; }
      public int currentStep { get; set; }
      public int numCables { get; set; }
      // active and every cable answered — ready to review/save.
      public bool done { get; set; }
      // picks form a full permutation of 0..numCables-1 (saveable).
      public bool complete { get; set; }
      // Editing the previously-saved mapping (loaded for review) rather than
      // actively lighting cables — picks are shown/swappable/saveable but the
      // dome is not seized. Mutually exclusive with active.
      public bool reviewing { get; set; }
      // A valid saved mapping exists in config, so it can be loaded for review
      // (the client offers "Edit saved mapping" when idle).
      public bool hasSavedMapping { get; set; }
      // The endpoint recorded for each controller cable; -1 = unset/skipped.
      public int[] picks { get; set; }
      public IReadOnlyList<string> cableLabels { get; set; }
      public IReadOnlyList<string> endpointLabels { get; set; }
    }

    // The static, click-target geometry the client draws the dome diagram from —
    // the web port of the WPF DomeMappingWindow's canvas. Each endpoint's line
    // segments are the physical struts on that controller cable, projected the
    // same way the native window projects them (normalized 0..1 point × Scale +
    // Offset), plus a centroid to anchor the label and the per-endpoint color.
    // This never changes, so the client fetches it once.
    public sealed class DiagramGeometry {
      // The projection the native window uses (Canvas is ViewSize square).
      public double scale { get; set; }
      public double offset { get; set; }
      public double viewSize { get; set; }
      public IReadOnlyList<EndpointGeometry> endpoints { get; set; }
    }

    public sealed class EndpointGeometry {
      public int endpoint { get; set; }
      public string label { get; set; }
      // "#rrggbb", matching DomeMappingWindow.ColorForEndpoint.
      public string color { get; set; }
      // Centroid of the endpoint's segment points, where the label sits.
      public double cx { get; set; }
      public double cy { get; set; }
      // One [x1, y1, x2, y2] per strut, already projected into view space.
      public IReadOnlyList<double[]> segments { get; set; }
    }

    // Must match DomeMappingWindow's DomeScale/DomeOffset and the 740-square
    // Canvas, so the web diagram is laid out identically to the native one.
    private const double DomeScale = 700;
    private const double DomeOffset = 20;
    private const double ViewSize = 740;

    private readonly ControlGateway gateway;
    private readonly Configuration config;
    private readonly DomeCalibrationState calibration;
    private readonly int numCables;
    private readonly string[] cableLabels;
    private readonly string[] endpointLabels;

    private readonly object gate = new object();
    private readonly int[] picks;
    private int currentStep;
    private bool active;
    // Loaded the saved mapping for review/edit (dome not seized). Never true at
    // the same time as active.
    private bool reviewing;

    // The diagram geometry is derived from the hard-coded dome layout and never
    // changes; build it once on first request and hand out the same instance.
    private DiagramGeometry geometry;

    public DomeCalibrationController(
      ControlGateway gateway,
      Configuration config,
      DomeCalibrationState calibration,
      int numCables
    ) {
      this.gateway = gateway;
      this.config = config;
      this.calibration = calibration;
      this.numCables = numCables;
      this.picks = new int[numCables];
      this.cableLabels = new string[numCables];
      this.endpointLabels = new string[numCables];
      for (int i = 0; i < numCables; i++) {
        this.cableLabels[i] = ControllerLabel(i);
        this.endpointLabels[i] = EndpointLabel(i);
      }
      lock (this.gate) {
        this.ResetPicksLocked();
      }
    }

    private static string HalfName(int half) => half == 0 ? "A" : "B";

    private static string ControllerLabel(int cable) =>
      "Box " + (cable / 2 + 1) + " · Cable " + HalfName(cable % 2);

    private static string EndpointLabel(int endpoint) =>
      "S" + (endpoint / 2 + 1) + HalfName(endpoint % 2);

    // The click-target geometry for the dome diagram (built once, then cached).
    // Mirrors DomeMappingWindow.BuildDiagram: for each endpoint (box*2 + half),
    // project every strut on that controller cable into the shared view space
    // and record the segment endpoints, their centroid, and the endpoint color.
    public DiagramGeometry Geometry() {
      lock (this.gate) {
        if (this.geometry != null) {
          return this.geometry;
        }
        var endpoints = new List<EndpointGeometry>(this.numCables);
        for (int endpoint = 0; endpoint < this.numCables; endpoint++) {
          int box = endpoint / 2;
          int half = endpoint % 2;
          var segments = new List<double[]>();
          double sumX = 0, sumY = 0;
          int pointCount = 0;
          foreach (int strutIndex in
              LEDDomeOutput.GetControllerCableStruts(box, half)) {
            Tuple<double, double> p0 =
              StrutLayoutFactory.GetProjectedPoint(strutIndex, 0);
            Tuple<double, double> p1 =
              StrutLayoutFactory.GetProjectedPoint(strutIndex, 1);
            double x0 = p0.Item1 * DomeScale + DomeOffset;
            double y0 = p0.Item2 * DomeScale + DomeOffset;
            double x1 = p1.Item1 * DomeScale + DomeOffset;
            double y1 = p1.Item2 * DomeScale + DomeOffset;
            segments.Add(new[] { x0, y0, x1, y1 });
            sumX += x0 + x1;
            sumY += y0 + y1;
            pointCount += 2;
          }
          endpoints.Add(new EndpointGeometry {
            endpoint = endpoint,
            label = this.endpointLabels[endpoint],
            color = ColorForEndpoint(endpoint),
            cx = pointCount > 0 ? sumX / pointCount : 0,
            cy = pointCount > 0 ? sumY / pointCount : 0,
            segments = segments,
          });
        }
        this.geometry = new DiagramGeometry {
          scale = DomeScale,
          offset = DomeOffset,
          viewSize = ViewSize,
          endpoints = endpoints,
        };
        return this.geometry;
      }
    }

    // One hue per sector, cable A brighter than B, matching the native window so
    // the two diagrams color endpoints identically.
    private static string ColorForEndpoint(int endpoint) {
      int sector = endpoint / 2;
      int half = endpoint % 2;
      double hue = sector / 5.0 * 360.0;
      double value = half == 0 ? 1.0 : 0.6;
      return ColorFromHSV(hue, 0.85, value);
    }

    private static string ColorFromHSV(double hue, double saturation, double value) {
      int hi = ((int)Math.Floor(hue / 60)) % 6;
      double f = hue / 60 - Math.Floor(hue / 60);
      double v = value * 255;
      byte vb = (byte)v;
      byte p = (byte)(v * (1 - saturation));
      byte q = (byte)(v * (1 - f * saturation));
      byte t = (byte)(v * (1 - (1 - f) * saturation));
      byte r, g, b;
      switch (hi) {
        case 0: r = vb; g = t; b = p; break;
        case 1: r = q; g = vb; b = p; break;
        case 2: r = p; g = vb; b = t; break;
        case 3: r = p; g = q; b = vb; break;
        case 4: r = t; g = p; b = vb; break;
        default: r = vb; g = p; b = q; break;
      }
      return $"#{r:X2}{g:X2}{b:X2}";
    }

    private void ResetPicksLocked() {
      for (int i = 0; i < this.picks.Length; i++) {
        this.picks[i] = -1;
      }
    }

    // Which controller cable the dome should light right now: the current step
    // while running, or -1 (all off) when idle or past the last cable.
    private int CableIndexLocked() =>
      (this.active && this.currentStep < this.numCables) ? this.currentStep : -1;

    // Begin (or restart) the flow: clear picks, light the first cable.
    public Task<CalibrationState> StartAsync() {
      lock (this.gate) {
        this.ResetPicksLocked();
        this.currentStep = 0;
        this.active = true;
        this.reviewing = false;
      }
      return this.DriveAndSnapshot();
    }

    // Load the saved mapping (config.domeCableMapping) for review/edit without
    // lighting the dome — the web equivalent of DomeMappingWindow opening onto an
    // existing mapping. All cables count as assigned so the mapping can be
    // swapped and re-saved. If no valid mapping is on file, stays idle.
    public Task<CalibrationState> LoadAsync() {
      lock (this.gate) {
        int[] mapping = this.config.domeCableMapping;
        if (IsValidMapping(mapping, this.numCables)) {
          Array.Copy(mapping, this.picks, this.numCables);
          this.currentStep = this.numCables;
          this.active = false;
          this.reviewing = true;
        } else {
          this.ResetPicksLocked();
          this.currentStep = 0;
          this.active = false;
          this.reviewing = false;
        }
      }
      return this.DriveAndSnapshot();
    }

    // Whether mapping is a full permutation of 0..count-1 (the validity test
    // LEDDomeOutput applies before using a mapping).
    private static bool IsValidMapping(int[] mapping, int count) {
      if (mapping == null || mapping.Length != count) {
        return false;
      }
      var seen = new bool[count];
      foreach (int endpoint in mapping) {
        if (endpoint < 0 || endpoint >= count || seen[endpoint]) {
          return false;
        }
        seen[endpoint] = true;
      }
      return true;
    }

    // Record the endpoint the operator saw light for the current cable, then
    // advance to the next cable.
    public Task<CalibrationState> PickAsync(int endpoint) {
      if (endpoint < 0 || endpoint >= this.numCables) {
        throw new ArgumentException(
          "endpoint out of range 0.." + (this.numCables - 1));
      }
      lock (this.gate) {
        if (this.active && this.currentStep < this.numCables) {
          // The mapping is a bijection: each endpoint backs exactly one cable,
          // so reject an endpoint already assigned to an earlier cable rather
          // than silently creating a many-to-one mapping. (The UI also hides
          // taken endpoints; this guards direct/stale callers.)
          for (int cable = 0; cable < this.currentStep; cable++) {
            if (this.picks[cable] == endpoint) {
              throw new ArgumentException(
                "endpoint already assigned to another cable");
            }
          }
          this.picks[this.currentStep] = endpoint;
          this.currentStep++;
        }
      }
      return this.DriveAndSnapshot();
    }

    // Advance past the current cable without recording an endpoint.
    public Task<CalibrationState> SkipAsync() {
      lock (this.gate) {
        if (this.active && this.currentStep < this.numCables) {
          this.picks[this.currentStep] = -1;
          this.currentStep++;
        }
      }
      return this.DriveAndSnapshot();
    }

    // Step back one cable, clearing its recorded pick so it can be redone.
    public Task<CalibrationState> BackAsync() {
      lock (this.gate) {
        if (this.active && this.currentStep > 0) {
          this.currentStep--;
          this.picks[this.currentStep] = -1;
        }
      }
      return this.DriveAndSnapshot();
    }

    // Clear all picks and return to the first cable without leaving the flow.
    public Task<CalibrationState> RestartAsync() {
      lock (this.gate) {
        this.ResetPicksLocked();
        this.currentStep = 0;
        this.active = true;
        this.reviewing = false;
      }
      return this.DriveAndSnapshot();
    }

    // Exchange the endpoints recorded for two controller cables — for fixing a
    // pair the operator knows is swapped without redoing the whole flow (the
    // native DomeMappingWindow's Swap control). Both cables must already be
    // assigned (index < currentStep) and distinct.
    public Task<CalibrationState> SwapAsync(int a, int b) {
      lock (this.gate) {
        if (a < 0 || b < 0 || a >= this.numCables || b >= this.numCables
            || a == b) {
          throw new ArgumentException("pick two different cables to swap");
        }
        if (a >= this.currentStep || b >= this.currentStep) {
          throw new ArgumentException(
            "both cables must be assigned before they can be swapped");
        }
        int tmp = this.picks[a];
        this.picks[a] = this.picks[b];
        this.picks[b] = tmp;
      }
      return this.DriveAndSnapshot();
    }

    // Persist the discovered permutation to config.domeCableMapping. Fails
    // (without writing) unless every cable maps to a distinct endpoint.
    public async Task<(bool ok, string error, CalibrationState state)> SaveAsync() {
      int[] mapping;
      lock (this.gate) {
        if (!this.IsCompleteLocked(out string error)) {
          return (false, error, this.SnapshotLocked());
        }
        mapping = (int[])this.picks.Clone();
      }
      await this.gateway.InvokeAsync(() => this.config.domeCableMapping = mapping);
      return (true, null, this.Snapshot());
    }

    // Leave the flow: blank the dome and hand control back to the normal
    // visualizers. The advisory lease is released separately by the client.
    public Task<CalibrationState> CancelAsync() {
      lock (this.gate) {
        this.active = false;
        this.reviewing = false;
      }
      return this.DriveAndSnapshot();
    }

    public CalibrationState State() => this.Snapshot();

    // Push the current active/cable-index selection to the shared calibration
    // state, then return a fresh snapshot. The fields are volatile and the only
    // reader (the calibration visualizer) polls, so no thread marshalling is
    // needed — unlike persisted config writes, which still go via the gateway.
    private Task<CalibrationState> DriveAndSnapshot() {
      lock (this.gate) {
        this.calibration.Active = this.active;
        this.calibration.CableIndex = this.CableIndexLocked();
      }
      return Task.FromResult(this.Snapshot());
    }

    private bool IsCompleteLocked(out string error) {
      var seen = new bool[this.numCables];
      for (int cable = 0; cable < this.numCables; cable++) {
        int endpoint = this.picks[cable];
        if (endpoint < 0) {
          error = "every cable must be assigned before saving ("
            + this.cableLabels[cable] + " is unset)";
          return false;
        }
        if (seen[endpoint]) {
          error = "two cables map to " + this.endpointLabels[endpoint]
            + " — each endpoint can only be picked once";
          return false;
        }
        seen[endpoint] = true;
      }
      error = null;
      return true;
    }

    private CalibrationState Snapshot() {
      lock (this.gate) {
        return this.SnapshotLocked();
      }
    }

    private CalibrationState SnapshotLocked() => new CalibrationState {
      active = this.active,
      currentStep = this.currentStep,
      numCables = this.numCables,
      done = this.active && this.currentStep >= this.numCables,
      complete = this.IsCompleteLocked(out _),
      reviewing = this.reviewing,
      hasSavedMapping = IsValidMapping(this.config.domeCableMapping, this.numCables),
      picks = (int[])this.picks.Clone(),
      cableLabels = this.cableLabels,
      endpointLabels = this.endpointLabels,
    };
  }
}
