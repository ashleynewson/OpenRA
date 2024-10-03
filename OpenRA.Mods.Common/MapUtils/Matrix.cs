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
using System.Diagnostics;
using OpenRA.Support;

namespace OpenRA.Mods.Common.MapUtils
{
	// <summary>
	// A fixed-size 2D array that can be indexed either linearly or by coordinates.
	// </summary>
	public sealed class Matrix<T>
	{
		// <summary>Underlying matrix data.</summary>
		public readonly T[] Data;
		// <summary>Matrix dimensions.</summary>
		public readonly int2 Size;

		Matrix(int2 size, T[] data)
		{
			Data = data;
			Size = size;
		}

		// <summary>Create a new matrix with the given size.</summary>
		public Matrix(int2 size)
			: this(size, new T[size.X * size.Y])
		{ }

		// <summary>Create a new matrix with the given size.</summary>
		public Matrix(int x, int y)
			: this(new int2(x, y))
		{ }

		// <summary>
		// Convert a pair of coordinates into an index into Data.
		// </summary>
		public int Index(int2 xy)
			=> Index(xy.X, xy.Y);

		// <summary>
		// Convert a pair of coordinates into an index into Data.
		// </summary>
		public int Index(int x, int y)
		{
			Debug.Assert(ContainsXY(x, y), $"({x}, {y}) is out of bounds for a matrix of size ({Size.X}, {Size.Y})");
			return y * Size.X + x;
		}

		// <summary>
		// Convert an index into Data into a pair of coordinates.
		// </summary>
		public int2 XY(int index)
		{
			var y = index / Size.X;
			var x = index % Size.X;
			return new int2(x, y);
		}

		public T this[int x, int y]
		{
			get => Data[Index(x, y)];
			set => Data[Index(x, y)] = value;
		}

		public T this[int2 xy]
		{
			get => Data[Index(xy.X, xy.Y)];
			set => Data[Index(xy.X, xy.Y)] = value;
		}

		// <summary>Shorthand for Data[i].</summary>
		public T this[int i]
		{
			get => Data[i];
			set => Data[i] = value;
		}

		// <summary>True iff xy is a valid index within the matrix.</summary>
		public bool ContainsXY(int2 xy)
		{
			return xy.X >= 0 && xy.X < Size.X && xy.Y >= 0 && xy.Y < Size.Y;
		}

		// <summary>True iff (x, y) is a valid index within the matrix.</summary>
		public bool ContainsXY(int x, int y)
		{
			return x >= 0 && x < Size.X && y >= 0 && y < Size.Y;
		}

		// <summary>Clamp xy to be the closest index within the matrix.</summary>
		public int2 ClampXY(int2 xy)
		{
			var (nx, ny) = ClampXY(xy.X, xy.Y);
			return new int2(nx, ny);
		}

		// <summary>Clamp (x, y) to be the closest index within the matrix.</summary>
		public (int Nx, int Ny) ClampXY(int x, int y)
		{
			if (x >= Size.X) x = Size.X - 1;
			if (x < 0) x = 0;
			if (y >= Size.Y) y = Size.Y - 1;
			if (y < 0) y = 0;
			return (x, y);
		}

		// <summary>
		// Creates a transposed (shallow) copy of the matrix.
		// <summary>
		public Matrix<T> Transpose()
		{
			var transposed = new Matrix<T>(new int2(Size.Y, Size.X));
			for (var y = 0; y < Size.Y; y++)
			{
				for (var x = 0; x < Size.X; x++)
				{
					transposed[y, x] = this[x, y];
				}
			}

			return transposed;
		}

		// <summary>
		// Return a new matrix with the same shape as this one containing the values after being
		// transformed by a mapping func.
		// </summary>
		public Matrix<R> Map<R>(Func<T, R> func)
		{
			var mapped = new Matrix<R>(Size);
			for (var i = 0; i < Data.Length; i++)
			{
				mapped.Data[i] = func(Data[i]);
			}

			return mapped;
		}

		// <summary>
		// Apply func to each value in the matrix in place, returning this.
		// </summary>
		public Matrix<T> Foreach(Func<T, T> func)
		{
			for (var i = 0; i < Data.Length; i++)
			{
				Data[i] = func(Data[i]);
			}

			return this;
		}

		// <summary>
		// Replace all values in the matrix with a given value. Returns this.
		// </summary>
		public Matrix<T> Fill(T value)
		{
			Array.Fill(Data, value);
			return this;
		}

		// <summary>
		// Return a shallow clone of this matrix
		// </summary>
		public Matrix<T> Clone()
		{
			return new Matrix<T>(Size, (T[])Data.Clone());
		}

		// <summary>
		// Combine two same-shape matrices into a new output matrix.
		// The zipping function specifies how values are combined.
		// </summary>
		public static Matrix<T> Zip<T1, T2>(Matrix<T1> a, Matrix<T2> b, Func<T1, T2, T> func)
		{
			if (a.Size != b.Size)
				throw new ArgumentException("Input matrices to FromZip must match in shape and size");
			var matrix = new Matrix<T>(a.Size);
			for (var i = 0; i < a.Data.Length; i++)
				matrix.Data[i] = func(a.Data[i], b.Data[i]);
			return matrix;
		}

		// <summary>
		// Draw (update values within) a circle of given center and radius.
		// The values are updated based on the setTo function, (radiusSquared, oldValue) => newValue.
		// If invert is true, values outside of the circle are updated instead.
		//
		// A matrix cell is inside the circle if its distance from the center is <= radius.
		// Coordinates outside of the matrix are ignored.
		// </summary>
		public void DrawCircle(float2 center, float radius, Func<float, T, T> setTo, bool invert)
		{
			int minX;
			int minY;
			int maxX;
			int maxY;
			if (invert)
			{
				minX = 0;
				minY = 0;
				maxX = Size.X - 1;
				maxY = Size.Y - 1;
			}
			else
			{
				minX = (int)MathF.Floor(center.X - radius);
				minY = (int)MathF.Floor(center.Y - radius);
				maxX = (int)MathF.Ceiling(center.X + radius);
				maxY = (int)MathF.Ceiling(center.Y + radius);
				if (minX < 0)
					minX = 0;
				if (minY < 0)
					minY = 0;
				if (maxX >= Size.X)
					maxX = Size.X - 1;
				if (maxY >= Size.Y)
					maxY = Size.Y - 1;
			}

			var radiusSquared = radius * radius;
			for (var y = minY; y <= maxY; y++)
			{
				for (var x = minX; x <= maxX; x++)
				{
					var rx = x - center.X;
					var ry = y - center.Y;
					var thisRadiusSquared = rx * rx + ry * ry;
					if (rx * rx + ry * ry <= radiusSquared != invert)
						this[x, y] = setTo(thisRadiusSquared, this[x, y]);
				}
			}
		}

		// <summary>
		// Rank all matrix values and select the best (greatest compared) value.
		// If there are equally good best candidates, choose one at random.
		// </summary>
		public (int2 XY, T Value) FindRandomBest(MersenneTwister random, Comparison<T> comparison)
		{
			var candidates = new List<int>();
			var best = this[0];
			for (var n = 0; n < Data.Length; n++)
			{
				int rank = comparison(this[n], best);
				if (rank > 0)
				{
					best = this[n];
					candidates.Clear();
				}

				if (rank >= 0)
					candidates.Add(n);
			}
			var choice = candidates[random.Next(candidates.Count)];
			var xy = XY(choice);
			return (xy, best);
		}

	}
}
