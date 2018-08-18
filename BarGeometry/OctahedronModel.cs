namespace BarGeometry {
  public class OctahedronModel : Shape {
    public OctahedronModel(byte startChannel) : base(startChannel) { 
      byte opcChannelVerticalLoop0 = startChannel;
      byte opcChannelVerticalLoop1 = (byte)(startChannel + 1);
      byte opcChannelHorizontalLoop = (byte)(startChannel + 3);

      AddOpcChannel(opcChannelVerticalLoop0, new[] {
       new Strip(0, 12, false, 0, 0, 1),
       new Strip(1, 12, false, 1, 1, 2),
       new Strip(2,  12, false, 2, 2, 3),
       new Strip(3, 12, false, 3, 3, 0),
       new Strip(4,12, false, 0, 0, 1),
       new Strip(5,12, false, 1, 1, 2),
       new Strip(6, 12, false, 2, 2, 3),
       new Strip(7, 12, false, 3, 3, 0),
       new Strip(8, 12, true , 0, 0, 1),
       new Strip(9, 12, true, 1, 1, 2),
       new Strip(10, 12, true, 2, 2, 3),
       new Strip(11, 12, true, 3, 3, 0),
       new Strip(12, 12, true , 0, 0, 1),
       new Strip(13, 12, true, 1, 1, 2),
       new Strip(14, 12, true, 2, 2, 3),
       new Strip(15, 12, true, 3, 3, 0)
       });
      AddOpcChannel(opcChannelVerticalLoop1, new[] {
       new Strip(16, 12, false, 4, 0, 4),
       new Strip(17, 12, false, 5, 4, 2),
       new Strip(18,  12, false, 6, 2, 5),
       new Strip(19,  12, false, 7, 5, 0),
       new Strip(20, 12, false, 4, 0, 4),
       new Strip(21, 12, false, 5, 4, 2),
       new Strip(22, 12, false, 6, 2, 5),
       new Strip(23, 12, false, 7, 5, 0),
       new Strip(24, 12, true , 4, 4),
       new Strip(25, 12, true, 5, 2),
       new Strip(26, 12, true, 6, 5),
       new Strip(27, 12, true, 7, 0),
       new Strip(28, 12, true , 4, 4),
       new Strip(29, 12, true, 5, 2),
       new Strip(30, 12, true, 6, 5),
       new Strip(31, 12, true, 7, 0)
      });
      AddOpcChannel(opcChannelHorizontalLoop, new[] {
        new Strip(32, 8, false, 8, 5, 1),
        new Strip(33, 8, false, 9, 1, 4),
        new Strip(34, 8, false, 10, 4, 2),
        new Strip(35, 8, false, 11, 2, 5),
        new Strip(36, 8, false, 8,5,1),
        new Strip(37, 8, false, 9,1,4),
        new Strip(38, 8, false, 10, 4,2),
        new Strip(39, 8, false, 11, 2, 5),
        new Strip(40, 8, true,  8, 5, 1),
        new Strip(41, 8, true,  9, 1, 4),
        new Strip(42, 8, true,  10, 4, 2),
        new Strip(43, 8, true,  11, 2, 5),
        new Strip(44, 8, true,  8,5,1),
        new Strip(45, 8, true,  9,1,4),
        new Strip(46, 8, true,  10, 4,2),
        new Strip(47, 8, true,  11, 2, 5)
        });
      SetFeatures();
    }
  }
}
