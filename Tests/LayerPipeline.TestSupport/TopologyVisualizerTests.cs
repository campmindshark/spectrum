using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Spectrum.Base;
using Spectrum.LEDs;
using Spectrum.Visualizers;
using static Spectrum.LayerPipeline.Tests.TestAssertions;
using static Spectrum.LayerPipeline.Tests.LayerPipelineTestFixtures;

namespace Spectrum.LayerPipeline.Tests {

  public static class TopologyVisualizerTests {
    public static void Register(Action<string, Action> run) {
      run(nameof(ArcLightningUsesPhysicalGraph), ArcLightningUsesPhysicalGraph);
      run(nameof(GlassMosaicUsesTriangleTopology), GlassMosaicUsesTriangleTopology);
      run(nameof(CellularDomeUsesTriangleCells), CellularDomeUsesTriangleCells);
    }
    private static void ArcLightningUsesPhysicalGraph() {
      LayerDefinition? definition = DomeLayerCatalog.Metadata.Get("arc-lightning");
      Assert(definition != null && definition.DisplayName == "Arc Lightning",
        "Arc Lightning was not registered");
      Assert(definition.FireAction?.Label == "Fire" &&
          definition.ClearAction?.Label == "Clear",
        "Arc Lightning strike/clear actions were not registered");

      ArcLightningLayerOptions defaults =
        BuiltInOptions<ArcLightningLayerOptions>(
          Layer("arc-lightning", "arc-lightning-defaults"));
      Assert(defaults.BranchCount == 4,
        "unexpected Arc Lightning branch count");
      AssertClose(0.65, defaults.Jaggedness,
        "unexpected Arc Lightning jaggedness");
      Assert(defaults.Width == 2,
        "unexpected Arc Lightning width");
      AssertClose(0.4, defaults.Afterglow,
        "unexpected Arc Lightning afterglow");
      AssertClose(0.25, defaults.Duration,
        "unexpected Arc Lightning duration");
      Assert(defaults.Trigger == 1 && defaults.Button == 0 &&
          defaults.Level == 0.3 && defaults.Interval == 800 &&
          defaults.Palette == 0,
        "unexpected Arc Lightning trigger or palette default");

      DomeLayerSettings configured = Layer(
        "arc-lightning", "arc-lightning-clamped");
      configured.RendererParams = new Dictionary<string, double> {
        ["branchCount"] = 99,
        ["jaggedness"] = -1,
        ["width"] = 99,
        ["afterglow"] = -1,
        ["duration"] = 9,
        ["trigger"] = 99,
        ["button"] = 99,
        ["level"] = 9,
        ["interval"] = 1,
        ["palette"] = 99,
      };
      ArcLightningLayerOptions clamped =
        BuiltInOptions<ArcLightningLayerOptions>(configured);
      Assert(clamped.BranchCount == 12 && clamped.Jaggedness == 0 &&
          clamped.Width == 4 && clamped.Afterglow == 0 &&
          clamped.Duration == 1.5,
        "Arc Lightning shape/lifecycle controls did not clamp");
      Assert(clamped.Trigger == 2 && clamped.Button == 3 &&
          clamped.Level == 1 && clamped.Interval == 50 &&
          clamped.Palette == PaletteService.MaxPalettes - 1,
        "Arc Lightning trigger controls did not clamp");

      int[,] edges = new int[,] {
        { 0, 1 }, { 1, 2 }, { 2, 3 },
        { 1, 4 }, { 4, 5 }, { 5, 3 },
        { 2, 6 }, { 6, 7 },
      };
      var graph = new ArcLightningGraph(edges);
      var physicalGraph = new ArcLightningGraph(StrutLayoutFactory.lines);
      Assert(physicalGraph.VertexCount == 71 &&
          physicalGraph.Edges.Length == 190,
        "Arc Lightning did not map the deployed physical dome graph");
      ArcLightningPath path = graph.Route(
        0, 3, 2, 0, new Random(7));
      Assert(path.MainVertices[0] == 0 &&
          path.MainVertices[path.MainVertices.Length - 1] == 3,
        "Arc Lightning main route lost a selected pole");
      Assert(path.MainStruts.SequenceEqual(new[] { 0, 1, 2 }),
        "zero-jaggedness Arc Lightning did not use the shortest route");
      var usedStruts = new HashSet<int>(path.MainStruts);
      foreach (ImmutableArray<int> branch in path.BranchStruts) {
        Assert(branch.Length > 0,
          "Arc Lightning emitted an empty branch");
        foreach (int strut in branch) {
          Assert(usedStruts.Add(strut),
            "Arc Lightning branch reused a main or branch strut");
        }
      }
      Assert(path.BranchStruts.Length == 2,
        "Arc Lightning did not produce the requested available branches");
      Assert(graph.FarthestVertex(0) == 7,
        "Arc Lightning fallback pole was not graph-distant");

      ImmutableArray<ArcLightningPolePair> poleFan =
        LEDDomeArcLightningVisualizer.BuildPoleFan(
          new ArcLightningPole[] {
            new ArcLightningPole(30, 4),
            new ArcLightningPole(10, 1),
            new ArcLightningPole(40, 7),
            new ArcLightningPole(20, 3),
          }, 8);
      Assert(poleFan.SequenceEqual(new ArcLightningPolePair[] {
          new ArcLightningPolePair(1, 3),
          new ArcLightningPolePair(1, 4),
          new ArcLightningPolePair(1, 7),
        }),
        "Arc Lightning did not build a deterministic multi-wand pole fan");
      ImmutableArray<ArcLightningPolePair> cappedPoleFan =
        LEDDomeArcLightningVisualizer.BuildPoleFan(
          Enumerable.Range(0, 12)
            .Select(i => new ArcLightningPole(i, i)), 8);
      Assert(cappedPoleFan.Length == 8 &&
          cappedPoleFan[0] == new ArcLightningPolePair(0, 1) &&
          cappedPoleFan[7] == new ArcLightningPolePair(0, 8),
        "Arc Lightning multi-wand pole fan exceeded its strike bound");
      Assert(LEDDomeArcLightningVisualizer.BuildPoleFan(
          Array.Empty<ArcLightningPole>(), 8).IsEmpty &&
          LEDDomeArcLightningVisualizer.BuildPoleFan(
            new[] { new ArcLightningPole(8, 2) }, 8).IsEmpty,
        "Arc Lightning pole fan replaced a one/no-wand fallback");
      ArcLightningPolePair oneWandFallback =
        LEDDomeArcLightningVisualizer.FallbackPolePair(
          graph, new[] { new ArcLightningPole(8, 2) }, new Random(3));
      Assert(oneWandFallback == new ArcLightningPolePair(
          2, graph.FarthestVertex(2)),
        "Arc Lightning one-wand fallback lost its selected origin");
      ArcLightningPolePair noWandFallback =
        LEDDomeArcLightningVisualizer.FallbackPolePair(
          graph, Array.Empty<ArcLightningPole>(), new Random(3));
      Assert(noWandFallback.Origin >= 0 &&
          noWandFallback.Origin < graph.VertexCount &&
          noWandFallback.Destination ==
            graph.FarthestVertex(noWandFallback.Origin),
        "Arc Lightning hands-off fallback did not choose a distant pole");

      Assert(LEDDomeArcLightningVisualizer.VisibleEdgeCount(10, 0, 1) == 2,
        "Arc Lightning did not reveal an origin segment immediately");
      Assert(LEDDomeArcLightningVisualizer.VisibleEdgeCount(10, 0.5, 1) == 6,
        "Arc Lightning leading edge did not advance with strike age");
      Assert(LEDDomeArcLightningVisualizer.VisibleEdgeCount(10, 1, 1) == 10,
        "Arc Lightning did not reveal its full path by strike end");
      AssertClose(0,
        LEDDomeArcLightningVisualizer.AfterglowRetention(0, 0.1),
        "zero Arc Lightning afterglow retained old light");
      AssertClose(0.5,
        LEDDomeArcLightningVisualizer.AfterglowRetention(0.4, 0.4),
        "Arc Lightning afterglow is not a brightness half-life");

      var config = ConfigurationWithLayers(
        Layer("arc-lightning", "arc-lightning-inputs"));
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer? lightning = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "arc-lightning") {
          lightning = layer;
          break;
        }
      }
      Assert(lightning != null, "Arc Lightning renderer was not created");
      Input[] inputs = lightning.GetInputs();
      Assert(inputs.Length == 2 &&
          ReferenceEquals(inputs[0], runtime.AudioInput) &&
          ReferenceEquals(inputs[1], runtime.OrientationInput),
        "Arc Lightning did not declare audio and orientation inputs");
      ((Visualizer)lightning).Visualize();
    }

    private static void GlassMosaicUsesTriangleTopology() {
      LayerDefinition? definition = DomeLayerCatalog.Metadata.Get("glass-mosaic");
      Assert(definition != null && definition.DisplayName == "Glass Mosaic",
        "Glass Mosaic was not registered");
      Assert(definition.FireAction?.Label == "Fire" &&
          definition.ClearAction?.Label == "Clear",
        "Glass Mosaic Fire/Clear actions were not registered");

      GlassMosaicLayerOptions defaults =
        BuiltInOptions<GlassMosaicLayerOptions>(
          Layer("glass-mosaic", "glass-defaults"));
      Assert(defaults.TileGrouping == 1,
        "unexpected Glass Mosaic tile grouping");
      AssertClose(30, defaults.CascadeSpeed,
        "unexpected Glass Mosaic cascade speed");
      Assert(defaults.PropagationRule == 0,
        "unexpected Glass Mosaic propagation rule");
      AssertClose(0.18, defaults.BorderBrightness,
        "unexpected Glass Mosaic border brightness");
      Assert(defaults.TileTransition == 0,
        "unexpected Glass Mosaic tile transition");
      Assert(defaults.Trigger == 1 && defaults.Button == 0 &&
          defaults.Palette == 0,
        "unexpected Glass Mosaic trigger or palette default");
      AssertClose(0.3, defaults.Level,
        "unexpected Glass Mosaic audio level");
      AssertClose(800, defaults.Interval,
        "unexpected Glass Mosaic audio interval");

      DomeLayerSettings configured = Layer(
        "glass-mosaic", "glass-clamped");
      configured.RendererParams = new Dictionary<string, double> {
        ["tileGrouping"] = 99,
        ["cascadeSpeed"] = 0,
        ["propagationRule"] = 99,
        ["borderBrightness"] = 0,
        ["tileTransition"] = 99,
        ["trigger"] = 99,
        ["button"] = 99,
        ["level"] = -1,
        ["interval"] = 99999,
        ["palette"] = 99,
      };
      GlassMosaicLayerOptions clamped =
        BuiltInOptions<GlassMosaicLayerOptions>(configured);
      Assert(clamped.TileGrouping == 6 && clamped.CascadeSpeed == 1 &&
          clamped.PropagationRule == 2 &&
          clamped.BorderBrightness == 0.02 &&
          clamped.TileTransition == 1 &&
          clamped.Trigger == 2 && clamped.Button == 3 &&
          clamped.Level == 0 && clamped.Interval == 4000 &&
          clamped.Palette == PaletteService.MaxPalettes - 1,
        "Glass Mosaic controls did not clamp");

      var topology = new GlassMosaicTopology(StrutLayoutFactory.lines);
      Assert(topology.TileCount == 120 && topology.EdgeCount == 190,
        "Glass Mosaic did not discover the deployed triangular faces");
      int boundaryEdges = 0;
      for (int edge = 0; edge < topology.EdgeCount; edge++) {
        int incident = topology.EdgeAt(edge).Tiles.Length;
        Assert(incident == 1 || incident == 2,
          "Glass Mosaic found a strut outside the face manifold");
        if (incident == 1) {
          boundaryEdges++;
        }
      }
      Assert(boundaryEdges == 20,
        "Glass Mosaic did not preserve the 20-strut dome rim");
      for (int tile = 0; tile < topology.TileCount; tile++) {
        Assert(topology.TileAt(tile).Struts.Length == 3,
          "Glass Mosaic created a non-triangular tile");
        foreach (int neighbor in topology.NeighborsAt(tile)) {
          Assert(topology.NeighborsAt(neighbor).Contains(tile),
            "Glass Mosaic face adjacency was not symmetric");
        }
      }

      var state = new GlassMosaicState(topology, 17);
      Assert(state.PhaseAt(0) == 0 && state.PhaseAt(9) == 1,
        "Glass Mosaic did not initialize persistent tile phases");
      state.StartCascade(0, 1, 0);
      ImmutableArray<int> waveOrder = state.LastCascadeTileOrder;
      Assert(waveOrder.Length == topology.TileCount &&
          waveOrder.Distinct().Count() == topology.TileCount,
        "Glass Mosaic neighbor wave did not cover each tile once");
      var reached = new HashSet<int> { waveOrder[0] };
      for (int index = 1; index < waveOrder.Length; index++) {
        int tile = waveOrder[index];
        Assert(topology.NeighborsAt(tile).Any(reached.Contains),
          "Glass Mosaic cascade jumped across disconnected faces");
        reached.Add(tile);
      }
      int secondTile = waveOrder[1];
      int secondInitialPhase = secondTile & 7;
      Assert(state.PhaseAt(0) == 1 && state.PreviousPhaseAt(0) == 0 &&
          state.PulseAt(0) == 1 && state.FlipProgressAt(0) == 0 &&
          state.PhaseAt(secondTile) == secondInitialPhase,
        "Glass Mosaic did not flip only its starting tile immediately");
      GlassMosaicTileAppearance instant =
        LEDDomeGlassMosaicVisualizer.TileAppearance(0, 1, 0, 0);
      Assert(instant.Phase == 1 && instant.Visibility == 1,
        "Glass Mosaic instant transition changed legacy phase rendering");
      GlassMosaicTileAppearance oldFace =
        LEDDomeGlassMosaicVisualizer.TileAppearance(0, 1, 0.25, 1);
      GlassMosaicTileAppearance edgeOn =
        LEDDomeGlassMosaicVisualizer.TileAppearance(0, 1, 0.5, 1);
      GlassMosaicTileAppearance newFace =
        LEDDomeGlassMosaicVisualizer.TileAppearance(0, 1, 0.75, 1);
      Assert(oldFace.Phase == 0 && newFace.Phase == 1,
        "Glass Mosaic flip did not select the old and new tile faces");
      AssertClose(0.5, oldFace.Visibility,
        "Glass Mosaic old face did not narrow toward edge-on");
      AssertClose(0, edgeOn.Visibility,
        "Glass Mosaic flip did not become invisible at edge-on");
      AssertClose(0.5, newFace.Visibility,
        "Glass Mosaic new face did not open from edge-on");
      state.AdvanceFlips(GlassMosaicState.FlipDurationSeconds * 0.25);
      AssertClose(0.25, state.FlipProgressAt(0),
        "Glass Mosaic flip did not advance with elapsed time");
      state.AdvanceCascade(0.99);
      Assert(state.PhaseAt(secondTile) == secondInitialPhase,
        "Glass Mosaic cascade ignored its tile-rate budget");
      state.AdvanceCascade(0.01);
      Assert(state.PhaseAt(secondTile) == ((secondInitialPhase + 1) & 7),
        "Glass Mosaic cascade did not advance at one tile of budget");
      state.DecayPulses(0.325);
      AssertClose(0.5, state.PulseAt(0),
        "Glass Mosaic arrival pulse did not decay independently");

      state.Reset();
      state.StartCascade(0, 3, 1);
      int groupedArrivals = Enumerable.Range(0, topology.TileCount)
        .Count(tile => state.PulseAt(tile) == 1);
      Assert(groupedArrivals == 3,
        "Glass Mosaic grouping did not flip one connected tile group");
      Assert(Enumerable.Range(0, topology.TileCount)
          .Count(tile => state.FlipProgressAt(tile) == 0) == 3,
        "Glass Mosaic grouped arrivals did not begin together");
      state.StartCascade(0, 1, 2);
      Assert(state.LastCascadeTileOrder.Length == topology.TileCount &&
          state.LastCascadeTileOrder.Distinct().Count() ==
            topology.TileCount,
        "Glass Mosaic randomized domino rule lost or duplicated tiles");
      state.Reset();
      Assert(Enumerable.Range(0, topology.TileCount)
          .All(tile => state.PulseAt(tile) == 0 &&
            state.PhaseAt(tile) == (tile & 7) &&
            state.PreviousPhaseAt(tile) == (tile & 7) &&
            state.FlipProgressAt(tile) == 1),
        "Glass Mosaic Clear did not restore its initial state");

      AssertClose(0.2,
        LEDDomeGlassMosaicVisualizer.TileBrightness(0.2, 0),
        "Glass Mosaic resting border brightness changed");
      AssertClose(1,
        LEDDomeGlassMosaicVisualizer.TileBrightness(0.2, 1),
        "Glass Mosaic arrival did not reach full brightness");
      Assert(LEDDomeGlassMosaicVisualizer.BlendColor(
          0xFF0000, 0x0000FF, 0.5) == 0x800080,
        "Glass Mosaic did not interpolate adjacent tile colors");

      var config = ConfigurationWithLayers(
        Layer("glass-mosaic", "glass-inputs"));
      SetPaletteColors(config, color => 0xFFFFFF - color * 0x10101);
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer? mosaic = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "glass-mosaic") {
          mosaic = layer;
          break;
        }
      }
      Assert(mosaic != null, "Glass Mosaic renderer was not created");
      Input[] inputs = mosaic.GetInputs();
      Assert(inputs.Length == 2 &&
          ReferenceEquals(inputs[0], runtime.AudioInput) &&
          ReferenceEquals(inputs[1], runtime.OrientationInput),
        "Glass Mosaic did not declare audio and wand inputs");
      ((Visualizer)mosaic).Visualize();
      Assert(mosaic.LayerBuffer.pixels.Any(pixel => pixel.color != 0),
        "Glass Mosaic did not render its resting stained-glass borders");
    }

    private static void CellularDomeUsesTriangleCells() {
      LayerDefinition? definition = DomeLayerCatalog.Metadata.Get("cellular-dome");
      Assert(definition != null && definition.DisplayName == "Cellular Dome",
        "Cellular Dome was not registered");
      Assert(definition.FireAction?.Label == "Fire" &&
          definition.ClearAction?.Label == "Clear",
        "Cellular Dome Fire/Clear actions were not registered");

      CellularDomeLayerOptions defaults =
        BuiltInOptions<CellularDomeLayerOptions>(
          Layer("cellular-dome", "cellular-defaults"));
      Assert(defaults.Rule == 0 && defaults.Neighborhood == 0,
        "unexpected Cellular Dome rule or neighborhood");
      AssertClose(6, defaults.GenerationRate,
        "unexpected Cellular Dome generation rate");
      Assert(defaults.BirthColor == 0 && defaults.TriggerMode == 0 &&
          defaults.Palette == 0,
        "unexpected Cellular Dome color, trigger, or palette");
      AssertClose(2.5, defaults.AgeDecay,
        "unexpected Cellular Dome age decay");

      DomeLayerSettings configured = Layer(
        "cellular-dome", "cellular-clamped");
      configured.RendererParams = new Dictionary<string, double> {
        ["rule"] = 99,
        ["neighborhood"] = 99,
        ["generationRate"] = -1,
        ["birthColor"] = 99,
        ["ageDecay"] = 0,
        ["triggerMode"] = 99,
        ["palette"] = 99,
      };
      CellularDomeLayerOptions clamped =
        BuiltInOptions<CellularDomeLayerOptions>(configured);
      Assert(clamped.Rule == 3 && clamped.Neighborhood == 1 &&
          clamped.GenerationRate == 0 && clamped.BirthColor == 7 &&
          clamped.AgeDecay == 0.1 && clamped.TriggerMode == 2 &&
          clamped.Palette == PaletteService.MaxPalettes - 1,
        "Cellular Dome controls did not clamp");

      var topology = new GlassMosaicTopology(StrutLayoutFactory.lines);
      bool foundWiderNeighborhood = false;
      for (int tile = 0; tile < topology.TileCount; tile++) {
        ImmutableArray<int> edgeNeighbors = topology.NeighborsAt(tile);
        ImmutableArray<int> vertexNeighbors =
          topology.VertexNeighborsAt(tile);
        Assert(edgeNeighbors.All(vertexNeighbors.Contains),
          "Cellular Dome vertex neighborhood lost an edge neighbor");
        if (vertexNeighbors.Length > edgeNeighbors.Length) {
          foundWiderNeighborhood = true;
        }
        foreach (int neighbor in vertexNeighbors) {
          Assert(topology.VertexNeighborsAt(neighbor).Contains(tile),
            "Cellular Dome vertex adjacency was not symmetric");
        }
      }
      Assert(foundWiderNeighborhood,
        "Cellular Dome neighborhoods did not produce distinct topologies");

      Assert(CellularDomeState.NextAlive(0, false, 2) &&
          CellularDomeState.NextAlive(0, true, 1) &&
          !CellularDomeState.NextAlive(0, true, 3),
        "Cellular Dome colony rule did not implement B2/S12");
      Assert(CellularDomeState.NextAlive(1, false, 1) &&
          !CellularDomeState.NextAlive(1, true, 1),
        "Cellular Dome oscillator rule did not implement B1/S0");
      Assert(CellularDomeState.NextAlive(2, true, 0) &&
          CellularDomeState.NextAlive(2, false, 1),
        "Cellular Dome front rule did not persist and expand");
      Assert(CellularDomeState.NextAlive(3, false, 3) &&
          !CellularDomeState.NextAlive(3, false, 2),
        "Cellular Dome chaos rule did not implement B13 births");

      var state = new CellularDomeState(topology);
      state.Clear();
      state.SeedAt(0, 0, 0);
      Assert(state.AliveCount == 1 && state.AliveAt(0) &&
          state.BrightnessAt(0) == 1,
        "Cellular Dome did not seed one physical face");
      int edgeCount = topology.NeighborsAt(0).Length;
      state.Step(2, 0);
      Assert(state.AliveCount == edgeCount + 1 && state.AliveAt(0) &&
          topology.NeighborsAt(0).All(state.AliveAt),
        "Cellular Dome traveling front did not cross shared struts");

      state.Clear();
      state.SeedAt(0, 1, 1);
      Assert(state.AliveCount ==
          topology.VertexNeighborsAt(0).Length + 1,
        "Cellular Dome vertex brush did not use the selected neighborhood");
      state.EraseAt(0, 1, 1);
      Assert(state.AliveCount == 0 && state.BrightnessAt(0) == 0,
        "Cellular Dome erase did not remove the colony and its light");

      state.SeedAt(0, 0, 0);
      state.AdvanceAppearance(2, 2);
      AssertClose(0.59, state.BrightnessAt(0),
        "Cellular Dome live-cell age did not decay by one half-life");
      Assert(state.ColorPhaseAt(0, 3, 2) == 4,
        "Cellular Dome age did not advance from the birth color");
      state.MutateAt(0, 0, 0);
      Assert(!state.AliveAt(0) && state.BrightnessAt(0) > 0,
        "Cellular Dome mutation did not leave a dying afterimage");
      state.AdvanceAppearance(2, 2);
      AssertClose(0.295, state.BrightnessAt(0),
        "Cellular Dome dead-cell afterimage did not decay independently");

      state.Initialize();
      Assert(state.AliveCount > 0 &&
          state.AliveCount < topology.TileCount,
        "Cellular Dome initial colonies were empty or covered the dome");
      Assert(LEDDomeCellularDomeVisualizer.BlendColor(
          0xFF0000, 0x0000FF, 0.5) == 0x800080,
        "Cellular Dome did not blend adjacent cell colors");

      var config = ConfigurationWithLayers(
        Layer("cellular-dome", "cellular-inputs"));
      SetPaletteColors(config, color => 0xFFFFFF - color * 0x10101);
      var runtime = new global::Spectrum.Operator(config);
      DomeLayerVisualizer? cellular = null;
      foreach (Visualizer visualizer in runtime.DomeOutput.GetVisualizers()) {
        if (visualizer is DomeLayerVisualizer layer &&
            layer.LayerKey == "cellular-dome") {
          cellular = layer;
          break;
        }
      }
      Assert(cellular != null, "Cellular Dome renderer was not created");
      Input[] inputs = cellular.GetInputs();
      Assert(inputs.Length == 1 &&
          ReferenceEquals(inputs[0], runtime.OrientationInput),
        "Cellular Dome did not declare its wand input");
      ((Visualizer)cellular).Visualize();
      Assert(cellular.LayerBuffer.pixels.Any(pixel => pixel.color != 0),
        "Cellular Dome did not render its initial colonies");
    }

  }
}