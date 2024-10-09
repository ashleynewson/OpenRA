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
using System.Diagnostics;
using System.Linq;
using OpenRA.Mods.Common.Terrain;
using OpenRA.Primitives;
using OpenRA.Support;

namespace OpenRA.Mods.Common.MapUtils
{
	public sealed class TilingPath
	{
		// <summary>Describes the type and direction of the start or end of a TilingPath.</summary>
		public struct Terminal
		{
			public string Type;

			// <summary>
			// Direction to use for this terminal.
			//
			// If the direction here is null, it will be determined automatically later.
			// </summary>
			public int? Direction;

			// <summary>
			// A string which can match the format used by
			// OpenRA.Mods.Common.Terrain.TemplateSegment's Start or End.
			// </summary>
			public readonly string SegmentType
			{
				get
				{
					var direction = Direction
						?? throw new InvalidOperationException("Direction is null");
					return $"{Type}.{MapUtils.Direction.ToString(direction)}";
				}
			}

			public Terminal(string type, int? direction)
			{
				Type = type;
				Direction = direction;
			}
		}

		// <summary>
		// Describes the permitted start, middle, and end templates that can be used to tile the
		// path.
		// </summary>
		public sealed class PermittedTemplates
		{
			// This should probably be changed to store Segments rather than Templates.
			public readonly IEnumerable<TerrainTemplateInfo> Start;
			public readonly IEnumerable<TerrainTemplateInfo> Inner;
			public readonly IEnumerable<TerrainTemplateInfo> End;
			public IEnumerable<TerrainTemplateInfo> All => Start.Union(Inner).Union(End);

			public PermittedTemplates(IEnumerable<TerrainTemplateInfo> start, IEnumerable<TerrainTemplateInfo> inner, IEnumerable<TerrainTemplateInfo> end)
			{
				Start = start;
				Inner = inner;
				End = end;
			}

			public PermittedTemplates(IEnumerable<TerrainTemplateInfo> all)
				: this(all, all, all)
			{ }

			// <summary>
			// Find templates that use the given types at their start and ends.
			//
			// The start and end of a template don't have to match, so long as both are in the
			// provided types list.
			// </summary>
			public static IEnumerable<TerrainTemplateInfo> FindTemplates(ITemplatedTerrainInfo templatedTerrainInfo, string[] types)
				=> FindTemplates(templatedTerrainInfo, types, types);

			// <summary>
			// Find templates that use the given start and end types.
			// </summary>
			public static IEnumerable<TerrainTemplateInfo> FindTemplates(ITemplatedTerrainInfo templatedTerrainInfo, string[] startTypes, string[] endTypes)
			{
				return templatedTerrainInfo.Templates.Values
					.Where(
						template => template.Segments.Any(
							segment =>
								startTypes.Any(type => segment.HasStartType(type) &&
								endTypes.Any(type => segment.HasEndType(type)))))
					.ToArray();
			}
		}

		public Map Map;

		// <summary>
		// Target point sequence to fit TemplateSegments to.
		//
		// If null, Tiling will be a no-op.
		//
		// If non-null, must have at least two points.
		//
		// A loop must have the start and end points equal.
		// </summary>
		public int2[] Points;

		// <summary>
		// Maximum permitted Chebychev distance that layed TemplateSegments may be from the
		// specified points.
		// </summary>
		public int MaxDeviation;

		// <summary>
		// Stores start type and direction.
		// </summary>
		public Terminal Start;

		// <summary>
		// Stores end type and direction.
		// </summary>
		public Terminal End;
		public PermittedTemplates Templates;

		// <summary>Whether the start and end points are the same.</summary>
		public bool IsLoop
		{
			get => Points != null && Points[0] == Points[^1];
		}

		public TilingPath(
			Map map,
			int2[] points,
			int maxDeviation,
			string startType,
			string endType,
			PermittedTemplates permittedTemplates)
		{
			Map = map;
			Points = points;
			MaxDeviation = maxDeviation;
			Start = new Terminal(startType, null);
			End = new Terminal(endType, null);
			Templates = permittedTemplates;
		}

		sealed class TilingSegment
		{
			public readonly TerrainTemplateInfo TemplateInfo;
			public readonly TemplateSegment TemplateSegment;
			public readonly int StartTypeId;
			public readonly int EndTypeId;
			public readonly int2 Offset;
			public readonly int2 Moves;
			public readonly int2[] RelativePoints;
			public readonly int[] Directions;
			public readonly int[] DirectionMasks;
			public readonly int[] ReverseDirectionMasks;

