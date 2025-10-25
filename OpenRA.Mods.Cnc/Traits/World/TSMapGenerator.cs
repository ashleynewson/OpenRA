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
using OpenRA.Mods.Common.MapGenerator;
using OpenRA.Mods.Common.Terrain;
using OpenRA.Mods.Common.Traits;
using OpenRA.Support;
using OpenRA.Traits;
using static OpenRA.Mods.Common.Traits.ResourceLayerInfo;

namespace OpenRA.Mods.Cnc.Traits
{
	[TraitLocation(SystemActors.EditorWorld)]
	[Desc("A map generator that clears a map.")]
	public sealed class TSMapGeneratorInfo : TraitInfo, IEditorMapGeneratorInfo
	{
		[FieldLoader.Require]
		[Desc("Human-readable name this generator uses.")]
		[FluentReference]
		public readonly string Name = null;

		[FieldLoader.Require]
		[Desc("Internal id for this map generator.")]
		public readonly string Type = null;

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
		const int EntityBonusMax = 1000000;

		sealed class Parameters
		{
			[FieldLoader.Require]
			public readonly int Seed = default;
			[FieldLoader.Require]
			public readonly int Rotations = default;
			[FieldLoader.LoadUsing(nameof(MirrorLoader))]
			public readonly Symmetry.Mirror Mirror = default;
			[FieldLoader.Require]
			public readonly int Players = default;
			[FieldLoader.Require]
			public readonly int TerrainFeatureSize = default;
			[FieldLoader.Require]
			public readonly int ForestFeatureSize = default;
			[FieldLoader.Require]
			public readonly int ResourceFeatureSize = default;
			[FieldLoader.Require]
			public readonly int CivilianBuildingsFeatureSize = default;
			[FieldLoader.Require]
			public readonly int Water = default;
			[FieldLoader.Require]
			public readonly int Mountains = default;
			[FieldLoader.Require]
			public readonly int Forests = default;
			[FieldLoader.Require]
			public readonly int ForestCutout = default;
			[FieldLoader.Require]
			public readonly int MaximumCutoutSpacing = default;
			[FieldLoader.Require]
			public readonly int ExternalCircularBias = default;
			[FieldLoader.Require]
			public readonly int TerrainSmoothing = default;
			[FieldLoader.Require]
			public readonly int SmoothingThreshold = default;
			public readonly int MinimumCoastStraight = -1;
			[FieldLoader.Require]
			public readonly int MinimumLandSeaThickness = default;
			[FieldLoader.Require]
			public readonly int MinimumMountainThickness = default;
			[FieldLoader.Require]
			public readonly int MaximumAltitude = default;
			[FieldLoader.Require]
			public readonly int RoughnessRadius = default;
			[FieldLoader.Require]
			public readonly int Roughness = default;
			public readonly int WaterRoughness = 0;
			[FieldLoader.Require]
			public readonly int MinimumTerrainContourSpacing = default;
			public readonly int MinimumBeachLength = 0;
			public readonly int MinimumWaterCliffLength = 0;
			[FieldLoader.Require]
			public readonly int MinimumCliffLength = default;
			[FieldLoader.Require]
			public readonly int ForestClumpiness = default;
			[FieldLoader.Require]
			public readonly bool DenyWalledAreas = default;
			[FieldLoader.Require]
			public readonly int EnforceSymmetry = default;
			[FieldLoader.Require]
			public readonly bool Roads = default;
			[FieldLoader.Require]
			public readonly int RoadSpacing = default;
			[FieldLoader.Require]
			public readonly int RoadShrink = default;
			[FieldLoader.Require]
			public readonly bool CreateEntities = default;
			[FieldLoader.Require]
			public readonly int AreaEntityBonus = default;
			[FieldLoader.Require]
			public readonly int PlayerCountEntityBonus = default;
			[FieldLoader.Require]
			public readonly int CentralSpawnReservationFraction = default;
			[FieldLoader.Require]
			public readonly int ResourceSpawnReservation = default;
			[FieldLoader.Require]
			public readonly int SpawnRegionSize = default;
			[FieldLoader.Require]
			public readonly int SpawnBuildSize = default;
			[FieldLoader.Require]
			public readonly int MinimumSpawnRadius = default;
			[FieldLoader.Require]
			public readonly int SpawnResourceSpawns = default;
			[FieldLoader.Require]
			public readonly int SpawnReservation = default;
			[FieldLoader.Require]
			public readonly int SpawnResourceBias = default;
			[FieldLoader.Require]
			public readonly int ResourcesPerPlayer = default;
			[FieldLoader.Require]
			public readonly int OreUniformity = default;
			[FieldLoader.Require]
			public readonly int OreClumpiness = default;
			[FieldLoader.Require]
			public readonly int MaximumExpansionResourceSpawns = default;
			[FieldLoader.Require]
			public readonly int MaximumResourceSpawnsPerExpansion = default;
			[FieldLoader.Require]
			public readonly int MinimumExpansionSize = default;
			[FieldLoader.Require]
			public readonly int MaximumExpansionSize = default;
			[FieldLoader.Require]
			public readonly int ExpansionInner = default;
			[FieldLoader.Require]
			public readonly int ExpansionBorder = default;
			[FieldLoader.Require]
			public readonly int MinimumBuildings = default;
			[FieldLoader.Require]
			public readonly int MaximumBuildings = default;
			[FieldLoader.LoadUsing(nameof(BuildingWeightsLoader))]
			public readonly IReadOnlyDictionary<string, int> BuildingWeights = default;
			[FieldLoader.Require]
			public readonly int CivilianBuildings = default;
			[FieldLoader.Require]
			public readonly int CivilianBuildingDensity = default;
			[FieldLoader.Require]
			public readonly int MinimumCivilianBuildingDensity = default;
			[FieldLoader.Require]
			public readonly int CivilianBuildingDensityRadius = default;

