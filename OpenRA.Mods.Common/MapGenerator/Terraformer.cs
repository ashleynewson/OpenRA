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
using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Support;
using static OpenRA.Mods.Common.Traits.ResourceLayerInfo;

namespace OpenRA.Mods.Common.MapGenerator
{
	/// <summary>Collection of high-level map generation utilities.</summary>
	public class Terraformer
	{
		/// <summary>Biases or excludes resources at a location during resource planning.</summary>
		public sealed class ResourceBias
		{
			/// <summary>The location of the bias.</summary>
			public WPos WPos;

			/// <summary>Resources will not be placed within this distance of the actor.</summary>
			public WDist? ExclusionRadius = null;

			/// <summary>Resources will be biased within this radius.</summary>
			public WDist? BiasRadius = null;

			/// <summary>
			/// Biasing function, applied either to all resources or the specific ResourceType.
			/// Maps the original value and the squared-WDist-from-CPos to a new value.
			/// </summary>
			public Func<int, long, int> Bias = null;

			/// <summary>If non-null, encourages resources to become this type.</summary>
			public ResourceTypeInfo ResourceType = null;

			/// <summary>Create a bias at a location.</summary>
			public ResourceBias(WPos wpos)
			{
				WPos = wpos;
			}

			/// <summary>Create a bias at an actor's location.</summary>
			public ResourceBias(ActorPlan actorPlan)
				: this(actorPlan.WPosCenterLocation)
			{ }
		}

		public readonly Map Map;
		public readonly ModData ModData;
		public readonly List<ActorPlan> ActorPlans;
		public readonly Symmetry.Mirror Mirror;
		public readonly int Rotations;

		readonly ITerrainInfo terrainInfo;

		public Terraformer(
			Map map,
			ModData modData,
			Symmetry.Mirror mirror,
			int rotations,
			List<ActorPlan> actorPlans)
		{
			this.Map = map;
			this.ModData = modData;
			this.Mirror = mirror;
			this.Rotations = rotations;
			this.ActorPlans = actorPlans;

			terrainInfo = modData.DefaultTerrainInfo[map.Tileset];
		}

		/// <summary>
		/// Enumerates through all current ActorPlans of the given type.
		/// </summary>
		public IEnumerable<ActorPlan> ActorsOfType(string type)
		{
			return ActorPlans.Where(a => a.Reference.Type == type);
		}

		/// <summary>
		/// <para>
		/// Produce an unbiased noise pattern for resource growth.
		/// </para><para>
		/// The output noise will have the range [uniformity, uniformity + 1024].
		/// </para>
		/// </summary>
		public CellLayer<int> GenerateResourcePattern(
			MersenneTwister random,
			int noiseFeatureSize,
			int clumpiness,
			int uniformity)
		{
			var pattern = new CellLayer<int>(Map);
			NoiseUtils.SymmetricFractalNoiseIntoCellLayer(
				random,
				pattern,
				Rotations,
				Mirror,
				noiseFeatureSize,
				wavelength => ClumpinessAmplitude(wavelength, clumpiness));
			{
				CellLayerUtils.CalibrateQuantileInPlace(
					pattern,
					0,
					0, 1);
				var max = pattern.Max();
				foreach (var mpos in Map.AllCells.MapCoords)
					pattern[mpos] = uniformity + 1024 * pattern[mpos] / max;
			}

			return pattern;
		}

