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
using OpenRA.Mods.Common.Terrain;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Support;
using static OpenRA.Mods.Common.Traits.ResourceLayerInfo;

namespace OpenRA.Mods.Common.MapGenerator
{
	/// <summary>Collection of high-level map generation utilities.</summary>
	public class Terraformer
	{
		/// <summary>Common denominator for fractional arguments.</summary>
		const int FractionMax = 1000;

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

		/// <summary>
		/// Optional Terraformer parameters with general use across utilities.
		/// </summary>
		public struct Params
		{
			public Symmetry.Mirror Mirror = Symmetry.Mirror.None;
			public int Rotations = 1;
			// TODO: Clean up what doesn't get used.
			public int? LandTile;
			public IReadOnlySet<byte> ClearTerrain;
			public IReadOnlySet<byte> PlayableTerrain;

			public Params() { }
		}

		public readonly Map Map;
		public readonly ModData ModData;
		public readonly List<ActorPlan> ActorPlans;
		public readonly Params param;

		readonly ITerrainInfo terrainInfo;

		public Terraformer(
			Map map,
			ModData modData,
			List<ActorPlan> actorPlans,
			Params parameters)
		{
			this.Map = map;
			this.ModData = modData;
			this.ActorPlans = actorPlans;
			param = parameters;

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
		/// Plan paths for roads that travel through the middle of playable space.
		/// </summary>
		/// <param name="availableSpace">Space in which roads are permitted.</param>
		/// <param name="minimumSpacing">Minimum distance that roads must be from the edges of available space.</param>
		/// <param name="minimumLength">Roads shorter than this will be merged or pruned.</param>
		public CPos[][] PlanRoads(
			CellLayer<bool> availableSpace,
			int minimumSpacing,
			int minimumLength)
		{
			// For awkward symmetries, we try harder to make sure roads are fairer.
			// This can degrade the quantity of roads, though.
			var imperfectSymmetry =
				param.Mirror != Symmetry.Mirror.None ||
				param.Rotations == 3 ||
				param.Rotations >= 5;
			var gridType = Map.Grid.Type;
			var wMapCenter = CellLayerUtils.Center(Map.Tiles);

			// Enlargement must increase dimensions by multiple of 4 to maximize compatibility
			// with IsometricRectangular grids, where a non-multiple of 4 would change how the
			// center aligns with the grid.
			var enlargedSize = new Size(
				Map.MapSize.Width + (Map.MapSize.Width & ~3) + 4,
				Map.MapSize.Height + (Map.MapSize.Height & ~3) + 4);

			var space = new CellLayer<bool>(gridType, enlargedSize);
			space.Clear(true);

			var enlargedOffset =
				CellLayerUtils.WPosToCPos(CellLayerUtils.Center(space), gridType)
					- CellLayerUtils.WPosToCPos(CellLayerUtils.Center(Map.Tiles), gridType);

			foreach (var cpos in Map.AllCells)
				space[cpos + enlargedOffset] = availableSpace[cpos];

			ImproveSymmetry(space, true, (a, b) => a && b);

			var matrixSpace = CellLayerUtils.ToMatrix(space, true);
			var kernel = new Matrix<bool>(minimumSpacing * 2 + 1, minimumSpacing * 2 + 1);
			MatrixUtils.OverCircle(
				matrix: kernel,
				centerIn1024ths: kernel.Size * 512,
				radiusIn1024ths: minimumSpacing * 1024,
				outside: false,
				action: (xy, _) => kernel[xy] = true);
			var dilated = MatrixUtils.KernelDilateOrErode(
				matrixSpace,
				kernel,
				new int2(minimumSpacing, minimumSpacing),
				false);
			var deflated = MatrixUtils.DeflateSpace(dilated, true);

			if (imperfectSymmetry)
			{
				var changing = true;
				while (changing)
				{
					changing = false;

					// Delete short paths.
					{
						MatrixUtils.RemoveStubsFromDirectionMapInPlace(deflated);
						var paths = MatrixUtils.DirectionMapToPaths(deflated);
						if (paths.Length == 0)
							break;

						var minLength = paths.Min(p => p.Length);
						if (minLength < minimumLength)
						{
							changing = true;
							var shortPaths = paths
								.Where(path => path.Length == minLength);
							foreach (var path in shortPaths)
								foreach (var point in path)
									deflated[point] = 0;
							MatrixUtils.RemoveStubsFromDirectionMapInPlace(deflated);
						}
					}

					// Prune asymmetric paths.
					{
						const int Dilation = 3;
						var nearPath = MatrixUtils.KernelDilateOrErode(
							deflated.Map(v => v != 0),
							new Matrix<bool>(Dilation * 2 + 1, Dilation * 2 + 1).Fill(true),
							new int2(Dilation, Dilation),
							true);
						var matrixPaths = MatrixUtils.DirectionMapToPaths(deflated);
						foreach (var path in matrixPaths)
						{
							var cposPath = CellLayerUtils.FromMatrixPoints([path], space)[0];
							var projectedPoints = cposPath
								.SelectMany(p => Symmetry.RotateAndMirrorCPos(p, space, param.Rotations, param.Mirror))
								.ToArray();
							var matrixPoints = CellLayerUtils.ToMatrixPoints([projectedPoints], space)[0];
							if (!matrixPoints.All(p => !nearPath.ContainsXY(p) || nearPath[p]))
							{
								// The path doesn't exist across all symmetries (or isn't consistent enough).
								changing = true;
								foreach (var point in path)
									deflated[point] = 0;
							}
						}
					}
				}
			}

			var matrixPointArrays = MatrixUtils.DirectionMapToPathsWithPruning(
				input: deflated,
				minimumLength: minimumLength,
				minimumJunctionSeparation: 6,
				preserveEdgePaths: true);
			var pointArrays = CellLayerUtils.FromMatrixPoints(matrixPointArrays, space);
			pointArrays = TilingPath.RetainDisjointPaths(pointArrays);
			pointArrays = pointArrays
				.Select(a => a.Select(p => p - enlargedOffset).ToArray())
				.Select(a => TilingPath.ChirallyNormalizePathPoints(a, cvec => CellLayerUtils.CornerToWPos(cvec, gridType) - wMapCenter))
				.ToArray();

			return pointArrays;
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
				param.Rotations,
				param.Mirror,
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
				foreach (var cpos in Symmetry.RotateAndMirrorCPos(chosenCPos, plan, param.Rotations, param.Mirror))
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
			var newLayer = new CellLayer<T>(layer.GridType, layer.Size);
			Symmetry.RotateAndMirrorOverCPos(
				layer,
				param.Rotations,
				param.Mirror,
				(sources, destination)
					=> newLayer[destination] = sources
						.Select(source => layer.TryGetValue(source, out var value) ? value : outsideValue)
						.Aggregate(aggregator));
			return newLayer;
		}

		/// <summary>
		/// Returns a CellLayer describing whether the space in a map satisfies given terrain types
		/// (if allowedTerrain is non-null), is free of actors, and/or is free of resources.
		/// </summary>
		public CellLayer<bool> CheckSpace(
			IReadOnlySet<byte> allowedTerrain,
			bool checkActors = false,
			bool checkResources = false)
		{
			var templatedTerrainInfo = (ITemplatedTerrainInfo)terrainInfo;

			var space = new CellLayer<bool>(Map);
			if (allowedTerrain != null)
			{
				foreach (var mpos in Map.AllCells.MapCoords)
					space[mpos] = allowedTerrain.Contains(templatedTerrainInfo.GetTerrainIndex(Map.Tiles[mpos]));
			}
			else
			{
				space.Clear(true);
			}

			if (checkActors)
				DezoneFromActors(space);

			if (checkResources)
				DezoneFromResources(space);

			return space;
		}

		/// <summary>
		/// Returns a CellLayer describing whether the space in a map has the given tile type and
		/// is free of actors and/or resources.
		/// </summary>
		public CellLayer<bool> CheckSpace(
			ushort requiredTile,
			bool checkActors = false,
			bool checkResources = false)
		{
			var space = new CellLayer<bool>(Map);
			foreach (var mpos in Map.AllCells.MapCoords)
				space[mpos] = Map.Tiles[mpos].Type == requiredTile;

			if (checkActors)
				DezoneFromActors(space);

			if (checkResources)
				DezoneFromResources(space);

			return space;
		}

		/// <summary>Sets all zoneable cells where the map has actor footprints to false.</summary>
		public void DezoneFromActors(CellLayer<bool> zoneable)
		{
			foreach (var actorPlan in ActorPlans)
				foreach (var (cpos, _) in actorPlan.Footprint())
					if (zoneable.Contains(cpos))
						zoneable[cpos] = false;
		}

		/// <summary>
		/// Creates mask for placing decorations in out-of-the-way locations on a map.
		/// </summary>
		/// <param name="random">Random source for layout and tiling.</param>
		/// <param name="space">Space that decorations must not significantly choke.</param>
		/// <param name="zoneable">Cells where decoration is allowed.</param>
		/// <param name="coverage">Maximum fraction of map to cover in decorations.</param>
		/// <param name="featureSize">Noise feature size for layout.</param>
		/// <param name="density">Density of decoration layout.</param>
		/// <param name="minimumDensity">
		/// Enforces a minimum local density of decorations. This can be used, for example, to
		/// ensure that villages have a substantial size, preventing lonely buildings. Decoration
		/// cells are removed until minimum
		/// </param>
		/// <param name="CivilianBuildingDensityRadius">Enforcement radius of minimum density</param>
		public CellLayer<bool> DecorationPattern(
			MersenneTwister random,
			CellLayer<bool> space,
			CellLayer<bool> zoneable,
			int coverage,
			int featureSize,
			int density,
			int minimumDensity,
			int CivilianBuildingDensityRadius)
		{
			CheckHasMapShape(space);
			CheckHasMapShape(zoneable);

			var matrixSpace = CellLayerUtils.ToMatrix(space, true);
			var deflated = MatrixUtils.DeflateSpace(matrixSpace, false);
			var kernel = new Matrix<bool>(2, 2).Fill(true);
			var reservedMatrix = MatrixUtils.KernelDilateOrErode(deflated.Map(v => v != 0), kernel, new int2(0, 0), true);
			var reserved = new CellLayer<bool>(Map);
			CellLayerUtils.FromMatrix(reserved, reservedMatrix, true);

			var decorationNoise = new CellLayer<int>(Map);
			NoiseUtils.SymmetricFractalNoiseIntoCellLayer(
				random,
				decorationNoise,
				param.Rotations,
				param.Mirror,
				featureSize,
				wavelength => 1);

			var densityNoise = new CellLayer<int>(Map);
			NoiseUtils.SymmetricFractalNoiseIntoCellLayer(
				random,
				densityNoise,
				param.Rotations,
				param.Mirror,
				1024,
				NoiseUtils.PinkAmplitude);
			CellLayerUtils.CalibrateQuantileInPlace(
				densityNoise,
				0,
				FractionMax - density, FractionMax);

			var decorable = new CellLayer<bool>(Map);
			var totalDecorable = 0;
			foreach (var mpos in Map.AllCells.MapCoords)
			{
				var isDecorable =
					zoneable[mpos] && space[mpos] && !reserved[mpos] && densityNoise[mpos] >= 0;
				decorable[mpos] = isDecorable;
				if (isDecorable)
					totalDecorable++;
				else
					decorationNoise[mpos] = -1024 * 1024;
			}

			var mapArea = Map.MapSize.Width * Map.MapSize.Height;
			CellLayerUtils.CalibrateQuantileInPlace(
				decorationNoise,
				0,
				mapArea - totalDecorable * coverage / FractionMax, mapArea);
			foreach (var mpos in Map.AllCells.MapCoords)
				if (decorationNoise[mpos] < 0)
					decorable[mpos] = false;

			for (var i = 0; i < 8; i++)
			{
				var (blurred, changes) = MatrixUtils.BooleanBlur(
					CellLayerUtils.ToMatrix(decorable, false),
					CivilianBuildingDensityRadius,
					FractionMax - minimumDensity, FractionMax);
				if (changes == 0)
					break;

				var densityFilter = new CellLayer<bool>(Map);
				CellLayerUtils.FromMatrix(densityFilter, blurred);

				foreach (var mpos in Map.AllCells.MapCoords)
					if (!densityFilter[mpos])
						decorable[mpos] = false;
			}

			ImproveSymmetry(decorable, false, (a, b) => a && b);

			return decorable;
		}

		/// <summary>
		/// Repaint the areas occupied by given tile types using MultiBrushes.
		/// </summary>
		public void RepaintTiles(
			MersenneTwister random,
			IReadOnlyDictionary<ushort, IReadOnlyList<MultiBrush>> rules)
		{
			foreach (var (tile, collection) in rules.OrderBy(kv => kv.Key))
			{
				var replace = new CellLayer<MultiBrush.Replaceability>(Map);
				foreach (var mpos in replace.CellRegion.MapCoords)
					replace[mpos] =
						Map.Tiles[mpos].Type == tile
							? MultiBrush.Replaceability.Any
							: MultiBrush.Replaceability.None;

				MultiBrush.PaintArea(Map, ActorPlans, replace, collection, random);
			}
		}

		/// <summary>
		/// Wrapper around MultiBrush.PaintArea that uses Replacibility.Actor for masked cells.
		/// </summary>
		public void PlaceActors(
			MersenneTwister random,
			CellLayer<bool> mask,
			IReadOnlyList<MultiBrush> brushes,
			bool alwaysPreferLargerBrushes = false)
		{
			CheckHasMapShape(mask);

			var replace = new CellLayer<MultiBrush.Replaceability>(Map);
			foreach (var mpos in Map.AllCells.MapCoords)
				replace[mpos] = mask[mpos] ? MultiBrush.Replaceability.Actor : MultiBrush.Replaceability.None;

			MultiBrush.PaintArea(
				Map,
				ActorPlans,
				replace,
				brushes,
				random,
				alwaysPreferLargerBrushes);
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

		public T Required<T>(T value) where T : class
		{
			if (value == null)
				throw new InvalidOperationException("A call to a method required a parameter that was not supplied to Terraformer at construction.");
			return value;
		}

		public T Required<T>(T? value) where T : struct
		{
			if (value == null)
				throw new InvalidOperationException("A call to a method required a parameter that was not supplied to Terraformer at construction");
			return value.Value;
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