			[FieldLoader.Require]
			public readonly ushort LandTile = default;
			[FieldLoader.Require]
			public readonly ushort WaterTile = default;
			[FieldLoader.Ignore]
			public readonly IReadOnlyList<MultiBrush> SegmentedBrushes;
			[FieldLoader.Ignore]
			public readonly IReadOnlyList<MultiBrush> ForestObstacles;
			[FieldLoader.Ignore]
			public readonly IReadOnlyList<MultiBrush> UnplayableObstacles;
			[FieldLoader.Ignore]
			public readonly IReadOnlyList<MultiBrush> CivilianBuildingsObstacles;
			[FieldLoader.Ignore]
			public readonly IReadOnlyDictionary<ushort, IReadOnlyList<MultiBrush>> RepaintTiles;

			[FieldLoader.Ignore]
			public readonly ResourceTypeInfo DefaultResource;
			[FieldLoader.Ignore]
			public readonly IReadOnlyDictionary<string, ResourceTypeInfo> ResourceSpawnSeeds;
			[FieldLoader.LoadUsing(nameof(ResourceSpawnWeightsLoader))]
			public readonly IReadOnlyDictionary<string, int> ResourceSpawnWeights = default;

			[FieldLoader.Ignore]
			public readonly IReadOnlySet<byte> ClearTerrain;
			[FieldLoader.Ignore]
			public readonly IReadOnlySet<byte> PlayableTerrain;
			[FieldLoader.Ignore]
			public readonly IReadOnlySet<byte> DominantTerrain;
			[FieldLoader.Ignore]
			public readonly IReadOnlySet<byte> ZoneableTerrain;
			[FieldLoader.Ignore]
			public readonly IReadOnlyList<string> ClearSegmentTypes;
			[FieldLoader.Ignore]
			public readonly IReadOnlyList<string> BeachSegmentTypes;
			[FieldLoader.Ignore]
			public readonly IReadOnlyList<string> WaterCliffSegmentTypes;
			[FieldLoader.Ignore]
			public readonly IReadOnlyList<string> CliffSegmentTypes;
			[FieldLoader.Ignore]
			public readonly IReadOnlyList<string> RoadSegmentTypes;

