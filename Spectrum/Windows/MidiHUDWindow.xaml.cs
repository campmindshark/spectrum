using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Spectrum.Base;

namespace Spectrum {

  public partial class MidiHUDWindow : Window {

    private Configuration config;

    public MidiHUDWindow(Configuration config) {
      this.InitializeComponent();
      this.config = config;
    }

  }

}