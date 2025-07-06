#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Schema;
using OpenRA.Mods.Common.MapGenerator;
using OpenRA.Mods.Common.Terrain;
using OpenRA.Mods.Common.Traits;
using OpenRA.Support;
using OpenRA.Traits;
using static OpenRA.Mods.Common.Traits.ResourceLayerInfo;

namespace OpenRA.Mods.D2k.Traits
{
	[TraitLocation(SystemActors.EditorWorld)]
	public sealed class D2kMapGeneratorInfo : TraitInfo<D2kMapGenerator>, IEditorMapGeneratorInfo, IEditorToolInfo
	{
		[FieldLoader.Require]
		public readonly string Type = null;

		[FieldLoader.Require]
		[FluentReference]
		public readonly string Name = null;

		[FieldLoader.Require]
		[Desc("Tilesets that are compatible with this map generator.")]
		public readonly string[] Tilesets = null;

		[FluentReference]
		[Desc("The title to use for generated maps.")]
		public readonly string MapTitle = "label-random-map";

		[Desc("The widget tree to open when the tool is selected.")]
		public readonly string PanelWidget = "MAP_GENERATOR_TOOL_PANEL";

		// This is purely of interest to the linter.
		[FieldLoader.LoadUsing(nameof(FluentReferencesLoader))]
		[FluentReference]
		public readonly List<string> FluentReferences = null;

		[FieldLoader.LoadUsing(nameof(SettingsLoader))]
		public readonly MiniYaml Settings;

		string IMapGeneratorInfo.Type => Type;
		string IMapGeneratorInfo.Name => Name;
		string IMapGeneratorInfo.MapTitle => MapTitle;
		string[] IEditorMapGeneratorInfo.Tilesets => Tilesets;

		static MiniYaml SettingsLoader(MiniYaml my)
		{
			return my.NodeWithKey("Settings").Value;
		}

		static List<string> FluentReferencesLoader(MiniYaml my)
		{
			return new MapGeneratorSettings(null, my.NodeWithKey("Settings").Value)
				.Options.SelectMany(o => o.GetFluentReferences()).ToList();
		}

		const int FractionMax = Terraformer.FractionMax;

		sealed class Parameters
		{
			[FieldLoader.Require]
			public readonly int Seed = default;
			[FieldLoader.Require]
			public readonly int Rotations = default;
			[FieldLoader.LoadUsing(nameof(MirrorLoader))]
			public readonly Symmetry.Mirror Mirror = default;

			[FieldLoader.Require]
			public readonly int TerrainFeatureSize = default;
			[FieldLoader.Require]
			public readonly int TerrainSmoothing = default;
			[FieldLoader.Require]
			public readonly int SmoothingThreshold = default;
			[FieldLoader.Require]
			public readonly int RockRoughness = default;
			[FieldLoader.Require]
			public readonly int SandRoughness = default;
			[FieldLoader.Require]
			public readonly int RoughnessRadius = default;
			[FieldLoader.Require]
			public readonly int Rock = default;
			[FieldLoader.Require]
			public readonly int SandCliffs = default;
			[FieldLoader.Require]
			public readonly int MinimumRockStraight = default;
			[FieldLoader.Require]
			public readonly int MinimumSandCliffStraight = default;
			[FieldLoader.Require]
			public readonly int MinimumRockSandThickness = default;
			[FieldLoader.Require]
			public readonly int MinimumSandCliffThickness = default;
			[FieldLoader.Require]
			public readonly int MinimumRockSmoothLength = default;
			[FieldLoader.Require]
			public readonly int MinimumSandRockCliffLength = default;
			[FieldLoader.Require]
			public readonly int MinimumSandSandCliffLength = default;
			[FieldLoader.Require]
			public readonly int MinimumSandLength = default;
			[FieldLoader.Require]
			public readonly int SandContourSpacing = default;

