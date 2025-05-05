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
using System.Numerics;
using OpenRA.Graphics;
using OpenRA.Mods.Common.MapGenerator;
using OpenRA.Mods.Common.Traits;
using OpenRA.Support;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class TilingPathToolLogic : ChromeLogic
	{
		readonly TilingPathTool tool;
		readonly EditorViewportControllerWidget editorWidget;
		readonly EditorActionManager editorActionManager;
		readonly World world;
		readonly ModData modData;
		readonly WorldRenderer worldRenderer;
		readonly IReadOnlyList<MultiBrush> segmentedBrushes;

		[ObjectCreator.UseCtor]
		public TilingPathToolLogic(
			Widget widget,
			World world,
			ModData modData,
			WorldRenderer worldRenderer,
			Dictionary<string, MiniYaml> logicArgs)
		{
			tool = world.WorldActor.Trait<TilingPathTool>();
			editorActionManager = world.WorldActor.Trait<EditorActionManager>();

			editorWidget = widget.Parent.Parent.Parent.Parent.Get<EditorViewportControllerWidget>("MAP_EDITOR");
			this.world = world;
			this.modData = modData;
			this.worldRenderer = worldRenderer;
			segmentedBrushes = MultiBrush.LoadCollection(world.Map, "Segmented");

			var editCheckbox = widget.Get<CheckboxWidget>("EDIT");
			editCheckbox.IsChecked = () => editorWidget.CurrentBrush is EditorTilingPathBrush;
			editCheckbox.OnClick = () =>
				editorWidget.SetBrush(
					editCheckbox.IsChecked()
						? null
						: new EditorTilingPathBrush(editorWidget, worldRenderer));

			var resetButton = widget.Get<ButtonWidget>("RESET");
			resetButton.OnClick = () => Reset();

			var reverseButton = widget.Get<ButtonWidget>("REVERSE");
			reverseButton.OnClick = () => Reverse();

			var paintButton = widget.Get<ButtonWidget>("PAINT");
			paintButton.OnClick = () => Paint();
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
		}

		void Reset()
		{
			if (tool.Plan == null)
				return;

			editorActionManager.Add(
				new UpdateTilingPathPlanEditorAction(tool, null));
		}

		void Reverse()
		{
			if (tool.Plan == null)
				return;

			editorActionManager.Add(
				new UpdateTilingPathPlanEditorAction(tool, tool.Plan.Reversed()));
		}

		void Paint()
		{
			var plan = tool.Plan;
			if (plan == null)
				return;
			
			var points = plan.Points();
			if (points == null)
				return;

			var map = world.Map;
			var terrainInfo = modData.DefaultTerrainInfo[map.Tileset];
			var permittedTemplates =
				TilingPath.PermittedSegments.FromTypes(
					segmentedBrushes, ["Clear"], ["Cliff"], ["Clear"]);

			var tilingPath = new TilingPath(
				map,
				points,
				5,
				"Clear",
				"Clear",
				permittedTemplates);
			tilingPath.Start.Direction = plan.AutoStart;
			tilingPath.End.Direction = plan.AutoEnd;

			var brush = tilingPath.Tile(new MersenneTwister(0));
			if (brush == null)
				return;

			editorActionManager.Add(
				new PaintTilingPathEditorAction(tool, worldRenderer, brush));
		}
	}
}
