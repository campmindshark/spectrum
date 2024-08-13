using Spectrum.Audio;
using Spectrum.Base;
using Spectrum.LEDs;
using System;
using System.Numerics;
using System.Collections.Generic;
using System.Drawing;

namespace Spectrum.Visualizers {
  class Planet {
    public bool cleanup = false;
    public Vector2 position;
    Vector2 velocity;
    public Vector3 dome_position;
    float timescale = .01f; // tweak this
    double escape_velocity = 10;
    Configuration config;
    public Planet(float x, float y, float vx, float vy, Configuration config) {
      position = new Vector2(x, y);
      velocity = new Vector2(vx, vy);
      this.config = config;
      dome_position = projectDome();
    }

    public Planet(Vector2 position, Vector2 velocity, Configuration config) {
      this.position = position;
      this.velocity = velocity;
      this.config = config;
    }

    public void update(Vector2 new_acceleration) {
      velocity = (float)config.orientationFriction * velocity;
      position += timescale * velocity;
      double position_r = position.Length();
      velocity += timescale * new_acceleration;
      if (position_r > 1) {
        if (config.orientationSphereTopology) {
          position = -position;
        } else {
          if (velocity.Length() > escape_velocity) {
            cleanup = true;
          }
        }
      }
      dome_position = projectDome();
    }

    private Vector3 projectDome() {
      float z = (float)Math.Sqrt(1 - position.LengthSquared());
      return new Vector3(position, z);
    }
  }
  class LEDDomeQuaternionPaintbrushVisualizer : Visualizer {


    private Configuration config;
    private AudioInput audio;
    private OrientationInput orientationInput;
    private LEDDomeOutput dome;
    private LEDDomeOutputBuffer buffer;

    private Quaternion currentOrientation;
    private Quaternion lastOrientation;

    private Random rand;

    private Vector3 spot = new Vector3(-1, 0, 0);

    // idle variables
    private int idleTimer = 100;
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
    int stampEffect = 0; // 1 - grid of rings; 2 - rhythm stamp

    // Ripple effect variables
    int rippleType = 0; // 0 - 'static' ripple; 1 - 'follower' ripple
    private Quaternion rippleCenter = new Quaternion(0, 0, 0, 1);
    double rippleCounter = 0;
    bool rippleFiring = false;
    double rippleCooldown = 100; // tweak this later

    // Contour line variables
    double contourCounter = 0;

    // Planets
    HashSet<Planet> planets = new HashSet<Planet>();
    bool spawningPlanets = false;
    int planetsToSpawn = 0;
    // Change these to stagger out planet spawning more; i.e. planetTimer = 10 will require 10 iterations per spawn
    int planetTimer = 2;
    int planetCounter = 0;
    Vector2 new_planet_position;
    Vector2 new_planet_velocity;

    public LEDDomeQuaternionPaintbrushVisualizer(
      Configuration config,
      AudioInput audio,
      OrientationInput orientationInput,
      LEDDomeOutput dome
    ) {
      this.config = config;
      this.audio = audio;
      this.orientationInput = orientationInput;
      this.dome = dome;
      this.dome.RegisterVisualizer(this);
      buffer = this.dome.MakeDomeOutputBuffer();
      rand = new Random();
      currentOrientation = new Quaternion(0, 0, 0, 1);
      lastOrientation = new Quaternion(0, 0, 0, 1);
      idleTimer = 0;
    }

    public int Priority {
      get {
        return this.config.domeActiveVis == 6 ? 2 : 0;
      }
    }

    public bool Enabled { get; set; }

    public Input[] GetInputs() {
      return new Input[] { this.orientationInput };
    }

