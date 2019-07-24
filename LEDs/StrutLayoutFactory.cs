using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;

namespace Spectrum.LEDs {

  // pt1 => pt2 => (strut, reversed)
  using EdgeDictionary = Dictionary<int, Dictionary<int, Tuple<int, bool>>>;

  public class StrutLayoutFactory {

    // these are all the struts, and their start and end points
    public static int[,] lines = new int[,] {
      { 0,  1  }, { 1,  2  }, { 3,  2  }, { 3,  4  }, { 4,  5  }, { 5,  6  },
      { 7,  6  }, { 7,  8  }, { 8,  9  }, { 9,  10 }, { 11, 10 }, { 11, 12 },
      { 12, 13 }, { 13, 14 }, { 15, 14 }, { 15, 16 }, { 16, 17 }, { 17, 18 },
      { 19, 18 }, { 19, 0  }, { 20, 21 }, { 22, 21 }, { 23, 22 }, { 24, 23 },
      { 24, 25 }, { 26, 25 }, { 27, 26 }, { 28, 27 }, { 28, 29 }, { 30, 29 },
      { 31, 30 }, { 32, 31 }, { 32, 33 }, { 34, 33 }, { 35, 34 }, { 36, 35 },
      { 36, 37 }, { 38, 37 }, { 39, 38 }, { 20, 39 }, { 41, 40 }, { 42, 41 },
      { 43, 42 }, { 44, 43 }, { 45, 44 }, { 46, 45 }, { 47, 46 }, { 48, 47 },
      { 49, 48 }, { 50, 49 }, { 51, 50 }, { 52, 51 }, { 53, 52 }, { 54, 53 },
      { 40, 54 }, { 56, 55 }, { 57, 56 }, { 58, 57 }, { 59, 58 }, { 60, 59 },
      { 61, 60 }, { 62, 61 }, { 63, 62 }, { 64, 63 }, { 55, 64 }, { 65, 66 },
      { 66, 67 }, { 67, 68 }, { 68, 69 }, { 69, 65 }, { 20, 0  }, { 0, 21  },
      { 21, 1  }, { 1,  22 }, { 2,  22 }, { 23, 2  }, { 23, 3  }, { 24, 3  },
      { 24, 4  }, { 4,  25 }, { 25, 5  }, { 5,  26 }, { 6, 26  }, { 27, 6  },
      { 27, 7  }, { 28, 7  }, { 28, 8  }, { 8,  29 }, { 29, 9  }, { 9, 30  },
      { 10, 30 }, { 31, 10 }, { 31, 11 }, { 32, 11 }, { 32, 12 }, { 12, 33 },
      { 33, 13 }, { 13, 34 }, { 14, 34 }, { 35, 14 }, { 35, 15 }, { 36, 15 },
      { 36, 16 }, { 16, 37 }, { 37, 17 }, { 17, 38 }, { 18, 38 }, { 39, 18 },
      { 39, 19 }, { 20, 19 }, { 20, 40 }, { 21, 40 }, { 21, 41 }, { 22, 41 },
      { 41, 23 }, { 42, 23 }, { 24, 42 }, { 24, 43 }, { 25, 43 }, { 25, 44 },
      { 26, 44 }, { 44, 27 }, { 45, 27 }, { 28, 45 }, { 28, 46 }, { 29, 46 },
      { 29, 47 }, { 30, 47 }, { 47, 31 }, { 48, 31 }, { 32, 48 }, { 32, 49 },
      { 33, 49 }, { 33, 50 }, { 34, 50 }, { 50, 35 }, { 51, 35 }, { 36, 51 },
      { 36, 52 }, { 37, 52 }, { 37, 53 }, { 38, 53 }, { 53, 39 }, { 54, 39 },
      { 20, 54 }, { 40, 55 }, { 40, 56 }, { 41, 56 }, { 56, 42 }, { 42, 57 },
      { 43, 57 }, { 43, 58 }, { 44, 58 }, { 58, 45 }, { 45, 59 }, { 46, 59 },
      { 46, 60 }, { 47, 60 }, { 60, 48 }, { 48, 61 }, { 49, 61 }, { 49, 62 },
      { 50, 62 }, { 62, 51 }, { 51, 63 }, { 52, 63 }, { 52, 64 }, { 53, 64 },
      { 64, 54 }, { 54, 55 }, { 55, 65 }, { 56, 65 }, { 57, 65 }, { 57, 66 },
      { 58, 66 }, { 59, 66 }, { 59, 67 }, { 60, 67 }, { 61, 67 }, { 61, 68 },
      { 62, 68 }, { 63, 68 }, { 63, 69 }, { 64, 69 }, { 55, 69 }, { 65, 70 },
      { 66, 70 }, { 67, 70 }, { 68, 70 }, { 69, 70 },
    };
    private static readonly EdgeDictionary edgeDictionary;

