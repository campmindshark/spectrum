using System;
using System.Collections.Generic;
using Spectrum.Base;

namespace Spectrum.LEDs {

  // Owns the installed dome's immutable physical geometry: strut lengths,
  // control-box ordering, raw device addresses, and projected LED topology.
  // Configured cable and port permutations deliberately remain in
  // DomeOutputMapper, which composes them over this identity layout.
  internal static class DomeWiringLayout {
    private enum StrutType { Yellow, Red, Blue, Green, Purple, Orange };

    private static readonly StrutType[][] controlBoxStrutOrder =
      new StrutType[][] {
        new StrutType[] {
          StrutType.Green, StrutType.Blue,
          StrutType.Orange, StrutType.Orange,
          StrutType.Yellow,
        },
        new StrutType[] {
          StrutType.Orange, StrutType.Blue,
          StrutType.Purple, StrutType.Blue,
          StrutType.Red,
        },
        new StrutType[] {
          StrutType.Red, StrutType.Blue,
          StrutType.Green, StrutType.Green,
          StrutType.Blue,
        },
        new StrutType[] {
          StrutType.Green, StrutType.Blue,
          StrutType.Red, StrutType.Yellow,
          StrutType.Yellow,
        },
        new StrutType[] {
          StrutType.Green, StrutType.Purple,
          StrutType.Blue, StrutType.Red,
        },
        new StrutType[] {
          StrutType.Green, StrutType.Purple,
          StrutType.Purple, StrutType.Green,
          StrutType.Green,
        },
        new StrutType[] {
          StrutType.Orange, StrutType.Yellow,
          StrutType.Yellow, StrutType.Red,
          StrutType.Red,
        },
        new StrutType[] {
          StrutType.Blue, StrutType.Blue,
          StrutType.Blue, StrutType.Yellow,
        },
      };
    private static readonly Dictionary<StrutType, int> strutLengths =
      new Dictionary<StrutType, int> {
        [StrutType.Yellow] = 34,
        [StrutType.Red] = 40,
        [StrutType.Blue] = 40,
        [StrutType.Orange] = 40,
        [StrutType.Green] = 42,
        [StrutType.Purple] = 44,
      };

