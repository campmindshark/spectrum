using System;
using System.Collections.Generic;
using System.Globalization;

namespace Spectrum.Base {

  /**
   * Which surface a parameter belongs to (see docs/web_architecture.md). The
   * split is a server-side authorization boundary, not just two HTML pages: a
   * "user" caller may only touch User parameters; a "maintenance" caller may
   * touch everything.
   */
  public enum ControlRole {
    User,
    Maintenance,
  }

  /**
   * One descriptor per web-controllable knob. This is the contract between the
   * REST/SignalR layer and Configuration: the web layer reads and writes ONLY
   * through descriptors, never by reflecting Configuration directly. That gives
   * us server-side validation/clamping, role gating, and a single source from
   * which both pages' UIs can be generated.
   *
   * Coerce() is where the range enforcement the WPF sliders do in XAML has to
   * live for the web (which has no such guard). It takes a raw incoming value
   * (typically boxed from JSON) and returns a validated, clamped, correctly
   * typed value ready for Set(), or throws ArgumentException if the value can't
   * be made valid.
   */
  public abstract class ParameterDescriptor {

    public string Key { get; }
    public ControlRole Role { get; }
    // A short type tag ("double", "int", "bool", "enum") used by clients to
    // pick an appropriate control and by the JSON layer to shape values.
    public string Type { get; }

    protected ParameterDescriptor(string key, ControlRole role, string type) {
      this.Key = key;
      this.Role = role;
      this.Type = type;
    }

    // Current value of this parameter, read from the given Configuration.
    public abstract object Get(Configuration config);

    // Validate/clamp/convert a raw incoming value. Throws ArgumentException if
    // the value cannot be coerced into the valid range/type.
    public abstract object Coerce(object raw);

    // Apply an already-Coerce()d value to the Configuration. Must only be
    // called on the serialization thread (via a ControlGateway).
    public abstract void Set(Configuration config, object coerced);

    // Optional UI-generation metadata; null when not applicable.
    public virtual object Min => null;
    public virtual object Max => null;
    public virtual IReadOnlyList<string> Options => null;

    protected static double ToDouble(object raw) {
      try {
        return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
      } catch (Exception e) when (e is FormatException || e is InvalidCastException) {
        throw new ArgumentException("value is not a number");
      }
    }

    protected static int ToInt(object raw) {
      // Accept JSON numbers that arrive as doubles ("2.0") but reject
      // genuinely fractional values.
      double d = ToDouble(raw);
      if (d != Math.Floor(d)) {
        throw new ArgumentException("value is not an integer");
      }
      return checked((int)d);
    }
  }

  public sealed class DoubleParameter : ParameterDescriptor {

    private readonly Func<Configuration, double> get;
    private readonly Action<Configuration, double> set;
    private readonly double min;
    private readonly double max;

    public DoubleParameter(
      string key,
      ControlRole role,
      double min,
      double max,
      Func<Configuration, double> get,
      Action<Configuration, double> set
    ) : base(key, role, "double") {
      this.min = min;
      this.max = max;
      this.get = get;
      this.set = set;
    }

    public override object Get(Configuration config) => this.get(config);

    public override object Coerce(object raw) {
      double v = ToDouble(raw);
      if (double.IsNaN(v) || double.IsInfinity(v)) {
        throw new ArgumentException("value is not a finite number");
      }
      return Math.Clamp(v, this.min, this.max);
    }

    public override void Set(Configuration config, object coerced) =>
      this.set(config, (double)coerced);

    public override object Min => this.min;
    public override object Max => this.max;
  }

  public sealed class IntParameter : ParameterDescriptor {

    private readonly Func<Configuration, int> get;
    private readonly Action<Configuration, int> set;
    private readonly int min;
    private readonly int max;

    public IntParameter(
      string key,
      ControlRole role,
      int min,
      int max,
      Func<Configuration, int> get,
      Action<Configuration, int> set
    ) : base(key, role, "int") {
      this.min = min;
      this.max = max;
      this.get = get;
      this.set = set;
    }

    public override object Get(Configuration config) => this.get(config);

    public override object Coerce(object raw) =>
      Math.Clamp(ToInt(raw), this.min, this.max);

    public override void Set(Configuration config, object coerced) =>
      this.set(config, (int)coerced);

    public override object Min => this.min;
    public override object Max => this.max;
  }

  public sealed class BoolParameter : ParameterDescriptor {

    private readonly Func<Configuration, bool> get;
    private readonly Action<Configuration, bool> set;

    public BoolParameter(
      string key,
      ControlRole role,
      Func<Configuration, bool> get,
      Action<Configuration, bool> set
    ) : base(key, role, "bool") {
      this.get = get;
      this.set = set;
    }

    public override object Get(Configuration config) => this.get(config);

    public override object Coerce(object raw) {
      if (raw is bool b) {
        return b;
      }
      if (raw is string s && bool.TryParse(s, out bool parsed)) {
        return parsed;
      }
      throw new ArgumentException("value is not a boolean");
    }

    public override void Set(Configuration config, object coerced) =>
      this.set(config, (bool)coerced);
  }

  public sealed class StringParameter : ParameterDescriptor {

    private readonly Func<Configuration, string> get;
    private readonly Action<Configuration, string> set;
    private readonly int maxLength;

    public StringParameter(
      string key,
      ControlRole role,
      Func<Configuration, string> get,
      Action<Configuration, string> set,
      int maxLength = 256
    ) : base(key, role, "string") {
      this.get = get;
      this.set = set;
      this.maxLength = maxLength;
    }

    public override object Get(Configuration config) => this.get(config);

    public override object Coerce(object raw) {
      if (raw == null) {
        throw new ArgumentException("value is null");
      }
      string s = raw as string ?? raw.ToString();
      if (s.Length > this.maxLength) {
        throw new ArgumentException("value exceeds " + this.maxLength + " chars");
      }
      return s;
    }

    public override void Set(Configuration config, object coerced) =>
      this.set(config, (string)coerced);

    public override object Max => this.maxLength;
  }

  /**
   * An int-valued parameter constrained to a small enumeration (e.g. dome
   * test-pattern index, active visualizer index). Options are the human labels
   * for each index 0..N-1, used to generate a dropdown; the wire value is the
   * int index, clamped to the valid range.
   */
  public sealed class EnumIntParameter : ParameterDescriptor {

    private readonly Func<Configuration, int> get;
    private readonly Action<Configuration, int> set;
    private readonly IReadOnlyList<string> options;

    public EnumIntParameter(
      string key,
      ControlRole role,
      IReadOnlyList<string> options,
      Func<Configuration, int> get,
      Action<Configuration, int> set
    ) : base(key, role, "enum") {
      this.options = options;
      this.get = get;
      this.set = set;
    }

    public override object Get(Configuration config) => this.get(config);

    public override object Coerce(object raw) {
      int v = ToInt(raw);
      if (v < 0 || v >= this.options.Count) {
        throw new ArgumentException(
          "value out of range 0.." + (this.options.Count - 1));
      }
      return v;
    }

    public override void Set(Configuration config, object coerced) =>
      this.set(config, (int)coerced);

    public override object Min => 0;
    public override object Max => this.options.Count - 1;
    public override IReadOnlyList<string> Options => this.options;
  }
}
