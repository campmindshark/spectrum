using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.LEDs;

namespace Spectrum.Web {

  // Authoritative server-side draft for the two-stage dome calibration. Every
  // action is an operator intent; clients render returned snapshots and never
  // own the only copy of a confirmation or candidate.
  public sealed class DomeCalibrationController {
    private const string IdleStage = "idle";
    private const string CableStage = "cables";
    private const string StripStage = "strips";
    private const string ReviewStage = "review";

    public sealed class CalibrationState {
      public string stage { get; set; } = "";
      public bool active { get; set; }
      public int currentStep { get; set; }
      public int numCables { get; set; }
      public bool cablesComplete { get; set; }
      public bool complete { get; set; }
      public bool saveable { get; set; }
      public bool hasSavedMapping { get; set; }
      public int currentCandidate { get; set; }
      public int[] picks { get; set; } = Array.Empty<int>();
      public bool[] cableConfirmed { get; set; } = Array.Empty<bool>();
      public string[] cableLabels { get; set; } = Array.Empty<string>();
      public string[] endpointLabels { get; set; } = Array.Empty<string>();
      public int selectedBox { get; set; }
      public int[] stripSteps { get; set; } = Array.Empty<int>();
      public int[][] stripMappings { get; set; } = Array.Empty<int[]>();
      public bool[][] stripConfirmed { get; set; } = Array.Empty<bool[]>();
      public bool[] copiedFromBoxOne { get; set; } = Array.Empty<bool>();
      public string[] boxStatuses { get; set; } = Array.Empty<string>();
      public bool canApplyBoxOne { get; set; }
      public int[] savedCableMapping { get; set; } = Array.Empty<int>();
      public int[][] savedPortMappings { get; set; } = Array.Empty<int[]>();
      public int rawControllerCable { get; set; }
      public int rawControllerBox { get; set; }
      public int rawControllerPort { get; set; }
      public int simulatorEndpoint { get; set; }
      public int simulatorBox { get; set; }
      public int simulatorPath { get; set; }
      public string cableReadout { get; set; } = "";
      public string stripReadout { get; set; } = "";
    }

    private readonly ApplicationStateDispatcher gateway;
    private readonly Configuration config;
    private readonly ConfigurationEditor editor;
    private readonly DomeCalibrationState calibration;
    private readonly int numCables;
    private readonly string[] cableLabels;
    private readonly string[] endpointLabels;
    private readonly object gate = new object();

    private string stage = IdleStage;
    private readonly int[] cableDraft;
    private readonly bool[] cableConfirmed;
    private int cableStep;
    private int currentCandidate = -1;
    private int[] savedCableMapping = Array.Empty<int>();

    private readonly int[][] stripDraft;
    private readonly bool[][] stripConfirmed;
    private readonly int[] stripSteps;
    private readonly bool[] copiedFromBoxOne;
    private int[][] savedPortMappings = Array.Empty<int[]>();
    private int selectedBox;

    public DomeCalibrationController(
      ApplicationStateDispatcher gateway,
      Configuration config,
      DomeCalibrationState calibration,
      int numCables
    ) {
      if (numCables != LEDDomeOutput.NumCables) {
        throw new ArgumentException(
          "dome calibration requires " + LEDDomeOutput.NumCables +
          " controller cables");
      }
      this.gateway = gateway;
      this.config = config;
      this.editor = config as ConfigurationEditor ??
        throw new ArgumentException(
          "Calibration configuration must support collection edits.",
          nameof(config));
      this.calibration = calibration;
      this.numCables = numCables;
      this.cableDraft = new int[numCables];
      this.cableConfirmed = new bool[numCables];
      this.cableLabels = Enumerable.Range(0, numCables)
        .Select(ControllerLabel).ToArray();
      this.endpointLabels = Enumerable.Range(0, numCables)
        .Select(EndpointLabel).ToArray();

      this.stripDraft = NewIntMatrix(
        LEDDomeOutput.NumDomeBoxes, LEDDomeOutput.NumPortsPerBox);
      this.stripConfirmed = NewBoolMatrix(
        LEDDomeOutput.NumDomeBoxes, LEDDomeOutput.NumPortsPerBox);
      this.stripSteps = new int[LEDDomeOutput.NumDomeBoxes];
      this.copiedFromBoxOne = new bool[LEDDomeOutput.NumDomeBoxes];

      lock (this.gate) {
        this.LoadSavedGuessesLocked();
        this.ResetDraftLocked();
        this.calibration.Deactivate();
      }
    }