			public TilingSegment(TerrainTemplateInfo templateInfo, TemplateSegment templateSegment, int startId, int endId)
			{
				TemplateInfo = templateInfo;
				TemplateSegment = templateSegment;
				StartTypeId = startId;
				EndTypeId = endId;
				Offset = templateSegment.Points[0];
				Moves = templateSegment.Points[^1] - Offset;
				RelativePoints = templateSegment.Points
					.Select(p => p - templateSegment.Points[0])
					.ToArray();

				Directions = new int[RelativePoints.Length];
				DirectionMasks = new int[RelativePoints.Length];
				ReverseDirectionMasks = new int[RelativePoints.Length];

				// Last point has no direction.
				Directions[^1] = Direction.NONE;
				DirectionMasks[^1] = 0;
				ReverseDirectionMasks[^1] = 0;
				for (var i = 0; i < RelativePoints.Length - 1; i++)
				{
					var direction = Direction.FromOffset(RelativePoints[i + 1] - RelativePoints[i]);
					if (direction == Direction.NONE)
						throw new ArgumentException("TemplateSegment has duplicate points in sequence");
					Directions[i] = direction;
					DirectionMasks[i] = 1 << direction;
					ReverseDirectionMasks[i] = 1 << Direction.Reverse(direction);
				}
			}
		}

