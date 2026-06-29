using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.Base {

  public struct DomeLEDCommand {
    public bool isFlush;
    // When non-null, this command carries a whole frame: a snapshot of every
    // dome pixel's color in canonical buffer order (strut 0..N, led 0..len,
    // matching LEDDomeOutput.MakeDomeOutputBuffer). Lets a buffer-based
    // visualizer hand the simulator one command per frame instead of one per
    // pixel. Implies a redraw; strutIndex/ledIndex/color are unused when set.
    public int[] frame;
    // rest doesn't matter if isFlush or frame != null
    public int strutIndex;
    public int ledIndex;
    public int color;
  }

  public struct BarLEDCommand {
    public bool isFlush;
    // rest doesn't matter if isFlush
    // isRunner: false - infinity strip(s); true - runner strip
    public bool isRunner;
    public int ledIndex;
    public int color;
  }

  public struct StageLEDCommand {
    public bool isFlush;
    // rest doesn't matter if isFlush
    public int sideIndex;
    public int ledIndex;
    public int layerIndex;
    public int color;
  }

}