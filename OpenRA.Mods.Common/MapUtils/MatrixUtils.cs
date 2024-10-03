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
using System.Globalization;
using System.Linq;

namespace OpenRA.Mods.Common.MapUtils
{
	public static class MatrixUtils
	{
		// <summary>
		// Debugging method that prints a matrix to stderr.
		// </summary>
		public static void Dump2d(string label, Matrix<bool> matrix)
		{
			Console.Error.WriteLine($"{label}:");
			for (var y = 0; y < matrix.Size.Y; y++)
			{
				for (var x = 0; x < matrix.Size.X; x++)
				{
					Console.Error.Write(matrix[x, y] ? "\u001b[0;42m .\u001b[m" : "\u001b[m .");
				}

				Console.Error.Write("\n");
			}

			Console.Error.WriteLine("");
			Console.Error.Flush();
		}

		// <summary>
		// Debugging method that prints a matrix to stderr.
		// </summary>
		public static void Dump2d(string label, Matrix<int> matrix)
		{
			Console.Error.WriteLine($"{label}: {matrix.Size.X} by {matrix.Size.Y}, {matrix.Data.Min()} to {matrix.Data.Max()}");
			for (var y = 0; y < matrix.Size.Y; y++)
			{
				for (var x = 0; x < matrix.Size.X; x++)
				{
					var v = matrix[x, y];
					string formatted;
					if (v > 0)
						formatted = string.Format(NumberFormatInfo.InvariantInfo, "\u001b[1;42m{0:X8}\u001b[m ", v);
					else if (v < 0)
						formatted = string.Format(NumberFormatInfo.InvariantInfo, "\u001b[1;41m{0:X8}\u001b[m ", v);
					else
						formatted = "\u001b[m       0 ";
					Console.Error.Write(formatted);
				}

				Console.Error.Write("\n");
			}

			Console.Error.WriteLine("");
			Console.Error.Flush();
		}

		// <summary>
		// Debugging method that prints a matrix to stderr.
		// </summary>
		public static void Dump2d(string label, Matrix<byte> matrix)
		{
			Console.Error.WriteLine($"{label}: {matrix.Size.X} by {matrix.Size.Y}, {matrix.Data.Min()} to {matrix.Data.Max()}");
			for (var y = 0; y < matrix.Size.Y; y++)
			{
				for (var x = 0; x < matrix.Size.X; x++)
				{
					var v = matrix[x, y];
					string formatted;
					if (v > 0 && v < 0x80)
						formatted = string.Format(NumberFormatInfo.InvariantInfo, "\u001b[1;42m{0:X2}\u001b[m ", v);
					else if (v >= 0x80)
						formatted = string.Format(NumberFormatInfo.InvariantInfo, "\u001b[1;41m{0:X2}\u001b[m ", v);
					else
						formatted = "\u001b[m 0 ";
					Console.Error.Write(formatted);
				}

				Console.Error.Write("\n");
			}

			Console.Error.WriteLine("");
			Console.Error.Flush();
		}