			public Parameters(Map map, MiniYaml my)
			{
				FieldLoader.Load(this, my);

				var terrainInfo = (ITemplatedTerrainInfo)map.Rules.TerrainInfo;
				SegmentedBrushes = MultiBrush.LoadCollection(map, "Segmented");
				ForestObstacles = MultiBrush.LoadCollection(map, my.NodeWithKey("ForestObstacles").Value.Value);
				UnplayableObstacles = MultiBrush.LoadCollection(map, my.NodeWithKey("UnplayableObstacles").Value.Value);
				CivilianBuildingsObstacles = MultiBrush.LoadCollection(map, my.NodeWithKey("CivilianBuildingsObstacles").Value.Value);
				RepaintTiles = my.NodeWithKeyOrDefault("RepaintTiles")?.Value.ToDictionary(
					k =>
					{
						if (Exts.TryParseUshortInvariant(k, out var tile))
							return tile;
						else
							throw new YamlException($"RepaintTile {k} is not a ushort");
					},
					v => MultiBrush.LoadCollection(map, v.Value) as IReadOnlyList<MultiBrush>);
				RepaintTiles ??= ImmutableDictionary<ushort, IReadOnlyList<MultiBrush>>.Empty;

				var resourceTypes = map.Rules.Actors[SystemActors.World].TraitInfoOrDefault<ResourceLayerInfo>().ResourceTypes;
				if (!resourceTypes.TryGetValue(my.NodeWithKey("DefaultResource").Value.Value, out DefaultResource))
					throw new YamlException("DefaultResource is not valid");
				var playerResourcesInfo = map.Rules.Actors[SystemActors.Player].TraitInfoOrDefault<PlayerResourcesInfo>();
				try
				{
					ResourceSpawnSeeds = my.NodeWithKey("ResourceSpawnSeeds").Value
						.ToDictionary(subMy => subMy.Value)
						.ToDictionary(kv => kv.Key, kv => resourceTypes[kv.Value]);
				}
				catch (KeyNotFoundException e)
				{
					throw new YamlException("Bad ResourceSpawnSeeds resource: " + e);
				}

				switch (Rotations)
				{
					case 1:
					case 2:
					case 4:
						break;
					default:
						EnforceSymmetry = 0;
						break;
				}

				IReadOnlySet<byte> ParseTerrainIndexes(string key)
				{
					return my.NodeWithKey(key).Value.Value
						.Split(',', StringSplitOptions.RemoveEmptyEntries)
						.Select(terrainInfo.GetTerrainIndex)
						.ToImmutableHashSet();
				}

				IReadOnlyList<string> ParseSegmentTypes(string key)
				{
					return my.NodeWithKey(key).Value.Value
						.Split(',', StringSplitOptions.RemoveEmptyEntries)
						.ToImmutableArray();
				}

				ClearTerrain = ParseTerrainIndexes("ClearTerrain");
				PlayableTerrain = ParseTerrainIndexes("PlayableTerrain");
				DominantTerrain = ParseTerrainIndexes("DominantTerrain");
				ZoneableTerrain = ParseTerrainIndexes("ZoneableTerrain");

				ClearSegmentTypes = ParseSegmentTypes("ClearSegmentTypes");
				BeachSegmentTypes = ParseSegmentTypes("BeachSegmentTypes");
				if (WaterRoughness > 0)
					WaterCliffSegmentTypes = ParseSegmentTypes("WaterCliffSegmentTypes");

				CliffSegmentTypes = ParseSegmentTypes("CliffSegmentTypes");
				RoadSegmentTypes = ParseSegmentTypes("RoadSegmentTypes");

				Validate(terrainInfo);
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

			public void Validate(ITemplatedTerrainInfo terrainInfo)
			{
				if (Rotations < 1)
					throw new MapGenerationException("Rotations must be >= 1");
				if (TerrainFeatureSize < 1)
					throw new MapGenerationException("TerrainFeatureSize must be >= 1");
				if (ForestFeatureSize < 1)
					throw new MapGenerationException("ForestFeatureSize must be >= 1");
				if (ResourceFeatureSize < 1)
					throw new MapGenerationException("ResourceFeatureSize must be >= 1");
				if (CivilianBuildingsFeatureSize < 1)
					throw new MapGenerationException("CivilianBuildingsFeatureSize must be >= 1");
				if (TerrainSmoothing < 0 || TerrainSmoothing > MatrixUtils.MaxBinomialKernelRadius)
					throw new MapGenerationException($"TerrainSmoothing must be between 0 and {MatrixUtils.MaxBinomialKernelRadius} inclusive");
				if (WaterRoughness > 0 && MinimumCoastStraight < 0)
					throw new MapGenerationException("MinimumCoastStraight must be >= 0");
				if (SmoothingThreshold < (FractionMax + 1) / 2 || SmoothingThreshold > FractionMax)
					throw new MapGenerationException($"SmoothingThreshold must be between {(FractionMax + 1) / 2} and {FractionMax} inclusive");
				if (MinimumLandSeaThickness < 1)
					throw new MapGenerationException("MinimumLandSeaThickness must be >= 1");
				if (MinimumMountainThickness < 1)
					throw new MapGenerationException("MinimumMountainThickness must be >= 1");
				if (Water < 0 || Water > FractionMax)
					throw new MapGenerationException($"Water must be between 0 and {FractionMax} inclusive");
				if (Forests < 0 || Forests > FractionMax)
					throw new MapGenerationException($"Forest must be between 0 and {FractionMax} inclusive");
				if (ForestCutout < 0)
					throw new MapGenerationException("ForestCutout must be >= 0");
				if (MaximumCutoutSpacing < 0)
					throw new MapGenerationException("TopologyAugmentationThreshold must be >= 0");
				if (ForestClumpiness < 0)
					throw new MapGenerationException("ForestClumpiness must be >= 0");
				if (Mountains < 0 || Mountains > FractionMax)
					throw new MapGenerationException($"Mountains must be between 0 and {FractionMax} inclusive");
				if (Roughness < 0 || Roughness > FractionMax)
					throw new MapGenerationException("Roughness must be between 0 and {FractionMax}");
				if (WaterRoughness < 0 || WaterRoughness > FractionMax)
					throw new MapGenerationException("WaterRoughness must be between 0 and {FractionMax}");
				if (RoughnessRadius < 1)
					throw new MapGenerationException("RoughnessRadius must be >= 1");
				if (MaximumAltitude < 0)
					throw new MapGenerationException("MaximumAltitude must be >= 0");
				if (MinimumTerrainContourSpacing < 0)
					throw new MapGenerationException("MinimumTerrainContourSpacing must be >= 0");
				if (WaterRoughness > 0 && MinimumBeachLength < 1)
					throw new MapGenerationException("MinimumBeachLength must be >= 1");
				if (WaterRoughness > 0 && MinimumCliffLength < 1)
					throw new MapGenerationException("MinimumWaterCliffLength must be >= 1");
				if (MinimumCliffLength < 1)
					throw new MapGenerationException("MinimumCliffLength must be >= 1");
				if (RoadSpacing < 0)
					throw new MapGenerationException("RoadSpacing must be >= 0");
				if (RoadShrink < 0)
					throw new MapGenerationException("RoadShrink must be >= 0");
				if (Players < 0)
					throw new MapGenerationException("Players must be >= 0");
				if (CentralSpawnReservationFraction < 0)
					throw new MapGenerationException("CentralSpawnReservationFraction must be >= 0");
				if (AreaEntityBonus < 0)
					throw new MapGenerationException("PlayableAreaDensityBonus must be >= 0");
				if (PlayerCountEntityBonus < 0)
					throw new MapGenerationException("PlayerCountDensityBonus must be >= 0");
				if (SpawnRegionSize < 1)
					throw new MapGenerationException("SpawnRegionSize must be >= 1");
				if (SpawnReservation < 1)
					throw new MapGenerationException("SpawnReservation must be >= 1");
				if (SpawnBuildSize < 1)
					throw new MapGenerationException("SpawnBuildSize must be >= 1");
				if (MinimumSpawnRadius < 1)
					throw new MapGenerationException("MinimumSpawnRadius must be >= 1");
				if (SpawnResourceSpawns < 0)
					throw new MapGenerationException("SpawnResourceSpawns must be >= 0");
				if (ResourceSpawnReservation < 1)
					throw new MapGenerationException("ResourceSpawnReservation must be >= 1");
				if (MaximumExpansionResourceSpawns < 0)
					throw new MapGenerationException("MaximumExpansionResourceSpawns must be >= 0");
				if (MinimumExpansionSize < 1)
					throw new MapGenerationException("MinimumExpansionSize must be >= 1");
				if (MaximumExpansionSize < 1)
					throw new MapGenerationException("MaximumExpansionSize must be >= 1");
				if (MinimumExpansionSize > MaximumExpansionSize)
					throw new MapGenerationException("MinimumExpansionSize must be <= maximumExpansionSize");
				if (ExpansionBorder < 1)
					throw new MapGenerationException("ExpansionBorder must be >= 1");
				if (ExpansionInner < 1)
					throw new MapGenerationException("ExpansionInner must be >= 1");
				if (MaximumResourceSpawnsPerExpansion < 1)
					throw new MapGenerationException("MaximumResourceSpawnsPerExpansion must be >= 1");
				if (MinimumBuildings < 0)
					throw new MapGenerationException("MinimumBuildings must be >= 0");
				if (MaximumBuildings < 0)
					throw new MapGenerationException("MaximumBuildings must be >= 0");
				if (MinimumBuildings > MaximumBuildings)
					throw new MapGenerationException("MinimumBuildings must be <= maximumBuildings");
				if (CivilianBuildings < 0 || CivilianBuildings > FractionMax)
					throw new MapGenerationException($"CivilianBuildings must be between 0 and {FractionMax} inclusive");
				if (CivilianBuildingDensity < 0 || CivilianBuildingDensity > FractionMax)
					throw new MapGenerationException($"CivilianBuildingDensity must be between 0 and {FractionMax} inclusive");
				if (MinimumCivilianBuildingDensity < 0 || MinimumCivilianBuildingDensity > FractionMax)
					throw new MapGenerationException($"MinimumCivilianBuildingDensity must be between 0 and {FractionMax} inclusive");
				if (CivilianBuildingDensityRadius < 0)
					throw new MapGenerationException("CivilianBuildingDensityRadius must be >= 0");
				if (ResourcesPerPlayer < 0)
					throw new MapGenerationException("ResourcesPerPlayer must be >= 0");
				if (OreUniformity < 0)
					throw new MapGenerationException("OreUniformity must be >= 0");
				if (OreClumpiness < 0)
					throw new MapGenerationException("OreClumpiness must be >= 0");
				foreach (var kv in BuildingWeights)
					if (kv.Value < 0)
						throw new MapGenerationException("BuildingWeights.* must be >= 0");
				foreach (var kv in ResourceSpawnWeights)
					if (kv.Value < 0)
						throw new MapGenerationException("ResourceSpawnWeights.* must be >= 0");
				foreach (var kv in ResourceSpawnWeights)
					if (!ResourceSpawnSeeds.ContainsKey(kv.Key))
						throw new MapGenerationException($"ResourceSpawnSeeds does not contain possible resource spawn `{kv.Key}`");

				if (!(terrainInfo.Templates.TryGetValue(LandTile, out var landTemplate) && landTemplate.Contains(0)))
					throw new MapGenerationException("LandTile is not valid");
				if (!(terrainInfo.Templates.TryGetValue(LandTile, out var waterTemplate) && waterTemplate.Contains(0)))
					throw new MapGenerationException("WaterTile is not valid");

				if (Players > 32)
					throw new MapGenerationException("Total number of players must not exceed 32");

				var symmetryCount = Symmetry.RotateAndMirrorProjectionCount(Rotations, Mirror);
				if (Players % symmetryCount != 0)
					throw new MapGenerationException($"Total number of players must be a multiple of {symmetryCount}");
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

			CellLayer<MultiBrush.Replaceability> PlayableToReplaceable()
			{
				var playable = terraformer.CheckSpace(param.PlayableTerrain, true);
				var basicLand = terraformer.CheckSpace(param.LandTile);
				var replace = new CellLayer<MultiBrush.Replaceability>(map);
				foreach (var mpos in map.AllCells.MapCoords)
					if (playable[mpos])
					{
						if (basicLand[mpos])
							replace[mpos] = MultiBrush.Replaceability.Any;
						else
							replace[mpos] = MultiBrush.Replaceability.Actor;
					}
					else
					{
						replace[mpos] = MultiBrush.Replaceability.None;
					}

				return replace;
			}

			var cellBounds = CellLayerUtils.CellBounds(map);
			int2 CVecToMatrixXY(CVec cvec)
			{
				return new int2(cvec.X, cvec.Y) - cellBounds.TopLeft;
			}

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
			var coastTilingRandom = new MersenneTwister(random.Next());
			var cliffTilingRandom = new MersenneTwister(random.Next());
			var rampTilingRandom = new MersenneTwister(random.Next());
			var forestRandom = new MersenneTwister(random.Next());
			var forestTilingRandom = new MersenneTwister(random.Next());
			var symmetryTilingRandom = new MersenneTwister(random.Next());
			var debrisTilingRandom = new MersenneTwister(random.Next());
			var resourceRandom = new MersenneTwister(random.Next());
			var roadTilingRandom = new MersenneTwister(random.Next());
			var playerRandom = new MersenneTwister(random.Next());
			var expansionRandom = new MersenneTwister(random.Next());
			var buildingRandom = new MersenneTwister(random.Next());
			var topologyRandom = new MersenneTwister(random.Next());
			var repaintRandom = new MersenneTwister(random.Next());
			var decorationRandom = new MersenneTwister(random.Next());
			var decorationTilingRandom = new MersenneTwister(random.Next());

			terraformer.InitMap();

			RampTiler rampTiler;
			{
				var templates = new ushort[] { 0, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57 };
				var brushes = templates
					.Select(t => new MultiBrush().WithTemplate(map, t, CVec.Zero))
					.ToList();
				rampTiler = new RampTiler(map, brushes);
			}

			var clearZone = new Terraformer.PathPartitionZone()
			{
				ShouldTile = false,
				SegmentType = param.ClearSegmentTypes[0],
				MinimumLength = 4,
			};
			var beachZone = new Terraformer.PathPartitionZone()
			{
				SegmentType = param.BeachSegmentTypes[0],
				MinimumLength = param.MinimumBeachLength,
				MaximumDeviation = param.MinimumLandSeaThickness - 1,
			};
			var cliffZone = new Terraformer.PathPartitionZone()
			{
				SegmentType = param.CliffSegmentTypes[0],
				MinimumLength = param.MinimumCliffLength,
				MaximumDeviation = param.MinimumMountainThickness - 1,
			};

			foreach (var mpos in map.AllCells.MapCoords)
				map.Tiles[mpos] = terraformer.PickTile(random, param.LandTile);

			var elevation = terraformer.ElevationNoiseMatrix(
				elevationRandom,
				param.TerrainFeatureSize,
				param.TerrainSmoothing);
			var roughnessMatrix = MatrixUtils.GridVariance(
				elevation,
				param.RoughnessRadius);

			var landPlan = terraformer.SliceElevation(elevation, null, FractionMax - param.Water);
			landPlan = MatrixUtils.BooleanBlotch(
				landPlan,
				param.TerrainSmoothing,
				param.SmoothingThreshold, /*smoothingThresholdOutOf=*/FractionMax,
				param.MinimumLandSeaThickness,
				/*bias=*/param.Water <= FractionMax / 2);

			var elevationCalibration =
				Enumerable.Zip(
					landPlan.Data,
					elevation.Data,
					(p, e) => p ? e : int.MaxValue)
				.Min();
			elevation = elevation.Map(v => v - elevationCalibration);

			var heightMap = new RampTiler.HeightMap(map);

			var coast = MatrixUtils.BordersToPoints(landPlan);
			List<TilingPath> coastPaths;
			if (param.WaterRoughness > 0)
			{
				var waterCliffZone = new Terraformer.PathPartitionZone()
				{
					SegmentType = param.WaterCliffSegmentTypes[0],
					MinimumLength = param.MinimumCliffLength,
					MaximumDeviation = param.MinimumLandSeaThickness - 1,
				};
				var waterCliffMask = MatrixUtils.CalibratedBooleanThreshold(
					roughnessMatrix,
					param.WaterRoughness, FractionMax);
				var partitionMask = waterCliffMask.Map(masked => masked ? waterCliffZone : beachZone);
				coastPaths = terraformer.PartitionPaths(
					coast,
					[beachZone, waterCliffZone],
					partitionMask,
					param.SegmentedBrushes,
					param.MinimumCoastStraight);

				foreach (var coastPath in coastPaths)
					coastPath
						.OptimizeLoop()
						.ExtendEdge(4);
			}
			else
			{
				coastPaths = CellLayerUtils.FromMatrixPoints(coast, map.Tiles)
					.Select(beach =>
						TilingPath.QuickCreate(
								map,
								param.SegmentedBrushes,
								beach,
								param.MinimumLandSeaThickness - 1,
								param.BeachSegmentTypes[0],
								param.BeachSegmentTypes[0])
									.ExtendEdge(4))
					.ToList();
			}

			var landCoastWater = terraformer.PaintLoopsAndFill(
				coastTilingRandom,
				coastPaths,
				landPlan[0] ? Terraformer.Side.In : Terraformer.Side.Out,
				[new MultiBrush().WithTemplate(map, param.WaterTile, CVec.Zero)],
				null)
					?? throw new MapGenerationException("Could not fit tiles for coast");

			heightMap.SetCellHeights(
				(byte)(param.WaterRoughness > 0 ? 4 : 0),
				CellLayerUtils.Map(landCoastWater, v => v == Terraformer.Side.In));

			heightMap.MarkUntileable(
				CellLayerUtils.Map(landCoastWater, v => v != Terraformer.Side.In));

			if (param.Mountains > 0)
			{
				var cliffMask = MatrixUtils.CalibratedBooleanThreshold(
					roughnessMatrix,
					param.Roughness, FractionMax);
				var plan = landPlan;

				for (var altitude = 0; altitude < param.MaximumAltitude; altitude++)
				{
					plan = terraformer.SliceElevation(
						elevation,
						plan,
						param.Mountains,
						param.MinimumTerrainContourSpacing);
					plan = MatrixUtils.BooleanBlotch(
						plan,
						param.TerrainSmoothing,
						param.SmoothingThreshold, /*smoothingThresholdOutOf=*/FractionMax,
						param.MinimumMountainThickness,
						/*bias=*/false);

					var planCellLayer = new CellLayer<bool>(map);
					CellLayerUtils.FromMatrix(planCellLayer, plan);

					var contours = MatrixUtils.BordersToPoints(plan);
					var partitionMask = cliffMask.Map(masked => masked ? cliffZone : clearZone);

					var shortContours = new List<int2[]>();
					var tallContours = new List<int2[]>();

					foreach (var contour in contours)
					{
						var tilingPaths = terraformer.PartitionPath(
							contour,
							[cliffZone, clearZone],
							partitionMask,
							param.SegmentedBrushes,
							/*param.MinimumCliffStraight*/2);

						if (tilingPaths.Count > 0)
							tallContours.Add(contour);
						else
							shortContours.Add(contour);

						var baseHeight = heightMap.Target[contour[0]];

						foreach (var tilingPath in tilingPaths)
						{
							var brush = tilingPath
								.OptimizeLoop()
								.ExtendEdge(4)
								.SetAutoEndDeviation()
								.Tile(cliffTilingRandom)
									?? throw new MapGenerationException("Could not fit tiles for sand-sand cliffs");

							terraformer.PaintTiling(pickAnyRandom, brush, baseHeight);

							heightMap.MarkUntileable(brush.Shape.Select(cvec => CPos.Zero + cvec));
						}
					}

					var shortMask = new CellLayer<bool>(map);
					var tallMask = new CellLayer<bool>(map);

					var shortChirality = MatrixUtils.PointsChirality(cellBounds.Size.ToInt2(), shortContours);
					if (shortChirality != null)
					{
						CellLayerUtils.FromMatrix(
							shortMask,
							shortChirality.Map(v => v > 0));
					}

					var tallChirality = MatrixUtils.PointsChirality(cellBounds.Size.ToInt2(), tallContours);
					if (tallChirality != null)
					{
						CellLayerUtils.FromMatrix(
							tallMask,
							tallChirality.Map(v => v > 0));
					}

					heightMap.AdjustCellHeights(1, shortMask);
					heightMap.AdjustCellHeights(4, tallMask);
				}
			}

			{
				rampTiler.PullHeightMap(heightMap);
				MatrixUtils.ColorDump2d("adjustable", heightMap.Adjustable);
				MatrixUtils.EnumDump2d("targetHeights basic", heightMap.Target.Map(v => (int)v));

				var noise = NoiseUtils.SymmetricFractalNoise(
					random,
					heightMap.Target.Size,
					terraformer.Rotations,
					terraformer.Mirror,
					16 * 1024,
					NoiseUtils.PinkAmplitude);
				noise = MatrixUtils.BinomialBlur(noise, 1);
				noise = MatrixUtils.NormalizeRangeInPlace(noise, 3);
				for (var i = 0; i < noise.Data.Length; i++)
					heightMap.Target[i] = (byte)Math.Clamp(noise[i] + heightMap.Target[i], byte.MinValue, byte.MaxValue);
				MatrixUtils.EnumDump2d("targetHeights noised", heightMap.Target.Map(v => (int)v));

				heightMap.Soften(16);
				MatrixUtils.EnumDump2d("targetHeights soften", heightMap.Target.Map(v => (int)v));

				// var heightAdjust = MatrixUtils.KernelAggregate(
				// 	elevation,
				// 	new Matrix<int>(heightMap.Target.Size),
				// 	new int2(2, 2),
				// 	new int2(1, 1),
				// 	submatrix => submatrix.Data.Sum());
				// MatrixUtils.NormalizeRangeInPlace(heightAdjust, 2);

				// heightMap.SetHeights(
				// 	MatrixUtils.BinomialBlur(heightMap.Target.Map(v => (int)v), 5).Map(v => (byte)v));
				// heightMap.BinomialBlur(5);
				heightMap = heightMap.Constrain(RampTiler.AdjustmentMode.LowerMiddle)
					?? throw new MapGenerationException("created unfixable heightmap");
				MatrixUtils.EnumDump2d("targetHeights constrain", heightMap.Target.Map(v => (int)v));
				var brush = rampTiler.TileHeightMap(heightMap, rampTilingRandom);
				terraformer.PaintTiling(rampTilingRandom, brush, 0);
			}

			// {
			// 	var rampCellMask = new CellLayer<bool>(map);
			// 	CellLayerUtils.FromMatrix(
			// 		rampCellMask,
			// 		MatrixUtils.KernelAggregate(
			// 			rampMask,
			// 			new Matrix<bool>(landPlan.Size),
			// 			new int2(2, 2),
			// 			new int2(0, 0),
			// 			submatrix => submatrix.Data.Any(v => v)));
			// 	MatrixUtils.EnumDump2d("mask", targetHeights.Map(v => (int)v));
			// 	var brush = rampTiler.TileCorners(targetHeights, rampCellMask, rampTilingRandom);
			// 	terraformer.PaintTiling(rampTilingRandom, brush, 0);
			// }

			if (param.Forests > 0)
			{
				var space = terraformer.CheckSpace(param.ClearTerrain);
				var passages = terraformer.PlanPassages(
					topologyRandom,
					terraformer.ImproveSymmetry(space, true, (a, b) => a && b),
					param.ForestCutout,
					param.MaximumCutoutSpacing);
				var forestNoise = terraformer.BooleanNoise(
					forestRandom,
					param.ForestFeatureSize,
					param.Forests,
					param.ForestClumpiness);
				var replace = PlayableToReplaceable();
				foreach (var mpos in map.AllCells.MapCoords)
					if (!forestNoise[mpos] || !space[mpos] || passages[mpos])
						replace[mpos] = MultiBrush.Replaceability.None;
				terraformer.PaintArea(forestTilingRandom, replace, param.ForestObstacles);
			}

			if (param.EnforceSymmetry != 0)
			{
				var asymmetries = terraformer.FindAsymmetries(param.DominantTerrain, true, param.EnforceSymmetry == 2);
				terraformer.PaintActors(symmetryTilingRandom, asymmetries, param.ForestObstacles);
			}

			// var templates = new ushort[] { 0, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57 };
			// var brushes = templates
			// 	.Select(t => new MultiBrush().WithTemplate(map, t, CVec.Zero))
			// 	.ToList();
			// var tiler = new RampTiler(map, brushes);
			// var bounds = CellLayerUtils.CellBounds(map);
			// var cornerHeightsNoise =
			// 	NoiseUtils.FractalNoise(
			// 		random,
			// 		bounds.Size.ToInt2() + new int2(1, 1),
			// 		1024 * 32,
			// 		NoiseUtils.PinkAmplitude);
			// cornerHeightsNoise = MatrixUtils.NormalizeRangeInPlace(cornerHeightsNoise, 32);
			// var cornerHeights = MatrixUtils.BinomialBlur(cornerHeightsNoise, 0)
			// 	.Map(i => (byte)Math.Max(0, i));
			// var maskLayer = CellLayerUtils.Create(map, (MPos mpos) => map.Contains(mpos));
			// var mask = new Matrix<bool>(bounds.Size.ToInt2() + new int2(1, 1)).Fill(true);
			// // foreach (var cpos in map.AllEdgeCells)
			// // {
			// // 	var xy = new int2(cpos.X, cpos.Y) - bounds.TopLeft;
			// // 	mask[xy.X, xy.Y] = false;
			// // 	mask[xy.X + 1, xy.Y] = false;
			// // 	mask[xy.X + 1, xy.Y + 1] = false;
			// // 	mask[xy.X, xy.Y + 1] = false;
			// // 	cornerHeights[xy.X, xy.Y] = 0;
			// // 	cornerHeights[xy.X + 1, xy.Y] = 0;
			// // 	cornerHeights[xy.X + 1, xy.Y + 1] = 0;
			// // 	cornerHeights[xy.X, xy.Y + 1] = 0;
			// // }
			// for (var y = 0; y < mask.Size.Y; y++)
			// {
			// 	for (var x = 0; x < mask.Size.X; x++)
			// 	{
			// 		var cpos = new CPos(x + bounds.TopLeft.X, y + bounds.TopLeft.Y);
			// 		if (map.Contains(cpos))
			// 			mask[x, y] = true;
			// 		else
			// 			cornerHeights[x, y] = 0;
			// 	}
			// }

			// cornerHeights = tiler.ConstrainCornerHeights(cornerHeights, mask, RampTiler.AdjustmentMode.LowerMiddle);
			// var tiling = tiler.TileCorners(cornerHeights, null, random);
			// terraformer.PaintTiling(random, tiling);

			terraformer.BakeMap();

			return map;
		}

		public override object Create(ActorInitializer init)
		{
			return new TSMapGenerator(this);
		}

		string[] IEditorMapGeneratorInfo.Tilesets => Tilesets;
	}

	public class TSMapGenerator : IEditorTool
	{
		public string Label { get; }
		public string PanelWidget { get; }
		public TraitInfo TraitInfo { get; }
		public bool IsEnabled => true;

		public TSMapGenerator(TSMapGeneratorInfo info)
		{
			Label = info.Name;
			PanelWidget = info.PanelWidget;
			TraitInfo = info;
		}
	}
}