    private static int[][] NewIntMatrix(int rows, int columns) =>
      Enumerable.Range(0, rows).Select(_ => new int[columns]).ToArray();

    private static bool[][] NewBoolMatrix(int rows, int columns) =>
      Enumerable.Range(0, rows).Select(_ => new bool[columns]).ToArray();

    private static string HalfName(int half) => half == 0 ? "A" : "B";

    private static string ControllerLabel(int cable) =>
      "Box " + (cable / 2 + 1) + " Cable " + HalfName(cable % 2);

    private static string ControllerShortLabel(int cable) =>
      "B" + (cable / 2 + 1) + HalfName(cable % 2);

    private static string EndpointLabel(int endpoint) =>
      (endpoint / 2 + 1) + HalfName(endpoint % 2);

    public Task<CalibrationState> StartAsync() => this.UpdateLocked(() => {
      this.LoadSavedGuessesLocked();
      this.ResetDraftLocked();
      this.stage = CableStage;
      this.InitializeCableCandidateLocked();
    });

    public Task<CalibrationState> NavigateAsync(int direction) =>
      this.UpdateLocked(() => {
        if (direction != -1 && direction != 1) {
          throw new ArgumentException("direction must be -1 or 1");
        }
        if (this.stage == CableStage && this.cableStep < this.numCables) {
          this.currentCandidate = CycleCandidate(
            this.currentCandidate,
            this.cableConfirmed,
            this.cableDraft,
            direction);
          return;
        }
        if (this.stage == StripStage &&
            this.stripSteps[this.selectedBox] < LEDDomeOutput.NumPortsPerBox) {
          this.currentCandidate = CycleCandidate(
            this.currentCandidate,
            this.stripConfirmed[this.selectedBox],
            this.stripDraft[this.selectedBox],
            direction);
          return;
        }
        throw new ArgumentException("there is no active candidate to navigate");
      });

    public Task<CalibrationState> ConfirmAsync() => this.UpdateLocked(() => {
      if (this.stage == CableStage) {
        if (this.cableStep >= this.numCables) {
          this.BeginStripStageLocked();
          return;
        }
        this.ConfirmCableLocked();
        return;
      }
      if (this.stage != StripStage) {
        throw new ArgumentException("there is no active match to confirm");
      }
      if (this.stripSteps[this.selectedBox] >=
          LEDDomeOutput.NumPortsPerBox) {
        int next = this.NextUnfinishedBoxLocked(this.selectedBox + 1);
        if (next < 0) {
          this.stage = ReviewStage;
          this.currentCandidate = -1;
        } else {
          this.SelectBoxLocked(next);
        }
        return;
      }
      this.ConfirmStripLocked();
    });

    public Task<CalibrationState> BackAsync() => this.UpdateLocked(() => {
      if (this.stage == CableStage) {
        if (this.cableStep <= 0) {
          throw new ArgumentException("already at the first controller cable");
        }
        this.cableStep--;
        this.cableConfirmed[this.cableStep] = false;
        this.InitializeCableCandidateLocked();
        return;
      }
      if (this.stage == ReviewStage) {
        this.stage = StripStage;
      }
      if (this.stage != StripStage || this.stripSteps[this.selectedBox] <= 0) {
        throw new ArgumentException("already at the first port for this box");
      }
      this.copiedFromBoxOne[this.selectedBox] = false;
      this.stripSteps[this.selectedBox]--;
      this.stripConfirmed[this.selectedBox][
        this.stripSteps[this.selectedBox]] = false;
      this.InitializeStripCandidateLocked();
    });

    public Task<CalibrationState> SelectBoxAsync(int box) =>
      this.UpdateLocked(() => {
        if (this.stage != StripStage && this.stage != ReviewStage) {
          throw new ArgumentException(
            "a dome box can only be selected during strip calibration");
        }
        ValidateBox(box);
        this.SelectBoxLocked(box);
      });

