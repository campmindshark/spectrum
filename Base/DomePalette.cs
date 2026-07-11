namespace Spectrum.Base {

  // A named snapshot of the live palette: the eight gradient pairs (slots 0-7 of
  // config.colorPalette) the VJ actually performs with. An XML-serializable POCO
  // persisted inside config.domePalettes, modeled on DomeScene — XSerializer
  // discovers it through the List<DomePalette> config property and already knows
  // how to write LEDColor, so no extra registration is needed.
  //
  // Colors holds exactly eight entries (see PaletteService); a null entry is a
  // deliberately empty slot, the same "no color" hole LEDColorPalette already
  // tolerates in the render path. Instances are treated as immutable once
  // stored: PaletteService deep-copies on both Save (so a preset never aliases
  // the live palette) and Apply (so a later live edit never mutates the preset).
  public class DomePalette {
    public string Name { get; set; }

    // Deep copy of live palette slots 0-7. Eight entries; a null entry = an
    // empty slot.
    public LEDColor[] Colors { get; set; }
  }

}
