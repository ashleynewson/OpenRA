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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.MapUtils
{
	public sealed class ActorPlan
	{
		public readonly Map Map;
		public readonly ActorInfo Info;
		public readonly ActorReference Reference;
		// TODO: This concept is a bit weird here.
		// <summary>A radius for planning actor placement</summary>
		public float ZoningRadius;

		public CPos Location
		{
			get => Reference.Get<LocationInit>().Value;
			set
			{
				Reference.RemoveAll<LocationInit>();
				Reference.Add(new LocationInit(value));
			}
		}

		// <summary>
		// Int2 MPos-like representation of location.
		// </summary>
		public int2 Int2Location
		{
			get
			{
				var cpos = Reference.Get<LocationInit>().Value;
				var mpos = cpos.ToMPos(Map);
				return new int2(mpos.U, mpos.V);
			}
			set => Location = new MPos(value.X, value.Y).ToCPos(Map);
		}

		// <summary>
		// Float2 MPos-like representation of actor's center.
		// For example, A 1x4 actor will have +(0.5,2.0) offset to its Int2Location.
		// </summary>
		public float2 CenterLocation
		{
			get => Int2Location + CenterOffset();
			set
			{
				var float2Location = value - CenterOffset();
				Int2Location = new int2((int)MathF.Round(float2Location.X), (int)MathF.Round(float2Location.Y));
			}
		}

		// <summary>
		// Create an ActorPlan from a reference. The referenced actor becomes owned.
		// </summary>
		public ActorPlan(Map map, ActorReference reference)
		{
			Map = map;
			Reference = reference;
			if (!map.Rules.Actors.TryGetValue(Reference.Type.ToLowerInvariant(), out Info))
				throw new ArgumentException($"Actor of unknown type {Reference.Type.ToLowerInvariant()}");
		}

		// <summary>
		// Create an ActorPlan containing a new Neutral-owned actor of the given type.
		// </summary>
		public ActorPlan(Map map, string type)
			: this(map, ActorFromType(type))
		{ }

		// <summary>
		// Create a cloned actor plan, cloning the underlying ActorReference.
		// </summary>
		public ActorPlan Clone()
		{
			return new ActorPlan(Map, Reference.Clone())
			{
				ZoningRadius = ZoningRadius,
			};
		}

		static ActorReference ActorFromType(string type)
		{
			return new ActorReference(type)
			{
				new LocationInit(default),
				new OwnerInit("Neutral"),
			};
		}

		// <summary>
		// The footprint of the actor (influenced by its location).
		// </summary>
		public IReadOnlyDictionary<CPos, SubCell> Footprint()
		{
			var location = Location;
			var ios = Info.TraitInfoOrDefault<IOccupySpaceInfo>();
			var subCellInit = Reference.GetOrDefault<SubCellInit>();
			var subCell = subCellInit != null ? subCellInit.Value : SubCell.Any;

			var occupiedCells = ios?.OccupiedCells(Info, location, subCell);
			if (occupiedCells == null || occupiedCells.Count == 0)
				return new Dictionary<CPos, SubCell>() { { location, SubCell.FullCell } };
			else
				return occupiedCells;
		}

		// <summary>
		// Relocates the actor such that the top-most, left-most footprint
		// square is at (0, 0).
		// </summary>
		public ActorPlan AlignFootprint()
		{
			var footprint = Footprint();
			var first = footprint.Select(kv => kv.Key).OrderBy(cpos => (cpos.Y, cpos.X)).First();
			Location -= new CVec(first.X, first.Y);
			return this;
		}

		// <summary>
		// Return an MPos-like center offset for the actor.
		//
		// For example, for a 1x1 actor, this would be (0.5, 0.5)
		// <summary>
		public float2 CenterOffset()
		{
			var bi = Info.TraitInfoOrDefault<BuildingInfo>();
			if (bi == null)
				return new float2(0.5f, 0.5f);

			var left = int.MaxValue;
			var right = int.MinValue;
			var top = int.MaxValue;
			var bottom = int.MinValue;
			foreach (var (cvec, type) in bi.Footprint)
			{
				if (type == FootprintCellType.Empty)
					continue;
				var mpos = (new CPos(0, 0) + cvec).ToMPos(Map);
				left = Math.Min(left, mpos.U);
				top = Math.Min(top, mpos.V);
				right = Math.Max(right, mpos.U);
				bottom = Math.Max(bottom, mpos.V);
			}

			return new float2((left + right + 1) / 2.0f, (top + bottom + 1) / 2.0f);
		}
	}
}
