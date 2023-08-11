using Spectrum.Base;
using Spectrum.Audio;
using Spectrum.LEDs;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Spectrum {

  class LEDDomeSnakesVisualizer : Visualizer {
    // Simple visualizer enbracing the triangles
    // A couple of snakes randomly run around leaving colors in their wake

    private const int colorPaletteCount = 8;
    private const int snakeLength = 7;

    private readonly AudioInput audio;
    private readonly Configuration config;
    private readonly LEDDomeOutput dome;
    private readonly Random random = new Random();
    private readonly Queue<TriangleSegment>[] snakes = new Queue<TriangleSegment>[] { new Queue<TriangleSegment>(), new Queue<TriangleSegment>() };
    private readonly TriangleSegment[] triangleSegments;

    private int colorPaletteIndex = 0;
    private bool enabled = false;
    private DateTime lastUpdate = DateTime.Now;

    public LEDDomeSnakesVisualizer(
      Configuration config,
      AudioInput audio,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.audio = audio;
      this.dome = dome;

      this.dome.RegisterVisualizer(this);

      var triangleFactory = new TriangleSegmentFactory(config);
      triangleSegments = triangleFactory.GetAll();
    }

    public int Priority {
      get {
        // The mapping between Visualizers and their corresponding domeActiveVis
        // integer value is determined in the condition below, as well as in the
        // Bind call for domeActiveVis in MainWindow.xaml.cs
        if (this.config.domeActiveVis != 3) {
          // By setting the priority to 0, we guarantee that this Visualizer
          // will not run
          return 0;
        }
        // You can return any number higher than 1 here to make sure this
        // Visualizer runs and the screensaver Visualizer doesn't
        return 2;
      }
    }

    public bool Enabled {
      get {
        return this.enabled;
      }
      set {
        if (value == this.enabled) {
          return;
        }

        this.enabled = value;
      }
    }

    public Input[] GetInputs() {
      return new Input[] {
        this.audio,
      };
    }

    public void Visualize() {

      // Rudimentary way to slow things down
      // Need to add logic to set speed on audio attributes
      if (lastUpdate.AddMilliseconds(50) > DateTime.Now) {
        return;
      }

      // Progress all snakes
      foreach (var snake in snakes) {
        ProgressSnake(snake, 0, this.dome.GetSingleColor(colorPaletteIndex));
      }
      colorPaletteIndex = (colorPaletteIndex + 1) % (colorPaletteCount - 1);
      lastUpdate = DateTime.Now;

      this.dome.Flush();
    }

    private void ProgressSnake(Queue<TriangleSegment> snake, int snakeColor, int trailingColor) {
      if (!snake.Any()) {
        snake.Enqueue(triangleSegments[0]);
      }

      TriangleSegment nextTriangle = null;

      int attemptCount = 0;
      while (nextTriangle == null || snake.Contains(nextTriangle)) {
        if (attemptCount++ > 10) {
          nextTriangle = triangleSegments[0];
          break;
        }
        nextTriangle = GetNextTriangle(snake.Last());
      }

      if (snake.Count() > snakeLength) {
        SetTriangleColor(snake.Dequeue(), trailingColor);
      }
      snake.Enqueue(nextTriangle);
      SetTriangleColor(nextTriangle, snakeColor);
    }

    private TriangleSegment GetNextTriangle(TriangleSegment currentTriangle) {
      int startingDirection = this.random.Next(0, 4);
      int direction = startingDirection;
      TriangleSegment nextTriangle = null;
      while (true) {
        nextTriangle = GetDirectionalTriangle(currentTriangle, direction++);
        if (nextTriangle != null) {
          return nextTriangle;
        }
        
        if (direction > 3) {
          direction = 0;
        }

        if (direction == startingDirection) {
          return null;
        }
      }
    }

    private static TriangleSegment GetDirectionalTriangle(TriangleSegment currentTriangle, int direction) {
      switch (direction) {
        case 0: return currentTriangle.SegmentToLeft;
        case 1: return currentTriangle.SegmentAbove;
        case 2: return currentTriangle.SegmentToRight;
        case 3: return currentTriangle.SegmentBelow;
        default: return currentTriangle.SegmentToRight;
      }
    }

    private void SetTriangleColor(TriangleSegment triangle, int color) {
      foreach (var strut in triangle.GetStruts()) {
        SetStrutColor(strut, color);
      }
    }

    private void SetStrutColor(Strut strut, int color) {
      for (int j = 0; j < strut.Length; j++) {
        this.dome.SetPixel(strut.Index, j, color);
      }
    }
  }

  // We may want to move this into the StrutLayout class or nearby
  public class TriangleSegment : StrutLayoutSegment {
    public Strut FirstStrut { get; }
    public Strut SecondStrut { get; }
    public Strut ThirdStrut { get; }

    public TriangleSegment SegmentAbove { get; set; }
    public TriangleSegment SegmentBelow { get; set; }
    public TriangleSegment SegmentToLeft { get; set; }
    public TriangleSegment SegmentToRight { get; set; }

    public bool PointsUp { get; }

    /// <summary>
    /// Provide struts in a clockwise direction
    /// </summary>
    /// <param name="config"></param>
    /// <param name="firstStrutIndex"></param>
    /// <param name="secondStrutIndex"></param>
    /// <param name="thirdStrutIndex"></param>
    /// <param name="pointsUp"></param>
    public TriangleSegment(Configuration config, bool pointsUp, int firstStrutIndex, int secondStrutIndex, int thirdStrutIndex) :
      this(pointsUp, Strut.FromIndex(config, firstStrutIndex), Strut.FromIndex(config, secondStrutIndex), Strut.FromIndex(config, thirdStrutIndex)) {
    }

    public TriangleSegment(bool pointsUp, Strut firstStrut, Strut secondStrut, Strut thirdStrut) :
      base(new HashSet<Strut>() { firstStrut, secondStrut, thirdStrut }) {
      FirstStrut = firstStrut;
      SecondStrut = secondStrut;
      ThirdStrut = thirdStrut;
      PointsUp = pointsUp;
    }
  }

  // We may want to move this into the StrutLayoutFactory class or nearby
  public class TriangleSegmentFactory {
    private readonly Configuration config;
    private readonly List<TriangleSegment>[] rows = new List<TriangleSegment>[5];

    public TriangleSegment[] GetAll() {
      return GetMergedRows().ToArray();
    }

    public TriangleSegment[] GetLayer(uint layer) {
      if (rows.Length > layer) {
        return rows[(int)layer]?.ToArray() ?? new TriangleSegment[0];
      }

      return new TriangleSegment[0];
    }

    public TriangleSegmentFactory(Configuration config) {
      this.config = config;
      LoadSegments();
    }

    private IEnumerable<TriangleSegment> GetMergedRows() {
      foreach (List<TriangleSegment> row in rows) {
        if (row == null) {
          continue;
        }

        foreach (TriangleSegment triangleSegment in row) {
          if (triangleSegment != null) {
            yield return triangleSegment;
          }
        }
      }
    }

    private void LoadSegments() {

      // Layer 1
      AddTriangle(0, 72, 71, 0, true);
      AddTriangle(0, 73, 21, 72, false);
      AddTriangle(0, 74, 73, 1, true);
      AddTriangle(0, 75, 22, 74, false);
      AddTriangle(0, 76, 75, 2, true);
      AddTriangle(0, 77, 23, 76, false);
      AddTriangle(0, 78, 77, 3, true);
      AddTriangle(0, 79, 24, 78, false);
      AddTriangle(0, 80, 79, 4, true);
      AddTriangle(0, 81, 25, 80, false);
      AddTriangle(0, 82, 81, 5, true);
      AddTriangle(0, 83, 26, 82, false);
      AddTriangle(0, 84, 83, 6, true);
      AddTriangle(0, 85, 27, 84, false);
      AddTriangle(0, 86, 85, 7, true);
      AddTriangle(0, 87, 28, 86, false);
      AddTriangle(0, 88, 87, 8, true);
      AddTriangle(0, 89, 29, 74, false);
      AddTriangle(0, 90, 89, 9, true);
      AddTriangle(0, 91, 30, 74, false);
      AddTriangle(0, 92, 91, 10, true);
      AddTriangle(0, 93, 31, 74, false);
      AddTriangle(0, 94, 93, 11, true);
      AddTriangle(0, 95, 32, 74, false);
      AddTriangle(0, 96, 95, 12, true);
      AddTriangle(0, 97, 33, 74, false);
      AddTriangle(0, 98, 97, 13, true);
      AddTriangle(0, 99, 34, 74, false);
      AddTriangle(0, 100, 99, 14, true);
      AddTriangle(0, 101, 35, 74, false);
      AddTriangle(0, 102, 101, 15, true);
      AddTriangle(0, 103, 36, 74, false);
      AddTriangle(0, 104, 103, 16, true);
      AddTriangle(0, 105, 37, 74, false);
      AddTriangle(0, 106, 105, 17, true);
      AddTriangle(0, 107, 38, 74, false);
      AddTriangle(0, 108, 107, 18, true);
      AddTriangle(0, 109, 39, 108, false);
      AddTriangle(0, 70, 109, 19, true);
      AddTriangle(0, 71, 20, 70, false);

      // Layer 2
      AddTriangle(1, 111, 110, 20, true);
      AddTriangle(1, 112, 40, 111, false);
      AddTriangle(1, 113, 112, 21, true);
      AddTriangle(1, 114, 113, 22, true);
      AddTriangle(1, 115, 41, 114, false);
      AddTriangle(1, 116, 115, 23, true);
      AddTriangle(1, 117, 42, 116, false);
      AddTriangle(1, 118, 117, 24, true);
      AddTriangle(1, 119, 43, 118, false);
      AddTriangle(1, 120, 119, 25, true);
      AddTriangle(1, 121, 120, 26, true);
      AddTriangle(1, 122, 44, 121, false);
      AddTriangle(1, 123, 122, 27, true);
      AddTriangle(1, 124, 45, 123, false);
      AddTriangle(1, 125, 124, 28, true);
      AddTriangle(1, 126, 46, 125, false);
      AddTriangle(1, 127, 126, 29, true);
      AddTriangle(1, 128, 127, 30, true);
      AddTriangle(1, 129, 47, 128, false);
      AddTriangle(1, 130, 129, 31, true);
      AddTriangle(1, 131, 48, 130, false);
      AddTriangle(1, 132, 131, 32, true);
      AddTriangle(1, 133, 49, 132, false);
      AddTriangle(1, 134, 133, 33, true);
      AddTriangle(1, 135, 134, 34, true);
      AddTriangle(1, 136, 50, 135, false);
      AddTriangle(1, 137, 136, 35, true);
      AddTriangle(1, 138, 51, 137, false);
      AddTriangle(1, 139, 138, 36, true);
      AddTriangle(1, 140, 52, 139, false);
      AddTriangle(1, 141, 140, 37, true);
      AddTriangle(1, 142, 141, 38, true);
      AddTriangle(1, 143, 53, 142, false);
      AddTriangle(1, 144, 143, 39, true);
      AddTriangle(1, 110, 54, 144, false);

      // Layer 3
      AddTriangle(2, 147, 146, 40, true);
      AddTriangle(2, 148, 147, 41, true);
      AddTriangle(2, 149, 56, 148, false);
      AddTriangle(2, 150, 149, 42, true);
      AddTriangle(2, 151, 57, 150, false);
      AddTriangle(2, 152, 151, 43, true);
      AddTriangle(2, 153, 152, 44, true);
      AddTriangle(2, 154, 58, 153, false);
      AddTriangle(2, 155, 154, 45, true);
      AddTriangle(2, 156, 59, 155, false);
      AddTriangle(2, 157, 156, 46, true);
      AddTriangle(2, 158, 157, 47, true);
      AddTriangle(2, 159, 60, 158, false);
      AddTriangle(2, 160, 159, 48, true);
      AddTriangle(2, 161, 61, 160, false);
      AddTriangle(2, 162, 161, 49, true);
      AddTriangle(2, 163, 162, 50, true);
      AddTriangle(2, 164, 62, 163, false);
      AddTriangle(2, 165, 164, 51, true);
      AddTriangle(2, 166, 63, 165, false);
      AddTriangle(2, 167, 166, 52, true);
      AddTriangle(2, 168, 167, 53, true);
      AddTriangle(2, 169, 64, 168, false);
      AddTriangle(2, 145, 169, 54, true);
      AddTriangle(2, 146, 55, 145, false);

      // Layer 4
      AddTriangle(3, 171, 170, 55, true);
      AddTriangle(3, 172, 171, 56, true);
      AddTriangle(3, 173, 65, 172, false);
      AddTriangle(3, 174, 173, 57, true);
      AddTriangle(3, 175, 174, 58, true);
      AddTriangle(3, 176, 66, 175, false);
      AddTriangle(3, 177, 176, 59, true);
      AddTriangle(3, 178, 177, 60, true);
      AddTriangle(3, 179, 67, 178, false);
      AddTriangle(3, 180, 179, 61, true);
      AddTriangle(3, 181, 180, 62, true);
      AddTriangle(3, 182, 68, 181, false);
      AddTriangle(3, 183, 182, 63, true);
      AddTriangle(3, 184, 183, 64, true);
      AddTriangle(3, 170, 69, 184, false);

      // Layer 5
      AddTriangle(4, 186, 185, 65, true);
      AddTriangle(4, 187, 186, 66, true);
      AddTriangle(4, 188, 187, 67, true);
      AddTriangle(4, 189, 188, 68, true);
      AddTriangle(4, 185, 189, 69, true);
    }

    private void AddTriangle(int row, int first, int second, int third, bool pointsUp) {
      rows[row] = rows[row] ?? new List<TriangleSegment>();
      var newTriangleSegment = new TriangleSegment(config, pointsUp, first, second, third);
      rows[row].Add(newTriangleSegment);

      TriangleSegment[] allTriangles = GetAllTriangles(rows).ToArray();

      // Points Up
      if (pointsUp) {
        // Find Below
        var triangleBelow = allTriangles.Where(t => t.SecondStrut.Index == third).FirstOrDefault();
        if (triangleBelow != null) {
          newTriangleSegment.SegmentBelow = triangleBelow;
          triangleBelow.SegmentAbove = newTriangleSegment;
        }

        // Find Left
        var triangleToLeft = allTriangles.Where(t => (t.SecondStrut.Index == first && t.PointsUp) || (t.ThirdStrut.Index == first && !t.PointsUp)).FirstOrDefault();
        if (triangleToLeft != null) {
          newTriangleSegment.SegmentToLeft = triangleToLeft;
          triangleToLeft.SegmentToRight = newTriangleSegment;
        }

        // Find Right
        var triangleToRight = allTriangles.Where(t => t.FirstStrut.Index == second ).FirstOrDefault();
        if (triangleToRight != null) {
          newTriangleSegment.SegmentToRight = triangleToRight;
          triangleToRight.SegmentToLeft = newTriangleSegment;
        }

        return;
      }

      // Points Down
      if (!pointsUp) {
        // Find Above
        var triangleAbove = allTriangles.Where(t => t.ThirdStrut.Index == second).FirstOrDefault();
        if (triangleAbove != null) {
          newTriangleSegment.SegmentAbove = triangleAbove;
          triangleAbove.SegmentBelow = newTriangleSegment;
        }

        // Find Left
        var triangleToLeft = allTriangles.Where(t => (t.SecondStrut.Index == first && t.PointsUp) || (t.ThirdStrut.Index == first && !t.PointsUp)).FirstOrDefault();
        if (triangleToLeft != null) {
          newTriangleSegment.SegmentToLeft = triangleToLeft;
          triangleToLeft.SegmentToRight = newTriangleSegment;
        }

        // Find Right
        var triangleToRight = allTriangles.Where(t => t.FirstStrut.Index == third).FirstOrDefault();
        if (triangleToRight != null) {
          newTriangleSegment.SegmentToRight = triangleToRight;
          triangleToRight.SegmentToLeft = newTriangleSegment;
        }
      }
    }

    private IEnumerable<TriangleSegment> GetAllTriangles(List<TriangleSegment>[] layers) {
      foreach (var layer in layers) {
        if (layer == null) {
          continue;
        }

        foreach (var triangle in layer) {
          if (triangle != null) {
            yield return triangle;
          }
        }
      }
    }
  }
}