		// <summary>
		// Attempt to tile the given path onto a map.
		//
		// If the path could be tiled, returns the sequence of points actually traversed by the
		// chosen TemplateSegments. Returns null if the path could not be tiled within constraints.
		// </summary>
		public int2[] Tile(MersenneTwister random)
		{
			// High-level explanation:
			//
			// This is essentially a Dijkstra's algorithm best-first search.
			//
			// The search is performed over a 3-dimensional space: (x, y, connection type).
			// Connection types correspond to the .Start or .End values of TemplateSegments.
			//
			// The best found scores of the nodes in this space are stored as an array of matrices.
			// There is a matrix for each possible connection type, and each matrix stores the
			// (current) best scores at the (X, Y) locations for that given connection type.
			//
			// The directed edges between the nodes of this 3-dimensional space are defined by the
			// TemplateSegments within the permitted set of templates. For example, a segment
			// defined as
			//
			//   Segment:
			//       Start: Beach.L
			//       End: Beach.D
			//       Points: 3,1, 2,1, 2,2, 2,3
			//
			// may connect a node from (10, 10) in the "Beach.L" matrix to node (9, 12) in the
			// "Beach.D" matrix. (The overall point displacement is (2,3) - (3,1) = (-1, +2))
			//
			// The cost of a transition/link/edge between nodes is defined by how well the
			// template segment fits the path (how little "deviation" is accumulates). However, in
			// order for a transition to be allowed at all, it must satisfy some constraints:
			//
			// - It must not regress backward along the path (but no immediate "progress" is OK).
			// - It must not deviate at any point in the segment beyond MaxDeviation from the path.
			// - TODO: It must not skip to much later path points which are within MaxDeviation.
			//
			// The search is conducted from the path start node until the best possible score of
			// the end node is confirmed. This also populates possible intermediate nodes' scores.
			//
			// Then, from the end node, it works backwards. It finds any (random) suitable template
			// segment which connects back to a previous node where the difference in score is
			// that of the template segment's cost, implying that that previous node is on an
			// optimal path towards the end node. This process repeats until the start node is
			// reached, painting templates along the way.
			//
			// Note that this algorithm makes a few (reasonable) assumptions about the shapes of
			// templates, such as that they don't individually snake around too much. The actual
			// tiles of a template are ignored during the search, with only the segment being used
			// to calculate transition cost and validity.
			if (Points == null)
				return null;

			var start = Start;
			var end = End;
			start.Direction ??= Direction.FromOffset(Points[1] - Points[0]);
			end.Direction ??= Direction.FromOffset(IsLoop ? Points[1] - Points[0] : Points[^1] - Points[^2]);

			var minPoint = new int2(
				Points.Min(p => p.X) - MaxDeviation,
				Points.Min(p => p.Y) - MaxDeviation);
			var maxPoint = new int2(
				Points.Max(p => p.X) + MaxDeviation,
				Points.Max(p => p.Y) + MaxDeviation);
			var points = Points.Select(point => point - minPoint).ToArray();

			var isLoop = IsLoop;

			// grid points (not squares), so these are offset 0.5 from tile centers.
			var size = new int2(1 + maxPoint.X - minPoint.X, 1 + maxPoint.Y - minPoint.Y);
			var sizeXY = size.X * size.Y;

			const int MAX_DEVIATION = int.MaxValue;

			// Bit masks of 8-angle directions which are considered a positive progress
			// traversal. Template choices with an overall negative progress traversal
			// are rejected.
			var directions = new Matrix<byte>(size);

			// How far away from the path this point is.
			var deviations = new Matrix<int>(size).Fill(MAX_DEVIATION);

			// Bit masks of 8-angle directions which define whether it's permitted
			// to traverse from one point to a given neighbour.
			var traversables = new Matrix<byte>(size);
			{
				var gradientX = new Matrix<int>(size);
				var gradientY = new Matrix<int>(size);
				for (var pointI = 0; pointI < points.Length; pointI++)
				{
					if (isLoop && pointI == 0)
					{
						// Same as last point.
						continue;
					}

					var point = points[pointI];
					var pointPrevI = pointI - 1;
					var pointNextI = pointI + 1;
					var directionX = 0;
					var directionY = 0;
					if (pointNextI < points.Length)
					{
						directionX += points[pointNextI].X - point.X;
						directionY += points[pointNextI].Y - point.Y;
					}

					if (pointPrevI >= 0)
					{
						directionX += point.X - points[pointPrevI].X;
						directionY += point.Y - points[pointPrevI].Y;
					}

					for (var deviation = 0; deviation <= MaxDeviation; deviation++)
					{
						var minX = point.X - deviation;
						var minY = point.Y - deviation;
						var maxX = point.X + deviation;
						var maxY = point.Y + deviation;
						for (var y = minY; y <= maxY; y++)
						{
							for (var x = minX; x <= maxX; x++)
							{
								// const i = y * sizeX + x;
								if (deviation < deviations[x, y])
								{
									deviations[x, y] = deviation;
								}

								if (deviation == MaxDeviation)
								{
									gradientX[x, y] += directionX;
									gradientY[x, y] += directionY;
									if (x > minX)
										traversables[x, y] |= Direction.M_L;
									if (x < maxX)
										traversables[x, y] |= Direction.M_R;
									if (y > minY)
										traversables[x, y] |= Direction.M_U;
									if (y < maxY)
										traversables[x, y] |= Direction.M_D;
									if (x > minX && y > minY)
										traversables[x, y] |= Direction.M_LU;
									if (x > minX && y < maxY)
										traversables[x, y] |= Direction.M_LD;
									if (x < maxX && y > minY)
										traversables[x, y] |= Direction.M_RU;
									if (x < maxX && y < maxY)
										traversables[x, y] |= Direction.M_RD;
								}
							}
						}
					}
				}

				// Probational
				for (var i = 0; i < sizeXY; i++)
				{
					if (gradientX[i] == 0 && gradientY[i] == 0)
					{
						directions[i] = 0;
						continue;
					}

					var direction = Direction.FromOffset(gradientX[i], gradientY[i]);

					// .... direction: 0123456701234567
					//                 UUU DDD UUU DDD
					//                 R LLL RRR LLL R
					directions[i] = (byte)(0b100000111000001 >> (7 - direction));
				}
			}

			var pathStart = points[0];
			var pathEnd = points[^1];
			var permittedTemplates = Templates.All.ToImmutableHashSet();

			const int MAX_SCORE = int.MaxValue;
			var segmentTypeToId = new Dictionary<string, int>();
			var segmentsByStart = new List<List<TilingSegment>>();
			var segmentsByEnd = new List<List<TilingSegment>>();
			var scores = new List<Matrix<int>>();
			{
				void RegisterSegmentType(string type)
				{
					if (segmentTypeToId.ContainsKey(type)) return;
					var newId = segmentTypeToId.Count;
					segmentTypeToId.Add(type, newId);
					segmentsByStart.Add(new List<TilingSegment>());
					segmentsByEnd.Add(new List<TilingSegment>());
					scores.Add(new Matrix<int>(size).Fill(MAX_SCORE));
				}

				foreach (var template in permittedTemplates)
				{
					foreach (var segment in template.Segments)
					{
						RegisterSegmentType(segment.Start);
						RegisterSegmentType(segment.End);
						var startTypeId = segmentTypeToId[segment.Start];
						var endTypeId = segmentTypeToId[segment.End];
						var tilePathSegment = new TilingSegment(template, segment, startTypeId, endTypeId);
						segmentsByStart[startTypeId].Add(tilePathSegment);
						segmentsByEnd[endTypeId].Add(tilePathSegment);
					}
				}
			}

			var totalTypeIds = segmentTypeToId.Count;

			var priorities = new PriorityArray<int>(totalTypeIds * size.X * size.Y, MAX_SCORE);
			void SetPriorityAt(int typeId, int2 pos, int priority)
				=> priorities[typeId * sizeXY + pos.Y * size.X + pos.X] = priority;
			(int TypeId, int2 Pos, int Priority) GetNextPriority()
			{
				var index = priorities.GetMinIndex();
				var priority = priorities[index];
				var typeId = index / sizeXY;
				var xy = index % sizeXY;
				return (typeId, new int2(xy % size.X, xy / size.X), priority);
			}

			var pathStartTypeId = segmentTypeToId[start.SegmentType];
			var pathEndTypeId = segmentTypeToId[end.SegmentType];
			var innerTypeIds = Templates.Inner
				.SelectMany(template => template.Segments)
				.SelectMany(segment => new[] { segment.Start, segment.End })
				.Select(segmentType => segmentTypeToId[segmentType])
				.ToImmutableHashSet();

			// Lower (closer to zero) scores are better matches.
			// MAX_SCORE means totally unacceptable.
			int ScoreSegment(TilingSegment segment, int2 from)
			{
				if (from == pathStart)
				{
					if (segment.StartTypeId != pathStartTypeId)
						return MAX_SCORE;
				}
				else
				{
					if (!innerTypeIds.Contains(segment.StartTypeId))
						return MAX_SCORE;
				}

				if (from + segment.Moves == pathEnd)
				{
					if (segment.EndTypeId != pathEndTypeId)
						return MAX_SCORE;
				}
				else
				{
					if (!innerTypeIds.Contains(segment.EndTypeId))
						return MAX_SCORE;
				}

				var deviationAcc = 0;
				var progressionAcc = 0;
				var lastPointI = segment.RelativePoints.Length - 1;
				for (var pointI = 0; pointI <= lastPointI; pointI++)
				{
					var point = from + segment.RelativePoints[pointI];
					var directionMask = segment.DirectionMasks[pointI];
					var reverseDirectionMask = segment.ReverseDirectionMasks[pointI];
					if (!deviations.ContainsXY(point))
					{
						// Intermediate point escapes bounds.
						return MAX_SCORE;
					}

					if (pointI < lastPointI)
					{
						if ((traversables[point] & directionMask) == 0)
						{
							// Next point escapes traversable area.
							return MAX_SCORE;
						}

						if ((directions[point] & directionMask) == directionMask)
						{
							progressionAcc++;
						}
						else if ((directions[point] & reverseDirectionMask) == reverseDirectionMask)
						{
							progressionAcc--;
						}
					}

					// pointI > 0 is needed to avoid double-counting the segments's start with the
					// previous one's end.
					if (pointI > 0)
					{
						deviationAcc += deviations[point];
					}
				}

				if (progressionAcc < 0)
				{
					// It's moved backwards
					return MAX_SCORE;
				}

				// Satisfies all requirements.
				return deviationAcc;
			}

			void UpdateFrom(int2 from, int fromTypeId)
			{
				var fromScore = scores[fromTypeId][from];
				foreach (var segment in segmentsByStart[fromTypeId])
				{
					var to = from + segment.Moves;
					if (to.X < 0 || to.X >= size.X || to.Y < 0 || to.Y >= size.Y)
					{
						continue;
					}

					// Most likely to fail. Check first.
					if (deviations[to] == MAX_DEVIATION)
					{
						// End escapes bounds.
						continue;
					}

					var segmentScore = ScoreSegment(segment, from);
					if (segmentScore == MAX_SCORE)
					{
						continue;
					}

					var toScore = fromScore + segmentScore;
					var toTypeId = segment.EndTypeId;
					if (toScore < scores[toTypeId][to])
					{
						scores[toTypeId][to] = toScore;
						SetPriorityAt(toTypeId, to, toScore);
					}
				}

				SetPriorityAt(fromTypeId, from, MAX_SCORE);
			}

			scores[pathStartTypeId][pathStart] = 0;
			UpdateFrom(pathStart, pathStartTypeId);

			// Needed in case we loop back to the start.
			scores[pathStartTypeId][pathStart] = MAX_SCORE;

			while (true)
			{
				var (fromTypeId, from, priority) = GetNextPriority();

				// TODO: Break if we're on the end point?
				if (priority == MAX_SCORE)
				{
					break;
				}

				UpdateFrom(from, fromTypeId);
			}

			// Trace back and update tiles
			var resultPoints = new List<int2>
			{
				pathEnd + minPoint
			};

			(int2 From, int FromTypeId) TraceBackStep(int2 to, int toTypeId)
			{
				var toScore = scores[toTypeId][to];
				var candidates = new List<TilingSegment>();
				foreach (var segment in segmentsByEnd[toTypeId])
				{
					var from = to - segment.Moves;
					if (from.X < 0 || from.X >= size.X || from.Y < 0 || from.Y >= size.Y)
					{
						continue;
					}

					// Most likely to fail. Check first.
					if (deviations[from] == MAX_DEVIATION)
					{
						// Start escapes bounds.
						continue;
					}

					var segmentScore = ScoreSegment(segment, from);
					if (segmentScore == MAX_SCORE)
					{
						continue;
					}

					var fromScore = toScore - segmentScore;
					if (fromScore == scores[segment.StartTypeId][from])
					{
						candidates.Add(segment);
					}
				}

				Debug.Assert(candidates.Count >= 1, "TraceBack didn't find an original route");
				var chosenSegment = candidates[random.Next(candidates.Count)];
				var chosenFrom = to - chosenSegment.Moves;
				PaintTemplate(Map, chosenFrom - chosenSegment.Offset + minPoint, chosenSegment.TemplateInfo);

				// Skip end point as it is recorded in the previous template.
				for (var i = chosenSegment.RelativePoints.Length - 2; i >= 0; i--)
				{
					var point = chosenSegment.RelativePoints[i];
					resultPoints.Add(chosenFrom + point + minPoint);
				}

				return (chosenFrom, chosenSegment.StartTypeId);
			}

			{
				var to = pathEnd;
				var toTypeId = pathEndTypeId;
				if (scores[toTypeId][to] == MAX_SCORE)
					return null;
				(to, toTypeId) = TraceBackStep(to, toTypeId);

				// We previously set this to MAX_SCORE in case we were a loop. Reset it for getting back to the start.
				scores[pathStartTypeId][pathStart] = 0;

				// No need to check direction. If that is an issue, I have bigger problems to worry about.
				while (to != pathStart)
				{
					(to, toTypeId) = TraceBackStep(to, toTypeId);
				}
			}

			// Traced back in reverse, so reverse the reversal.
			resultPoints.Reverse();
			return resultPoints.ToArray();
		}

