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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic
{
	public class TilingPathToolLogic : ChromeLogic
	{
		readonly TilingPathTool tool;
		readonly EditorViewportControllerWidget editorWidget;
		readonly EditorActionManager editorActionManager;

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

			var editCheckbox = widget.Get<CheckboxWidget>("EDIT");
			editCheckbox.Disabled = !tool.Available;
			if (!tool.Available)
				return;

			editCheckbox.IsChecked = () => editorWidget.CurrentBrush is EditorTilingPathBrush;
			editCheckbox.OnClick = () =>
				editorWidget.SetBrush(
					editCheckbox.IsChecked()
						? null
						: new EditorTilingPathBrush(tool));

			void SetupDropDown(
				string name,
				ImmutableArray<string> choices,
				Func<string> read,
				Action<string> write)
			{
				var dropDown = widget
					.Get<ContainerWidget>(name)
					.Get<DropDownButtonWidget>("DROPDOWN");
				dropDown.GetText = read;
				dropDown.OnMouseDown = _ =>
				{
					ScrollItemWidget SetupItem(string choice, ScrollItemWidget template)
					{
						bool IsSelected() => choice == read();
						void OnClick() => write(choice);
						var item = ScrollItemWidget.Setup(template, IsSelected, OnClick);
						item.Get<LabelWidget>("LABEL").GetText = () => choice;
						return item;
					}

					dropDown.ShowDropDown("LABEL_DROPDOWN_WITH_TOOLTIP_TEMPLATE", choices.Length * 30, choices, SetupItem);
				};
			}

			SetupDropDown("START_TYPE", tool.StartTypes, () => tool.StartType, tool.SetStartType);
			SetupDropDown("INNER_TYPE", tool.InnerTypes, () => tool.InnerType, tool.SetInnerType);
			SetupDropDown("END_TYPE", tool.EndTypes, () => tool.EndType, tool.SetEndType);

			var deviationSlider = widget.Get<ContainerWidget>("DEVIATION").Get<SliderWidget>("SLIDER");
			deviationSlider.GetValue = () => tool.MaxDeviation;
			deviationSlider.OnChange += (value) => tool.SetMaxDeviation((int)value);

			var closedLoopsCheckbox = widget.Get<CheckboxWidget>("CLOSED_LOOPS");
			closedLoopsCheckbox.IsChecked = () => tool.ClosedLoops;
			closedLoopsCheckbox.OnClick = () => tool.SetClosedLoops(!tool.ClosedLoops);

			var resetButton = widget.Get<ButtonWidget>("RESET");
			resetButton.OnClick = () =>
			{
				if (tool.Plan == null)
					return;

				editorActionManager.Add(
					new UpdateTilingPathPlanEditorAction(tool, null));
			};

			var reverseButton = widget.Get<ButtonWidget>("REVERSE");
			reverseButton.OnClick = () =>
			{
				if (tool.Plan == null)
					return;

				editorActionManager.Add(
					new UpdateTilingPathPlanEditorAction(tool, tool.Plan.Reversed()));
			};

			var randomizeButton = widget.Get<ButtonWidget>("RANDOMIZE");
			randomizeButton.OnClick = () => tool.SetRandomSeed(Environment.TickCount);

			var paintButton = widget.Get<ButtonWidget>("PAINT");
			paintButton.OnClick = () =>
			{
				if (tool.EditorBlitSource == null)
					return;

				editorActionManager.Add(new PaintTilingPathEditorAction(tool));
			};
		}
	}
}
