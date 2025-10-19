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
using OpenRA.Primitives;
using OpenRA.Support;

namespace OpenRA.Mods.Common.MapGenerator
{
	public sealed class RampTiler
	{
		public enum AdjustmentMode
		{
			/// <summary>Heights will only increase if absolutely necessary.</summary>
			Minimal,

			/// <summary>Heights will be a be a rounded down median of minimal and maximal.</summary>
			LowerMiddle,

			/// <summary>Heights will be a be a rounded up median of minimal and maximal.</summary>
			UpperMiddle,

			/// <summary>Heights will only decrease if absolutely necessary.</summary>
			Maximal,
		}

		private record struct RampProperties
		{
			public MultiBrush[] Brushes;
			public int[] Weights;
			public byte Tl;
			public byte Tr;
			public byte Br;
			public byte Bl;
		}

		readonly Map map;
		readonly Rectangle cellBounds;

		// Contains single-tile brushes with zero height offset.
		readonly RampProperties[] rampProperties;

		// readonly Dictionary<byte, (MultiBrush[] Brushes, int[] Weights)> brushLookup;

		// Lookup from a binary-concatenation of corner heights (0, 1, or 2) to ramp types.
		// Only contains mappings for which there are brushes.
		readonly Dictionary<int, List<byte>> rampLookup;
		readonly Dictionary<byte, byte> rampCorners;

		public RampTiler(Map map, IReadOnlyList<MultiBrush> brushes)
		{
			this.map = map;
			cellBounds = CellLayerUtils.CellBounds(map);
			var heightStep = map.Grid.TileScale / 2;

			var rampsToBrushes = new Dictionary<byte, List<MultiBrush>>();
			foreach (var brush in brushes)
			{
				var heightsAndRamps = brush.GetHeightsAndRamps().ToList();
				if (heightsAndRamps.Count != 1 || heightsAndRamps[0].Height != 0)
					throw new NotImplementedException("brushes that are not single-tile are not supported");

				var ramp = heightsAndRamps[0].Ramp;
				if (!rampsToBrushes.ContainsKey(ramp))
					rampsToBrushes.Add(ramp, []);

				rampsToBrushes[ramp].Add(brush);
			}

			rampLookup = [];
			rampProperties = new RampProperties[map.Grid.Ramps.Length];

			for (byte ramp = 0; ramp < rampProperties.Length; ramp++)
			{
				var cellRamp = map.Grid.Ramps[ramp];
				var tl = cellRamp.Corners[0].Z / heightStep;
				var tr = cellRamp.Corners[1].Z / heightStep;
				var br = cellRamp.Corners[2].Z / heightStep;
				var bl = cellRamp.Corners[3].Z / heightStep;

				rampProperties[ramp] = new RampProperties()
				{
					Brushes = rampsToBrushes.GetValueOrDefault(ramp, []).ToArray(),
					Weights = rampsToBrushes.GetValueOrDefault(ramp, []).Select(b => b.Weight).ToArray(),
					Tl = (byte)tl,
					Tr = (byte)tr,
					Br = (byte)br,
					Bl = (byte)bl,
				};

				if (rampProperties[ramp].Brushes.Length > 0)
				{
					var lookup = tl | (tr << 2) | (br << 4) | (bl << 6);
					if (!rampLookup.ContainsKey(lookup))
						rampLookup.Add(lookup, []);

					rampLookup[lookup].Add(ramp);
				}
			}
				// brushLookup = rampsToBrushes
				// 	.ToDictionary(
				// 		kv => kv.Key,
				// 		kv => (kv.Value.ToArray(), kv.Value.Select(b => b.Weight).ToArray()));


			// foreach (var rampType in brushLookup.Keys)
			// {
			// 	var cellRamp = map.Grid.Ramps[rampType];
			// 	var tl = cellRamp.Corners[0].Z / heightStep;
			// 	var tr = cellRamp.Corners[1].Z / heightStep;
			// 	var br = cellRamp.Corners[2].Z / heightStep;
			// 	var bl = cellRamp.Corners[3].Z / heightStep;
			// 	var lookup = tl | (tr << 2) | (br << 4) | (bl << 6);
			// 	if (!rampLookup.ContainsKey(lookup))
			// 		rampLookup.Add(lookup, []);

			// 	rampLookup[lookup].Add(rampType);
			// }
		}

		public byte GetTileHeightAt(CPos cpos)
		{
			if (!map.Height.Contains(cpos))
				return 0;

			return map.Height[cpos];
		}

		public byte GetRampTypeAt(CPos cpos)
		{
			if (!map.Tiles.Contains(cpos))
				return 0;

			return map.Rules.TerrainInfo.GetTerrainInfo(map.Tiles[cpos]).RampType;
		}

		public byte GetCornerHeightAt(CPos cpos)
		{
			int height = 0;
			var br = cpos;
			var bl = cpos - new CVec(1, 0);
			var tl = cpos - new CVec(1, 1);
			var tr = cpos - new CVec(0, 1);
			height = Math.Max(height, GetTileHeightAt(br) + rampProperties[GetRampTypeAt(br)].Tl);
			height = Math.Max(height, GetTileHeightAt(bl) + rampProperties[GetRampTypeAt(bl)].Tr);
			height = Math.Max(height, GetTileHeightAt(tl) + rampProperties[GetRampTypeAt(tl)].Br);
			height = Math.Max(height, GetTileHeightAt(tr) + rampProperties[GetRampTypeAt(tr)].Bl);
			return (byte)height;
		}

