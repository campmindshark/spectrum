namespace BarGeometry {
  public class Triangle {
    public int[] SideIds { get; set; } = new int[3];
    public Triangle(int[] sideIds) {
      SideIds = sideIds;
    }

  }
  public class Side {
    public int SideId;
    public int[] StripIds { get; set; } = new int[2];
    public Side(int[] stripIds) {
      StripIds = stripIds;
    }
    public Side(int sideId, int[] stripIds) {
      SideId = sideId;
      StripIds = stripIds;
    }
  }
  /// <summary>
  /// A Vertex on the Shape 
  /// </summary>
  public class Vertex {
    public int[] SideIds { get; set; } = new int[5];
    public Vertex(int[] sideIds) {
      SideIds = sideIds;
    }
  }
}
