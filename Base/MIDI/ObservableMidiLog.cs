using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Collections.Concurrent;

namespace Spectrum.Base {

  public struct MidiLogMessage {
    public string message;
    public DateTime time;
  }

  public class ObservableMidiLog : INotifyPropertyChanged {

    public static int bufferSize = 200;

    private BlockingCollection<MidiLogMessage> messages
      = new BlockingCollection<MidiLogMessage>(bufferSize);

    public event PropertyChangedEventHandler PropertyChanged;

    public void Append(string message) {
      lock (this.messages) {
        if (this.messages.Count >= this.messages.BoundedCapacity) {
          this.messages.Take(this.messages.Count + 1 - this.messages.BoundedCapacity);
        }
        this.messages.Add(new MidiLogMessage() {
          message = message,
          time = DateTime.Now,
        });
        this.PropertyChanged?.Invoke(
          this,
          new PropertyChangedEventArgs("")
        );
      }
    }

    public MidiLogMessage[] DequeueAllMessages() {
      lock (this.messages) {
        MidiLogMessage[] result = new MidiLogMessage[this.messages.Count];
        for (int i = 0; i < this.messages.Count; i++) {
          // Curiously, Take() removes the item, but Take(1) does not
          result[i] = this.messages.Take();
        }
        return result;
      }
    }

  }

}
