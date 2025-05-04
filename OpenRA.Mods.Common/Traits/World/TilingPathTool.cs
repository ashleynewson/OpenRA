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
		readonly World world;
		public bool Enabled = true;
		public bool PreviewEnabled = true;
		public bool AutoLoopEnabled = true;

		bool disposed;

		public TilingPathTool(Actor self, TilingPathToolInfo info)
		{
			world = self.World;
		}

		public void WorldLoaded(World w, WorldRenderer wr)
		{
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			if (disposed)
				return;

			disposed = true;
		}

		IEnumerable<IRenderable> IRenderAnnotations.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!Enabled)
				yield break;

			yield break;
		}

		bool IRenderAnnotations.SpatiallyPartitionable => false;
	}
}