		public byte GetCornerHeightAtMatrixXy(int2 xy)
		{
			return GetCornerHeightAt(new CPos(xy.X + cellBounds.TopLeft.X, xy.Y + cellBounds.TopLeft.Y));
		}

		public byte GetRampedCellHeightAt(CPos cpos)
		{
			int height = 0;
			var tl = cpos;
			var tr = cpos + new CVec(1, 0);
			var br = cpos + new CVec(1, 1);
			var bl = cpos + new CVec(0, 1);
			height = Math.Max(height, GetCornerHeightAt(tl));
			height = Math.Max(height, GetCornerHeightAt(tr));
			height = Math.Max(height, GetCornerHeightAt(br));
			height = Math.Max(height, GetCornerHeightAt(bl));
			return (byte)height;
		}

		/// <summary>
		/// Updates the corner heights of given cells (each cell has 4 corners) according to what
		/// is currently in the map.
		/// </summary>
		public void PullCornerHeightsForCells(Matrix<byte> cornerHeights, IEnumerable<CPos> cells)
		{
			foreach (var cpos in cells)
			{
				var tl = cpos;
				var tr = cpos + new CVec(1, 0);
				var br = cpos + new CVec(1, 1);
				var bl = cpos + new CVec(0, 1);
				var mtl = new int2(tl.X, tl.Y) - cellBounds.TopLeft;
				var mtr = new int2(tr.X, tr.Y) - cellBounds.TopLeft;
				var mbr = new int2(br.X, br.Y) - cellBounds.TopLeft;
				var mbl = new int2(bl.X, bl.Y) - cellBounds.TopLeft;
				if (cornerHeights.ContainsXY(mtl))
					cornerHeights[mtl] = GetCornerHeightAt(tl);
				if (cornerHeights.ContainsXY(mtr))
					cornerHeights[mtr] = GetCornerHeightAt(tr);
				if (cornerHeights.ContainsXY(mbr))
					cornerHeights[mbr] = GetCornerHeightAt(br);
				if (cornerHeights.ContainsXY(mbl))
					cornerHeights[mbl] = GetCornerHeightAt(bl);
			}
		}

		public void PullCornerHeightsForCellCorners(Matrix<byte> cornerHeights, IEnumerable<CPos> corners)
		{
			foreach (var cpos in corners)
			{
				var xy = new int2(cpos.X, cpos.Y) - cellBounds.TopLeft;
				if (cornerHeights.ContainsXY(xy))
					cornerHeights[xy] = GetCornerHeightAt(cpos);
			}
		}

		public void PullCornerHeightsForMatrixCorners(Matrix<byte> cornerHeights, IEnumerable<int2> corners)
		{
			foreach (var xy in corners)
			{
				if (cornerHeights.ContainsXY(xy))
					cornerHeights[xy] = GetCornerHeightAtMatrixXy(xy);
			}
		}

		/// <summary>
		/// Updates the masked corner heights according to what is currently in the map.
		/// </summary>
		public void PullUnmaskedCornerHeights(Matrix<byte> cornerHeights, Matrix<bool> mask)
		{
			for (var y = 0; y < cornerHeights.Size.Y; y++)
				for (var x = 0; x < cornerHeights.Size.X; x++)
					if (!(mask?[x, y] ?? false))
						cornerHeights[x, y] = GetCornerHeightAtMatrixXy(new int2(x, y));
		}

