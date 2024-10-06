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
		public readonly struct Terminal
		{
			public readonly string Type;
			public readonly int Direction;

			// <summary>
			// A string which can match the format used by
			// OpenRA.Mods.Common.Terrain.TemplateSegment's Start or End.
			// </summary>
			public string SegmentType
			{
				get => $"{Type}.{MapUtils.Direction.ToString(Direction)}";
			}

			public Terminal(string type, int direction)
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

		// <summary>
		// Target point sequence to fit TemplateSegments to.
		//
		// Must have at least two points.
		//
		// A loop must have the start and end points equal.
		// </summary>
		public int2[] Points;
		// <summary>
		// Maximum permitted Chebychev distance that layed TemplateSegments may be from the
		// specified points.
		// </summary>
		public int MaxDeviation;
		public Terminal Start;
		public Terminal End;
		public PermittedTemplates Templates;
		// <summary>Whether the start and end points are the same.</summary>
		public bool IsLoop
		{
			get => Points[0] == Points[^1];
		}

		public TilingPath(
			int2[] points,
			int maxDeviation,
			string startType,
			string endType,
			PermittedTemplates permittedTemplates)
		{
			Points = points;
			MaxDeviation = maxDeviation;
			var startDirection = Direction.FromOffset(Points[1] - Points[0]);
			Start = new Terminal(startType, startDirection);
			var endDirection = Direction.FromOffset(IsLoop ? Points[1] - Points[0] : Points[^1] - Points[^2]);
			End = new Terminal(endType, endDirection);
			Templates = permittedTemplates;
		}

		private sealed class TilePathSegment
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

			public TilePathSegment(TerrainTemplateInfo templateInfo, TemplateSegment templateSegment, int startId, int endId)
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
				Directions[^1] = MapUtils.Direction.NONE;
				DirectionMasks[^1] = 0;
				ReverseDirectionMasks[^1] = 0;
				for (var i = 0; i < RelativePoints.Length - 1; i++)
				{
					var direction = Direction.FromOffset(RelativePoints[i + 1] - RelativePoints[i]);
					if (direction == MapUtils.Direction.NONE)
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
		public int2[] Tile(Map map, MersenneTwister random)
		{
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
			var segmentsByStart = new List<List<TilePathSegment>>();
			var segmentsByEnd = new List<List<TilePathSegment>>();
			var scores = new List<Matrix<int>>();
			{
				void RegisterSegmentType(string type)
				{
					if (segmentTypeToId.ContainsKey(type)) return;
					var newId = segmentTypeToId.Count;
					segmentTypeToId.Add(type, newId);
					segmentsByStart.Add(new List<TilePathSegment>());
					segmentsByEnd.Add(new List<TilePathSegment>());
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
						var tilePathSegment = new TilePathSegment(template, segment, startTypeId, endTypeId);
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

			var pathStartTypeId = segmentTypeToId[Start.SegmentType];
			var pathEndTypeId = segmentTypeToId[End.SegmentType];
			var innerTypeIds = Templates.Inner
				.SelectMany(template => template.Segments)
				.SelectMany(segment => new[] { segment.Start, segment.End })
				.Select(segmentType => segmentTypeToId[segmentType])
				.ToImmutableHashSet();

			// Assumes both f and t are in the sizeX/sizeY bounds.
			// Lower (closer to zero) scores are better matches.
			// Higher scores are worse matches.
			// MAX_SCORE means totally unacceptable.
			int ScoreSegment(TilePathSegment segment, int2 from)
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
					if (point.X < 0 || point.X >= size.X || point.Y < 0 || point.Y >= size.Y)
					{
						// Intermediate point escapes array bounds.
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

					if (pointI > 0)
					{
						// Don't double-count the template's path's starts and ends
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
				var candidates = new List<TilePathSegment>();
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
				PaintTemplate(map, chosenFrom - chosenSegment.Offset + minPoint, chosenSegment.TemplateInfo);

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

		private static void PaintTemplate(Map map, int2 at, TerrainTemplateInfo template)
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
	}
}
