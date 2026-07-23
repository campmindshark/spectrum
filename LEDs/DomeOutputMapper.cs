using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Spectrum.Base;

namespace Spectrum.LEDs {

  // Owns the installed cable/port permutations and publishes one immutable
  // logical-pixel projection. The hard-coded dome wiring remains the raw
  // geometry source; this service composes operator calibration over it.
  internal sealed class DomeOutputMapper {
    internal const int StrandsPerCable = 4;
    internal const int NumCables = 10;
    internal const int NumDomeBoxes = NumCables / 2;
    internal const int NumPortsPerBox = 8;

    private readonly int maxStripLength;
    private readonly Func<int> getStrutCount;
    private readonly Func<int, int> getLedCount;
    private readonly Func<int, int, Tuple<int, int>> getRawAddress;
    private MappingState state;

    public DomeOutputMapping Current =>
      Volatile.Read(ref this.state).OutputMapping;

    public DomeOutputMapper(
      int maxStripLength,
      Func<int> getStrutCount,
      Func<int, int> getLedCount,
      Func<int, int, Tuple<int, int>> getRawAddress
    ) {
      if (maxStripLength <= 0) {
        throw new ArgumentOutOfRangeException(nameof(maxStripLength));
      }
      this.maxStripLength = maxStripLength;
      this.getStrutCount = getStrutCount ??
        throw new ArgumentNullException(nameof(getStrutCount));
      this.getLedCount = getLedCount ??
        throw new ArgumentNullException(nameof(getLedCount));
      this.getRawAddress = getRawAddress ??
        throw new ArgumentNullException(nameof(getRawAddress));
      this.state = MappingState.Empty;
    }

    public void Apply(DomeOutputSettingsSnapshot settings) {
      if (settings == null) {
        throw new ArgumentNullException(nameof(settings));
      }

      int[] controllerForEndpoint = BuildControllerMapping(
        settings.CableMapping);
      int[,] portForPath = BuildPortMapping(settings.PortMappings);
      var boxes = new List<int>();
      var pixels = new List<int>();
      int strutCount = this.getStrutCount();
      for (int strut = 0; strut < strutCount; strut++) {
        for (int led = 0; led < this.getLedCount(strut); led++) {
          Tuple<int, int> address = this.MapAddress(
            this.getRawAddress(strut, led),
            controllerForEndpoint,
            portForPath);
          boxes.Add(address.Item1);
          pixels.Add(address.Item2);
        }
      }

      Volatile.Write(
        ref this.state,
        new MappingState(
          controllerForEndpoint,
          portForPath,
          new DomeOutputMapping(boxes.ToArray(), pixels.ToArray())));
    }

    public Tuple<int, int> Map(int strutIndex, int ledIndex) {
      MappingState snapshot = Volatile.Read(ref this.state);
      return this.MapAddress(
        this.getRawAddress(strutIndex, ledIndex),
        snapshot.ControllerForEndpoint,
        snapshot.PortForPath);
    }

    public static bool IsValidPortMapping(int[]? mapping) =>
      IsValidPermutation(mapping, NumPortsPerBox);

    private static bool IsValidPermutation(
      IReadOnlyList<int>? mapping, int count
    ) {
      if (mapping == null || mapping.Count != count) {
        return false;
      }
      var seen = new bool[count];
      foreach (int value in mapping) {
        if (value < 0 || value >= count || seen[value]) {
          return false;
        }
        seen[value] = true;
      }
      return true;
    }

    private static int[] BuildControllerMapping(
      IReadOnlyList<int> cableMapping
    ) {
      var controllerForEndpoint = new int[NumCables];
      bool valid = IsValidPermutation(cableMapping, NumCables);
      for (int controller = 0;
          controller < NumCables;
          controller++) {
        int endpoint = valid ? cableMapping[controller] : controller;
        controllerForEndpoint[endpoint] = controller;
      }
      return controllerForEndpoint;
    }

    private static int[,] BuildPortMapping(
      ImmutableArray<ImmutableArray<int>> configuredMappings
    ) {
      var portForPath = new int[NumDomeBoxes, NumPortsPerBox];
      bool hasPerBoxMappings = configuredMappings.Length == NumDomeBoxes;
      for (int box = 0; box < NumDomeBoxes; box++) {
        IReadOnlyList<int>? portMapping = hasPerBoxMappings
          ? configuredMappings[box]
          : null;
        bool valid = IsValidPermutation(
          portMapping, NumPortsPerBox);
        for (int port = 0;
            port < NumPortsPerBox;
            port++) {
          int path = valid && portMapping != null
            ? portMapping[port]
            : port;
          portForPath[box, path] = port;
        }
      }
      return portForPath;
    }

    private Tuple<int, int> MapAddress(
      Tuple<int, int> raw,
      int[] controllerForEndpoint,
      int[,] portForPath
    ) {
      int box = raw.Item1;
      int legacyPath = raw.Item2 / this.maxStripLength;
      int offsetWithinStrand =
        raw.Item2 - legacyPath * this.maxStripLength;
      int physicalPort = portForPath[box, legacyPath];
      int half = physicalPort / StrandsPerCable;
      int strandWithinCable = physicalPort % StrandsPerCable;
      int endpoint = box * 2 + half;
      int controller = controllerForEndpoint[endpoint];
      int newBox = controller / 2;
      int newHalf = controller % 2;
      int newStrandSlot =
        newHalf * StrandsPerCable + strandWithinCable;
      return Tuple.Create(
        newBox,
        newStrandSlot * this.maxStripLength + offsetWithinStrand);
    }

    private sealed class MappingState {
      public static MappingState Empty { get; } = new MappingState(
        Array.Empty<int>(),
        new int[0, 0],
        new DomeOutputMapping(Array.Empty<int>(), Array.Empty<int>()));

      public int[] ControllerForEndpoint { get; }
      public int[,] PortForPath { get; }
      public DomeOutputMapping OutputMapping { get; }

      public MappingState(
        int[] controllerForEndpoint,
        int[,] portForPath,
        DomeOutputMapping outputMapping
      ) {
        this.ControllerForEndpoint = controllerForEndpoint;
        this.PortForPath = portForPath;
        this.OutputMapping = outputMapping;
      }
    }
  }

}
