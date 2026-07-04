using System;

namespace Spectrum {

  // CRC-8/SMBus (a.k.a. CRC-8, poly 0x07, init 0x00, no input/output reflection,
  // no final xor). This is the variant the ESP32 receiver firmware appends to
  // each wand struct before COBS-encoding it. Kept as a tiny, well-commented
  // bitwise loop so the variant is trivial to change if the firmware differs:
  // to switch variants, adjust the polynomial, the initial value, and (if the
  // firmware reflects) reverse the bit order — but as long as the receiver uses
  // CRC-8/SMBus this matches byte-for-byte.
  public static class Crc8 {

    private const byte Polynomial = 0x07;

    public static byte Compute(ReadOnlySpan<byte> data) {
      byte crc = 0x00; // init
      foreach (byte b in data) {
        crc ^= b;
        for (int bit = 0; bit < 8; bit++) {
          // MSB-first (no reflection): shift left, xor the poly when the top
          // bit was set before the shift.
          if ((crc & 0x80) != 0) {
            crc = (byte)((crc << 1) ^ Polynomial);
          } else {
            crc = (byte)(crc << 1);
          }
        }
      }
      return crc; // no final xorout
    }
  }
}
