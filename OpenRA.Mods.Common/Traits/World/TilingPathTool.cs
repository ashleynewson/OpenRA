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
using OpenRA.Graphics;
using OpenRA.Mods.Common.EditorBrushes;
using OpenRA.Mods.Common.MapGenerator;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.EditorWorld)]
	public class TilingPathToolInfo : TraitInfo, IEditorToolInfo
	{
		[FluentReference]
		[Desc("The label to show in the tools menu.")]
		public readonly string Label = "label-tool-tiling-path";

		[Desc("The widget tree to open when the tool is selected.")]
		public readonly string PanelWidget = "TILING_PATH_TOOL_PANEL";

		public override object Create(ActorInitializer init)
		{
			return new TilingPathTool(init.Self, this);
		}

		string IEditorToolInfo.Label => Label;
		string IEditorToolInfo.PanelWidget => PanelWidget;
	}

	public class TilingPathTool : IRenderAnnotations, INotifyActorDisposing, IWorldLoaded
	{
		public class PathPlan
		{
			public readonly int Start;
			public readonly int End;
			public readonly bool Loop;
			public readonly ImmutableArray<CPos> Rallies;
			public int AutoStart
			{
				get {
					if (Start != Direction.None)
					{
						return Start;
					}
					else
					{
						if (Rallies.Length >= 2)
							return Direction.FromCVecRounding(Rallies[1] - Rallies[0]);
						else
							return Direction.None;
					}
				}
			}
			public int AutoEnd
			{
				get {
					if (End != Direction.None)
					{
						return End;
					}
					else if (Loop)
					{
						return AutoStart;
					}
					else
					{
						if (Rallies.Length >= 2)
							return Direction.FromCVecRounding(Rallies[^1] - Rallies[^2]);
						else
							return Direction.None;
					}
				}
			}
			public CPos FirstPoint => Rallies[0];
			public CPos LastPoint => Loop ? Rallies[0] : Rallies[^1];

			public PathPlan(CPos first)
			{
				Start = Direction.None;
				End = Direction.None;
				Loop = false;
				Rallies = [first];
			}

			public PathPlan(int start, int end, bool loop, ImmutableArray<CPos> rallies)
			{
				if (rallies == null || rallies.Length == 0)
					throw new ArgumentException("rallies must have at least one point");

				Start = start;
				End = end;
				Loop = loop && rallies.Length >= 3;
				Rallies = rallies;
			}

			public PathPlan WithStart(int start)
			{
				return new PathPlan(start, End, Loop, Rallies);
			}

			public PathPlan WithEnd(int end)
			{
				return new PathPlan(Start, end, Loop, Rallies);
			}

			public PathPlan WithLoop(bool loop)
			{
				return new PathPlan(Start, End, loop, Rallies);
			}

			public PathPlan WithRallyAppended(CPos cpos)
			{
				return new PathPlan(Start, Direction.None, Loop, [..Rallies, cpos]);
			}

			public PathPlan WithRallyRemoved(int index)
			{
				if (Rallies.Length == 1)
					return null;

				return new PathPlan(
					index != 0 ? Start : Direction.None,
					index != (Rallies.Length - 1) ? End : Direction.None,
					Loop,
					[..Rallies[..index], ..Rallies[(index + 1)..]]);
			}

			public PathPlan WithRallyReplaced(int index, CPos cpos)
			{
				return new PathPlan(Start, End, Loop, [..Rallies[..index], cpos, ..Rallies[(index + 1)..]]);
			}

			public PathPlan WithRallyInserted(int index, CPos cpos)
			{
				return new PathPlan(Start, End, Loop, [..Rallies[..index], cpos, ..Rallies[index..]]);
			}

			public PathPlan Moved(CVec offset)
			{
				var rallies = Rallies.Select(r => r + offset).ToImmutableArray();
				return new PathPlan(Start, End, Loop, rallies);
			}

			public PathPlan Reversed()
			{
				if (Loop)
					return new PathPlan(
						Direction.Reverse(End),
						Direction.Reverse(Start),
						Loop,
						Rallies.Skip(1).Append(Rallies[0]).Reverse().ToImmutableArray());
				else
					return new PathPlan(
						Direction.Reverse(End),
						Direction.Reverse(Start),
						Loop,
						Rallies.Reverse().ToImmutableArray());
			}

			/// <summary>
			/// Convert the rally points into a sequence of unit-space CPos points, suitable for
			/// processing with TilingPath. Returns null in some failure cases.
			/// </summary>
			public CPos[] Points()
			{
				return PointsWithRallyIndex().Select(pair => pair.CPos).ToArray();
			}

			/// <summary>
			/// Convert the rally points into a sequence of unit-space CPos points and their
			/// associated later rally index. For loops, the last rally index is the number of the
			/// rallies.
			/// </summary>
			public (CPos CPos, int RallyIndex)[] PointsWithRallyIndex()
			{
				if (Rallies.Length == 1)
					return [(Rallies[0], 0)];

				var points = new List<(CPos CPos, int RallyIndex)>();
				var cpos = Rallies[0];
				points.Add((cpos, 0));
				var inertia = Direction.ToCVec(AutoStart);
				if (inertia.X != 0 && inertia.Y != 0)
					inertia = new CVec(inertia.X, 0);
				
				void AddPointsUpTo(CPos target, int i)
				{
					if (cpos == target)
						throw new InvalidOperationException("there are duplicate rally points");

					var offset = target - cpos;
					var xStep = Math.Sign(offset.X);
					var yStep = Math.Sign(offset.Y);
					// (xStep and yStep cannot both be 0.)
					var axisAligned = xStep == 0 || yStep == 0;

					if (axisAligned)
					{
						while (cpos != target)
						{
							inertia = new CVec(xStep, yStep);
							cpos += inertia;
							points.Add((cpos, i));
						}
					}
					else
					{
						var xUnderModulo = Math.Abs(offset.Y);
						var yUnderModulo = Math.Abs(offset.X);
						// Technically, these range from 0 inclusive to modulo inclusive!
						var xModulo = xUnderModulo * 2;
						var yModulo = yUnderModulo * 2;

						if (xUnderModulo < yUnderModulo)
							inertia = new CVec(xStep, 0);
						else if (yUnderModulo > xUnderModulo)
							inertia = new CVec(0, yStep);
						else
							inertia =
								Direction.ToCVec(
									Direction.FromCVecNonDiagonal(
										inertia + new CVec(xStep * 2, yStep * 2)));

						while (cpos != target)
						{
							if (xUnderModulo < yUnderModulo)
							{
								yUnderModulo -= xUnderModulo;
								xUnderModulo = xModulo;
								inertia = new CVec(xStep, 0);
							}
							else if (xUnderModulo > yUnderModulo)
							{
								xUnderModulo -= yUnderModulo;
								yUnderModulo = yModulo;
								inertia = new CVec(0, yStep);
							}
							else if (inertia.X != 0) // equal
							{
								xUnderModulo = xModulo;
								yUnderModulo = 0;
							}
							else // equal, inertia.Y != 0
							{
								yUnderModulo = yModulo;
								xUnderModulo = 0;
							}
							cpos += inertia;
							points.Add((cpos, i));
						}
					}
				}

				for (var i = 1; i < Rallies.Length; i++)
					AddPointsUpTo(Rallies[i], i);

				if (Loop)
					AddPointsUpTo(Rallies[0], Rallies.Length);

				return points.ToArray();
			}
		}

		public readonly World World;
		ITiledTerrainRenderer terrainRenderer = null;
		public PathPlan Plan = null;
		public MultiBrush MultiBrush = null;
		public EditorBlitSource? CachedEditorBlitSource = null;
		readonly IReadOnlyList<MultiBrush> segmentedBrushes;
		public readonly ImmutableArray<string> segmentCategories;
		public readonly ImmutableArray<string> segmentTypes;
		public string StartType = "Clear";
		public string InnerCategory = "Cliff";
		public string EndType = "Clear";
		public bool ClosedLoops = true;
		public int RandomSeed = 0;

		bool disposed;

		public TilingPathTool(Actor self, TilingPathToolInfo info)
		{
			World = self.World;
			segmentedBrushes = MultiBrush.LoadCollection(World.Map, "Segmented");

			segmentCategories = segmentedBrushes
				.Where(b => b.Segment != null)
				.SelectMany<MultiBrush, string>(b => [b.Segment.Start, b.Segment.Inner, b.Segment.End])
				.Select(s => s.Split('.')[0])
				.Distinct()
				.Order()
				.ToImmutableArray();

			segmentTypes = segmentedBrushes
				.Where(b => b.Segment != null)
				.SelectMany<MultiBrush, string>(b => [b.Segment.Start, b.Segment.Inner, b.Segment.End])
				.Select(s => string.Join(".", s.Split('.').SkipLast(1)))
				.Distinct()
				.Order()
				.ToImmutableArray();
		}

		public void WorldLoaded(World w, WorldRenderer wr)
		{
			terrainRenderer = World.WorldActor.Trait<ITiledTerrainRenderer>();
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			if (disposed)
				return;

			disposed = true;
		}

		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			yield break;
		}

		bool IRenderAnnotations.SpatiallyPartitionable => false;

		MultiBrush PlanToBrush(PathPlan plan)
		{
			if (plan == null || plan.Rallies.Length < 2)
				return null;
			
			var points = plan.Points();
			if (points == null)
				return null;

			(string Start, string End)[] terminalTypes = [(StartType, EndType)];
			if (ClosedLoops && plan.Loop)
			{
				terminalTypes = segmentTypes
					.Where(t => t.Split('.')[0] == InnerCategory)
					.Select(t => (t, t))
					.ToArray();
			}

			var map = World.Map;
			var permittedTemplates =
				TilingPath.PermittedSegments.FromTypes(
					segmentedBrushes,
					terminalTypes.Select(t => t.Start),
					[InnerCategory],
					terminalTypes.Select(t => t.End));

			MultiBrush result = null;
			foreach (var (startType, endType) in terminalTypes)
			{
				TilingPath tilingPath = new TilingPath(
					map,
					points,
					5,
					startType,
					endType,
					permittedTemplates);
				tilingPath.Start.Direction = plan.AutoStart;
				tilingPath.End.Direction = plan.AutoEnd;
				result = tilingPath.Tile(new MersenneTwister(RandomSeed));
				if (result != null)
					break;
			}

			return result;
		}

		public void UpdatePlan(PathPlan plan)
		{
			Plan = plan;
			MultiBrush = PlanToBrush(plan);
			CachedEditorBlitSource = null;
		}
	}
}