    // these are all the points of the dome, mapped into 2d space ("azimuthal
    // equidistant" projection)
    // these are normalized to 0 to 1 coordinates in InitPoints
    // What has become of my life
    private static int[,] handDrawnPoints = new int[,] {
      { 395, 86  }, { 477, 107 }, { 545, 157 }, { 591, 229 }, { 623, 319 }, // 1
      { 627, 404 }, { 599, 484 }, { 546, 551 }, { 471, 606 }, { 390, 637 },
      { 304, 637 }, { 226, 605 }, { 149, 551 }, { 95,  485 }, { 70,  403 },
      { 74,  319 }, { 103, 229 }, { 149, 157 }, { 218, 107 }, { 299, 87  },
      { 348, 139 }, { 425, 149 }, { 487, 165 }, { 524, 219 }, { 555, 290 }, // 2
      { 572, 366 }, { 575, 431 }, { 535, 482 }, { 477, 534 }, { 409, 574 },
      { 348, 595 }, { 286, 573 }, { 220, 536 }, { 163, 483 }, { 123, 431 },
      { 125, 366 }, { 141, 292 }, { 172, 220 }, { 209, 166 }, { 270, 148 },
      { 386, 199 }, { 456, 210 }, { 487, 272 }, { 511, 346 }, { 523, 414 }, // 3
      { 473, 463 }, { 410, 509 }, { 348, 541 }, { 286, 509 }, { 223, 464 },
      { 173, 415 }, { 185, 347 }, { 209, 273 }, { 240, 209 }, { 310, 199 },
      { 348, 259 }, { 418, 262 }, { 442, 327 }, { 461, 394 }, { 406, 437 }, // 4
      { 348, 476 }, { 290, 438 }, { 236, 394 }, { 253, 327 }, { 278, 262 },
      { 379, 314 }, { 400, 375 }, { 349, 412 }, { 297, 375 }, { 316, 314 }, // 5
      { 348, 358 }                                                          // 6
    };

    private static double[,] points;
    private static void InitPoints() {
      points = new double[handDrawnPoints.GetLength(0), 2];
      for (int i = 0; i < handDrawnPoints.GetLength(0); i++) {
        points[i, 0] = (((double)handDrawnPoints[i,0])-70.0)/557.0;
        points[i, 1] = (((double)handDrawnPoints[i, 1]) - 86) / 551.0;
      }
    }

    static StrutLayoutFactory() {
      edgeDictionary = new EdgeDictionary();
      for (int i = 0; i < lines.GetLength(0); i++) {
        int pt0 = lines[i, 0];
        int pt1 = lines[i, 1];
        if (!edgeDictionary.ContainsKey(pt0)) {
          edgeDictionary[pt0] = new Dictionary<int, Tuple<int, bool>>();
        }
        if (!edgeDictionary.ContainsKey(pt1)) {
          edgeDictionary[pt1] = new Dictionary<int, Tuple<int, bool>>();
        }
        edgeDictionary[pt0].Add(pt1, new Tuple<int, bool>(i, false));
        edgeDictionary[pt1].Add(pt0, new Tuple<int, bool>(i, true));
      }

      InitPoints();
    }

    public static Tuple<double, double> GetProjectedPoint(
      int strutIndex,
      int point
    ) {
      int index = StrutLayoutFactory.lines[strutIndex, point];
      return new Tuple<double, double>(
        points[index, 0], points[index, 1]
       );
    }

    public static Tuple<double, double> GetProjectedLEDPoint(
      int strutIndex,
      int ledIndex
    ) {
      var p1 = GetProjectedPoint(strutIndex, 0);
      var p2 = GetProjectedPoint(strutIndex, 1);
      var leds = LEDDomeOutput.GetNumLEDs(strutIndex);

      var d = ((double)ledIndex + 1) / ((double)leds + 2);
      var x = (p2.Item1 - p1.Item1) * d + p1.Item1;
      var y = (p2.Item2 - p1.Item2) * d + p1.Item2;

      return new Tuple<double, double>(x, y);
    }