		/// <summary>
		/// Adjusts input cell corner heights such that all adjacent corners only have a height
		/// difference of -1, 0, or 1.
		/// </summary>
		/// <param name="cornerHeights">Original corner heights.</param>
		/// <param name="mask">Mask of corners that can be adjusted, or null if all can be adjusted.</param>
		/// <param name="mode">Preferred direction to adjust heights.</param>
		/// <returns>Adjusted corner heights, or null if there is no valid solution.</returns>
		public Matrix<byte> ConstrainCornerHeights(
			Matrix<byte> cornerHeights,
			Matrix<bool> mask,
			AdjustmentMode mode)
		{
			IEnumerable<(int2 XY, (byte Height, bool First) Prop)> MaskedSeeds()
			{
				for (var y = 0; y < cornerHeights.Size.Y; y++)
					for (var x = 0; x < cornerHeights.Size.X; x++)
						if (mask?[x, y] ?? true)
							yield return (new int2(x, y), (cornerHeights[x, y], true));
			}

			IEnumerable<(int2 XY, (byte Height, bool First) Prop)> UnmaskedSeeds()
			{
				for (var y = 0; y < cornerHeights.Size.Y; y++)
					for (var x = 0; x < cornerHeights.Size.X; x++)
						if (!mask[x, y])
							yield return (new int2(x, y), (cornerHeights[x, y], true));
			}

			Matrix<byte> GetMinimal(Matrix<byte> matrix, bool masked)
			{
				(byte Lower, bool First)? FillMinimal(int2 xy, (byte Lower, bool First) prop)
				{
					if (!prop.First && (!(mask?[xy] ?? true) || prop.Lower >= matrix[xy]))
						return null;

					matrix[xy] = prop.Lower;
					if (prop.Lower == byte.MaxValue)
						return null;

					return ((byte)(prop.Lower + 1), false);
				}

				var seeds = masked ? MaskedSeeds() : UnmaskedSeeds();
				MatrixUtils.FloodFill(
					cornerHeights.Size,
					seeds.OrderBy(s => s.Prop.Height),
					FillMinimal,
					DirectionExts.Spread4);
				return matrix;
			}

			Matrix<byte> GetMaximal(Matrix<byte> matrix, bool masked)
			{
				(byte Upper, bool First)? FillMaximal(int2 xy, (byte Upper, bool First) prop)
				{
					if (!prop.First && (!(mask?[xy] ?? true) || prop.Upper <= matrix[xy]))
						return null;

					matrix[xy] = prop.Upper;
					if (prop.Upper == byte.MinValue)
						return null;

					return ((byte)(prop.Upper - 1), false);
				}

				var seeds = masked ? MaskedSeeds() : UnmaskedSeeds();
				MatrixUtils.FloodFill(
					cornerHeights.Size,
					seeds.OrderByDescending(s => s.Prop.Height),
					FillMaximal,
					DirectionExts.Spread4);
				return matrix;
			}

			if (mask != null)
			{
				var floor = GetMaximal(new Matrix<byte>(cornerHeights.Size).Fill(byte.MinValue), false);
				var ceiling = GetMinimal(new Matrix<byte>(cornerHeights.Size).Fill(byte.MaxValue), false);

				cornerHeights = cornerHeights.Clone();
				for (var y = 0; y < cornerHeights.Size.Y; y++)
				{
					for (var x = 0; x < cornerHeights.Size.X; x++)
					{
						if (!mask[x, y])
							continue;

						if (floor[x, y] > ceiling[x, y])
							return null;
						else if (cornerHeights[x, y] < floor[x, y])
							cornerHeights[x, y] = floor[x, y];
						else if (cornerHeights[x, y] > ceiling[x, y])
							cornerHeights[x, y] = ceiling[x, y];
					}
				}
			}

			switch (mode)
			{
				case AdjustmentMode.Minimal:
					return GetMinimal(cornerHeights.Clone(), true);
				case AdjustmentMode.LowerMiddle:
					return Matrix<byte>.Zip(
						GetMinimal(cornerHeights.Clone(), true),
						GetMaximal(cornerHeights.Clone(), true),
						(a, b) => (byte)((a + b) / 2));
				case AdjustmentMode.UpperMiddle:
					return Matrix<byte>.Zip(
						GetMinimal(cornerHeights.Clone(), true),
						GetMaximal(cornerHeights.Clone(), true),
						(a, b) => (byte)((a + b + 1) / 2));
				case AdjustmentMode.Maximal:
					return GetMaximal(cornerHeights.Clone(), true);
				default:
					throw new ArgumentException("invalid fitting mode");
			}
		}

		public (CellLayer<byte> Heights, CellLayer<byte> Ramps) CornersToRampsAndHeights(
			Matrix<byte> cornerHeights,
			CellLayer<bool> mask,
			MersenneTwister random)
		{
			// TODO: ensure map shape consistency (or just use grid).
			var heights = new CellLayer<byte>(map);
			var ramps = new CellLayer<byte>(map);

			var masked =
				mask != null
					? mask.CellRegion.Where(cpos => mask[cpos])
					: map.Tiles.CellRegion;
			foreach (var cpos in masked)
			{
				var x = cpos.X - cellBounds.X;
				var y = cpos.Y - cellBounds.Y;
				var tl = cornerHeights[x, y];
				var tr = cornerHeights[x + 1, y];
				var br = cornerHeights[x + 1, y + 1];
				var bl = cornerHeights[x, y + 1];

				var baseHeight = Math.Min(Math.Min(tl, tr), Math.Min(bl, br));
				tl -= baseHeight;
				tr -= baseHeight;
				br -= baseHeight;
				bl -= baseHeight;
				if (Math.Abs(tl - tr) > 1 ||
					Math.Abs(tr - br) > 1 ||
					Math.Abs(br - bl) > 1 ||
					Math.Abs(bl - tl) > 1)
				{
					throw new ArgumentException("cornerHeights has adjacent cell corners with a height difference > 1");
				}

				var lookup = tl | (tr << 2) | (br << 4) | (bl << 6);
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
		/// Tile a heightmap with pre-computed ramps.
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
				var brushes = rampProperties[ramps[cpos]].Brushes;
				if (brushes.Length == 0)
					return null;

				var weights = rampProperties[ramps[cpos]].Weights;
				var brush = brushes[random.PickWeighted(weights)];
				result.MergeFrom(brush, cpos - CPos.Zero, mapGridType, heights[cpos]);
			}

			return result;
		}
	}
}
