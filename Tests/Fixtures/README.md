# OPC wire fixture

`Get-KnownGoodOpcBaseline.ps1` independently reconstructs a deterministic OPC
frame from the dome wiring and serializer in deployed commit
`72d68765ae2465724ed1958c8fa5f1709b95000b`.

The fixture assumptions match that revision's deployed defaults: channel 0,
`domeSkipLEDs=0`, and the original identity cable routing. Every one of the
7,580 logical LEDs receives a deterministic non-black RGB color. The historical
serializer fills unused strip addresses with black through the highest written
pixel, producing 25,504 TCP bytes with SHA-256:

```text
2d56011c81d89c182884d7a8d1fe869a81ff5a9b0e489c917eb8a1f0606e715f
```

The same generator also reconstructs `Iterate Through Struts` at the deployed
brightness (`domeMaxBrightness=1`,
`domeBrightness=0.356915762888129`). It covers all 190 struts in controller
plug order plus the next color-rollover frame: 191 frames and 4,871,264 exact
TCP bytes with sequence SHA-256:

```text
fc385d8219be2736a2b6e85fb6f58289be28aaadbb743c67556d147a44065f46
```

Run the audit from the repository root:

```powershell
Tests\Fixtures\Get-KnownGoodOpcBaseline.ps1
```

Pass `-OutputPath frame.opc` to also write the reconstructed binary frame for
inspection in a packet-analysis tool. Pass `-PatternOutputPath struts.opc` to
write the concatenated historical pattern sequence. The automated suite stores
only the hashes; its smaller protocol fixtures report the exact differing byte
when framing, RGB order, dense zero fill, or flush behavior regresses.

Set `SPECTRUM_OPC_CAPTURE_DIR` when running the tests to retain the current
identity and cable-remapped frames for byte-level comparison with that baseline.