		static void PaintTemplate(Map map, int2 at, TerrainTemplateInfo template)
		{
			if (template.PickAny)
				throw new ArgumentException("PaintTemplate does not expect PickAny");
			for (var y = 0; y < template.Size.Y; y++)
			{
				for (var x = 0; x < template.Size.X; x++)
				{
					var i = (byte)(y * template.Size.X + x);
					if (template[i] == null)
						continue;
					var tile = new TerrainTile(template.Id, i);
					var mpos = new MPos(at.X + x, at.Y + y);
					if (map.Tiles.Contains(mpos))
						map.Tiles[mpos] = tile;
				}
			}
		}

		// <summary>
		// Extend the start and end of a path by extensionLength points. The directions of the
		// extensions are based on the overall direction of the outermost inertialRange points.
		//
		// Returns this;
		// </summary>
		public TilingPath InertiallyExtend(int extensionLength, int inertialRange)
		{
			Points = InertiallyExtendPathPoints(Points, extensionLength, inertialRange);
			return this;
		}

		// <summary>
		// Extend the start and end of a path by extensionLength points. The directions of the
		// extensions are based on the overall direction of the outermost inertialRange points.
		// </summary>
		public static int2[] InertiallyExtendPathPoints(int2[] points, int extensionLength, int inertialRange)
		{
			if (points == null)
				return null;

			if (inertialRange > points.Length - 1)
				inertialRange = points.Length - 1;
			var sd = Direction.FromOffsetNonDiagonal(points[inertialRange] - points[0]);
			var ed = Direction.FromOffsetNonDiagonal(points[^1] - points[^(inertialRange + 1)]);
			var newPoints = new int2[points.Length + extensionLength * 2];

			for (var i = 0; i < extensionLength; i++)
			{
				newPoints[i] = points[0] - Direction.ToOffset(sd) * (extensionLength - i);
			}

			Array.Copy(points, 0, newPoints, extensionLength, points.Length);

			for (var i = 0; i < extensionLength; i++)
			{
				newPoints[extensionLength + points.Length + i] = points[^1] + Direction.ToOffset(ed) * (i + 1);
			}

			return newPoints;
		}

