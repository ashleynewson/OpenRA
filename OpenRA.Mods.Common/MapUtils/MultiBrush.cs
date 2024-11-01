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
using OpenRA.Mods.Common.Terrain;
using OpenRA.Support;

namespace OpenRA.Mods.Common.MapUtils
{
	/// <summary>A super template that can be used to paint both tiles and actors.</summary>
	sealed class MultiBrush
	{
		public enum Replaceability
		{
			/// <summary>Area cannot be replaced by a tile or obstructing actor.</summary>
			None = 0,

			/// <summary>Area must be replaced by a different tile, and may optionally be given an actor.</summary>
			Tile = 1,

			/// <summary>Area must be given an actor, but the underlying tile must not change.</summary>
			Actor = 2,

			/// <summary>Area can be replaced by a tile and/or actor.</summary>
			Any = 3,
		}

		public float Weight;
		public readonly Map Map;
		public readonly ModData ModData;
		readonly List<(int2, TerrainTile)> tiles;
		readonly List<ActorPlan> actorPlans;
		int2[] shape;

		public IEnumerable<(int2 XY, TerrainTile Tile)> Tiles => tiles;
		public IEnumerable<ActorPlan> ActorPlans => actorPlans;
		public bool HasTiles => tiles.Count != 0;
		public bool HasActors => actorPlans.Count != 0;
		public IEnumerable<int2> Shape => shape;
		public int Area => shape.Length;
		public Replaceability Contract()
		{
			var hasTiles = tiles.Count != 0;
			var hasActorPlans = actorPlans.Count != 0;
			if (hasTiles && hasActorPlans)
				return Replaceability.Any;
			else if (hasTiles && !hasActorPlans)
				return Replaceability.Tile;
			else if (!hasTiles && hasActorPlans)
				return Replaceability.Actor;
			else
				throw new ArgumentException("MultiBrush has no tiles or actors");
		}

		/// <summary>
		/// Create a new empty MultiBrush with a default weight of 1.0.
		/// </summary>
		public MultiBrush(Map map, ModData modData)
		{
			Weight = 1.0f;
			Map = map;
			ModData = modData;
			tiles = new List<(int2, TerrainTile)>();
			actorPlans = new List<ActorPlan>();
			shape = Array.Empty<int2>();
		}

		MultiBrush(MultiBrush other)
		{
			Weight = other.Weight;
			Map = other.Map;
			ModData = other.ModData;
			tiles = new List<(int2, TerrainTile)>(other.tiles);
			actorPlans = new List<ActorPlan>(other.actorPlans);
			shape = other.shape.ToArray();
		}

		/// <summary>
		/// Clone the brush. Note that this does not deep clone any ActorPlans.
		/// </summary>
		public MultiBrush Clone()
		{
			return new MultiBrush(this);
		}

		void UpdateShape()
		{
			var xys = new HashSet<int2>();

			foreach (var (xy, _) in tiles)
			{
				xys.Add(xy);
			}

			foreach (var actorPlan in actorPlans)
			{
				foreach (var cpos in actorPlan.Footprint())
				{
					var mpos = cpos.Key.ToMPos(Map);
					xys.Add(new int2(mpos.U, mpos.V));
				}
			}

			shape = xys.OrderBy(xy => (xy.Y, xy.X)).ToArray();
		}

		/// <summary>
		/// Add tiles from a template, optionally with a given offset. By
		/// default, it will be auto-offset such that the first tile is
		/// under (0, 0).
		/// </summary>
		public MultiBrush WithTemplate(ushort templateId, int2? offset = null)
		{
			var tileset = ModData.DefaultTerrainInfo[Map.Tileset] as ITemplatedTerrainInfo;
			var templateInfo = tileset.Templates[templateId];
			if (templateInfo.PickAny)
				throw new ArgumentException("PickAny not supported - create separate obstacles instead.");
			for (var y = 0; y < templateInfo.Size.Y; y++)
			{
				for (var x = 0; x < templateInfo.Size.X; x++)
				{
					var i = y * templateInfo.Size.X + x;
					if (templateInfo[i] != null)
					{
						if (offset == null)
							offset = new int2(-x, -y);
						var tile = new TerrainTile(templateId, (byte)i);
						tiles.Add((new int2(x, y) + (int2)offset, tile));
					}
				}
			}

			UpdateShape();
			return this;
		}

		/// <summary>Add a single tile at (0, 0).</summary>
		public MultiBrush WithTile(TerrainTile tile)
		{
			tiles.Add((new int2(0, 0), tile));
			UpdateShape();
			return this;
		}

		/// <summary>Add an actor at (0, 0).</summary>
		public MultiBrush WithActor(ActorPlan actor)
		{
			actorPlans.Add(actor);
			UpdateShape();
			return this;
		}

		/// <summary>
		/// <para>For all spaces occupied by the brush, add the given tile.</para>
		/// <para>This is useful for adding a backing tile for actors.</para>
		/// </summary>
		public MultiBrush WithBackingTile(TerrainTile tile)
		{
			if (Area == 0)
				throw new InvalidOperationException("No area");
			foreach (var xy in shape)
			{
				tiles.Add((xy, tile));
			}

			return this;
		}

		/// <summary>Update the weight.</summary>
		public MultiBrush WithWeight(float weight)
		{
			Weight = weight;
			return this;
		}