    // Maps a logical strut index to its raw control box and its sequential
    // strut index within that box's eight output paths.
    private static readonly Tuple<int, int>[] strutPositions =
      new Tuple<int, int>[] {
        new Tuple<int, int>(0, 22), new Tuple<int, int>(0, 23),
        new Tuple<int, int>(1, 36), new Tuple<int, int>(1, 21),
        new Tuple<int, int>(1, 22), new Tuple<int, int>(1, 23),
        new Tuple<int, int>(2, 36), new Tuple<int, int>(2, 21),
        new Tuple<int, int>(2, 22), new Tuple<int, int>(2, 23),
        new Tuple<int, int>(3, 36), new Tuple<int, int>(3, 21),
        new Tuple<int, int>(3, 22), new Tuple<int, int>(3, 23),
        new Tuple<int, int>(4, 36), new Tuple<int, int>(4, 21),
        new Tuple<int, int>(4, 22), new Tuple<int, int>(4, 23),
        new Tuple<int, int>(0, 36), new Tuple<int, int>(0, 21),
        new Tuple<int, int>(0, 5), new Tuple<int, int>(0, 19),
        new Tuple<int, int>(1, 30), new Tuple<int, int>(1, 29),
        new Tuple<int, int>(1, 5), new Tuple<int, int>(1, 19),
        new Tuple<int, int>(2, 30), new Tuple<int, int>(2, 29),
        new Tuple<int, int>(2, 5), new Tuple<int, int>(2, 19),
        new Tuple<int, int>(3, 30), new Tuple<int, int>(3, 29),
        new Tuple<int, int>(3, 5), new Tuple<int, int>(3, 19),
        new Tuple<int, int>(4, 30), new Tuple<int, int>(4, 29),
        new Tuple<int, int>(4, 5), new Tuple<int, int>(4, 19),
        new Tuple<int, int>(0, 30), new Tuple<int, int>(0, 29),
        new Tuple<int, int>(0, 11), new Tuple<int, int>(1, 1),
        new Tuple<int, int>(1, 25), new Tuple<int, int>(1, 11),
        new Tuple<int, int>(2, 1), new Tuple<int, int>(2, 25),
        new Tuple<int, int>(2, 11), new Tuple<int, int>(3, 1),
        new Tuple<int, int>(3, 25), new Tuple<int, int>(3, 11),
        new Tuple<int, int>(4, 1), new Tuple<int, int>(4, 25),
        new Tuple<int, int>(4, 11), new Tuple<int, int>(0, 1),
        new Tuple<int, int>(0, 25), new Tuple<int, int>(0, 13),
        new Tuple<int, int>(1, 27), new Tuple<int, int>(1, 13),
        new Tuple<int, int>(2, 27), new Tuple<int, int>(2, 13),
        new Tuple<int, int>(3, 27), new Tuple<int, int>(3, 13),
        new Tuple<int, int>(4, 27), new Tuple<int, int>(4, 13),
        new Tuple<int, int>(0, 27), new Tuple<int, int>(1, 9),
        new Tuple<int, int>(2, 9), new Tuple<int, int>(3, 9),
        new Tuple<int, int>(4, 9), new Tuple<int, int>(0, 9),
        new Tuple<int, int>(0, 15), new Tuple<int, int>(0, 16),
        new Tuple<int, int>(0, 17), new Tuple<int, int>(0, 18),
        new Tuple<int, int>(1, 37), new Tuple<int, int>(1, 33),
        new Tuple<int, int>(1, 35), new Tuple<int, int>(1, 20),
        new Tuple<int, int>(1, 15), new Tuple<int, int>(1, 16),
        new Tuple<int, int>(1, 17), new Tuple<int, int>(1, 18),
        new Tuple<int, int>(2, 37), new Tuple<int, int>(2, 33),
        new Tuple<int, int>(2, 35), new Tuple<int, int>(2, 20),
        new Tuple<int, int>(2, 15), new Tuple<int, int>(2, 16),
        new Tuple<int, int>(2, 17), new Tuple<int, int>(2, 18),
        new Tuple<int, int>(3, 37), new Tuple<int, int>(3, 33),
        new Tuple<int, int>(3, 35), new Tuple<int, int>(3, 20),
        new Tuple<int, int>(3, 15), new Tuple<int, int>(3, 16),
        new Tuple<int, int>(3, 17), new Tuple<int, int>(3, 18),
        new Tuple<int, int>(4, 37), new Tuple<int, int>(4, 33),
        new Tuple<int, int>(4, 35), new Tuple<int, int>(4, 20),
        new Tuple<int, int>(4, 15), new Tuple<int, int>(4, 16),
        new Tuple<int, int>(4, 17), new Tuple<int, int>(4, 18),
        new Tuple<int, int>(0, 37), new Tuple<int, int>(0, 33),
        new Tuple<int, int>(0, 35), new Tuple<int, int>(0, 20),
        new Tuple<int, int>(0, 24), new Tuple<int, int>(0, 6),
        new Tuple<int, int>(0, 10), new Tuple<int, int>(1, 31),
        new Tuple<int, int>(1, 32), new Tuple<int, int>(1, 34),
        new Tuple<int, int>(1, 0), new Tuple<int, int>(1, 24),
        new Tuple<int, int>(1, 6), new Tuple<int, int>(1, 10),
        new Tuple<int, int>(2, 31), new Tuple<int, int>(2, 32),
        new Tuple<int, int>(2, 34), new Tuple<int, int>(2, 0),
        new Tuple<int, int>(2, 24), new Tuple<int, int>(2, 6),
        new Tuple<int, int>(2, 10), new Tuple<int, int>(3, 31),
        new Tuple<int, int>(3, 32), new Tuple<int, int>(3, 34),
        new Tuple<int, int>(3, 0), new Tuple<int, int>(3, 24),
        new Tuple<int, int>(3, 6), new Tuple<int, int>(3, 10),
        new Tuple<int, int>(4, 31), new Tuple<int, int>(4, 32),
        new Tuple<int, int>(4, 34), new Tuple<int, int>(4, 0),
        new Tuple<int, int>(4, 24), new Tuple<int, int>(4, 6),
        new Tuple<int, int>(4, 10), new Tuple<int, int>(0, 31),
        new Tuple<int, int>(0, 32), new Tuple<int, int>(0, 34),
        new Tuple<int, int>(0, 0), new Tuple<int, int>(0, 7),
        new Tuple<int, int>(0, 12), new Tuple<int, int>(1, 2),
        new Tuple<int, int>(1, 28), new Tuple<int, int>(1, 26),
        new Tuple<int, int>(1, 7), new Tuple<int, int>(1, 12),
        new Tuple<int, int>(2, 2), new Tuple<int, int>(2, 28),
        new Tuple<int, int>(2, 26), new Tuple<int, int>(2, 7),
        new Tuple<int, int>(2, 12), new Tuple<int, int>(3, 2),
        new Tuple<int, int>(3, 28), new Tuple<int, int>(3, 26),
        new Tuple<int, int>(3, 7), new Tuple<int, int>(3, 12),
        new Tuple<int, int>(4, 2), new Tuple<int, int>(4, 28),
        new Tuple<int, int>(4, 26), new Tuple<int, int>(4, 7),
        new Tuple<int, int>(4, 12), new Tuple<int, int>(0, 2),
        new Tuple<int, int>(0, 28), new Tuple<int, int>(0, 26),
        new Tuple<int, int>(0, 14), new Tuple<int, int>(1, 3),
        new Tuple<int, int>(1, 8), new Tuple<int, int>(1, 14),
        new Tuple<int, int>(2, 3), new Tuple<int, int>(2, 8),
        new Tuple<int, int>(2, 14), new Tuple<int, int>(3, 3),
        new Tuple<int, int>(3, 8), new Tuple<int, int>(3, 14),
        new Tuple<int, int>(4, 3), new Tuple<int, int>(4, 8),
        new Tuple<int, int>(4, 14), new Tuple<int, int>(0, 3),
        new Tuple<int, int>(0, 8), new Tuple<int, int>(1, 4),
        new Tuple<int, int>(2, 4), new Tuple<int, int>(3, 4),
        new Tuple<int, int>(4, 4), new Tuple<int, int>(0, 4),
      };

