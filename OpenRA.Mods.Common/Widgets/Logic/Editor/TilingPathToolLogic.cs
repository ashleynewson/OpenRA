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
using OpenRA.Mods.Common.MapGenerator;
using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class TilingPathToolLogic : ChromeLogic
	{
		readonly TilingPathTool tool;
		readonly EditorActionManager editorActionManager;
		readonly EditorViewportControllerWidget editorWidget;
		readonly WorldRenderer worldRenderer;

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
			this.worldRenderer = worldRenderer;

			var editCheckbox = widget.Get<CheckboxWidget>("EDIT");

			editCheckbox.IsChecked = () => editorWidget.CurrentBrush is EditorTilingPathBrush;
			editCheckbox.OnClick = () =>
				editorWidget.SetBrush(
					editCheckbox.IsChecked()
						? null
						: new EditorTilingPathBrush(editorWidget, worldRenderer));
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
		}
	}
}
