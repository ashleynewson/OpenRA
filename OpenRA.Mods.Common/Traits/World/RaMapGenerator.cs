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
using OpenRA.Mods.Common.Terrain;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Map generator for Red Alert maps.")]
	[TraitLocation(SystemActors.World)]
	public sealed class RaMapGeneratorInfo : TraitInfo, IMapGeneratorInfo
	{
		[Desc("Human-readable name this generator uses.")]
		public readonly string Name = "OpenRA Red Alert";

		[FieldLoader.Require]
		[Desc("Internal id for this map generator.")]
		public readonly string Type = null;

		string IMapGeneratorInfo.Type => Type;

		string IMapGeneratorInfo.Name => Name;

		public override object Create(ActorInitializer init) { return new RaMapGenerator(this); }
	}

	public sealed class RaMapGenerator : IMapGenerator
	{
		readonly RaMapGeneratorInfo info;

		IMapGeneratorInfo IMapGenerator.Info => info;

		public RaMapGenerator(RaMapGeneratorInfo info)
		{
			this.info = info;
		}

		public IEnumerable<MapGeneratorSetting> GetDefaultSettings(Map map, ModData modData)
		{
			return ImmutableList.Create(
				new MapGeneratorSetting("#Primary", "Primary settings", new MapGeneratorSetting.SectionValue()),
				new MapGeneratorSetting("Rotations", "Rotations", new MapGeneratorSetting.IntegerValue(2)),
				new MapGeneratorSetting("Mirror", "Mirror", new MapGeneratorSetting.EnumValue(
					ImmutableList.Create(
						new KeyValuePair<string, string>("0", "None"),
						new KeyValuePair<string, string>("1", "Left matches right"),
						new KeyValuePair<string, string>("2", "Top-left matches bottom-right"),
						new KeyValuePair<string, string>("3", "Top matches bottom"),
						new KeyValuePair<string, string>("4", "Top-right matches bottom-left")
					),
					"0"
				)),
				new MapGeneratorSetting("Players", "Players per symmetry", new MapGeneratorSetting.IntegerValue(1)),

				new MapGeneratorSetting("#Terrain", "Terrain settings", new MapGeneratorSetting.SectionValue()),
				new MapGeneratorSetting("WavelengthScale", "Noise Wavelength Scale", new MapGeneratorSetting.FloatValue(0.2)),
				new MapGeneratorSetting("Water", "Water fraction", new MapGeneratorSetting.FloatValue(0.2)),
				new MapGeneratorSetting("Mountains", "Mountain fraction (nesting)", new MapGeneratorSetting.FloatValue(0.1)),
				new MapGeneratorSetting("Forests", "Forest fraction", new MapGeneratorSetting.FloatValue(0.025)),
				new MapGeneratorSetting("ForestCutout", "Forest path cutout size", new MapGeneratorSetting.IntegerValue(2)),
				new MapGeneratorSetting("ExternalCircularBias", "Square/circular map", new MapGeneratorSetting.EnumValue(
					ImmutableList.Create(
						new KeyValuePair<string, string>("0", "Square"),
						new KeyValuePair<string, string>("-1", "Circle (outside is water)"),
						new KeyValuePair<string, string>("1", "Circle (outside is mountain)")
					),
					"0"
				)),
				new MapGeneratorSetting("TerrainSmoothing", "Terrain smoothing", new MapGeneratorSetting.IntegerValue(4)),
				new MapGeneratorSetting("SmoothingThreshold", "Smoothing threshold", new MapGeneratorSetting.FloatValue(0.33)),
				new MapGeneratorSetting("MinimumLandSeaThickness", "Minimum land/sea thickness", new MapGeneratorSetting.IntegerValue(5)),
				new MapGeneratorSetting("MinimumMountainThickness", "Minimum mountain thickness", new MapGeneratorSetting.IntegerValue(5)),
				new MapGeneratorSetting("MaximumAltitude", "Maximum mountain altitude", new MapGeneratorSetting.IntegerValue(8)),
				new MapGeneratorSetting("RoughnessRadius", "Roughness sampling size", new MapGeneratorSetting.IntegerValue(5)),
				new MapGeneratorSetting("Roughness", "Terrain roughness", new MapGeneratorSetting.FloatValue(0.5)),
				new MapGeneratorSetting("MinimumTerrainContourSpacing", "Minimum contour spacing", new MapGeneratorSetting.IntegerValue(6)),
				new MapGeneratorSetting("MinimumCliffLength", "Minimum cliff length", new MapGeneratorSetting.IntegerValue(10)),
				new MapGeneratorSetting("ForestClumpiness", "Forest clumpiness", new MapGeneratorSetting.FloatValue(0.5)),
				new MapGeneratorSetting("DenyWalledAreas", "Deny areas with limited access", new MapGeneratorSetting.BooleanValue(true)),
				new MapGeneratorSetting("EnforceSymmetry", "Symmetry Corrections", new MapGeneratorSetting.EnumValue(
					ImmutableList.Create(
						new KeyValuePair<string, string>("0", "None"),
						new KeyValuePair<string, string>("1", "Match passability"),
						new KeyValuePair<string, string>("2", "Match terrain type")
					),
					"0"
				)),
				new MapGeneratorSetting("Roads", "Roads", new MapGeneratorSetting.BooleanValue(true)),
				new MapGeneratorSetting("RoadSpacing", "Road spacing", new MapGeneratorSetting.IntegerValue(5)),

				new MapGeneratorSetting("#Entities", "Entity settings", new MapGeneratorSetting.SectionValue()),
				new MapGeneratorSetting("CreateEntities", "Create entities", new MapGeneratorSetting.BooleanValue(true)),
				new MapGeneratorSetting("CentralSpawnReservationFraction", "Central reservation against spawns", new MapGeneratorSetting.FloatValue(0.3)),
				new MapGeneratorSetting("CentralExpansionReservationFraction", "Central reservation against expansions", new MapGeneratorSetting.FloatValue(0.1)),
				new MapGeneratorSetting("MineReservation", "Unrelated mine spacing", new MapGeneratorSetting.IntegerValue(8)),
				new MapGeneratorSetting("SpawnRegionSize", "Spawn region size", new MapGeneratorSetting.IntegerValue(16)),
				new MapGeneratorSetting("SpawnBuildSize", "Spawn build size", new MapGeneratorSetting.IntegerValue(8)),
				new MapGeneratorSetting("SpawnMines", "Spawn mine count", new MapGeneratorSetting.IntegerValue(3)),
				new MapGeneratorSetting("SpawnReservation", "Spawn reservation size", new MapGeneratorSetting.IntegerValue(20)),
				new MapGeneratorSetting("SpawnResourceBias", "Spawn resource placement bais", new MapGeneratorSetting.FloatValue(1.25)),
				new MapGeneratorSetting("ResourcesPerPlace", "Starting resource value per player", new MapGeneratorSetting.IntegerValue(50000)),
				new MapGeneratorSetting("GemUpgrade", "Ore to gem upgrade probability", new MapGeneratorSetting.FloatValue(0.05)),
				new MapGeneratorSetting("OreUniformity", "Ore uniformity", new MapGeneratorSetting.FloatValue(0.25)),
				new MapGeneratorSetting("OreClumpiness", "Ore clumpiness", new MapGeneratorSetting.FloatValue(0.25)),
				new MapGeneratorSetting("MaximumExpansionMines", "Expansion mines per player", new MapGeneratorSetting.IntegerValue(5)),
				new MapGeneratorSetting("MaximumMinesPerExpansion", "Maximum mines per expansion", new MapGeneratorSetting.IntegerValue(2)),
				new MapGeneratorSetting("MinimumExpansionSize", "Minimum expansion size", new MapGeneratorSetting.IntegerValue(2)),
				new MapGeneratorSetting("MaximumExpansionSize", "Maximum expansion size", new MapGeneratorSetting.IntegerValue(12)),
				new MapGeneratorSetting("ExpansionInner", "Expansion inner size", new MapGeneratorSetting.IntegerValue(2)),
				new MapGeneratorSetting("ExpansionBorder", "Expansion border size", new MapGeneratorSetting.IntegerValue(1)),
				new MapGeneratorSetting("MinimumBuildings", "Minimum building count per symmetry", new MapGeneratorSetting.IntegerValue(0)),
				new MapGeneratorSetting("MaximumBuildings", "Maximum building count per symmetry", new MapGeneratorSetting.IntegerValue(3)),
				new MapGeneratorSetting("WeightFcom", "Building weight: Forward Command", new MapGeneratorSetting.FloatValue(1)),
				new MapGeneratorSetting("WeightHosp", "Building weight: Hospital", new MapGeneratorSetting.FloatValue(2)),
				new MapGeneratorSetting("WeightMiss", "Building weight: Communications Center", new MapGeneratorSetting.FloatValue(2)),
				new MapGeneratorSetting("WeightBio", "Building weight: Biological Lab", new MapGeneratorSetting.FloatValue(0)),
				new MapGeneratorSetting("WeightOilb", "Building weight: Oil Derrick", new MapGeneratorSetting.FloatValue(8))
			);
		}

		public IEnumerable<MapGeneratorSetting> GetPresetSettings(Map map, ModData modData, string preset)
		{
			var settings = GetDefaultSettings(map, modData);
			switch (preset) {
				case null:
					break;
				case "plains":
					settings.Where(s => s.Name == "Water").First().Set<double>(0.0);
					break;
				default:
					throw new ArgumentException("Invalid preset.");
			}
			return settings;
		}

		public IEnumerable<KeyValuePair<string, string>> GetPresets(Map map, ModData modData)
		{
			return ImmutableList.Create(
				new KeyValuePair<string, string>("plains", "Plains")
			);
		}

		public void Generate(Map map, ModData modData, MersenneTwister random, IEnumerable<MapGeneratorSetting> settingsEnumerable)
		{
			// TODO: translate exception messages?
			var settings = Enumerable.ToDictionary(settingsEnumerable, s => s.Name);
			var tileset = modData.DefaultTerrainInfo[map.Tileset];
			throw new MapGenerationException("Unimplemented");
		}

		public bool ShowInEditor(Map map, ModData modData)
		{
			switch (map.Tileset)
			{
				case "TEMPERAT":
					break;
				default:
					return false;
			}
			return true;
		}
	}
}
