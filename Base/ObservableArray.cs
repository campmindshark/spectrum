using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Spectrum.Base {

  public class ComputerEnabledColors : INotifyPropertyChanged {

    public bool[] array;

    public event PropertyChangedEventHandler PropertyChanged;

    public ComputerEnabledColors() {
      this.array = Enumerable.Repeat(true, 16).ToArray();
    }

    public bool this[int index] {
      get {
        return this.array[index];
      }
      set {
        this.array[index] = value;
        this.PropertyChanged?.Invoke(
          this,
          new PropertyChangedEventArgs(Binding.IndexerName)
        );
      }
    }
  }

}