    public Task<CalibrationState> ApplyBoxOneAsync() =>
      this.UpdateLocked(() => {
        if (!this.CanApplyBoxOneLocked()) {
          throw new ArgumentException(
            "Box 1 must be matched and Boxes 2-5 must have no progress");
        }
        for (int box = 1; box < LEDDomeOutput.NumDomeBoxes; box++) {
          Array.Copy(
            this.stripDraft[0], this.stripDraft[box],
            LEDDomeOutput.NumPortsPerBox);
          Array.Fill(this.stripConfirmed[box], true);
          this.stripSteps[box] = LEDDomeOutput.NumPortsPerBox;
          this.copiedFromBoxOne[box] = true;
        }
        this.stage = ReviewStage;
        this.currentCandidate = -1;
      });

    public Task<CalibrationState> RecalibrateBoxAsync(int box) =>
      this.UpdateLocked(() => {
        if (this.stage != StripStage && this.stage != ReviewStage) {
          throw new ArgumentException(
            "a dome box can only be recalibrated after cable calibration");
        }
        ValidateBox(box);
        Array.Clear(
          this.stripConfirmed[box], 0, this.stripConfirmed[box].Length);
        this.stripSteps[box] = 0;
        this.copiedFromBoxOne[box] = false;
        this.stage = StripStage;
        this.selectedBox = box;
        this.InitializeStripCandidateLocked();
      });

    public Task<(bool ok, string? error, CalibrationState state)> SaveAsync() =>
      this.gateway.InvokeAsync(() => {
        lock (this.gate) {
          if (!this.IsSaveableLocked(out string? error)) {
            return (false, error, this.SnapshotLocked());
          }
          int[] cables = (int[])this.cableDraft.Clone();
          var mappings = CloneMatrix(this.stripDraft)
            .Select(ports => new DomePortMapping(ports)).ToArray();
          // One dispatcher action makes the cable permutation and all five
          // strip permutations visible as one committed calibration.
          this.editor.ReplaceDomeCableMapping(cables);
          this.editor.ReplaceDomePortMappings(mappings);
          this.LoadSavedGuessesLocked();
          this.ResetDraftLocked();
          this.stage = IdleStage;
          this.calibration.Deactivate();
          return (true, (string?)null, this.SnapshotLocked());
        }
      });

    public Task<CalibrationState> CancelAsync() => this.UpdateLocked(() => {
      this.LoadSavedGuessesLocked();
      this.ResetDraftLocked();
      this.stage = IdleStage;
    });

    public CalibrationState State() {
      lock (this.gate) {
        return this.SnapshotLocked();
      }
    }

    public Task<CalibrationState> StateAsync() =>
      this.gateway.InvokeAsync(this.State);

    private Task<CalibrationState> UpdateLocked(Action mutation) =>
      this.gateway.InvokeAsync(() => {
        lock (this.gate) {
          mutation();
          this.DriveSelectionLocked();
          return this.SnapshotLocked();
        }
      });

    private void ConfirmCableLocked() {
      if (!CandidateAvailable(
            this.currentCandidate,
            this.cableConfirmed,
            this.cableDraft)) {
        throw new ArgumentException(
          "the simulator endpoint is already assigned");
      }
      this.cableDraft[this.cableStep] = this.currentCandidate;
      this.cableConfirmed[this.cableStep] = true;
      this.cableStep++;
      if (this.cableStep < this.numCables) {
        this.InitializeCableCandidateLocked();
      } else {
        this.currentCandidate = -1;
      }
    }

    private void ConfirmStripLocked() {
      int box = this.selectedBox;
      if (!CandidateAvailable(
            this.currentCandidate,
            this.stripConfirmed[box],
            this.stripDraft[box])) {
        throw new ArgumentException(
          "the simulator strip path is already assigned in this box");
      }
      int port = this.stripSteps[box];
      this.stripDraft[box][port] = this.currentCandidate;
      this.stripConfirmed[box][port] = true;
      this.stripSteps[box]++;
      this.copiedFromBoxOne[box] = false;
      if (this.AllBoxesCompleteLocked()) {
        this.stage = ReviewStage;
        this.currentCandidate = -1;
      } else if (this.stripSteps[box] < LEDDomeOutput.NumPortsPerBox) {
        this.InitializeStripCandidateLocked();
      } else {
        this.currentCandidate = -1;
      }
    }

    private void BeginStripStageLocked() {
      if (!this.CablesCompleteLocked()) {
        throw new ArgumentException(
          "all controller cables must be matched before strip calibration");
      }
      this.stage = StripStage;
      int unfinished = this.NextUnfinishedBoxLocked(0);
      this.SelectBoxLocked(unfinished < 0 ? 0 : unfinished);
    }

