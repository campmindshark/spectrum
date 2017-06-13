using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Spectrum.Base {

  public class ComputerEnabledColors : INotifyPropertyChanged {

    public bool[] array { get; set; }

    public event PropertyChangedEventHandler PropertyChanged;

    public bool this[int index] {
      get {
        if (this.array == null) {
          this.array = Enumerable.Repeat(true, 16).ToArray();
        }
        return this.array[index];
      }
      set {
        if (this.array == null) {
          this.array = Enumerable.Repeat(true, 16).ToArray();
        }
        this.array[index] = value;
        this.PropertyChanged?.Invoke(
          this,
          new PropertyChangedEventArgs(Binding.IndexerName)
        );
      }
    }
  }

}
