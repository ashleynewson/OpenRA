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
	[Desc("A map generator that clears a map.")]
	[TraitLocation(SystemActors.World)]
	public sealed class ClearMapGeneratorInfo : TraitInfo, IMapGeneratorInfo
	{
		[Desc("Human-readable name this generator uses.")]
		public readonly string Name = "Clear";

		[FieldLoader.Require]
		[Desc("Internal id for this map generator.")]
		public readonly string Type = null;

		string IMapGeneratorInfo.Type => Type;

		string IMapGeneratorInfo.Name => Name;

		public override object Create(ActorInitializer init) { return new ClearMapGenerator(this); }
	}

	public sealed class ClearMapGenerator : IMapGenerator
	{
		readonly ClearMapGeneratorInfo info;

		IMapGeneratorInfo IMapGenerator.Info => info;

		public ClearMapGenerator(ClearMapGeneratorInfo info)
		{
			this.info = info;
		}

		public IEnumerable<MapGeneratorSetting> GetDefaultSettings(Map map, ModData modData)
		{
			var tileset = modData.DefaultTerrainInfo[map.Tileset];
			return ImmutableList.Create(
				new MapGeneratorSetting("tile", "Tile", new MapGeneratorSetting.IntegerValue(tileset.DefaultTerrainTile.Type))
			);
		}

		public void Generate(Map map, ModData modData, MersenneTwister random, IEnumerable<MapGeneratorSetting> settingsEnumerable)
		{
			// TODO: translate exception messages?
			var settings = Enumerable.ToDictionary(settingsEnumerable, s => s.Name);
			var tileset = modData.DefaultTerrainInfo[map.Tileset];

			ushort tileType;
			try
			{
				checked
				{
					tileType = (ushort)settings["tile"].Get<long>();
				}
			}
			catch (OverflowException)
			{
				throw new MapGenerationException("Illegal tile type");
			}

			var tile = new TerrainTile(tileType, 0);
			if (!tileset.TryGetTerrainInfo(tile, out var _))
				throw new MapGenerationException("Illegal tile type");

			// If the default terrain tile is part of a PickAny template, pick
			// a random tile index. Otherwise, just use the default tile.
			Func<TerrainTile> tilePicker;
			if (map.Rules.TerrainInfo is ITemplatedTerrainInfo templatedTerrainInfo && templatedTerrainInfo.Templates.TryGetValue(tileType, out var template) && template.PickAny)
				tilePicker = () => new TerrainTile(tileType, (byte)random.Next(0, template.TilesCount));
			else
				tilePicker = () => tile;

			foreach (var cell in map.AllCells)
			{
				var mpos = cell.ToMPos(map);
				map.Tiles[mpos] = tilePicker();
				map.Resources[mpos] = new ResourceTile(0, 0);
				map.Height[mpos] = 0;
			}

			map.PlayerDefinitions = new MapPlayers(map.Rules, 0).ToMiniYaml();
			map.ActorDefinitions = ImmutableArray<MiniYamlNode>.Empty;
		}

		public bool ShowInEditor(Map map, ModData modData)
		{
			return true;
		}
	}
}
