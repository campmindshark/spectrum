using Spectrum.Base;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace BarGeometry {
  public abstract class Shape {
    public List<Side> Sides { get; private set; } = new List<Side>();
    public List<Vertex> StartVerticies { get; private set; } = new List<Vertex>();//Strips that Start at a Vertex
    public List<Vertex> EndVerticies { get; private set; } = new List<Vertex>();//Strips that end at a Vertex
    public List<Vertex> Vertices { get; private set; } = new List<Vertex>(); // Strips that touch a vertex, start or end


    private List<Strip> _strips;
    public List<Strip> Strips //Master List of Strips. 
      {
      get {
        if (_strips == null) {
          _strips = new List<Strip>();
        }
        return _strips;
      }
      set {
        if (value != _strips) {
          if (_strips == null) {
            _strips = new List<Strip>(value);
          } else {
            _strips = value;
          }

        }
      }
    }
    public void SetFeatures() {
      SetSides();
      SetVerticies();
    }
    private void SetSides() {
      var stripIds = (from strip in _strips
                      group strip.StripId by strip.SideId into g

                      select g.ToArray()).ToList();

      for (int i = 0; i < stripIds.Count; i++) {
        Sides.Add(new Side(i, stripIds[i]));
      }
    }
    private void SetVerticies() {
      //Start Fresh
      StartVerticies.Clear();
      EndVerticies.Clear();
      Vertices.Clear();

     //Todo: build vertex look up objects from Strip data
    }

    public int OPCStartChannel; //This Shape's first pin on the NatShip
    public List<OpcChannel> Channels;//Index + OPCStartChannel =  NatShip Channel
    public int MaxOPCStripLength {
      get {
        int max = 0;
        foreach (var channel in Channels) {
          int channelCount = GetLedCountForChannel(channel.OPCChannelId - OPCStartChannel);
          max = max < channelCount ? channelCount : max;
        }
        return max;
      }
    }

    public Shape(byte startChannel) {
      OPCStartChannel = startChannel;

    }

    /// <summary>
    /// Add an OpcChannel and the strips attached to it. 
    /// </summary>
    /// <param name="channelId">NatShip/OPC Channel Id</param>
    /// <param name="strips"></param>
    public void AddOpcChannel(byte channelId, Strip[] strips) {
      int nextStripId = Strips.Count;

      //Create the Channel
      OpcChannel channel = new OpcChannel(channelId, nextStripId, strips.Length);
      //Add this channels strips
      Strips.AddRange(strips);
    }

    private int GetLedCountForChannel(int channelId) {
      OpcChannel channel = Channels[channelId];
      int count = 0;
      for (int i = channel.StartStripId; i < channel.StartStripId + channel.StripCount; i++) {
        count += Strips[i].LedCount;
      }

      return count;
    }
  }

  /// <summary>
  /// 
  /// </summary>
  public class OpcChannel {
    public byte OPCChannelId; //LEDScape Channel
    public int StartStripId;
    public int StripCount;
    public OpcChannel(byte channelId, int startStripId, int stripCount) {
      OPCChannelId = channelId;
      StartStripId = startStripId;
      StripCount = stripCount;
    }
  }

  /// <summary>
  /// An LED Strip
  /// </summary>
  public class Strip {
    public int StripId;
    public bool IsInside; // True if this strip is inside the shape.
    public int SideId; // upto 4 strips per side, 2 inside, 2 out
    public int LedCount;
    public LEDColor[] Colors;

    public int StartVertexId;
    public int EndVertexId;

    public Strip(int stripId, int ledCount, bool inside, int sideId, int? startVertexId = null, int? endVertexId = null, LEDColor[] colors = null) {
      StripId = stripId;
      LedCount = ledCount;
      IsInside = inside;
      Colors = colors ?? GetDefaultColors();
      StartVertexId = startVertexId ?? -1;
      EndVertexId = endVertexId ?? -1;
    }

    private LEDColor[] GetDefaultColors() {
      return new[] {
          new LEDColor(Color.IndianRed.GetHashCode(), Color.BlanchedAlmond.GetHashCode()),
          new LEDColor(Color.HotPink.GetHashCode(), Color.LightGoldenrodYellow.GetHashCode()),
          new LEDColor(Color.Lime.GetHashCode(), Color.MediumAquamarine.GetHashCode()),
          new LEDColor(Color.Magenta.GetHashCode(), Color.Olive.GetHashCode()),
          new LEDColor(Color.Purple.GetHashCode(), Color.NavajoWhite.GetHashCode())
        };
    }
  }

  public enum ShapeType {
    Icosahedron,
    Octahedron
  }
}



