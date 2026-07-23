using System;
using System.Diagnostics.CodeAnalysis;

namespace Spectrum {

  // Consistent Overhead Byte Stuffing (COBS) decoder for the wand serial framing.
  //
  // The ESP32 receiver frames each wand struct as
  //   COBS(struct ‖ CRC8(struct)) ‖ 0x00
  // and emits the encoded bytes followed by a 0x00 delimiter over USB CDC. The
  // caller splits the raw byte stream on 0x00 and hands each delimiter-stripped
  // run here; the encoded input therefore contains no 0x00 bytes itself.
  public static class CobsCodec {

    // Smallest decoded frame we accept: the 6-byte deviceType-5 heartbeat plus
    // its 1 CRC byte. This floor exists so the caller's `payload = decoded[..^1]`
    // and CRC-index arithmetic can never go negative on a runt frame.
    //
    // NOTE: 7 is heartbeat-sized, NOT wand-sized. Do not tighten this to a wand's
    // 15+1 — the heartbeat (which drives the receiver-alive indicator) is the
    // smallest legitimate frame, and raising the floor would silently drop every
    // heartbeat.
    private const int MinDecodedLength = 7;

    // Standard COBS decode. Returns false (with decoded = null) for a malformed
    // frame — an overlong code jump that runs past the end of the input — or for
    // a decoded length below MinDecodedLength.
    public static bool TryDecode(
      ReadOnlySpan<byte> encoded,
      [NotNullWhen(true)] out byte[]? decoded
    ) {
      decoded = null;
      if (encoded.Length == 0) {
        return false;
      }

      // Worst case the decode is exactly as long as the input; allocate that and
      // track the real length.
      var output = new byte[encoded.Length];
      int outLen = 0;

      int i = 0;
      while (i < encoded.Length) {
        byte code = encoded[i];
        if (code == 0) {
          // A 0x00 inside an encoded run is illegal COBS (the delimiter is
          // stripped by the caller).
          return false;
        }
        i++;
        int copy = code - 1;
        if (i + copy > encoded.Length) {
          // The code jumps past the end of the frame: truncated / corrupt.
          return false;
        }
        for (int j = 0; j < copy; j++) {
          output[outLen++] = encoded[i++];
        }
        // A code < 0xFF encodes an implicit 0x00 separating the next group,
        // except after the final group (when we've consumed all input).
        if (code != 0xFF && i < encoded.Length) {
          output[outLen++] = 0x00;
        }
      }

      if (outLen < MinDecodedLength) {
        return false;
      }

      decoded = new byte[outLen];
      Array.Copy(output, decoded, outLen);
      return true;
    }
  }
}