    private void SelectBoxLocked(int box) {
      this.selectedBox = box;
      if (this.stage == ReviewStage) {
        this.currentCandidate = -1;
      } else if (this.stripSteps[box] < LEDDomeOutput.NumPortsPerBox) {
        this.InitializeStripCandidateLocked();
      } else {
        this.currentCandidate = -1;
      }
    }

    private void InitializeCableCandidateLocked() {
      this.currentCandidate = InitialCandidate(
        this.cableDraft[this.cableStep],
        this.cableConfirmed,
        this.cableDraft);
    }

    private void InitializeStripCandidateLocked() {
      int box = this.selectedBox;
      int port = this.stripSteps[box];
      this.currentCandidate = InitialCandidate(
        this.stripDraft[box][port],
        this.stripConfirmed[box],
        this.stripDraft[box]);
    }

    private static int InitialCandidate(
      int guess, bool[] confirmed, int[] values
    ) {
      if (guess >= 0 && guess < values.Length &&
          CandidateAvailable(guess, confirmed, values)) {
        return guess;
      }
      for (int value = 0; value < values.Length; value++) {
        if (CandidateAvailable(value, confirmed, values)) {
          return value;
        }
      }
      return -1;
    }

    private static int CycleCandidate(
      int current, bool[] confirmed, int[] values, int direction
    ) {
      for (int offset = 1; offset <= values.Length; offset++) {
        int candidate =
          (current + direction * offset + values.Length * 2) % values.Length;
        if (CandidateAvailable(candidate, confirmed, values)) {
          return candidate;
        }
      }
      return current;
    }

    private static bool CandidateAvailable(
      int candidate, bool[] confirmed, int[] values
    ) {
      if (candidate < 0 || candidate >= values.Length) {
        return false;
      }
      for (int i = 0; i < values.Length; i++) {
        if (confirmed[i] && values[i] == candidate) {
          return false;
        }
      }
      return true;
    }

    private void DriveSelectionLocked() {
      if (this.stage == IdleStage) {
        this.calibration.Deactivate();
        return;
      }
      if (this.stage == CableStage && this.cableStep < this.numCables) {
        this.calibration.ShowCable(this.cableStep, this.currentCandidate);
        return;
      }
      if (this.stage == StripStage &&
          this.stripSteps[this.selectedBox] < LEDDomeOutput.NumPortsPerBox) {
        int port = this.stripSteps[this.selectedBox];
        int endpoint = this.selectedBox * 2 + port / 4;
        int controllerCable = Array.IndexOf(this.cableDraft, endpoint);
        if (controllerCable < 0) {
          throw new InvalidOperationException(
            "draft cable mapping does not contain dome endpoint " + endpoint);
        }
        int rawPort = (controllerCable % 2) * 4 + port % 4;
        this.calibration.ShowPort(
          controllerCable,
          controllerCable / 2,
          rawPort,
          this.selectedBox,
          this.currentCandidate);
        return;
      }
      this.calibration.ShowBlank();
    }

    private void LoadSavedGuessesLocked() {
      int[] configuredCables = this.config.domeCableMapping.ToArray();
      this.savedCableMapping = IsPermutation(
        configuredCables, this.numCables)
          ? (int[])configuredCables.Clone()
          : Identity(this.numCables);

      var configuredPorts = this.config.domePortMappings;
      bool validPerBox = configuredPorts.Length ==
        LEDDomeOutput.NumDomeBoxes && configuredPorts.All(mapping =>
          IsPermutation(mapping, LEDDomeOutput.NumPortsPerBox));
      if (validPerBox) {
        this.savedPortMappings = configuredPorts
          .Select(mapping => mapping.ToArray()).ToArray();
        return;
      }
      int[] source = Identity(LEDDomeOutput.NumPortsPerBox);
      this.savedPortMappings = Enumerable.Range(
        0, LEDDomeOutput.NumDomeBoxes)
        .Select(_ => (int[])source.Clone()).ToArray();
    }

