using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;

namespace LEDome {

  class Program {

    static void Main(string[] args) {
      string[] ports = SerialPort.GetPortNames();
      foreach (string port in ports) {
        Console.WriteLine(port);
      }
      Console.ReadLine();
      /*SquareAPI api = new SquareAPI("COM3", 30, 5);
      api.Open();
      for (int i = 0; i < 30; i++) {
        for (int j = 0; j < 40; j++) {
          api.SetPixel(i, j, 0x000000);
        }
      }
      api.Flush();
      for (int i = 0; i < 256; i++) {
        int color = 0x0000FF | (i << 16);
        api.SetPixel(22, 5, color);
        api.Flush();
      }
      api.SetPixel(0, 6, 0xFFFFFF);
      api.SetPixel(1, 6, 0xFFFFFF);
      api.SetPixel(2, 6, 0xFFFFFF);
      api.SetPixel(1, 7, 0xFFFFFF);
      api.SetPixel(0, 8, 0xFFFFFF);
      api.SetPixel(1, 8, 0xFFFFFF);
      api.SetPixel(2, 8, 0xFFFFFF);
      api.SetPixel(0, 10, 0xFFFFFF);
      api.SetPixel(1, 10, 0xFFFFFF);
      api.SetPixel(2, 10, 0xFFFFFF);
      api.SetPixel(0, 13, 0xFFFFFF);
      api.SetPixel(1, 13, 0xFFFFFF);
      api.SetPixel(2, 13, 0xFFFFFF);
      api.SetPixel(0, 14, 0xFFFFFF);
      api.SetPixel(0, 15, 0xFFFFFF);
      api.SetPixel(0, 17, 0xFFFFFF);
      api.SetPixel(1, 17, 0xFFFFFF);
      api.SetPixel(1, 18, 0xFFFFFF);
      api.SetPixel(2, 18, 0xFFFFFF);
      api.SetPixel(1, 19, 0xFFFFFF);
      api.SetPixel(0, 19, 0xFFFFFF);
      api.SetPixel(0, 21, 0xFFFFFF);
      api.SetPixel(1, 21, 0xFFFFFF);
      api.SetPixel(2, 21, 0xFFFFFF);
      api.SetPixel(1, 22, 0xFFFFFF);
      api.SetPixel(2, 22, 0xFFFFFF);
      api.SetPixel(0, 23, 0xFFFFFF);
      api.SetPixel(2, 23, 0xFFFFFF);
      api.SetPixel(0, 25, 0xFFFFFF);
      api.SetPixel(1, 25, 0xFFFFFF);
      api.SetPixel(2, 25, 0xFFFFFF);
      api.SetPixel(1, 26, 0xFFFFFF);
      api.SetPixel(2, 26, 0xFFFFFF);
      api.SetPixel(0, 27, 0xFFFFFF);
      api.SetPixel(2, 27, 0xFFFFFF);
      api.SetPixel(2, 29, 0xFFFFFF);
      api.SetPixel(1, 30, 0xFFFFFF);
      api.SetPixel(0, 30, 0xFFFFFF);
      api.SetPixel(2, 31, 0xFFFFFF);
      api.Flush();
      api.Close();*/
    }

  }

}
