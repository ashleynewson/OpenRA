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
			[FieldLoader.Require]
			public readonly ushort Main;
			public readonly ushort? Low = null;
			public readonly ushort? High = null;

			// Array index is a bitmask of U=1, R=2, D=4, L=8.
			[FieldLoader.Ignore]
			public readonly ImmutableArray<ushort> Replacements;

			public LatRule(
				ushort main,
				ushort? low,
				ushort? high,
				ImmutableArray<ushort> replacements)
			{
				if (!low.HasValue && !high.HasValue)
					throw new ArgumentException("both lowTile and highTile were null");

				if (replacements.Length != 16)
					throw new ArgumentException("replacements did not have 16 elements");

				Main = main;
				Low = low;
				High = high;
				Replacements = replacements;
			}

			public LatRule(MiniYaml my)
			{
				FieldLoader.Load(this, my);
				Replacements = FieldLoader.GetValue<List<ushort>>(
					nameof(Replacements), my.NodeWithKey(nameof(Replacements)).Value.Value)
						.ToImmutableArray();

				if (!Low.HasValue && !High.HasValue)
					throw new YamlException("both Low and High were null in LatRule");

				if (Replacements.Length != 16)
					throw new ArgumentException("Replacements did not have 16 elements");
			}

			public ushort? OfferReplacement(ushort main, ushort[] adjacents)
			{
				if (main != Main)
					return null;

				if (Low.HasValue &&
					High.HasValue &&
					adjacents.Any(t => t != Low.Value && t != High.Value))
				{
					return null;
				}

				bool CheckBit(ushort type) =>
					Low.HasValue
						? type != Low.Value
						: type == High.Value;

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

		public LatTiler(MiniYaml my)
		{
			var latRules = new List<LatRule>();
			var canonicalizations = new Dictionary<ushort, ushort>();
			foreach (var node in my.Nodes)
			{
				var parts = node.Key.Split('@');
				switch (parts[0])
				{
					case "Rule":
						latRules.Add(new LatRule(node.Value));
						break;
					case "UseAs":
						if (parts.Length != 2 || !Exts.TryParseUshortInvariant(parts[1], out var to))
							throw new YamlException($"invalid UseAs `{node.Key}`");

						foreach (var fromStr in node.Value.Value.Split(","))
						{
							if (!Exts.TryParseUshortInvariant(fromStr, out var from))
								throw new YamlException($"invalid UseAs `{node.Key}`");

							canonicalizations.Add(from, to);
						}

						break;
					default:
						throw new YamlException($"Invalid LatTiler key `{node.Key}`");
				}
			}

			this.latRules = latRules.ToImmutableArray();
			this.canonicalizations = canonicalizations.ToImmutableDictionary();
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
