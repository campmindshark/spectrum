using System;
using System.Collections.Generic;

namespace BarGeometry {

  [Serializable]
  public class IcosahedronModel :Shape {

    public readonly int LedsPerStrip;
    
    public static List<Vertex> ManualVerticies { get; set; }
    public static List<Side> ManualSides { get; set; }
    public static List<Triangle> Triangles { get; set; }

    public IcosahedronModel(byte startOpcChannel) : base(startOpcChannel) {//int stripCount, int LedsPerStrip, int opcChannels, int maxWiredStripLength, int vertexCount, int sideCount, int faceCount) {
      //new IcosahedronModel(60, 5, 1, 300, 12, 30, 20);

      AddOpcChannel(startOpcChannel, new[] {
          new Strip(LedsPerStrip,0, true, 0, 0, 2),
          new Strip(LedsPerStrip,1, true, 0, 2, 0),
          new Strip(LedsPerStrip,2, true, 1, 0, 3),
          new Strip(LedsPerStrip,3, true, 1, 3, 0),
          new Strip(LedsPerStrip,4, true, 2, 0, 4),
          new Strip(LedsPerStrip,5, true, 2, 4, 0), //5
          new Strip(LedsPerStrip,6, true, 3, 0, 5),
          new Strip(LedsPerStrip,7, true, 3, 5, 0),
          new Strip(LedsPerStrip,8, true, 4, 0, 1),
          new Strip(LedsPerStrip,9, true, 5, 1, 5),
          new Strip(LedsPerStrip,10, true, 5, 5, 1), //10
          new Strip(LedsPerStrip,11, true, 6, 1, 6),
          new Strip(LedsPerStrip,12, true, 6, 6, 1),
          new Strip(LedsPerStrip,13, true, 7, 1, 7),
          new Strip(LedsPerStrip,14, true, 7, 7, 1),
          new Strip(LedsPerStrip,15, true, 8, 1, 2), //15
          new Strip(LedsPerStrip,16, true, 9, 2, 7),
          new Strip(LedsPerStrip,17, true, 9, 7, 2),
          new Strip(LedsPerStrip,18, true, 10, 2, 8),
          new Strip(LedsPerStrip,19, true, 24, 8, 7),
          new Strip(LedsPerStrip,20, true, 24, 7, 8), //20
          new Strip(LedsPerStrip,21, true, 25, 8, 11),
          new Strip(LedsPerStrip,22, true, 25, 11, 8),
          new Strip(LedsPerStrip,23, true, 26,8, 9),
          new Strip(LedsPerStrip,24, true, 26, 9, 8),
          new Strip(LedsPerStrip,25, true, 12, 8, 3), //25
          new Strip(LedsPerStrip,26, true, 11, 3, 2),
          new Strip(LedsPerStrip,27, true, 11, 2, 3),
          new Strip(LedsPerStrip,28, true, 14, 3, 4),
          new Strip(LedsPerStrip,29, true, 14, 4, 3),
          new Strip(LedsPerStrip,30, true, 13, 3, 9), //30
          new Strip(LedsPerStrip,31, true, 15, 9, 4),
          new Strip(LedsPerStrip,32, true, 15, 4, 9),
          new Strip(LedsPerStrip,33, true, 28, 9, 10),
          new Strip(LedsPerStrip,34, true, 28, 10, 9),
          new Strip(LedsPerStrip,35, true, 27, 9, 11), //35
          new Strip(LedsPerStrip,36, true, 29,11, 10),
          new Strip(LedsPerStrip,37, true, 29,10, 11),
          new Strip(LedsPerStrip,38, true, 21,11, 6),
          new Strip(LedsPerStrip,39, true, 21, 6, 11),
          new Strip(LedsPerStrip,40, true, 23, 11, 7), //40
          new Strip(LedsPerStrip,41, true, 22, 7, 6),
          new Strip(LedsPerStrip,42, true, 20, 6, 10),
          new Strip(LedsPerStrip,43, true, 20, 10, 6),
          new Strip(LedsPerStrip,44, true, 19, 6, 5),
          new Strip(LedsPerStrip,45, true, 18, 5, 10), //45
          new Strip(LedsPerStrip,46, true, 16, 10, 4),
          new Strip(LedsPerStrip,47, true, 17, 4, 5),
          new Strip(LedsPerStrip,48, true, 17, 5, 4),
          new Strip(LedsPerStrip,49, true, 16, 4, 10),
          new Strip(LedsPerStrip,50, true, 18, 10, 5), //50
          new Strip(LedsPerStrip,51, true, 19, 5, 6),
          new Strip(LedsPerStrip,52, true, 22, 6, 7),
          new Strip(LedsPerStrip,53, true, 23, 7, 11),
          new Strip(LedsPerStrip,54, true, 27, 11, 9),
          new Strip(LedsPerStrip,55, true, 13, 9, 3), //55
          new Strip(LedsPerStrip,56, true, 12, 3, 8),
          new Strip(LedsPerStrip,57, true, 10, 8, 2),
          new Strip(LedsPerStrip,58, true, 8, 2, 1),
          new Strip(LedsPerStrip,59, true, 4, 1, 0)
        });
      ManualVerticies = new List<Vertex>(){
          new Vertex(0, new[] {0,1,2,3,4}),
          new Vertex(1, new[] {4,5,6,7,8}),
          new Vertex(2, new[] {0,8,9,10,11}),
          new Vertex(3, new[] {1,11,12,13,14}),
          new Vertex(4, new[] {2,14,15,16,17}),
          new Vertex(5, new[] {3,5,17,18,19}),
          new Vertex(6, new[] {6,19,20,21,22}),
          new Vertex(7, new[] {7,9,22,23,24}),
          new Vertex(8, new[] {10,12,24,25,26}),
          new Vertex(9, new[] {13,15,26,27,28}),
          new Vertex(10, new[] {16,18,20,28,29}),
          new Vertex(11, new[] {21,23,25,27,29})
        };
      ManualSides = new List<Side>() {
          new Side(new[]{0, 1}),
          new Side(new[]{2, 3}),
          new Side(new[]{4, 5}),
          new Side(new[]{6, 7}),
          new Side(new[]{8, 59}),
          new Side(new[]{9, 10}), //5
          new Side(new[]{11, 12}),
          new Side(new[]{13, 14}),
          new Side(new[]{15, 58}),
          new Side(new[]{16, 17}),
          new Side(new[]{18, 57}), //10
          new Side(new[]{26, 27}),
          new Side(new[]{56, 25}),
          new Side(new[]{55, 30}),
          new Side(new[]{29, 28}),
          new Side(new[]{31, 32}), //15
          new Side(new[]{49, 46}),
          new Side(new[]{47, 48}),
          new Side(new[]{50, 45}),
          new Side(new[]{44, 51}),
          new Side(new[]{43, 42}), //20
          new Side(new[]{38, 39}),
          new Side(new[]{41, 52}),
          new Side(new[]{40, 53}),
          new Side(new[]{19, 20}),
          new Side(new[]{21, 22}),//25
          new Side(new[]{23, 24}),
          new Side(new[]{54, 35}),
          new Side(new[]{34, 33}),
          new Side(new[]{36, 37})
        };
      Triangles = new List<Triangle>() {
          new Triangle(new []{0, 11, 1}),
          new Triangle(new []{1, 14, 2}),
          new Triangle(new []{2, 17, 3}),
          new Triangle(new []{3, 5, 4}),
          new Triangle(new []{4, 8, 0}),
          new Triangle(new []{8, 7, 9}),
          new Triangle(new []{9, 24, 10}),
          new Triangle(new []{10, 12, 11}),
          new Triangle(new []{6, 5, 19}),
          new Triangle(new []{6, 22, 7}),
          new Triangle(new []{12, 13, 26}),
          new Triangle(new []{13, 14, 15}),
          new Triangle(new []{15, 28, 16}),
          new Triangle(new []{16, 17, 18}),
          new Triangle(new []{18, 19, 20}),
          new Triangle(new []{20, 21, 29}),
          new Triangle(new []{21, 22, 23}),
          new Triangle(new []{23, 24, 25}),
          new Triangle(new []{25, 27, 26}),
          new Triangle(new []{27, 28, 29}),
        };
      SetFeatures();
    }
    public int[] GetVerticiesFromSide(int sideId) {
      var strip = Strips[Sides[sideId].StripIds[0]];
      return new[] {
         strip.StartVertexId,
         strip.EndVertexId
      };
    }
  }
}
