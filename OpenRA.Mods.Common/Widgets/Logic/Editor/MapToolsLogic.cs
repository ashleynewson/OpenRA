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
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class MapToolsLogic : ChromeLogic
	{
		[TranslationReference]
		const string MarkerTiles = "label-tool-marker-tiles";
		[TranslationReference]
		const string RandomMap = "label-tool-random-map";

		enum MapTool
		{
			MarkerTiles,
			RandomMap
		}

		readonly DropDownButtonWidget toolsDropdown;
		readonly Dictionary<MapTool, string> toolNames = new()
		{
			{ MapTool.MarkerTiles, MarkerTiles },
			{ MapTool.RandomMap, RandomMap }
		};

		readonly Dictionary<MapTool, Widget> toolPanels = new();

		MapTool selectedTool = MapTool.MarkerTiles;

		[ObjectCreator.UseCtor]
		public MapToolsLogic(Widget widget, World world, ModData modData, WorldRenderer worldRenderer, Dictionary<string, MiniYaml> logicArgs)
		{
			toolsDropdown = widget.Get<DropDownButtonWidget>("TOOLS_DROPDOWN");

			var markerToolPanel = widget.Get("MARKER_TOOL_PANEL");
			toolPanels.Add(MapTool.MarkerTiles, markerToolPanel);
			var randomMapToolPanel = widget.Get<ScrollPanelWidget>("RANDOM_MAP_TOOL_PANEL");
			toolPanels.Add(MapTool.RandomMap, randomMapToolPanel);

			toolsDropdown.OnMouseDown = _ => ShowToolsDropDown(toolsDropdown);
			toolsDropdown.GetText = () => TranslationProvider.GetString(toolNames[selectedTool]);
		}

		void ShowToolsDropDown(DropDownButtonWidget dropdown)
		{
			ScrollItemWidget SetupItem(MapTool tool, ScrollItemWidget itemTemplate)
			{
				var item = ScrollItemWidget.Setup(itemTemplate,
					() => selectedTool == tool,
					() => SelectTool(tool));

				item.Get<LabelWidget>("LABEL").GetText = () => TranslationProvider.GetString(toolNames[tool]);

				return item;
			}

			var options = new[] { MapTool.MarkerTiles, MapTool.RandomMap };
			dropdown.ShowDropDown("LABEL_DROPDOWN_TEMPLATE", 150, options, SetupItem);
		}

		void SelectTool(MapTool tool)
		{
			if (tool != selectedTool)
			{
				var currentToolPanel = toolPanels[selectedTool];
				currentToolPanel.Visible = false;
			}

			selectedTool = tool;

			var toolPanel = toolPanels[selectedTool];
			toolPanel.Visible = true;
		}
	}
}