			[FieldLoader.Require]
			public readonly ushort SandTile = default;
			[FieldLoader.Require]
			public readonly ushort RockTile = default;
			[FieldLoader.Require]
			public readonly string RockSmoothSegmentType = default;
			[FieldLoader.Require]
			public readonly string SandRockCliffSegmentType = default;
			[FieldLoader.Require]
			public readonly string SandSandCliffSegmentType = default;
			[FieldLoader.Require]
			public readonly string SandSegmentType = default;
			[FieldLoader.Ignore]
			public readonly IReadOnlyList<MultiBrush> SegmentedBrushes;

			public Parameters(Map map, MiniYaml my)
			{
				FieldLoader.Load(this, my);

				SegmentedBrushes = MultiBrush.LoadCollection(map, "Segmented");
			}

			static object MirrorLoader(MiniYaml my)
			{
				if (Symmetry.TryParseMirror(my.NodeWithKey("Mirror").Value.Value, out var mirror))
					return mirror;
				else
					throw new YamlException($"Invalid Mirror value `{my.NodeWithKey("Mirror").Value.Value}`");
			}

			static IReadOnlyDictionary<string, int> BuildingWeightsLoader(MiniYaml my)
			{
				return my.NodeWithKey("BuildingWeights").Value.ToDictionary(subMy =>
					{
						if (Exts.TryParseInt32Invariant(subMy.Value, out var f))
							return f;
						else
							throw new YamlException($"Invalid building weight `{subMy.Value}`");
					});
			}

			static IReadOnlyDictionary<string, int> ResourceSpawnWeightsLoader(MiniYaml my)
			{
				return my.NodeWithKey("ResourceSpawnWeights").Value.ToDictionary(subMy =>
					{
						if (Exts.TryParseInt32Invariant(subMy.Value, out var f))
							return f;
						else
							throw new YamlException($"Invalid resource spawn weight `{subMy.Value}`");
					});
			}
		}

		public IMapGeneratorSettings GetSettings()
		{
			return new MapGeneratorSettings(this, Settings);
		}

