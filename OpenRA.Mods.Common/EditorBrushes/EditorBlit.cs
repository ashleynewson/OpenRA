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
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.EditorBrushes
{
	public readonly struct BlitTile
	{
		public readonly TerrainTile TerrainTile;
		public readonly ResourceTile ResourceTile;
		public readonly ResourceLayerContents? ResourceLayerContents;
		public readonly byte Height;

		public BlitTile(TerrainTile terrainTile, ResourceTile resourceTile, ResourceLayerContents? resourceLayerContents, byte height)
		{
			TerrainTile = terrainTile;
			ResourceTile = resourceTile;
			ResourceLayerContents = resourceLayerContents;
			Height = height;
		}
	}

	public readonly struct EditorBlitSource
	{
		public readonly CellRegion CellRegion;
		public readonly Dictionary<string, EditorActorPreview> Actors;
		public readonly Dictionary<CPos, BlitTile> Tiles;

		public EditorBlitSource(CellRegion cellRegion, Dictionary<string, EditorActorPreview> actors, Dictionary<CPos, BlitTile> tiles)
		{
			CellRegion = cellRegion;
			Actors = actors;
			Tiles = tiles;
		}
	}

	[Flags]
	public enum MapBlitFilters
	{
		None = 0,
		Terrain = 1,
		Resources = 2,
		Actors = 4,
		All = Terrain | Resources | Actors
	}

	/// <summary>
	/// Core implementation for EditorActions which overwrite a region of the map (such as
	/// copy-paste).
	/// </summary>
	public sealed class EditorBlit
	{
		readonly MapBlitFilters blitFilters;
		readonly IResourceLayer resourceLayer;
		readonly EditorActorLayer editorActorLayer;
		readonly EditorBlitSource blitSource;
		readonly EditorBlitSource undoBlitSource;
		readonly CPos blitPosition;
		readonly Map map;

		public EditorBlit(
			MapBlitFilters blitFilters,
			IResourceLayer resourceLayer,
			CPos blitPosition,
			Map map,
			EditorBlitSource blitSource,
			EditorActorLayer editorActorLayer)
		{
			this.blitFilters = blitFilters;
			this.resourceLayer = resourceLayer;
			this.blitSource = blitSource;
			this.blitPosition = blitPosition;
			this.editorActorLayer = editorActorLayer;
			this.map = map;

			undoBlitSource = CopySelectionContents();
		}

		/// <summary>
		/// TODO: This is pretty much repeated in MapEditorSelectionLogic.
		/// </summary>
		/// <returns>BlitSource containing map contents for this region.</returns>
		EditorBlitSource CopySelectionContents()
		{
			var selectionSize = blitSource.CellRegion.BottomRight - blitSource.CellRegion.TopLeft;
			var source = new CellCoordsRegion(blitPosition, blitPosition + selectionSize);
			var selection = new CellRegion(map.Grid.Type, blitPosition, blitPosition + selectionSize);

			var mapTiles = map.Tiles;
			var mapHeight = map.Height;
			var mapResources = map.Resources;

			var previews = new Dictionary<string, EditorActorPreview>();
			var tiles = new Dictionary<CPos, BlitTile>();

			foreach (var cell in source)
			{
				if (!mapTiles.Contains(cell))
					continue;

				var resourceLayerContents = resourceLayer?.GetResource(cell);
				tiles.Add(cell, new BlitTile(mapTiles[cell], mapResources[cell], resourceLayerContents, mapHeight[cell]));

				if (blitFilters.HasFlag(MapBlitFilters.Actors))
					foreach (var preview in editorActorLayer.PreviewsInCellRegion(selection.CellCoords))
						previews.TryAdd(preview.ID, preview);
			}

			return new EditorBlitSource(selection, previews, tiles);
		}

		public void Blit()
		{
			var sourcePos = blitSource.CellRegion.TopLeft;
			var blitVec = new CVec(blitPosition.X - sourcePos.X, blitPosition.Y - sourcePos.Y);

			if (blitFilters.HasFlag(MapBlitFilters.Actors))
			{
				// Clear any existing actors in the paste cells.
				var selectionSize = blitSource.CellRegion.BottomRight - blitSource.CellRegion.TopLeft;
				var blitRegion = new CellRegion(map.Grid.Type, blitPosition, blitPosition + selectionSize);
				foreach (var regionActor in editorActorLayer.PreviewsInCellRegion(blitRegion.CellCoords).ToList())
					editorActorLayer.Remove(regionActor);
			}

			foreach (var tileKeyValuePair in blitSource.Tiles)
			{
				var position = tileKeyValuePair.Key + blitVec;
				if (!map.Contains(position))
					continue;

				// Clear any existing resources.
				if (resourceLayer != null && blitFilters.HasFlag(MapBlitFilters.Resources))
					resourceLayer.ClearResources(position);

				var tile = tileKeyValuePair.Value;
				var resourceLayerContents = tile.ResourceLayerContents;

				if (blitFilters.HasFlag(MapBlitFilters.Terrain))
				{
					map.Tiles[position] = tile.TerrainTile;
					map.Height[position] = tile.Height;
				}

				if (blitFilters.HasFlag(MapBlitFilters.Resources) &&
					resourceLayerContents.HasValue &&
					!string.IsNullOrWhiteSpace(resourceLayerContents.Value.Type))
					resourceLayer.AddResource(resourceLayerContents.Value.Type, position, resourceLayerContents.Value.Density);
			}

			if (blitFilters.HasFlag(MapBlitFilters.Actors))
			{
				// Now place actors.
				foreach (var actorKeyValuePair in blitSource.Actors)
				{
					var selection = blitSource.CellRegion;
					var copy = actorKeyValuePair.Value.Export();
					var locationInit = copy.GetOrDefault<LocationInit>();
					if (locationInit != null)
					{
						var actorPosition = locationInit.Value + new CVec(blitPosition.X - selection.TopLeft.X, blitPosition.Y - selection.TopLeft.Y);
						if (!map.Contains(actorPosition))
							continue;

						copy.RemoveAll<LocationInit>();
						copy.Add(new LocationInit(actorPosition));
					}

					editorActorLayer.Add(copy);
				}
			}
		}

		public void Revert()
		{
			if (blitFilters.HasFlag(MapBlitFilters.Actors))
			{
				// Clear existing actors.
				foreach (var regionActor in editorActorLayer.PreviewsInCellRegion(undoBlitSource.CellRegion.CellCoords).ToList())
					editorActorLayer.Remove(regionActor);
			}

			foreach (var tileKeyValuePair in undoBlitSource.Tiles)
			{
				var position = tileKeyValuePair.Key;
				var tile = tileKeyValuePair.Value;
				var resourceLayerContents = tile.ResourceLayerContents;

				// Clear any existing resources.
				if (resourceLayer != null && blitFilters.HasFlag(MapBlitFilters.Resources))
					resourceLayer.ClearResources(position);

				if (blitFilters.HasFlag(MapBlitFilters.Terrain))
				{
					map.Tiles[position] = tile.TerrainTile;
					map.Height[position] = tile.Height;
				}

				if (blitFilters.HasFlag(MapBlitFilters.Resources) &&
					resourceLayerContents.HasValue &&
					!string.IsNullOrWhiteSpace(resourceLayerContents.Value.Type))
					resourceLayer.AddResource(resourceLayerContents.Value.Type, position, resourceLayerContents.Value.Density);
			}

			if (blitFilters.HasFlag(MapBlitFilters.Actors))
			{
				// Place actors back again.
				foreach (var actor in undoBlitSource.Actors.Values)
					editorActorLayer.Add(actor);
			}
		}

		public int TileCount() {
			return blitSource.Tiles.Count;
		}
	}
}
