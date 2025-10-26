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

namespace OpenRA.Mods.Common.MapGenerator
{
	public sealed class LatTiler
	{
		public class LatRule
		{
			public readonly ushort MainTile;
			public readonly ushort? LowTile;
			public readonly ushort? HighTile;

			// Array index is a bitmask of U=1, R=2, D=4, L=8.
			public readonly ImmutableArray<ushort> Replacements;

			public LatRule(
				ushort mainTile,
				ushort? lowTile,
				ushort? highTile,
				ImmutableArray<ushort> replacements)
			{
				if (!lowTile.HasValue && !highTile.HasValue)
					throw new ArgumentException("both lowTile and highTile were null");

				MainTile = mainTile;
				LowTile = lowTile;
				HighTile = highTile;
				Replacements = replacements;
			}

			public ushort? OfferReplacement(ushort main, ushort[] adjacents)
			{
				if (main != MainTile)
					return null;

				if (LowTile.HasValue &&
					HighTile.HasValue &&
					adjacents.Any(t => t != LowTile.Value && t != HighTile.Value))
				{
					return null;
				}

				bool CheckBit(ushort type) =>
					LowTile.HasValue
						? type != LowTile.Value
						: type == HighTile.Value;

				var index =
					(CheckBit(adjacents[0]) ? 1 : 0) |
					(CheckBit(adjacents[1]) ? 2 : 0) |
					(CheckBit(adjacents[2]) ? 4 : 0) |
					(CheckBit(adjacents[3]) ? 8 : 0);
				return Replacements[index];
			}
		}

		readonly ImmutableArray<LatRule> latRules;
		readonly ImmutableDictionary<ushort, ushort> canonicalizations;

		public LatTiler(
			ImmutableArray<LatRule> latRules,
			ImmutableDictionary<ushort, ushort> canonicalizations)
		{
			this.latRules = latRules;
			this.canonicalizations = canonicalizations;
		}

		public ushort CanonicalType(TerrainTile tile)
		{
			return canonicalizations.GetValueOrDefault(tile.Type, tile.Type);
		}

		static TerrainTile PickTile(MersenneTwister random, ITemplatedTerrainInfo templatedTerrainInfo, ushort tileType)
		{
			if (random != null && templatedTerrainInfo != null && templatedTerrainInfo.Templates.TryGetValue(tileType, out var template) && template.PickAny)
				return new TerrainTile(tileType, (byte)random.Next(0, template.TilesCount));
			else
				return new TerrainTile(tileType, 0);
		}

		public CellLayer<TerrainTile> OfferReplacements(
			MersenneTwister random,
			ITemplatedTerrainInfo templatedTerrainInfo,
			CellLayer<TerrainTile> original)
		{
			var replaced = CellLayerUtils.Clone(original);
			foreach (var cpos in original.CellRegion)
			{
				var main = original[cpos].Type;
				ushort[] adjacents = [main, main, main, main];
				if (original.Contains(cpos + new CVec(0, -1)))
					adjacents[0] = CanonicalType(original[cpos + new CVec(0, -1)]);

				if (original.Contains(cpos + new CVec(1, 0)))
					adjacents[1] = CanonicalType(original[cpos + new CVec(1, 0)]);

				if (original.Contains(cpos + new CVec(0, 1)))
					adjacents[2] = CanonicalType(original[cpos + new CVec(0, 1)]);

				if (original.Contains(cpos + new CVec(-1, 0)))
					adjacents[3] = CanonicalType(original[cpos + new CVec(-1, 0)]);

				foreach (var latRule in latRules)
				{
					var maybe = latRule.OfferReplacement(main, adjacents);
					if (maybe.HasValue)
					{
						replaced[cpos] = PickTile(random, templatedTerrainInfo, maybe.Value);
						break;
					}
				}
			}

			return replaced;
		}

		public void Replace(MersenneTwister random, Map map)
		{
			var templatedTerrainInfo = map.Rules.TerrainInfo as ITemplatedTerrainInfo;
			var updated = OfferReplacements(random, templatedTerrainInfo, map.Tiles);
			foreach (var mpos in map.Tiles.CellRegion.MapCoords)
				map.Tiles[mpos] = updated[mpos];
		}
	}
}
