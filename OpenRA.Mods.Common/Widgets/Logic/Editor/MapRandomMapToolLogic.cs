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
using System.Diagnostics;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.EditorBrushes;
using OpenRA.Mods.Common.Traits;
using OpenRA.Support;
using OpenRA.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class MapRandomMapToolLogic : ChromeLogic
	{
		readonly EditorActionManager editorActionManager;
		readonly ButtonWidget generateButtonWidget;
		readonly ButtonWidget generateRandomButtonWidget;
		readonly TextFieldWidget seedTextFieldWidget;
		readonly DropDownButtonWidget presetsDropDownWidget;
		readonly World world;
		readonly WorldRenderer worldRenderer;
		readonly ModData modData;

		// nullable
		IMapGenerator selectedGenerator;

		// Should settings be part of the IMapGenerator itself?
		Dictionary<IMapGenerator, IEnumerable<MapGeneratorSetting>> generatorsToSettings;

		readonly ScrollPanelWidget settingsPanel;
		readonly Widget unknownSettingTemplate;
		readonly Widget sectionSettingTemplate;
		readonly Widget checkboxSettingTemplate;
		readonly Widget textSettingTemplate;
		readonly Widget dropDownSettingTemplate;

		[ObjectCreator.UseCtor]
		public MapRandomMapToolLogic(Widget widget, World world, WorldRenderer worldRenderer, ModData modData)
		{
			editorActionManager = world.WorldActor.Trait<EditorActionManager>();

			this.world = world;
			this.worldRenderer = worldRenderer;
			this.modData = modData;

			selectedGenerator = null;
			generatorsToSettings = new Dictionary<IMapGenerator, IEnumerable<MapGeneratorSetting>>();

			var mapGenerators = world.WorldActor.TraitsImplementing<IMapGenerator>().Where(generator => generator.ShowInEditor(world.Map, modData));

			generateButtonWidget = widget.Get<ButtonWidget>("GENERATE_BUTTON");
			generateRandomButtonWidget = widget.Get<ButtonWidget>("GENERATE_RANDOM_BUTTON");
			seedTextFieldWidget = widget.Get<TextFieldWidget>("SEED");
			presetsDropDownWidget = widget.Get<DropDownButtonWidget>("PRESETS");

			settingsPanel = widget.Get<ScrollPanelWidget>("SETTINGS_PANEL");
			unknownSettingTemplate = settingsPanel.Get<Widget>("UNKNOWN_TEMPLATE");
			sectionSettingTemplate = settingsPanel.Get<Widget>("SECTION_TEMPLATE");
			checkboxSettingTemplate = settingsPanel.Get<Widget>("CHECKBOX_TEMPLATE");
			textSettingTemplate = settingsPanel.Get<Widget>("TEXT_TEMPLATE");
			dropDownSettingTemplate = settingsPanel.Get<Widget>("DROPDOWN_TEMPLATE");

			generateButtonWidget.OnClick = GenerateMap;
			generateRandomButtonWidget.OnClick = RandomSeedThenGenerateMap;

			var generatorDropDown = widget.Get<DropDownButtonWidget>("GENERATOR");
			ChangeGenerator(mapGenerators.FirstOrDefault((IMapGenerator)null));
			if (selectedGenerator != null)
			{
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

				presetsDropDownWidget.OnMouseDown = _ =>
				{
					// TODO: Perhaps migrate to some MiniYAML defined structure.
					var presets = selectedGenerator.GetPresets(world.Map, modData)
						.Prepend(new KeyValuePair<string, string>(null, "Default settings"));
					ScrollItemWidget SetupItem(KeyValuePair<string, string> preset, ScrollItemWidget template)
					{
						bool IsSelected() => false;
						void OnClick()
						{
							generatorsToSettings[selectedGenerator] = selectedGenerator.GetPresetSettings(world.Map, modData, preset.Key);
							UpdateSettingsUi();
						}

						var item = ScrollItemWidget.Setup(template, IsSelected, OnClick);
						item.Get<LabelWidget>("LABEL").GetText = () => preset.Value;
						return item;
					}

					presetsDropDownWidget.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", presets.Count() * 30, presets, SetupItem);
				};
			}
			else
			{
				generateButtonWidget.IsDisabled = () => true;
				generateRandomButtonWidget.IsDisabled = () => true;
				seedTextFieldWidget.IsDisabled = () => true;
				presetsDropDownWidget.IsDisabled = () => true;
				generatorDropDown.IsDisabled = () => true;
			}
		}

		sealed class RandomMapEditorAction : IEditorAction
		{
			[TranslationReference("amount")]
			const string GeneratedRandomMap = "notification-generated-random-map";

			public string Text { get; }

			readonly EditorBlit editorBlit;

			public RandomMapEditorAction(EditorBlit editorBlit, string description)
			{
				this.editorBlit = editorBlit;

				Text = description;
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

		// newGenerator may be null.
		void ChangeGenerator(IMapGenerator newGenerator)
		{
			selectedGenerator = newGenerator;

			if (selectedGenerator != null && !generatorsToSettings.ContainsKey(selectedGenerator))
			{
				generatorsToSettings.Add(selectedGenerator, selectedGenerator.GetDefaultSettings(world.Map, modData));
			}

			UpdateSettingsUi();
		}

		void UpdateSettingsUi()
		{
			settingsPanel.RemoveChildren();
			settingsPanel.ContentHeight = 0;
			if (selectedGenerator == null) return;
			foreach (var setting in generatorsToSettings[selectedGenerator])
			{
				Widget settingWidget;
				switch (setting.Value)
				{
					case MapGeneratorSetting.SectionValue value:
					{
						settingWidget = sectionSettingTemplate.Clone();
						var label = settingWidget.Get<LabelWidget>("LABEL");
						label.GetText = () => setting.Label;
						break;
					}
					case MapGeneratorSetting.BooleanValue value:
					{
						settingWidget = checkboxSettingTemplate.Clone();
						var checkbox = settingWidget.Get<CheckboxWidget>("CHECKBOX");
						checkbox.GetText = () => setting.Label;
						checkbox.IsChecked = () => value.Value;
						checkbox.OnClick = () => value.Value = !value.Value;
						break;
					}
					case MapGeneratorSetting.StringValue value:
					{
						settingWidget = textSettingTemplate.Clone();
						var label = settingWidget.Get<LabelWidget>("LABEL");
						var input = settingWidget.Get<TextFieldWidget>("INPUT");
						label.GetText = () => setting.Label;
						input.Text = value.Value;
						input.OnTextEdited = () => value.Value = input.Text;
						break;
					}
					case MapGeneratorSetting.IntegerValue value:
					{
						settingWidget = textSettingTemplate.Clone();
						var label = settingWidget.Get<LabelWidget>("LABEL");
						var input = settingWidget.Get<TextFieldWidget>("INPUT");
						label.GetText = () => setting.Label;
						input.Text = value.Value.ToString();
						input.OnTextEdited = () =>
						{
							var valid = long.TryParse(input.Text, out value.Value);
							input.IsValid = () => valid;
						};
						break;
					}
					case MapGeneratorSetting.FloatValue value:
					{
						settingWidget = textSettingTemplate.Clone();
						var label = settingWidget.Get<LabelWidget>("LABEL");
						var input = settingWidget.Get<TextFieldWidget>("INPUT");
						label.GetText = () => setting.Label;
						input.Text = value.Value.ToString();
						input.OnTextEdited = () =>
						{
							var valid = double.TryParse(input.Text, out value.Value);
							input.IsValid = () => valid;
						};
						break;
					}
					case MapGeneratorSetting.EnumValue value:
					{
						settingWidget = dropDownSettingTemplate.Clone();
						var label = settingWidget.Get<LabelWidget>("LABEL");
						var dropDown = settingWidget.Get<DropDownButtonWidget>("DROPDOWN");
						label.GetText = () => setting.Label;
						dropDown.GetText = () => value.DisplayValue;
						dropDown.OnMouseDown = _ =>
						{
							ScrollItemWidget SetupItem(KeyValuePair<string, string> kv, ScrollItemWidget template)
							{
								bool IsSelected() => kv.Key == value.Value;
								void OnClick() => value.Value = kv.Key;
								var item = ScrollItemWidget.Setup(template, IsSelected, OnClick);
								item.Get<LabelWidget>("LABEL").GetText = () => kv.Value;
								return item;
							}

							dropDown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", value.Choices.Count * 30, value.Choices, SetupItem);
						};
						break;
					}
					default:
					{
						settingWidget = unknownSettingTemplate.Clone();
						// TODO: translate
						settingWidget.Get<LabelWidget>("PLACEHOLDER").GetText = () => $"(?) {setting.Label}";
						break;
					}
				}
				settingWidget.IsVisible = () => true;
				settingsPanel.AddChild(settingWidget);
			}
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

		void RandomSeedThenGenerateMap()
		{
			// Perhaps somewhat unsatisfactory?
			seedTextFieldWidget.Text = Environment.TickCount.ToString();
			GenerateMap();
		}

		void GenerateMap()
		{
			try
			{
				GenerateMapMayThrow();
			}
			catch (MapGenerationException e)
			{
				// TODO: present error, translate
				DisplayError(e);
			}
		}

		void GenerateMapMayThrow()
		{
			if (!int.TryParse(seedTextFieldWidget.Text, out var seed))
			{
				throw new MapGenerationException("Invalid seed.");
			}

			var random = new MersenneTwister(seed);
			var map = world.Map;
			var tileset = modData.DefaultTerrainInfo[map.Tileset];
			var generatedMap = new Map(modData, tileset, map.MapSize.X, map.MapSize.Y);
			var bounds = map.Bounds;
			generatedMap.SetBounds(new PPos(bounds.Left, bounds.Top), new PPos(bounds.Right - 1, bounds.Bottom - 1));
			var settings = generatorsToSettings[selectedGenerator];

			// Run main generator logic. May throw
			var generateStopwatch = Stopwatch.StartNew();
			Log.Write("debug", $"Running '{selectedGenerator.Info.Type}' map generator with seed {seed}");
			selectedGenerator.Generate(generatedMap, modData, random, settings);
			Log.Write("debug", $"Generator finished, taking {generateStopwatch.ElapsedMilliseconds}ms");

			var editorActorLayer = world.WorldActor.Trait<EditorActorLayer>();
			var resourceLayer = world.WorldActor.TraitOrDefault<IResourceLayer>();

			// Hack, hack, hack.
			var resourceTypesByIndex = (resourceLayer.Info as EditorResourceLayerInfo).ResourceTypes.ToDictionary(
				kv => kv.Value.ResourceIndex,
				kv => kv.Key);

			var tiles = new Dictionary<CPos, BlitTile>();
			foreach (var cell in generatedMap.AllCells)
			{
				var mpos = cell.ToMPos(map);
				var resourceTile = generatedMap.Resources[mpos];
				resourceTypesByIndex.TryGetValue(resourceTile.Type, out var resourceType);
				var resourceLayerContents = new ResourceLayerContents(resourceType, resourceTile.Index);
				tiles.Add(cell, new BlitTile(generatedMap.Tiles[mpos], resourceTile, resourceLayerContents, generatedMap.Height[mpos]));
			}

			var previews = new Dictionary<string, EditorActorPreview>();
			var players = generatedMap.PlayerDefinitions.Select(pr => new PlayerReference(new MiniYaml(pr.Key, pr.Value.Nodes)))
				.ToDictionary(player => player.Name);
			foreach (var kv in generatedMap.ActorDefinitions)
			{
				var actorReference = new ActorReference(kv.Value.Value, kv.Value.ToDictionary());
				var ownerInit = actorReference.Get<OwnerInit>();
				if (!players.TryGetValue(ownerInit.InternalName, out var owner))
				{
					// TODO: present error, translate
					throw new MapGenerationException("Generator produced mismatching player and actor definitions.");
				}
				var preview = new EditorActorPreview(worldRenderer, kv.Key, actorReference, owner);
				previews.Add(kv.Key, preview);
			}

			var blitSource = new EditorBlitSource(generatedMap.AllCells, previews, tiles);
			var editorBlit = new EditorBlit(
				MapBlitFilters.All,
				resourceLayer,
				new CPos(0, 0),
				map,
				blitSource,
				editorActorLayer,
				false);
			// TODO: translate
			// TranslationProvider.GetString(GeneratedRandomMap)
			var description = $"Generate {selectedGenerator.Info.Name} map ({seed})";
			var action = new RandomMapEditorAction(editorBlit, description);
			editorActionManager.Add(action);
		}
	}
}
