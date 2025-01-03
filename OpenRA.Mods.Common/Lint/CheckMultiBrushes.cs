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
using OpenRA.Mods.Common.MapGenerator;
using OpenRA.Mods.Common.Terrain;

namespace OpenRA.Mods.Common.Lint
{
	public class CheckMultiBrushes : ILintMapPass
	{
		public void Run(Action<string> emitError, Action<string> emitWarning, ModData modData, Map map)
		{
			var TemplatedTerrainInfo = map.Rules.TerrainInfo as ITemplatedTerrainInfo;
			foreach (var kv in TemplatedTerrainInfo.MultiBrushCollections)
			{
				var name = kv.Key;
				var collection = kv.Value;
				foreach (var info in collection)
				{
					try {
						// Includes validation of actor types and template IDs.
						var multiBrush = new MultiBrush(map, info);

						// Validates there is at least something in the MultiBrush.
						multiBrush.Contract();

						foreach (var (_, tile) in multiBrush.Tiles)
						{
							if (!TemplatedTerrainInfo.TryGetTerrainInfo(tile, out var _))
								emitError($"Invalid MultiBrush collection `{name}`: Map's tileset does not contain tile {tile.Type},{tile.Index}");
						}
					}
					catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
					{
						emitError($"Invalid MultiBrush collection `{name}`: {e.Message}");
					}
				}
			}
		}
	}
}
