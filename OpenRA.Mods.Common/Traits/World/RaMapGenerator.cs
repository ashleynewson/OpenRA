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
using System.Diagnostics;
using System.Linq;
using OpenRA;
using OpenRA.Mods.Common.MapUtils;
using OpenRA.Mods.Common.Terrain;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;

// TODO: Sort out CPos, MPos, WPos, PPos?, int2, float2, *Vec, etc.
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
		const ushort LAND_TILE = 255;
		const ushort WATER_TILE = 1;

		const double SQRT2 = 1.4142135623730951;

		const float EXTERNAL_BIAS = 1000000.0f;

		readonly RaMapGeneratorInfo info;

		IMapGeneratorInfo IMapGenerator.Info => info;

		public RaMapGenerator(RaMapGeneratorInfo info)
		{
			this.info = info;
		}

		enum Replaceability
		{

			// Area cannot be replaced by a tile or obstructing entity.
			None = 0,

			// Area must be replaced by a different tile, and may optionally be given an entity.
			Tile = 1,

			// Area must be given an entity, but the underlying tile must not change.
			Entity = 2,

			// Area can be replaced by a tile and/or entity.
			Any = 3,
		}

		enum Playability
		{
			// Area is unplayable by land/naval units.
			Unplayable = 0,

			// Area is unplayable by land/naval units, but should count as
			// being "within" a playable region. This usually applies to random
			// rock or river tiles in largely passable templates.
			Partial = 1,

			// Area is playable by either land or naval units.
			Playable = 2,
		}

		readonly struct PathTerminal
		{
			public readonly string Type;
			public readonly int Direction;

			public string SegmentType
			{
				get => $"{Type}.{MapUtils.Direction.ToString(Direction)}";
			}

			public PathTerminal(string type, int direction)
			{
				Type = type;
				Direction = direction;
			}
		}

		sealed class PermittedTemplates
		{
			// This should probably be changed to store Segments rather than Templates.
			public readonly IEnumerable<TerrainTemplateInfo> Start;
			public readonly IEnumerable<TerrainTemplateInfo> Inner;
			public readonly IEnumerable<TerrainTemplateInfo> End;
			public IEnumerable<TerrainTemplateInfo> All => Start.Union(Inner).Union(End);

			public PermittedTemplates(IEnumerable<TerrainTemplateInfo> start, IEnumerable<TerrainTemplateInfo> inner, IEnumerable<TerrainTemplateInfo> end)
			{
				Start = start;
				Inner = inner;
				End = end;
			}

			public PermittedTemplates(IEnumerable<TerrainTemplateInfo> all)
				: this(all, all, all)
			{ }

			public static IEnumerable<TerrainTemplateInfo> FindTemplates(ITemplatedTerrainInfo templatedTerrainInfo, string[] types)
				=> FindTemplates(templatedTerrainInfo, types, types);

			public static IEnumerable<TerrainTemplateInfo> FindTemplates(ITemplatedTerrainInfo templatedTerrainInfo, string[] startTypes, string[] endTypes)
			{
				return templatedTerrainInfo.Templates.Values
					.Where(
						template => template.Segments.Any(
							segment =>
								startTypes.Any(type => segment.HasStartType(type) &&
								endTypes.Any(type => segment.HasEndType(type)))))
					.ToArray();
			}
		}

		sealed class Path
		{
			public int2[] Points;
			public PathTerminal Start;
			public PathTerminal End;
			public PermittedTemplates PermittedTemplates;
			public bool IsLoop
			{
				get => Points[0] == Points[^1];
			}

			// <summary>
			// Simple constructor for paths
			// </summary>
			public Path(int2[] points, string startType, string endType, PermittedTemplates permittedTemplates)
			{
				Points = points;
				var startDirection = Direction.FromOffset(Points[1] - Points[0]);
				Start = new PathTerminal(startType, startDirection);
				var endDirection = Direction.FromOffset(IsLoop ? Points[1] - Points[0] : Points[^1] - Points[^2]);
				End = new PathTerminal(endType, endDirection);
				PermittedTemplates = permittedTemplates;
			}
		}

		// TODO: Rename to something more generic like "painting template".
		sealed class Obstacle
		{
			public float Weight;
			public readonly Map map;
			public readonly ModData modData;
			readonly List<(int2, TerrainTile)> tiles;
			readonly List<ActorPlan> entities;
			int2[] shape;

			public IEnumerable<(int2 XY, TerrainTile Tile)> Tiles => tiles;
			public IEnumerable<ActorPlan> Entities => entities;
			public bool HasTiles => tiles.Count != 0;
			public bool HasEntities => entities.Count != 0;
			public IEnumerable<int2> Shape => shape;
			public int Area => shape.Length;
			public Replaceability Contract()
			{
				var hasTiles = tiles.Count != 0;
				var hasEntities = entities.Count != 0;
				if (hasTiles && hasEntities)
					return Replaceability.Any;
				else if (hasTiles && !hasEntities)
					return Replaceability.Tile;
				else if (!hasTiles && hasEntities)
					return Replaceability.Entity;
				else
					throw new ArgumentException("Obstacle has no tiles or entities");
			}

			public Obstacle(Map map, ModData modData)
			{
				Weight = 1.0f;
				this.map = map;
				this.modData = modData;
				tiles = new List<(int2, TerrainTile)>();
				entities = new List<ActorPlan>();
				shape = Array.Empty<int2>();
			}

			Obstacle(Obstacle other)
			{
				Weight = other.Weight;
				map = other.map;
				modData = other.modData;
				tiles = other.tiles.ToList();
				entities = other.entities.ToList();
				shape = other.shape.ToArray();
			}

			public Obstacle Clone()
			{
				return new Obstacle(this);
			}

			void UpdateShape()
			{
				var xys = new HashSet<int2>();

				foreach (var (xy, _) in tiles)
				{
					xys.Add(xy);
				}

				foreach (var entity in entities)
				{
					foreach (var cpos in entity.Footprint())
					{
						var mpos = cpos.Key.ToMPos(map);
						xys.Add(new int2(mpos.U, mpos.V));
					}
				}

				shape = xys.OrderBy(xy => (xy.Y, xy.X)).ToArray();
			}

			// <summary>
			// Add tiles from a template, optionally with a given offset. By
			// default, it will be auto-offset such that the first tile is
			// under (0, 0).
			// </summary>
			public Obstacle WithTemplate(ushort templateId, int2? offset = null)
			{
				var tileset = modData.DefaultTerrainInfo[map.Tileset] as ITemplatedTerrainInfo;
				var templateInfo = tileset.Templates[templateId];
				if (templateInfo.PickAny)
					throw new ArgumentException("PickAny not supported - create separate obstacles instead.");
				for (var y = 0; y < templateInfo.Size.Y; y++)
				{
					for (var x = 0; x < templateInfo.Size.X; x++)
					{
						var i = y * templateInfo.Size.X + x;
						if (templateInfo[i] != null)
						{
							if (offset == null)
								offset = new int2(-x, -y);
							var tile = new TerrainTile(templateId, (byte)i);
							tiles.Add((new int2(x, y) + (int2)offset, tile));
						}
					}
				}

				UpdateShape();
				return this;
			}

			public Obstacle WithTile(TerrainTile tile)
			{
				tiles.Add((new int2(0, 0), tile));
				UpdateShape();
				return this;
			}

			public Obstacle WithEntity(ActorPlan entity)
			{
				entities.Add(entity);
				UpdateShape();
				return this;
			}

			public Obstacle WithBackingTile(TerrainTile tile)
			{
				if (Area == 0)
					throw new InvalidOperationException("No entities");
				foreach (var xy in shape)
				{
					tiles.Add((xy, tile));
				}

				return this;
			}

			public Obstacle WithWeight(float weight)
			{
				Weight = weight;
				return this;
			}

			public void Paint(List<ActorPlan> actorPlans, int2 paintXY, Replaceability contract)
			{
				switch (contract)
				{
					case Replaceability.None:
						throw new ArgumentException("Cannot paint: Replaceability.None");
					case Replaceability.Any:
						if (entities.Count > 0)
							PaintEntities(actorPlans, paintXY);
						else if (tiles.Count > 0)
							PaintTiles(paintXY);
						else
							throw new ArgumentException("Cannot paint: no tiles or entities");
						break;
					case Replaceability.Tile:
						if (tiles.Count == 0)
							throw new ArgumentException("Cannot paint: no tiles");
						PaintTiles(paintXY);
						PaintEntities(actorPlans, paintXY);
						break;
					case Replaceability.Entity:
						if (entities.Count == 0)
							throw new ArgumentException("Cannot paint: no entities");
						PaintEntities(actorPlans, paintXY);
						break;
				}
			}

			void PaintTiles(int2 paintXY)
			{
				foreach (var (xy, tile) in tiles)
				{
					var mpos = new MPos(paintXY.X + xy.X, paintXY.Y + xy.Y);
					if (map.Contains(mpos))
						map.Tiles[mpos] = tile;
				}
			}

			void PaintEntities(List<ActorPlan> actorPlans, int2 paintXY)
			{
				foreach (var entity in entities)
				{
					var plan = entity.Clone();
					var paintUV = new MPos(paintXY.X, paintXY.Y);
					var offset = plan.Location;
					plan.Location = paintUV.ToCPos(map) + new CVec(offset.X, offset.Y);
					actorPlans.Add(plan);
				}
			}
		}

		sealed class Region
		{
			public int Area;
			public int PlayableArea;
			public int Id;
			public bool ExternalCircle;
		}

		public IEnumerable<MapGeneratorSetting> GetDefaultSettings(Map map, ModData modData)
		{
			return ImmutableList.Create(
				new MapGeneratorSetting("#Primary", "Primary settings", new MapGeneratorSetting.SectionValue()),
				new MapGeneratorSetting("Rotations", "Rotations", new MapGeneratorSetting.IntegerValue(2)),
				new MapGeneratorSetting("Mirror", "Mirror", new MapGeneratorSetting.EnumValue(
					ImmutableList.Create(
						new KeyValuePair<int, string>((int)Symmetry.Mirror.None, "None"),
						new KeyValuePair<int, string>((int)Symmetry.Mirror.LeftMatchesRight, "Left matches right"),
						new KeyValuePair<int, string>((int)Symmetry.Mirror.TopLeftMatchesBottomRight, "Top-left matches bottom-right"),
						new KeyValuePair<int, string>((int)Symmetry.Mirror.TopMatchesBottom, "Top matches bottom"),
						new KeyValuePair<int, string>((int)Symmetry.Mirror.TopRightMatchesBottomLeft, "Top-right matches bottom-left")
					),
					(int)Symmetry.Mirror.None
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
				new MapGeneratorSetting("ResourcesPerPlayer", "Starting resource value per player", new MapGeneratorSetting.IntegerValue(50000)),
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
				new MapGeneratorSetting("WeightMiss", "Building weight: Communications Center", new MapGeneratorSetting.FloatValue(1)),
				new MapGeneratorSetting("WeightBio", "Building weight: Biological Lab", new MapGeneratorSetting.FloatValue(0)),
				new MapGeneratorSetting("WeightOilb", "Building weight: Oil Derrick", new MapGeneratorSetting.FloatValue(9))
			);
		}

		public IEnumerable<MapGeneratorSetting> GetPresetSettings(Map map, ModData modData, string preset)
		{
			var settings = GetDefaultSettings(map, modData);
			switch (preset)
			{
				case null:
					break;
				case "plains":
					settings.First(s => s.Name == "Water").Set(0.0);
					break;
				default:
					throw new ArgumentException("Invalid preset.");
			}

			return settings;
		}

		public IEnumerable<KeyValuePair<string, string>> GetPresets(Map map, ModData modData)
		{
			return ImmutableList.Create(
				new KeyValuePair<string, string>("plains", "Plains"));
		}

		public void Generate(Map map, ModData modData, MersenneTwister random, IEnumerable<MapGeneratorSetting> settingsEnumerable)
		{
			// TODO: translate exception messages?
			var settings = Enumerable.ToDictionary(settingsEnumerable, s => s.Name);
			var tileset = modData.DefaultTerrainInfo[map.Tileset] as ITemplatedTerrainInfo;
			var size = map.MapSize;
			var minSpan = Math.Min(size.X, size.Y);
			var maxSpan = Math.Max(size.X, size.Y);

			var actorPlans = new List<ActorPlan>();

			var rotations = settings["Rotations"].Get<int>();
			var mirror = (Symmetry.Mirror)settings["Mirror"].Get<int>();
			var wavelengthScale = settings["WavelengthScale"].Get<float>();
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
			var maximumExpansionSize = settings["MaximumExpansionSize"].Get<int>();
			var minimumExpansionSize = settings["MinimumExpansionSize"].Get<int>();
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

			var beachIndex = tileset.GetTerrainIndex("Beach");
			var clearIndex = tileset.GetTerrainIndex("Clear");
			var gemsIndex = tileset.GetTerrainIndex("Gems");
			var oreIndex = tileset.GetTerrainIndex("Ore");
			var riverIndex = tileset.GetTerrainIndex("River");
			var roadIndex = tileset.GetTerrainIndex("Road");
			var rockIndex = tileset.GetTerrainIndex("Rock");
			var roughIndex = tileset.GetTerrainIndex("Rough");
			var waterIndex = tileset.GetTerrainIndex("Water");

			ImmutableArray<Obstacle> forestObstacles;
			ImmutableArray<Obstacle> unplayableObstacles;
			{
				var basic = new Obstacle(map, modData).WithWeight(1.0f);
				var husk = basic.Clone().WithWeight(0.1f);
				forestObstacles = ImmutableArray.Create(
					basic.Clone().WithEntity(new ActorPlan(map, "t01").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "t02").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "t03").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "t05").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "t06").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "t07").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "t08").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "t10").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "t11").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "t12").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "t13").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "t14").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "t15").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "t16").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "t17").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "tc01").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "tc02").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "tc03").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "tc04").AlignFootprint()),
					basic.Clone().WithEntity(new ActorPlan(map, "tc05").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "t01.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "t02.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "t03.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "t05.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "t06.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "t07.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "t08.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "t10.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "t11.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "t12.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "t13.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "t14.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "t15.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "t16.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "t17.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "tc01.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "tc02.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "tc03.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "tc04.husk").AlignFootprint()),
					husk.Clone().WithEntity(new ActorPlan(map, "tc05.husk").AlignFootprint()));

				var clear = new TerrainTile(LAND_TILE, 0);
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
					basic.Clone().WithTemplate(580).WithWeight(0.1f),
					basic.Clone().WithTemplate(581).WithWeight(0.1f),
					basic.Clone().WithTemplate(582).WithWeight(0.1f),
					basic.Clone().WithTemplate(583).WithWeight(0.1f),
					basic.Clone().WithTemplate(584).WithWeight(0.1f),
					basic.Clone().WithTemplate(585).WithWeight(0.1f),
					basic.Clone().WithTemplate(586).WithWeight(0.1f),
					basic.Clone().WithTemplate(587).WithWeight(0.1f),
					basic.Clone().WithTemplate(588).WithWeight(0.1f),
					basic.Clone().WithTemplate(400).WithWeight(0.1f),
					basic.Clone().WithEntity(new ActorPlan(map, "t01").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
					basic.Clone().WithEntity(new ActorPlan(map, "t02").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
					basic.Clone().WithEntity(new ActorPlan(map, "t03").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
					basic.Clone().WithEntity(new ActorPlan(map, "t05").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
					basic.Clone().WithEntity(new ActorPlan(map, "t06").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
					basic.Clone().WithEntity(new ActorPlan(map, "t07").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
					basic.Clone().WithEntity(new ActorPlan(map, "t08").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
					basic.Clone().WithEntity(new ActorPlan(map, "t10").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
					basic.Clone().WithEntity(new ActorPlan(map, "t11").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
					basic.Clone().WithEntity(new ActorPlan(map, "t12").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
					basic.Clone().WithEntity(new ActorPlan(map, "t13").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
					basic.Clone().WithEntity(new ActorPlan(map, "t14").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
					basic.Clone().WithEntity(new ActorPlan(map, "t15").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
					basic.Clone().WithEntity(new ActorPlan(map, "t16").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f),
					basic.Clone().WithEntity(new ActorPlan(map, "t17").AlignFootprint()).WithBackingTile(clear).WithWeight(0.1f));
			}

			var replaceabilityMap = new Dictionary<TerrainTile, Replaceability>();
			var playabilityMap = new Dictionary<TerrainTile, Playability>();
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
						playabilityMap[tile] = Playability.Playable;
					}
					else
					{
						playabilityMap[tile] = Playability.Unplayable;
					}

					if (id == WATER_TILE)
					{
						replaceabilityMap[tile] = Replaceability.Tile;
					}
					else if (template.Categories.Contains("Cliffs"))
					{
						if (type == rockIndex)
							replaceabilityMap[tile] = Replaceability.None;
						else
							replaceabilityMap[tile] = Replaceability.Entity;
					}
					else if (template.Categories.Contains("Beach") || template.Categories.Contains("Road"))
					{
						replaceabilityMap[tile] = Replaceability.Tile;
						if (playabilityMap[tile] == Playability.Unplayable)
							playabilityMap[tile] = Playability.Partial;
					}
				}
			}

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

			if (water < 0.0f || water > 1.0f)
				throw new MapGenerationException("water setting must be between 0 and 1 inclusive");

			if (forests < 0.0f || forests > 1.0f)
				throw new MapGenerationException("forest setting must be between 0 and 1 inclusive");

			if (forestClumpiness < 0.0f)
				throw new MapGenerationException("forestClumpiness setting must be >= 0");
			if (mountains < 0.0 || mountains > 1.0)
				throw new MapGenerationException("mountains fraction must be between 0 and 1 inclusive");
			if (water + mountains > 1.0)
				throw new MapGenerationException("water and mountains fractions combined must not exceed 1");

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
				map.Tiles[mpos] = PickTile(LAND_TILE);
				map.Resources[mpos] = new ResourceTile(0, 0);
				map.Height[mpos] = 0;
			}

			Log.Write("debug", "elevation: generating noise");
			var elevation = FractalNoise2dWithSymmetry(
				waterRandom,
				size,
				rotations,
				mirror,
				wavelengthScale,
				PinkAmplitudeFunction);

			if (terrainSmoothing > 0)
			{
				Log.Write("debug", "elevation: applying gaussian blur");
				var radius = terrainSmoothing;
				elevation = MatrixUtils.GaussianBlur(elevation, radius, radius);
			}

			CalibrateHeightInPlace(
				elevation,
				0.0f,
				water);
			var externalCircleCenter = (size.ToFloat2() - new float2(0.5f, 0.5f)) / 2.0f;
			if (externalCircularBias != 0)
			{
				elevation.DrawCircle(
					center: externalCircleCenter,
					radius: minSpan / 2.0f - (minimumLandSeaThickness + minimumMountainThickness),
					setTo: (_, _) => externalCircularBias * EXTERNAL_BIAS,
					invert: true);
			}

			Log.Write("debug", "land planning: producing terrain");
			var landPlan = ProduceTerrain(elevation, terrainSmoothing, smoothingThreshold, minimumLandSeaThickness, /*bias=*/water < 0.5, "land planning");

			Log.Write("debug", "beaches");
			var beaches = BordersToPoints(landPlan);
			if (beaches.Length > 0)
			{
				var beachPermittedTemplates = new PermittedTemplates(PermittedTemplates.FindTemplates(tileset, new[] { "Beach" }));
				var tiledBeaches = new int2[beaches.Length][];
				for (var i = 0; i < beaches.Length; i++)
				{
					var tweakedPoints = TweakPathPoints(beaches[i], size);
					var beachPath = new Path(tweakedPoints, "Beach", "Beach", beachPermittedTemplates);
					tiledBeaches[i] = TilePath(map, beachPath, beachTilingRandom, minimumLandSeaThickness);
				}

				Log.Write("debug", "filling water");
				var beachChirality = PointsChirality(size, tiledBeaches);
				foreach (var cell in map.AllCells)
				{
					var mpos = cell.ToMPos(map);
					var point = new int2(mpos.U, mpos.V);

					// `map.Tiles[mpos].Index == LAND_TILE` avoids overwriting beach tiles.
					if (beachChirality[mpos.U, mpos.V] < 0 && map.Tiles[mpos].Type == LAND_TILE)
						map.Tiles[mpos] = PickTile(WATER_TILE);
				}
			}
			else
			{
				// There weren't any coastlines
				var tileType = landPlan[0] ? LAND_TILE : WATER_TILE;
				foreach (var cell in map.AllCells)
				{
					var mpos = cell.ToMPos(map);
					map.Tiles[mpos] = PickTile(tileType);
				}
			}

			var nonLoopedCliffPermittedTemplates = new PermittedTemplates(
				PermittedTemplates.FindTemplates(tileset, new[] { "Clear" }, new[] { "Cliff" }),
				PermittedTemplates.FindTemplates(tileset, new[] { "Cliff" }),
				PermittedTemplates.FindTemplates(tileset, new[] { "Cliff" }, new[] { "Clear" }));
			var loopedCliffPermittedTemplates = new PermittedTemplates(
				PermittedTemplates.FindTemplates(tileset, new[] { "Cliff" }));
			if (externalCircularBias > 0)
			{
				Log.Write("debug", "creating circular cliff map border");
				var cliffRing = new Matrix<bool>(size);
				cliffRing.DrawCircle(
					center: externalCircleCenter,
					radius: minSpan / 2.0f - minimumLandSeaThickness,
					setTo: (_, _) => true,
					invert: true);
				var cliffs = BordersToPoints(cliffRing);
				foreach (var cliff in cliffs)
				{
					var tweakedPoints = TweakPathPoints(cliff, size);
					var isLoop = tweakedPoints[0] == tweakedPoints[^1];
					Path cliffPath;
					if (isLoop)
						cliffPath = new Path(tweakedPoints, "Cliff", "Cliff", loopedCliffPermittedTemplates);
					else
						cliffPath = new Path(tweakedPoints, "Clear", "Clear", nonLoopedCliffPermittedTemplates);
					TilePath(map, cliffPath, cliffTilingRandom, minimumMountainThickness);
				}
			}

			if (mountains > 0.0f || externalCircularBias == 1)
			{
				Log.Write("debug", "mountains: calculating elevation roughness");
				var roughnessMatrix = MatrixUtils.GridVariance(elevation, roughnessRadius).Map(v => MathF.Sqrt(v));
				CalibrateHeightInPlace(
					roughnessMatrix,
					0.0f,
					1.0f - roughness);
				var cliffMask = roughnessMatrix.Map(v => v >= 0.0f);
				var mountainElevation = elevation.Clone();
				var cliffPlan = landPlan;
				if (externalCircularBias > 0)
				{
					cliffPlan.DrawCircle(
						center: externalCircleCenter,
						radius: minSpan / 2.0f - (minimumLandSeaThickness + minimumMountainThickness),
						setTo: (_, _) => false,
						invert: true);
				}

				for (var altitude = 1; altitude <= maximumAltitude; altitude++)
				{
					Log.Write("debug", $"mountains: altitude {altitude}: determining eligible area for cliffs");

					// Limit mountain area to the existing mountain space (starting with all available land)
					var roominess = CalculateRoominess(cliffPlan, true);
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
					CalibrateHeightInPlace(
						mountainElevation,
						0.0f,
						1.0f - availableFraction * mountains);
					Log.Write("debug", $"mountains: altitude {altitude}: fixing terrain anomalies");
					cliffPlan = ProduceTerrain(mountainElevation, terrainSmoothing, smoothingThreshold, minimumMountainThickness, false, $"mountains: altitude {altitude}");
					Log.Write("debug", $"mountains: altitude {altitude}: tracing cliffs");
					var unmaskedCliffs = BordersToPoints(cliffPlan);
					Log.Write("debug", $"mountains: altitude {altitude}: appling roughness mask to cliffs");
					var maskedCliffs = MaskPoints(unmaskedCliffs, cliffMask);
					var cliffs = maskedCliffs.Where(cliff => cliff.Length >= minimumCliffLength).ToArray();
					if (cliffs.Length == 0)
						break;
					Log.Write("debug", $"mountains: altitude {altitude}: fitting and laying tiles");
					foreach (var cliff in cliffs)
					{
						var tweakedPoints = TweakPathPoints(cliff, size);
						var isLoop = tweakedPoints[0] == tweakedPoints[^1];
						Path cliffPath;
						if (isLoop)
							cliffPath = new Path(tweakedPoints, "Cliff", "Cliff", loopedCliffPermittedTemplates);
						else
							cliffPath = new Path(tweakedPoints, "Clear", "Clear", nonLoopedCliffPermittedTemplates);
						TilePath(map, cliffPath, cliffTilingRandom, minimumMountainThickness);
					}
				}
			}

			if (forests > 0.0f)
			{
				Log.Write("debug", "forests: generating noise");
				var forestNoise = FractalNoise2dWithSymmetry(
					forestRandom,
					size,
					rotations,
					mirror,
					wavelengthScale,
					wavelength => MathF.Pow(wavelength, forestClumpiness));
				CalibrateHeightInPlace(
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
								=> newSpace[destination] = sources.All(source => space[source]));
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

				var forestReplace = Matrix<Replaceability>.Zip(
					forestPlan,
					IdentifyReplaceableTiles(map, tileset, replaceabilityMap),
					(a, b) => a ? b : Replaceability.None);
				ObstructArea(map, actorPlans, forestReplace, forestObstacles, forestTilingRandom);
			}

			if (enforceSymmetry != 0)
			{
				Log.Write("debug", "symmatry enforcement: analysing");
				if (!trivialRotate)
					throw new MapGenerationException("cannot use symmetry enforcement on non-trivial rotations");
				bool CheckCompatibility(byte main, byte other)
				{
					if (main == other)
						return true;
					if (main == riverIndex || main == rockIndex || main == waterIndex)
						return true;
					else if (main == beachIndex || main == clearIndex || main == roughIndex)
					{
						if (other == riverIndex || other == rockIndex || other == waterIndex)
							return false;
						if (other == beachIndex || other == clearIndex || other == roughIndex)
							return enforceSymmetry < 2;
						else
							throw new MapGenerationException("ambiguous symmetry policy");
					}
					else
						throw new MapGenerationException("ambiguous symmetry policy");
				}

				var replace = new Matrix<Replaceability>(size);
				Symmetry.RotateAndMirrorOverGridSquares(size, rotations, mirror,
					(int2[] sources, int2 destination) =>
					{
						var main = tileset.GetTerrainIndex(map.Tiles[new MPos(destination.X, destination.Y)]);
						var compatible = sources
							.Where(replace.ContainsXY)
							.Select(source => tileset.GetTerrainIndex(map.Tiles[new MPos(source.X, source.Y)]))
							.All(source => CheckCompatibility(main, source));
						replace[destination] = compatible ? Replaceability.None : Replaceability.Entity;
					});
				Log.Write("debug", "symmatry enforcement: obstructing");
				ObstructArea(map, actorPlans, replace, forestObstacles, random);
			}

			var playableArea = new Matrix<bool>(size);
			{
				Log.Write("debug", "determining playable regions");
				var (regionMask, regions, playability) = FindPlayableRegions(map, actorPlans, playabilityMap);
				Region largest = null;
				foreach (var region in regions)
				{
					if (externalCircularBias > 0 && region.ExternalCircle)
						continue;
					if (largest == null || region.PlayableArea > largest.PlayableArea)
						largest = region;
				}

				if (largest == null)
					throw new MapGenerationException("could not find a playable region");
				if (denyWalledAreas)
				{
					Log.Write("debug", "obstructing semi-unreachable areas");

					var replace = Matrix<Replaceability>.Zip(
						regionMask,
						IdentifyReplaceableTiles(map, tileset, replaceabilityMap),
						(a, b) => a == largest.Id ? Replaceability.None : b);
					ObstructArea(map, actorPlans, replace, unplayableObstacles, random);
				}

				for (var n = 0; n < playableArea.Data.Length; n++)
				{
					playableArea[n] = playability[n] == Playability.Playable && regionMask[n] == largest.Id;
				}
			}

			if (roads)
			{
				// TODO: merge with roads
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
							=> newSpace[destination] = sources.All(source => space[source]));
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
				var noJunctions = RemoveJunctionsFromDirectionMap(deflated);
				var pointArrays = DeduplicateAndNormalizePointArrays(DirectionMapToPointArrays(noJunctions), size);

				var roadPermittedTemplates = new PermittedTemplates(
					PermittedTemplates.FindTemplates(tileset, new[] { "Clear" }, new[] { "Road", "RoadIn", "RoadOut" }),
					PermittedTemplates.FindTemplates(tileset, new[] { "Road", "RoadIn", "RoadOut" }),
					PermittedTemplates.FindTemplates(tileset, new[] { "Road", "RoadIn", "RoadOut" }, new[] { "Clear" }));

				foreach (var pointArray in pointArrays)
				{
					var shrunk = ShrinkPointArray(pointArray, 4, 12);
					if (shrunk == null)
						continue;
					var extended = InertiallyExtendPathInPlace(shrunk, 2, 8);
					var tweaked = TweakPathPoints(extended, size);

					// Roads that are _almost_ vertical or horizontal tile badly.
					// Filter them out.
					var minX = tweaked.Min((p) => p.X);
					var minY = tweaked.Min((p) => p.Y);
					var maxX = tweaked.Max((p) => p.X);
					var maxY = tweaked.Max((p) => p.Y);
					if (maxX - minX < 6 || maxY - minY < 6)
						continue;

					// Currently, never looped.
					var path = new Path(tweaked, "Clear", "Clear", roadPermittedTemplates);

					TilePath(map, path, roadTilingRandom, roadSpacing * 2);
				}
			}

			if (createEntities)
			{
				Log.Write("debug", "entities: determining eligible space");

				// TODO: remove map cordon from zoneable.
				var zoneable = new Matrix<bool>(size);
				for (var y = 0; y < size.Y; y++)
				{
					for (var x = 0; x < size.X; x++)
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
							=> newZoneable[destination] = sources.All(source => zoneable[source]));
					zoneable = newZoneable;
				}
				else
				{
					// Non 1, 2, 4 rotations need entity placement confined to a circle, regardless of externalCircularBias
					zoneable.DrawCircle(
						center: externalCircleCenter,
						radius: minSpan / 2.0f - 1.0f,
						setTo: (_, _) => false,
						invert: true);
				}

				if (rotations > 1 || mirror != 0)
				{
					// Reserve the center of the map - otherwise it will mess with rotations
					// TODO: Change externalCircleCenter to mapCenter
					zoneable.DrawCircle(
						center: externalCircleCenter,
						radius: 1.0f,
						setTo: (_, _) => false,
						invert: false);
				}

				// Spawn generation
				Log.Write("debug", "entities: zoning for spawns");
				for (var iteration = 0; iteration < players; iteration++)
				{
					var roominess = CalculateRoominess(zoneable, false)
						.Foreach((v) => Math.Min(v, spawnRegionSize));
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
					var templatePlayer = new ActorPlan(map, "mpspawn")
					{
						ZoningRadius = spawnReservation,
						Int2Location = chosenXY,
					};
					var spawnActorPlans = new List<ActorPlan>
					{
						templatePlayer
					};

					var radius2 = MathF.Min(spawnRegionSize, room);
					var radius1 = MathF.Min(MathF.Min(spawnBuildSize, room), radius2 - 2);
					if (radius1 >= 2.0f)
					{
						var mineWeights = new Matrix<float>(size);
						var radius1Sq = radius1 * radius1;
						mineWeights.DrawCircle(
							center: chosenXY,
							radius: radius2,
							setTo: (rSq, _) => rSq >= radius1Sq ? (1.0f * rSq) : 0.0f,
							invert: false);
						for (var mine = 0; mine < spawnMines; mine++)
						{
							var xy = mineWeights.XY(playerRandom.PickWeighted(mineWeights.Data));
							var minePlan =
								playerRandom.NextFloat() < gemUpgrade
									? new ActorPlan(map, "gmine")
									: new ActorPlan(map, "mine");
							minePlan.ZoningRadius = mineReservation;
							minePlan.Int2Location = xy;
							spawnActorPlans.Add(minePlan);
							mineWeights.DrawCircle(
								center: minePlan.Int2Location,
								radius: 1.0f,
								setTo: (_, _) => 0.0f,
								invert: false);
						}
					}

					Symmetry.RotateAndMirrorActorPlans(actorPlans, spawnActorPlans, rotations, mirror);
					MatrixUtils.ReserveForEntitiesInPlace(zoneable, actorPlans, (_) => false);
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
								center: externalCircleCenter,
								radius: minSpan * centralExpansionReservationFraction,
								setTo: (_, _) => false,
								invert: false);
						}

						var expansionRoominess = CalculateRoominess(expansionZoneable, false)
							.Foreach((v) => Math.Min(v, maximumExpansionSize + expansionBorder));
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

						var expansionActorPlans = new List<ActorPlan>();
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
							minePlan.ZoningRadius = mineReservation;
							minePlan.Int2Location = xy;
							expansionActorPlans.Add(minePlan);
							mineWeights.DrawCircle(
								center: minePlan.Int2Location,
								radius: 1.0f,
								setTo: (_, _) => 0.0f,
								invert: false);
						}

						Symmetry.RotateAndMirrorActorPlans(actorPlans, expansionActorPlans, rotations, mirror);
						MatrixUtils.ReserveForEntitiesInPlace(zoneable, actorPlans, (_) => false);
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
						var roominess = CalculateRoominess(zoneable, false)
							.Foreach((v) => Math.Min(v, 3));
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
							ZoningRadius = 2.0f,
							CenterLocation = new float2(chosenXY.X + 0.5f, chosenXY.Y + 0.5f),
						};

						Symmetry.RotateAndMirrorActorPlan(actorPlans, actorPlan, rotations, mirror);
						MatrixUtils.ReserveForEntitiesInPlace(zoneable, actorPlans, (_) => false);
					}
				}

				// Grow resources
				{
					Log.Write("debug", "ore: generating noise");
					var orePattern = FractalNoise2dWithSymmetry(
						resourceRandom,
						size,
						rotations,
						mirror,
						wavelengthScale,
						wavelength => MathF.Pow(wavelength, oreClumpiness));
					{
						CalibrateHeightInPlace(
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

					foreach (var actorPlan in actorPlans)
					{
						if (actorPlan.Reference.Type == "mpspawn")
							orePlan.DrawCircle(
								center: actorPlan.Int2Location,
								radius: 32,
								setTo: (rSq, v) => v * (1.0f + spawnResourceBias / rSq),
								invert: false);
					}

					foreach (var actorPlan in actorPlans)
					{
						if (actorPlan.Reference.Type == "mpspawn")
							orePlan.DrawCircle(
								center: actorPlan.Int2Location,
								radius: 3,
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
								=> orePlan[destination] = sources.Min(source => orePlan[source]));
					}

					var remaining = resourcesPerPlayer * players * Symmetry.RotateAndMirrorProjectionCount(rotations, mirror);
					var priorities = new PriorityArray<float>(orePlan.Data.Length, float.PositiveInfinity);
					for (var n = 0; n < orePlan.Data.Length; n++)
					{
						priorities[n] = -orePlan[n];
					}

					// TODO: Reuse EditorResourceLayer logic.
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

		static Matrix<float> FractalNoise2dWithSymmetry(MersenneTwister random, int2 size, int rotations, Symmetry.Mirror mirror, float wavelengthScale, AmplitudeFunction ampFunc)
		{
			if (rotations < 1)
			{
				throw new ArgumentException("rotations must be >= 1");
			}

			// Need higher resolution due to cropping and rotation artifacts
			var templateSpan = Math.Max(size.X, size.Y) * 2 + 2;
			var templateSize = new int2(templateSpan, templateSpan);
			var template = FractalNoise2d(random, templateSize, wavelengthScale, ampFunc);
			var unmirrored = new Matrix<float>(size);

			// This -1 is required to compensate for the top-left vs the center of a grid square.
			var offset = new float2((size.X - 1) / 2.0f, (size.Y - 1) / 2.0f);
			var templateOffset = new float2(templateSpan / 2.0f, templateSpan / 2.0f);
			for (var rotation = 0; rotation < rotations; rotation++)
			{
				var angle = rotation * MathF.Tau / rotations;
				var cosAngle = Symmetry.CosSnapF(angle);
				var sinAngle = Symmetry.SinSnapF(angle);
				for (var y = 0; y < size.Y; y++)
				{
					for (var x = 0; x < size.X; x++)
					{
						var xy = new float2(x, y);

						// xy # corner noise space
						// xy - offset # middle noise space
						// (xy - offset) * SQRT2 # middle temp space
						// R * ((xy - offset) * SQRT2) # middle temp space rotate
						// R * ((xy - offset) * SQRT2) + to # corner temp space rotate
						var midt = (xy - offset) * (float)SQRT2;
						var tx = (midt.X * cosAngle - midt.Y * sinAngle) + templateOffset.X;
						var ty = (midt.X * sinAngle + midt.Y * cosAngle) + templateOffset.Y;
						unmirrored[x, y] +=
							Interpolate2d(
								template,
								tx,
								ty) / rotations;
					}
				}
			}

			if (mirror == Symmetry.Mirror.None)
			{
				return unmirrored;
			}

			var mirrored = new Matrix<float>(size);
			for (var y = 0; y < size.Y; y++)
			{
				for (var x = 0; x < size.X; x++)
				{
					var txy = Symmetry.MirrorGridSquare(mirror, new int2(x, y), size);
					mirrored[x, y] = unmirrored[x, y] + unmirrored[txy];
				}
			}

			return mirrored;
		}

		delegate float AmplitudeFunction(float wavelength);
		static float PinkAmplitudeFunction(float wavelength) => wavelength;
		static Matrix<float> FractalNoise2d(MersenneTwister random, int2 size, float wavelengthScale, AmplitudeFunction ampFunc)
		{
			var span = Math.Max(size.X, size.Y);
			var wavelengths = new float[(int)Math.Log2(span)];
			for (var i = 0; i < wavelengths.Length; i++)
			{
				wavelengths[i] = (1 << i) * wavelengthScale;
			}

			// float AmpFunc(float wavelength) => wavelength / span / wavelengths.Length;
			var noise = new Matrix<float>(size);
			foreach (var wavelength in wavelengths)
			{
				var amps = ampFunc(wavelength);
				var subSpan = (int)(span / wavelength) + 2;
				var subNoise = PerlinNoise2d(random, subSpan);

				// Offsets should align to grid.
				// (The wavelength is divided back out later.)
				var offsetX = (int)(random.NextFloat() * wavelength);
				var offsetY = (int)(random.NextFloat() * wavelength);
				for (var y = 0; y < size.Y; y++)
				{
					for (var x = 0; x < size.X; x++)
					{
						noise[y * size.X + x] +=
							amps * Interpolate2d(
								subNoise,
								(offsetX + x) / wavelength,
								(offsetY + y) / wavelength);
					}
				}
			}

			return noise;
		}

		static Matrix<float> PerlinNoise2d(MersenneTwister random, int span)
		{
			var noise = new Matrix<float>(span, span);
			const float D = 0.25f;
			for (var y = 0; y <= span; y++)
			{
				for (var x = 0; x <= span; x++)
				{
					var phase = MathF.Tau * random.NextFloatExclusive();
					var vx = MathF.Cos(phase);
					var vy = MathF.Sin(phase);
					if (x > 0 && y > 0)
						noise[x - 1, y - 1] += vx * -D + vy * -D;
					if (x < span && y > 0)
						noise[x    , y - 1] += vx *  D + vy * -D;
					if (x > 0 && y < span)
						noise[x - 1, y    ] += vx * -D + vy *  D;
					if (x < span && y < span)
						noise[x    , y    ] += vx *  D + vy *  D;
				}
			}

			return noise;
		}

		static float Interpolate2d(Matrix<float> matrix, float x, float y)
		{
			var xa = (int)MathF.Floor(x);
			var xb = (int)MathF.Ceiling(x);
			var ya = (int)MathF.Floor(y);
			var yb = (int)MathF.Ceiling(y);

			// "w" for "weight"
			var xbw = x - xa;
			var ybw = y - ya;
			var xaw = 1.0f - xbw;
			var yaw = 1.0f - ybw;

			if (xa < 0)
			{
				xa = 0;
				xb = 0;
			}
			else if (xb > matrix.Size.X - 1)
			{
				xa = matrix.Size.X - 1;
				xb = matrix.Size.X - 1;
			}

			if (ya < 0)
			{
				ya = 0;
				yb = 0;
			}
			else if (yb > matrix.Size.Y - 1)
			{
				ya = matrix.Size.Y - 1;
				yb = matrix.Size.Y - 1;
			}

			var naa = matrix[xa, ya];
			var nba = matrix[xb, ya];
			var nab = matrix[xa, yb];
			var nbb = matrix[xb, yb];
			return (naa * xaw + nba * xbw) * yaw + (nab * xaw + nbb * xbw) * ybw;
		}

		static void CalibrateHeightInPlace(Matrix<float> matrix, float target, float fraction)
		{
			var sorted = (float[])matrix.Data.Clone();
			Array.Sort(sorted);
			var adjustment = target - ArrayQuantile(sorted, fraction);
			for (var i = 0; i < matrix.Data.Length; i++)
			{
				matrix[i] += adjustment;
			}
		}

		static float ArrayQuantile(float[] array, float quantile)
		{
			if (array.Length == 0)
			{
				throw new ArgumentException("Cannot get quantile of empty array");
			}

			var iFloat = quantile * (array.Length - 1);
			if (iFloat < 0)
			{
				iFloat = 0;
			}

			if (iFloat > array.Length - 1)
			{
				iFloat = array.Length - 1;
			}

			var iLow = (int)iFloat;
			if (iLow == iFloat)
			{
				return array[iLow];
			}

			var iHigh = iLow + 1;
			var weight = iFloat - iLow;
			return array[iLow] * (1 - weight) + array[iHigh] * weight;
		}

		static Matrix<bool> ProduceTerrain(Matrix<float> elevation, int terrainSmoothing, float smoothingThreshold, int minimumThickness, bool bias, string debugLabel)
		{
			Log.Write("debug", $"{debugLabel}: fixing terrain anomalies: primary median blur");
			var maxSpan = Math.Max(elevation.Size.X, elevation.Size.Y);
			var landmass = elevation.Map(v => v >= 0);

			(landmass, _) = MatrixUtils.BooleanBlur(landmass, terrainSmoothing, true, 0.0f);
			for (var i1 = 0; i1 < /*max passes*/16; i1++)
			{
				for (var i2 = 0; i2 < maxSpan; i2++)
				{
					int changes;
					var changesAcc = 0;
					for (var r = 1; r <= terrainSmoothing; r++)
					{
						(landmass, changes) = MatrixUtils.BooleanBlur(landmass, r, true, smoothingThreshold);
						changesAcc += changes;
					}

					if (changesAcc == 0)
					{
						break;
					}
				}

				{
					var changesAcc = 0;
					int changes;
					int thinnest;
					(landmass, changes) = MatrixUtils.ErodeAndDilate(landmass, true, minimumThickness);
					changesAcc += changes;
					(thinnest, changes) = FixThinMassesInPlaceFull(landmass, true, minimumThickness);
					changesAcc += changes;

					var midFixLandmass = landmass.Clone();

					(landmass, changes) = MatrixUtils.ErodeAndDilate(landmass, false, minimumThickness);
					changesAcc += changes;
					(thinnest, changes) = FixThinMassesInPlaceFull(landmass, false, minimumThickness);
					changesAcc += changes;
					if (changesAcc == 0)
					{
						break;
					}

					if (i1 >= 8 && i1 % 4 == 0)
					{
						var diff = Matrix<bool>.Zip(midFixLandmass, landmass, (a, b) => a != b);
						for (var y = 0; y < elevation.Size.Y; y++)
						{
							for (var x = 0; x < elevation.Size.X; x++)
							{
								if (diff[x, y])
									landmass.DrawCircle(
										center: new float2(x, y),
										radius: minimumThickness * 2,
										setTo: (_, _) => bias,
										invert: false);
							}
						}
					}
				}
			}

			return landmass;
		}

		static (int Thinnest, int Changes) FixThinMassesInPlaceFull(Matrix<bool> input, bool dilate, int width)
		{
			int thinnest;
			int changes;
			int changesAcc;
			(thinnest, changes) = FixThinMassesInPlace(input, dilate, width);
			changesAcc = changes;
			while (changes > 0)
			{
				(_, changes) = FixThinMassesInPlace(input, dilate, width);
				changesAcc += changes;
			}

			return (thinnest, changesAcc);
		}

		static (int Thinnest, int Changes) FixThinMassesInPlace(Matrix<bool> input, bool dilate, int width)
		{
			var sizeMinus1 = input.Size - new int2(1, 1);
			var cornerMaskSpan = width + 1;

			// Zero means ignore.
			var cornerMask = new Matrix<int>(cornerMaskSpan, cornerMaskSpan);

			for (var y = 0; y < cornerMaskSpan; y++)
			{
				for (var x = 0; x < cornerMaskSpan; x++)
				{
					cornerMask[x, y] = 1 + width + width - x - y;
				}
			}

			cornerMask[0] = 0;

			// Higher number indicates a thinner area.
			var thinness = new Matrix<int>(input.Size);
			void SetThinness(int x, int y, int v)
			{
				if (!input.ContainsXY(x, y)) return;
				if (input[x, y] == dilate) return;
				thinness[x, y] = Math.Max(v, thinness[x, y]);
			}

			for (var cy = 0; cy < input.Size.Y; cy++)
			{
				for (var cx = 0; cx < input.Size.X; cx++)
				{
					if (input[cx, cy] == dilate)
						continue;

					// _L_eft _R_ight _U_p _D_own
					var l = input[Math.Max(cx - 1, 0), cy] == dilate;
					var r = input[Math.Min(cx + 1, sizeMinus1.X), cy] == dilate;
					var u = input[cx, Math.Max(cy - 1, 0)] == dilate;
					var d = input[cx, Math.Min(cy + 1, sizeMinus1.Y)] == dilate;
					var lu = l && u;
					var ru = r && u;
					var ld = l && d;
					var rd = r && d;
					for (var ry = 0; ry < cornerMaskSpan; ry++)
					{
						for (var rx = 0; rx < cornerMaskSpan; rx++)
						{
							if (rd)
							{
								var x = cx + rx;
								var y = cy + ry;
								SetThinness(x, y, cornerMask[rx, ry]);
							}

							if (ru)
							{
								var x = cx + rx;
								var y = cy - ry;
								SetThinness(x, y, cornerMask[rx, ry]);
							}

							if (ld)
							{
								var x = cx - rx;
								var y = cy + ry;
								SetThinness(x, y, cornerMask[rx, ry]);
							}

							if (lu)
							{
								var x = cx - rx;
								var y = cy - ry;
								SetThinness(x, y, cornerMask[rx, ry]);
							}
						}
					}
				}
			}

			var thinnest = thinness.Data.Max();
			if (thinnest == 0)
			{
				// No fixes
				return (0, 0);
			}

			var changes = 0;
			for (var y = 0; y < input.Size.Y; y++)
			{
				for (var x = 0; x < input.Size.X; x++)
				{
					if (thinness[x, y] == thinnest)
					{
						input[x, y] = dilate;
						changes++;
					}
				}
			}

			// Fixes made, with potentially more that can be in another pass.
			return (thinnest, changes);
		}

		static int2[][] BordersToPoints(Matrix<bool> matrix)
		{
			// There is redundant memory/iteration, but I don't care enough.

			// These are really only the signs of the gradients.
			var gradientH = new Matrix<sbyte>(matrix.Size);
			var gradientV = new Matrix<sbyte>(matrix.Size);
			for (var y = 0; y < matrix.Size.Y; y++)
			{
				for (var x = 1; x < matrix.Size.X; x++)
				{
					var l = matrix[x - 1, y] ? 1 : 0;
					var r = matrix[x, y] ? 1 : 0;
					gradientV[x, y] = (sbyte)(r - l);
				}
			}

			for (var y = 1; y < matrix.Size.Y; y++)
			{
				for (var x = 0; x < matrix.Size.X; x++)
				{
					var u = matrix[x, y - 1] ? 1 : 0;
					var d = matrix[x, y] ? 1 : 0;
					gradientH[x, y] = (sbyte)(d - u);
				}
			}

			// Looping paths contain the start/end point twice.
			var paths = new List<int2[]>();
			void TracePath(int sx, int sy, int direction)
			{
				var points = new List<int2>();
				var x = sx;
				var y = sy;
				points.Add(new int2(x, y));
				do
				{
					switch (direction)
					{
						case Direction.R:
							gradientH[x, y] = 0;
							x++;
							break;
						case Direction.D:
							gradientV[x, y] = 0;
							y++;
							break;
						case Direction.L:
							x--;
							gradientH[x, y] = 0;
							break;
						case Direction.U:
							y--;
							gradientV[x, y] = 0;
							break;
						default:
							throw new ArgumentException("direction assertion failed");
					}

					points.Add(new int2(x, y));
					var r = gradientH.ContainsXY(x, y) && gradientH[x, y] > 0;
					var d = gradientV.ContainsXY(x, y) && gradientV[x, y] < 0;
					var l = gradientH.ContainsXY(x - 1, y) && gradientH[x - 1, y] < 0;
					var u = gradientV.ContainsXY(x, y - 1) && gradientV[x, y - 1] > 0;
					if (direction == Direction.R && u)
						direction = Direction.U;
					else if (direction == Direction.D && r)
						direction = Direction.R;
					else if (direction == Direction.L && d)
						direction = Direction.D;
					else if (direction == Direction.U && l)
						direction = Direction.L;
					else if (r)
						direction = Direction.R;
					else if (d)
						direction = Direction.D;
					else if (l)
						direction = Direction.L;
					else if (u)
						direction = Direction.U;
					else
						break; // Dead end (not a loop)
				}
				while (x != sx || y != sy);

				paths.Add(points.ToArray());
			}

			// Trace non-loops (from edge of map)
			for (var x = 1; x < matrix.Size.X; x++)
			{
				if (gradientV[x, 0] < 0)
					TracePath(x, 0, Direction.D);
				if (gradientV[x, matrix.Size.Y - 1] > 0)
					TracePath(x, matrix.Size.Y, Direction.U);
			}

			for (var y = 1; y < matrix.Size.Y; y++)
			{
				if (gradientH[0, y] > 0)
					TracePath(0, y, Direction.R);
				if (gradientH[matrix.Size.X - 1, y] < 0)
					TracePath(matrix.Size.X, y, Direction.L);
			}

			// Trace loops
			for (var y = 0; y < matrix.Size.Y; y++)
			{
				for (var x = 0; x < matrix.Size.X; x++)
				{
					if (gradientH[x, y] > 0)
						TracePath(x, y, Direction.R);
					else if (gradientH[x, y] < 0)
						TracePath(x + 1, y, Direction.L);

					if (gradientV[x, y] < 0)
						TracePath(x, y, Direction.D);
					else if (gradientV[x, y] > 0)
						TracePath(x, y + 1, Direction.U);
				}
			}

			return paths.ToArray();
		}

		// <summary>
		// Modifies a points (for a path) to be easier to tile.
		// For loops, the points are made to start and end within the longest straight.
		// For map edge-connected paths, the path is extended beyond the edge.
		static int2[] TweakPathPoints(int2[] points, int2 size)
		{
			var len = points.Length;
			var last = len - 1;
			if (points[0].X == points[last].X && points[0].Y == points[last].Y)
			{
				// Closed loop. Find the longest straight
				// (nrlen excludes the repeated point at the end.)
				var nrlen = len - 1;
				var prevDim = -1;
				var scanStart = -1;
				var bestScore = -1;
				var bestBend = -1;
				var prevBend = -1;
				var prevI = 0;
				for (var i = 1; ; i++)
				{
					if (i == nrlen)
						i = 0;
					var dim = points[i].X == points[prevI].X ? 1 : 0;
					if (prevDim != -1 && prevDim != dim)
					{
						if (scanStart == -1)
						{
							// This is technically just after the bend. But that's fine.
							scanStart = i;
						}
						else
						{
							var score = prevI - prevBend;
							if (score < 0)
								score += nrlen;

							if (score > bestScore)
							{
								bestBend = prevBend;
								bestScore = score;
							}

							if (i == scanStart)
							{
								break;
							}
						}

						prevBend = prevI;
					}

					prevDim = dim;
					prevI = i;
				}

				var favouritePoint = (bestBend + (bestScore >> 1)) % nrlen;

				// Repeat the start at the end.
				// [...points.slice(favouritePoint, nrlen), ...points.slice(0, favouritePoint + 1)];
				var tweaked = new int2[points.Length];
				Array.Copy(points, favouritePoint, tweaked, 0, nrlen - favouritePoint);
				Array.Copy(points, 0, tweaked, nrlen - favouritePoint, favouritePoint + 1);
				return tweaked;
			}
			else
			{
				// Not a loop.
				int2[] Extend(int2 point, int extensionLength)
				{
					var ox = (point.X == 0) ? -1
						: (point.X == size.X) ? 1
						: 0;
					var oy = (point.Y == 0) ? -1
						: (point.Y == size.Y) ? 1
						: 0;
					if (ox == 0 && oy == 0)
						return Array.Empty<int2>(); // We're not on an edge, so don't extend.
					var offset = new int2(ox, oy);

					var extension = new int2[extensionLength];
					var newPoint = point;
					for (var i = 0; i < extensionLength; i++)
					{
						newPoint += offset;
						extension[i] = newPoint;
					}

					return extension;
				}

				// Open paths. Extend if beyond edges.
				var startExt = Extend(points[0], /*extensionLength=*/4).Reverse().ToArray();
				var endExt = Extend(points[last], /*extensionLength=*/4);

				// [...startExt, ...points, ...endExt];
				var tweaked = new int2[points.Length + startExt.Length + endExt.Length];
				Array.Copy(startExt, 0, tweaked, 0, startExt.Length);
				Array.Copy(points, 0, tweaked, startExt.Length, points.Length);
				Array.Copy(endExt, 0, tweaked, points.Length + startExt.Length, endExt.Length);
				return tweaked;
			}
		}

		class TilePathSegment
		{
			public readonly TerrainTemplateInfo TemplateInfo;
			public readonly TemplateSegment TemplateSegment;
			public readonly int StartTypeId;
			public readonly int EndTypeId;
			public readonly int2 Offset;
			public readonly int2 Moves;
			public readonly int2[] RelativePoints;
			public readonly int[] Directions;
			public readonly int[] DirectionMasks;
			public readonly int[] ReverseDirectionMasks;

			public TilePathSegment(TerrainTemplateInfo templateInfo, TemplateSegment templateSegment, int startId, int endId)
			{
				TemplateInfo = templateInfo;
				TemplateSegment = templateSegment;
				StartTypeId = startId;
				EndTypeId = endId;
				Offset = templateSegment.Points[0];
				Moves = templateSegment.Points[^1] - Offset;
				RelativePoints = templateSegment.Points
					.Select(p => p - templateSegment.Points[0])
					.ToArray();

				Directions = new int[RelativePoints.Length];
				DirectionMasks = new int[RelativePoints.Length];
				ReverseDirectionMasks = new int[RelativePoints.Length];

				// Last point has no direction.
				Directions[^1] = MapUtils.Direction.NONE;
				DirectionMasks[^1] = 0;
				ReverseDirectionMasks[^1] = 0;
				for (var i = 0; i < RelativePoints.Length - 1; i++)
				{
					var direction = Direction.FromOffset(RelativePoints[i + 1] - RelativePoints[i]);
					if (direction == MapUtils.Direction.NONE)
						throw new ArgumentException("TemplateSegment has duplicate points in sequence");
					Directions[i] = direction;
					DirectionMasks[i] = 1 << direction;
					ReverseDirectionMasks[i] = 1 << Direction.Reverse(direction);
				}
			}
		}

		static int2[] TilePath(Map map, Path path, MersenneTwister random, int minimumThickness)
		{
			var maxDeviation = (minimumThickness - 1) >> 1;
			var minPoint = new int2(
				path.Points.Min(p => p.X) - maxDeviation,
				path.Points.Min(p => p.Y) - maxDeviation);
			var maxPoint = new int2(
				path.Points.Max(p => p.X) + maxDeviation,
				path.Points.Max(p => p.Y) + maxDeviation);
			var points = path.Points.Select(point => point - minPoint).ToArray();

			var isLoop = path.IsLoop;

			// grid points (not squares), so these are offset 0.5 from tile centers.
			var size = new int2(1 + maxPoint.X - minPoint.X, 1 + maxPoint.Y - minPoint.Y);
			var sizeXY = size.X * size.Y;

			const int MAX_DEVIATION = int.MaxValue;

			// Bit masks of 8-angle directions which are considered a positive progress
			// traversal. Template choices with an overall negative progress traversal
			// are rejected.
			var directions = new Matrix<byte>(size);

			// How far away from the path this point is.
			var deviations = new Matrix<int>(size).Fill(MAX_DEVIATION);

			// Bit masks of 8-angle directions which define whether it's permitted
			// to traverse from one point to a given neighbour.
			var traversables = new Matrix<byte>(size);
			{
				var gradientX = new Matrix<int>(size);
				var gradientY = new Matrix<int>(size);
				for (var pointI = 0; pointI < points.Length; pointI++)
				{
					if (isLoop && pointI == 0)
					{
						// Same as last point.
						continue;
					}

					var point = points[pointI];
					var pointPrevI = pointI - 1;
					var pointNextI = pointI + 1;
					var directionX = 0;
					var directionY = 0;
					if (pointNextI < points.Length)
					{
						directionX += points[pointNextI].X - point.X;
						directionY += points[pointNextI].Y - point.Y;
					}

					if (pointPrevI >= 0)
					{
						directionX += point.X - points[pointPrevI].X;
						directionY += point.Y - points[pointPrevI].Y;
					}

					for (var deviation = 0; deviation <= maxDeviation; deviation++)
					{
						var minX = point.X - deviation;
						var minY = point.Y - deviation;
						var maxX = point.X + deviation;
						var maxY = point.Y + deviation;
						for (var y = minY; y <= maxY; y++)
						{
							for (var x = minX; x <= maxX; x++)
							{
								// const i = y * sizeX + x;
								if (deviation < deviations[x, y])
								{
									deviations[x, y] = deviation;
								}

								if (deviation == maxDeviation)
								{
									gradientX[x, y] += directionX;
									gradientY[x, y] += directionY;
									if (x > minX)
										traversables[x, y] |= Direction.M_L;
									if (x < maxX)
										traversables[x, y] |= Direction.M_R;
									if (y > minY)
										traversables[x, y] |= Direction.M_U;
									if (y < maxY)
										traversables[x, y] |= Direction.M_D;
									if (x > minX && y > minY)
										traversables[x, y] |= Direction.M_LU;
									if (x > minX && y < maxY)
										traversables[x, y] |= Direction.M_LD;
									if (x < maxX && y > minY)
										traversables[x, y] |= Direction.M_RU;
									if (x < maxX && y < maxY)
										traversables[x, y] |= Direction.M_RD;
								}
							}
						}
					}
				}

				// Probational
				for (var i = 0; i < sizeXY; i++)
				{
					if (gradientX[i] == 0 && gradientY[i] == 0)
					{
						directions[i] = 0;
						continue;
					}

					var direction = Direction.FromOffset(gradientX[i], gradientY[i]);

					// .... direction: 0123456701234567
					//                 UUU DDD UUU DDD
					//                 R LLL RRR LLL R
					directions[i] = (byte)(0b100000111000001 >> (7 - direction));
				}
			}

			var pathStart = points[0];
			var pathEnd = points[^1];
			var permittedTemplates = path.PermittedTemplates.All.ToImmutableHashSet();

			const int MAX_SCORE = int.MaxValue;
			var segmentTypeToId = new Dictionary<string, int>();
			var segmentsByStart = new List<List<TilePathSegment>>();
			var segmentsByEnd = new List<List<TilePathSegment>>();
			var scores = new List<Matrix<int>>();
			{
				void RegisterSegmentType(string type)
				{
					if (segmentTypeToId.ContainsKey(type)) return;
					var newId = segmentTypeToId.Count;
					segmentTypeToId.Add(type, newId);
					segmentsByStart.Add(new List<TilePathSegment>());
					segmentsByEnd.Add(new List<TilePathSegment>());
					scores.Add(new Matrix<int>(size).Fill(MAX_SCORE));
				}

				foreach (var template in permittedTemplates)
				{
					foreach (var segment in template.Segments)
					{
						RegisterSegmentType(segment.Start);
						RegisterSegmentType(segment.End);
						var startTypeId = segmentTypeToId[segment.Start];
						var endTypeId = segmentTypeToId[segment.End];
						var tilePathSegment = new TilePathSegment(template, segment, startTypeId, endTypeId);
						segmentsByStart[startTypeId].Add(tilePathSegment);
						segmentsByEnd[endTypeId].Add(tilePathSegment);
					}
				}
			}

			var totalTypeIds = segmentTypeToId.Count;

			var priorities = new PriorityArray<int>(totalTypeIds * size.X * size.Y, MAX_SCORE);
			void SetPriorityAt(int typeId, int2 pos, int priority)
				=> priorities[typeId * sizeXY + pos.Y * size.X + pos.X] = priority;
			(int TypeId, int2 Pos, int Priority) GetNextPriority()
			{
				var index = priorities.GetMinIndex();
				var priority = priorities[index];
				var typeId = index / sizeXY;
				var xy = index % sizeXY;
				return (typeId, new int2(xy % size.X, xy / size.X), priority);
			}

			var pathStartTypeId = segmentTypeToId[path.Start.SegmentType];
			var pathEndTypeId = segmentTypeToId[path.End.SegmentType];
			var innerTypeIds = path.PermittedTemplates.Inner
				.SelectMany(template => template.Segments)
				.SelectMany(segment => new[] { segment.Start, segment.End })
				.Select(segmentType => segmentTypeToId[segmentType])
				.ToImmutableHashSet();

			// Assumes both f and t are in the sizeX/sizeY bounds.
			// Lower (closer to zero) scores are better matches.
			// Higher scores are worse matches.
			// MAX_SCORE means totally unacceptable.
			int ScoreSegment(TilePathSegment segment, int2 from)
			{
				if (from == pathStart)
				{
					if (segment.StartTypeId != pathStartTypeId)
						return MAX_SCORE;
				}
				else
				{
					if (!innerTypeIds.Contains(segment.StartTypeId))
						return MAX_SCORE;
				}

				if (from + segment.Moves == pathEnd)
				{
					if (segment.EndTypeId != pathEndTypeId)
						return MAX_SCORE;
				}
				else
				{
					if (!innerTypeIds.Contains(segment.EndTypeId))
						return MAX_SCORE;
				}

				var deviationAcc = 0;
				var progressionAcc = 0;
				var lastPointI = segment.RelativePoints.Length - 1;
				for (var pointI = 0; pointI <= lastPointI; pointI++)
				{
					var point = from + segment.RelativePoints[pointI];
					var directionMask = segment.DirectionMasks[pointI];
					var reverseDirectionMask = segment.ReverseDirectionMasks[pointI];
					if (point.X < 0 || point.X >= size.X || point.Y < 0 || point.Y >= size.Y)
					{
						// Intermediate point escapes array bounds.
						return MAX_SCORE;
					}

					if (pointI < lastPointI)
					{
						if ((traversables[point] & directionMask) == 0)
						{
							// Next point escapes traversable area.
							return MAX_SCORE;
						}

						if ((directions[point] & directionMask) == directionMask)
						{
							progressionAcc++;
						}
						else if ((directions[point] & reverseDirectionMask) == reverseDirectionMask)
						{
							progressionAcc--;
						}
					}

					if (pointI > 0)
					{
						// Don't double-count the template's path's starts and ends
						deviationAcc += deviations[point];
					}
				}

				if (progressionAcc < 0)
				{
					// It's moved backwards
					return MAX_SCORE;
				}

				// Satisfies all requirements.
				return deviationAcc;
			}

			void UpdateFrom(int2 from, int fromTypeId)
			{
				var fromScore = scores[fromTypeId][from];
				foreach (var segment in segmentsByStart[fromTypeId])
				{
					var to = from + segment.Moves;
					if (to.X < 0 || to.X >= size.X || to.Y < 0 || to.Y >= size.Y)
					{
						continue;
					}

					// Most likely to fail. Check first.
					if (deviations[to] == MAX_DEVIATION)
					{
						// End escapes bounds.
						continue;
					}

					var segmentScore = ScoreSegment(segment, from);
					if (segmentScore == MAX_SCORE)
					{
						continue;
					}

					var toScore = fromScore + segmentScore;
					var toTypeId = segment.EndTypeId;
					if (toScore < scores[toTypeId][to])
					{
						scores[toTypeId][to] = toScore;
						SetPriorityAt(toTypeId, to, toScore);
					}
				}

				SetPriorityAt(fromTypeId, from, MAX_SCORE);
			}

			scores[pathStartTypeId][pathStart] = 0;
			UpdateFrom(pathStart, pathStartTypeId);

			// Needed in case we loop back to the start.
			scores[pathStartTypeId][pathStart] = MAX_SCORE;

			while (true)
			{
				var (fromTypeId, from, priority) = GetNextPriority();

				// TODO: Break if we're on the end point?
				if (priority == MAX_SCORE)
				{
					break;
				}

				UpdateFrom(from, fromTypeId);
			}

			// Trace back and update tiles
			var resultPoints = new List<int2>
			{
				pathEnd + minPoint
			};

			(int2 From, int FromTypeId) TraceBackStep(int2 to, int toTypeId)
			{
				var toScore = scores[toTypeId][to];
				var candidates = new List<TilePathSegment>();
				foreach (var segment in segmentsByEnd[toTypeId])
				{
					var from = to - segment.Moves;
					if (from.X < 0 || from.X >= size.X || from.Y < 0 || from.Y >= size.Y)
					{
						continue;
					}

					// Most likely to fail. Check first.
					if (deviations[from] == MAX_DEVIATION)
					{
						// Start escapes bounds.
						continue;
					}

					var segmentScore = ScoreSegment(segment, from);
					if (segmentScore == MAX_SCORE)
					{
						continue;
					}

					var fromScore = toScore - segmentScore;
					if (fromScore == scores[segment.StartTypeId][from])
					{
						candidates.Add(segment);
					}
				}

				Debug.Assert(candidates.Count >= 1, "TraceBack didn't find an original route");
				var chosenSegment = candidates[random.Next(candidates.Count)];
				var chosenFrom = to - chosenSegment.Moves;
				PaintTemplate(map, chosenFrom - chosenSegment.Offset + minPoint, chosenSegment.TemplateInfo);

				// Skip end point as it is recorded in the previous template.
				for (var i = chosenSegment.RelativePoints.Length - 2; i >= 0; i--)
				{
					var point = chosenSegment.RelativePoints[i];
					resultPoints.Add(chosenFrom + point + minPoint);
				}

				return (chosenFrom, chosenSegment.StartTypeId);
			}

			{
				var to = pathEnd;
				var toTypeId = pathEndTypeId;
				if (scores[toTypeId][to] == MAX_SCORE)
					throw new MapGenerationException("Could not fit tiles for path");
				(to, toTypeId) = TraceBackStep(to, toTypeId);

				// We previously set this to MAX_SCORE in case we were a loop. Reset it for getting back to the start.
				scores[pathStartTypeId][pathStart] = 0;

				// No need to check direction. If that is an issue, I have bigger problems to worry about.
				while (to != pathStart)
				{
					(to, toTypeId) = TraceBackStep(to, toTypeId);
				}
			}

			// Traced back in reverse, so reverse the reversal.
			resultPoints.Reverse();
			return resultPoints.ToArray();
		}

		static void PaintTemplate(Map map, int2 at, TerrainTemplateInfo template)
		{
			if (template.PickAny)
				throw new ArgumentException("PaintTemplate does not expect PickAny");
			for (var y = 0; y < template.Size.Y; y++)
			{
				for (var x = 0; x < template.Size.X; x++)
				{
					var i = (byte)(y * template.Size.X + x);
					if (template[i] == null)
						continue;
					var tile = new TerrainTile(template.Id, i);
					var mpos = new MPos(at.X + x, at.Y + y);
					if (map.Tiles.Contains(mpos))
						map.Tiles[mpos] = tile;
				}
			}
		}

		static Matrix<sbyte> PointsChirality(int2 size, int2[][] pointArrayArray)
		{
			var chirality = new Matrix<sbyte>(size);
			var next = new List<int2>();
			void SeedChirality(int2 point, sbyte value, bool firstPass)
			{
				if (!chirality.ContainsXY(point))
					return;
				if (firstPass)
				{
					// Some paths which overlap or go back on themselves
					// might fight for chirality. Vote on it.
					chirality[point] += value;
				}
				else
				{
					if (chirality[point] != 0)
						return;
					chirality[point] = value;
				}

				next.Add(point);
			}

			foreach (var pointArray in pointArrayArray)
			{
				for (var i = 1; i < pointArray.Length; i++)
				{
					var from = pointArray[i - 1];
					var to = pointArray[i];
					var direction = Direction.FromOffset(to - from);
					var fx = from.X;
					var fy = from.Y;
					switch (direction)
					{
						case Direction.R:
							SeedChirality(new int2(fx    , fy    ),  1, true);
							SeedChirality(new int2(fx    , fy - 1), -1, true);
							break;
						case Direction.D:
							SeedChirality(new int2(fx - 1, fy    ),  1, true);
							SeedChirality(new int2(fx    , fy    ), -1, true);
							break;
						case Direction.L:
							SeedChirality(new int2(fx - 1, fy - 1),  1, true);
							SeedChirality(new int2(fx - 1, fy    ), -1, true);
							break;
						case Direction.U:
							SeedChirality(new int2(fx    , fy - 1),  1, true);
							SeedChirality(new int2(fx - 1, fy - 1), -1, true);
							break;
						default:
							throw new ArgumentException("Unsupported direction for chirality");
					}
				}
			}

			while (next.Count != 0)
			{
				var current = next;
				next = new List<int2>();
				foreach (var point in current)
				{
					foreach (var offset in Direction.SPREAD4)
					{
						SeedChirality(point + offset, chirality[point], false);
					}
				}
			}

			return chirality;
		}

		static Matrix<int> CalculateRoominess(Matrix<bool> elevations, bool roomyEdges)
		{
			var roominess = new Matrix<int>(elevations.Size);

			// This could be more efficient.
			var next = new List<int2>();

			// Find shores and map boundary
			for (var cy = 0; cy < elevations.Size.Y; cy++)
			{
				for (var cx = 0; cx < elevations.Size.X; cx++)
				{
					var pCount = 0;
					var nCount = 0;
					for (var oy = -1; oy <= 1; oy++)
					{
						for (var ox = -1; ox <= 1; ox++)
						{
							var x = cx + ox;
							var y = cy + oy;
							if (!elevations.ContainsXY(x, y))
							{
								// Boundary
							}
							else if (elevations[x, y])
								pCount++;
							else
								nCount++;
						}
					}

					if (roomyEdges && nCount + pCount != 9)
					{
						continue;
					}

					if (pCount != 9 && nCount != 9)
					{
						roominess[cx, cy] = elevations[cx, cy] ? 1 : -1;
						next.Add(new int2(cx, cy));
					}
				}
			}

			if (next.Count == 0)
			{
				// There were no shores. Use minSpan or -minSpan as appropriate.
				var minSpan = Math.Min(elevations.Size.X, elevations.Size.Y);
				roominess.Fill(elevations[0] ? minSpan : -minSpan);
				return roominess;
			}

			for (var distance = 2; next.Count != 0; distance++)
			{
				var current = next;
				next = new List<int2>();
				foreach (var point in current)
				{
					var cx = point.X;
					var cy = point.Y;
					for (var oy = -1; oy <= 1; oy++)
					{
						for (var ox = -1; ox <= 1; ox++)
						{
							if (ox == 0 && oy == 0)
								continue;
							var x = cx + ox;
							var y = cy + oy;
							if (!roominess.ContainsXY(x, y))
								continue;
							if (roominess[x, y] != 0)
								continue;
							roominess[x, y] = elevations[x, y] ? distance : -distance;
							next.Add(new int2(x, y));
						}
					}
				}
			}

			return roominess;
		}

		static int2[][] MaskPoints(int2[][] pointArrayArray, Matrix<bool> mask)
		{
			var newPointArrayArray = new List<int2[]>();

			foreach (var pointArray in pointArrayArray)
			{
				var isLoop = pointArray[0] == pointArray[^1];
				int firstBad;
				for (firstBad = 0; firstBad < pointArray.Length; firstBad++)
				{
					if (!mask[pointArray[firstBad]])
						break;
				}

				if (firstBad == pointArray.Length)
				{
					// The path is entirely within the mask already.
					newPointArrayArray.Add(pointArray);
					continue;
				}

				var startAt = isLoop ? firstBad : 0;
				var wrapAt = isLoop ? pointArray.Length - 1 : pointArray.Length;
				if (wrapAt == 0)
					throw new ArgumentException("single point paths should not exist");
				Debug.Assert(startAt < wrapAt, "start outside wrap bounds");
				var i = startAt;
				List<int2> currentPointArray = null;
				do
				{
					if (mask[pointArray[i]])
					{
						currentPointArray ??= new List<int2>();
						currentPointArray.Add(pointArray[i]);
					}
					else
					{
						if (currentPointArray != null && currentPointArray.Count > 1)
							newPointArrayArray.Add(currentPointArray.ToArray());
						currentPointArray = null;
					}

					i++;
					if (i == wrapAt)
						i = 0;
				}
				while (i != startAt);

				if (currentPointArray != null && currentPointArray.Count > 1)
					newPointArrayArray.Add(currentPointArray.ToArray());
			}

			return newPointArrayArray.ToArray();
		}

		static void ObstructArea(Map map, List<ActorPlan> actorPlans, Matrix<Replaceability> replace, IReadOnlyList<Obstacle> permittedObstacles, MersenneTwister random)
		{
			var obstaclesByAreaDict = new Dictionary<int, List<Obstacle>>();
			foreach (var obstacle in permittedObstacles)
			{
				if (!obstaclesByAreaDict.ContainsKey(obstacle.Area))
					obstaclesByAreaDict.Add(obstacle.Area, new List<Obstacle>());
				obstaclesByAreaDict[obstacle.Area].Add(obstacle);
			}

			var obstaclesByArea = obstaclesByAreaDict
				.OrderBy(kv => -kv.Key)
				.ToList();
			var obstacleTotalArea = permittedObstacles.Sum(t => t.Area);
			var obstacleTotalWeight = permittedObstacles.Sum(t => t.Weight);

			// Give 1-by-1 entities the final pass, as they are most flexible.
			obstaclesByArea.Add(
				new KeyValuePair<int, List<Obstacle>>(
					1,
					permittedObstacles.Where(o => o.HasEntities && o.Area == 1).ToList()));
			var size = map.MapSize;
			var replaceIndices = new int[replace.Data.Length];
			var remaining = new Matrix<bool>(size);
			var replaceArea = 0;
			for (var n = 0; n < replace.Data.Length; n++)
			{
				if (replace[n] != Replaceability.None)
				{
					remaining[n] = true;
					replaceIndices[replaceArea] = n;
					replaceArea++;
				}
				else
				{
					remaining[n] = false;
				}
			}

			var indices = new int[replace.Data.Length];
			int indexCount;

			void RefreshIndices()
			{
				indexCount = 0;
				// TODO: Why is this array not truncated? Why is it even done this way?
				foreach (var n in replaceIndices)
				{
					if (remaining[n])
					{
						indices[indexCount] = n;
						indexCount++;
					}
				}

				random.ShuffleInPlace(indices, 0, indexCount);
			}

			Replaceability ReserveShape(int2 paintXY, IEnumerable<int2> shape, Replaceability contract)
			{
				foreach (var shapeXY in shape)
				{
					var xy = paintXY + shapeXY;
					if (!replace.ContainsXY(xy))
						continue;
					if (!remaining[xy])
					{
						// Can't reserve - not the right shape
						return Replaceability.None;
					}

					contract &= replace[xy];
					if (contract == Replaceability.None)
					{
						// Can't reserve - obstruction choice doesn't comply
						// with replaceability of original tiles.
						return Replaceability.None;
					}
				}

				// Can reserve. Commit.
				foreach (var shapeXY in shape)
				{
					var xy = paintXY + shapeXY;
					if (!replace.ContainsXY(xy))
						continue;

					remaining[xy] = false;
				}

				return contract;
			}

			foreach (var obstaclesKv in obstaclesByArea)
			{
				var obstacles = obstaclesKv.Value;
				if (obstacles.Count == 0)
					continue;

				var obstacleArea = obstacles[0].Area;
				var obstacleWeights = obstacles.Select(o => o.Weight).ToArray();
				var obstacleWeightForArea = obstacleWeights.Sum();
				var remainingQuota =
					obstacleArea == 1
						? int.MaxValue
						: (int)Math.Ceiling(replaceArea * obstacleWeightForArea / obstacleTotalWeight);
				RefreshIndices();
				foreach (var n in indices)
				{
					var obstacle = obstacles[random.PickWeighted(obstacleWeights)];
					var paintXY = replace.XY(n);
					var contract = ReserveShape(paintXY, obstacle.Shape, obstacle.Contract());
					if (contract != Replaceability.None)
					{
						obstacle.Paint(actorPlans, paintXY, contract);
					}

					remainingQuota -= obstacleArea;
					if (remainingQuota <= 0)
						break;
				}
			}
		}

		static Matrix<Replaceability> IdentifyReplaceableTiles(Map map, ITemplatedTerrainInfo tileset, Dictionary<TerrainTile, Replaceability> replaceabilityMap)
		{
			var output = new Matrix<Replaceability>(map.MapSize);

			foreach (var cell in map.AllCells)
			{
				var mpos = cell.ToMPos(map);
				var tile = map.Tiles[mpos];
				var replaceability = Replaceability.Any;
				if (replaceabilityMap.TryGetValue(tile, out var value))
					replaceability = value;
				output[mpos.U, mpos.V] = replaceability;
			}

			return output;
		}


		static (Matrix<int> RegionMask, Region[] Regions, Matrix<Playability> Playable) FindPlayableRegions(Map map, List<ActorPlan> actorPlans, Dictionary<TerrainTile, Playability> playabilityMap)
		{
			var size = map.MapSize;
			var regions = new List<Region>();
			var regionMask = new Matrix<int>(size);
			var playable = new Matrix<Playability>(size);
			for (var y = 0; y < size.Y; y++)
			{
				for (var x = 0; x < size.X; x++)
				{
					playable[x, y] = playabilityMap[map.Tiles[new MPos(x, y)]];
				}
			}

			var externalCircle = new Matrix<bool>(size);
			var externalCircleCenter = (size - new float2(1.0f, 1.0f)) / 2.0f;
			var minSpan = Math.Min(size.X, size.Y);
			externalCircle.DrawCircle(
				center: externalCircleCenter,
				radius: minSpan / 2.0f - 1.0f,
				setTo: (_, _) => true,
				invert: true);
			MatrixUtils.ReserveForEntitiesInPlace(
				playable,
				actorPlans,
				(old) => old == Playability.Playable ? Playability.Partial : old);
			void Fill(Region region, int2 start)
			{
				void AddToRegion(int2 xy, bool fullyPlayable)
				{
					regionMask[xy] = region.Id;
					region.Area++;
					if (fullyPlayable)
						region.PlayableArea++;
					if (externalCircle[xy])
						region.ExternalCircle = true;
				}

				bool? Filler(int2 xy, bool fullyPlayable, int _)
				{
					if (regionMask[xy] == 0)
					{
						if (fullyPlayable && playable[xy] == Playability.Playable)
						{
							AddToRegion(xy, true);
							return true;
						}
						else if (playable[xy] == Playability.Partial)
						{
							AddToRegion(xy, false);
							return false;
						}
					}

					return null;
				}

				MatrixUtils.FloodFill(size, new[] { (start, true, Direction.NONE) }, Filler, Direction.SPREAD4_D);
			}

			for (var y = 0; y < size.Y; y++)
			{
				for (var x = 0; x < size.X; x++)
				{
					var start = new int2(x, y);
					if (regionMask[start] == 0 && playable[start] == Playability.Playable)
					{
						var region = new Region()
						{
							Area = 0,
							PlayableArea = 0,
							Id = regions.Count + 1,
							ExternalCircle = false,
						};
						regions.Add(region);
						Fill(region, start);
					}
				}
			}
			return (regionMask, regions.ToArray(), playable);
		}

		static Matrix<byte> RemoveJunctionsFromDirectionMap(Matrix<byte> input)
		{
			var output = input.Clone();
			for (var cy = 0; cy < input.Size.Y; cy++)
			{
				for (var cx = 0; cx < input.Size.X; cx++)
				{
					var dm = input[cx, cy];
					if (Direction.Count(dm) > 2)
					{
						output[cx, cy] = 0;
						foreach (var (offset, d) in Direction.SPREAD8_D)
						{
							var xy = new int2(cx + offset.X, cy + offset.Y);
							if (!input.ContainsXY(xy))
								continue;
							var dr = Direction.Reverse(d);
							output[xy] = (byte)(output[xy] & ~(1 << dr));
						}
					}
				}
			}

			for (var x = 0; x < input.Size.X; x++)
			{
				output[x, 0] = (byte)(output[x, 0] & ~(Direction.M_LU | Direction.M_U | Direction.M_RU));
				output[x, input.Size.Y - 1] = (byte)(output[x, input.Size.Y - 1] & ~(Direction.M_RD | Direction.M_D | Direction.M_LD));
			}

			for (var y = 0; y < input.Size.Y; y++)
			{
				output[0, y] = (byte)(output[0, y] & ~(Direction.M_LD | Direction.M_L | Direction.M_LU));
				output[input.Size.X - 1, y] &= (byte)(output[input.Size.X - 1, y] & ~(Direction.M_RU | Direction.M_R | Direction.M_RD));
			}

			return output;
		}

		static int2[][] DirectionMapToPointArrays(Matrix<byte> input)
		{
			// Loops not handled, but these would be extremely rare anyway.
			var pointArrays = new List<int2[]>();
			for (var sy = 0; sy < input.Size.Y; sy++)
			{
				for (var sx = 0; sx < input.Size.X; sx++)
				{
					var sdm = input[sx, sy];
					if (Direction.FromMask(sdm) != Direction.NONE)
					{
						var points = new List<int2>();
						var xy = new int2(sx, sy);
						var reverseDm = 0;

						bool AddPoint()
						{
							points.Add(xy);
							var dm = input[xy] & ~reverseDm;
							foreach (var (offset, d) in Direction.SPREAD8_D)
							{
								if ((dm & (1 << d)) != 0)
								{
									xy += offset;
									if (!input.ContainsXY(xy))
										throw new ArgumentException("input should not link out of bounds");
									reverseDm = 1 << Direction.Reverse(d);
									return true;
								}
							}

							return false;
						}

						while (AddPoint())
						{
						}

						pointArrays.Add(points.ToArray());
					}
				}
			}

			return pointArrays.ToArray();
		}

		// Assumes that inputs do not have overlapping end points.
		// No loop support.
		static int2[][] DeduplicateAndNormalizePointArrays(int2[][] inputs, int2 size)
		{
			bool ShouldReverse(int2[] points)
			{
				// This could be converted to integer math, but there's little motive.
				var midX = (size.X - 1) / 2.0f;
				var midY = (size.Y - 1) / 2.0f;
				var v1x = points[0].X - midX;
				var v1y = points[0].Y - midY;
				var v2x = points[^1].X - midX;
				var v2y = points[^1].Y - midY;

				// Rotation around center?
				var crossProd = v1x * v2y - v2x * v1y;
				if (crossProd != 0)
					return crossProd < 0;

				// Distance from center?
				var r1 = v1x * v1x + v1y * v1y;
				var r2 = v2x * v2x + v2y * v2y;
				if (r1 != r2)
					return r1 < r2;

				// Absolute angle
				return v1y == v2y ? v1x > v2x : v1y > v2y;
			}

			var outputs = new List<int2[]>();
			var lookup = new Matrix<bool>(size + new int2(1, 1));
			foreach (var points in inputs)
			{
				var normalized = (int2[])points.Clone();
				if (ShouldReverse(points))
					Array.Reverse(normalized);
				var xy = new int2(normalized[0].X, normalized[0].Y);
				if (!lookup[xy])
				{
					outputs.Add(normalized);
					lookup[xy] = true;
				}
			}

			return outputs.ToArray();
		}

		// No loop support.
		// May return null.
		static int2[] ShrinkPointArray(int2[] points, int shrinkBy, int minimumLength)
		{
			if (minimumLength <= 1)
				throw new ArgumentException("minimumLength must be greater than 1");
			if (points.Length < shrinkBy * 2 + minimumLength)
				return null;
			return points[shrinkBy..(points.Length - shrinkBy)];
		}

		static int2[] InertiallyExtendPathInPlace(int2[] points, int extension, int inertialRange)
		{
			if (inertialRange > points.Length - 1)
				inertialRange = points.Length - 1;
			var sd = Direction.FromOffsetNonDiagonal(points[inertialRange] - points[0]);
			var ed = Direction.FromOffsetNonDiagonal(points[^1] - points[^(inertialRange + 1)]);
			var newPoints = new int2[points.Length + extension * 2];

			for (var i = 0; i < extension; i++)
			{
				newPoints[i] = points[0] - Direction.ToOffset(sd) * (extension - i);
			}

			Array.Copy(points, 0, newPoints, extension, points.Length);

			for (var i = 0; i < extension; i++)
			{
				newPoints[extension + points.Length + i] = points[^1] + Direction.ToOffset(ed) * (i + 1);
			}

			return newPoints;
		}

		static Matrix<int> CalculateSpawnPreferences(Matrix<int> roominess, float centralReservation, int spawnRegionSize, int rotations, Symmetry.Mirror mirror)
		{
			var preferences = roominess.Map(r => Math.Min(r, spawnRegionSize));
			var centralReservationSq = centralReservation * centralReservation;
			var spawnRegionSize2Sq = 4 * spawnRegionSize * spawnRegionSize;
			var size = roominess.Size;

			// This -0.5 is required to compensate for the top-left vs the center of a grid square.
			var center = new float2(size) / 2.0f - new float2(0.5f, 0.5f);

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
							if (MathF.Abs((x - center.X) + (y - center.Y)) <= centralReservation * SQRT2)
								preferences[x, y] = 1;
							break;
						case Symmetry.Mirror.TopMatchesBottom:
							if (MathF.Abs(y - center.Y) <= centralReservation)
								preferences[x, y] = 1;
							break;
						case Symmetry.Mirror.TopRightMatchesBottomLeft:
							if (MathF.Abs((x - center.X) - (y - center.Y)) <= centralReservation * SQRT2)
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
				case "TEMPERAT":
					break;
				default:
					return false;
			}

			return true;
		}
	}
}
