using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Numerics;
using System.Collections.Generic;

namespace Spectrum.Visualizers {
  class LEDDomeQuaternionPaintbrushVisualizer : Visualizer {


    private Configuration config;
    private AudioInput audio;
    private OrientationInput orientation;
    private LEDDomeOutput dome;
    private LEDDomeOutputBuffer buffer;

    private Quaternion currentOrientation;
    private Quaternion lastOrientation;

    private Random rand;

    private Vector3 spot = new Vector3(0, 1, 0);

    // idle variables
    private int idleTimer = 600;
    private bool idle = false;
    private double yaw = 0;
    private double pitch = -.25;
    private double roll = 0;

    private double yawMomentum = 0;
    private double pitchMomentum = 0.0005;
    private double rollMomentum = 0;

    private int spotlightId = -1;
    private Quaternion spotlightCenter = new Quaternion(0, 0, 0, 1);

    // Stamp effect variables
    private Quaternion stampCenter = new Quaternion(0, 0, 0, 1);
    int counter = 0;
    int cooldown = 7;
    double lastProgress = 0;
    bool stampFired = false;
    int stampEffect = 0; // 0 - meridian ring; 1 - grid of rings; 2 - rhythm stamp

    // Ripple effect variables
    int rippleType = 0; // 0 - 'static' ripple; 1 - 'follower' ripple
    private Quaternion rippleCenter = new Quaternion(0, 0, 0, 1);
    double rippleCounter = 0;
    bool rippleFiring = false;
    double rippleCooldown = 100; // tweak this later

    // Contour line variables
    bool contourFiring = false;
    double contourCounter = 0;

    public LEDDomeQuaternionPaintbrushVisualizer(
      Configuration config,
      AudioInput audio,
      OrientationInput orientation,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.audio = audio;
      this.orientation = orientation;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      buffer = this.dome.MakeDomeOutputBuffer();
      rand = new Random();
      currentOrientation = orientation.deviceRotation(config.orientationDeviceSpotlight);
      lastOrientation = new Quaternion(0, 0, 0, 1);
    }

    public int Priority {
      get {
        return this.config.domeActiveVis == 6 ? 2 : 0;
      }
    }

    public bool Enabled { get; set; }

    public Input[] GetInputs() {
      return new Input[] { this.orientation };
    }

