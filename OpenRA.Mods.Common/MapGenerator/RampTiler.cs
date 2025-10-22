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
		// public record struct HeightElement
		// {
		// 	public byte R;
		// 	public byte D;
		// 	public byte L;
		// 	public byte U;
		// 	public byte Height;
		// }

		public sealed class HeightMap
		{
			readonly Map map;
			public readonly Rectangle CellBounds;
			public readonly Matrix<byte> Target;
			public readonly Matrix<byte> LowerBound;
			public readonly Matrix<byte> UpperBound;
			public readonly Matrix<bool> Adjustable;
			public readonly CellLayer<bool> Tileable;

			public HeightMap(Map map)
			{
				this.map = map;
				CellBounds = CellLayerUtils.CellBounds(map);
				var size = CellBounds.Size.ToInt2() + new int2(1, 1);
				Target = new Matrix<byte>(size);
				LowerBound = new Matrix<byte>(size).Fill(byte.MinValue);
				UpperBound = new Matrix<byte>(size).Fill(byte.MaxValue);
				Adjustable = new Matrix<bool>(size).Fill(true);
				Tileable = new CellLayer<bool>(map);
				Tileable.Clear(true);

				for (var y = 0; y < size.Y; y++)
				{
					for (var x = 0; x < size.X; x++)
					{
						var inside =
							Tileable.Contains(XyToCPos(new int2(x, y))) ||
							Tileable.Contains(XyToCPos(new int2(x - 1, y))) ||
							Tileable.Contains(XyToCPos(new int2(x, y - 1))) ||
							Tileable.Contains(XyToCPos(new int2(x - 1, y - 1)));
						if (!inside)
						{
							LowerBound[x, y] = byte.MaxValue;
							UpperBound[x, y] = byte.MinValue;
							Adjustable[x, y] = false;
						}
					}
				}
			}

			HeightMap(
				Map map,
				Rectangle cellBounds,
				Matrix<byte> target,
				Matrix<byte> lowerBounds,
				Matrix<byte> upperBounds,
				Matrix<bool> adjustable,
				CellLayer<bool> tileable)
			{
				this.map = map;
				CellBounds = cellBounds;
				Target = target;
				LowerBound = lowerBounds;
				UpperBound = upperBounds;
				Adjustable = adjustable;
				Tileable = tileable;
			}

			public void SetHeights(Matrix<byte> heights)
			{
				if (heights.Size != Target.Size)
					throw new ArgumentException("heights matrix has wrong size");
				for (var i = 0; i < Target.Data.Length; i++)
					Target[i] = Adjustable[i] ? heights[i] : (byte)0;
			}

			public int2 CPosToXy(CPos cpos)
			{
				return new int2(cpos.X, cpos.Y) - CellBounds.TopLeft;
			}

			public CPos XyToCPos(int2 xy)
			{
				xy += CellBounds.TopLeft;
				return new CPos(xy.X, xy.Y);
			}

			public void MarkUntileable(CellLayer<bool> mask)
			{
				foreach (var cpos in mask.CellRegion)
					if (mask[cpos])
						MarkUntileable(cpos);
			}

			/// <summary>
			/// Updates the corner heights of given cells (each cell has 4 corners) according to what
			/// is currently in the map.
			/// </summary>
			public void MarkUntileable(IEnumerable<CPos> cells)
			{
				foreach (var cpos in cells)
					MarkUntileable(cpos);
			}

			/// <summary>
			/// Mark all corners of a CPos cell as untileable.
			/// </summary>
			/// <param name="cpos">Tile to commit. Must be within the map.</param>
			public void MarkUntileable(CPos cpos)
			{
				if (!Tileable.Contains(cpos))
					return;

				Tileable[cpos] = false;

				// var xy = CPosToXy(cpos);
				// Adjustable[xy] = false;

				// var tl = cpos;
				// var tr = cpos + new CVec(1, 0);
				// var br = cpos + new CVec(1, 1);
				// var bl = cpos + new CVec(0, 1);
				// var mtl = new int2(tl.X, tl.Y) - cellBounds.TopLeft;
				// var mtr = new int2(tr.X, tr.Y) - cellBounds.TopLeft;
				// var mbr = new int2(br.X, br.Y) - cellBounds.TopLeft;
				// var mbl = new int2(bl.X, bl.Y) - cellBounds.TopLeft;

				Adjustable[CPosToXy(cpos)] = false;
				Adjustable[CPosToXy(cpos + new CVec(1, 0))] = false;
				Adjustable[CPosToXy(cpos + new CVec(1, 1))] = false;
				Adjustable[CPosToXy(cpos + new CVec(0, 1))] = false;

				Target[CPosToXy(cpos)] = 0;
				Target[CPosToXy(cpos + new CVec(1, 0))] = 0;
				Target[CPosToXy(cpos + new CVec(1, 1))] = 0;
				Target[CPosToXy(cpos + new CVec(0, 1))] = 0;

				LowerBound[CPosToXy(cpos)] = byte.MaxValue;
				LowerBound[CPosToXy(cpos + new CVec(1, 0))] = byte.MaxValue;
				LowerBound[CPosToXy(cpos + new CVec(1, 1))] = byte.MaxValue;
				LowerBound[CPosToXy(cpos + new CVec(0, 1))] = byte.MaxValue;

				UpperBound[CPosToXy(cpos)] = 0;
				UpperBound[CPosToXy(cpos + new CVec(1, 0))] = 0;
				UpperBound[CPosToXy(cpos + new CVec(1, 1))] = 0;
				UpperBound[CPosToXy(cpos + new CVec(0, 1))] = 0;

				// var tl = heightMap.CPosToXy(cpos);
				// var tr = heightMap.CPosToXy(cpos + new CVec(1, 0));

				// if (cornerHeights.ContainsXY(mtl))
				// 	cornerHeights[mtl] = GetCornerHeightAt(tl);
				// if (cornerHeights.ContainsXY(mtr))
				// 	cornerHeights[mtr] = GetCornerHeightAt(tr);
				// if (cornerHeights.ContainsXY(mbr))
				// 	cornerHeights[mbr] = GetCornerHeightAt(br);
				// if (cornerHeights.ContainsXY(mbl))
				// 	cornerHeights[mbl] = GetCornerHeightAt(bl);
			}

			IEnumerable<(int2 XY, (byte Height, bool First) Prop)> FillSeeds(Matrix<byte> heights)
			{
				for (var y = 0; y < Adjustable.Size.Y; y++)
					for (var x = 0; x < Adjustable.Size.X; x++)
						if (Adjustable[x, y])
							yield return (new int2(x, y), (heights[x, y], true));
			}

			Matrix<byte> GetLowerHull(Matrix<byte> matrix)
			{
				(byte Lower, bool First)? Fill(int2 xy, (byte Lower, bool First) prop)
				{
					if (!prop.First && (!Adjustable[xy] || prop.Lower >= matrix[xy]))
						return null;

					matrix[xy] = prop.Lower;
					if (prop.Lower == byte.MaxValue)
						return null;

					return ((byte)(prop.Lower + 1), false);
				}

				MatrixUtils.FloodFill(
					matrix.Size,
					FillSeeds(matrix).OrderBy(s => s.Prop.Height),
					Fill,
					DirectionExts.Spread4);
				return matrix;
			}

			Matrix<byte> GetUpperHull(Matrix<byte> matrix)
			{
				(byte Upper, bool First)? Fill(int2 xy, (byte Upper, bool First) prop)
				{
					if (!prop.First && (!Adjustable[xy] || prop.Upper <= matrix[xy]))
						return null;

					matrix[xy] = prop.Upper;
					if (prop.Upper == byte.MinValue)
						return null;

					return ((byte)(prop.Upper - 1), false);
				}

				MatrixUtils.FloodFill(
					matrix.Size,
					FillSeeds(matrix).OrderByDescending(s => s.Prop.Height),
					Fill,
					DirectionExts.Spread4);
				return matrix;
			}

			public HeightMap Constrain(AdjustmentMode mode)
			{
				MatrixUtils.EnumDump2d("lower", LowerBound.Map(v => (int)v));
				MatrixUtils.EnumDump2d("upper", UpperBound.Map(v => (int)v));

				var forcedMaximum = GetLowerHull(UpperBound.Clone());
				var forcedMinimum = GetUpperHull(LowerBound.Clone());
				var constrained = Target.Clone();

				for (var y = 0; y < Target.Size.Y; y++)
				{
					for (var x = 0; x < Target.Size.X; x++)
					{
						if (!Adjustable[x, y])
							continue;

						if (forcedMinimum[x, y] > forcedMaximum[x, y])
							return null;
						else if (constrained[x, y] < forcedMinimum[x, y])
							constrained[x, y] = forcedMinimum[x, y];
						else if (constrained[x, y] > forcedMaximum[x, y])
							constrained[x, y] = forcedMaximum[x, y];
					}
				}

				switch (mode)
				{
					case AdjustmentMode.Minimal:
						constrained = GetLowerHull(constrained);
						break;
					case AdjustmentMode.LowerMiddle:
						constrained = Matrix<byte>.Zip(
							GetLowerHull(constrained.Clone()),
							GetUpperHull(constrained),
							(a, b) => (byte)((a + b) / 2));
						break;
					case AdjustmentMode.UpperMiddle:
						constrained = Matrix<byte>.Zip(
							GetLowerHull(constrained.Clone()),
							GetUpperHull(constrained),
							(a, b) => (byte)((a + b + 1) / 2));
						break;
					case AdjustmentMode.Maximal:
						constrained = GetUpperHull(constrained);
						break;
					default:
						throw new ArgumentException("invalid fitting mode");
				}

				return new HeightMap(
					map,
					CellBounds,
					constrained,
					LowerBound.Clone(),
					UpperBound.Clone(),
					Adjustable.Clone(),
					CellLayerUtils.Clone(Tileable));
			}

			// public void CommitXy(int2 xy, byte height)
			// {
			// 	Tileable[xy] = false;
			// 	LowerBound[xy] = height;
			// 	UpperBound[xy] = height;
			// }
		}

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

		record struct RampProperties
		{
			public MultiBrush[] Brushes;
			public int[] Weights;
			public byte Tl;
			public byte Tr;
			public byte Br;
			public byte Bl;

			public readonly byte GetCorner(Riser.Connection connection)
			{
				switch (connection)
				{
					case Riser.Connection.LU:
					case Riser.Connection.UL:
						return Tl;
					case Riser.Connection.UR:
					case Riser.Connection.RU:
						return Tr;
					case Riser.Connection.RD:
					case Riser.Connection.DR:
						return Br;
					case Riser.Connection.DL:
					case Riser.Connection.LD:
						return Bl;
				}

				throw new ArgumentException("invalid connection");
			}
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

		public byte GetConnectionHeight(byte height, TerrainTileInfo info, Riser.Connection connection)
		{
			var riser = info.Riser;
			var properties = rampProperties[info.RampType];
			var unclamped =
				riser[connection].HasValue
					? height + riser[connection].Value - info.Height
					: height + properties.GetCorner(connection);
			return (byte)Math.Clamp(unclamped, byte.MinValue, byte.MaxValue);
		}

		public void PullHeightMap(HeightMap heightMap)
		{
			foreach (var cpos in heightMap.Tileable.CellRegion)
				PullHeightMap(heightMap, cpos);
		}

		// public void PullHeightMap(HeightMap heightMap, CellLayer<bool> mask)
		// {
		// 	foreach (var cpos in mask.CellRegion)
		// 		if (mask[cpos])
		// 			PullHeightMap(heightMap, cpos);
		// }

		// public void PullHeightMap(HeightMap heightMap, IEnumerable<CPos> cells)
		// {
		// 	foreach (var cpos in cells)
		// 		PullHeightMap(heightMap, cpos);
		// }

		public void PullHeightMap(HeightMap heightMap, CPos cpos)
		{
			if (!heightMap.Tileable.Contains(cpos) || heightMap.Tileable[cpos])
				return;

			var height = map.Height[cpos];
			var info = map.Rules.TerrainInfo.GetTerrainInfo(map.Tiles[cpos]);
			// var properties = rampProperties[GetRampTypeAt(cpos)];
			// var riser = info.Riser;
			for (var i = 0; i < 8; i++)
			{
				var connection = (Riser.Connection)i;
				// var fromCVec = Riser.ConnectionFromCorner(connection);
				var toCVec = Riser.ConnectionToCorner(connection);
				// var fromCPos = cpos + fromCVec;
				var toCPos = cpos + toCVec;
				// var fromXy = heightMap.CPosToXy(fromCPos);
				var toXy = heightMap.CPosToXy(toCPos);
				if (!(heightMap.Adjustable.ContainsXY(toXy) && heightMap.Adjustable[toXy]))
					continue;

				// // Needed to prevent literal edge cases at the edge of the map.
				// if (!heightMap.Tileable.Contains(cpos + Riser.ConnectionToCell(connection)))
				// 	continue;

				var connectionHeight = GetConnectionHeight(height, info, connection);
				// var rampedHeight =
				// 	riser[connection].HasValue
				// 		? height + riser[connection].Value - info.Height
				// 		: height + properties.GetCorner(connection);

				var lower = Math.Clamp(connectionHeight - 1, byte.MinValue, byte.MaxValue);
				var upper = Math.Clamp(connectionHeight + 1, byte.MinValue, byte.MaxValue);
				// heightMap.LowerBound[fromXy] = (byte)Math.Max(heightMap.LowerBound[fromXy], connectionHeight);
				// heightMap.UpperBound[fromXy] = (byte)Math.Min(heightMap.UpperBound[fromXy], connectionHeight);
				heightMap.LowerBound[toXy] = (byte)Math.Max(heightMap.LowerBound[toXy], lower);
				heightMap.UpperBound[toXy] = (byte)Math.Min(heightMap.UpperBound[toXy], upper);
			}

				// var tl = cpos;
				// var tr = cpos + new CVec(1, 0);
				// var br = cpos + new CVec(1, 1);
				// var bl = cpos + new CVec(0, 1);
				// var mtl = new int2(tl.X, tl.Y) - cellBounds.TopLeft;
				// var mtr = new int2(tr.X, tr.Y) - cellBounds.TopLeft;
				// var mbr = new int2(br.X, br.Y) - cellBounds.TopLeft;
				// var mbl = new int2(bl.X, bl.Y) - cellBounds.TopLeft;
				// if (cornerHeights.ContainsXY(mtl))
				// 	cornerHeights[mtl] = GetCornerHeightAt(tl);
				// if (cornerHeights.ContainsXY(mtr))
				// 	cornerHeights[mtr] = GetCornerHeightAt(tr);
				// if (cornerHeights.ContainsXY(mbr))
				// 	cornerHeights[mbr] = GetCornerHeightAt(br);
				// if (cornerHeights.ContainsXY(mbl))
				// 	cornerHeights[mbl] = GetCornerHeightAt(bl);
			// }
		}

		// public void PullCornerHeightsForCellCorners(Matrix<byte> cornerHeights, IEnumerable<CPos> corners)
		// {
		// 	foreach (var cpos in corners)
		// 	{
		// 		var xy = new int2(cpos.X, cpos.Y) - cellBounds.TopLeft;
		// 		if (cornerHeights.ContainsXY(xy))
		// 			cornerHeights[xy] = GetCornerHeightAt(cpos);
		// 	}
		// }

		// public void PullCornerHeightsForMatrixCorners(Matrix<byte> cornerHeights, IEnumerable<int2> corners)
		// {
		// 	foreach (var xy in corners)
		// 	{
		// 		if (cornerHeights.ContainsXY(xy))
		// 			cornerHeights[xy] = GetCornerHeightAtMatrixXy(xy);
		// 	}
		// }

		// /// <summary>
		// /// Updates the masked corner heights according to what is currently in the map.
		// /// </summary>
		// public void PullUnmaskedCornerHeights(Matrix<byte> cornerHeights, Matrix<bool> mask)
		// {
		// 	for (var y = 0; y < cornerHeights.Size.Y; y++)
		// 		for (var x = 0; x < cornerHeights.Size.X; x++)
		// 			if (!(mask?[x, y] ?? false))
		// 				cornerHeights[x, y] = GetCornerHeightAtMatrixXy(new int2(x, y));
		// }

		// /// <summary>
		// /// Adjusts input cell corner heights such that all adjacent corners only have a height
		// /// difference of -1, 0, or 1.
		// /// </summary>
		// /// <param name="cornerHeights">Original corner heights.</param>
		// /// <param name="mask">Mask of corners that can be adjusted, or null if all can be adjusted.</param>
		// /// <param name="mode">Preferred direction to adjust heights.</param>
		// /// <returns>Adjusted corner heights, or null if there is no valid solution.</returns>
		// public Matrix<byte> ConstrainCornerHeights(
		// 	Matrix<byte> cornerHeights,
		// 	Matrix<bool> mask,
		// 	AdjustmentMode mode)
		// {
		// 	IEnumerable<(int2 XY, (byte Height, bool First) Prop)> MaskedSeeds()
		// 	{
		// 		for (var y = 0; y < cornerHeights.Size.Y; y++)
		// 			for (var x = 0; x < cornerHeights.Size.X; x++)
		// 				if (mask?[x, y] ?? true)
		// 					yield return (new int2(x, y), (cornerHeights[x, y], true));
		// 	}

		// 	IEnumerable<(int2 XY, (byte Height, bool First) Prop)> UnmaskedSeeds()
		// 	{
		// 		for (var y = 0; y < cornerHeights.Size.Y; y++)
		// 			for (var x = 0; x < cornerHeights.Size.X; x++)
		// 				if (!mask[x, y])
		// 					yield return (new int2(x, y), (cornerHeights[x, y], true));
		// 	}

		// 	Matrix<byte> GetMinimal(Matrix<byte> matrix, bool masked)
		// 	{
		// 		(byte Lower, bool First)? FillMinimal(int2 xy, (byte Lower, bool First) prop)
		// 		{
		// 			if (!prop.First && (!(mask?[xy] ?? true) || prop.Lower >= matrix[xy]))
		// 				return null;

		// 			matrix[xy] = prop.Lower;
		// 			if (prop.Lower == byte.MaxValue)
		// 				return null;

		// 			return ((byte)(prop.Lower + 1), false);
		// 		}

		// 		var seeds = masked ? MaskedSeeds() : UnmaskedSeeds();
		// 		MatrixUtils.FloodFill(
		// 			cornerHeights.Size,
		// 			seeds.OrderBy(s => s.Prop.Height),
		// 			FillMinimal,
		// 			DirectionExts.Spread4);
		// 		return matrix;
		// 	}

		// 	Matrix<byte> GetMaximal(Matrix<byte> matrix, bool masked)
		// 	{
		// 		(byte Upper, bool First)? FillMaximal(int2 xy, (byte Upper, bool First) prop)
		// 		{
		// 			if (!prop.First && (!(mask?[xy] ?? true) || prop.Upper <= matrix[xy]))
		// 				return null;

		// 			matrix[xy] = prop.Upper;
		// 			if (prop.Upper == byte.MinValue)
		// 				return null;

		// 			return ((byte)(prop.Upper - 1), false);
		// 		}

		// 		var seeds = masked ? MaskedSeeds() : UnmaskedSeeds();
		// 		MatrixUtils.FloodFill(
		// 			cornerHeights.Size,
		// 			seeds.OrderByDescending(s => s.Prop.Height),
		// 			FillMaximal,
		// 			DirectionExts.Spread4);
		// 		return matrix;
		// 	}

		// 	if (mask != null)
		// 	{
		// 		var floor = GetMaximal(new Matrix<byte>(cornerHeights.Size).Fill(byte.MinValue), false);
		// 		var ceiling = GetMinimal(new Matrix<byte>(cornerHeights.Size).Fill(byte.MaxValue), false);

		// 		cornerHeights = cornerHeights.Clone();
		// 		for (var y = 0; y < cornerHeights.Size.Y; y++)
		// 		{
		// 			for (var x = 0; x < cornerHeights.Size.X; x++)
		// 			{
		// 				if (!mask[x, y])
		// 					continue;

		// 				if (floor[x, y] > ceiling[x, y])
		// 					return null;
		// 				else if (cornerHeights[x, y] < floor[x, y])
		// 					cornerHeights[x, y] = floor[x, y];
		// 				else if (cornerHeights[x, y] > ceiling[x, y])
		// 					cornerHeights[x, y] = ceiling[x, y];
		// 			}
		// 		}
		// 	}

		// 	switch (mode)
		// 	{
		// 		case AdjustmentMode.Minimal:
		// 			return GetMinimal(cornerHeights.Clone(), true);
		// 		case AdjustmentMode.LowerMiddle:
		// 			return Matrix<byte>.Zip(
		// 				GetMinimal(cornerHeights.Clone(), true),
		// 				GetMaximal(cornerHeights.Clone(), true),
		// 				(a, b) => (byte)((a + b) / 2));
		// 		case AdjustmentMode.UpperMiddle:
		// 			return Matrix<byte>.Zip(
		// 				GetMinimal(cornerHeights.Clone(), true),
		// 				GetMaximal(cornerHeights.Clone(), true),
		// 				(a, b) => (byte)((a + b + 1) / 2));
		// 		case AdjustmentMode.Maximal:
		// 			return GetMaximal(cornerHeights.Clone(), true);
		// 		default:
		// 			throw new ArgumentException("invalid fitting mode");
		// 	}
		// }

		public (CellLayer<byte> Heights, CellLayer<byte> Ramps) GenerateRampsAndHeights(
			HeightMap heightMap,
			MersenneTwister random)
		{
			var tlCorners = new CellLayer<byte>(map);
			var trCorners = new CellLayer<byte>(map);
			var brCorners = new CellLayer<byte>(map);
			var blCorners = new CellLayer<byte>(map);

			var heights = CellLayerUtils.Clone(map.Height);

			// Map may not have ramps initialized. Don't clone.
			var ramps = new CellLayer<byte>(map);

			foreach (var cpos in heightMap.Tileable.CellRegion)
			{
				var height = map.Height[cpos];
				var info = map.Rules.TerrainInfo.GetTerrainInfo(map.Tiles[cpos]);
				ramps[cpos] = info.RampType;

				if (heightMap.Tileable[cpos])
				{
					var xy = heightMap.CPosToXy(cpos);
					var tl = xy;
					var tr = xy + new int2(1, 0);
					var br = xy + new int2(1, 1);
					var bl = xy + new int2(0, 1);
					if (heightMap.Adjustable[tl])
						tlCorners[cpos] = heightMap.Target[tl];

					if (heightMap.Adjustable[tr])
						trCorners[cpos] = heightMap.Target[tr];

					if (heightMap.Adjustable[br])
						brCorners[cpos] = heightMap.Target[br];

					if (heightMap.Adjustable[bl])
						blCorners[cpos] = heightMap.Target[bl];
				}
				else
				{
					var r = new CVec(1, 0);
					var d = new CVec(0, 1);
					var l = new CVec(-1, 0);
					var u = new CVec(0, -1);
					// var rCPos = cpos + new CVec(1, 0);
					// var dCPos = cpos + new CVec(0, 1);
					// var lCPos = cpos + new CVec(-1, 0);
					// var uCPos = cpos + new CVec(0, -1);

					if (heightMap.Tileable.Contains(cpos + r) && heightMap.Tileable[cpos + r])
					{
						tlCorners[cpos + r] = GetConnectionHeight(height, info, Riser.Connection.RU);
						blCorners[cpos + r] = GetConnectionHeight(height, info, Riser.Connection.RD);

						if (heightMap.Tileable.Contains(cpos + r + u) && heightMap.Tileable[cpos + r + u])
						{
							blCorners[cpos + r + u] = GetConnectionHeight(height, info, Riser.Connection.RU);
						}

						if (heightMap.Tileable.Contains(cpos + r + d) && heightMap.Tileable[cpos + r + d])
						{
							tlCorners[cpos + r + d] = GetConnectionHeight(height, info, Riser.Connection.RD);
						}
					}

					if (heightMap.Tileable.Contains(cpos + d) && heightMap.Tileable[cpos + d])
					{
						trCorners[cpos + d] = GetConnectionHeight(height, info, Riser.Connection.DR);
						tlCorners[cpos + d] = GetConnectionHeight(height, info, Riser.Connection.DL);

						if (heightMap.Tileable.Contains(cpos + d + r) && heightMap.Tileable[cpos + d + r])
						{
							tlCorners[cpos + d + r] = GetConnectionHeight(height, info, Riser.Connection.DR);
						}

						if (heightMap.Tileable.Contains(cpos + d + l) && heightMap.Tileable[cpos + d + l])
						{
							trCorners[cpos + d + l] = GetConnectionHeight(height, info, Riser.Connection.DL);
						}
					}

					if (heightMap.Tileable.Contains(cpos + l) && heightMap.Tileable[cpos + l])
					{
						brCorners[cpos + l] = GetConnectionHeight(height, info, Riser.Connection.LD);
						trCorners[cpos + l] = GetConnectionHeight(height, info, Riser.Connection.LU);

						if (heightMap.Tileable.Contains(cpos + l + d) && heightMap.Tileable[cpos + l + d])
						{
							trCorners[cpos + l + d] = GetConnectionHeight(height, info, Riser.Connection.LD);
						}

						if (heightMap.Tileable.Contains(cpos + l + u) && heightMap.Tileable[cpos + l + u])
						{
							brCorners[cpos + l + u] = GetConnectionHeight(height, info, Riser.Connection.LU);
						}
					}

					if (heightMap.Tileable.Contains(cpos + u) && heightMap.Tileable[cpos + u])
					{
						blCorners[cpos + u] = GetConnectionHeight(height, info, Riser.Connection.UL);
						brCorners[cpos + u] = GetConnectionHeight(height, info, Riser.Connection.UR);

						if (heightMap.Tileable.Contains(cpos + u + l) && heightMap.Tileable[cpos + u + l])
						{
							brCorners[cpos + u + l] = GetConnectionHeight(height, info, Riser.Connection.UL);
						}

						if (heightMap.Tileable.Contains(cpos + u + r) && heightMap.Tileable[cpos + u + r])
						{
							blCorners[cpos + u + r] = GetConnectionHeight(height, info, Riser.Connection.UR);
						}
					}
				}
			}

			foreach (var cpos in heightMap.Tileable.CellRegion)
			{
				if (!heightMap.Tileable[cpos])
					continue;

				var tl = tlCorners[cpos];
				var tr = trCorners[cpos];
				var br = brCorners[cpos];
				var bl = blCorners[cpos];

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

			// // TODO: ensure map shape consistency (or just use grid).
			// var masked =
			// 	mask != null
			// 		? mask.CellRegion.Where(cpos => mask[cpos])
			// 		: map.Tiles.CellRegion;
			// foreach (var cpos in masked)
			// {
			// 	var x = cpos.X - cellBounds.X;
			// 	var y = cpos.Y - cellBounds.Y;
			// 	var tl = cornerHeights[x, y];
			// 	var tr = cornerHeights[x + 1, y];
			// 	var br = cornerHeights[x + 1, y + 1];
			// 	var bl = cornerHeights[x, y + 1];

			// 	var baseHeight = Math.Min(Math.Min(tl, tr), Math.Min(bl, br));
			// 	tl -= baseHeight;
			// 	tr -= baseHeight;
			// 	br -= baseHeight;
			// 	bl -= baseHeight;
			// 	if (Math.Abs(tl - tr) > 1 ||
			// 		Math.Abs(tr - br) > 1 ||
			// 		Math.Abs(br - bl) > 1 ||
			// 		Math.Abs(bl - tl) > 1)
			// 	{
			// 		throw new ArgumentException("cornerHeights has adjacent cell corners with a height difference > 1");
			// 	}

			// 	var lookup = tl | (tr << 2) | (br << 4) | (bl << 6);
			// 	if (!rampLookup.TryGetValue(lookup, out var validRamps))
			// 		return (null, null);

			// 	heights[cpos] = baseHeight;
			// 	ramps[cpos] =
			// 		validRamps.Count == 1
			// 			? validRamps[0]
			// 			: validRamps[random.Next() % validRamps.Count];
			// }

			return (heights, ramps);
		}

		// /// <summary>Wrapper around CornersToRampsAndHeights and Tile.</summary>
		// public MultiBrush TileCorners(Matrix<byte> cornerHeights, CellLayer<bool> mask, MersenneTwister random)
		// {
		// 	var (heights, ramps) = GenerateRampsAndHeights(cornerHeights, mask, random);
		// 	if (heights == null)
		// 		return null;

		// 	return Tile(heights, ramps, mask, random);
		// }

		/// <summary>Wrapper around CornersToRampsAndHeights and Tile.</summary>
		public MultiBrush TileHeightMap(HeightMap heightMap, MersenneTwister random)
		{
			// var mask = new CellLayer<bool>(map);
			// CellLayerUtils.FromMatrix(
			// 	mask,
			// 	MatrixUtils.KernelAggregate(
			// 		heightMap.Adjustable,
			// 		new Matrix<bool>(cellBounds.Size.ToInt2()),
			// 		new int2(2, 2),
			// 		new int2(0, 0),
			// 		submatrix => submatrix.Data.Any(v => v)));
			var (heights, ramps) = GenerateRampsAndHeights(heightMap, random);
			if (heights == null)
				return null;

			return Tile(heights, ramps, heightMap.Tileable, random);
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
