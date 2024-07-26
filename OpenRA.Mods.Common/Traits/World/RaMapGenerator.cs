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
using System.Drawing;
using System.Globalization;
using System.Linq;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Terrain;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Map generator for Red Alert maps.")]
	[TraitLocation(SystemActors.World)]
	public sealed class RaMapGeneratorInfo : TraitInfo, IMapGeneratorInfo
	{
		[Desc("Human-readable name this generator uses.")]
		public readonly string Name = "OpenRA Red Alert";

		[FieldLoader.Require]
		[Desc("Internal id for this map generator.")]
		public readonly string Type = null;

		string IMapGeneratorInfo.Type => Type;

		string IMapGeneratorInfo.Name => Name;

		public override object Create(ActorInitializer init) { return new RaMapGenerator(this); }
	}

	public sealed class RaMapGenerator : IMapGenerator
	{
		const double DEGREES_0   = 0.0;
		const double DEGREES_90  = Math.PI * 0.5;
		const double DEGREES_180 = Math.PI * 1.0;
		const double DEGREES_270 = Math.PI * 1.5;
		const double DEGREES_360 = Math.PI * 2.0;
		const double DEGREES_120 = Math.PI * (2.0 / 3.0);
		const double DEGREES_240 = Math.PI * (4.0 / 3.0);

		const double COS_0   = 1.0;
		const double COS_90  = 0.0;
		const double COS_180 = -1.0;
		const double COS_270 = 0.0;
		const double COS_360 = 1.0;
		const double COS_120 = -0.5;
		const double COS_240 = -0.5;

		const double SIN_0   = 0.0;
		const double SIN_90  = 1.0;
		const double SIN_180 = 0.0;
		const double SIN_270 = -1.0;
		const double SIN_360 = 0.0;
		const double SIN_120 = 0.86602540378443864676;
		const double SIN_240 = -0.86602540378443864676;

		const double SQRT2 = 1.4142135623730951;

		readonly RaMapGeneratorInfo info;

		IMapGeneratorInfo IMapGenerator.Info => info;

		public RaMapGenerator(RaMapGeneratorInfo info)
		{
			this.info = info;
		}

		enum Mirror
		{
			None = 0,
			LeftMatchesRight = 1,
			TopLeftMatchesBottomRight = 2,
			TopMatchesBottom = 3,
			TopRightMatchesBottomLeft = 4,
		}


		static int2 MirrorXY(Mirror mirror, int2 original, int2 size)
		{
			if (size.X != size.Y)
			{
				throw new NotImplementedException("Size.X must match Size.Y for now");
			}

			// THESE LOOK WRONG!
			switch (mirror)
			{
				case Mirror.None:
					throw new ArgumentException("Mirror.None has no transformed point");
				case Mirror.LeftMatchesRight:
					return new int2(original.X, size.Y - 1 - original.Y);
				case Mirror.TopLeftMatchesBottomRight:
					return new int2(original.Y, original.X);
				case Mirror.TopMatchesBottom:
					return new int2(size.X - 1 - original.X, original.Y);
				case Mirror.TopRightMatchesBottomLeft:
					return new int2(size.Y - 1 - original.Y, size.X - 1 - original.X);
				default:
					throw new ArgumentException("Bad mirror");
			}
		}

		static float2 MirrorXY(Mirror mirror, float2 original, float2 size)
		{
			if (size.X != size.Y)
			{
				throw new NotImplementedException("Size.X must match Size.Y for now");
			}

			// THESE LOOK WRONG!
			switch (mirror)
			{
				case Mirror.None:
					throw new ArgumentException("Mirror.None has no transformed point");
				case Mirror.LeftMatchesRight:
					return new float2(original.X, size.Y - 1.0f - original.Y);
				case Mirror.TopLeftMatchesBottomRight:
					return new float2(original.Y, original.X);
				case Mirror.TopMatchesBottom:
					return new float2(size.X - 1.0f - original.X, original.Y);
				case Mirror.TopRightMatchesBottomLeft:
					return new float2(size.Y - 1.0f - original.Y, size.X - 1.0f - original.X);
				default:
					throw new ArgumentException("Bad mirror");
			}
		}

		readonly struct Matrix<T>
		{
			public readonly T[] Data;
			public readonly int2 Size;
			public Matrix(int2 size)
			{
				Data = new T[size.X * size.Y];
				Size = size;
			}

			public Matrix(int x, int y)
				: this(new int2(x, y))
			{ }

			public T this[int x, int y]
			{
				get => Data[y * Size.X + x];
				set => Data[y * Size.X + x] = value;
			}

			public T this[int2 xy]
			{
				get => Data[xy.Y * Size.X + xy.X];
				set => Data[xy.Y * Size.X + xy.X] = value;
			}

			public T this[int i]
			{
				get => Data[i];
				set => Data[i] = value;
			}
		}


		static double SinSnap(double angle)
		{
			switch (angle)
			{
				case DEGREES_0:
					return COS_0;
				case DEGREES_90:
				case (double)(float)DEGREES_90:
					return COS_90;
				case DEGREES_180:
				case (double)(float)DEGREES_180:
					return COS_180;
				case DEGREES_270:
				case (double)(float)DEGREES_270:
					return COS_270;
				case DEGREES_360:
				case (double)(float)DEGREES_360:
					return COS_360;
				case DEGREES_120:
				case (double)(float)DEGREES_120:
					return COS_120;
				case DEGREES_240:
				case (double)(float)DEGREES_240:
					return COS_240;
				default:
					return Math.Cos(angle);
			}
		}

		static double CosSnap(double angle)
		{
			switch (angle)
			{
				case DEGREES_0:
					return SIN_0;
				case DEGREES_90:
				case (double)(float)DEGREES_90:
					return SIN_90;
				case DEGREES_180:
				case (double)(float)DEGREES_180:
					return SIN_180;
				case DEGREES_270:
				case (double)(float)DEGREES_270:
					return SIN_270;
				case DEGREES_360:
				case (double)(float)DEGREES_360:
					return SIN_360;
				case DEGREES_120:
				case (double)(float)DEGREES_120:
					return SIN_120;
				case DEGREES_240:
				case (double)(float)DEGREES_240:
					return SIN_240;
				default:
					return Math.Sin(angle);
			}
		}
		public IEnumerable<MapGeneratorSetting> GetDefaultSettings(Map map, ModData modData)
		{
			return ImmutableList.Create(
				new MapGeneratorSetting("#Primary", "Primary settings", new MapGeneratorSetting.SectionValue()),
				new MapGeneratorSetting("Rotations", "Rotations", new MapGeneratorSetting.IntegerValue(2)),
				new MapGeneratorSetting("Mirror", "Mirror", new MapGeneratorSetting.EnumValue(
					ImmutableList.Create(
						new KeyValuePair<int, string>((int)Mirror.None, "None"),
						new KeyValuePair<int, string>((int)Mirror.LeftMatchesRight, "Left matches right"),
						new KeyValuePair<int, string>((int)Mirror.TopLeftMatchesBottomRight, "Top-left matches bottom-right"),
						new KeyValuePair<int, string>((int)Mirror.TopMatchesBottom, "Top matches bottom"),
						new KeyValuePair<int, string>((int)Mirror.TopRightMatchesBottomLeft, "Top-right matches bottom-left")
					),
					(int)Mirror.None
				)),
				new MapGeneratorSetting("Players", "Players per symmetry", new MapGeneratorSetting.IntegerValue(1)),

				new MapGeneratorSetting("#Terrain", "Terrain settings", new MapGeneratorSetting.SectionValue()),
				new MapGeneratorSetting("WavelengthScale", "Noise Wavelength Scale", new MapGeneratorSetting.FloatValue(0.2)),
				new MapGeneratorSetting("Water", "Water fraction", new MapGeneratorSetting.FloatValue(0.2)),
				new MapGeneratorSetting("Mountains", "Mountain fraction (nesting)", new MapGeneratorSetting.FloatValue(0.1)),
				new MapGeneratorSetting("Forests", "Forest fraction", new MapGeneratorSetting.FloatValue(0.025)),
				new MapGeneratorSetting("ForestCutout", "Forest path cutout size", new MapGeneratorSetting.IntegerValue(2)),
				new MapGeneratorSetting("ExternalCircularBias", "Square/circular map", new MapGeneratorSetting.EnumValue(
					ImmutableList.Create(
						new KeyValuePair<string, string>("0", "Square"),
						new KeyValuePair<string, string>("-1", "Circle (outside is water)"),
						new KeyValuePair<string, string>("1", "Circle (outside is mountain)")
					),
					"0"
				)),
				new MapGeneratorSetting("TerrainSmoothing", "Terrain smoothing", new MapGeneratorSetting.IntegerValue(4)),
				new MapGeneratorSetting("SmoothingThreshold", "Smoothing threshold", new MapGeneratorSetting.FloatValue(0.33)),
				new MapGeneratorSetting("MinimumLandSeaThickness", "Minimum land/sea thickness", new MapGeneratorSetting.IntegerValue(5)),
				new MapGeneratorSetting("MinimumMountainThickness", "Minimum mountain thickness", new MapGeneratorSetting.IntegerValue(5)),
				new MapGeneratorSetting("MaximumAltitude", "Maximum mountain altitude", new MapGeneratorSetting.IntegerValue(8)),
				new MapGeneratorSetting("RoughnessRadius", "Roughness sampling size", new MapGeneratorSetting.IntegerValue(5)),
				new MapGeneratorSetting("Roughness", "Terrain roughness", new MapGeneratorSetting.FloatValue(0.5)),
				new MapGeneratorSetting("MinimumTerrainContourSpacing", "Minimum contour spacing", new MapGeneratorSetting.IntegerValue(6)),
				new MapGeneratorSetting("MinimumCliffLength", "Minimum cliff length", new MapGeneratorSetting.IntegerValue(10)),
				new MapGeneratorSetting("ForestClumpiness", "Forest clumpiness", new MapGeneratorSetting.FloatValue(0.5)),
				new MapGeneratorSetting("DenyWalledAreas", "Deny areas with limited access", new MapGeneratorSetting.BooleanValue(true)),
				new MapGeneratorSetting("EnforceSymmetry", "Symmetry Corrections", new MapGeneratorSetting.EnumValue(
					ImmutableList.Create(
						new KeyValuePair<string, string>("0", "None"),
						new KeyValuePair<string, string>("1", "Match passability"),
						new KeyValuePair<string, string>("2", "Match terrain type")
					),
					"0"
				)),
				new MapGeneratorSetting("Roads", "Roads", new MapGeneratorSetting.BooleanValue(true)),
				new MapGeneratorSetting("RoadSpacing", "Road spacing", new MapGeneratorSetting.IntegerValue(5)),

				new MapGeneratorSetting("#Entities", "Entity settings", new MapGeneratorSetting.SectionValue()),
				new MapGeneratorSetting("CreateEntities", "Create entities", new MapGeneratorSetting.BooleanValue(true)),
				new MapGeneratorSetting("CentralSpawnReservationFraction", "Central reservation against spawns", new MapGeneratorSetting.FloatValue(0.3)),
				new MapGeneratorSetting("CentralExpansionReservationFraction", "Central reservation against expansions", new MapGeneratorSetting.FloatValue(0.1)),
				new MapGeneratorSetting("MineReservation", "Unrelated mine spacing", new MapGeneratorSetting.IntegerValue(8)),
				new MapGeneratorSetting("SpawnRegionSize", "Spawn region size", new MapGeneratorSetting.IntegerValue(16)),
				new MapGeneratorSetting("SpawnBuildSize", "Spawn build size", new MapGeneratorSetting.IntegerValue(8)),
				new MapGeneratorSetting("SpawnMines", "Spawn mine count", new MapGeneratorSetting.IntegerValue(3)),
				new MapGeneratorSetting("SpawnReservation", "Spawn reservation size", new MapGeneratorSetting.IntegerValue(20)),
				new MapGeneratorSetting("SpawnResourceBias", "Spawn resource placement bais", new MapGeneratorSetting.FloatValue(1.25)),
				new MapGeneratorSetting("ResourcesPerPlace", "Starting resource value per player", new MapGeneratorSetting.IntegerValue(50000)),
				new MapGeneratorSetting("GemUpgrade", "Ore to gem upgrade probability", new MapGeneratorSetting.FloatValue(0.05)),
				new MapGeneratorSetting("OreUniformity", "Ore uniformity", new MapGeneratorSetting.FloatValue(0.25)),
				new MapGeneratorSetting("OreClumpiness", "Ore clumpiness", new MapGeneratorSetting.FloatValue(0.25)),
				new MapGeneratorSetting("MaximumExpansionMines", "Expansion mines per player", new MapGeneratorSetting.IntegerValue(5)),
				new MapGeneratorSetting("MaximumMinesPerExpansion", "Maximum mines per expansion", new MapGeneratorSetting.IntegerValue(2)),
				new MapGeneratorSetting("MinimumExpansionSize", "Minimum expansion size", new MapGeneratorSetting.IntegerValue(2)),
				new MapGeneratorSetting("MaximumExpansionSize", "Maximum expansion size", new MapGeneratorSetting.IntegerValue(12)),
				new MapGeneratorSetting("ExpansionInner", "Expansion inner size", new MapGeneratorSetting.IntegerValue(2)),
				new MapGeneratorSetting("ExpansionBorder", "Expansion border size", new MapGeneratorSetting.IntegerValue(1)),
				new MapGeneratorSetting("MinimumBuildings", "Minimum building count per symmetry", new MapGeneratorSetting.IntegerValue(0)),
				new MapGeneratorSetting("MaximumBuildings", "Maximum building count per symmetry", new MapGeneratorSetting.IntegerValue(3)),
				new MapGeneratorSetting("WeightFcom", "Building weight: Forward Command", new MapGeneratorSetting.FloatValue(1)),
				new MapGeneratorSetting("WeightHosp", "Building weight: Hospital", new MapGeneratorSetting.FloatValue(2)),
				new MapGeneratorSetting("WeightMiss", "Building weight: Communications Center", new MapGeneratorSetting.FloatValue(2)),
				new MapGeneratorSetting("WeightBio", "Building weight: Biological Lab", new MapGeneratorSetting.FloatValue(0)),
				new MapGeneratorSetting("WeightOilb", "Building weight: Oil Derrick", new MapGeneratorSetting.FloatValue(8))
			);
		}

		public IEnumerable<MapGeneratorSetting> GetPresetSettings(Map map, ModData modData, string preset)
		{
			var settings = GetDefaultSettings(map, modData);
			switch (preset) {
				case null:
					break;
				case "plains":
					settings.First(s => s.Name == "Water").Set(0.0);
					break;
				default:
					throw new ArgumentException("Invalid preset.");
			}
			return settings;
		}

		public IEnumerable<KeyValuePair<string, string>> GetPresets(Map map, ModData modData)
		{
			return ImmutableList.Create(
				new KeyValuePair<string, string>("plains", "Plains"));
		}

		public void Generate(Map map, ModData modData, MersenneTwister random, IEnumerable<MapGeneratorSetting> settingsEnumerable)
		{
			// TODO: translate exception messages?
			var settings = Enumerable.ToDictionary(settingsEnumerable, s => s.Name);
			var tileset = modData.DefaultTerrainInfo[map.Tileset];
			var size = map.MapSize;
			var rotations = settings["Rotations"].Get<int>();
			var mirror = (Mirror)settings["Mirror"].Get<int>();
			var wavelengthScale = settings["WavelengthScale"].Get<float>();

			if (settings["Water"].Get<double>() < 0.0 || settings["Water"].Get<double>() > 1.0)
			{
				throw new MapGenerationException("water setting must be between 0 and 1 inclusive");
			}

			// if (params.mountain < 0.0 || params.mountain > 1.0) {
			//     die("mountain fraction must be between 0 and 1 inclusive");
			// }
			// if (params.water + params.mountain > 1.0) {
			//     die("water and mountain fractions combined must not exceed 1");
			// }

			Log.Write("debug", "deriving random generators");

			// Use `random` to derive separate independent random number generators.
			//
			// This prevents changes in one part of the algorithm from affecting randomness in
			// other parts and provides flexibility for future parallel processing.
			//
			// In order to maximize stability, additions should be appended only. Disused
			// derivatives may be deleted but should be replaced with their unused call to
			// random.Next(). All generators should be created unconditionally.
			var waterRandom = new MersenneTwister(random.Next());

			Log.Write("debug", "elevation: generating noise");
			var elevation = FractalNoise2dWithSymmetry(waterRandom, size, rotations, mirror, wavelengthScale);

			var clear = new TerrainTile(255, 0);
			var water = new TerrainTile(1, 0);

			foreach (var cell in map.AllCells)
			{
				var mpos = cell.ToMPos(map);
				map.Tiles[mpos] = elevation[mpos.U, mpos.V] >= 0 ? clear : water;
				map.Resources[mpos] = new ResourceTile(0, 0);
				map.Height[mpos] = 0;
			}

			map.PlayerDefinitions = new MapPlayers(map.Rules, 0).ToMiniYaml();
			map.ActorDefinitions = ImmutableArray<MiniYamlNode>.Empty;
		}

		static Matrix<float> FractalNoise2dWithSymmetry(MersenneTwister random, int2 size, int rotations, Mirror mirror, float wavelengthScale)
		{
			if (rotations < 1)
			{
				throw new ArgumentException("rotations must be >= 1");
			}

			// Need higher resolution due to cropping and rotation artifacts
			var templateSpan = Math.Max(size.X, size.Y) * 2 + 2;
			var templateSize = new int2(templateSpan, templateSpan);
			var template = FractalNoise2d(random, templateSize, wavelengthScale);
			var unmirrored = new Matrix<float>(size);

			// This -1 is required to compensate for the top-left vs the center of a grid square.
			var offset = new float2((size.X - 1) / 2.0f, (size.Y - 1) / 2.0f);
			var templateOffset = new float2(templateSpan / 2.0f, templateSpan / 2.0f);
			for (var rotation = 0; rotation < rotations; rotation++)
			{
				var angle = rotation * 2.0 * Math.PI / rotations;
				var cosAngle = (float)CosSnap(angle);
				var sinAngle = (float)SinSnap(angle);
				for (var y = 0; y < size.Y; y++)
				{
					for (var x = 0; x < size.X; x++)
					{
						var xy = new float2(x, y);

						// xy # corner noise space
						// xy - offset # middle noise space
						// (xy - offset) * SQRT2 # middle temp space
						// R * ((xy - offset) * SQRT2) # middle temp space rotate
						// R * ((xy - offset) * SQRT2) + to # corner temp space rotate
						var midt = (xy - offset) * (float)SQRT2;
						var tx = (midt.X * cosAngle - midt.Y * sinAngle) + templateOffset.X;
						var ty = (midt.X * sinAngle + midt.Y * cosAngle) + templateOffset.Y;
						unmirrored[x, y] +=
							Interpolate2d(
								template,
								tx,
								ty) / rotations;
					}
				}
			}

			if (mirror == Mirror.None)
			{
				return unmirrored;
			}

			var mirrored = new Matrix<float>(size);
			for (var y = 0; y < size.Y; y++)
			{
				for (var x = 0; x < size.X; x++)
				{
					var txy = MirrorXY(mirror, new int2(x, y), size);
					mirrored[x, y] = unmirrored[x, y] + unmirrored[txy];
				}
			}
			return mirrored;
		}

		static Matrix<float> FractalNoise2d(MersenneTwister random, int2 size, float wavelengthScale)
		{
			var span = Math.Max(size.X, size.Y);
			var wavelengths = new float[(int)Math.Log2(span)];
			for (var i = 0; i < wavelengths.Length; i++)
			{
				wavelengths[i] = (1 << i) * wavelengthScale;
			}

			float AmpFunc(float wavelength) => wavelength / span / wavelengths.Length;
			var noise = new Matrix<float>(size);
			foreach (var wavelength in wavelengths)
			{
				var amps = AmpFunc(wavelength);
				var subSpan = (int)(span / wavelength) + 2;
				var subNoise = PerlinNoise2d(random, subSpan);

				// Offsets should align to grid.
				// (The wavelength is divided back out later.)
				var offsetX = (int)(random.NextFloat() * wavelength);
				var offsetY = (int)(random.NextFloat() * wavelength);
				for (var y = 0; y < size.Y; y++)
				{
					for (var x = 0; x < size.X; x++)
					{
						noise[y * size.X + x] +=
							amps * Interpolate2d(
								subNoise,
								(offsetX + x) / wavelength,
								(offsetY + y) / wavelength);
					}
				}
			}

			return noise;
		}

		static Matrix<float> PerlinNoise2d(MersenneTwister random, int span)
		{
			var noise = new Matrix<float>(span, span);
			const float D = 0.25f;
			for (var y = 0; y <= span; y++)
			{
				for (var x = 0; x <= span; x++)
				{
					var phase = 2.0f * (float)Math.PI * random.NextFloatExclusive();
					var vx = (float)Math.Cos(phase);
					var vy = (float)Math.Sin(phase);
					if (x > 0 && y > 0)
						noise[x - 1, y - 1] += vx * -D + vy * -D;
					if (x < span && y > 0)
						noise[x    , y - 1] += vx *  D + vy * -D;
					if (x > 0 && y < span)
						noise[x - 1, y    ] += vx * -D + vy *  D;
					if (x < span && y < span)
						noise[x    , y    ] += vx *  D + vy *  D;
				}
			}

			return noise;
		}

		static float Interpolate2d(Matrix<float> matrix, float x, float y)
		{
			var xa = (int)Math.Floor(x) | 0;
			var xb = (int)Math.Ceiling(x) | 0;
			var ya = (int)Math.Floor(y) | 0;
			var yb = (int)Math.Ceiling(y) | 0;
			var xbw = x - xa;
			var ybw = y - ya;
			var xaw = 1.0f - xbw;
			var yaw = 1.0f - ybw;
			if (xa < 0)
			{
				xa = 0;
				xb = 0;
			}
			else if (xb > matrix.Size.X - 1)
			{
				xa = matrix.Size.X - 1;
				xb = matrix.Size.X - 1;
			}
			if (ya < 0)
			{
				ya = 0;
				yb = 0;
			}
			else if (yb > matrix.Size.Y - 1)
			{
				ya = matrix.Size.Y - 1;
				yb = matrix.Size.Y - 1;
			}
			var naa = matrix[xa, ya];
			var nba = matrix[xb, ya];
			var nab = matrix[xa, yb];
			var nbb = matrix[xb, yb];
			return (naa * xaw + nba * xbw) * yaw + (nab * xaw + nbb * xbw) * ybw;
		}

		public bool ShowInEditor(Map map, ModData modData)
		{
			switch (map.Tileset)
			{
				case "TEMPERAT":
					break;
				default:
					return false;
			}
			return true;
		}
	}
}