		// Perform a generic flood fill starting at seeds [(xy, prop, d), ...].
		//
		// The prop (propagation value) and d (propagation direction) values of
		// the seed are optional.
		//
		// For each point being considered for fill, filler(xy, prop, d) is
		// called with the current position (xy), propagation value (prop),
		// and propagation direction (d). filler should return the value to be
		// propagated or null if not to be propagated. Propagation happens to
		// all non-diagonally adjacent neighbours, regardless of whether they
		// have previously been visited, so filler is responsible for
		// terminating propagation.
		//
		// The spread argument defines the propagation pattern from a point.
		// Usually, Direction.SPREAD4_D is appropriate.
		//
		// filler should capture and manipulate any necessary input and output
		// arrays.
		//
		// Each call to filler will have either an equal or greater
		// growth/propagation distance from their seed value than all calls
		// before it. (You can think of this as them being called in ordered
		// growth layers.)
		//
		// Note that filler may be called multiple times for the same spot,
		// perhaps with different propagation values. Within the same
		// growth/propagation distance, filler will be called from values
		// propagated from earlier seeds before values propagated from later
		// seeds.
		//
		// filler is not called for positions outside of the bounds defined by
		// size EXCEPT for points being processed as seed values.
		public static void FloodFill<P>(
			int2 size,
			IEnumerable<(int2 XY, P Prop, int D)> seeds,
			Func<int2, P, int, P?> filler,
			ImmutableArray<(int2 Offset, int Direction)> spread) where P : struct
		{
			var next = seeds.ToList();
			while (next.Count != 0)
			{
				var current = next;
				next = new List<(int2, P, int)>();
				foreach (var (source, prop, d) in current)
				{
					var newProp = filler(source, prop, d);
					if (newProp != null)
					{
						foreach (var (offset, direction) in spread)
						{
							var destination = source + offset;
							if (destination.X < 0 || destination.X >= size.X || destination.Y < 0 || destination.Y >= size.Y)
								continue;

							next.Add((destination, (P)newProp, direction));
						}
					}
				}
			}
		}

		// <summary>
		// Shrinkwraps true space to be as far away from false space as possible, preserving
		// topology. The result is a kind of rough Voronoi diagram.
		//
		// If the space matrix has width (w, h), the returned matrix will have width (w + 1, h + 1).
		// Each value in the returned matrix is a Direction bitmask describing the border structure
		// between the cells of the original space matrix.
		// </summary>
		public static Matrix<byte> DeflateSpace(Matrix<bool> space, bool outsideIsHole)
		{
			var size = space.Size;
			var holes = new Matrix<int>(size);
			var holeCount = 0;
			for (var y = 0; y < space.Size.Y; y++)
			{
				for (var x = 0; x < space.Size.X; x++)
				{
					if (!space[x, y] && holes[x, y] == 0)
					{
						holeCount++;
						int? Filler(int2 xy, int holeId, int direction)
						{
							if (!space[xy] && holes[xy] == 0)
							{
								holes[xy] = holeId;
								return holeId;
							}
							else
							{
								return null;
							}
						}

						MatrixUtils.FloodFill(space.Size, new[] { (new int2(x, y), holeCount, Direction.NONE) }, Filler, Direction.SPREAD4_D);
					}
				}
			}

			const int UNASSIGNED = int.MaxValue;
			var voronoi = new Matrix<int>(size);
			var distances = new Matrix<int>(size).Fill(UNASSIGNED);
			var closestN = new Matrix<int>(size).Fill(UNASSIGNED);
			var midN = (size.X * size.Y + 1) / 2;
			var seeds = new List<(int2, (int, int2, int), int)>();
			for (var y = 0; y < size.Y; y++)
			{
				for (var x = 0; x < size.X; x++)
				{
					var xy = new int2(x, y);
					if (holes[xy] != 0)
						seeds.Add((xy, (holes[xy], xy, closestN.Index(x, y)), Direction.NONE));
				}
			}

			if (outsideIsHole)
			{
				holeCount++;
				for (var x = 0; x < size.X; x++)
				{
					// Hack: closestN is actually inside, but starting x, y are outside.
					seeds.Add((new int2(x, 0), (holeCount, new int2(x, -1), closestN.Index(x, 0)), Direction.NONE));
					seeds.Add((new int2(x, size.Y - 1), (holeCount, new int2(x, size.Y), closestN.Index(x, size.Y - 1)), Direction.NONE));
				}

				for (var y = 0; y < size.Y; y++)
				{
					// Hack: closestN is actually inside, but starting x, y are outside.
					seeds.Add((new int2(0, y), (holeCount, new int2(-1, y), closestN.Index(0, y)), Direction.NONE));
					seeds.Add((new int2(size.X - 1, y), (holeCount, new int2(size.X, y), closestN.Index(size.X - 1, y)), Direction.NONE));
				}
			}

			{
				(int HoleId, int2 StartXY, int StartN)? Filler(int2 xy, (int HoleId, int2 StartXY, int StartN) prop, int direction)
				{
					var n = closestN.Index(xy);
					var distance = (xy - prop.StartXY).LengthSquared;
					if (distance < distances[n])
					{
						voronoi[n] = prop.HoleId;
						distances[n] = distance;
						closestN[n] = prop.StartN;
						return (prop.HoleId, prop.StartXY, prop.StartN);
					}
					else if (distance == distances[n])
					{
						if (closestN[n] == prop.StartN)
						{
							return null;
						}
						else if (n <= midN == prop.StartN < closestN[n])
						{
							// For the first half of the map, lower seed indexes are preferred.
							// For the second half of the map, higher seed indexes are preferred.
							voronoi[n] = prop.HoleId;
							closestN[n] = prop.StartN;
							return (prop.HoleId, prop.StartXY, prop.StartN);
						}
						else
						{
							return null;
						}
					}
					else
					{
						return null;
					}
				}

				MatrixUtils.FloodFill(size, seeds, Filler, Direction.SPREAD4_D);
			}

			var deflatedSize = size + new int2(1, 1);
			var deflated = new Matrix<byte>(deflatedSize);
			var neighborhood = new int[4];
			var scan = new int2[]
			{
				new(-1, -1),
				new(0, -1),
				new(-1, 0),
				new(0, 0)
			};
			for (var cy = 0; cy < deflatedSize.Y; cy++)
			{
				for (var cx = 0; cx < deflatedSize.X; cx++)
				{
					for (var neighbor = 0; neighbor < 4; neighbor++)
					{
						var x = Math.Clamp(cx + scan[neighbor].X, 0, size.X - 1);
						var y = Math.Clamp(cy + scan[neighbor].Y, 0, size.Y - 1);
						neighborhood[neighbor] = voronoi[x, y];
					}

					deflated[cx, cy] = (byte)(
						(neighborhood[0] != neighborhood[1] ? Direction.M_U : 0) |
						(neighborhood[1] != neighborhood[3] ? Direction.M_R : 0) |
						(neighborhood[3] != neighborhood[2] ? Direction.M_D : 0) |
						(neighborhood[2] != neighborhood[0] ? Direction.M_L : 0));
				}
			}

			return deflated;
		}

