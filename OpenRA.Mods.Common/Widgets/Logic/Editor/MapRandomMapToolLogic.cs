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
using OpenRA.Graphics;
using OpenRA.Mods.Common.EditorBrushes;
using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class MapRandomMapToolLogic : ChromeLogic
	{
		readonly EditorActionManager editorActionManager;
		readonly ButtonWidget clearButtonWidget;
		// readonly EditorViewportControllerWidget editor;

		readonly World world;

		[ObjectCreator.UseCtor]
		public MapRandomMapToolLogic(Widget widget, World world, ModData modData)
		{
			editorActionManager = world.WorldActor.Trait<EditorActionManager>();

			this.world = world;

			// editor = widget.Parent.Parent.Parent.Parent.Get<EditorViewportControllerWidget>("MAP_EDITOR");

			clearButtonWidget = widget.Get<ButtonWidget>("CLEAR_BUTTON");
			clearButtonWidget.OnClick = ClearMap;
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
		}

		sealed class RandomMapEditorAction : IEditorAction
		{
			[TranslationReference("amount")]
			const string GeneratedRandomMap = "notification-generated-random-map";

			public string Text { get; }

			readonly EditorBlit editorBlit;

			public RandomMapEditorAction(EditorBlit editorBlit)
			{
				this.editorBlit = editorBlit;

				Text = TranslationProvider.GetString(GeneratedRandomMap);
			}

			public void Execute()
			{
				Do();
			}

			public void Do()
			{
				editorBlit.Blit();
			}

			public void Undo()
			{
				editorBlit.Revert();
			}
		}

		void ClearMap()
		{
			var map = world.Map;
			var editorActorLayer = world.WorldActor.Trait<EditorActorLayer>();
			var resourceLayer = world.WorldActor.TraitOrDefault<IResourceLayer>();

			var source = new CellCoordsRegion(new CPos(0, 0), new CPos(map.MapSize.X, map.MapSize.Y));
			var selection = new CellRegion(map.Grid.Type, new CPos(0, 0), new CPos(map.MapSize.X, map.MapSize.Y));

			var mapTiles = map.Tiles;
			var mapHeight = map.Height;
			var mapResources = map.Resources;

			var previews = new Dictionary<string, EditorActorPreview>();
			var tiles = new Dictionary<CPos, BlitTile>();

			foreach (var cell in source)
			{
				// var resourceLayerContents = resourceLayer?.GetResource(cell);
				// var tile = 0;
				// var index = (byte)(i % 4 + j % 4 * 4);
				// Tiles[new MPos(i, j)] = new TerrainTile(tile, index);
				tiles.Add(cell, new BlitTile(new TerrainTile(255, 0), new ResourceTile(0, 0), null, 0));
			}

			var blitSource = new EditorBlitSource(selection, previews, tiles);
			var editorBlit = new EditorBlit(
				MapBlitFilters.All,
				resourceLayer,
				new CPos(1, 1),
				map,
				blitSource,
				editorActorLayer);
			var action = new RandomMapEditorAction(editorBlit);
			editorActionManager.Add(action);
		}
	}
}
