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

using System.Collections.Immutable;
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

		public bool Generate(Map map, ModData modData)
		{
			var tileset = modData.DefaultTerrainInfo[map.Tileset];

			foreach (var cell in map.AllCells)
			{
				var mpos = cell.ToMPos(map);
				map.Tiles[mpos] = tileset.DefaultTerrainTile;
				map.Resources[mpos] = new ResourceTile(0, 0);
				map.Height[mpos] = 0;
			}

			map.PlayerDefinitions = new MapPlayers(map.Rules, 0).ToMiniYaml();
			map.ActorDefinitions = ImmutableArray<MiniYamlNode>.Empty;

			return true;
		}

		public bool ShowInEditor(Map map, ModData modData)
		{
			return true;
		}
	}
}