    public static Tuple<
      double,
      double,
      double,
      double
    > GetProjectedLEDPointParametric(int strutIndex, int ledIndex) {
      var p = GetProjectedLEDPoint(strutIndex, ledIndex);

      var x = p.Item1;
      var y = p.Item2;

      x = x * 2 - 1;
      y = y * 2 - 1;

      var angle = Math.Atan2(y, x);
      var dist = Math.Sqrt(x * x + y * y);
      dist = dist > 1 ? 1 : dist;

      return new Tuple<double, double, double, double>(
        x, y, angle, dist
      );
    }
    public static StrutLayout[] ConcentricFromStartingPoints(
      Configuration config,
      HashSet<int> startingPoints,
      int numLayers
    ) {
      List<HashSet<int>> curPointsByGroup = new List<HashSet<int>>();
      foreach (int point in startingPoints) {
        HashSet<int> group = new HashSet<int>();
        group.Add(point);
        curPointsByGroup.Add(group);
      }

      List<StrutLayoutSegment> spokeSegments = new List<StrutLayoutSegment>();
      HashSet<Strut>[] strutsByGroup = new HashSet<Strut>[] {
        new HashSet<Strut>(), new HashSet<Strut>(), new HashSet<Strut>(),
        new HashSet<Strut>(), new HashSet<Strut>(), new HashSet<Strut>(),
      };
      List<StrutLayoutSegment> circleSegments = new List<StrutLayoutSegment>();
      HashSet<int> usedStrutIndices = new HashSet<int>();
      while (numLayers > 0) {
        HashSet<Strut> layer1 = new HashSet<Strut>();
        List<HashSet<int>> nextPointsByGroup = new List<HashSet<int>>();
        for (int i = 0; i < curPointsByGroup.Count(); i++) {
          var group = curPointsByGroup[i];
          HashSet<int> newPoints = new HashSet<int>();
          foreach (int point in group) {
            foreach (var connected in edgeDictionary[point]) {
              int strutIndex = connected.Value.Item1;
              if (usedStrutIndices.Contains(strutIndex)) {
                continue;
              }
              usedStrutIndices.Add(strutIndex);

              bool reversed = connected.Value.Item2;
              Strut strut = reversed
                ? Strut.ReversedFromIndex(config, strutIndex)
                : Strut.FromIndex(config, strutIndex);
              layer1.Add(strut);
              strutsByGroup[i].Add(strut);

              int connectedPoint = connected.Key;
              newPoints.Add(connectedPoint);
            }
          }
          nextPointsByGroup.Add(newPoints);
        }
        spokeSegments.Add(new StrutLayoutSegment(layer1));
        numLayers--;
        if (numLayers == 0) {
          break;
        }
        curPointsByGroup = nextPointsByGroup;

        HashSet<Strut> layer2 = new HashSet<Strut>();
        for (int i = 0; i < curPointsByGroup.Count(); i++) {
          var group = curPointsByGroup[i];

          // If we're not a circle, we need to start on one of the edges
          int currentPoint = group.First();
          foreach (int pt1 in group) {
            var connectedPoints = edgeDictionary[pt1].Keys.Intersect(group);
            if (connectedPoints.Count() == 1) {
              currentPoint = pt1;
              break;
            }
          }

          var pointsLeft = new HashSet<int>(group);
          HashSet<Strut> circleStruts = new HashSet<Strut>();
          while (true) {
            int? nextPointInLoop = null;
            var connectedPoints = edgeDictionary[currentPoint].Keys;
            foreach (int connectedPoint in connectedPoints) {
              if (!group.Contains(connectedPoint)) {
                continue;
              }
              var strutInfo = edgeDictionary[currentPoint][connectedPoint];

              var strutIndex = strutInfo.Item1;
              if (usedStrutIndices.Contains(strutIndex)) {
                continue;
              }
              usedStrutIndices.Add(strutIndex);

              var reversed = strutInfo.Item2;
              Strut strut = reversed
                ? Strut.ReversedFromIndex(config, strutIndex)
                : Strut.FromIndex(config, strutIndex);
              layer2.Add(strut);
              circleStruts.Add(strut);
              strutsByGroup[i].Add(strut);

              if (pointsLeft.Contains(connectedPoint)) {
                nextPointInLoop = connectedPoint;
              }
              break;
            }
            pointsLeft.Remove(currentPoint);
            if (nextPointInLoop.HasValue) {
              currentPoint = nextPointInLoop.Value;
            } else {
              break;
            }
          }
          circleSegments.Add(new StrutLayoutSegment(circleStruts));
        }
        spokeSegments.Add(new StrutLayoutSegment(layer2));
        numLayers--;
      }

      return new StrutLayout[] {
        new StrutLayout(spokeSegments.ToArray()),
        new StrutLayout(
          strutsByGroup.Select(set => new StrutLayoutSegment(set)).ToArray()
        ),
        new StrutLayout(circleSegments.ToArray()),
      };
    }

  }

}