		// <summary>
		// For map edge-connected (non-loop) starts/ends, the path is extended beyond the edge.
		// For loops or paths which don't connect to the map edge, no change is applied.
		//
		// Starts/ends which are corner-connected or already extend beyond the edge are unaltered.
		//
		// Returns this.
		// </summary>
		public TilingPath ExtendEdge(int extensionLength)
		{
			Points = ExtendEdgePathPoints(Points, Map.MapSize, extensionLength);
			return this;
		}

		// <summary>
		// For map edge-connected (non-loop) starts/ends, the path is extended beyond the edge.
		// For loops or paths which don't connect to the map edge, the input points are returned
		// unaltered.
		//
		// Starts/ends which are corner-connected or already extend beyond the edge are unaltered.
		// </summary>
		public static int2[] ExtendEdgePathPoints(int2[] points, int2 size, int extensionLength)
		{
			if (points == null)
				return null;

			if (points[0] != points[^1])
			{
				// Not a loop.
				int2[] Extend(int2 point)
				{
					var ox = (point.X == 0) ? -1
						: (point.X == size.X) ? 1
						: 0;
					var oy = (point.Y == 0) ? -1
						: (point.Y == size.Y) ? 1
						: 0;
					if (ox == oy)
					{
						// We're either not on an edge or we're at a corner, so don't extend.
						return Array.Empty<int2>();
					}

					var offset = new int2(ox, oy);

					var extension = new int2[extensionLength];
					var newPoint = point;
					for (var i = 0; i < extensionLength; i++)
					{
						newPoint += offset;
						extension[i] = newPoint;
					}

					return extension;
				}

				// Open paths. Extend if beyond edges.
				var startExt = Extend(points[0]).Reverse().ToArray();
				var endExt = Extend(points[^1]);

				// [...startExt, ...points, ...endExt];
				var tweaked = new int2[points.Length + startExt.Length + endExt.Length];
				Array.Copy(startExt, 0, tweaked, 0, startExt.Length);
				Array.Copy(points, 0, tweaked, startExt.Length, points.Length);
				Array.Copy(endExt, 0, tweaked, points.Length + startExt.Length, endExt.Length);
				return tweaked;
			}
			else
			{
				return points;
			}
		}

