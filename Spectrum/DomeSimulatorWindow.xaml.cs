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
using System.Threading;
using System.Windows.Threading;

namespace Spectrum {

  public partial class DomeSimulatorWindow : Window {

    private Configuration config;
    private Line[] struts;
    private Thread thread;
    private List<LEDCommand> commandsSinceFlush;

    public DomeSimulatorWindow(Configuration config) {
      this.InitializeComponent();
      this.config = config;
      this.struts = new Line[lines.Length];
      this.commandsSinceFlush = new List<LEDCommand>();

      this.InitialDraw();

      this.thread = new Thread(DrawThread);
      this.thread.SetApartmentState(ApartmentState.STA);
      this.thread.Start();
    }

    private void InitialDraw() {
      this.Canvas.Children.Clear();
      for (int i = 0; i < lines.GetLength(0); i++) {
        var pt1 = GetPoint(lines[i, 0]);
        var pt2 = GetPoint(lines[i, 1]);
        Line line = new Line();
        line.X1 = pt1.Item1;
        line.Y1 = pt1.Item2;
        line.X2 = pt2.Item1;
        line.Y2 = pt2.Item2;
        line.Stroke = new SolidColorBrush(Colors.Black);
        line.StrokeThickness = 2;
        this.struts[i] = line;
        this.Canvas.Children.Add(line);
      }
    }

    private void HandleClose(object sender, EventArgs e) {
      this.thread.Abort();
      this.thread.Join();
      this.thread = null;
    }

    // What has become of my life
    private static int[,] points = new int[,] {
      { 349, 21 }, { 453, 37 }, { 546, 86 }, { 621, 159 }, { 668, 254 },    // 1
      { 685, 358 }, { 667, 461 }, { 618, 555 }, { 544, 629 }, { 453, 678 },
      { 348, 694 }, { 244, 676 }, { 150, 629 }, { 76, 554 }, { 29, 462 },
      { 12, 358 }, { 29, 253 }, { 77, 160 }, { 149, 87 }, { 245, 38 },
      { 395, 86 }, { 477, 107 }, { 545, 157 }, { 591, 229 }, { 623, 319 },  // 2
      { 627, 404 }, { 599, 484 }, { 546, 551 }, { 471, 606 }, { 390, 637 },
      { 304, 637 }, { 226, 605 }, { 149, 551 }, { 95, 485 }, { 70, 403 },
      { 74, 319 }, { 103, 229 }, { 149, 157 }, { 218, 107 }, { 299, 87 },
      { 348, 139 }, { 425, 149 }, { 487, 165 }, { 524, 219 }, { 555, 290 }, // 3
      { 572, 366 }, { 575, 431 }, { 535, 482 }, { 477, 534 }, { 409, 574 },
      { 348, 595 }, { 286, 573 }, { 220, 536 }, { 163, 483 }, { 123, 431 },
      { 125, 366 }, { 141, 292 }, { 172, 220 }, { 209, 166 }, { 270, 148 },
      { 386, 199 }, { 456, 210 }, { 487, 272 }, { 511, 346 }, { 523, 414 }, // 4
      { 473, 463 }, { 410, 509 }, { 348, 541 }, { 286, 509 }, { 223, 464 },
      { 173, 415 }, { 185, 347 }, { 209, 273 }, { 240, 209 }, { 310, 199 },
      { 348, 259 }, { 418, 262 }, { 442, 327 }, { 461, 394 }, { 406, 437 }, // 5
      { 348, 476 }, { 290, 438 }, { 236, 394 }, { 253, 327 }, { 278, 262 },
      { 379, 314 }, { 400, 375 }, { 349, 412 }, { 297, 375 }, { 316, 314 }, // 6
      { 348, 358 }                                                          // 7
    };