    void Render() {
      double progress = this.config.beatBroadcaster.ProgressThroughMeasure;
      double level = this.audio.Volume;

      // Check for planet interaction
      if (config.orientationPlanetClear) {
        config.orientationPlanetClear = false;
        planets = new HashSet<Planet>();
      }

      if (config.orientationPlanetSpawn && config.orientationPlanetSpawnNumber > 0) {
        config.orientationPlanetSpawn = false;
        spawningPlanets = true;
        planetsToSpawn = config.orientationPlanetSpawnNumber;
        // pick a random x, y somewhere between r = .2 and r = .8
        // pick a random velocity that's 1/r^2, perpendicular to random x, y, maybe plus or minus a small amount
        float new_planet_r = (float)rand.NextDouble() * .2f + .5f;
        float new_planet_x = (float)rand.NextDouble() * .6f + .2f;
        float new_planet_y = (float)Math.Sqrt(new_planet_r * new_planet_r - new_planet_x * new_planet_x);
        new_planet_position = new Vector2(new_planet_x, new_planet_y);
        new_planet_velocity = new Vector2(-new_planet_y, new_planet_x);

      }

      if (spawningPlanets) {
        planetCounter++;
        if (planetCounter >= planetTimer) {
          planetCounter = 0;
          planetsToSpawn--;
          planets.Add(new Planet(new_planet_position, new_planet_velocity, config));
        }
        if (planetsToSpawn <= 0) {
          spawningPlanets = false;
        }
      }
      // Global effects
      // Fade out
      buffer.Fade(1 - Math.Pow(5, -this.config.domeGlobalFadeSpeed), 0);
      // Hue shift to the beat
      buffer.HueRotate((3 * progress * progress - 3 * progress + 1) * Math.Pow(10, -this.config.domeGlobalHueSpeed));
      counter++;

      // Store the device states as of this frame; this avoids problems when the devices get updated
      // in another thread
      Dictionary<int, OrientationDevice> devices;
      devices = new Dictionary<int, OrientationDevice>(orientationInput.devices);

      if (devices.ContainsKey(config.orientationDeviceSpotlight)) {
        spotlightId = config.orientationDeviceSpotlight;
        spotlightCenter = devices[spotlightId].currentRotation();
      }
      // Check if sensor is moving or not; this is only relevant if one device is left, if theres multiple we first wait for them to turn off
      if (devices.Count == 0) {
        idle = true;
      } else if (devices.Count == 1) {
        float sensorThreshold = .0001f;
        var en = devices.Values.GetEnumerator();
        en.MoveNext();
        currentOrientation = en.Current.currentRotation();
        en.Dispose();
        double diff = Math.Abs(1 - Quaternion.Dot(lastOrientation, currentOrientation));
        if ((diff < sensorThreshold) |
          (IsZero(currentOrientation))) {
          if (idleTimer > 0) {
            idleTimer--;
          }
        } else {
          idle = false;
          idleTimer = 100;
        }
        lastOrientation = currentOrientation;
        if (idleTimer <= 0) {
          idle = true;
        }
      } else {
        idle = false;
      }

      // hack to temporarily ignore all wands if the spotlight ID is -2
      if (config.orientationDeviceSpotlight == -2) {
        idle = true;
      }
      // end hack
      if (idle) {
        // Sensor not apparently moving
        // randomly nudge pointer
        // enforce unit-ness
        // tweak these in the future
        double noise = 0.0001;
        yawMomentum = Clamp(yawMomentum + Nudge(noise), -.001, .001);
        rollMomentum = Clamp(rollMomentum + Nudge(noise), -.001, .001);
        pitchMomentum = Clamp(pitchMomentum + Nudge(noise), -.001, .001);

        yaw = (yaw + 4 * (level + .25) * yawMomentum);
        pitch = (pitch + 4 * (level + .25) * pitchMomentum);
        roll = (roll + 4 * (level + .25) * rollMomentum);

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
        if (stampEffect == 0) {
          stampEffect = 1;
        }
        if (stampEffect == 1) {
          stampEffect = 2;
        }
        if (stampEffect == 2) {
          stampEffect = 1;
        }
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

      // Contour logic ('animates' the contours pulsing)
      contourCounter += 4 * level;
      if (contourCounter >= 100) {
        contourCounter = 0;
      }

      double thresholdFactor = (config.domeRadialSize / 4) + level + .01; // tweak this
      double threshold = 2 / thresholdFactor;

      // Big pixel painting loop
      for (int i = 0; i < buffer.pixels.Length; i++) {
        // 1. Convert the current pixel in the buffer to its coordinates
        var p = buffer.pixels[i];
        var x = 2 * p.x - 1; // now centered on (0, 0) and with range [-1, 1]
        var y = 1 - 2 * p.y; // this is because in the original mapping x, y come "out of" the top left corner
        float z = (x * x + y * y) > 1 ? 0 : (float)Math.Sqrt(1 - x * x - y * y);
        Vector3 pixelPoint = new Vector3((float)x, (float)y, z);

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
            if (devices[deviceId].actionFlag == 1 | devices[deviceId].actionFlag == 2 | devices[deviceId].actionFlag == 3) {
              scale = scale * 4; // 'bonus' from button press; dial this in later
            }
            if (devices[deviceId].deviceType == 2) {
              scale = scale * (4 * (1 + devices[deviceId].rotationalSpeed)); // 'bonus' from spinning poi faster
              if (orientationInput.onlyPoi()) {
                scale /= 2; // Cut scale in half if non-poi are connected
                // there might be a race condition here, since `devices` is hard copied at this point in the loop while `orientationInput` keeps going
              }
            }
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
        double metaballhue = (256 * (1 + colorCenter.W) / 2) / 256d;
        // 'absolute' metaball - just a crisp cutoff at threshold
        if (strength > 0) {
          // At the high volumes, desaturate
          double saturation = Clamp(1.3 / level - 1, .2, 1);
          Color color = new Color(metaballhue, saturation, 1);
          buffer.pixels[i].color = Color.BlendLightPaint(new Color(buffer.pixels[i].color), color).ToInt();
        }

        // Contour - highlight level curves of the potential field
        double potentialContours = Math.Log(1000 * (potential - .5)) + contourCounter / 100;
        double contourBracket = Math.Truncate(potentialContours);
        double contourValue = potentialContours - contourBracket;
        if (config.orientationShowContours & contourValue < .2) {
          Color color = new Color(metaballhue, .4, .8 - Clamp(1 - contourBracket / 10, 0, .8));
          buffer.pixels[i].color = Color.BlendLightPaint(new Color(buffer.pixels[i].color), color).ToInt();
        }

        // Ripple_follow - global color wave that follows a spot
        double rippleRadius = rippleCounter / 300d;
        if (CloseTo(Vector3.Distance(Vector3.Transform(pixelPoint, rippleCenter), spot), rippleRadius, .01)) {
          double saturation = Clamp(1 - rippleCounter / 600d, 0, 1);
          double value = Clamp(1 - rippleCounter / 800d, 0, 1);
          Color color = new Color(metaballhue, saturation, value);
          buffer.pixels[i].color = Color.BlendLightPaint(new Color(buffer.pixels[i].color), color).ToInt();
        }

        // Planets
        foreach (Planet planet in planets) {
          double distance = Vector3.Distance(pixelPoint, planet.dome_position);
          if (distance < config.orientationPlanetVisualSize) {
            Color color = new Color(1, .01, 1);
            buffer.pixels[i].color = color.ToInt();
          }
        }
          
        // # Ring stamps - shapes that appear based on sensor facing
        if (stampFired) {
          if (stampEffect == 1) {
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
      }

      // Finished pixel iterations - clean up
      if (cooldown < 7 & stampEffect == 1) {
        stampFired = false;
      }
      // Update planets
      planets.RemoveWhere(p => p.cleanup);
      foreach (Planet p in planets) {
        double dome_gravity = config.orientationDomeG / p.position.LengthSquared();
        // Cap acceleration to avoid 'divide by zero' type issues
        dome_gravity = Clamp(dome_gravity, -100, 100);
        Vector2 acceleration = (float)dome_gravity * -p.position;
        if (idle) {
          Vector3 device_position = Vector3.Transform(spot, Quaternion.Inverse(currentOrientation));
          Vector2 device_separation = new Vector2(device_position.X, device_position.Y) - p.position;
          double wand_gravity = config.orientationWandG / device_separation.LengthSquared();
          wand_gravity = Clamp(wand_gravity, -100, 100);
          acceleration += (float)wand_gravity * device_separation;
        } else {
          foreach (int deviceId in devices.Keys) {
            Quaternion orientation = devices[deviceId].currentRotation();
            Vector3 device_position = Vector3.Transform(spot, Quaternion.Inverse(orientation));
            Vector2 device_separation = new Vector2(device_position.X, device_position.Y) - p.position;
            double wand_gravity = config.orientationWandG / device_separation.LengthSquared();
            wand_gravity = Clamp(wand_gravity, -100, 100);
            acceleration += (float)wand_gravity * device_separation;
          }
        }
        p.update(acceleration);
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