		// <summary>
		// For loops, points are rotated such that the start/end reside in the longest straight.
		// For non-loops, the input points are returned unaltered.
		//
		// Returns this.
		// </summary>
		public TilingPath OptimizeLoop()
		{
			Points = OptimizeLoopPathPoints(Points);
			return this;
		}

		// <summary>
		// For loops, points are rotated such that the start/end reside in the longest straight.
		// For non-loops, the input points are returned unaltered.
		// </summary>
		public static int2[] OptimizeLoopPathPoints(int2[] points)
		{
			if (points == null)
				return null;

			if (points[0] == points[^1])
			{
				// Closed loop. Find the longest straight
				// (nrlen excludes the repeated point at the end.)
				var nrlen = points.Length - 1;
				var prevDim = -1;
				var scanStart = -1;
				var bestScore = -1;
				var bestBend = -1;
				var prevBend = -1;
				var prevI = 0;
				for (var i = 1; ; i++)
				{
					if (i == nrlen)
						i = 0;
					var dim = points[i].X == points[prevI].X ? 1 : 0;
					if (prevDim != -1 && prevDim != dim)
					{
						if (scanStart == -1)
						{
							// This is technically just after the bend. But that's fine.
							scanStart = i;
						}
						else
						{
							var score = prevI - prevBend;
							if (score < 0)
								score += nrlen;

							if (score > bestScore)
							{
								bestBend = prevBend;
								bestScore = score;
							}

							if (i == scanStart)
							{
								break;
							}
						}

						prevBend = prevI;
					}

					prevDim = dim;
					prevI = i;
				}

				var favouritePoint = (bestBend + (bestScore >> 1)) % nrlen;

				// Repeat the start at the end.
				// [...points.slice(favouritePoint, nrlen), ...points.slice(0, favouritePoint + 1)];
				var tweaked = new int2[points.Length];
				Array.Copy(points, favouritePoint, tweaked, 0, nrlen - favouritePoint);
				Array.Copy(points, 0, tweaked, nrlen - favouritePoint, favouritePoint + 1);
				return tweaked;
			}
			else
			{
				return points;
			}
		}