    private void ResetDraftLocked() {
      Array.Copy(
        this.savedCableMapping, this.cableDraft, this.numCables);
      Array.Clear(this.cableConfirmed, 0, this.cableConfirmed.Length);
      this.cableStep = 0;
      this.currentCandidate = -1;
      this.selectedBox = 0;
      for (int box = 0; box < LEDDomeOutput.NumDomeBoxes; box++) {
        Array.Copy(
          this.savedPortMappings[box], this.stripDraft[box],
          LEDDomeOutput.NumPortsPerBox);
        Array.Clear(
          this.stripConfirmed[box], 0, this.stripConfirmed[box].Length);
        this.stripSteps[box] = 0;
        this.copiedFromBoxOne[box] = false;
      }
    }

    private static bool IsPermutation(
      IReadOnlyList<int> values, int count
    ) {
      if (values == null || values.Count != count) {
        return false;
      }
      var seen = new bool[count];
      foreach (int value in values) {
        if (value < 0 || value >= count || seen[value]) {
          return false;
        }
        seen[value] = true;
      }
      return true;
    }

    private static int[] Identity(int count) =>
      Enumerable.Range(0, count).ToArray();

    private bool CablesCompleteLocked() =>
      this.cableConfirmed.All(value => value) &&
      IsPermutation(this.cableDraft, this.numCables);

    private bool AllBoxesCompleteLocked() {
      for (int box = 0; box < LEDDomeOutput.NumDomeBoxes; box++) {
        if (!this.stripConfirmed[box].All(value => value) ||
            !IsPermutation(
              this.stripDraft[box], LEDDomeOutput.NumPortsPerBox)) {
          return false;
        }
      }
      return true;
    }

    private bool IsSaveableLocked(
      [NotNullWhen(false)] out string? error
    ) {
      if (!this.CablesCompleteLocked()) {
        error = "all ten controller cables must be confirmed before saving";
        return false;
      }
      for (int box = 0; box < LEDDomeOutput.NumDomeBoxes; box++) {
        if (!this.stripConfirmed[box].All(value => value)) {
          error = "Box " + (box + 1) +
            " must have all eight ports confirmed before saving";
          return false;
        }
        if (!IsPermutation(
              this.stripDraft[box], LEDDomeOutput.NumPortsPerBox)) {
          error = "Box " + (box + 1) +
            " must use every strip path exactly once";
          return false;
        }
      }
      error = null;
      return true;
    }

    private bool CanApplyBoxOneLocked() {
      if (this.stage != StripStage ||
          this.stripSteps[0] != LEDDomeOutput.NumPortsPerBox ||
          !IsPermutation(this.stripDraft[0], LEDDomeOutput.NumPortsPerBox)) {
        return false;
      }
      for (int box = 1; box < LEDDomeOutput.NumDomeBoxes; box++) {
        if (this.stripSteps[box] != 0 ||
            this.stripConfirmed[box].Any(value => value)) {
          return false;
        }
      }
      return true;
    }

    private int NextUnfinishedBoxLocked(int start) {
      for (int offset = 0; offset < LEDDomeOutput.NumDomeBoxes; offset++) {
        int box = (start + offset) % LEDDomeOutput.NumDomeBoxes;
        if (this.stripSteps[box] < LEDDomeOutput.NumPortsPerBox) {
          return box;
        }
      }
      return -1;
    }

    private static void ValidateBox(int box) {
      if (box < 0 || box >= LEDDomeOutput.NumDomeBoxes) {
        throw new ArgumentException(
          "box out of range 0.." + (LEDDomeOutput.NumDomeBoxes - 1));
      }
    }

    private CalibrationState SnapshotLocked() {
      bool saveable = this.IsSaveableLocked(out _);
      DomeCalibrationSelection output = this.calibration.Snapshot();
      return new CalibrationState {
        stage = this.stage,
        active = this.stage != IdleStage,
        currentStep = this.cableStep,
        numCables = this.numCables,
        cablesComplete = this.CablesCompleteLocked(),
        complete = saveable,
        saveable = saveable,
        hasSavedMapping = this.HasSavedMappingLocked(),
        currentCandidate = this.currentCandidate,
        picks = (int[])this.cableDraft.Clone(),
        cableConfirmed = (bool[])this.cableConfirmed.Clone(),
        cableLabels = (string[])this.cableLabels.Clone(),
        endpointLabels = (string[])this.endpointLabels.Clone(),
        selectedBox = this.selectedBox,
        stripSteps = (int[])this.stripSteps.Clone(),
        stripMappings = CloneMatrix(this.stripDraft),
        stripConfirmed = CloneMatrix(this.stripConfirmed),
        copiedFromBoxOne = (bool[])this.copiedFromBoxOne.Clone(),
        boxStatuses = this.BoxStatusesLocked(),
        canApplyBoxOne = this.CanApplyBoxOneLocked(),
        savedCableMapping = (int[])this.savedCableMapping.Clone(),
        savedPortMappings = CloneMatrix(this.savedPortMappings),
        rawControllerCable = output.RawControllerCable,
        rawControllerBox = output.RawControllerBox,
        rawControllerPort = output.RawControllerPort,
        simulatorEndpoint = output.SimulatorEndpoint,
        simulatorBox = output.SimulatorBox,
        simulatorPath = output.SimulatorPath,
        cableReadout = this.CableReadoutLocked(),
        stripReadout = this.StripReadoutLocked(),
      };
    }