		// <summary>
		// Convolute a kernel over a boolean input matrix.
		// If dilating, the values specified by the kernel are logically OR-ed.
		// If eroding, the values specified by the kernel are logically AND-ed.
		// </summary>
		public static Matrix<bool> KernelDilateOrErode(Matrix<bool> input, Matrix<bool> kernel, int2 kernelCenter, bool dilate)
		{
			var output = new Matrix<bool>(input.Size).Fill(!dilate);
			for (var cy = 0; cy < input.Size.Y; cy++)
			{
				for (var cx = 0; cx < input.Size.X; cx++)
				{
					void InnerLoop()
					{
						for (var ky = 0; ky < kernel.Size.Y; ky++)
						{
							for (var kx = 0; kx < kernel.Size.X; kx++)
							{
								var x = cx + kx - kernelCenter.X;
								var y = cy + ky - kernelCenter.Y;
								if (!input.ContainsXY(x, y))
									continue;
								if (kernel[kx, ky] && input[x, y] == dilate)
								{
									output[cx, cy] = dilate;
									return;
								}
							}
						}
					}

					InnerLoop();
				}
			}

			return output;
		}

		// TODO: Add a convenience method for doing a 2D gaussian blur.
		// <summary>
		// Create a one-dimensional gaussian kernel.
		//
		// This can be applied once, transposed, then applied again to perform a full gaussian blur.
		// </summary>
		public static Matrix<float> GaussianKernel1D(int radius, float standardDeviation)
		{
			var span = radius * 2 + 1;
			var kernel = new Matrix<float>(new int2(span, 1));
			var dsd2 = 2 * standardDeviation * standardDeviation;
			var total = 0.0f;
			for (var x = -radius; x <= radius; x++)
			{
				var value = MathF.Exp(-x * x / dsd2);
				kernel[x + radius] = value;
				total += value;
			}

			// Instead of dividing by sqrt(PI * dsd2), divide by the total.
			for (var i = 0; i < span; i++)
			{
				kernel[i] /= total;
			}

			return kernel;
		}