		// <summary>
		// Shrink a path by a given amount at both ends. If the number of points in the path drops
		// below minimumLength, the path is nullified.
		//
		// If a loop is provided, the path is not shrunk, but the minimumLength requirement still
		// holds.
		//
		// Returns this.
		// </summary>
		public TilingPath Shrink(int shrinkBy, int minimumLength)
		{
			Points = ShrinkPathPoints(Points, shrinkBy, minimumLength);
			return this;
		}

		// <summary>
		// Shrink a path by a given amount at both ends. If the number of points in the path drops
		// below minimumLength, null is returned.
		//
		// If a loop is provided, the path is not shrunk, but the minimumLength requirement still
		// holds.
		// </summary>
		public static int2[] ShrinkPathPoints(int2[] points, int shrinkBy, int minimumLength)
		{
			if (points == null)
				return null;

			if (minimumLength <= 1)
				throw new ArgumentException("minimumLength must be greater than 1");

			if (points[0] == points[^1])
			{
				// Loop.
				if (points.Length < minimumLength)
					return null;
				return points[0..^0];
			}

			if (points.Length < shrinkBy * 2 + minimumLength)
				return null;
			return points[shrinkBy..(points.Length - shrinkBy)];
		}

		// <summary>
		// Takes a path and normalizes its progression direction around the map center.
		// Normalized but opposing paths should rotate around the center in the same direction.
		// </summary>
		public TilingPath ChirallyNormalize()
		{
			Points = ChirallyNormalizePathPoints(Points, Map.MapSize);
			return this;
		}

		// <summary>
		// Takes a path and normalizes its progression direction around the map center.
		// Normalized but opposing paths should rotate around the center in the same direction.
		// </summary>
		public static int2[] ChirallyNormalizePathPoints(int2[] points, int2 size)
		{
			if (points == null || points.Length < 2)
				return points;

			var normalized = (int2[])points.Clone();
			var start = points[0];
			var end = points[^1];

			if (start == end)
			{
				// Is loop
				start = points[1];
				end = points[^2];
			}

			bool ShouldReverse(int2 start, int2 end)
			{
				// This could be converted to integer math, but there's little motive.
				var midX = (size.X - 1) / 2.0f;
				var midY = (size.Y - 1) / 2.0f;
				var v1x = start.X - midX;
				var v1y = start.Y - midY;
				var v2x = end.X - midX;
				var v2y = end.Y - midY;

				// Rotation around center?
				var crossProd = v1x * v2y - v2x * v1y;
				if (crossProd != 0)
					return crossProd < 0;

				// Distance from center?
				var r1 = v1x * v1x + v1y * v1y;
				var r2 = v2x * v2x + v2y * v2y;
				if (r1 != r2)
					return r1 < r2;

				// Absolute angle
				return v1y == v2y ? v1x > v2x : v1y > v2y;
			}

			if (ShouldReverse(start, end))
				Array.Reverse(normalized);

			return normalized;
		}

		// <summary>
		// Given a set of point sequences and a stencil mask that defines permitted point positions,
		// remove points that are disallowed, splitting or dropping point sequences as needed.
		//
		// The outside of the matrix is considered false (points disallowed).
		//
		// Sequences with fewer than 2 points are dropped.
		// </summary>
		public static int2[][] MaskPathPoints(IEnumerable<int2[]> pointArrayArray, Matrix<bool> mask)
		{
			var newPointArrayArray = new List<int2[]>();

			foreach (var pointArray in pointArrayArray)
			{
				if (pointArray == null || pointArray.Length < 2)
					continue;

				var isLoop = pointArray[0] == pointArray[^1];
				int firstBad;
				for (firstBad = 0; firstBad < pointArray.Length; firstBad++)
				{
					if (!(mask.ContainsXY(pointArray[firstBad]) && mask[pointArray[firstBad]]))
						break;
				}

				if (firstBad == pointArray.Length)
				{
					// The path is entirely within the mask already.
					newPointArrayArray.Add(pointArray);
					continue;
				}

				var startAt = isLoop ? firstBad : 0;
				var wrapAt = isLoop ? pointArray.Length - 1 : pointArray.Length;
				var i = startAt;
				List<int2> currentPointArray = null;
				do
				{
					if (mask.ContainsXY(pointArray[i]) && mask[pointArray[i]])
					{
						currentPointArray ??= new List<int2>();
						currentPointArray.Add(pointArray[i]);
					}
					else
					{
						if (currentPointArray != null && currentPointArray.Count > 1)
							newPointArrayArray.Add(currentPointArray.ToArray());
						currentPointArray = null;
					}

					i++;
					if (i == wrapAt)
						i = 0;
				}
				while (i != startAt);

				if (currentPointArray != null && currentPointArray.Count > 1)
					newPointArrayArray.Add(currentPointArray.ToArray());
			}

			return newPointArrayArray.ToArray();
		}