    private static readonly int cableAStrutCount;
    private static readonly int domeStrutsPerBox;
    private static readonly Lazy<DomeTopology> topology =
      new Lazy<DomeTopology>(BuildTopology);

    public static int MaxStripLength { get; }
    public static int ControlBoxPixelCount =>
      MaxStripLength * DomeOutputMapper.NumPortsPerBox;
    public static int StrutCount => strutPositions.Length;

    static DomeWiringLayout() {
      int maximum = 0;
      foreach (StrutType[] struts in controlBoxStrutOrder) {
        int length = 0;
        foreach (StrutType type in struts) {
          length += strutLengths[type];
        }
        maximum = Math.Max(maximum, length);
      }
      MaxStripLength = maximum;

      int aCount = 0;
      for (int strand = 0;
          strand < DomeOutputMapper.StrandsPerCable;
          strand++) {
        aCount += controlBoxStrutOrder[strand].Length;
      }
      cableAStrutCount = aCount;

      int total = 0;
      foreach (StrutType[] strand in controlBoxStrutOrder) {
        total += strand.Length;
      }
      domeStrutsPerBox = total;
    }

    public static int GetLedCount(int strutIndex) {
      Tuple<int, int> strutPosition = strutPositions[strutIndex];
      int strutsLeft = strutPosition.Item2;
      int path = 0;
      while (controlBoxStrutOrder[path].Length <= strutsLeft) {
        strutsLeft -= controlBoxStrutOrder[path].Length;
        path++;
      }
      return strutLengths[controlBoxStrutOrder[path][strutsLeft]];
    }