		// <summary>
		// Apply an arithmetic convolution of a kernel over an input matrix.
		// </summary>
		public static Matrix<float> KernelBlur(Matrix<float> input, Matrix<float> kernel, int2 kernelCenter)
		{
			var output = new Matrix<float>(input.Size);
			for (var cy = 0; cy < input.Size.Y; cy++)
			{
				for (var cx = 0; cx < input.Size.X; cx++)
				{
					var total = 0.0f;
					var samples = 0;
					for (var ky = 0; ky < kernel.Size.Y; ky++)
					{
						for (var kx = 0; kx < kernel.Size.X; kx++)
						{
							var x = cx + kx - kernelCenter.X;
							var y = cy + ky - kernelCenter.Y;
							if (!input.ContainsXY(x, y)) continue;
							total += input[x, y] * kernel[kx, ky];
							samples++;
						}
					}

					output[cx, cy] = total / samples;
				}
			}

			return output;
		}

		// <summary>
		// Apply a square gaussian blur to a matrix, returning a new matrix.
		// </summary>
		public static Matrix<float> GaussianBlur(Matrix<float> input, int radius, float standardDeviation)
		{
			var kernel = MatrixUtils.GaussianKernel1D(radius, standardDeviation);
			var stage1 = MatrixUtils.KernelBlur(input, kernel, new int2(radius, 0));
			var stage2 = MatrixUtils.KernelBlur(stage1, kernel.Transpose(), new int2(0, radius));
			return stage2;
		}

		// TODO: Refactor zoning radius out of this.
		// <summary>
		// Set positions occupied by entities to a given value, accounting for both their footprint
		// and zoning radius.
		// </summary>
		public static void ReserveForEntitiesInPlace<T>(Matrix<T> matrix, IEnumerable<ActorPlan> actorPlans, Func<T, T> setTo)
		{
			foreach (var actorPlan in actorPlans)
			{
				foreach (var (cpos, _) in actorPlan.Footprint())
				{
					var mpos = cpos.ToMPos(actorPlan.Map);
					var xy = new int2(mpos.U, mpos.V);
					if (matrix.ContainsXY(xy))
						matrix[xy] = setTo(matrix[xy]);
				}

				if (actorPlan.ZoningRadius > 0.0f)
					matrix.DrawCircle(
						center: actorPlan.Int2Location,
						radius: actorPlan.ZoningRadius,
						setTo: (_, v) => setTo(v),
						invert: false);
			}
		}

		// TODO: Improve documentation
		// <summary>
		// Finds the local variance of points in a grid (using a square sample area).
		// Sample areas are centered on data point corners, so output is (size + 1) * (size + 1).
		// </summary>
		public static Matrix<float> GridVariance(Matrix<float> input, int radius)
		{
			var output = new Matrix<float>(input.Size + new int2(1, 1));
			for (var cy = 0; cy < output.Size.Y; cy++)
			{
				for (var cx = 0; cx < output.Size.X; cx++)
				{
					var total = 0.0f;
					var samples = 0;
					for (var ry = -radius; ry < radius; ry++)
					{
						for (var rx = -radius; rx < radius; rx++)
						{
							var y = cy + ry;
							var x = cx + rx;
							if (!input.ContainsXY(x, y))
								continue;
							total += input[x, y];
							samples++;
						}
					}

					var mean = total / samples;
					var sumOfSquares = 0.0f;
					for (var ry = -radius; ry < radius; ry++)
					{
						for (var rx = -radius; rx < radius; rx++)
						{
							var y = cy + ry;
							var x = cx + rx;
							if (!input.ContainsXY(x, y))
								continue;
							sumOfSquares += MathF.Pow(mean - input[x, y], 2);
						}
					}

					output[cx, cy] = sumOfSquares / samples;
				}
			}

			return output;
		}

