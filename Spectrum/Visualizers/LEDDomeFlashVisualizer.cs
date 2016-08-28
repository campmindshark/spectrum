using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectrum.Base;
using Spectrum.Audio;
using Spectrum.LEDs;
using Spectrum.MIDI;
using System.Collections.Concurrent;

namespace Spectrum {

  enum ShapeType : byte {
    Triangle, Triforce, Polygon, LargePolygon, Everything
  };

  class Shape {

    private readonly LEDDomeOutput dome;
    public readonly ShapeType type;
    public readonly StrutLayout layout;
    public Animation activeAnimation;
    public readonly HashSet<int> struts;

    public Shape(LEDDomeOutput dome, ShapeType type, StrutLayout layout) {
      this.dome = dome;
      this.type = type;
      this.layout = layout;
      this.activeAnimation = null;
      this.struts = new HashSet<int>(
        Enumerable.Range(0, this.layout.NumSegments)
          .SelectMany(i =>
            this.layout.GetSegment(i).GetStruts().Select(strut => strut.Index)
          )
      );
    }

    public bool Available {
      get {
        return this.Enabled && this.activeAnimation == null;
      }
    }

    public bool Enabled {
      get {
        return !this.struts.Overlaps(this.dome.ReservedStruts());
      }
    }

  }

  class Animation {

    public readonly Shape shape;
    public readonly int pad;
    private readonly int animationLength;
    private readonly double velocity;

    public readonly long startingTime;
    private readonly long peakTime;
    private long endTime;
    private bool released;

    public Animation(
      Shape shape,
      int pad,
      double velocity,
      int measureLength
    ) {
      this.shape = shape;
      this.pad = pad;
      this.animationLength = measureLength / 4;
      this.velocity = velocity;

      this.startingTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      this.peakTime = this.startingTime + (int)(this.animationLength * 0.8);
      this.endTime = this.startingTime + this.animationLength;
      this.released = false;
    }

    public bool Active {
      get {
        long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        return this.shape.Enabled
          && (!this.released || this.endTime > timestamp);
      }
    }

    public void Release() {
      if (this.released) {
        return;
      }
      this.released = true;
      var timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      if (timestamp > this.peakTime) {
        this.endTime = timestamp + (int)(this.animationLength * 0.2);
      }
    }

    public double AnimationIntensity {
      get {
        long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        if (timestamp < this.peakTime) {
          return ((double)timestamp - this.startingTime)
            / ((double)this.peakTime - this.startingTime);
        } else if (!this.released) {
          return 1.0;
        } else if (timestamp >= this.endTime) {
          return 0.0;
        } else {
          return 1.0 - ((double)timestamp - this.peakTime)
            / ((double)this.endTime - this.peakTime);
        }
      }
    }

    public void Animate(LEDDomeOutput dome) {
      double intensity = this.AnimationIntensity;
      double scaleColor = Math.Min(intensity * 2 * this.velocity, 1.0);
      int totalParts = this.shape.layout.NumSegments;
      int animationSplitInto = 2 * ((totalParts - 1) / 2 + 1);
      for (int part = 0; part < totalParts; part += 2) {
        double startRange = (double)part / animationSplitInto;
        double endRange = (double)(part + 2) / animationSplitInto;
        double scaled = (intensity - startRange) /
          (endRange - startRange);
        scaled = Math.Max(Math.Min(scaled, 1.0), 0.0);
        startRange = Math.Min(startRange / intensity, 1.0);
        endRange = Math.Min(endRange / intensity, 1.0);

        var spokeSegment = this.shape.layout.GetSegment(part);
        foreach (var strut in spokeSegment.GetStruts()) {
          for (int i = 0; i < strut.Length; i++) {
            double gradientPos =
              strut.GetGradientPos(scaled, startRange, endRange, i);
            int color1 = gradientPos != -1.0
              ? LEDColor.ScaleColor(
                  dome.GetGradientColor(this.pad, gradientPos, 0.0, false),
                  scaleColor
                )
              : 0x000000;
            dome.SetPixel(strut.Index, i, color1);
          }
        }

        if (part + 1 == totalParts) {
          break;
        }

        var circleSegment = this.shape.layout.GetSegment(part + 1);
        var color2 = scaled == 1.0
          ? LEDColor.ScaleColor(dome.GetSingleColor(this.pad), scaleColor)
          : 0x000000;
        foreach (var strut in circleSegment.GetStruts()) {
          for (int i = 0; i < strut.Length; i++) {
            dome.SetPixel(strut.Index, i, color2);
          }
        }
      }
    }

  }

  class LEDDomeFlashVisualizer : Visualizer {

    private Configuration config;
    private AudioInput audio;
    private LEDDomeOutput dome;
    private MidiInput midi;

    private Dictionary<ShapeType, List<Shape>> shapes;
    private Dictionary<int, List<Shape>> strutsToShapes;
    private List<Animation> activeAnimations;
    private Dictionary<int, Animation> padsToLastAnimation;

    private Random rand;
    public long lastUserAnimationCreated;

    public LEDDomeFlashVisualizer(
      Configuration config,
      AudioInput audio,
      MidiInput midi,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.audio = audio;
      this.midi = midi;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);

