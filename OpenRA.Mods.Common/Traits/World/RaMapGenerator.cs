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
using OpenRA.Mods.Common.MapUtils;
using OpenRA.Mods.Common.Terrain;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Map generator for Red Alert maps.")]
	[TraitLocation(SystemActors.World)]
	public sealed class RaMapGeneratorInfo : TraitInfo, IMapGeneratorInfo
	{
		[FieldLoader.Require]
		[Desc("Human-readable name this generator uses.")]
		[FluentReference]
		public readonly string Name = null;

		[FieldLoader.Require]
		[Desc("Internal id for this map generator.")]
		public readonly string Type = null;

		string IMapGeneratorInfo.Type => Type;

		string IMapGeneratorInfo.Name => Name;

		public override object Create(ActorInitializer init) { return new RaMapGenerator(this); }
	}

	public sealed class RaMapGenerator : IMapGenerator
	{
		[FluentReference]
		const string StrPrimary = "label-ra-map-generator-primary";
		[FluentReference]
		const string StrRotations = "label-ra-map-generator-rotations";
		[FluentReference]
		const string StrMirror = "label-ra-map-generator-mirror";
		[FluentReference]
		const string StrMirrorNone = "label-ra-map-generator-mirror-none";
		[FluentReference]
		const string StrMirrorLr = "label-ra-map-generator-mirror-lr";
		[FluentReference]
		const string StrMirrorTlbr = "label-ra-map-generator-mirror-tlbr";
		[FluentReference]
		const string StrMirrorTb = "label-ra-map-generator-mirror-tb";
		[FluentReference]
		const string StrMirrorTrbl = "label-ra-map-generator-mirror-trbl";
		[FluentReference]
		const string StrPlayers = "label-ra-map-generator-players";
		[FluentReference]
		const string StrTerrain = "label-ra-map-generator-terrain";
		[FluentReference]
		const string StrTerrainFeatureSize = "label-ra-map-generator-terrain-feature-size";
		[FluentReference]
		const string StrForestFeatureSize = "label-ra-map-generator-forest-feature-size";
		[FluentReference]
		const string StrResourceFeatureSize = "label-ra-map-generator-resource-feature-size";
		[FluentReference]
		const string StrWater = "label-ra-map-generator-water";
		[FluentReference]
		const string StrMountains = "label-ra-map-generator-mountains";
		[FluentReference]
		const string StrForests = "label-ra-map-generator-forests";
		[FluentReference]
		const string StrForestCutout = "label-ra-map-generator-forest-cutout";
		[FluentReference]
		const string StrExternalCircularBias = "label-ra-map-generator-external-circular-bias";
		[FluentReference]
		const string StrExternalCircularBiasSquare = "label-ra-map-generator-external-circular-bias-square";
		[FluentReference]
		const string StrExternalCircularBiasCircleWater = "label-ra-map-generator-external-circular-bias-circle-water";
		[FluentReference]
		const string StrExternalCircularBiasCircleMountain = "label-ra-map-generator-external-circular-bias-circle-mountain";
		[FluentReference]
		const string StrTerrainSmoothing = "label-ra-map-generator-terrain-smoothing";
		[FluentReference]
		const string StrSmoothingThreshold = "label-ra-map-generator-smoothing-threshold";
		[FluentReference]
		const string StrMinimumLandSeaThickness = "label-ra-map-generator-minimum-land-sea-thickness";
		[FluentReference]
		const string StrMinimumMountainThickness = "label-ra-map-generator-minimum-mountain-thickness";
		[FluentReference]
		const string StrMaximumAltitude = "label-ra-map-generator-maximum-altitude";
		[FluentReference]
		const string StrRoughnessRadius = "label-ra-map-generator-roughness-radius";
		[FluentReference]
		const string StrRoughness = "label-ra-map-generator-roughness";
		[FluentReference]
		const string StrMinimumTerrainContourSpacing = "label-ra-map-generator-minimum-terrain-contour-spacing";
		[FluentReference]
		const string StrMinimumCliffLength = "label-ra-map-generator-minimum-cliff-length";
		[FluentReference]
		const string StrForestClumpiness = "label-ra-map-generator-forest-clumpiness";
		[FluentReference]
		const string StrDenyWalledAreas = "label-ra-map-generator-deny-walled-areas";
		[FluentReference]
		const string StrEnforceSymmetry = "label-ra-map-generator-enforce-symmetry";
		[FluentReference]
		const string StrEnforceSymmetryNone = "label-ra-map-generator-enforce-symmetry-none";
		[FluentReference]
		const string StrEnforceSymmetryPassability = "label-ra-map-generator-enforce-symmetry-passability";
		[FluentReference]
		const string StrEnforceSymmetryType = "label-ra-map-generator-enforce-symmetry-type";
		[FluentReference]
		const string StrRoads = "label-ra-map-generator-roads";
		[FluentReference]
		const string StrRoadSpacing = "label-ra-map-generator-road-spacing";
		[FluentReference]
		const string StrRoadShrink = "label-ra-map-generator-road-shrink";
		[FluentReference]
		const string StrEntities = "label-ra-map-generator-entities";
		[FluentReference]
		const string StrCreateEntities = "label-ra-map-generator-create-entities";
		[FluentReference]
		const string StrCentralSpawnReservationFraction = "label-ra-map-generator-central-spawn-reservation-fraction";
		[FluentReference]
		const string StrCentralExpansionReservationFraction = "label-ra-map-generator-central-expansion-reservation-fraction";
		[FluentReference]
		const string StrMineReservation = "label-ra-map-generator-mine-reservation";
		[FluentReference]
		const string StrSpawnRegionSize = "label-ra-map-generator-spawn-region-size";
		[FluentReference]
		const string StrSpawnBuildSize = "label-ra-map-generator-spawn-build-size";
		[FluentReference]
		const string StrSpawnMines = "label-ra-map-generator-spawn-mines";
		[FluentReference]
		const string StrSpawnReservation = "label-ra-map-generator-spawn-reservation";
		[FluentReference]
		const string StrSpawnResourceBias = "label-ra-map-generator-spawn-resource-bias";
		[FluentReference]
		const string StrResourcesPerPlayer = "label-ra-map-generator-resources-per-player";
		[FluentReference]
		const string StrGemUpgrade = "label-ra-map-generator-gem-upgrade";
		[FluentReference]
		const string StrOreUniformity = "label-ra-map-generator-ore-uniformity";
		[FluentReference]
		const string StrOreClumpiness = "label-ra-map-generator-ore-clumpiness";
		[FluentReference]
		const string StrMaximumExpansionMines = "label-ra-map-generator-maximum-expansion-mines";
		[FluentReference]
		const string StrMaximumMinesPerExpansion = "label-ra-map-generator-maximum-mines-per-expansion";
		[FluentReference]
		const string StrMinimumExpansionsSize = "label-ra-map-generator-minimum-expansions-size";
		[FluentReference]
		const string StrMaximumExpansionsSize = "label-ra-map-generator-maximum-expansions-size";
		[FluentReference]
		const string StrExpansionInner = "label-ra-map-generator-expansion-inner";
		[FluentReference]
		const string StrExpansionBorder = "label-ra-map-generator-expansion-border";
		[FluentReference]
		const string StrMinimumBuildings = "label-ra-map-generator-minimum-buildings";
		[FluentReference]
		const string StrMaximumBuildings = "label-ra-map-generator-maximum-buildings";
		[FluentReference]
		const string StrWeightFcom = "label-ra-map-generator-weight-fcom";
		[FluentReference]
		const string StrWeightHosp = "label-ra-map-generator-weight-hosp";
		[FluentReference]
		const string StrWeightMiss = "label-ra-map-generator-weight-miss";
		[FluentReference]
		const string StrWeightBio = "label-ra-map-generator-weight-bio";
		[FluentReference]
		const string StrWeightOilb = "label-ra-map-generator-weight-oilb";

		[FluentReference]
		const string StrPresetLakes = "label-ra-map-generator-preset-lakes";
		[FluentReference]
		const string StrPresetPuddles = "label-ra-map-generator-preset-puddles";
		[FluentReference]
		const string StrPresetPlains = "label-ra-map-generator-preset-plains";
		[FluentReference]
		const string StrPresetParks = "label-ra-map-generator-preset-parks";
		[FluentReference]
		const string StrPresetWoodlands = "label-ra-map-generator-preset-woodlands";
		[FluentReference]
		const string StrPresetOvergrown = "label-ra-map-generator-preset-overgrown";
		[FluentReference]
		const string StrPresetMountains = "label-ra-map-generator-preset-mountains";
		[FluentReference]
		const string StrPresetMountainLakes = "label-ra-map-generator-preset-mountain-lakes";
		[FluentReference]
		const string StrPresetOceanic = "label-ra-map-generator-preset-oceanic";
		[FluentReference]
		const string StrPresetLargeIslands = "label-ra-map-generator-preset-large-islands";
		[FluentReference]
		const string StrPresetContinents = "label-ra-map-generator-preset-continents";
		[FluentReference]
		const string StrPresetWetlands = "label-ra-map-generator-preset-wetlands";
		[FluentReference]
		const string StrPresetNarrowWetlands = "label-ra-map-generator-preset-narrow-wetlands";

		readonly RaMapGeneratorInfo info;

		IMapGeneratorInfo IMapGenerator.Info => info;

		public RaMapGenerator(RaMapGeneratorInfo info)
		{
			this.info = info;
		}

		public IEnumerable<MapGeneratorSetting> GetDefaultSettings(Map map, ModData modData)
		{
			return ImmutableList.Create(
				new MapGeneratorSetting("#Primary", FluentProvider.GetString(StrPrimary), new MapGeneratorSetting.SectionValue()),
				new MapGeneratorSetting("Rotations", FluentProvider.GetString(StrRotations), new MapGeneratorSetting.IntegerValue(2)),
				new MapGeneratorSetting("Mirror", FluentProvider.GetString(StrMirror), new MapGeneratorSetting.EnumValue(
					ImmutableList.Create(
						new KeyValuePair<int, string>((int)Symmetry.Mirror.None, FluentProvider.GetString(StrMirrorNone)),
						new KeyValuePair<int, string>((int)Symmetry.Mirror.LeftMatchesRight, FluentProvider.GetString(StrMirrorLr)),
						new KeyValuePair<int, string>((int)Symmetry.Mirror.TopLeftMatchesBottomRight, FluentProvider.GetString(StrMirrorTlbr)),
						new KeyValuePair<int, string>((int)Symmetry.Mirror.TopMatchesBottom, FluentProvider.GetString(StrMirrorTb)),
						new KeyValuePair<int, string>((int)Symmetry.Mirror.TopRightMatchesBottomLeft, FluentProvider.GetString(StrMirrorTrbl))),
					(int)Symmetry.Mirror.None)),
				new MapGeneratorSetting("Players", FluentProvider.GetString(StrPlayers), new MapGeneratorSetting.IntegerValue(1)),
				new MapGeneratorSetting("#Terrain", FluentProvider.GetString(StrTerrain), new MapGeneratorSetting.SectionValue()),
				new MapGeneratorSetting("TerrainFeatureSize", FluentProvider.GetString(StrTerrainFeatureSize), new MapGeneratorSetting.FloatValue(20.0f)),
				new MapGeneratorSetting("ForestFeatureSize", FluentProvider.GetString(StrForestFeatureSize), new MapGeneratorSetting.FloatValue(20.0f)),
				new MapGeneratorSetting("ResourceFeatureSize", FluentProvider.GetString(StrResourceFeatureSize), new MapGeneratorSetting.FloatValue(20.0f)),
				new MapGeneratorSetting("Water", FluentProvider.GetString(StrWater), new MapGeneratorSetting.FloatValue(0.2)),
				new MapGeneratorSetting("Mountains", FluentProvider.GetString(StrMountains), new MapGeneratorSetting.FloatValue(0.1)),
				new MapGeneratorSetting("Forests", FluentProvider.GetString(StrForests), new MapGeneratorSetting.FloatValue(0.025)),
				new MapGeneratorSetting("ForestCutout", FluentProvider.GetString(StrForestCutout), new MapGeneratorSetting.IntegerValue(2)),
				new MapGeneratorSetting("ExternalCircularBias", FluentProvider.GetString(StrExternalCircularBias), new MapGeneratorSetting.EnumValue(
					ImmutableList.Create(
						new KeyValuePair<string, string>("0", FluentProvider.GetString(StrExternalCircularBiasSquare)),
						new KeyValuePair<string, string>("-1", FluentProvider.GetString(StrExternalCircularBiasCircleWater)),
						new KeyValuePair<string, string>("1", FluentProvider.GetString(StrExternalCircularBiasCircleMountain))),
					"0")),
				new MapGeneratorSetting("TerrainSmoothing", FluentProvider.GetString(StrTerrainSmoothing), new MapGeneratorSetting.IntegerValue(4)),
				new MapGeneratorSetting("SmoothingThreshold", FluentProvider.GetString(StrSmoothingThreshold), new MapGeneratorSetting.FloatValue(5f / 6f)),
				new MapGeneratorSetting("MinimumLandSeaThickness", FluentProvider.GetString(StrMinimumLandSeaThickness), new MapGeneratorSetting.IntegerValue(5)),
				new MapGeneratorSetting("MinimumMountainThickness", FluentProvider.GetString(StrMinimumMountainThickness), new MapGeneratorSetting.IntegerValue(5)),
				new MapGeneratorSetting("MaximumAltitude", FluentProvider.GetString(StrMaximumAltitude), new MapGeneratorSetting.IntegerValue(8)),
				new MapGeneratorSetting("RoughnessRadius", FluentProvider.GetString(StrRoughnessRadius), new MapGeneratorSetting.IntegerValue(5)),
				new MapGeneratorSetting("Roughness", FluentProvider.GetString(StrRoughness), new MapGeneratorSetting.FloatValue(0.5)),
				new MapGeneratorSetting("MinimumTerrainContourSpacing", FluentProvider.GetString(StrMinimumTerrainContourSpacing), new MapGeneratorSetting.IntegerValue(6)),
				new MapGeneratorSetting("MinimumCliffLength", FluentProvider.GetString(StrMinimumCliffLength), new MapGeneratorSetting.IntegerValue(10)),
				new MapGeneratorSetting("ForestClumpiness", FluentProvider.GetString(StrForestClumpiness), new MapGeneratorSetting.FloatValue(0.5)),
				new MapGeneratorSetting("DenyWalledAreas", FluentProvider.GetString(StrDenyWalledAreas), new MapGeneratorSetting.BooleanValue(true)),
				new MapGeneratorSetting("EnforceSymmetry", FluentProvider.GetString(StrEnforceSymmetry), new MapGeneratorSetting.EnumValue(
					ImmutableList.Create(
						new KeyValuePair<string, string>("0", FluentProvider.GetString(StrEnforceSymmetryNone)),
						new KeyValuePair<string, string>("1", FluentProvider.GetString(StrEnforceSymmetryPassability)),
						new KeyValuePair<string, string>("2", FluentProvider.GetString(StrEnforceSymmetryType))),
					"0")),
				new MapGeneratorSetting("Roads", FluentProvider.GetString(StrRoads), new MapGeneratorSetting.BooleanValue(true)),
				new MapGeneratorSetting("RoadSpacing", FluentProvider.GetString(StrRoadSpacing), new MapGeneratorSetting.IntegerValue(5)),
				new MapGeneratorSetting("RoadShrink", FluentProvider.GetString(StrRoadShrink), new MapGeneratorSetting.IntegerValue(0)),
				new MapGeneratorSetting("#Entities", FluentProvider.GetString(StrEntities), new MapGeneratorSetting.SectionValue()),
				new MapGeneratorSetting("CreateEntities", FluentProvider.GetString(StrCreateEntities), new MapGeneratorSetting.BooleanValue(true)),
				new MapGeneratorSetting("CentralSpawnReservationFraction", FluentProvider.GetString(StrCentralSpawnReservationFraction), new MapGeneratorSetting.FloatValue(0.3)),
				new MapGeneratorSetting("CentralExpansionReservationFraction", FluentProvider.GetString(StrCentralExpansionReservationFraction), new MapGeneratorSetting.FloatValue(0.1)),
				new MapGeneratorSetting("MineReservation", FluentProvider.GetString(StrMineReservation), new MapGeneratorSetting.IntegerValue(8)),
				new MapGeneratorSetting("SpawnRegionSize", FluentProvider.GetString(StrSpawnRegionSize), new MapGeneratorSetting.IntegerValue(12)),
				new MapGeneratorSetting("SpawnBuildSize", FluentProvider.GetString(StrSpawnBuildSize), new MapGeneratorSetting.IntegerValue(8)),
				new MapGeneratorSetting("SpawnMines", FluentProvider.GetString(StrSpawnMines), new MapGeneratorSetting.IntegerValue(3)),
				new MapGeneratorSetting("SpawnReservation", FluentProvider.GetString(StrSpawnReservation), new MapGeneratorSetting.IntegerValue(20)),
				new MapGeneratorSetting("SpawnResourceBias", FluentProvider.GetString(StrSpawnResourceBias), new MapGeneratorSetting.FloatValue(1.25)),
				new MapGeneratorSetting("ResourcesPerPlayer", FluentProvider.GetString(StrResourcesPerPlayer), new MapGeneratorSetting.IntegerValue(50000)),
				new MapGeneratorSetting("GemUpgrade", FluentProvider.GetString(StrGemUpgrade), new MapGeneratorSetting.FloatValue(0.05)),
				new MapGeneratorSetting("OreUniformity", FluentProvider.GetString(StrOreUniformity), new MapGeneratorSetting.FloatValue(0.25)),
				new MapGeneratorSetting("OreClumpiness", FluentProvider.GetString(StrOreClumpiness), new MapGeneratorSetting.FloatValue(0.25)),
				new MapGeneratorSetting("MaximumExpansionMines", FluentProvider.GetString(StrMaximumExpansionMines), new MapGeneratorSetting.IntegerValue(5)),
				new MapGeneratorSetting("MaximumMinesPerExpansion", FluentProvider.GetString(StrMaximumMinesPerExpansion), new MapGeneratorSetting.IntegerValue(2)),
				new MapGeneratorSetting("MinimumExpansionSize", FluentProvider.GetString(StrMinimumExpansionsSize), new MapGeneratorSetting.IntegerValue(2)),
				new MapGeneratorSetting("MaximumExpansionSize", FluentProvider.GetString(StrMaximumExpansionsSize), new MapGeneratorSetting.IntegerValue(12)),
				new MapGeneratorSetting("ExpansionInner", FluentProvider.GetString(StrExpansionInner), new MapGeneratorSetting.IntegerValue(2)),
				new MapGeneratorSetting("ExpansionBorder", FluentProvider.GetString(StrExpansionBorder), new MapGeneratorSetting.IntegerValue(1)),
				new MapGeneratorSetting("MinimumBuildings", FluentProvider.GetString(StrMinimumBuildings), new MapGeneratorSetting.IntegerValue(0)),
				new MapGeneratorSetting("MaximumBuildings", FluentProvider.GetString(StrMaximumBuildings), new MapGeneratorSetting.IntegerValue(3)),
				new MapGeneratorSetting("WeightFcom", FluentProvider.GetString(StrWeightFcom), new MapGeneratorSetting.FloatValue(1)),
				new MapGeneratorSetting("WeightHosp", FluentProvider.GetString(StrWeightHosp), new MapGeneratorSetting.FloatValue(2)),
				new MapGeneratorSetting("WeightMiss", FluentProvider.GetString(StrWeightMiss), new MapGeneratorSetting.FloatValue(1)),
				new MapGeneratorSetting("WeightBio", FluentProvider.GetString(StrWeightBio), new MapGeneratorSetting.FloatValue(0)),
				new MapGeneratorSetting("WeightOilb", FluentProvider.GetString(StrWeightOilb), new MapGeneratorSetting.FloatValue(9)));
		}

		public IEnumerable<MapGeneratorSetting> GetPresetSettings(Map map, ModData modData, string preset)
		{
			var settings = (ImmutableList<MapGeneratorSetting>)GetDefaultSettings(map, modData);
			switch (preset)
			{
				case null:
				case "lakes":
					break;
				case "puddles":
					settings.First(s => s.Name == "Water").Set(0.1);
					break;
				case "plains":
					settings.First(s => s.Name == "Water").Set(0.0);
					break;
				case "parks":
					settings.First(s => s.Name == "Water").Set(0.0);
					settings.First(s => s.Name == "Forests").Set(0.1);
					break;
				case "woodlands":
					settings.First(s => s.Name == "Water").Set(0.0);
					settings.First(s => s.Name == "Forests").Set(0.4);
					settings.First(s => s.Name == "ForestCutout").Set(3);
					settings.First(s => s.Name == "EnforceSymmetry").Set(2);
					settings.First(s => s.Name == "RoadSpacing").Set(3);
					settings.First(s => s.Name == "RoadShrink").Set(4);
					break;
				case "overgrown":
					settings.First(s => s.Name == "Water").Set(0.0);
					settings.First(s => s.Name == "Forests").Set(0.5);
					settings.First(s => s.Name == "EnforceSymmetry").Set(2);
					settings.First(s => s.Name == "Mountains").Set(0.5);
					settings.First(s => s.Name == "Roughness").Set(0.25);
					break;
				case "mountains":
					settings.First(s => s.Name == "Water").Set(0.0);
					settings.First(s => s.Name == "Mountains").Set(1.0);
					settings.First(s => s.Name == "Roughness").Set(0.85);
					settings.First(s => s.Name == "MinimumTerrainContourSpacing").Set(5);
					break;
				case "mountain-lakes":
					settings.First(s => s.Name == "Water").Set(0.2);
					settings.First(s => s.Name == "Mountains").Set(1.0);
					settings.First(s => s.Name == "Roughness").Set(0.85);
					settings.First(s => s.Name == "MinimumTerrainContourSpacing").Set(5);
					break;
				case "oceanic":
					settings.First(s => s.Name == "Water").Set(0.8);
					settings.First(s => s.Name == "Forests").Set(0.0);
					break;
				case "large-islands":
					settings.First(s => s.Name == "Water").Set(0.75);
					settings.First(s => s.Name == "TerrainFeatureSize").Set(50.0f);
					settings.First(s => s.Name == "Forests").Set(0.0);
					break;
				case "continents":
					settings.First(s => s.Name == "Water").Set(0.5);
					settings.First(s => s.Name == "TerrainFeatureSize").Set(100.0f);
					break;
				case "wetlands":
					settings.First(s => s.Name == "Water").Set(0.5);
					break;
				case "narrow-wetlands":
					settings.First(s => s.Name == "Water").Set(0.5);
					settings.First(s => s.Name == "TerrainFeatureSize").Set(5.0f);
					settings.First(s => s.Name == "Forests").Set(0.0);
					settings.First(s => s.Name == "SpawnBuildSize").Set(6);
					break;
				default:
					throw new ArgumentException("Invalid preset.");
			}

			return settings;
		}

		public IEnumerable<KeyValuePair<string, string>> GetPresets(Map map, ModData modData)
		{
			return ImmutableList.Create(
				new KeyValuePair<string, string>("lakes", FluentProvider.GetString(StrPresetLakes)),
				new KeyValuePair<string, string>("puddles", FluentProvider.GetString(StrPresetPuddles)),
				new KeyValuePair<string, string>("plains", FluentProvider.GetString(StrPresetPlains)),
				new KeyValuePair<string, string>("parks", FluentProvider.GetString(StrPresetParks)),
				new KeyValuePair<string, string>("woodlands", FluentProvider.GetString(StrPresetWoodlands)),
				new KeyValuePair<string, string>("overgrown", FluentProvider.GetString(StrPresetOvergrown)),
				new KeyValuePair<string, string>("mountains", FluentProvider.GetString(StrPresetMountains)),
				new KeyValuePair<string, string>("mountain-lakes", FluentProvider.GetString(StrPresetMountainLakes)),
				new KeyValuePair<string, string>("oceanic", FluentProvider.GetString(StrPresetOceanic)),
				new KeyValuePair<string, string>("large-islands", FluentProvider.GetString(StrPresetLargeIslands)),
				new KeyValuePair<string, string>("continents", FluentProvider.GetString(StrPresetContinents)),
				new KeyValuePair<string, string>("wetlands", FluentProvider.GetString(StrPresetWetlands)),
				new KeyValuePair<string, string>("narrow-wetlands", FluentProvider.GetString(StrPresetNarrowWetlands)));
		}

		public void Generate(Map map, ModData modData, MersenneTwister random, IEnumerable<MapGeneratorSetting> settingsEnumerable)
		{
			const ushort LandTile = 255;
			var waterTile = map.Tileset == "DESERT" ? (ushort)256 : (ushort)1;

			const float ExternalBias = 1000000.0f;

			var settings = Enumerable.ToDictionary(settingsEnumerable, s => s.Name);
			var tileset = modData.DefaultTerrainInfo[map.Tileset] as ITemplatedTerrainInfo;
			var size = map.MapSize;
			var minSpan = Math.Min(size.X, size.Y);
			var maxSpan = Math.Max(size.X, size.Y);

			var actorPlans = new List<ActorPlan>();

			var rotations = settings["Rotations"].Get<int>();
			var mirror = (Symmetry.Mirror)settings["Mirror"].Get<int>();
			var terrainFeatureSize = settings["TerrainFeatureSize"].Get<float>();
			var forestFeatureSize = settings["ForestFeatureSize"].Get<float>();
			var resourceFeatureSize = settings["ResourceFeatureSize"].Get<float>();
			var terrainSmoothing = settings["TerrainSmoothing"].Get<int>();
			var smoothingThreshold = settings["SmoothingThreshold"].Get<float>();
			var externalCircularBias = settings["ExternalCircularBias"].Get<int>();
			var minimumLandSeaThickness = settings["MinimumLandSeaThickness"].Get<int>();
			var minimumMountainThickness = settings["MinimumMountainThickness"].Get<int>();
			var water = settings["Water"].Get<float>();
			var forests = settings["Forests"].Get<float>();
			var forestClumpiness = settings["ForestClumpiness"].Get<float>();
			var forestCutout = settings["ForestCutout"].Get<int>();
			var mountains = settings["Mountains"].Get<float>();
			var roughness = settings["Roughness"].Get<float>();
			var roughnessRadius = settings["RoughnessRadius"].Get<int>();
			var maximumAltitude = settings["MaximumAltitude"].Get<int>();
			var minimumTerrainContourSpacing = settings["MinimumTerrainContourSpacing"].Get<int>();
			var minimumCliffLength = settings["MinimumCliffLength"].Get<int>();
			var enforceSymmetry = settings["EnforceSymmetry"].Get<int>();
			var denyWalledAreas = settings["DenyWalledAreas"].Get<bool>();
			var roads = settings["Roads"].Get<bool>();
			var roadSpacing = settings["RoadSpacing"].Get<int>();
			var roadShrink = settings["RoadShrink"].Get<int>();
			var createEntities = settings["CreateEntities"].Get<bool>();
			var players = settings["Players"].Get<int>();
			var centralSpawnReservationFraction = settings["CentralSpawnReservationFraction"].Get<float>();
			var centralExpansionReservationFraction = settings["CentralExpansionReservationFraction"].Get<float>();
			var spawnRegionSize = settings["SpawnRegionSize"].Get<int>();
			var spawnReservation = settings["SpawnReservation"].Get<int>();
			var spawnBuildSize = settings["SpawnBuildSize"].Get<int>();
			var spawnMines = settings["SpawnMines"].Get<int>();
			var gemUpgrade = settings["GemUpgrade"].Get<float>();
			var mineReservation = settings["MineReservation"].Get<int>();
			var maximumExpansionMines = settings["MaximumExpansionMines"].Get<int>();
			var minimumExpansionSize = settings["MinimumExpansionSize"].Get<int>();
			var maximumExpansionSize = settings["MaximumExpansionSize"].Get<int>();
			var expansionBorder = settings["ExpansionBorder"].Get<int>();
			var expansionInner = settings["ExpansionInner"].Get<int>();
			var maximumMinesPerExpansion = settings["MaximumMinesPerExpansion"].Get<int>();
			var minimumBuildings = settings["MinimumBuildings"].Get<int>();
			var maximumBuildings = settings["MaximumBuildings"].Get<int>();
			var weightFcom = settings["WeightFcom"].Get<float>();
			var weightHosp = settings["WeightHosp"].Get<float>();
			var weightMiss = settings["WeightMiss"].Get<float>();
			var weightBio = settings["WeightBio"].Get<float>();
			var weightOilb = settings["WeightOilb"].Get<float>();
			var spawnResourceBias = settings["SpawnResourceBias"].Get<float>();
			var resourcesPerPlayer = settings["ResourcesPerPlayer"].Get<int>();
			var oreUniformity = settings["OreUniformity"].Get<float>();
			var oreClumpiness = settings["OreClumpiness"].Get<float>();

			if (rotations < 1)
				throw new MapGenerationException("rotations must be >= 1");
			if (terrainFeatureSize < 1.0f)
				throw new MapGenerationException("terrainFeatureSize must be >= 1.0");
			if (forestFeatureSize < 1.0f)
				throw new MapGenerationException("forestFeatureSize must be >= 1.0");
			if (resourceFeatureSize < 1.0f)
				throw new MapGenerationException("resourceFeatureSize must be >= 1.0");
			if (terrainSmoothing < 1)
				throw new MapGenerationException("terrainSmoothing must be < 1");
			if (smoothingThreshold < 0.5f || smoothingThreshold > 1.0f)
				throw new MapGenerationException("smoothingThreshold must be between 0.5 and 1.0 inclusive");
			if (minimumLandSeaThickness < 1)
				throw new MapGenerationException("minimumLandSeaThickness must be >= 1");
			if (minimumMountainThickness < 1)
				throw new MapGenerationException("minimumMountainThickness must be >= 1");
			if (water < 0.0f || water > 1.0f)
				throw new MapGenerationException("water setting must be between 0 and 1 inclusive");
			if (forests < 0.0f || forests > 1.0f)
				throw new MapGenerationException("forest setting must be between 0 and 1 inclusive");
			if (forestClumpiness < 0.0f)
				throw new MapGenerationException("forestClumpiness setting must be >= 0");
			if (mountains < 0.0f || mountains > 1.0f)
				throw new MapGenerationException("mountains fraction must be between 0 and 1 inclusive");
			if (roughness < 0.0f || roughness > 1.0f)
				throw new MapGenerationException("roughness must be between 0.0 and 1.0");
			if (roughnessRadius < 1)
				throw new MapGenerationException("roughnessRadius must be >= 1");
			if (maximumAltitude < 0)
				throw new MapGenerationException("maximumAltitude must be >= 0");
			if (minimumTerrainContourSpacing < 0)
				throw new MapGenerationException("minimumTerrainContourSpacing must be >= 0");
			if (minimumCliffLength < 1)
				throw new MapGenerationException("minimumCliffLength must be >= 1");
			if (roadSpacing < 0)
				throw new MapGenerationException("roadSpacing must be >= 0");
			if (roadShrink < 0)
				throw new MapGenerationException("roadShrink must be >= 0");
			if (players < 1)
				throw new MapGenerationException("players must be >= 1");
			if (centralSpawnReservationFraction < 0.0f)
				throw new MapGenerationException("centralSpawnReservationFraction must be >= 0.0");
			if (centralExpansionReservationFraction < 0.0f)
				throw new MapGenerationException("centralExpansionReservationFraction must be >= 0.0");
			if (spawnRegionSize < 1)
				throw new MapGenerationException("spawnRegionSize must be >= 1");
			if (spawnReservation < 1)
				throw new MapGenerationException("spawnReservation must be >= 1");
			if (spawnBuildSize < 1)
				throw new MapGenerationException("spawnBuildSize must be >= 1");
			if (spawnMines < 0)
				throw new MapGenerationException("spawnMines must be >= 0");
			if (gemUpgrade < 0.0f || gemUpgrade > 1.0f)
				throw new MapGenerationException("gemUpgrade must be between 0.0 and 1.0");
			if (mineReservation < 1)
				throw new MapGenerationException("mineReservation must be >= 1");
			if (maximumExpansionMines < 1)
				throw new MapGenerationException("maximumExpansionMines must be >= 1");
			if (minimumExpansionSize < 1)
				throw new MapGenerationException("minimumExpansionSize must be >= 1");
			if (maximumExpansionSize < 1)
				throw new MapGenerationException("maximumExpansionSize must be >= 1");
			if (minimumExpansionSize > maximumExpansionSize)
				throw new MapGenerationException("minimumExpansionSize must be <= maximumExpansionSize");
			if (expansionBorder < 1)
				throw new MapGenerationException("expansionBorder must be >= 1");
			if (expansionInner < 1)
				throw new MapGenerationException("expansionInner must be >= 1");
			if (maximumMinesPerExpansion < 1)
				throw new MapGenerationException("maximumMinesPerExpansion must be >= 1");
			if (minimumBuildings < 0)
				throw new MapGenerationException("minimumBuildings must be >= 0");
			if (maximumBuildings < 1)
				throw new MapGenerationException("maximumBuildings must be >= 0");
			if (minimumBuildings > maximumBuildings)
				throw new MapGenerationException("minimumBuildings must be <= maximumBuildings");
			if (weightFcom < 0.0f)
				throw new MapGenerationException("weightFcom must be >= 0.0");
			if (weightHosp < 0.0f)
				throw new MapGenerationException("weightHosp must be >= 0.0");
			if (weightMiss < 0.0f)
				throw new MapGenerationException("weightMiss must be >= 0.0");
			if (weightBio < 0.0f)
				throw new MapGenerationException("weightBio must be >= 0.0");
			if (weightOilb < 0.0f)
				throw new MapGenerationException("weightOilb must be >= 0.0");
			if (resourcesPerPlayer < 0)
				throw new MapGenerationException("resourcesPerPlayer must be >= 0");
			if (oreUniformity < 0.0f)
				throw new MapGenerationException("oreUniformity must be >= 0.0");
			if (oreClumpiness < 0.0f)
				throw new MapGenerationException("oreClumpiness must be >= 0.0");

			bool trivialRotate;
			switch (rotations)
			{
				case 1:
				case 2:
				case 4:
					trivialRotate = true;
					break;
				default:
					trivialRotate = false;
					break;
			}

			var trivialMirror =
				size.X == size.Y ||
				mirror == Symmetry.Mirror.None ||
				mirror == Symmetry.Mirror.LeftMatchesRight ||
				mirror == Symmetry.Mirror.TopMatchesBottom;

			var beachIndex = tileset.GetTerrainIndex("Beach");
			var clearIndex = tileset.GetTerrainIndex("Clear");
			var gemsIndex = tileset.GetTerrainIndex("Gems");
			var oreIndex = tileset.GetTerrainIndex("Ore");
			var riverIndex = tileset.GetTerrainIndex("River");
			var roadIndex = tileset.GetTerrainIndex("Road");
			var rockIndex = tileset.GetTerrainIndex("Rock");
			var roughIndex = tileset.GetTerrainIndex("Rough");
			var treeIndex = tileset.GetTerrainIndex("Tree");
			var waterIndex = tileset.GetTerrainIndex("Water");

			ImmutableArray<MultiBrush> forestObstacles;
			ImmutableArray<MultiBrush> unplayableObstacles;
			{
				var clear = new TerrainTile(LandTile, 0);
				var basic = new MultiBrush(map, modData).WithWeight(1.0f);
				var husk = basic.Clone().WithWeight(0.1f);
				switch (map.Tileset)
				{
					case "DESERT":
						forestObstacles = ImmutableArray.Create(
							basic.Clone().WithTemplate(14),
							basic.Clone().WithTemplate(15),
							basic.Clone().WithTemplate(16),
							basic.Clone().WithTemplate(17),
							basic.Clone().WithTemplate(18),
							basic.Clone().WithTemplate(19),
							basic.Clone().WithTemplate(20),
							basic.Clone().WithTemplate(21),
							basic.Clone().WithTemplate(22),
							basic.Clone().WithTemplate(23),
							basic.Clone().WithActor(new ActorPlan(map, "t04").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "t08").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "t09").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "tc01").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t04.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t08.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t09.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "tc01.husk").AlignFootprint()));
						unplayableObstacles = ImmutableArray.Create(
							basic.Clone().WithTemplate(2),
							basic.Clone().WithTemplate(3),
							basic.Clone().WithTemplate(4),
							basic.Clone().WithTemplate(5),
							basic.Clone().WithTemplate(6),
							basic.Clone().WithTemplate(7),
							basic.Clone().WithTemplate(14).WithWeight(0.5f),
							basic.Clone().WithTemplate(15).WithWeight(0.5f),
							basic.Clone().WithTemplate(16).WithWeight(0.5f),
							basic.Clone().WithTemplate(17).WithWeight(0.5f),
							basic.Clone().WithTemplate(18).WithWeight(0.5f),
							basic.Clone().WithTemplate(19).WithWeight(0.5f),
							basic.Clone().WithTemplate(20).WithWeight(0.5f),
							basic.Clone().WithTemplate(21).WithWeight(0.5f),
							basic.Clone().WithTemplate(22).WithWeight(0.5f),
							basic.Clone().WithTemplate(23).WithWeight(0.5f),
							basic.Clone().WithTemplate(35),
							basic.Clone().WithTemplate(36),
							basic.Clone().WithTemplate(37),
							basic.Clone().WithTemplate(38),
							basic.Clone().WithTemplate(43),
							basic.Clone().WithActor(new ActorPlan(map, "t04").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "t08").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "t09").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "tc01").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							husk.Clone().WithActor(new ActorPlan(map, "t04.husk").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							husk.Clone().WithActor(new ActorPlan(map, "t08.husk").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							husk.Clone().WithActor(new ActorPlan(map, "t09.husk").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							husk.Clone().WithActor(new ActorPlan(map, "tc01.husk").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f));
						break;
					case "SNOW":
					case "TEMPERAT":
						forestObstacles = ImmutableArray.Create(
							basic.Clone().WithActor(new ActorPlan(map, "t01").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "t02").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "t03").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "t05").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "t06").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "t07").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "t08").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "t10").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "t11").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "t12").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "t13").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "t14").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "t15").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "t16").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "t17").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "tc01").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "tc02").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "tc03").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "tc04").AlignFootprint()),
							basic.Clone().WithActor(new ActorPlan(map, "tc05").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t01.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t02.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t03.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t05.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t06.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t07.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t08.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t10.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t11.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t12.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t13.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t14.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t15.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t16.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "t17.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "tc01.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "tc02.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "tc03.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "tc04.husk").AlignFootprint()),
							husk.Clone().WithActor(new ActorPlan(map, "tc05.husk").AlignFootprint()));
						unplayableObstacles = ImmutableArray.Create(
							basic.Clone().WithTemplate(97),
							basic.Clone().WithTemplate(98),
							basic.Clone().WithTemplate(99),
							basic.Clone().WithTemplate(217),
							basic.Clone().WithTemplate(218),
							basic.Clone().WithTemplate(219),
							basic.Clone().WithTemplate(220),
							basic.Clone().WithTemplate(221),
							basic.Clone().WithTemplate(222),
							basic.Clone().WithTemplate(223),
							basic.Clone().WithTemplate(224),
							basic.Clone().WithTemplate(225),
							basic.Clone().WithTemplate(226),
							basic.Clone().WithTemplate(103),
							basic.Clone().WithTemplate(104),
							basic.Clone().WithTemplate(105).WithWeight(0.05f),
							basic.Clone().WithTemplate(106).WithWeight(0.05f),
							basic.Clone().WithTemplate(109),
							basic.Clone().WithTemplate(110),
							basic.Clone().WithActor(new ActorPlan(map, "t01").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "t02").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "t03").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "t05").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "t06").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "t07").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "t08").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "t10").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "t11").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "t12").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "t13").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "t14").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "t15").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "t16").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
							basic.Clone().WithActor(new ActorPlan(map, "t17").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f));
						if (map.Tileset == "TEMPERAT")
							unplayableObstacles = unplayableObstacles.AddRange(new[]
							{
								basic.Clone().WithTemplate(580).WithWeight(0.1f),
								basic.Clone().WithTemplate(581).WithWeight(0.1f),
								basic.Clone().WithTemplate(582).WithWeight(0.1f),
								basic.Clone().WithTemplate(583).WithWeight(0.1f),
								basic.Clone().WithTemplate(584).WithWeight(0.1f),
								basic.Clone().WithTemplate(585).WithWeight(0.1f),
								basic.Clone().WithTemplate(586).WithWeight(0.1f),
								basic.Clone().WithTemplate(587).WithWeight(0.1f),
								basic.Clone().WithTemplate(588).WithWeight(0.1f)
							});
						break;
					default:
						throw new ArgumentException("Unexpected tileset");
				}
			}

			var replaceabilityMap = new Dictionary<TerrainTile, MultiBrush.Replaceability>();
			var playabilityMap = new Dictionary<TerrainTile, PlayableSpace.Playability>();
			foreach (var kv in tileset.Templates)
			{
				var id = kv.Key;
				var template = kv.Value;
				for (var ti = 0; ti < template.TilesCount; ti++)
				{
					if (template[ti] == null) continue;
					var tile = new TerrainTile(id, (byte)ti);
					var type = tileset.GetTerrainIndex(tile);

					if (type == beachIndex ||
						type == clearIndex ||
						type == gemsIndex ||
						type == oreIndex ||
						type == roadIndex ||
						type == roughIndex ||
						type == waterIndex)
					{
						playabilityMap[tile] = PlayableSpace.Playability.Playable;
					}
					else if (type == treeIndex)
					{
						playabilityMap[tile] = PlayableSpace.Playability.Partial;
					}
					else
					{
						playabilityMap[tile] = PlayableSpace.Playability.Unplayable;
					}

					if (id == waterTile)
					{
						replaceabilityMap[tile] = MultiBrush.Replaceability.Tile;
					}
					else if (template.Categories.Contains("Cliffs"))
					{
						if (type == rockIndex)
							replaceabilityMap[tile] = MultiBrush.Replaceability.None;
						else
							replaceabilityMap[tile] = MultiBrush.Replaceability.Actor;
					}
					else if (template.Categories.Contains("Debris"))
					{
						replaceabilityMap[tile] = MultiBrush.Replaceability.None;
					}
					else if (template.Categories.Contains("Beach") || template.Categories.Contains("Road"))
					{
						replaceabilityMap[tile] = MultiBrush.Replaceability.Tile;
						if (playabilityMap[tile] == PlayableSpace.Playability.Unplayable)
							playabilityMap[tile] = PlayableSpace.Playability.Partial;
					}
				}
			}

			Log.Write("debug", "deriving random generators");

			// Use `random` to derive separate independent random number generators.
			//
			// This prevents changes in one part of the algorithm from affecting randomness in
			// other parts and provides flexibility for future parallel processing.
			//
			// In order to maximize stability, additions should be appended only. Disused
			// derivatives may be deleted but should be replaced with their unused call to
			// random.Next(). All generators should be created unconditionally.
			var pickAnyRandom = new MersenneTwister(random.Next());
			var waterRandom = new MersenneTwister(random.Next());
			var beachTilingRandom = new MersenneTwister(random.Next());
			var cliffTilingRandom = new MersenneTwister(random.Next());
			var forestRandom = new MersenneTwister(random.Next());
			var forestTilingRandom = new MersenneTwister(random.Next());
			var resourceRandom = new MersenneTwister(random.Next());
			var roadTilingRandom = new MersenneTwister(random.Next());
			var playerRandom = new MersenneTwister(random.Next());
			var expansionRandom = new MersenneTwister(random.Next());
			var buildingRandom = new MersenneTwister(random.Next());

			TerrainTile PickTile(ushort tileType)
			{
				if (tileset.Templates.TryGetValue(tileType, out var template) && template.PickAny)
					return new TerrainTile(tileType, (byte)random.Next(0, template.TilesCount));
				else
					return new TerrainTile(tileType, 0);
			}

			Log.Write("debug", "clearing map");
			foreach (var cell in map.AllCells)
			{
				var mpos = cell.ToMPos(map);
				map.Tiles[mpos] = PickTile(LandTile);
				map.Resources[mpos] = new ResourceTile(0, 0);
				map.Height[mpos] = 0;
			}

			Log.Write("debug", "elevation: generating noise");
			var elevation = NoiseUtils.SymmetricFractalNoise(
				waterRandom,
				size,
				rotations,
				mirror,
				terrainFeatureSize,
				NoiseUtils.PinkAmplitude);

			if (terrainSmoothing > 0)
			{
				Log.Write("debug", "elevation: applying gaussian blur");
				var radius = terrainSmoothing;
				elevation = MatrixUtils.GaussianBlur(elevation, radius, radius);
			}

			MatrixUtils.CalibrateQuantileInPlace(
				elevation,
				0.0f,
				water);

			var mapCenter = (size.ToFloat2() - new float2(1.0f, 1.0f)) / 2.0f;
			var externalCircleRadius = minSpan / 2.0f - (minimumLandSeaThickness + minimumMountainThickness);
			if (externalCircularBias != 0)
			{
				elevation.DrawCircle(
					center: mapCenter,
					radius: externalCircleRadius,
					setTo: (_, _) => externalCircularBias * ExternalBias,
					invert: true);
			}

			Log.Write("debug", "land planning: producing terrain");
			var landPlan = MatrixUtils.BooleanBlotch(
				elevation.Map(v => v >= 0),
				terrainSmoothing,
				smoothingThreshold,
				minimumLandSeaThickness,
				/*bias=*/water < 0.5);

			Log.Write("debug", "beaches");
			var beaches = MatrixUtils.BordersToPoints(landPlan);
			if (beaches.Length > 0)
			{
				var beachPermittedTemplates =
					TilingPath.PermittedSegments.FromInner(tileset, new[] { "Beach" });
				var tiledBeaches = new int2[beaches.Length][];
				for (var i = 0; i < beaches.Length; i++)
				{
					var beachPath = new TilingPath(
						map,
						beaches[i],
						(minimumLandSeaThickness - 1) / 2,
						"Beach",
						"Beach",
						beachPermittedTemplates);
					beachPath
						.ExtendEdge(4)
						.OptimizeLoop();
					tiledBeaches[i] =
						beachPath.Tile(beachTilingRandom)
							?? throw new MapGenerationException("Could not fit tiles for beach");
				}

				Log.Write("debug", "filling water");
				var beachChirality = MatrixUtils.PointsChirality(size, tiledBeaches);
				foreach (var cell in map.AllCells)
				{
					var mpos = cell.ToMPos(map);
					var point = new int2(mpos.U, mpos.V);

					// `map.Tiles[mpos].Index == LAND_TILE` avoids overwriting beach tiles.
					if (beachChirality[mpos.U, mpos.V] < 0 && map.Tiles[mpos].Type == LandTile)
						map.Tiles[mpos] = PickTile(waterTile);
				}
			}
			else
			{
				// There weren't any coastlines
				var tileType = landPlan[0] ? LandTile : waterTile;
				foreach (var cell in map.AllCells)
				{
					var mpos = cell.ToMPos(map);
					map.Tiles[mpos] = PickTile(tileType);
				}
			}

			var nonLoopedCliffPermittedTemplates =
				TilingPath.PermittedSegments.FromInnerAndTerminal(
					tileset, new[] { "Cliff" }, new[] { "Clear" });
			var loopedCliffPermittedTemplates =
				TilingPath.PermittedSegments.FromInner(
					tileset, new[] { "Cliff" });
			if (externalCircularBias > 0)
			{
				Log.Write("debug", "creating circular cliff map border");
				var cliffRing = new Matrix<bool>(size);
				cliffRing.DrawCircle(
					center: mapCenter,
					radius: minSpan / 2.0f - minimumLandSeaThickness,
					setTo: (_, _) => true,
					invert: true);
				var cliffs = MatrixUtils.BordersToPoints(cliffRing);
				foreach (var cliff in cliffs)
				{
					var isLoop = cliff[0] == cliff[^1];
					TilingPath cliffPath;
					if (isLoop)
						cliffPath = new TilingPath(
							map,
							cliff,
							(minimumMountainThickness - 1) / 2,
							"Cliff",
							"Cliff",
							loopedCliffPermittedTemplates);
					else
						cliffPath = new TilingPath(
							map,
							cliff,
							(minimumMountainThickness - 1) / 2,
							"Clear",
							"Clear",
							nonLoopedCliffPermittedTemplates);
					cliffPath
						.ExtendEdge(4)
						.OptimizeLoop();
					if (cliffPath.Tile(cliffTilingRandom) == null)
						throw new MapGenerationException("Could not fit tiles for exterior circle cliffs");
				}
			}

			if (mountains > 0.0f || externalCircularBias == 1)
			{
				Log.Write("debug", "mountains: calculating elevation roughness");
				var roughnessMatrix = MatrixUtils.GridVariance(elevation, roughnessRadius).Map(v => MathF.Sqrt(v));
				MatrixUtils.CalibrateQuantileInPlace(
					roughnessMatrix,
					0.0f,
					1.0f - roughness);
				var cliffMask = roughnessMatrix.Map(v => v >= 0.0f);
				var mountainElevation = elevation.Clone();
				var cliffPlan = landPlan;
				if (externalCircularBias > 0)
				{
					cliffPlan.DrawCircle(
						center: mapCenter,
						radius: minSpan / 2.0f - (minimumLandSeaThickness + minimumMountainThickness),
						setTo: (_, _) => false,
						invert: true);
				}

				for (var altitude = 1; altitude <= maximumAltitude; altitude++)
				{
					Log.Write("debug", $"mountains: altitude {altitude}: determining eligible area for cliffs");

					// Limit mountain area to the existing mountain space (starting with all available land)
					var roominess = MatrixUtils.ChebyshevRoom(cliffPlan, true);
					var available = 0;
					var total = size.X * size.Y;
					for (var n = 0; n < mountainElevation.Data.Length; n++)
					{
						if (roominess.Data[n] < minimumTerrainContourSpacing)
						{
							// Too close to existing cliffs (or coastline)
							mountainElevation.Data[n] = -1.0f;
						}
						else
						{
							available++;
						}

						total++;
					}

					var availableFraction = (float)available / total;
					MatrixUtils.CalibrateQuantileInPlace(
						mountainElevation,
						0.0f,
						1.0f - availableFraction * mountains);
					Log.Write("debug", $"mountains: altitude {altitude}: fixing terrain anomalies");
					cliffPlan = MatrixUtils.BooleanBlotch(
						mountainElevation.Map(v => v >= 0),
						terrainSmoothing,
						smoothingThreshold,
						minimumMountainThickness,
						/*bias=*/false);
					Log.Write("debug", $"mountains: altitude {altitude}: tracing cliffs");
					var unmaskedCliffs = MatrixUtils.BordersToPoints(cliffPlan);
					Log.Write("debug", $"mountains: altitude {altitude}: appling roughness mask to cliffs");
					var maskedCliffs = TilingPath.MaskPathPoints(unmaskedCliffs, cliffMask);
					var cliffs = maskedCliffs.Where(cliff => cliff.Length >= minimumCliffLength).ToArray();
					if (cliffs.Length == 0)
						break;
					Log.Write("debug", $"mountains: altitude {altitude}: fitting and laying tiles");
					foreach (var cliff in cliffs)
					{
						var isLoop = cliff[0] == cliff[^1];
						TilingPath cliffPath;
						if (isLoop)
							cliffPath = new TilingPath(
								map,
								cliff,
								(minimumMountainThickness - 1) / 2,
								"Cliff",
								"Cliff",
								loopedCliffPermittedTemplates);
						else
							cliffPath = new TilingPath(
								map,
								cliff,
								(minimumMountainThickness - 1) / 2,
								"Clear",
								"Clear",
								nonLoopedCliffPermittedTemplates);
						cliffPath
							.ExtendEdge(4)
							.OptimizeLoop();
						if (cliffPath.Tile(cliffTilingRandom) == null)
							throw new MapGenerationException("Could not fit tiles for cliffs");
					}
				}
			}

			if (forests > 0.0f)
			{
				Log.Write("debug", "forests: generating noise");
				var forestNoise = NoiseUtils.SymmetricFractalNoise(
					forestRandom,
					size,
					rotations,
					mirror,
					forestFeatureSize,
					wavelength => MathF.Pow(wavelength, forestClumpiness));
				MatrixUtils.CalibrateQuantileInPlace(
					forestNoise,
					0.0f,
					1.0f - forests);

				var forestPlan = forestNoise.Map(v => v >= 0.0f);

				Log.Write("debug", "forests: planting trees");
				for (var y = 0; y < size.Y; y++)
				{
					for (var x = 0; x < size.X; x++)
					{
						var mpos = new MPos(x, y);
						if (map.GetTerrainIndex(mpos) != clearIndex)
							forestPlan[x, y] = false;
					}
				}

				if (forestCutout > 0)
				{
					var space = new Matrix<bool>(size);
					for (var y = 0; y < size.Y; y++)
					{
						for (var x = 0; x < size.X; x++)
						{
							var mpos = new MPos(x, y);
							space[x, y] = map.GetTerrainIndex(mpos) == clearIndex;
						}
					}

					if (trivialRotate)
					{
						// Improve symmetry.
						var newSpace = new Matrix<bool>(size);
						Symmetry.RotateAndMirrorOverGridSquares(
							size,
							rotations,
							mirror,
							(sources, destination)
								=> newSpace.SetIfWithin(
									destination,
									sources.All(source => space.GetOrDefault(source, true))));
						space = newSpace;
					}

					// This is grid points, not squares. Has a size of `size + 1`.
					var deflated = MatrixUtils.DeflateSpace(space, true);
					var kernel = new Matrix<bool>(2 * forestCutout, 2 * forestCutout).Fill(true);
					var inflated = MatrixUtils.KernelDilateOrErode(deflated.Map(v => v != 0), kernel, new int2(forestCutout - 1, forestCutout - 1), true);
					for (var y = 0; y < size.Y; y++)
					{
						for (var x = 0; x < size.X; x++)
						{
							if (inflated[x, y])
								forestPlan[x, y] = false;
						}
					}
				}

				var forestReplace = Matrix<MultiBrush.Replaceability>.Zip(
					forestPlan,
					IdentifyReplaceableTiles(map, replaceabilityMap),
					(a, b) => a ? b : MultiBrush.Replaceability.None);
				MultiBrush.PaintArea(map, actorPlans, forestReplace, forestObstacles, forestTilingRandom);
			}

			if (enforceSymmetry != 0)
			{
				Log.Write("debug", "symmatry enforcement: analysing");
				if (!trivialRotate)
					throw new MapGenerationException("cannot use symmetry enforcement on non-trivial rotations");

				// This is not commutative. It can be true if main is impassable, even if other is.
				bool CheckCompatibility(byte main, byte other)
				{
					if (main == other)
						return true;
					else if (main == riverIndex || main == rockIndex || main == waterIndex || main == treeIndex)
						return true;
					else if (main == beachIndex || main == clearIndex || main == roughIndex)
					{
						if (other == riverIndex || other == rockIndex || other == waterIndex || other == treeIndex)
							return false;
						if (other == beachIndex || other == clearIndex || other == roughIndex)
							return enforceSymmetry < 2;
						else
							throw new MapGenerationException("ambiguous symmetry policy");
					}
					else
						throw new MapGenerationException("ambiguous symmetry policy");
				}

				var replace = new Matrix<MultiBrush.Replaceability>(size);
				Symmetry.RotateAndMirrorOverGridSquares(size, rotations, mirror,
					(int2[] sources, int2 destination) =>
					{
						var main = tileset.GetTerrainIndex(map.Tiles[new MPos(destination.X, destination.Y)]);
						var compatible = sources
							.Where(replace.ContainsXY)
							.Select(source => tileset.GetTerrainIndex(map.Tiles[new MPos(source.X, source.Y)]))
							.All(source => CheckCompatibility(main, source));
						replace[destination] = compatible ? MultiBrush.Replaceability.None : MultiBrush.Replaceability.Actor;
					});
				Log.Write("debug", "symmetry enforcement: obstructing");
				MultiBrush.PaintArea(map, actorPlans, replace, forestObstacles, random);
			}

			var playableArea = new Matrix<bool>(size);
			{
				Log.Write("debug", "determining playable regions");
				var (regions, regionMask, playability) = PlayableSpace.FindPlayableRegions(map, actorPlans, playabilityMap);
				PlayableSpace.Region largest = null;
				var disqualifications = new HashSet<int>();
				if (externalCircularBias > 0)
				{
					var forbiddenSpace = new Matrix<bool>(size);
					forbiddenSpace.DrawCircle(
						center: mapCenter,
						radius: minSpan / 2.0f - 1.0f,
						setTo: (_, _) => true,
						invert: true);
					for (var n = 0; n < forbiddenSpace.Data.Length; n++)
					{
						if (forbiddenSpace[n] && regionMask[n] != PlayableSpace.NULL_REGION)
							disqualifications.Add(regionMask[n]);
					}
				}

				foreach (var region in regions)
				{
					if (disqualifications.Contains(region.Id))
						continue;
					if (largest == null || region.PlayableArea > largest.PlayableArea)
						largest = region;
				}

				if (largest == null)
					throw new MapGenerationException("could not find a playable region");
				if (denyWalledAreas)
				{
					Log.Write("debug", "obstructing semi-unreachable areas");

					var replace = Matrix<MultiBrush.Replaceability>.Zip(
						regionMask,
						IdentifyReplaceableTiles(map, replaceabilityMap),
						(a, b) => a == largest.Id ? MultiBrush.Replaceability.None : b);
					MultiBrush.PaintArea(map, actorPlans, replace, unplayableObstacles, random);
				}

				for (var n = 0; n < playableArea.Data.Length; n++)
				{
					playableArea[n] = playability[n] == PlayableSpace.Playability.Playable && regionMask[n] == largest.Id;
				}
			}

			if (roads)
			{
				var space = new Matrix<bool>(size);
				for (var y = 0; y < size.Y; y++)
				{
					for (var x = 0; x < size.X; x++)
					{
						space[x, y] = playableArea[x, y] && tileset.GetTerrainIndex(map.Tiles[new MPos(x, y)]) == clearIndex;
					}
				}

				if (trivialRotate)
				{
					// Improve symmetry.
					var newSpace = new Matrix<bool>(size);
					Symmetry.RotateAndMirrorOverGridSquares(
						size,
						rotations,
						mirror,
						(sources, destination)
							=> newSpace.SetIfWithin(
								destination,
								sources.All(source => space.GetOrDefault(source, true))));
					space = newSpace;
				}

				{
					var kernel = new Matrix<bool>(roadSpacing * 2 + 1, roadSpacing * 2 + 1);
					kernel.DrawCircle(
						center: new float2(roadSpacing, roadSpacing),
						radius: roadSpacing,
						setTo: (_, _) => true,
						invert: false);
					space = MatrixUtils.KernelDilateOrErode(
						space,
						kernel,
						new int2(roadSpacing, roadSpacing),
						false);
				}

				var deflated = MatrixUtils.DeflateSpace(space, true);
				var pointArrays = TilingPath.DirectionMapToPaths(deflated);
				pointArrays = TilingPath.RetainDisjointPaths(pointArrays, size);

				var roadPermittedTemplates =
					TilingPath.PermittedSegments.FromInnerAndTerminal(
						tileset, new[] { "Road", "RoadIn", "RoadOut" }, new[] { "Clear" });

				foreach (var pointArray in pointArrays)
				{
					// Currently, never looped.
					var path = new TilingPath(
						map,
						pointArray,
						roadSpacing - 1,
						"Clear",
						"Clear",
						roadPermittedTemplates);
					path
						.ChirallyNormalize()
						.Shrink(4 + roadShrink, 12)
						.InertiallyExtend(2, 8)
						.ExtendEdge(4);

					// Shrinking may have deleted the path.
					if (path.Points == null)
						continue;

					// Roads that are _almost_ vertical or horizontal tile badly. Filter them out.
					var minX = path.Points.Min((p) => p.X);
					var minY = path.Points.Min((p) => p.Y);
					var maxX = path.Points.Max((p) => p.X);
					var maxY = path.Points.Max((p) => p.Y);
					if (maxX - minX < 6 || maxY - minY < 6)
						continue;

					if (path.Tile(roadTilingRandom) == null)
						throw new MapGenerationException("Could not fit tiles for roads");
				}
			}

			if (createEntities)
			{
				Log.Write("debug", "entities: determining eligible space");

				var zoneable = new Matrix<bool>(size);
				for (var y = map.Bounds.Top; y < map.Bounds.Bottom; y++)
				{
					for (var x = map.Bounds.Left; x < map.Bounds.Right; x++)
					{
						zoneable[x, y] = playableArea[x, y] && tileset.GetTerrainIndex(map.Tiles[new MPos(x, y)]) == clearIndex;
					}
				}

				MatrixUtils.ReserveForEntitiesInPlace(zoneable, actorPlans, (_) => false);
				if (trivialRotate)
				{
					// Improve symmetry.
					var newZoneable = new Matrix<bool>(size);
					Symmetry.RotateAndMirrorOverGridSquares(
						size,
						rotations,
						mirror,
						(sources, destination)
							=> newZoneable.SetIfWithin(
								destination,
								sources.All(source => zoneable.GetOrDefault(source, true))));
					zoneable = newZoneable;
				}

				if (!trivialRotate || !trivialMirror)
				{
					zoneable.DrawCircle(
						center: mapCenter,
						radius: minSpan / 2.0f - 1.0f,
						setTo: (_, _) => false,
						invert: true);
				}

				if (rotations > 1 || mirror != 0)
				{
					// Reserve the center of the map - otherwise it will mess with rotations
					zoneable.DrawCircle(
						center: mapCenter,
						radius: 1.0f,
						setTo: (_, _) => false,
						invert: false);
				}

				// Spawn generation
				Log.Write("debug", "entities: zoning for spawns");
				for (var iteration = 0; iteration < players; iteration++)
				{
					var roominess = MatrixUtils.ChebyshevRoom(zoneable, false)
						.Transform(v => Math.Min(v, spawnRegionSize));
					var spawnPreference =
						CalculateSpawnPreferences(
							roominess,
							minSpan * centralSpawnReservationFraction,
							spawnRegionSize,
							rotations,
							mirror);
					var (chosenXY, chosenValue) = spawnPreference.FindRandomBest(
						playerRandom,
						(a, b) => a.CompareTo(b));

					if (chosenValue <= 1)
					{
						Log.Write("debug", "No ideal spawn location. Ignoring central reservation constraint.");
						(chosenXY, chosenValue) = roominess.FindRandomBest(
							playerRandom,
							(a, b) => a.CompareTo(b));
					}

					var room = chosenValue - 1;
					var spawn = new ActorPlan(map, "mpspawn")
					{
						Int2Location = chosenXY,
					};

					var mineWeights = MatrixUtils.WalkingDistances(
						zoneable,
						new[] { chosenXY },
						spawnRegionSize);
					mineWeights.Transform(v =>
						{
							var preferedRange = (spawnBuildSize + spawnRegionSize * 2) / 2;
							return MathF.Ceiling(v > preferedRange ? 2 * preferedRange - v : v);
						});

					var mines = new List<ActorPlan>();
					for (var mine = 0; mine < spawnMines; mine++)
					{
						var (xy, value) = mineWeights.FindRandomBest(playerRandom, (a, b) => a.CompareTo(b));
						if (value <= 1.0f)
							break;
						var minePlan =
							playerRandom.NextFloat() < gemUpgrade
								? new ActorPlan(map, "gmine")
								: new ActorPlan(map, "mine");
						minePlan.Int2Location = xy;
						mines.Add(minePlan);
						mineWeights.DrawCircle(
							center: minePlan.Int2Location,
							radius: 1.0f,
							setTo: (_, _) => 0.0f,
							invert: false);
					}

					var projectedSpawns = Symmetry.RotateAndMirrorActorPlan(spawn, rotations, mirror);
					actorPlans.AddRange(projectedSpawns);
					foreach (var projectedSpawn in projectedSpawns)
					{
						zoneable.DrawCircle(
							center: projectedSpawn.Int2Location,
							radius: spawnReservation,
							setTo: (_, _) => false,
							invert: false);
					}

					var projectedMines = Symmetry.RotateAndMirrorActorPlans(mines, rotations, mirror);
					actorPlans.AddRange(projectedMines);
					foreach (var projectedMine in projectedMines)
					{
						zoneable.DrawCircle(
							center: projectedMine.Int2Location,
							radius: mineReservation,
							setTo: (_, _) => false,
							invert: false);
					}
				}

				// Expansions
				Log.Write("debug", "entities: zoning for expansions");
				{
					var minesRemaining = maximumExpansionMines;
					while (minesRemaining > 0)
					{
						var expansionZoneable = zoneable.Clone();
						if (centralExpansionReservationFraction > 0)
						{
							expansionZoneable.DrawCircle(
								center: mapCenter,
								radius: minSpan * centralExpansionReservationFraction,
								setTo: (_, _) => false,
								invert: false);
						}

						var expansionRoominess = MatrixUtils.ChebyshevRoom(expansionZoneable, false)
							.Transform((v) => Math.Min(v, maximumExpansionSize + expansionBorder));
						var (chosenXY, chosenValue) = expansionRoominess.FindRandomBest(
							expansionRandom,
							(a, b) => a.CompareTo(b));
						var room = chosenValue - 1;
						var radius2 = room - expansionBorder;
						if (radius2 < minimumExpansionSize)
							break;
						if (radius2 > maximumExpansionSize)
							radius2 = maximumExpansionSize;
						var radius1 = Math.Min(Math.Min(expansionInner, room), radius2);
						var mineCount = Math.Min(minesRemaining, expansionRandom.Next(maximumMinesPerExpansion) + 1);
						minesRemaining -= mineCount;

						if (radius1 < 1.0f)
							break;

						var mines = new List<ActorPlan>();
						var mineWeights = new Matrix<float>(size);
						var radius1Sq = radius1 * radius1;
						mineWeights.DrawCircle(
							center: chosenXY,
							radius: radius2,
							setTo: (rSq, _) => rSq >= radius1Sq ? (1.0f * rSq) : 0.0f,
							invert: false);
						for (var mine = 0; mine < mineCount; mine++)
						{
							var xy = mineWeights.XY(expansionRandom.PickWeighted(mineWeights.Data));
							var minePlan =
								expansionRandom.NextFloat() < gemUpgrade
									? new ActorPlan(map, "gmine")
									: new ActorPlan(map, "mine");
							minePlan.Int2Location = xy;
							mines.Add(minePlan);
							mineWeights.DrawCircle(
								center: minePlan.Int2Location,
								radius: 1.0f,
								setTo: (_, _) => 0.0f,
								invert: false);
						}

						var projectedMines = Symmetry.RotateAndMirrorActorPlans(mines, rotations, mirror);
						actorPlans.AddRange(projectedMines);
						foreach (var projectedMine in projectedMines)
						{
							zoneable.DrawCircle(
								center: projectedMine.Int2Location,
								radius: mineReservation,
								setTo: (_, _) => false,
								invert: false);
						}
					}
				}

				// Neutral buildings
				Log.Write("debug", "entities: zoning for tech structures");
				{
					var targetBuildingCount =
						(maximumBuildings != 0)
							? expansionRandom.Next(minimumBuildings, maximumBuildings + 1)
							: 0;
					for (var i = 0; i < targetBuildingCount; i++)
					{
						var roominess = MatrixUtils.ChebyshevRoom(zoneable, false)
							.Transform((v) => Math.Min(v, 3));
						var (chosenXY, chosenValue) = roominess.FindRandomBest(
							buildingRandom,
							(a, b) => a.CompareTo(b));
						if (chosenValue < 3)
							break;
						var types = new string[]
							{
								"fcom",
								"hosp",
								"miss",
								"bio",
								"oilb",
							};
						var typeChoice = random.PickWeighted(
							new float[]
							{
								weightFcom,
								weightHosp,
								weightMiss,
								weightBio,
								weightOilb,
							});
						var type = types[typeChoice];
						var actorPlan = new ActorPlan(map, type)
						{
							CenterLocation = new float2(chosenXY.X + 0.5f, chosenXY.Y + 0.5f),
						};

						var projectedBuildings = Symmetry.RotateAndMirrorActorPlan(actorPlan, rotations, mirror);
						actorPlans.AddRange(projectedBuildings);
						foreach (var projectedBuilding in projectedBuildings)
						{
							zoneable.DrawCircle(
								center: projectedBuilding.Int2Location,
								radius: 2.0f,
								setTo: (_, _) => false,
								invert: false);
						}
					}
				}

				// Grow resources
				{
					Log.Write("debug", "ore: generating noise");
					var orePattern = NoiseUtils.SymmetricFractalNoise(
						resourceRandom,
						size,
						rotations,
						mirror,
						resourceFeatureSize,
						wavelength => MathF.Pow(wavelength, oreClumpiness));
					{
						MatrixUtils.CalibrateQuantileInPlace(
							orePattern,
							0.0f,
							0.0f);
						var max = orePattern.Data.Max();
						for (var n = 0; n < orePattern.Data.Length; n++)
						{
							orePattern[n] /= max;
							orePattern[n] += oreUniformity;
						}
					}

					Log.Write("debug", "ore: planning ore");
					var oreStrength = new Matrix<float>(size);
					var gemStrength = new Matrix<float>(size);
					foreach (var actorPlan in actorPlans)
					{
						switch (actorPlan.Reference.Type)
						{
							case "mine":
								oreStrength.DrawCircle(
									center: actorPlan.Int2Location,
									radius: 16,
									setTo: (rSq, v) => v + 1.0f / (1.0f + MathF.Sqrt(rSq)),
									invert: false);
								break;
							case "gmine":
								gemStrength.DrawCircle(
									center: actorPlan.Int2Location,
									radius: 16,
									setTo: (rSq, v) => v + 1.0f / (1.0f + MathF.Sqrt(rSq)),
									invert: false);
								break;
							default:
								break;
						}
					}

					var orePlan = new Matrix<float>(size);
					for (var y = 0; y < size.Y; y++)
					{
						for (var x = 0; x < size.X; x++)
						{
							if (playableArea[x, y] && map.GetTerrainIndex(new MPos(x, y)) == clearIndex)
								orePlan[x, y] = orePattern[x, y] * MathF.Max(oreStrength[x, y], gemStrength[x, y]);
							else
								orePlan[x, y] = float.NegativeInfinity;
						}
					}

					var spawnBuildSizeSq = spawnBuildSize * spawnBuildSize;
					foreach (var actorPlan in actorPlans)
					{
						if (actorPlan.Reference.Type == "mpspawn")
							orePlan.DrawCircle(
								center: actorPlan.Int2Location,
								radius: spawnRegionSize * 2,
								setTo: (rSq, v) => v * (1.0f + spawnResourceBias * spawnBuildSizeSq / rSq),
								invert: false);
					}

					foreach (var actorPlan in actorPlans)
					{
						if (actorPlan.Reference.Type == "mpspawn")
							orePlan.DrawCircle(
								center: actorPlan.Int2Location,
								radius: spawnBuildSize,
								setTo: (_, _) => float.NegativeInfinity,
								invert: false);
					}

					foreach (var actorPlan in actorPlans)
					{
						foreach (var (cpos, _) in actorPlan.Footprint())
						{
							var mpos = cpos.ToMPos(map);
							var xy = new int2(mpos.U, mpos.V);
							if (orePlan.ContainsXY(xy))
								orePlan[xy] = float.NegativeInfinity;
						}
					}

					if (trivialRotate)
					{
						// Improve symmetry
						Symmetry.RotateAndMirrorOverGridSquares(
							size,
							rotations,
							mirror,
							(sources, destination)
								=> orePlan.SetIfWithin(
									destination,
									sources.Min(source => orePlan.GetOrDefault(source, float.PositiveInfinity))));
					}

					var remaining = resourcesPerPlayer * players * Symmetry.RotateAndMirrorProjectionCount(rotations, mirror);
					var priorities = new PriorityArray<float>(orePlan.Data.Length, float.PositiveInfinity);
					for (var n = 0; n < orePlan.Data.Length; n++)
					{
						priorities[n] = -orePlan[n];
					}

					const byte ORE_RESOURCE = 1;
					const byte GEM_RESOURCE = 2;
					const byte ORE_DENSITY = 12;
					const byte GEM_DENSITY = 3;
					var resources = new Matrix<byte>(size);
					var densities = new Matrix<byte>(size);

					// Return resource value of a given square.
					// See https://github.com/OpenRA/OpenRA/blob/9302bac6199fbc925a85fd7a08fc2ba4b9317d16/OpenRA.Mods.Common/Traits/World/ResourceLayer.cs#L144-L166
					// https://github.com/OpenRA/OpenRA/blob/9302bac6199fbc925a85fd7a08fc2ba4b9317d16/OpenRA.Mods.Common/Traits/World/EditorResourceLayer.cs#L175-L183
					int CheckValue(int2 c)
					{
						if (!resources.ContainsXY(c))
							return 0;
						var resource = resources[c];
						if (resource == 0)
							return 0;
						var adjacent = 0;
						for (var y = c.Y - 1; y <= c.Y + 1; y++)
						{
							for (var x = c.X - 1; x <= c.X + 1; x++)
							{
								if (!resources.ContainsXY(x, y))
									continue;
								if (resources[x, y] == resource)
									adjacent++;
							}
						}

						var maxDensity =
							resource == ORE_RESOURCE ? 12 : 3;
						var valuePerDensity =
							resource == ORE_RESOURCE ? 25 : 50;
						var density = Math.Max(maxDensity * adjacent / /*maxAdjacent=*/9, 1);

						// density + 1 to mirror a bug that got ossified due to balancing.
						return valuePerDensity * (density + 1);
					}

					int CheckValue3By3(int2 c)
					{
						var total = 0;
						for (var y = c.Y - 1; y <= c.Y + 1; y++)
						{
							for (var x = c.X - 1; x <= c.X + 1; x++)
							{
								total += CheckValue(new int2(x, y));
							}
						}

						return total;
					}

					// Set and return change in overall value.
					int AddResource(int2 c, byte resource, byte density)
					{
						var n = resources.Index(c);
						priorities[n] = float.PositiveInfinity;
						if (resources[n] != 0)
						{
							// Generally shouldn't happen, but perhaps a rotation/mirror related inaccuracy.
							return 0;
						}

						var oldValue = CheckValue3By3(c);
						resources[n] = resource;
						densities[n] = density;
						var newValue = CheckValue3By3(c);
						return newValue - oldValue;
					}

					Log.Write("debug", "ore: placing ore");
					while (remaining > 0)
					{
						var n = priorities.GetMinIndex();
						if (priorities[n] == float.PositiveInfinity)
						{
							Log.Write("debug", "Could not meet resource target");
							break;
						}

						var chosenXY = resources.XY(n);
						foreach (var square in Symmetry.RotateAndMirrorGridSquare(chosenXY, size, rotations, mirror))
						{
							if (!resources.ContainsXY(square))
								continue;

							if (oreStrength[n] >= gemStrength[n])
								remaining -= AddResource(square, ORE_RESOURCE, ORE_DENSITY);
							else
								remaining -= AddResource(square, GEM_RESOURCE, GEM_DENSITY);
						}
					}

					for (var y = 0; y < size.Y; y++)
					{
						for (var x = 0; x < size.X; x++)
						{
							map.Resources[new MPos(x, y)] = new ResourceTile(resources[x, y], densities[x, y]);
						}
					}
				}
			}

			map.PlayerDefinitions = new MapPlayers(map.Rules, 0).ToMiniYaml();
			map.ActorDefinitions = actorPlans
				.Select((plan, i) => new MiniYamlNode($"Actor{i}", plan.Reference.Save()))
				.ToImmutableArray();
		}

		static Matrix<MultiBrush.Replaceability> IdentifyReplaceableTiles(
			Map map,
			Dictionary<TerrainTile, MultiBrush.Replaceability> replaceabilityMap)
		{
			var output = new Matrix<MultiBrush.Replaceability>(map.MapSize);

			foreach (var cell in map.AllCells)
			{
				var mpos = cell.ToMPos(map);
				var tile = map.Tiles[mpos];
				var replaceability = MultiBrush.Replaceability.Any;
				if (replaceabilityMap.TryGetValue(tile, out var value))
					replaceability = value;
				output[mpos.U, mpos.V] = replaceability;
			}

			return output;
		}

		static Matrix<int> CalculateSpawnPreferences(Matrix<int> roominess, float centralReservation, int spawnRegionSize, int rotations, Symmetry.Mirror mirror)
		{
			var preferences = roominess.Map(r => Math.Min(r, spawnRegionSize));
			var centralReservationSq = centralReservation * centralReservation;
			var spawnRegionSize2Sq = 4 * spawnRegionSize * spawnRegionSize;
			var size = roominess.Size;

			// This -0.5 is required to compensate for the top-left vs the center of a grid square.
			var center = new float2(size) / 2.0f - new float2(0.5f, 0.5f);

			const float SQRT2 = 1.4142135623730951f;

			// Mark areas close to the center or mirror lines as last resort.
			for (var y = 0; y < size.Y; y++)
			{
				for (var x = 0; x < size.X; x++)
				{
					if (preferences[x, y] <= 1)
						continue;
					switch (mirror)
					{
						case Symmetry.Mirror.None:
							var r = new float2(x, y) - center;
							if (r.LengthSquared <= centralReservationSq)
								preferences[x, y] = 1;
							break;
						case Symmetry.Mirror.LeftMatchesRight:
							if (MathF.Abs(x - center.X) <= centralReservation)
								preferences[x, y] = 1;
							break;
						case Symmetry.Mirror.TopLeftMatchesBottomRight:
							if (MathF.Abs(x - center.X + (y - center.Y)) <= centralReservation * SQRT2)
								preferences[x, y] = 1;
							break;
						case Symmetry.Mirror.TopMatchesBottom:
							if (MathF.Abs(y - center.Y) <= centralReservation)
								preferences[x, y] = 1;
							break;
						case Symmetry.Mirror.TopRightMatchesBottomLeft:
							if (MathF.Abs(x - center.X - (y - center.Y)) <= centralReservation * SQRT2)
								preferences[x, y] = 1;
							break;
						default:
							throw new ArgumentException("bad mirror direction");
					}

					if (preferences[x, y] <= 1)
						continue;

					var worstSpacing = Symmetry.RotateAndMirrorProjectionProximity(new int2(x, y), size, rotations, mirror) / 2;
					if (worstSpacing < preferences[x, y])
						preferences[x, y] = worstSpacing;
				}
			}

			return preferences;
		}

		public bool ShowInEditor(Map map, ModData modData)
		{
			switch (map.Tileset)
			{
				case "DESERT":
				case "SNOW":
				case "TEMPERAT":
					return true;
				default:
					return false;
			}
		}
	}
}