		/// <summary>
		/// Given a resource noise pattern, produce plans for resource growth.
		/// Resources will be limitted to masked cells. Resources will only be placed on compatible
		/// terrain tiles and will avoid actor footprints.
		/// Resources can be biased towards or away from specified actors. Biases are applied in
		/// the order they are supplied, but all reservations take precedence.
		/// Resource type will be determined by proximity to resource spawn actors, or a default
		/// resource.
		/// </summary>
		public (CellLayer<int> Plan, CellLayer<ResourceTypeInfo> TypePlan) PlanResources(
			CellLayer<int> pattern,
			CellLayer<bool> mask,
			ResourceTypeInfo defaultResource,
			IReadOnlyList<ResourceBias> resourceBiases)
		{
			CheckHasMapShape(pattern);
			CheckHasMapShape(mask);

			// IReadOnlyDictionary<string, ResourceTypeInfo> resourceSpawnSeeds = ...;
			var resourceTypes = Map.Rules.Actors[SystemActors.World]
				.TraitInfoOrDefault<ResourceLayerInfo>()
				.ResourceTypes
					.OrderBy(kv => kv.Key)
					.Select(kv => kv.Value)
					.ToImmutableArray();
			var allowedTerrainResourceCombos = resourceTypes
				.SelectMany(resourceTypeInfo => resourceTypeInfo.AllowedTerrainTypes
					.Select(terrainName => (resourceTypeInfo, terrainInfo.GetTerrainIndex(terrainName))))
				.ToImmutableHashSet();

			var strengths = new Dictionary<ResourceTypeInfo, CellLayer<int>>();
			foreach (var resourceType in resourceTypes)
			{
				var strength = new CellLayer<int>(Map);
				strength.Clear(1);
				strengths.Add(resourceType, strength);
			}

			foreach (var bias in resourceBiases)
			{
				if (bias.Bias == null || bias.BiasRadius == null)
					continue;

				IEnumerable<ResourceTypeInfo> types = bias.ResourceType != null
					? [bias.ResourceType]
					: resourceTypes;
				foreach (var resourceType in types)
				{
					var strength = strengths[resourceType];
					CellLayerUtils.OverCircle(
						cellLayer: strength,
						wCenter: bias.WPos,
						wRadius: bias.BiasRadius.Value,
						outside: false,
						action: (mpos, _, _, wrSq) =>
							strength[mpos] = bias.Bias(strength[mpos], wrSq));
				}
			}

			foreach (var bias in resourceBiases)
			{
				if (bias.ExclusionRadius == null)
					continue;

				foreach (var resourceType in resourceTypes)
				{
					var strength = strengths[resourceType];
					CellLayerUtils.OverCircle(
						cellLayer: strength,
						wCenter: bias.WPos,
						wRadius: bias.ExclusionRadius.Value,
						outside: false,
						action: (mpos, _, _, wrSq) =>
							strength[mpos] = -int.MaxValue);
				}
			}

			var maxStrength1024ths = new CellLayer<int>(Map);
			maxStrength1024ths.Clear(1);
			var bestResource = new CellLayer<ResourceTypeInfo>(Map);
			bestResource.Clear(defaultResource);
			foreach (var resourceStrength in strengths)
			{
				var resource = resourceStrength.Key;
				var strength1024ths = resourceStrength.Value;
				foreach (var mpos in Map.AllCells.MapCoords)
					if (strength1024ths[mpos] > maxStrength1024ths[mpos])
					{
						maxStrength1024ths[mpos] = strength1024ths[mpos];
						bestResource[mpos] = resource;
					}
			}

			// Closer to +inf means "more preferable" for plan.
			var plan = new CellLayer<int>(Map);
			foreach (var mpos in Map.AllCells.MapCoords)
			{
				plan[mpos] = pattern[mpos] >= 0
					? pattern[mpos] * maxStrength1024ths[mpos]
					: -int.MaxValue;
			}

			foreach (var mpos in Map.AllCells.MapCoords)
				if (!mask[mpos] || !allowedTerrainResourceCombos.Contains((bestResource[mpos], Map.GetTerrainIndex(mpos))))
					plan[mpos] = -int.MaxValue;

			foreach (var actorPlan in ActorPlans)
				foreach (var (cpos, _) in actorPlan.Footprint())
					if (plan.Contains(cpos))
						plan[cpos] = -int.MaxValue;

			plan = ImproveSymmetry(plan, -int.MaxValue, int.Min);

			return (plan, bestResource);
		}

