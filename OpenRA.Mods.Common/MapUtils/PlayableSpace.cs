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

namespace OpenRA.Mods.Common.MapUtils
{
	public static class PlayableSpace
	{
		public enum Playability
		{
			// Area is unplayable by land/naval units.
			Unplayable = 0,

			// Area is unplayable by land/naval units, but should count as
			// being "within" a playable region. This usually applies to random
			// rock or river tiles in largely passable templates.
			Partial = 1,

			// Area is playable by either land or naval units.
			Playable = 2,
		}

		// <summary>
		// Additional data for a region containing playable space.
		// </summary>
		public sealed class Region
		{
			// <summary>Area of playable and partially playable space.</summary>
			public int Area;
			// <summary>Area of fully playable space.</summary>
			public int PlayableArea;
			// <summary>Region ID.</summary>
			public int Id;
			public bool ExternalCircle;
		}

		// <summary>
		// Analyses a given map's tiles and ActorPlans and determines the playable space within it.
		//
		// Requires a playabilityMap which specifies whether certain tiles are considered playable
		// or not. Actors are always considered partially playable.
		// </summary>
		public static (Matrix<int> RegionMask, Region[] Regions, Matrix<Playability> Playable) FindPlayableRegions(
			Map map,
			List<ActorPlan> actorPlans,
			Dictionary<TerrainTile, Playability> playabilityMap)
		{
			var size = map.MapSize;
			var regions = new List<Region>();
			var regionMask = new Matrix<int>(size);
			var playable = new Matrix<Playability>(size);
			for (var y = 0; y < size.Y; y++)
			{
				for (var x = 0; x < size.X; x++)
				{
					playable[x, y] = playabilityMap[map.Tiles[new MPos(x, y)]];
				}
			}

			var externalCircle = new Matrix<bool>(size);
			var externalCircleCenter = (size - new float2(1.0f, 1.0f)) / 2.0f;
			var minSpan = Math.Min(size.X, size.Y);
			externalCircle.DrawCircle(
				center: externalCircleCenter,
				radius: minSpan / 2.0f - 1.0f,
				setTo: (_, _) => true,
				invert: true);
			MatrixUtils.ReserveForEntitiesInPlace(
				playable,
				actorPlans,
				(old) => old == Playability.Playable ? Playability.Partial : old);
			void Fill(Region region, int2 start)
			{
				void AddToRegion(int2 xy, bool fullyPlayable)
				{
					regionMask[xy] = region.Id;
					region.Area++;
					if (fullyPlayable)
						region.PlayableArea++;
					if (externalCircle[xy])
						region.ExternalCircle = true;
				}

				bool? Filler(int2 xy, bool fullyPlayable, int _)
				{
					if (regionMask[xy] == 0)
					{
						if (fullyPlayable && playable[xy] == Playability.Playable)
						{
							AddToRegion(xy, true);
							return true;
						}
						else if (playable[xy] == Playability.Partial)
						{
							AddToRegion(xy, false);
							return false;
						}
					}

					return null;
				}

				MatrixUtils.FloodFill(size, new[] { (start, true, Direction.NONE) }, Filler, Direction.SPREAD4_D);
			}

			for (var y = 0; y < size.Y; y++)
			{
				for (var x = 0; x < size.X; x++)
				{
					var start = new int2(x, y);
					if (regionMask[start] == 0 && playable[start] == Playability.Playable)
					{
						var region = new Region()
						{
							Area = 0,
							PlayableArea = 0,
							Id = regions.Count + 1,
							ExternalCircle = false,
						};
						regions.Add(region);
						Fill(region, start);
					}
				}
			}
			return (regionMask, regions.ToArray(), playable);
		}
	}
}