		public Map Generate(ModData modData, MapGenerationArgs args)
		{
			var terrainInfo = modData.DefaultTerrainInfo[args.Tileset];
			var size = args.Size;

			var map = new Map(modData, terrainInfo, size);
			var actorPlans = new List<ActorPlan>();

			var param = new Parameters(map, args.Settings);

			var terraformer = new Terraformer(args, map, modData, actorPlans, param.Mirror, param.Rotations);

			var sandZone = new Terraformer.PathPartitionZone()
			{
				ShouldTile = false,
				SegmentType = param.SandSegmentType,
				MinimumLength = param.MinimumSandLength,
			};
			var rockSmoothZone = new Terraformer.PathPartitionZone()
			{
				SegmentType = param.RockSmoothSegmentType,
				MinimumLength = param.MinimumRockSmoothLength,
				MaximumDeviation = 10,
			};
			var sandRockCliffZone = new Terraformer.PathPartitionZone()
			{
				SegmentType = param.SandRockCliffSegmentType,
				MinimumLength = param.MinimumSandRockCliffLength,
				MaximumDeviation = 10,
			};
			var sandSandCliffZone = new Terraformer.PathPartitionZone()
			{
				SegmentType = param.SandSandCliffSegmentType,
				MinimumLength = param.MinimumSandRockCliffLength,
				MaximumDeviation = 10,
			};

			// Use `random` to derive separate independent random number generators.
			//
			// This prevents changes in one part of the algorithm from affecting randomness in
			// other parts and provides flexibility for future parallel processing.
			//
			// In order to maximize stability, additions should be appended only. Disused
			// derivatives may be deleted but should be replaced with their unused call to
			// random.Next(). All generators should be created unconditionally.
			var random = new MersenneTwister(param.Seed);
			var pickAnyRandom = new MersenneTwister(random.Next());
			var elevationRandom = new MersenneTwister(random.Next());
			var rockTilingRandom = new MersenneTwister(random.Next());
			var sandSandCliffTilingRandom = new MersenneTwister(random.Next());

			terraformer.InitMap();

			foreach (var mpos in map.AllCells.MapCoords)
				map.Tiles[mpos] = terraformer.PickTile(pickAnyRandom, param.SandTile);

			var elevation = terraformer.ElevationNoiseMatrix(
				elevationRandom,
				param.TerrainFeatureSize,
				param.TerrainSmoothing);
			var roughnessMatrix = MatrixUtils.GridVariance(
				elevation,
				param.RoughnessRadius);

			CellLayer<Terraformer.Side> rockSmoothSand;
			{
				var cliffMask = MatrixUtils.CalibratedBooleanThreshold(
					roughnessMatrix,
					param.RockRoughness, FractionMax);
				var plan = terraformer.SliceElevation(elevation, null, param.Rock);
				plan = MatrixUtils.BooleanBlotch(
					plan,
					param.TerrainSmoothing,
					param.SmoothingThreshold, /*smoothingThresholdOutOf=*/FractionMax,
					param.MinimumRockSandThickness,
					true);
				var contours = MatrixUtils.BordersToPoints(plan);
				var partitionMask = cliffMask.Map(masked => masked ? sandRockCliffZone : rockSmoothZone);
				var tilingPaths = terraformer.PartitionPaths(
					contours,
					[rockSmoothZone, sandRockCliffZone],
					partitionMask,
					param.SegmentedBrushes,
					param.MinimumRockStraight);
				foreach (var tilingPath in tilingPaths)
					tilingPath
						.OptimizeLoop()
						.ExtendEdge(4);

				rockSmoothSand = terraformer.PaintLoopsAndFill(
					rockTilingRandom,
					tilingPaths,
					plan[0] ? Terraformer.Side.In : Terraformer.Side.Out,
					null,
					[new MultiBrush().WithTemplate(map, param.RockTile, CVec.Zero)])
						?? throw new MapGenerationException("Could not fit tiles for rock platforms");
			}

			{
				var inverseElevation = elevation.Map(v => -v);
				var cliffMask = MatrixUtils.CalibratedBooleanThreshold(
					roughnessMatrix,
					param.SandRoughness, FractionMax);
				var plan = terraformer.SliceElevation(
					inverseElevation,
					CellLayerUtils.ToMatrix(rockSmoothSand, Terraformer.Side.Out)
						.Map(s => s == Terraformer.Side.Out),
					param.SandCliffs,
					param.SandContourSpacing);
				plan = MatrixUtils.BooleanBlotch(
					plan,
					param.TerrainSmoothing,
					param.SmoothingThreshold, /*smoothingThresholdOutOf=*/FractionMax,
					param.MinimumSandCliffThickness,
					true);
				var contours = MatrixUtils.BordersToPoints(plan);
				var partitionMask = cliffMask.Map(masked => masked ? sandSandCliffZone : sandZone);
				var tilingPaths = terraformer.PartitionPaths(
					contours,
					[sandSandCliffZone, sandZone],
					partitionMask,
					param.SegmentedBrushes,
					param.MinimumSandCliffStraight);
				foreach (var tilingPath in tilingPaths)
				{
					var brush = tilingPath
						.OptimizeLoop()
						.ExtendEdge(4)
						.SetAutoEndDeviation()
						.Tile(sandSandCliffTilingRandom)
							?? throw new MapGenerationException("Could not fit tiles for sand-sand cliffs");
					terraformer.PaintTiling(pickAnyRandom, brush);
				}
			}

			terraformer.BakeMap();

			return map;
		}

		string IEditorToolInfo.Label => Name;
		string IEditorToolInfo.PanelWidget => PanelWidget;
	}

	public class D2kMapGenerator { /* we're only interested in the Info */ }
}
