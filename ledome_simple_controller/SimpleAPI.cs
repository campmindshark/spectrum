using System.IO.Ports;

namespace LEDome {

  public class SimpleAPI {

    private SerialPort port;

    public SimpleAPI(string portName) {
      this.port = new SerialPort(portName);
    }

    public void Open() {
      this.port.Open();
      byte[] mode_buffer = new byte[1] { 1 };
      this.port.Write(mode_buffer, 0, 1);
    }

    public void Close() {
      byte[] exit_buffer = new byte[2] { 0, 0 };
      this.port.Write(exit_buffer, 0, 2);
      this.port.Close();
    }

    public void Flush() {
      byte[] flush_buffer = new byte[2] { 1, 0 };
      this.port.Write(flush_buffer, 0, 2);
    }

    public void SetPixel(int pixelIndex, int color) {
      int message = pixelIndex + 2;
      byte[] command_buffer = new byte[5] {
        (byte)message,
        (byte)(message >> 8),
        (byte)color,
        (byte)(color >> 8),
        (byte)(color >> 16),
      };
      this.port.Write(command_buffer, 0, 5);
    }

  }

}