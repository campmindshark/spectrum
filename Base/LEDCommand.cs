using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.Base {

  public struct DomeLEDCommand {
    public bool isFlush;
    // rest doesn't matter if isFlush
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

}