		// TODO: Use circles rather than squares maybe?
		// TODO: ExtendOut usage?
		// <summary>
		// Blur a boolean matrix using a square kernel, only changing the value
		// if the neighborhood is significantly different based on a threshold.
		//
		// If extendOut is true, the space outside of the matrix is treated as
		// if the border was extended out. Otherwise, the outside does not
		// contribute any influence.
		//
		// Along with the blured matrix, the number of changes compared to the
		// original is returned.
		// </summary>
		public static (Matrix<bool> Output, int Changes) BooleanBlur(Matrix<bool> input, int radius, bool extendOut, float threshold)
		{
			// var halfThreshold = threshold / 2.0f;
			var output = new Matrix<bool>(input.Size);
			var changes = 0;

			for (var cy = 0; cy < input.Size.Y; cy++)
			{
				for (var cx = 0; cx < input.Size.X; cx++)
				{
					var falseCount = 0;
					var trueCount = 0;
					for (var oy = -radius; oy <= radius; oy++)
					{
						for (var ox = -radius; ox <= radius; ox++)
						{
							var x = cx + ox;
							var y = cy + oy;
							if (extendOut)
							{
								(x, y) = input.ClampXY(x, y);
							}
							else
							{
								if (!input.ContainsXY(x, y)) continue;
							}

							if (input[x, y])
								trueCount++;
							else
								falseCount++;
						}
					}

					var sampleCount = falseCount + trueCount;
					var requirement = (int)(sampleCount * threshold);
					var thisInput = input[cx, cy];
					bool thisOutput;
					if (trueCount - falseCount > requirement)
						thisOutput = true;
					else if (falseCount - trueCount > requirement)
						thisOutput = false;
					else
						thisOutput = input[cx, cy];

					output[cx, cy] = thisOutput;
					if (thisOutput != thisInput)
						changes++;
				}
			}

			return (output, changes);
		}

		// TODO: Maybe circles?
		// <summary>
		// Shrink then grow either the (foreground) true or false regions of an
		// input matrix by a given amount.
		// </summary>
		public static (Matrix<bool> Output, int Changes) ErodeAndDilate(Matrix<bool> input, bool foreground, int amount)
		{
			var output = new Matrix<bool>(input.Size).Fill(!foreground);
			for (var cy = 1 - amount; cy < input.Size.Y; cy++)
			{
				for (var cx = 1 - amount; cx < input.Size.X; cx++)
				{
					bool IsRetained()
					{
						for (var ry = 0; ry < amount; ry++)
						{
							for (var rx = 0; rx < amount; rx++)
							{
								var x = cx + rx;
								var y = cy + ry;
								if (!input.ContainsXY(x, y)) continue;

								if (input[x, y] != foreground)
								{
									return false;
								}
							}
						}

						return true;
					}

					if (!IsRetained()) continue;

					for (var ry = 0; ry < amount; ry++)
					{
						for (var rx = 0; rx < amount; rx++)
						{
							var x = cx + rx;
							var y = cy + ry;
							if (!input.ContainsXY(x, y)) continue;

							output[x, y] = foreground;
						}
					}
				}
			}

			var changes = 0;
			for (var i = 0; i < input.Data.Length; i++)
			{
				if (input[i] != output[i])
					changes++;
			}

			return (output, changes);
		}
	}
}
