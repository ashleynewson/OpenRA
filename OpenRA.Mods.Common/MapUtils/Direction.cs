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
using System.Collections.Immutable;

namespace OpenRA.Mods.Common.MapUtils
{
	public static class Direction
	{
		// <summary>No direction</summary>
		public const int NONE = -1;
		// <summary>Right, 0 degrees</summary>
		public const int R = 0;
		// <summary>Right-down, 45 degrees</summary>
		public const int RD = 1;
		// <summary>Down, 90 degrees</summary>
		public const int D = 2;
		// <summary>Left-down, 135 degrees</summary>
		public const int LD = 3;
		// <summary>Left, 180 degrees</summary>
		public const int L = 4;
		// <summary>Left-up, 225 degrees</summary>
		public const int LU = 5;
		// <summary>Up, 270 degrees</summary>
		public const int U = 6;
		// <summary>Right-up, 315 degrees</summary>
		public const int RU = 7;

		// <summary>Bitmask right</summary>
		public const int M_R = 1 << R;
		// <summary>Bitmask right-down</summary>
		public const int M_RD = 1 << RD;
		// <summary>Bitmask down</summary>
		public const int M_D = 1 << D;
		// <summary>Bitmask left-down</summary>
		public const int M_LD = 1 << LD;
		// <summary>Bitmask left</summary>
		public const int M_L = 1 << L;
		// <summary>Bitmask left-up</summary>
		public const int M_LU = 1 << LU;
		// <summary>Bitmask up</summary>
		public const int M_U = 1 << U;
		// <summary>Bitmask right-up</summary>
		public const int M_RU = 1 << RU;

		// <summary>Adjacent offsets, excluding diagonals</summary>
		public static readonly ImmutableArray<int2> SPREAD4 = ImmutableArray.Create(new[]
		{
			new int2(1, 0),
			new int2(0, 1),
			new int2(-1, 0),
			new int2(0, -1)
		});

		// <summary>Adjacent offsets with directions, excluding diagonals</summary>
		public static readonly ImmutableArray<(int2, int)> SPREAD4_D = ImmutableArray.Create(new[]
		{
			(new int2(1, 0), R),
			(new int2(0, 1), D),
			(new int2(-1, 0), L),
			(new int2(0, -1), U)
		});

		// <summary>Adjacent offsets, including diagonals</summary>
		public static readonly ImmutableArray<int2> SPREAD8 = ImmutableArray.Create(new[]
		{
			new int2(1, 0),
			new int2(1, 1),
			new int2(0, 1),
			new int2(-1, 1),
			new int2(-1, 0),
			new int2(-1, -1),
			new int2(0, -1),
			new int2(1, -1)
		});

		// <summary>Adjacent offsets with directions, including diagonals</summary>
		public static readonly ImmutableArray<(int2, int)> SPREAD8_D = ImmutableArray.Create(new[]
		{
			(new int2(1, 0), R),
			(new int2(1, 1), RD),
			(new int2(0, 1), D),
			(new int2(-1, 1), LD),
			(new int2(-1, 0), L),
			(new int2(-1, -1), LU),
			(new int2(0, -1), U),
			(new int2(1, -1), RU)
		});

		// <summary>Convert a non-none direction to a offset.</summary>
		public static int2 ToOffset(int d)
		{
			if (d >= 0 && d < 8)
				return SPREAD8[d];
			else
				throw new ArgumentException("bad direction");
		}

		// <summary>
		// Convert an offset (of arbitrary non-zero magnitude) to a direction.
		// Supplying a zero-offset will throw.
		// </summary>
		public static int FromOffset(int dx, int dy)
		{
			if (dx > 0)
			{
				if (dy > 0)
					return RD;
				else if (dy < 0)
					return RU;
				else
					return R;
			}
			else if (dx < 0)
			{
				if (dy > 0)
					return LD;
				else if (dy < 0)
					return LU;
				else
					return L;
			}
			else
			{
				if (dy > 0)
					return D;
				else if (dy < 0)
					return U;
				else
					throw new ArgumentException("Bad direction");
			}
		}

		// <summary>
		// Convert an offset (of arbitrary non-zero magnitude) to a direction.
		// Supplying a zero-offset will throw.
		// </summary>
		public static int FromOffset(int2 delta)
			=> FromOffset(delta.X, delta.Y);

		// <summary>
		// Convert an offset (of arbitrary non-zero magnitude) to a non-diagonal direction.
		// Supplying a zero-offset will throw.
		// </summary>
		public static int FromOffsetNonDiagonal(int dx, int dy)
		{
			if (dx - dy > 0 && dx + dy >= 0)
				return R;
			if (dy + dx > 0 && dy - dx >= 0)
				return D;
			if (-dx + dy > 0 && -dx - dy >= 0)
				return L;
			if (-dy - dx > 0 && -dy + dx >= 0)
				return U;
			throw new ArgumentException("bad direction");
		}

		// <summary>
		// Convert an offset (of arbitrary non-zero magnitude) to a non-diagonal direction.
		// Supplying a zero-offset will throw.
		// </summary>
		public static int FromOffsetNonDiagonal(int2 delta)
			=> FromOffsetNonDiagonal(delta.X, delta.Y);

		// <summary>Return the opposite direction.</summary>
		public static int Reverse(int direction)
		{
			if (direction == NONE)
				return NONE;
			return direction ^ 4;
		}

		// <summary>Convert a direction to a short string, like "None", "R", "RD", etc.</summary>
		public static string ToString(int direction)
		{
			switch (direction)
			{
				case NONE: return "None";
				case R: return "R";
				case RD: return "RD";
				case D: return "D";
				case LD: return "LD";
				case L: return "L";
				case LU: return "LU";
				case U: return "U";
				case RU: return "RU";
				default: throw new ArgumentException("bad direction");
			}
		}

		// <summary>Count the number of set bits in a direction mask.</summary>
		public static int Count(int dm)
		{
			var count = 0;
			for (var m = dm; m != 0; m >>= 1)
			{
				if ((m & 1) == 1)
					count++;
			}

			return count;
		}

		// <summary>Finds the only direction set in a direction mask or returns NONE.</summary>
		public static int FromMask(int mask)
		{
			switch (mask)
			{
				case M_R: return R;
				case M_RD: return RD;
				case M_D: return D;
				case M_LD: return LD;
				case M_L: return L;
				case M_LU: return LU;
				case M_U: return U;
				case M_RU: return RU;
				default: return NONE;
			}
		}
	}
}
