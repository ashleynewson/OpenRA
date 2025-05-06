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
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.MapGenerator;
using OpenRA.Primitives;
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
			public readonly ImmutableArray<CPos> Rallies;

			public int AutoStart => 
				Start != Direction.None
					? Start
					: Rallies.Length >= 2
						? Direction.FromCVecNonDiagonal(Rallies[1] - Rallies[0])
						: Direction.None;
			public int AutoEnd => 
				End != Direction.None
					? End
					: Rallies.Length >= 2
						? Direction.FromCVecNonDiagonal(Rallies[^1] - Rallies[^2])
						: Direction.None;

			public PathPlan(CPos first)
			{
				Start = Direction.None;
				End = Direction.None;
				Rallies = [first];
			}

			public PathPlan(int start, int end, ImmutableArray<CPos> rallies)
			{
				if (rallies == null || rallies.Length == 0)
					throw new ArgumentException("rallies must have at least one point");

				Start = start;
				End = end;
				Rallies = rallies;
			}

			public PathPlan WithStart(int start)
			{
				return new PathPlan(start, End, Rallies);
			}

			public PathPlan WithEnd(int end)
			{
				return new PathPlan(Start, end, Rallies);
			}

			public PathPlan WithRallyAppended(CPos cpos)
			{
				return new PathPlan(Start, Direction.None, [..Rallies, cpos]);
			}

			public PathPlan WithRallyRemoved(int index)
			{
				if (Rallies.Length == 1)
					return null;

				return new PathPlan(
					index != 0 ? Start : Direction.None,
					index != (Rallies.Length - 1) ? End : Direction.None,
					[..Rallies[..index], ..Rallies[(index + 1)..]]);
			}

			public PathPlan WithRallyReplaced(int index, CPos cpos)
			{
				return new PathPlan(Start, End, [..Rallies[..index], cpos, ..Rallies[(index + 1)..]]);
			}

			public PathPlan WithRallyInserted(int index, CPos cpos)
			{
				return new PathPlan(Start, End, [..Rallies[..index], cpos, ..Rallies[index..]]);
			}

			public PathPlan Moved(CVec offset)
			{
				var rallies = Rallies.Select(r => r + offset).ToImmutableArray();
				return new PathPlan(Start, End, rallies);
			}

			public PathPlan Reversed()
			{
				return new PathPlan(
					Direction.Reverse(End),
					Direction.Reverse(Start),
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
			/// associated later rally index. Returns null in some failure cases.
			/// </summary>
			public (CPos CPos, int RallyIndex)[] PointsWithRallyIndex()
			{
				if (Rallies.Length == 1)
					return [(Rallies[0], 0)];

				var points = new List<(CPos CPos, int RallyIndex)>();
				var cpos = Rallies[0];
				points.Add((cpos, 0));
				var inertia = Direction.ToCVec(AutoStart);
				for (var i = 1; i < Rallies.Length; i++)
				{
					var target = Rallies[i];
					if (cpos == target)
						return null;

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

				return points.ToArray();
			}
		}

		public readonly World World;
		ITiledTerrainRenderer terrainRenderer = null;
		public bool Enabled = true;
		public bool PreviewEnabled = true;
		public bool AutoLoopEnabled = true;
		public PathPlan Plan = null;
		public MultiBrush PreviewBrush = null;
		readonly IReadOnlyList<MultiBrush> segmentedBrushes;
		readonly ITerrainInfo terrainInfo;
		public string StartCategory = "Clear";
		public string InnerCategory = "Cliff";
		public string EndCategory = "Clear";

		bool disposed;

		public TilingPathTool(Actor self, TilingPathToolInfo info)
		{
			World = self.World;
			segmentedBrushes = MultiBrush.LoadCollection(World.Map, "Segmented");
			// TODO: Better ModData sourcing?
			terrainInfo = World.Map.Rules.TerrainInfo;
		}

		public void WorldLoaded(World w, WorldRenderer wr)
		{
			terrainRenderer = World.WorldActor.Trait<ITiledTerrainRenderer>();
			// UpdatePlan(
			// 	new PathPlan(
			// 		Direction.R,
			// 		Direction.D,
			// 		[new(10, 10), new(20, 10), new(20, 20)]));
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
			// if (!Enabled || Plan == null)
			// 	yield break;

			// var map = World.Map;

			// if (terrainRenderer != null && PreviewBrush != null)
			// {
			// 	foreach (var (xy, tile) in PreviewBrush.Tiles)
			// 	{
			// 		var preview = terrainRenderer.RenderPreview(wr, tile, map.CenterOfCell(CPos.Zero + xy));
			// 		foreach (var renderable in preview)
			// 			yield return renderable;
			// 	}
			// }

			// var points = Plan.Points();
			// for (var i = 1; i < points.Length; i++)
			// {
			// 	yield return new CircleAnnotationRenderable(
			// 		map.CenterOfCell(points[i]), new WDist(128), 1, Color.Red, false);
			// 	yield return new LineAnnotationRenderable(
			// 		map.CenterOfCell(points[i - 1]),
			// 		map.CenterOfCell(points[i]),
			// 		1,
			// 		Color.Red,
			// 		Color.Red);
			// }

			// for (var i = 1; i < Plan.Rallies.Length; i++)
			// {
			// 	yield return new CircleAnnotationRenderable(
			// 		map.CenterOfCell(Plan.Rallies[i]), new WDist(512), 2, Color.Cyan, false);
			// 	yield return new LineAnnotationRenderable(
			// 		map.CenterOfCell(Plan.Rallies[i - 1]),
			// 		map.CenterOfCell(Plan.Rallies[i]),
			// 		2,
			// 		Color.Cyan,
			// 		Color.Cyan);
			// }

			// if (Plan.AutoEnd != Direction.None)
			// 	yield return new CircleAnnotationRenderable(
			// 		map.CenterOfCell(Plan.Rallies[^1]) + Direction.ToWVec(Plan.AutoEnd) * 768, new WDist(256), 2, Color.Magenta, false);

			// if (Plan.AutoStart != Direction.None)
			// 	yield return new CircleAnnotationRenderable(
			// 		map.CenterOfCell(Plan.Rallies[0]) - Direction.ToWVec(Plan.AutoStart) * 768, new WDist(256), 2, Color.Magenta, true);

			// yield return new CircleAnnotationRenderable(
			// 	map.CenterOfCell(Plan.Rallies[0]), new WDist(512), 2, Color.Cyan, true);

			// yield break;
		}

		bool IRenderAnnotations.SpatiallyPartitionable => false;

		MultiBrush PlanToBrush(PathPlan plan)
		{
			if (plan == null || plan.Rallies.Length < 2)
				return null;
			
			var points = plan.Points();
			if (points == null)
				return null;

			var map = World.Map;
			var permittedTemplates =
				TilingPath.PermittedSegments.FromTypes(
					segmentedBrushes, [StartCategory], [InnerCategory], [EndCategory]);

			var tilingPath = new TilingPath(
				map,
				points,
				5,
				StartCategory, /* TODO: Should these be categories or directionless types? */
				EndCategory,
				permittedTemplates);
			tilingPath.Start.Direction = plan.AutoStart;
			tilingPath.End.Direction = plan.AutoEnd;

			return tilingPath.Tile(new MersenneTwister(0));
		}

		public void UpdatePlan(PathPlan plan)
		{
			Plan = plan;
			PreviewBrush = PlanToBrush(plan);
		}
	}
}