		/// <summary>
		/// Given a resource plan, place resources onto the map up to a target value.
		/// Resources are placed first on the pattern cells with the greatest value.
		/// No resources will be placed on pattern cells with a value less than 0.
		/// The plan should only contain values >= 0 where resource placement is legal.
		/// The type of resource placed is specified by typePlan.
		/// Any previously existing resources on the map will be cleared.
		/// </summary>
		public void GrowResources(
			CellLayer<int> plan,
			CellLayer<ResourceTypeInfo> typePlan,
			long targetValue)
		{
			CheckHasMapShape(plan);
			CheckHasMapShape(typePlan);

			var remaining = targetValue;

			var resourceTypes = Map.Rules.Actors[SystemActors.World].TraitInfoOrDefault<ResourceLayerInfo>().ResourceTypes;
			var playerResourcesInfo = Map.Rules.Actors[SystemActors.Player].TraitInfoOrDefault<PlayerResourcesInfo>();
			var resourceValues = playerResourcesInfo.ResourceValues
					.ToDictionary(kv => resourceTypes[kv.Key], kv => kv.Value);

			// Closer to -inf means "more preferable" for priorities.
			var priorities = new PriorityArray<int>(
				plan.Size.Width * plan.Size.Height,
				int.MaxValue);
			{
				var i = 0;
				foreach (var v in plan)
					priorities[i++] = -v;
			}

			int PriorityIndex(MPos mpos) => mpos.V * plan.Size.Width + mpos.U;
			MPos PriorityMPos(int index)
			{
				var v = Math.DivRem(index, plan.Size.Width, out var u);
				return new MPos(u, v);
			}

			Map.Resources.Clear();

			// Return resource value of a given square.
			// Matches the logic in ResourceLayer trait.
			int CheckValue(CPos cpos)
			{
				if (!Map.Resources.Contains(cpos))
					return 0;
				var resource = Map.Resources[cpos].Type;
				if (resource == 0)
					return 0;

				var resourceType = typePlan[cpos];

				var adjacent = 0;
				var directions = CVec.Directions;
				for (var i = 0; i < directions.Length; i++)
				{
					var c = cpos + directions[i];
					if (Map.Resources.Contains(c) && Map.Resources[c].Type == resource)
						++adjacent;
				}

				// We need to have at least one resource in the cell.
				// HACK: we should not be lerping to 9, as maximum adjacent resources is 8.
				// HACK: it's too disruptive to fix.
				var density = Math.Max(int2.Lerp(0, resourceType.MaxDensity, adjacent, 9), 1);

				return resourceValues[resourceType] * density;
			}

			int CheckValue3By3(CPos cpos)
			{
				var total = 0;
				for (var y = -1; y <= 1; y++)
					for (var x = -1; x <= 1; x++)
						total += CheckValue(cpos + new CVec(x, y));

				return total;
			}

			var gridType = Map.Grid.Type;

			// Set and return change in overall value.
			int AddResource(CPos cpos)
			{
				var mpos = cpos.ToMPos(gridType);
				priorities[PriorityIndex(mpos)] = int.MaxValue;

				// Generally shouldn't happen, but perhaps a rotation/mirror related inaccuracy.
				if (Map.Resources[mpos].Type != 0)
					return 0;

				var resourceType = typePlan[mpos];
				var oldValue = CheckValue3By3(cpos);
				Map.Resources[mpos] = new ResourceTile(
					resourceType.ResourceIndex,
					(byte)resourceType.MaxDensity);
				var newValue = CheckValue3By3(cpos);
				return newValue - oldValue;
			}

			while (remaining > 0)
			{
				var n = priorities.GetMinIndex();
				if (priorities[n] == int.MaxValue)
					break;

				var chosenMPos = PriorityMPos(n);
				var chosenCPos = chosenMPos.ToCPos(gridType);
				foreach (var cpos in Symmetry.RotateAndMirrorCPos(chosenCPos, plan, Rotations, Mirror))
					if (Map.Resources.Contains(cpos))
						remaining -= AddResource(cpos);
			}
		}

		/// <summary>Sets all zoneable cells where the map has resources to false.</summary>
		public void DezoneFromResources(CellLayer<bool> zoneable)
		{
			CheckHasMapShape(zoneable);

			foreach (var mpos in zoneable.CellRegion)
				if (Map.Resources[mpos].Type != 0)
					zoneable[mpos] = false;
		}

		/// <summary>
		/// Return a new CellLayer produced by aggregating projected cells from an input CellLayer.
		/// </summary>
		public CellLayer<T> ImproveSymmetry<T>(
			CellLayer<T> layer,
			T outsideValue,
			Func<T, T, T> aggregator)
		{
			CheckHasMapShape(layer);

			var newLayer = new CellLayer<T>(Map);
			Symmetry.RotateAndMirrorOverCPos(
				layer,
				Rotations,
				Mirror,
				(sources, destination)
					=> newLayer[destination] = sources
						.Select(source => layer.TryGetValue(source, out var value) ? value : outsideValue)
						.Aggregate(aggregator));
			return newLayer;
		}

		/// <summary>
		/// Commits draft data to the map, such as player and actor definitions.
		/// </summary>
		public void Bake()
		{
			var playerCount = ActorsOfType("mpspawn").Count();
			Map.PlayerDefinitions = new MapPlayers(Map.Rules, playerCount).ToMiniYaml();
			Map.ActorDefinitions = ActorPlans
				.Select((plan, i) => new MiniYamlNode($"Actor{i}", plan.Reference.Save()))
				.ToImmutableArray();
		}

		public void CheckHasMapShape<T>(CellLayer<T> layer)
		{
			if (!CellLayerUtils.AreSameShape(layer, Map.Tiles))
				throw new ArgumentException("CellLayer has different shape to map");
		}

		public static int ClumpinessAmplitude(int wavelength, int clumpiness)
		{
			var amplitude = wavelength;
			for (var i = 0; i < clumpiness; i++)
				amplitude = Exts.ISqrt(amplitude);
			return amplitude;
		}
	}
}
