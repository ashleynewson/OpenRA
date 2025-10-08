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
using System.Linq;
using OpenRA.Support;

namespace OpenRA.Mods.Common.MapGenerator
{
	public class RampTiler
	{
		readonly Map map;

		// Lookup from a binary-concatenation of corner heights (0, 1, or 2) to ramp types.
		readonly Dictionary<int, List<byte>> rampLookup;

		// Contains single-tile brushes with zero height offset.
		readonly Dictionary<byte, (MultiBrush[] Brushes, int[] Weights)> brushLookup;

		public RampTiler(Map map, IReadOnlyList<MultiBrush> brushes)
		{
			this.map = map;
			var heightStep = map.Grid.TileScale / 2;
			rampLookup = new();
			for (var i = 0; i < map.Grid.Ramps.Length; i++) {
				var ramp = map.Grid.Ramps[i];
				var tl = ramp.Corners[0].Z / heightStep;
				var tr = ramp.Corners[1].Z / heightStep;
				var bl = ramp.Corners[2].Z / heightStep;
				var br = ramp.Corners[3].Z / heightStep;
				var lookup = tl | (tr << 2) | (bl << 4) | (br << 6);
				if (!rampLookup.ContainsKey(lookup))
					rampLookup.Add(lookup, new());

				rampLookup[lookup].Add((byte)i);
			}

			var rampsToBrushes = new Dictionary<byte, List<MultiBrush>>();
			foreach (var brush in brushes)
			{
				var heightsAndRamps = brush.GetHeightsAndRamps().ToList();
				if (heightsAndRamps.Count != 1 || heightsAndRamps[0].Height != 0)
					throw new NotImplementedException("brushes that are not single-tile are not supported");

				var ramp = heightsAndRamps[0].Ramp;
				if (!rampsToBrushes.ContainsKey(ramp))
					rampsToBrushes.Add(ramp, new());

				rampsToBrushes[ramp].Add(brush);
			}
			brushLookup = rampsToBrushes
				.ToDictionary(
					kv => kv.Key,
					kv => (kv.Value.ToArray(), kv.Value.Select(b => b.Weight).ToArray()));
		}

		public (CellLayer<byte> Heights, CellLayer<byte> Ramps) CornersToRampsAndHeights(
			Matrix<byte> cornerHeights,
			CellLayer<bool> mask,
			MersenneTwister random)
		{
			// TODO: ensure map shape consistency (or just use grid).
			CellLayer<byte> heights = new CellLayer<byte>(map);
			CellLayer<byte> ramps = new CellLayer<byte>(map);
			var matrixBounds = CellLayerUtils.CellBounds(mask);

			var masked =
				mask != null
					? mask.CellRegion.Where(cpos => mask[cpos])
					: map.Tiles.CellRegion;
			foreach (var cpos in masked)
			{
				var x = cpos.X + matrixBounds.X;
				var y = cpos.Y + matrixBounds.Y;
				var tl = cornerHeights[x, y];
				var tr = cornerHeights[x + 1, y];
				var bl = cornerHeights[x, y + 1];
				var br = cornerHeights[x + 1, y + 1];

				var baseHeight = Math.Min(Math.Min(tl, tr), Math.Min(bl, br));
				tl -= baseHeight;
				tr -= baseHeight;
				bl -= baseHeight;
				br -= baseHeight;
				if (tl > 2 || tr > 2 || bl > 2 || br > 2)
					throw new ArgumentException("cornerHeights has adjacent cells with a height difference > 2");

				var lookup = tl | (tr << 2) | (bl << 4) | (br << 6);
				if (!rampLookup.TryGetValue(lookup, out var validRamps))
					return (null, null);

				heights[cpos] = baseHeight;
				ramps[cpos] =
					validRamps.Count == 1
						? validRamps[0]
						: validRamps[random.Next() % validRamps.Count];
			}

			return (heights, ramps);
		}

		/// <summary>Wrapper around CornersToRampsAndHeights and Tile.</summary>
		public MultiBrush TileCorners(Matrix<byte> cornerHeights, CellLayer<bool> mask, MersenneTwister random)
		{
			var (heights, ramps) = CornersToRampsAndHeights(cornerHeights, mask, random);
			if (heights == null)
				return null;

			return Tile(heights, ramps, mask, random);
		}


		/// <summary>
		/// Tile a heightmap using
		/// </summary>
		/// <param name="heights">Heights for tiles.</param>
		/// <param name="ramps">Ramps for tiles.</param>
		/// <param name="mask">Cells to include in the output. Can be null to include everything.</param>
		/// <param name="random">Random source for picking brushes.</param>
		/// <returns>A MultiBrush containing the tiled result, or null if tiling is not possible.</returns>
		public MultiBrush Tile(CellLayer<byte> heights, CellLayer<byte> ramps, CellLayer<bool> mask, MersenneTwister random)
		{
			var result = new MultiBrush();
			var mapGridType = map.Grid.Type;
			var masked =
				mask != null
					? mask.CellRegion.Where(cpos => mask[cpos])
					: map.Tiles.CellRegion;
			foreach (var cpos in masked)
			{
				if (!brushLookup.TryGetValue(ramps[cpos], out var validBrushes))
					return null;

				var brush = validBrushes.Brushes[random.PickWeighted(validBrushes.Weights)];
				result.MergeFrom(brush, cpos - CPos.Zero, mapGridType, heights[cpos]);
			}

			return result;
		}
	}
}
