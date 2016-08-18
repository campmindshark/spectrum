using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;

namespace Spectrum.LEDs {

  public class VolumeAnimation {

    private static AnimationLayout layout;

    static VolumeAnimation() {
      layout = new AnimationLayout(new AnimationLayoutSegment[] {
        new AnimationLayoutSegment(new HashSet<Strut>() {
        }),
        new AnimationLayoutSegment(new HashSet<Strut>() {
        }),
      });
    }

    private Configuration config;


  }

}