		/// <summary>
		/// <para>Paint tiles onto the map and/or add actors to actorPlans at the given location.</para>
		/// <para>contract specifies whether tiles or actors are allowed to be painted.</para>
		/// <para>If nothing could be painted, throws ArgumentException.</para>
		/// </summary>
		public void Paint(List<ActorPlan> actorPlans, int2 paintXY, Replaceability contract)
		{
			switch (contract)
			{
				case Replaceability.None:
					throw new ArgumentException("Cannot paint: Replaceability.None");
				case Replaceability.Any:
					if (this.actorPlans.Count > 0)
						PaintActors(actorPlans, paintXY);
					else if (tiles.Count > 0)
						PaintTiles(paintXY);
					else
						throw new ArgumentException("Cannot paint: no tiles or actors");
					break;
				case Replaceability.Tile:
					if (tiles.Count == 0)
						throw new ArgumentException("Cannot paint: no tiles");
					PaintTiles(paintXY);
					PaintActors(actorPlans, paintXY);
					break;
				case Replaceability.Actor:
					if (this.actorPlans.Count == 0)
						throw new ArgumentException("Cannot paint: no actors");
					PaintActors(actorPlans, paintXY);
					break;
			}
		}

		void PaintTiles(int2 paintXY)
		{
			foreach (var (xy, tile) in tiles)
			{
				var mpos = new MPos(paintXY.X + xy.X, paintXY.Y + xy.Y);
				if (Map.Contains(mpos))
					Map.Tiles[mpos] = tile;
			}
		}

		void PaintActors(List<ActorPlan> actorPlans, int2 paintXY)
		{
			foreach (var actorPlan in this.actorPlans)
			{
				var plan = actorPlan.Clone();
				var paintUV = new MPos(paintXY.X, paintXY.Y);
				var offset = plan.Location;
				plan.Location = paintUV.ToCPos(Map) + new CVec(offset.X, offset.Y);
				actorPlans.Add(plan);
			}
		}

		/// <summary>
		/// Paint an area defined by replace onto map and actorPlans using availableBrushes.
		/// </summary>
		public static void PaintArea(
			Map map,
			List<ActorPlan> actorPlans,
			Matrix<Replaceability> replace,
			IReadOnlyList<MultiBrush> availableBrushes,
			MersenneTwister random)
		{
			var brushesByAreaDict = new Dictionary<int, List<MultiBrush>>();
			foreach (var brush in availableBrushes)
			{
				if (!brushesByAreaDict.ContainsKey(brush.Area))
					brushesByAreaDict.Add(brush.Area, new List<MultiBrush>());
				brushesByAreaDict[brush.Area].Add(brush);
			}

			var brushesByArea = brushesByAreaDict
				.OrderBy(kv => -kv.Key)
				.ToList();
			var brushTotalArea = availableBrushes.Sum(t => t.Area);
			var brushTotalWeight = availableBrushes.Sum(t => t.Weight);

			// Give 1-by-1 actors the final pass, as they are most flexible.
			brushesByArea.Add(
				new KeyValuePair<int, List<MultiBrush>>(
					1,
					availableBrushes.Where(o => o.HasActors && o.Area == 1).ToList()));
			var size = map.MapSize;
			var replaceIndices = new List<int>();
			var remaining = new Matrix<bool>(size);
			for (var n = 0; n < replace.Data.Length; n++)
			{
				if (replace[n] != Replaceability.None)
				{
					remaining[n] = true;
					replaceIndices.Add(n);
				}
				else
				{
					remaining[n] = false;
				}
			}

			var indices = new int[replace.Data.Length];
			int indexCount;

			void RefreshIndices()
			{
				indexCount = 0;
				foreach (var n in replaceIndices)
				{
					if (remaining[n])
					{
						indices[indexCount] = n;
						indexCount++;
					}
				}

				random.ShuffleInPlace(indices, 0, indexCount);
			}

			Replaceability ReserveShape(int2 paintXY, IEnumerable<int2> shape, Replaceability contract)
			{
				foreach (var shapeXY in shape)
				{
					var xy = paintXY + shapeXY;
					if (!replace.ContainsXY(xy))
						continue;
					if (!remaining[xy])
					{
						// Can't reserve - not the right shape
						return Replaceability.None;
					}

					contract &= replace[xy];
					if (contract == Replaceability.None)
					{
						// Can't reserve - obstruction choice doesn't comply
						// with replaceability of original tiles.
						return Replaceability.None;
					}
				}

				// Can reserve. Commit.
				foreach (var shapeXY in shape)
				{
					var xy = paintXY + shapeXY;
					if (!replace.ContainsXY(xy))
						continue;

					remaining[xy] = false;
				}

				return contract;
			}

			foreach (var brushesKv in brushesByArea)
			{
				var brushes = brushesKv.Value;
				if (brushes.Count == 0)
					continue;

				var brushArea = brushes[0].Area;
				var brushWeights = brushes.Select(o => o.Weight).ToArray();
				var brushWeightForArea = brushWeights.Sum();
				var remainingQuota =
					brushArea == 1
						? int.MaxValue
						: (int)Math.Ceiling(replaceIndices.Count * brushWeightForArea / brushTotalWeight);
				RefreshIndices();
				foreach (var n in indices)
				{
					var brush = brushes[random.PickWeighted(brushWeights)];
					var paintXY = replace.XY(n);
					var contract = ReserveShape(paintXY, brush.Shape, brush.Contract());
					if (contract != Replaceability.None)
					{
						brush.Paint(actorPlans, paintXY, contract);
					}

					remainingQuota -= brushArea;
					if (remainingQuota <= 0)
						break;
				}
			}
		}
	}
}
