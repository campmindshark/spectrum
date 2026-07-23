using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Spectrum.Base;

namespace Spectrum.Platform.Linux {

  internal interface IAlsaCapture : IDisposable {
    int Channels { get; }
    int Read(short[] samples);
  }

  internal interface IAlsaApi {
    IReadOnlyList<AudioCaptureDevice> GetCaptureDevices();
    IAlsaCapture OpenCapture(string deviceId, int sampleRate, int framesPerRead);
  }

  internal sealed class AlsaApi : IAlsaApi {
    private const string AlsaLibrary = "libasound.so.2";
    private const int CaptureStream = 1;
    private const int ReadWriteInterleaved = 3;
    private const int Signed16LittleEndian = 2;
    private const int TargetLatencyMicroseconds = 50000;

    public IReadOnlyList<AudioCaptureDevice> GetCaptureDevices() {
      int result = snd_device_name_hint(-1, "pcm", out IntPtr hints);
      ThrowOnError(result, "enumerate ALSA PCM devices");
      var devices = new Dictionary<string, AudioCaptureDevice>(
        StringComparer.Ordinal);
      try {
        for (int offset = 0; ; offset += IntPtr.Size) {
          IntPtr hint = Marshal.ReadIntPtr(hints, offset);
          if (hint == IntPtr.Zero) {
            break;
          }
          string? id = ReadHint(hint, "NAME");
          string? direction = ReadHint(hint, "IOID");
          if (string.IsNullOrWhiteSpace(id) ||
              string.Equals(direction, "Output", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(id, "null", StringComparison.OrdinalIgnoreCase)) {
            continue;
          }
          string? description = ReadHint(hint, "DESC");
          description = string.IsNullOrWhiteSpace(description)
            ? id
            : description.Replace('\n', ' ').Replace('\r', ' ').Trim();
          devices[id] = new AudioCaptureDevice(id, description);
        }
      } finally {
        snd_device_name_free_hint(hints);
      }
      var ordered = new List<AudioCaptureDevice>(devices.Values);
      ordered.Sort((left, right) =>
        string.Compare(left.Id, right.Id, StringComparison.Ordinal));
      return ordered;
    }

    public IAlsaCapture OpenCapture(
      string deviceId,
      int sampleRate,
      int framesPerRead
    ) {
      if (string.IsNullOrWhiteSpace(deviceId)) {
        throw new ArgumentException(
          "An ALSA capture device ID is required.", nameof(deviceId));
      }
      if (sampleRate <= 0) {
        throw new ArgumentOutOfRangeException(nameof(sampleRate));
      }
      if (framesPerRead <= 0) {
        throw new ArgumentOutOfRangeException(nameof(framesPerRead));
      }

      Exception stereoFailure;
      try {
        return OpenConfigured(
          deviceId, sampleRate, framesPerRead, channels: 2);
      } catch (Exception error) {
        stereoFailure = error;
      }
      try {
        return OpenConfigured(
          deviceId, sampleRate, framesPerRead, channels: 1);
      } catch (Exception monoFailure) {
        throw new InvalidOperationException(
          "Could not configure ALSA capture device '" + deviceId +
          "' for stereo or mono PCM.",
          new AggregateException(stereoFailure, monoFailure));
      }
    }

    private static IAlsaCapture OpenConfigured(
      string deviceId,
      int sampleRate,
      int framesPerRead,
      int channels
    ) {
      int result = snd_pcm_open(
        out IntPtr handle, deviceId, CaptureStream, 0);
      ThrowOnError(result, "open ALSA capture device '" + deviceId + "'");
      try {
        result = snd_pcm_set_params(
          handle,
          Signed16LittleEndian,
          ReadWriteInterleaved,
          (uint)channels,
          (uint)sampleRate,
          1,
          TargetLatencyMicroseconds);
        ThrowOnError(
          result,
          "configure ALSA capture device '" + deviceId + "'");
        return new NativeAlsaCapture(handle, channels, framesPerRead);
      } catch {
        snd_pcm_close(handle);
        throw;
      }
    }

    private static string? ReadHint(IntPtr hint, string key) {
      IntPtr value = snd_device_name_get_hint(hint, key);
      if (value == IntPtr.Zero) {
        return null;
      }
      try {
        return Marshal.PtrToStringUTF8(value);
      } finally {
        free(value);
      }
    }

    private static void ThrowOnError(int result, string operation) {
      if (result >= 0) {
        return;
      }
      string? detail = Marshal.PtrToStringAnsi(snd_strerror(result));
      throw new InvalidOperationException(
        "Failed to " + operation + ": " + (detail ?? "ALSA error " + result));
    }

    private sealed class NativeAlsaCapture : IAlsaCapture {
      private IntPtr handle;
      private readonly IntPtr nativeBuffer;
      private readonly int framesPerRead;

      public NativeAlsaCapture(
        IntPtr handle,
        int channels,
        int framesPerRead
      ) {
        this.handle = handle;
        this.Channels = channels;
        this.framesPerRead = framesPerRead;
        this.nativeBuffer = Marshal.AllocHGlobal(
          checked(framesPerRead * channels * sizeof(short)));
      }

      public int Channels { get; }

      public int Read(short[] samples) {
        if (this.handle == IntPtr.Zero) {
          throw new ObjectDisposedException(this.GetType().Name);
        }
        int required = checked(this.framesPerRead * this.Channels);
        if (samples == null || samples.Length < required) {
          throw new ArgumentException(
            "The ALSA sample buffer is too small.", nameof(samples));
        }
        nint frames = snd_pcm_readi(
          this.handle, this.nativeBuffer, (nuint)this.framesPerRead);
        if (frames < 0) {
          int recovered = snd_pcm_recover(
            this.handle, checked((int)frames), 1);
          ThrowOnError(recovered, "recover ALSA capture");
          return 0;
        }
        int sampleCount = checked((int)frames * this.Channels);
        Marshal.Copy(this.nativeBuffer, samples, 0, sampleCount);
        return sampleCount;
      }

      public void Dispose() {
        IntPtr current = this.handle;
        if (current == IntPtr.Zero) {
          return;
        }
        this.handle = IntPtr.Zero;
        snd_pcm_drop(current);
        snd_pcm_close(current);
        Marshal.FreeHGlobal(this.nativeBuffer);
      }
    }

    [DllImport(AlsaLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_device_name_hint(
      int card, string iface, out IntPtr hints);

    [DllImport(AlsaLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr snd_device_name_get_hint(
      IntPtr hint, string id);

    [DllImport(AlsaLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_device_name_free_hint(IntPtr hints);

    [DllImport(AlsaLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_open(
      out IntPtr pcm, string name, int stream, int mode);

    [DllImport(AlsaLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_set_params(
      IntPtr pcm,
      int format,
      int access,
      uint channels,
      uint rate,
      int softResample,
      uint latency);

    [DllImport(AlsaLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint snd_pcm_readi(
      IntPtr pcm, IntPtr buffer, nuint size);

    [DllImport(AlsaLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_recover(
      IntPtr pcm, int error, int silent);

    [DllImport(AlsaLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_drop(IntPtr pcm);

    [DllImport(AlsaLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern int snd_pcm_close(IntPtr pcm);

    [DllImport(AlsaLibrary, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr snd_strerror(int error);

    [DllImport("libc", CallingConvention = CallingConvention.Cdecl)]
    private static extern void free(IntPtr pointer);
  }
}
