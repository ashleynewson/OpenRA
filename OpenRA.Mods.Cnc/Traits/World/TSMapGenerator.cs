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
using OpenRA.Mods.Common.MapGenerator;
using OpenRA.Mods.Common.Traits;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	[TraitLocation(SystemActors.EditorWorld)]
	[Desc("A map generator that clears a map.")]
	public sealed class TSMapGeneratorInfo : TraitInfo, IEditorMapGeneratorInfo
	{
		[FieldLoader.Require]
		[Desc("Human-readable name this generator uses.")]
		[FluentReference]
		public readonly string Name = null;

		[FieldLoader.Require]
		[Desc("Internal id for this map generator.")]
		public readonly string Type = null;

		[FieldLoader.Require]
		[Desc("Tilesets that are compatible with this map generator.")]
		public readonly string[] Tilesets = null;

		[FluentReference]
		[Desc("The title to use for generated maps.")]
		public readonly string MapTitle = "label-random-map";

		[Desc("The widget tree to open when the tool is selected.")]
		public readonly string PanelWidget = "MAP_GENERATOR_TOOL_PANEL";

		// This is purely of interest to the linter.
		[FieldLoader.LoadUsing(nameof(FluentReferencesLoader))]
		[FluentReference]
		public readonly List<string> FluentReferences = null;

		[FieldLoader.LoadUsing(nameof(SettingsLoader))]
		public readonly MiniYaml Settings;

		string IMapGeneratorInfo.Type => Type;
		string IMapGeneratorInfo.Name => Name;
		string IMapGeneratorInfo.MapTitle => MapTitle;

		static MiniYaml SettingsLoader(MiniYaml my)
		{
			return my.NodeWithKey("Settings").Value;
		}

		static List<string> FluentReferencesLoader(MiniYaml my)
		{
			return new MapGeneratorSettings(null, my.NodeWithKey("Settings").Value)
				.Options.SelectMany(o => o.GetFluentReferences()).ToList();
		}

		public IMapGeneratorSettings GetSettings()
		{
			return new MapGeneratorSettings(this, Settings);
		}

		public Map Generate(ModData modData, MapGenerationArgs args)
		{
			var random = new MersenneTwister();
			var terrainInfo = modData.DefaultTerrainInfo[args.Tileset];

			if (!Exts.TryParseUshortInvariant(args.Settings.NodeWithKey("Tile").Value.Value, out var tileType))
				throw new YamlException("Illegal tile type");

			if (!terrainInfo.TryGetTerrainInfo(new TerrainTile(tileType, 0), out var _))
				throw new MapGenerationException("Illegal tile type");

			var map = new Map(modData, terrainInfo, args.Size);
			var terraformer = new Terraformer(args, map, modData, [], Symmetry.Mirror.None, 1);

			terraformer.InitMap();

			// foreach (var mpos in map.AllCells.MapCoords)
			// 	map.Tiles[mpos] = terraformer.PickTile(random, tileType);

			var templates = new ushort[] { 0, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57 };
			var brushes = templates
				.Select(t => new MultiBrush().WithTemplate(map, t, CVec.Zero))
				.ToList();
			var tiler = new RampTiler(map, brushes);
			var bounds = CellLayerUtils.CellBounds(map);
			var cornerHeightsNoise =
				NoiseUtils.FractalNoise(
					random,
					bounds.Size.ToInt2() + new int2(1, 1),
					1024 * 32,
					NoiseUtils.PinkAmplitude);
			cornerHeightsNoise = MatrixUtils.NormalizeRangeInPlace(cornerHeightsNoise, 32);
			var cornerHeights = MatrixUtils.BinomialBlur(cornerHeightsNoise, 0)
				.Map(i => (byte)Math.Max(0, i));
			var maskLayer = CellLayerUtils.Create(map, (MPos mpos) => map.Contains(mpos));
			var mask = new Matrix<bool>(bounds.Size.ToInt2() + new int2(1, 1)).Fill(true);
			// foreach (var cpos in map.AllEdgeCells)
			// {
			// 	var xy = new int2(cpos.X, cpos.Y) - bounds.TopLeft;
			// 	mask[xy.X, xy.Y] = false;
			// 	mask[xy.X + 1, xy.Y] = false;
			// 	mask[xy.X + 1, xy.Y + 1] = false;
			// 	mask[xy.X, xy.Y + 1] = false;
			// 	cornerHeights[xy.X, xy.Y] = 0;
			// 	cornerHeights[xy.X + 1, xy.Y] = 0;
			// 	cornerHeights[xy.X + 1, xy.Y + 1] = 0;
			// 	cornerHeights[xy.X, xy.Y + 1] = 0;
			// }
			for (var y = 0; y < mask.Size.Y; y++)
			{
				for (var x = 0; x < mask.Size.X; x++)
				{
					var cpos = new CPos(x + bounds.TopLeft.X, y + bounds.TopLeft.Y);
					if (map.Contains(cpos))
						mask[x, y] = true;
					else
						cornerHeights[x, y] = 0;
				}
			}

			cornerHeights = tiler.ConstrainCornerHeights(cornerHeights, mask, RampTiler.AdjustmentMode.LowerMiddle);
			var tiling = tiler.TileCorners(cornerHeights, null, random);
			terraformer.PaintTiling(random, tiling);

			terraformer.BakeMap();

			return map;
		}

		public override object Create(ActorInitializer init)
		{
			return new TSMapGenerator(this);
		}

		string[] IEditorMapGeneratorInfo.Tilesets => Tilesets;
	}

	public class TSMapGenerator : IEditorTool
	{
		public string Label { get; }
		public string PanelWidget { get; }
		public TraitInfo TraitInfo { get; }
		public bool IsEnabled => true;

		public TSMapGenerator(TSMapGeneratorInfo info)
		{
			Label = info.Name;
			PanelWidget = info.PanelWidget;
			TraitInfo = info;
		}
	}
}
