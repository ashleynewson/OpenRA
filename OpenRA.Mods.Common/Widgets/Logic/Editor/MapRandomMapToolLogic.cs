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
using System.Net;
using OpenRA.Graphics;
using OpenRA.Mods.Common.EditorBrushes;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class MapRandomMapToolLogic : ChromeLogic
	{
		readonly EditorActionManager editorActionManager;
		readonly ButtonWidget generateButtonWidget;
		// readonly EditorViewportControllerWidget editor;

		readonly World world;
		readonly WorldRenderer worldRenderer;
		readonly ModData modData;

		// nullable
		IMapGenerator selectedGenerator;

		[ObjectCreator.UseCtor]
		public MapRandomMapToolLogic(Widget widget, World world, WorldRenderer worldRenderer, ModData modData)
		{
			editorActionManager = world.WorldActor.Trait<EditorActionManager>();

			this.world = world;
			this.worldRenderer = worldRenderer;
			this.modData = modData;

			selectedGenerator = null;

			var mapGenerators = world.WorldActor.TraitsImplementing<IMapGenerator>().Where(generator => generator.ShowInEditor(world.Map, modData));

			generateButtonWidget = widget.Get<ButtonWidget>("GENERATE_BUTTON");
			generateButtonWidget.OnClick = GenerateMap;

			var generatorDropDown = widget.Get<DropDownButtonWidget>("GENERATOR");
			if (mapGenerators.Any()) {
				generateButtonWidget.IsDisabled = () => false;
				generatorDropDown.IsDisabled = () => false;
				selectedGenerator = mapGenerators.First();
				generatorDropDown.GetText = () => selectedGenerator.Info.Name;
				generatorDropDown.OnMouseDown = _ =>
				{
					ScrollItemWidget SetupItem(IMapGenerator g, ScrollItemWidget template)
					{
						bool IsSelected() => g.Info.Type == selectedGenerator.Info.Type;
						void OnClick() => ChangeGenerator(mapGenerators.Where(generator => generator.Info.Type == g.Info.Type).First());
						var item = ScrollItemWidget.Setup(template, IsSelected, OnClick);
						item.Get<LabelWidget>("LABEL").GetText = () => g.Info.Name;
						return item;
					}

					generatorDropDown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", mapGenerators.Count() * 30, mapGenerators, SetupItem);
				};
			} else {
				generateButtonWidget.IsDisabled = () => true;
				generatorDropDown.IsDisabled = () => true;
				// TODO: translate
				generatorDropDown.GetText = () => "No generators available";
			}
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

		void ChangeGenerator(IMapGenerator newGenerator)
		{
			selectedGenerator = newGenerator;
		}

		void DisplayError(MapGenerationException e)
		{
			Log.Write("debug", e);
			// TODO: translate
			ConfirmationDialogs.ButtonPrompt(modData,
				title: "Map generation failed",
				text: e.Message,
				onCancel: () => {},
				cancelText: "Dismiss");
		}

		void GenerateMap()
		{
			var map = world.Map;
			var tileset = modData.DefaultTerrainInfo[map.Tileset];
			var generatedMap = new Map(modData, tileset, map.MapSize.X, map.MapSize.Y);
			try
			{
				selectedGenerator.Generate(generatedMap, modData);
			} catch (MapGenerationException e)
			{
				// TODO: present error, translate
				DisplayError(e);
				return;
			}

			var editorActorLayer = world.WorldActor.Trait<EditorActorLayer>();
			var resourceLayer = world.WorldActor.TraitOrDefault<IResourceLayer>();

			var tiles = new Dictionary<CPos, BlitTile>();
			foreach (var cell in generatedMap.AllCells)
			{
				var mpos = cell.ToMPos(map);
				tiles.Add(cell, new BlitTile(generatedMap.Tiles[mpos], generatedMap.Resources[mpos], null, generatedMap.Height[mpos]));
			}

			var previews = new Dictionary<string, EditorActorPreview>();
			var players = generatedMap.PlayerDefinitions.Select(pr => new PlayerReference(new MiniYaml(pr.Key, pr.Value.Nodes)))
				.ToDictionary(player => player.Name);
			foreach (var kv in world.Map.ActorDefinitions)
			{
				var actorReference = new ActorReference(kv.Value.Value, kv.Value.ToDictionary());
				var ownerInit = actorReference.Get<OwnerInit>();
				if (!players.TryGetValue(ownerInit.InternalName, out var owner))
				{
					// TODO: present error, translate
					DisplayError(new MapGenerationException("Generator produced mismatching player and actor definitions."));
					return;
				}
				var preview = new EditorActorPreview(worldRenderer, kv.Key, actorReference, owner);
				previews.Add(kv.Key, preview);
			}

			var blitSource = new EditorBlitSource(generatedMap.AllCells, previews, tiles);
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
