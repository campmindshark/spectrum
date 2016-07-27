using System.Collections.Generic;
using System.IO.Ports;

namespace LEDome {

  /**
   * SimpleAI is an API that can handle a single Teensy. It has no conception
   * of how many LEDs the Teensy is addressing - it just communicates a given
   * index and color to the Teensy.
   */
  public class SimpleAPI {

    private SerialPort port;
    private List<byte> buffer;

    public SimpleAPI(string portName) {
      this.port = new SerialPort(portName);
      this.buffer = new List<byte>();
    }

    public void Open() {
      this.port.Open();
      byte[] mode_buffer = new byte[1] { 1 };
      this.port.Write(mode_buffer, 0, 1);
      this.buffer.Clear();
    }

    public void Close() {
      byte[] exit_buffer = new byte[2] { 0, 0 };
      this.port.Write(exit_buffer, 0, 2);
      this.port.Close();
    }

    public void Flush() {
      this.buffer.Add(1);
      this.buffer.Add(0);
      byte[] buffer_array = this.buffer.ToArray();
      this.port.Write(buffer_array, 0, this.buffer.Count);
      this.buffer.Clear();
    }

    public void SetPixel(int pixelIndex, int color) {
      int message = pixelIndex + 2;
      this.buffer.Add((byte)message);
      this.buffer.Add((byte)(message >> 8));
      this.buffer.Add((byte)color);
      this.buffer.Add((byte)(color >> 8));
      this.buffer.Add((byte)(color >> 16));
    }

  }

}