    void Render() {
      double progress = this.config.beatBroadcaster.ProgressThroughMeasure;
      double level = this.audio.Volume;
      // Global effects
      // Fade out
      buffer.Fade(1 - Math.Pow(5, -this.config.domeGlobalFadeSpeed), 0);
      // Hue shift to the beat
      buffer.HueRotate((3 * progress * progress - 3 * progress + 1) * Math.Pow(10, -this.config.domeGlobalHueSpeed));
      counter++;

      // Store the device states as of this frame; this avoids problems when the devices get updated
      // in another thread
      Dictionary<int, OrientationDevice> devices;
      devices = new Dictionary<int, OrientationDevice>(orientation.devices);

      if (devices.ContainsKey(config.orientationDeviceSpotlight)) {
        spotlightId = config.orientationDeviceSpotlight;
        spotlightCenter = devices[spotlightId].currentRotation();
      }

      // Check if sensor is moving or not; this is only relevant if one device is left, if theres multiple we first wait for them to turn off
      if (devices.Count <= 1) {
        float sensorThreshold = .00015f;
        if (devices.Count > 0) {
          var en = devices.Values.GetEnumerator();
          en.MoveNext();
          currentOrientation = en.Current.currentRotation();
          en.Dispose();
        }
        if ((Math.Abs(1 - Quaternion.Dot(lastOrientation, currentOrientation)) < sensorThreshold) |
          (IsZero(currentOrientation))) {
          if (idleTimer != 0) {
            idleTimer--;
          }
        } else {
          idleTimer = 600;
        }
        lastOrientation = currentOrientation;
        idle = idleTimer <= 0;
      } else {
        idle = false;
      }

      if (idle) {
        // Sensor not apparently moving
        // randomly nudge pointer
        // enforce unit-ness
        // tweak these in the future
        double noise = 0.0001;
        yawMomentum = Clamp(yawMomentum + Nudge(noise), -.001, .001);
        rollMomentum = Clamp(rollMomentum + Nudge(noise), -.001, .001);
        pitchMomentum = Clamp(pitchMomentum + Nudge(noise), -.001, .001);

        yaw = (yaw + 5 * (level + .2) * yawMomentum);
        pitch = (pitch + 5 * (level + .2) * pitchMomentum);
        roll = (roll + 5 * (level + .2) * rollMomentum);

        Quaternion dummyOrientation = Quaternion.CreateFromYawPitchRoll((float)(2 * Math.PI * yaw), (float)(2 * Math.PI * pitch), (float)(2 * Math.PI * roll));
        dummyOrientation = Quaternion.Normalize(dummyOrientation);
        currentOrientation = dummyOrientation;
        spotlightId = -1;
      }

      // STAMP logic
      // A beat has happened but we are still rendering a stamp
      if (cooldown > 0 & lastProgress > progress) {
        cooldown--;
        if (cooldown <= 0) {
        // stamp finished rendered
          stampFired = false;
        }
      }
      // Enough time has passed and something loud enough has happened - fire stamp
      if (counter > 1000 & level > .3) {
        stampFired = true;
        counter = 0;
        cooldown = 10;
        // Choose one of the three
        stampEffect = (stampEffect + 1) % 3;
        if (spotlightId == -1) {
          stampCenter = currentOrientation;
        } else {
          stampCenter = spotlightCenter;
        }
      }

      // RIPPLE logic
      if (rippleCounter > 1000) { // tweak this later
        rippleCounter = 0;
        rippleFiring = false;
      }

      if (!rippleFiring) {
        rippleCooldown -= this.config.domeRippleCDStep;
      }

      if (rippleCooldown < 0) {
        rippleFiring = true;
        rippleType += 1;
        rippleType = rippleType % 2;
        if (spotlightId == -1) {
          rippleCenter = currentOrientation;
        } else {
          rippleCenter = spotlightCenter;
        }
        rippleCooldown = 100;
      }

      if (rippleFiring) {
        rippleCounter += this.config.domeRippleStep;
        if (rippleType == 1) {
          if (spotlightId == -1) {
            rippleCenter = currentOrientation;
          } else {
            rippleCenter = spotlightCenter;
          }
        }
      }

      // CONTOUR logic
      if (config.orientationAction) {
        config.orientationAction = false;
        contourFiring = true;
        contourCounter = 6; 
      }

      if (contourFiring) {
        contourCounter -= .01;
        if (contourCounter < 2) {
          contourFiring = false;
        }
      }

      double thresholdFactor = (config.domeRadialSize / 4) + level + .25;
      double threshold = 1 / thresholdFactor;

      // Big pixel painting loop
      for (int i = 0; i < buffer.pixels.Length; i++) {
        // 1. Convert the current pixel in the buffer to its coordinates
        var p = buffer.pixels[i];
        var x = 2 * p.x - 1; // now centered on (0, 0) and with range [-1, 1]
        var y = 1 - 2 * p.y; // this is because in the original mapping x, y come "out of" the top left corner
        float z = (x * x + y * y) > 1 ? 0 : (float)Math.Sqrt(1 - x * x - y * y);
        Vector3 pixelPoint = new Vector3((float)x, (float)y, z);

        // # Ring stamps - shapes that appear based on sensor facing
        if (stampFired) {
          // Single band
          if (stampEffect == 0) {
            if (Between(Vector3.Distance(Vector3.Transform(pixelPoint, stampCenter), spot), 1.13, 1.27)) {
              double hue = (256 * (currentOrientation.W + 1) / 2) / 256d;
              Color color = new Color(hue, .2, 1);
              buffer.pixels[i].color = color.ToInt();
            }
          } else if (stampEffect == 1) {
            // Evenly spaced "grid"
            if (Vector3.Distance(Vector3.Transform(pixelPoint, stampCenter), spot) % .4 < .05) {
              double hue = (256 * (currentOrientation.W + 1) / 2) / 256d;
              Color color = new Color(hue, .2, 1);
              buffer.pixels[i].color = color.ToInt();
            }
          } else if (stampEffect == 2) {
            // Time delayed decreasing bands
            // 0 to 2
            // counter goes from 9 to 0
            // 2 - (counter / 9)
            // have them appear 'to the beat'?
            double ringDistance = 2.4 - Clamp(1.8d / (4 - (cooldown / 2d)), 0, 2.4);
            if (Between(Vector3.Distance(Vector3.Transform(pixelPoint, stampCenter), spot), ringDistance - .003 * cooldown * cooldown, ringDistance + .003 * cooldown * cooldown)) {
              double hue = (256 * (currentOrientation.W + 1) / 2) / 256d;
              Color color = new Color(hue, .2, 1);
              buffer.pixels[i].color = color.ToInt();
            }
          }
        }

        // # Twinkling - (configurably) dense bright dots at random
        if (rand.NextDouble() < this.config.domeTwinkleDensity & z > .2) {
          buffer.pixels[i].color = 0xFFFFFF;
        }

        // # Spotlight - orientation sensor dot
        // Calibration assigns (0, 1, 0) to be 'forward'
        // So we want the post-transformed pixel closest to (0, 1, 0)?
        //double radius = Clamp(config.domeRadialSize * level * level * level * level, .05, 4);
        double potential = 0;
        int deviceCounter = 0;
        Quaternion colorCenter = new Quaternion(0, 0, 0, 0);
        if (idle) {
          double distance = Vector3.Distance(Vector3.Transform(pixelPoint, currentOrientation), spot);
          double negadistance = Vector3.Distance(Vector3.Transform(pixelPoint, currentOrientation), Vector3.Negate(spot));
          potential += 1 / (distance * negadistance);
          colorCenter = currentOrientation;
        } else {
          foreach (int deviceId in devices.Keys) {

            deviceCounter++;
            Quaternion currentOrientation = devices[deviceId].currentRotation();

            if (!devices.ContainsKey(config.orientationDeviceSpotlight)) {
              // for simplicity lets just assign the first one we see as the spotlight ID
              spotlightId = deviceId;
              spotlightCenter = currentOrientation;
            }
            // Metaball - we sum up contributions from each sensor to the current point and shade based on that
            // The added trick is, we identify opposite ends of the hemispherical domain
            // So we sum both 'ends' at once (i.e. (0, 1, 0) and (0, -1, 0) are identified as the same point)
            double distance = Vector3.Distance(Vector3.Transform(pixelPoint, currentOrientation), spot);
            double negadistance = Vector3.Distance(Vector3.Transform(pixelPoint, currentOrientation), Vector3.Negate(spot));
            double scale = 1 / (distance * negadistance);
            if (distance < negadistance) {
              colorCenter += Quaternion.Multiply(currentOrientation, (float)scale);
            } else {
              colorCenter -= Quaternion.Multiply(currentOrientation, (float)scale);
            }
            potential += scale;
          }
          colorCenter = Quaternion.Normalize(colorCenter);
          potential = potential / deviceCounter; // normalize by device count
        }

        double strength = potential - threshold;
        // 'absolute' metaball - just a crisp cutoff at threshold
        if (strength > 0) {
          double hue = (256 * (1 + colorCenter.W) / 2) / 256d;
          // At the high volumes, desaturate
          double saturation = Clamp(1.2 / level - 1, 0, 1);
          Color color = new Color(hue, saturation, 1);
          buffer.pixels[i].color = Color.BlendLightPaint(new Color(buffer.pixels[i].color), color).ToInt();
        }

        // Contour - highlight level curves of the potential field
        if (contourFiring & Math.Abs(potential - contourCounter) < 1) {
          Color color = new Color(1, .1, 1);
          buffer.pixels[i].color = color.ToInt();
        }

        // Ripple_follow - global color wave that follows a spot
        double rippleRadius = rippleCounter / 300d;
        if (CloseTo(Vector3.Distance(Vector3.Transform(pixelPoint, rippleCenter), spot), rippleRadius, .01)) {
          double hue = Wrap(((256 * (currentOrientation.W + 1) / 2) / 256d) + Vector3.Dot(Vector3.Transform(pixelPoint, rippleCenter), spot) / 2, 0, 1);
          double saturation = Clamp(1 - rippleCounter / 600d, 0, 1);
          double value = Clamp(1 - rippleCounter / 800d, 0, 1);
          Color color = new Color(hue, saturation, value);
          buffer.pixels[i].color = Color.BlendValue(1, new Color(buffer.pixels[i].color), color).ToInt();
        }
      }
      // Finished pixel iterations - clean up
      if (cooldown < 7 & (stampEffect == 0 | stampEffect == 1)) {
        stampFired = false;
      }
      lastProgress = progress;
      dome.WriteBuffer(buffer);
    }

    public void Visualize() {
      this.Render();

      this.dome.Flush();
    }
    private static bool Between(double x, double a, double b) {
      return (x >= a & x <= b); // closed intervals
    }
    private static double Clamp(double x, double a, double b) {
      if (x < a) return a;
      if (x > b) return b;
      return x;
    }
    private static double Wrap(double x, double a, double b) {
      var range = b - a;
      while (x < a) x += range;
      while (x > b) x -= range;
      return x;
    }

    private static bool CloseTo(double x, double y, double tolerance) {
      return Math.Abs(x - y) < tolerance;
    }
    private float Nudge(double scale) {
      return (float)((this.rand.NextDouble() - .5) * 2 * scale);
    }

    private bool IsZero(Quaternion vector) {
      return (vector.W == 0 & vector.X == 0 & vector.Y == 0 & vector.Z == 0);
    }
  }
}
