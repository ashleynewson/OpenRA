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

using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Widgets
{
	public sealed class EditorTilingPathBrush : IEditorBrush
	{
		readonly WorldRenderer worldRenderer;
		readonly World world;
		readonly EditorActionManager editorActionManager;
		readonly TilingPathTool tool;
		readonly EditorViewportControllerWidget editorWidget;

		PaintMarkerTileEditorAction action;
		bool painting;

		public EditorTilingPathBrush(EditorViewportControllerWidget editorWidget, WorldRenderer wr)
		{
			this.editorWidget = editorWidget;
			worldRenderer = wr;
			world = wr.World;

			editorActionManager = world.WorldActor.Trait<EditorActionManager>();
			tool = world.WorldActor.Trait<TilingPathTool>();
		}

		public bool HandleMouseInput(MouseInput mi)
		{
			return true;
		}

		void IEditorBrush.TickRender(WorldRenderer wr, Actor self) { }
		IEnumerable<IRenderable> IEditorBrush.RenderAboveShroud(Actor self, WorldRenderer wr) { yield break; }
		IEnumerable<IRenderable> IEditorBrush.RenderAnnotations(Actor self, WorldRenderer wr) { yield break; }

		public void Tick() { }

		public void Dispose() { }
	}

	sealed class EditPlanEditorAction : IEditorAction
	{
		[FluentReference]
		const string UpdatedPlan = "notification-tiling-path-updated";

		public string Text { get; }

		readonly TilingPathTool tool;
		// old
		// new

		public EditPlanEditorAction(
			TilingPathTool tool)
		{
			this.tool = tool;
			Text = FluentProvider.GetMessage(UpdatedPlan);
		}

		public void Execute()
		{
			Do();
		}

		public void Do()
		{
		}

		public void Undo()
		{
		}
	}

	sealed class PaintTilingPathEditorAction : IEditorAction
	{
		[FluentReference]
		const string Painted = "notification-tiling-path-painted";

		public string Text { get; }

		readonly TilingPathTool tool;
		// old
		// new

		public PaintTilingPathEditorAction(
			TilingPathTool tool)
		{
			this.tool = tool;
			Text = FluentProvider.GetMessage(Painted);
		}

		public void Execute()
		{
			Do();
		}

		public void Do()
		{
		}

		public void Undo()
		{
		}
	}
}
