using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.IO;
//using static Stage.Utilities;
using System.Drawing;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace Stage
{
    /// <summary>
    /// A set of LED arrangements that individually react to general animation commands
    /// </summary>
    public class LEDInstallation
    {
        /// <summary>
        /// Key: Point, Value: LED
        /// </summary>
        private static Dictionary<Point, LED> _pixelDictionary;
        public static Dictionary<Point, LED> PixelDictionary
        {
            get
            {
                if (_pixelDictionary == null)
                {
                    _pixelDictionary = new Dictionary<Point, LED>();
                    ClearWindow();
                }
                return _pixelDictionary;
            }
            set
            {
                _pixelDictionary = value;
            }
        }

        #region Bitmap
        public static int SimulationWindowHeight;
        public static int SimulationWindowWidth;
        //4 bytes per pixel , bgra
        private static int ByteCount => SimulationWindowHeight * SimulationWindowWidth * 4;

        /// <summary>
        /// fro WritePixels.
        /// </summary>
        private static byte[] _pixels = null;
        public static byte[] Pixels
        {
            get
            {
                if (_pixels == null)
                {
                    LEDInstallation.ClearWindow();
                }
                return _pixels;
            }
            set
            {
                _pixels = value;
            }
        }
        
        /// <summary>
        /// Palette for Rendering
        /// </summary>
        public static BitmapPalette Palette { get => new BitmapPalette(LEDInstallation.ColorPalette); }

        /// <summary>
        /// Source for Image in xaml
        /// </summary>
        public static WriteableBitmap GetBitmap()
        {
            WriteableBitmap map = new WriteableBitmap(1000, 750, 96, 96, PixelFormats.Bgr32, Palette);
            Pixels = GetPixels();
            map.WritePixels(new System.Windows.Int32Rect(0, 0, SimulationWindowWidth, SimulationWindowHeight), Pixels, SimulationWindowWidth * 4, 0);
            return map;
        }
        #endregion

        /// <summary>
        /// Color Palette
        /// </summary>
        private static List<System.Windows.Media.Color> _colors;
        public static List<System.Windows.Media.Color> ColorPalette
        {
            get
            {
                if (_colors == null)
                {
                    _colors = new List<System.Windows.Media.Color>(){
                        Colors.Black,
                        Colors.White,
                        Colors.Tomato,
                        Colors.Red,
                        Colors.Purple,
                        Colors.Pink
                    };
                }
                return _colors;
            }
            set
            {
                _colors = value;
            }
        }

        public const double ScalingFactor = 100.0;
        
        /// <summary>
        /// Throw me to Notify the Simulator of an update the the Bitmap.
        /// </summary>
        //public event EventHandler UpdateStage;

        /// <summary>
        /// Use this to translate the whole array in the simulator
        /// </summary>
        public Point Origin { get; set; }

        /// <summary>
        /// Arbitrary set of Shapes that make up this installation
        /// </summary>
        public List<LightShape> Shapes;

        public LEDInstallation(Point? origin = null, Dictionary<Point, LED> pixels = null, List<LightShape> shapes = null, List<System.Windows.Media.Color> colors = null, int? windowHeight = null, int? windowWidth = null)
        {
            Origin = origin ?? new Point(0, 0);
            Shapes = shapes ?? GetDefaultShapes();
            ColorPalette = colors;
            PixelDictionary = pixels ?? new Dictionary<Point, LED>(); // TODO: build this from Shapes
            SimulationWindowHeight = windowHeight ?? 750;
            SimulationWindowWidth = windowWidth ?? 500;

            ClearWindow();
        }

        private List<LightShape> GetDefaultShapes()
        {
            var defaultShapes = new List<LightShape>();
            var point = Origin;
            for (int i = 0; i < 4; i++)
            {
                defaultShapes.Add(new LightShape(point));
                //translate the next shape's origin 100px horizontally and vertically
                point.X += 100;
                point.Y += 100;
            }
            return defaultShapes;
        }

        #region Rendering Work

        private static byte[] GetPixels()
        {
            var pixels = new byte[ByteCount];

            foreach (var pixel in PixelDictionary.Values)
            {
                var pos = pixel.Origin.Y * SimulationWindowWidth + pixel.Origin.X;
                pixels[pos] = pixel.CurrentColor.B;
                pixels[pos + 1] = pixel.CurrentColor.G;
                pixels[pos + 2] = pixel.CurrentColor.R;
                pixels[pos + 3] = pixel.CurrentColor.A;
                pixel.isDirty = false;
            }

            return pixels;
        }


        /// <summary>
        /// Rebuild Pixel Dictionary, Turn off all LEDs
        /// </summary>
        private static void ClearWindow()
        {
            PixelDictionary = new Dictionary<Point, LED>();
            for (int x = 0; x < SimulationWindowWidth; x++)
            {
                for (int y = 0; y < SimulationWindowHeight; y++)
                {
                    var origin = new Point(x, y);
                    PixelDictionary[origin] = new LED(origin, Colors.Black);
                    //PixelDictionary.Add(origin, new LED(origin, System.Windows.Media.Colors.MediumVioletRed));
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// an arrangement of LEDs that receive animation commands as a group
    /// </summary>
    /// 
    public class LightShape
    {
        /// <summary>
        /// Physical Origin of this LightShape
        /// </summary>
        public Point Origin { get; set; }

        /// <summary>
        /// # of sides to divide LED strip into
        /// </summary>
        public int SideCount { get; set; }

        /// <summary>
        /// depth of the LightShape
        /// </summary>
        public int LayerCount { get; set; }

        /// <summary>
        /// Longest Strip length for the layers of this LightShape
        /// </summary>
        public int StripLength { get; set; }

        /// <summary>
        /// Sequence of LightShapeSegments
        /// </summary>
        public OrderedDictionary Segments { get; set; }

        /// <summary>
        /// Key:Layer Value: Segments
        /// </summary>
        public Dictionary<int, OrderedDictionary> Layers { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public LightShape(Point? origin = null
            , int? sideCount = null
            , int? layerCount = null
            , int? stripLength = null
            , OrderedDictionary segments = null)
        {
            // Defaults are a triforce triangle at 0,0
            Origin = origin ?? new Point(0, 0);
            SideCount = sideCount ?? 3;
            LayerCount = layerCount ?? 3;
            StripLength = stripLength ?? 48;
            Segments = segments ?? GetDefaultSegments();
            Layers = BuildLayers();
        }

        /// <summary>
        /// Repeat the configured Segments
        /// </summary>
        /// <returns></returns>
        private Dictionary<int, OrderedDictionary> BuildLayers()
        {
            Dictionary<int, OrderedDictionary> dict = new Dictionary<int, OrderedDictionary>();
            for (int i = 0; i < LayerCount; i++)
            {
                dict.Add(i, Segments);
            }
            return dict;
        }

        private OrderedDictionary GetDefaultSegments()
        {
            Point point = Origin;
            OrderedDictionary od = new OrderedDictionary();
            for (int i = 0; i < 4; i++)
            {
                od.Add(point, new LightShapeSegment(origin:point));
                //translate the next shape's origin 10px horizontally and vertically
                point.X += 10;
                point.Y += 10;
            }
            return od;
        }
    }

    /// <summary>
    /// A Segment of LEDs
    /// </summary>
    public class LightShapeSegment
    {
        /// <summary>
        /// It's all lines for now
        /// </summary>
        //public SegmentType Type = SegmentType.Line;

        /// <summary>
        /// LEDs per meter
        /// </summary>
        public readonly int LEDDensity;

        /// <summary>
        /// # of LEDs on this segment
        /// </summary>
        public int Length { get; set; }

        /// <summary>
        /// Angle from level
        /// </summary>
        public double Angle { get; set; }

        /// <summary>
        /// the physical location of the LED that marks the start of this segment
        /// </summary>
        public readonly Point Origin;

        private Point _endPoint;
        public Point EndPoint
        {
            get
            {
                if (_endPoint == null)
                {
                    var segmentLength = LEDInstallation.ScalingFactor * (Length / LEDDensity);
                    var slope = Math.Tan(Angle * (Math.PI / 180));

                    var deltaX = Math.Sqrt(Math.Pow(segmentLength, 2) / (slope + 1));
                    var deltaY = slope * deltaX;

                    _endPoint = new Point((int)(Origin.X + deltaX), (int)(Origin.Y + deltaY));
                }
                return _endPoint;
            }
        }
        public List<LED> LEDs;

        public LightShapeSegment(int? density = null, Point? origin = null, int? length = null, double? angle = null, List<LED> leds = null)
        {
            LEDDensity = density ?? 30;
            Origin = origin ?? new Point(0, 0);
            Length = length ?? 16;
            Angle = angle ?? 60;
            LEDs = leds ?? GetDefaultLEDs();

        }

        private List<LED> GetDefaultLEDs()
        {
            Point point = Origin;
            double slope = Math.Tan(Angle * (Math.PI / 180.0));
            int deltaX, deltaY;
            List<LED> defaults = new List<LED>();

            var ledLength = LEDInstallation.ScalingFactor * (1.0 / LEDDensity);

            //We have to lose percision somewhere... 
            deltaX = (int)(Math.Sqrt(Math.Pow(ledLength, 2) / (slope + 1)));
            deltaY = (int)(slope * deltaX);

            for (int i = 0; i < Length; i++)
            {
                defaults.Add(new LED(point, Colors.White));
                //Move point to the next LED
                point.X += deltaX;
                point.Y += deltaY;
            }

            return defaults;
        }
    }

    /// <summary>
    /// It's an LED!
    /// </summary>
    public class LED
    {
        /// <summary>
        /// Configured Origin, shouldn't change
        /// </summary>
        public Point Origin { get; set; }

        public System.Windows.Media.Color CurrentColor { get; set; }

        /// <summary>
        /// indicates the type of color change (to the current color in Pixels)
        /// </summary>
        public bool isDirty { get; set; }

        public LED(Point point, System.Windows.Media.Color color)
        {
            Origin = point;
            CommandLED(LEDCommand.Replace, color);
        }

        /// <summary>
        /// Process a change to an LED
        /// </summary>
        /// <param name="command"></param>
        /// <param name="color"></param>
        public void CommandLED(LEDCommand command, System.Windows.Media.Color color)
        {
            // check the command type, update color according,  add led change to list.
            switch (command)
            {
                case LEDCommand.Replace:
                    CurrentColor = color;
                    isDirty = true;
                    DrawLED();
                    break;
                case LEDCommand.Add:
                    CurrentColor += color;
                    isDirty= true;
                    DrawLED();
                    break;
                case LEDCommand.Subtract:
                    CurrentColor -= color;
                    isDirty = true;
                    DrawLED();
                    break;
                default:
                    break;
            }

        }

        /// <summary>
        /// Add/Update this LED to the PixelDictionary
        /// </summary>
        public void DrawLED()
        {
            if (LEDInstallation.PixelDictionary.ContainsKey(Origin))
            {
                LEDInstallation.PixelDictionary[Origin] = this;
            }
            else
            {
                LEDInstallation.PixelDictionary.Add(Origin, this);
            }
        }
    }

    public enum LEDCommand
    {
        Replace,
        Add,
        Subtract,
        None
    }
}
