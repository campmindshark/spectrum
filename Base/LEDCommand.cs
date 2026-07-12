using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spectrum.Base {

  public struct DomeLEDCommand {
    public bool isFlush;
    // The rest doesn't matter if isFlush. Whole normal frames travel through
    // LEDDomeOutput's latest-frame mailbox; this command remains for ordered
    // diagnostic/calibration pixel writes.
    public int strutIndex;
    public int ledIndex;
    public int color;
  }

}
