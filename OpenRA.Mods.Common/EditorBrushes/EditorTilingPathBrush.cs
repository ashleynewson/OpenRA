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
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.EditorBrushes;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.MapGenerator;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Widgets
{
	public sealed class EditorTilingPathBrush : IEditorBrush
	{
		readonly WorldRenderer worldRenderer;
		readonly World world;
		readonly EditorActionManager editorActionManager;
		readonly TilingPathTool tool;
		readonly EditorViewportControllerWidget editorWidget;
		readonly ITiledTerrainRenderer terrainRenderer;

		PaintMarkerTileEditorAction action;
		bool painting;

		MouseInput? startingMouseInput = null;

		public EditorTilingPathBrush(EditorViewportControllerWidget editorWidget, WorldRenderer wr)
		{
			this.editorWidget = editorWidget;
			worldRenderer = wr;
			world = wr.World;

			editorActionManager = world.WorldActor.Trait<EditorActionManager>();
			terrainRenderer = world.WorldActor.Trait<ITiledTerrainRenderer>();
			tool = world.WorldActor.Trait<TilingPathTool>();
		}

		public bool HandleMouseInput(MouseInput mouseInput)
		{
			if (mouseInput.Button != MouseButton.Left)
				return false;

			if (mouseInput.Event == MouseInputEvent.Down)
			{
				startingMouseInput = mouseInput;
				return true;
			}
			else if (mouseInput.Event == MouseInputEvent.Up && startingMouseInput != null)
			{
				var from = worldRenderer.Viewport.ViewToWorld(
					((MouseInput)startingMouseInput).Location);
				var to = worldRenderer.Viewport.ViewToWorld(
					mouseInput.Location);

				var isDrag = to != from;
				var plan = tool.Plan;

				if (plan == null)
				{
					if (!isDrag)
						UpdatePlan(new TilingPathTool.PathPlan(to));
					return true;
				}

				var points = plan.PointsWithRallyIndex();

				(bool IsInside, bool IsRally, int RallyIndex, bool isStartDirector, bool isEndDirector)
				AssessCPos(CPos cpos)
				{
					var isInside = points.Select(p => p.CPos).Contains(cpos);
					var isRally = plan.Rallies.Contains(cpos);
					var rallyIndex =
						points
							.Where(p => p.CPos == cpos)
							.Select(p => p.RallyIndex)
							.LastOrDefault(0);
					var isStartDirector =
						plan.AutoStart != Direction.None
							&& cpos == plan.Rallies[0] - Direction.ToCVec(plan.AutoStart);
					var isEndDirector =
						plan.AutoEnd != Direction.None
							&& cpos == plan.Rallies[^1] + Direction.ToCVec(plan.AutoEnd);
					return (isInside, isRally, rallyIndex, isStartDirector, isEndDirector);

				}
				var (fromIsInside, fromIsRally, fromRallyIndex, fromIsStartDirector, fromIsEndDirector) =
					AssessCPos(from);
				var (toIsInside, toIsRally, toRallyIndex, toIsStartDirector, toIsEndDirector) =
					AssessCPos(to);

				if (isDrag)
				{
					if (fromIsStartDirector)
					{
						var offset = plan.Rallies[0] - to;
						var direction =
							offset != CVec.Zero
								? Direction.FromCVecNonDiagonal(offset)
								: Direction.None;
						UpdatePlan(plan.WithStart(direction));
					}
					else if (fromIsEndDirector)
					{
						var offset = to - plan.Rallies[^1];
						var direction =
							offset != CVec.Zero
								? Direction.FromCVecNonDiagonal(offset)
								: Direction.None;
						UpdatePlan(plan.WithEnd(direction));
					}
					else if (fromIsInside)
					{
						if (fromIsRally)
						{
							if (!toIsRally)
							{
								UpdatePlan(plan.WithRallyReplaced(fromRallyIndex, to));
							}
							// Not allowed to drag rallies onto each other.
						}
						else
						{
							UpdatePlan(plan.Moved(to - from));
						}
						// Find start director
						// Find end director
					}
				}
				else
				{
					if (toIsInside)
					{
						if (toIsRally)
						{
							UpdatePlan(plan.WithRallyRemoved(toRallyIndex));
						}
						else
						{
							UpdatePlan(plan.WithRallyInserted(toRallyIndex, to));
						}
					}
					else
					{
						// Find existing rally (ignore)
						// Find existing link
						// Outside, so add new rally.
						UpdatePlan(plan.WithRallyAppended(to));
					}
				}
				return true;
			}
			else
			{
				return false;
			}
		}

		void IEditorBrush.TickRender(WorldRenderer wr, Actor self) { }
		IEnumerable<IRenderable> IEditorBrush.RenderAboveShroud(Actor self, WorldRenderer wr)
		{
			var map = world.Map;

			if (terrainRenderer == null || tool.PreviewBrush == null)
				yield break;

			foreach (var (xy, tile) in tool.PreviewBrush.Tiles)
			{
				var preview = terrainRenderer.RenderPreview(wr, tile, map.CenterOfCell(CPos.Zero + xy));
				foreach (var renderable in preview)
					yield return renderable;
			}
		}

		IEnumerable<IRenderable> IEditorBrush.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			var map = world.Map;
			var plan = tool.Plan;
			if (plan == null)
				yield break;

			var mainColor = tool.PreviewBrush != null ? Color.Cyan : Color.Red;

			var points = plan.Points();
			for (var i = 1; i < points.Length; i++)
			{
				yield return new CircleAnnotationRenderable(
					map.CenterOfCell(points[i]), new WDist(128), 1, Color.Yellow, false);
				yield return new LineAnnotationRenderable(
					map.CenterOfCell(points[i - 1]),
					map.CenterOfCell(points[i]),
					1,
					Color.Yellow,
					Color.Yellow);
			}

			for (var i = 1; i < plan.Rallies.Length; i++)
			{
				yield return new CircleAnnotationRenderable(
					map.CenterOfCell(plan.Rallies[i]), new WDist(512), 2, mainColor, false);
				yield return new LineAnnotationRenderable(
					map.CenterOfCell(plan.Rallies[i - 1]),
					map.CenterOfCell(plan.Rallies[i]),
					2,
					mainColor,
					mainColor);
			}

			if (plan.AutoEnd != Direction.None)
				yield return new CircleAnnotationRenderable(
					map.CenterOfCell(plan.Rallies[^1]) + Direction.ToWVec(plan.AutoEnd) * 768, new WDist(256), 2, Color.Magenta, false);

			if (plan.AutoStart != Direction.None)
				yield return new CircleAnnotationRenderable(
					map.CenterOfCell(plan.Rallies[0]) - Direction.ToWVec(plan.AutoStart) * 768, new WDist(256), 2, Color.Magenta, true);

			yield return new CircleAnnotationRenderable(
				map.CenterOfCell(plan.Rallies[0]), new WDist(512), 2, mainColor, true);
		}

		public void Tick() { }

		public void Dispose() { }

		void UpdatePlan(TilingPathTool.PathPlan newPlan)
		{
			editorActionManager.Add(
				new UpdateTilingPathPlanEditorAction(tool, newPlan));
		}
	}

	sealed class UpdateTilingPathPlanEditorAction : IEditorAction
	{
		[FluentReference]
		const string StartedPlan = "notification-tiling-path-started";
		[FluentReference]
		const string UpdatedPlan = "notification-tiling-path-updated";
		[FluentReference]
		const string ResetPlan = "notification-tiling-path-reset";

		public string Text { get; }

		readonly TilingPathTool tool;
		readonly TilingPathTool.PathPlan oldPlan;
		readonly TilingPathTool.PathPlan newPlan;

		public UpdateTilingPathPlanEditorAction(
			TilingPathTool tool,
			TilingPathTool.PathPlan newPlan)
		{
			this.tool = tool;
			this.oldPlan = tool.Plan;
			this.newPlan = newPlan;
			if (oldPlan == null && newPlan == null)
				throw new ArgumentException("oldPlan and newPlan cannot both be null");
			else if (oldPlan == null)
				Text = FluentProvider.GetMessage(StartedPlan);
			else if (newPlan == null)
				Text = FluentProvider.GetMessage(ResetPlan);
			else
				Text = FluentProvider.GetMessage(UpdatedPlan);
		}

		public void Execute()
		{
			Do();
		}

		public void Do()
		{
			tool.UpdatePlan(newPlan);
		}

		public void Undo()
		{
			tool.UpdatePlan(oldPlan);
		}
	}

	sealed class PaintTilingPathEditorAction : IEditorAction
	{
		[FluentReference]
		const string Painted = "notification-tiling-path-painted";

		public string Text { get; }

		readonly TilingPathTool tool;
		readonly TilingPathTool.PathPlan plan;
		readonly EditorBlit editorBlit;

		public PaintTilingPathEditorAction(
			TilingPathTool tool,
			WorldRenderer worldRenderer,
			MultiBrush brush)
		{
			this.tool = tool;
			plan = tool.Plan;
			Text = FluentProvider.GetMessage(Painted);

			var world = tool.World;
			var editorActorLayer = world.WorldActor.Trait<EditorActorLayer>();
			if (editorActorLayer == null)
				throw new ArgumentException("World has no EditorActorLayer");				var blitSource = brush.ToEditorBlitSource(world, worldRenderer);

			editorBlit = new EditorBlit(
				MapBlitFilters.Terrain | MapBlitFilters.Actors,
				null,
				CPos.Zero + brush.TopLeft,
				world.Map,
				blitSource,
				editorActorLayer,
				false);
		}

		public void Execute()
		{
			Do();
		}

		public void Do()
		{
			tool.UpdatePlan(null);
			editorBlit.Commit();
		}

		public void Undo()
		{
			editorBlit.Revert();
			tool.UpdatePlan(plan);
		}
	}
}