    // How could a benevolent God will this
    private static int[,] lines = new int[,] {
      { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 4 }, { 4, 5 }, { 5, 6 }, { 6, 7 },
      { 7, 8 }, { 8, 9 }, { 9, 10 }, { 10, 11 }, { 11, 12 }, { 12, 13 },
      { 13, 14 }, { 14, 15 }, { 15, 16 }, { 16, 17 }, { 17, 18 }, { 18, 19 },
      { 19, 0 }, { 20, 21 }, { 21, 22, }, { 22, 23 }, { 23, 24 }, { 24, 25 },
      { 25, 26 }, { 26, 27 }, { 27, 28 }, { 28, 29 }, { 29, 30 }, { 30, 31 },
      { 31, 32 }, { 32, 33 }, { 33, 34 }, { 34, 35 }, { 35, 36 }, { 36, 37 },
      { 37, 38 }, { 38, 39 }, { 39, 20 }, { 40, 41 }, { 41, 42 }, { 42, 43 },
      { 43, 44 }, { 44, 45 }, { 45, 46 }, { 46, 47 }, { 47, 48 }, { 48, 49 },
      { 49, 50 }, { 50, 51 }, { 51, 52 }, { 52, 53 }, { 53, 54 }, { 54, 55 },
      { 55, 56 }, { 56, 57 }, { 57, 58 }, { 58, 59 }, { 59, 40 }, { 60, 61 },
      { 61, 62 }, { 62, 63 }, { 63, 64 }, { 64, 65 }, { 65, 66 }, { 66, 67 },
      { 67, 68 }, { 68, 69 }, { 69, 70 }, { 70, 71 }, { 71, 72 }, { 72, 73 },
      { 73, 74 }, { 74, 60 }, { 75, 76 }, { 76, 77 }, { 77, 78 }, { 78, 79 },
      { 79, 80 }, { 80, 81 }, { 81, 82 }, { 82, 83 }, { 83, 84 }, { 84, 75 },
      { 85, 86 }, { 86, 87 }, { 87, 88 }, { 88, 89 }, { 89, 85 }, { 0, 20 },
      { 20, 1 }, { 1, 21 }, { 21, 2 }, { 2, 22 }, { 22, 3 }, { 3, 23 },
      { 23, 4 }, { 4, 24 }, { 24, 5 }, { 5, 25 }, { 25, 6 }, { 6, 26 },
      { 26, 7 }, { 7, 27 }, { 27, 8 }, { 8, 28 }, { 28, 9 }, { 9, 29 },
      { 29, 10 }, { 10, 30 }, { 30, 11 }, { 11, 31 }, { 31, 12 }, { 12, 32 },
      { 32, 13 }, { 13, 33 }, { 33, 14 }, { 14, 34 }, { 34, 15 }, { 15, 35 },
      { 35, 16 }, { 16, 36 }, { 36, 17 }, { 17, 37 }, { 37, 18 }, { 18, 38 },
      { 38, 19 }, { 19, 39 }, { 39, 0 }, { 40, 20 }, { 20, 41 }, { 41, 21 },
      { 21, 42 }, { 42, 22 }, { 22, 43 }, { 43, 23 }, { 23, 44 }, { 44, 24 },
      { 24, 45 }, { 45, 25 }, { 25, 46 }, { 46, 26 }, { 26, 47 }, { 47, 27 },
      { 27, 48 }, { 48, 28 }, { 28, 49 }, { 49, 29 }, { 29, 50 }, { 50, 30 },
      { 30, 51 }, { 51, 31 }, { 31, 52 }, { 52, 32 }, { 32, 53 }, { 53, 33 },
      { 33, 54 }, { 54, 34 }, { 34, 55 }, { 55, 35 }, { 35, 56 }, { 56, 36 },
      { 36, 57 }, { 57, 37 }, { 37, 58 }, { 58, 38 }, { 38, 59 }, { 59, 39 },
      { 39, 40 }, { 40, 60 }, { 60, 41 }, { 41, 61 }, { 61, 42 }, { 61, 43 },
      { 43, 62 }, { 62, 44 }, { 44, 63 }, { 63, 45 }, { 45, 64 }, { 64, 46 },
      { 64, 47 }, { 47, 65 }, { 65, 48 }, { 48, 66 }, { 66, 49 }, { 49, 67 },
      { 67, 50 }, { 67, 51 }, { 51, 68 }, { 68, 52 }, { 52, 69 }, { 69, 53 },
      { 53, 70 }, { 70, 54 }, { 70, 55 }, { 55, 71 }, { 71, 56 }, { 56, 72 },
      { 72, 57 }, { 57, 73 }, { 73, 58 }, { 73, 59 }, { 59, 74 }, { 74, 40 },
      { 75, 60 }, { 60, 76 }, { 76, 61 }, { 76, 62 }, { 62, 77 }, { 77, 63 },
      { 63, 78 }, { 78, 64 }, { 78, 65 }, { 65, 79 }, { 79, 66 }, { 66, 80 },
      { 80, 67 }, { 80, 68 }, { 68, 81 }, { 81, 69 }, { 69, 82 }, { 82, 70 },
      { 82, 71 }, { 71, 83 }, { 83, 72 }, { 72, 84 }, { 84, 73 }, { 84, 74 },
      { 74, 75 }, { 75, 85 }, { 85, 76 }, { 85, 77 }, { 77, 86 }, { 86, 78 },
      { 86, 79 }, { 79, 87 }, { 87, 80 }, { 87, 81 }, { 81, 88 }, { 88, 82 },
      { 88, 83 }, { 83, 89 }, { 89, 84 }, { 89, 75 }, { 90, 85 }, { 90, 86 },
      { 90, 87 }, { 90, 88 }, { 90, 89 }
    };

    private static Tuple<double, double> GetPoint(int index) {
      var point = points[index, 0];
      return new Tuple<double, double>(
        ((double)(points[index, 0] - 12) / 673) * 540 + 20,
        ((double)(points[index, 1] - 21) / 673) * 520 + 20
      );
    }

    private void DrawThread() {
      try {
        while (true) {
          int queueLength = this.config.domeCommandQueue.Count;
          if (queueLength == 0) {
            continue;
          }
          for (int i = 0; i < queueLength; i++) {
            LEDCommand newCommand;
            bool result =
              this.config.domeCommandQueue.TryDequeue(out newCommand);
            if (!result) {
              throw new Exception("Someone else is dequeueing!");
            }
            if (!newCommand.isFlush) {
              this.commandsSinceFlush.Add(newCommand);
              continue;
            }
            foreach (LEDCommand command in this.commandsSinceFlush) {
              this.Dispatcher.Invoke(
                DispatcherPriority.Render,
                (Action)(() => {
                  var line = this.struts[command.strutIndex];
                  var color = Color.FromRgb(
                    (byte)(command.color >> 16),
                    (byte)(command.color >> 8),
                    (byte)command.color
                  );
                  line.Stroke = new SolidColorBrush(color);
                  line.InvalidateVisual();
                }
              ));
            }
            this.commandsSinceFlush.Clear();
          }
        }
      } catch (ThreadAbortException) { }
    }

  }

}