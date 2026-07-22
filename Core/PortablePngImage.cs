using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Spectrum {

  /**
   * Minimal, dependency-free decoder for the 8-bit, non-interlaced RGB/RGBA
   * PNG assets used by portable renderers. It deliberately rejects formats the
   * runtime does not ship rather than silently decoding them incorrectly.
   */
  internal sealed class PortablePngImage {
    private static readonly byte[] Signature = {
      0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
    };

    private PortablePngImage(int width, int height, byte[] rgb) {
      this.Width = width;
      this.Height = height;
      this.Rgb = rgb;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Rgb { get; }

    public static PortablePngImage Load(Stream stream) {
      if (stream == null) {
        throw new ArgumentNullException(nameof(stream));
      }

      Span<byte> signature = stackalloc byte[Signature.Length];
      stream.ReadExactly(signature);
      if (!signature.SequenceEqual(Signature)) {
        throw new InvalidDataException("The texture is not a PNG image.");
      }

      int width = 0;
      int height = 0;
      int bytesPerPixel = 0;
      bool sawHeader = false;
      bool sawEnd = false;
      using var compressed = new MemoryStream();
      Span<byte> chunkHeader = stackalloc byte[8];
      Span<byte> crc = stackalloc byte[4];
      while (!sawEnd) {
        stream.ReadExactly(chunkHeader);
        uint chunkLength = BinaryPrimitives.ReadUInt32BigEndian(
          chunkHeader[..4]);
        if (chunkLength > int.MaxValue) {
          throw new InvalidDataException("PNG chunk is too large.");
        }

        string chunkType = System.Text.Encoding.ASCII.GetString(
          chunkHeader[4..]);
        var chunk = new byte[(int)chunkLength];
        stream.ReadExactly(chunk);
        stream.ReadExactly(crc);

        switch (chunkType) {
          case "IHDR":
            if (sawHeader || chunk.Length != 13) {
              throw new InvalidDataException("PNG has an invalid IHDR chunk.");
            }
            width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
              chunk.AsSpan(0, 4)));
            height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
              chunk.AsSpan(4, 4)));
            if (width <= 0 || height <= 0 || chunk[8] != 8 ||
                (chunk[9] != 2 && chunk[9] != 6) || chunk[10] != 0 ||
                chunk[11] != 0 || chunk[12] != 0) {
              throw new InvalidDataException(
                "Only non-interlaced 8-bit RGB/RGBA PNG images are supported.");
            }
            bytesPerPixel = chunk[9] == 2 ? 3 : 4;
            sawHeader = true;
            break;
          case "IDAT":
            if (!sawHeader) {
              throw new InvalidDataException("PNG IDAT preceded IHDR.");
            }
            compressed.Write(chunk);
            break;
          case "IEND":
            sawEnd = true;
            break;
        }
      }

      if (!sawHeader || compressed.Length == 0) {
        throw new InvalidDataException("PNG has no image data.");
      }

      int rowBytes = checked(width * bytesPerPixel);
      int decodedLength = checked((rowBytes + 1) * height);
      var filtered = new byte[decodedLength];
      compressed.Position = 0;
      using (var inflater = new ZLibStream(
          compressed, CompressionMode.Decompress, leaveOpen: true)) {
        inflater.ReadExactly(filtered);
        if (inflater.ReadByte() != -1) {
          throw new InvalidDataException(
            "PNG decompressed beyond the declared dimensions.");
        }
      }

      var raw = new byte[checked(rowBytes * height)];
      for (int y = 0; y < height; y++) {
        int filteredRow = y * (rowBytes + 1);
        int outputRow = y * rowBytes;
        byte filter = filtered[filteredRow];
        for (int x = 0; x < rowBytes; x++) {
          byte encoded = filtered[filteredRow + 1 + x];
          byte left = x >= bytesPerPixel
            ? raw[outputRow + x - bytesPerPixel]
            : (byte)0;
          byte above = y > 0 ? raw[outputRow - rowBytes + x] : (byte)0;
          byte aboveLeft = y > 0 && x >= bytesPerPixel
            ? raw[outputRow - rowBytes + x - bytesPerPixel]
            : (byte)0;
          raw[outputRow + x] = filter switch {
            0 => encoded,
            1 => unchecked((byte)(encoded + left)),
            2 => unchecked((byte)(encoded + above)),
            3 => unchecked((byte)(encoded + ((left + above) >> 1))),
            4 => unchecked((byte)(encoded + Paeth(
              left, above, aboveLeft))),
            _ => throw new InvalidDataException(
              "PNG uses an unknown scanline filter."),
          };
        }
      }

      if (bytesPerPixel == 3) {
        return new PortablePngImage(width, height, raw);
      }

      var rgb = new byte[checked(width * height * 3)];
      for (int source = 0, destination = 0;
          source < raw.Length;
          source += 4, destination += 3) {
        // The Earth texture is opaque. For RGBA assets, composite against
        // black so callers continue to receive the same packed RGB contract.
        int alpha = raw[source + 3];
        rgb[destination] = (byte)(raw[source] * alpha / 255);
        rgb[destination + 1] = (byte)(raw[source + 1] * alpha / 255);
        rgb[destination + 2] = (byte)(raw[source + 2] * alpha / 255);
      }
      return new PortablePngImage(width, height, rgb);
    }

    private static byte Paeth(byte left, byte above, byte aboveLeft) {
      int estimate = left + above - aboveLeft;
      int leftDistance = Math.Abs(estimate - left);
      int aboveDistance = Math.Abs(estimate - above);
      int diagonalDistance = Math.Abs(estimate - aboveLeft);
      if (leftDistance <= aboveDistance &&
          leftDistance <= diagonalDistance) {
        return left;
      }
      return aboveDistance <= diagonalDistance ? above : aboveLeft;
    }
  }
}