      this.shapes = new Dictionary<ShapeType, List<Shape>>() {
        { ShapeType.Triangle, new List<Shape>() },
        { ShapeType.Triforce, new List<Shape>() },
        { ShapeType.Polygon, new List<Shape>() },
        { ShapeType.LargePolygon, new List<Shape>() },
        { ShapeType.Everything, new List<Shape>() },
      };
      this.strutsToShapes = new Dictionary<int, List<Shape>>();
      this.activeAnimations = new List<Animation>();
      this.padsToLastAnimation = new Dictionary<int, Animation>();

      this.rand = new Random();
      this.lastUserAnimationCreated = 0;

      this.BuildShapes();
    }

    private void BuildShapes() {
      for (int i = 20; i < 71; i++) {
        StrutLayout[] layouts = StrutLayoutFactory.LayoutsFromStartingPoints(
          this.config,
          new HashSet<int>() { i },
          2
        );
        Shape shape = new Shape(this.dome, ShapeType.Polygon, layouts[0]);
        this.shapes[ShapeType.Polygon].Add(shape);
        foreach (int strutIndex in shape.struts) {
          if (!this.strutsToShapes.ContainsKey(strutIndex)) {
            this.strutsToShapes[strutIndex] = new List<Shape>();
          }
          this.strutsToShapes[strutIndex].Add(shape);
        }
      }
      // TODO do enums have order? can they be compared?
      // TODO build other kinds of shapes here
    }

    public int Priority {
      get {
        return 2;
      }
    }

    // We don't actually care about this
    public bool Enabled { get; set; } = false;

    public Input[] GetInputs() {
      return new Input[] { this.audio, this.midi };
    }

    public void Visualize() {
      // Check if any active animations need to be removed because they use
      // struts that are now reserved, or because they are finished
      var toRemove = new HashSet<Animation>(
        this.activeAnimations.Where(a => !a.Active)
      );
      foreach (var animation in toRemove) {
        animation.shape.activeAnimation = null;
        if (this.padsToLastAnimation[animation.pad] == animation) {
          this.padsToLastAnimation.Remove(animation.pad);
        }
        // We only need to clear the LEDs on unreserved struts
        var unreservedStruts =
          animation.shape.struts.Except(this.dome.ReservedStruts());
        foreach (int strutIndex in unreservedStruts) {
          Strut strut = Strut.FromIndex(this.config, strutIndex);
          for (int i = 0; i < strut.Length; i++) {
            this.dome.SetPixel(strutIndex, i, 0x000000);
          }
        }
      }
      this.activeAnimations.RemoveAll(a => toRemove.Contains(a));

      // See if we need to create any new animations
      var commands = this.midi.GetCommandsSinceLastTick();
      foreach (MidiCommand command in commands) {
        if (command.type != MidiCommandType.Note) {
          continue;
        }
        if (command.index > 15) {
          continue;
        }
        if (this.padsToLastAnimation.ContainsKey(command.index)) {
          var padLastAnimation = this.padsToLastAnimation[command.index];
          padLastAnimation.Release();
          if (command.value == 0.0) {
            continue;
          }
        }
        Animation animation = this.NewRandomAnimation(
          ShapeType.Polygon,
          command.index,
          command.value
        );
        if (animation == null) {
          // If we aren't able to get any shape at all,
          // then give up on new animations
          break;
        }
        this.lastUserAnimationCreated = animation.startingTime;
      }

      // If we haven't seen any animations in a bit and computer animation is
      // enabled, then let's try and see if we should create one
      long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      if (timestamp > this.lastUserAnimationCreated + 2000) {
        foreach (AudioEvent audioEvent in this.audio.GetEventsSinceLastTick()) {
          int computerColorIndex;
          if (audioEvent.type == AudioDetectorType.Kick) {
            computerColorIndex = 0;
          } else if (audioEvent.type == AudioDetectorType.Snare) {
            computerColorIndex = 1;
          } else {
            throw new Exception("invalid AudioDetectorType");
          }
          int? colorIndex = this.config.domeColorPalette.GetIndexOfEnabledIndex(
            computerColorIndex
          );
          if (!colorIndex.HasValue) {
            continue;
          }
          Animation animation = this.NewRandomAnimation(
            ShapeType.Polygon,
            colorIndex.Value,
            audioEvent.significance
          );
          if (animation == null) {
            // If we aren't able to get any shape at all,
            // then give up on new animations
            break;
          }
          animation.Release();
        }
      }

      // Okay, now we have to actually animate each of the active animations
      foreach (var animation in this.activeAnimations) {
        animation.Animate(this.dome);
      }
    }

    private Animation NewRandomAnimation(
      ShapeType type,
      int pad,
      double velocity
    ) {
      Shape randomShape = this.RandomAvailableShape(type);
      // TODO: once we have multiple ShapeTypes,
      // we need to try small shapes when large ones fail
      if (randomShape == null) {
        return null;
      }
      long timestamp = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      var measureLength = this.config.domeBeatBroadcaster.MeasureLength;
      var animation = new Animation(
        randomShape,
        pad,
        velocity,
        measureLength == -1 ? 400 : measureLength
      );
      this.activeAnimations.Add(animation);
      this.padsToLastAnimation[pad] = animation;
      animation.shape.activeAnimation = animation;
      return animation;
    }

    private Shape RandomAvailableShape(ShapeType type) {
      var availableShapes = this.shapes[type].Where(shape => shape.Available);
      if (availableShapes.Count() == 0) {
        return null;
      }
      int index = this.rand.Next(availableShapes.Count());
      return availableShapes.ElementAt(index);
    }

  }

}