    public static Tuple<int, int> GetRawAddress(
      int strutIndex, int ledIndex
    ) {
      int pixelIndex = ledIndex;
      Tuple<int, int> strutPosition = strutPositions[strutIndex];
      int strutsLeft = strutPosition.Item2;
      int path = 0;
      while (controlBoxStrutOrder[path].Length <= strutsLeft) {
        strutsLeft -= controlBoxStrutOrder[path].Length;
        path++;
        pixelIndex += MaxStripLength;
      }
      for (int priorStrut = 0;
          priorStrut < strutsLeft;
          priorStrut++) {
        pixelIndex +=
          strutLengths[controlBoxStrutOrder[path][priorStrut]];
      }
      return Tuple.Create(strutPosition.Item1, pixelIndex);
    }

    public static int FindStrutIndex(
      int controlBoxIndex, int controlBoxStrutIndex
    ) {
      for (int i = 0; i < strutPositions.Length; i++) {
        Tuple<int, int> position = strutPositions[i];
        if (controlBoxIndex == position.Item1 &&
            controlBoxStrutIndex == position.Item2) {
          return i;
        }
      }
      return -1;
    }

    public static List<int> GetControllerCableStruts(
      int boxIndex, int half
    ) {
      int start = half == 0 ? 0 : cableAStrutCount;
      int end = half == 0 ? cableAStrutCount : domeStrutsPerBox;
      var struts = new List<int>();
      for (int localIndex = start; localIndex < end; localIndex++) {
        int strutIndex = FindStrutIndex(boxIndex, localIndex);
        if (strutIndex != -1) {
          struts.Add(strutIndex);
        }
      }
      return struts;
    }

    public static List<int> GetStripPathStruts(int boxIndex, int path) {
      var struts = new List<int>();
      if (boxIndex < 0 || boxIndex >= DomeOutputMapper.NumDomeBoxes ||
          path < 0 || path >= DomeOutputMapper.NumPortsPerBox) {
        return struts;
      }
      int start = 0;
      for (int priorPath = 0; priorPath < path; priorPath++) {
        start += controlBoxStrutOrder[priorPath].Length;
      }
      int end = start + controlBoxStrutOrder[path].Length;
      for (int localIndex = start; localIndex < end; localIndex++) {
        int strutIndex = FindStrutIndex(boxIndex, localIndex);
        if (strutIndex != -1) {
          struts.Add(strutIndex);
        }
      }
      return struts;
    }

    public static List<int> GetPhysicalCableStruts(
      int boxIndex, int half, int[]? portMapping
    ) {
      bool valid = DomeOutputMapper.IsValidPortMapping(portMapping);
      var struts = new List<int>();
      int firstPort = half * DomeOutputMapper.StrandsPerCable;
      int endPort = firstPort + DomeOutputMapper.StrandsPerCable;
      for (int port = firstPort; port < endPort; port++) {
        int path = valid && portMapping != null ? portMapping[port] : port;
        struts.AddRange(GetStripPathStruts(boxIndex, path));
      }
      return struts;
    }

    public static DomeFrame MakeFrame() {
      return new DomeFrame(topology.Value);
    }

    private static DomeTopology BuildTopology() {
      var pixels = new List<DomeTopologyPixel>();
      for (int strut = 0; strut < StrutCount; strut++) {
        int ledCount = GetLedCount(strut);
        for (int led = 0; led < ledCount; led++) {
          Tuple<double, double> stripPoint =
            StrutLayoutFactory.GetProjectedLEDPoint(
              strut, led, DomeProjection.StripExtents);
          Tuple<double, double> topDownPoint =
            StrutLayoutFactory.GetProjectedLEDPoint(
              strut, led, DomeProjection.TopDown);
          pixels.Add(new DomeTopologyPixel(
            strut,
            led,
            stripPoint.Item1,
            stripPoint.Item2,
            topDownPoint.Item1,
            topDownPoint.Item2));
        }
      }
      return new DomeTopology(pixels.ToArray());
    }
  }
}