    private bool HasSavedMappingLocked() {
      if (!IsPermutation(this.config.domeCableMapping, this.numCables)) {
        return false;
      }
      var mappings = this.config.domePortMappings;
      return mappings.Length == LEDDomeOutput.NumDomeBoxes &&
        mappings.All(mapping => IsPermutation(
          mapping, LEDDomeOutput.NumPortsPerBox));
    }

    private string[] BoxStatusesLocked() {
      var statuses = new string[LEDDomeOutput.NumDomeBoxes];
      for (int box = 0; box < statuses.Length; box++) {
        statuses[box] = this.copiedFromBoxOne[box]
          ? "Copied"
          : this.stripSteps[box] == 0
            ? "Not started"
            : this.stripSteps[box] == LEDDomeOutput.NumPortsPerBox
              ? "Matched"
              : this.stripSteps[box] + "/" +
                LEDDomeOutput.NumPortsPerBox;
      }
      return statuses;
    }

    private string CableReadoutLocked() {
      var text = new StringBuilder();
      for (int box = 0; box < this.numCables / 2; box++) {
        if (box > 0) {
          text.AppendLine();
        }
        for (int half = 0; half < 2; half++) {
          if (half > 0) {
            text.Append("    ");
          }
          int cable = box * 2 + half;
          text.Append(ControllerShortLabel(cable));
          text.Append(" -> ");
          text.Append(this.FormatCableValueLocked(cable));
        }
      }
      return text.ToString();
    }

    private string FormatCableValueLocked(int cable) {
      if (this.stage == CableStage && cable == this.cableStep &&
          this.currentCandidate >= 0) {
        return "[" + this.endpointLabels[this.currentCandidate] + "?]";
      }
      int endpoint = this.cableDraft[cable];
      string label = endpoint >= 0 && endpoint < this.endpointLabels.Length
        ? this.endpointLabels[endpoint] : "?";
      return this.cableConfirmed[cable] ? label : label + "~";
    }

    private string StripReadoutLocked() {
      var text = new StringBuilder();
      for (int box = 0; box < LEDDomeOutput.NumDomeBoxes; box++) {
        if (box > 0) {
          text.AppendLine();
        }
        for (int port = 0; port < LEDDomeOutput.NumPortsPerBox; port++) {
          if (port == 0) {
            text.Append("Box ");
            text.Append(box + 1);
            text.Append("  ");
          } else if (port == 4) {
            text.AppendLine();
            text.Append("       ");
          } else {
            text.Append("  ");
          }
          text.Append('P');
          text.Append(port + 1);
          text.Append("->");
          text.Append(this.FormatStripValueLocked(box, port));
        }
        if (this.copiedFromBoxOne[box]) {
          text.Append("  (copied from Box 1)");
        }
      }
      return text.ToString();
    }

    private string FormatStripValueLocked(int box, int port) {
      if (this.stage == StripStage && box == this.selectedBox &&
          port == this.stripSteps[box] && this.currentCandidate >= 0) {
        return "[S" + (this.currentCandidate + 1) + "?]";
      }
      string label = "S" + (this.stripDraft[box][port] + 1);
      return this.stripConfirmed[box][port] ? label : label + "~";
    }

    private static int[][] CloneMatrix(int[][] values) =>
      values.Select(row => (int[])row.Clone()).ToArray();

    private static bool[][] CloneMatrix(bool[][] values) =>
      values.Select(row => (bool[])row.Clone()).ToArray();
  }
}