		// <summary>
		// Retains paths which have no points in common with other (previous and retained) paths.
		//
		// The underlying point sequences are not cloned.
		//
		// All input sequences must be non-null.
		// </summary>
		public static int2[][] RetainDisjointPaths(IEnumerable<int2[]> inputs, int2 size)
		{
			var outputs = new List<int2[]>();
			var lookup = new Matrix<bool>(size + new int2(1, 1));
			foreach (var points in inputs)
			{
				var retain = true;
				foreach (var point in points)
				{
					if (lookup[point])
					{
						retain = false;
						break;
					}
				}

				if (retain)
				{
					outputs.Add(points);
					foreach (var point in points)
					{
						lookup[point] = true;
					}
				}
			}

			return outputs.ToArray();
		}

		static Matrix<byte> RemoveJunctionsFromDirectionMap(Matrix<byte> input)
		{
			var output = input.Clone();
			for (var cy = 0; cy < input.Size.Y; cy++)
			{
				for (var cx = 0; cx < input.Size.X; cx++)
				{
					var dm = input[cx, cy];
					if (Direction.Count(dm) > 2)
					{
						output[cx, cy] = 0;
						foreach (var (offset, d) in Direction.SPREAD8_D)
						{
							var xy = new int2(cx + offset.X, cy + offset.Y);
							if (!input.ContainsXY(xy))
								continue;
							var dr = Direction.Reverse(d);
							output[xy] = (byte)(output[xy] & ~(1 << dr));
						}
					}
				}
			}

			for (var x = 0; x < input.Size.X; x++)
			{
				output[x, 0] = (byte)(output[x, 0] & ~(Direction.M_LU | Direction.M_U | Direction.M_RU));
				output[x, input.Size.Y - 1] = (byte)(output[x, input.Size.Y - 1] & ~(Direction.M_RD | Direction.M_D | Direction.M_LD));
			}

			for (var y = 0; y < input.Size.Y; y++)
			{
				output[0, y] = (byte)(output[0, y] & ~(Direction.M_LD | Direction.M_L | Direction.M_LU));
				output[input.Size.X - 1, y] &= (byte)(output[input.Size.X - 1, y] & ~(Direction.M_RU | Direction.M_R | Direction.M_RD));
			}

			return output;
		}

		// <summary>
		// Traces a matrix of directions into a set of point sequences.
		//
		// Any junctions in the input direction map are dropped.
		// </summary>
		public static int2[][] DirectionMapToPaths(Matrix<byte> input)
		{
			input = RemoveJunctionsFromDirectionMap(input);

			// Loops not handled, but these would be extremely rare anyway.
			var pointArrays = new List<int2[]>();
			for (var sy = 0; sy < input.Size.Y; sy++)
			{
				for (var sx = 0; sx < input.Size.X; sx++)
				{
					var sdm = input[sx, sy];
					if (Direction.FromMask(sdm) != Direction.NONE)
					{
						var points = new List<int2>();
						var xy = new int2(sx, sy);
						var reverseDm = 0;

						bool AddPoint()
						{
							points.Add(xy);
							var dm = input[xy] & ~reverseDm;
							foreach (var (offset, d) in Direction.SPREAD8_D)
							{
								if ((dm & (1 << d)) != 0)
								{
									xy += offset;
									if (!input.ContainsXY(xy))
										throw new ArgumentException("input should not link out of bounds");
									reverseDm = 1 << Direction.Reverse(d);
									return true;
								}
							}

							return false;
						}

						while (AddPoint())
						{
						}

						pointArrays.Add(points.ToArray());
					}
				}
			}

			return pointArrays.ToArray();
		}
	}
}
