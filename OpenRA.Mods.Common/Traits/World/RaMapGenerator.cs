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
using System.Diagnostics;
using System.Linq;
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

		const float EXTERNAL_BIAS = 1000000.0f;

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

		sealed class Matrix<T>
		{
			public readonly T[] Data;
			public readonly int2 Size;
			Matrix(int2 size, T[] data)
			{
				Data = data;
				Size = size;
			}

			public Matrix(int2 size)
				: this(size, new T[size.X * size.Y])
			{ }

			public Matrix(int x, int y)
				: this(new int2(x, y))
			{ }

			int Index(int x, int y)
			{
				Debug.Assert(ContainsXY(x, y), $"({x}, {y}) is out of bounds for a matrix of size ({Size.X}, {Size.Y})");
				return y * Size.X + x;
			}

			public T this[int x, int y]
			{
				get => Data[Index(x, y)];
				set => Data[Index(x, y)] = value;
			}

			public T this[int2 xy]
			{
				get => Data[Index(xy.X, xy.Y)];
				set => Data[Index(xy.X, xy.Y)] = value;
			}

			public T this[int i]
			{
				get => Data[i];
				set => Data[i] = value;
			}

			public bool ContainsXY(int2 xy)
			{
				return xy.X >= 0 && xy.X < Size.X && xy.Y >= 0 && xy.Y < Size.Y;
			}

			public bool ContainsXY(int x, int y)
			{
				return x >= 0 && x < Size.X && y >= 0 && y < Size.Y;
			}

			public int2 Clamp(int2 xy)
			{
				var (nx, ny) = Clamp(xy.X, xy.Y);
				return new int2(nx, ny);
			}

			public (int Nx, int Ny) Clamp(int x, int y)
			{
				if (x >= Size.X) x = Size.X - 1;
				if (x < 0) x = 0;
				if (y >= Size.Y) y = Size.Y - 1;
				if (y < 0) y = 0;
				return (x, y);
			}

			// <summary>
			// Creates a transposed copy of the matrix.
			// <summary>
			public Matrix<T> Transpose()
			{
				var transposed = new Matrix<T>(new int2(Size.Y, Size.X));
				for (var y = 0; y < Size.Y; y++)
				{
					for (var x = 0; x < Size.X; x++)
					{
						transposed[y, x] = this[x, y];
					}
				}

				return transposed;
			}

			public Matrix<R> Map<R>(Func<T, R> func)
			{
				var mapped = new Matrix<R>(Size);
				for (var i = 0; i < Data.Length; i++)
				{
					mapped.Data[i] = func(Data[i]);
				}

				return mapped;
			}

			public Matrix<T> Fill(T value)
			{
				Array.Fill(Data, value);
				return this;
			}

			public Matrix<T> Clone()
			{
				return new Matrix<T>(Size, (T[])Data.Clone());
			}

			public static Matrix<T> Zip<T1, T2>(Matrix<T1> a, Matrix<T2> b, Func<T1, T2, T> func)
			{
				if (a.Size != b.Size)
					throw new ArgumentException("Input matrices to FromZip must match in shape and size");
				var matrix = new Matrix<T>(a.Size);
				for (var i = 0; i < a.Data.Length; i++)
					matrix.Data[i] = func(a.Data[i], b.Data[i]);
				return matrix;
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
			switch (preset)
			{
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
			var minSpan = Math.Min(size.X, size.Y);
			var maxSpan = Math.Max(size.X, size.Y);

			var rotations = settings["Rotations"].Get<int>();
			var mirror = (Mirror)settings["Mirror"].Get<int>();
			var wavelengthScale = settings["WavelengthScale"].Get<float>();
			var terrainSmoothing = settings["TerrainSmoothing"].Get<int>();
			var smoothingThreshold = settings["SmoothingThreshold"].Get<float>();
			var externalCircularBias = settings["ExternalCircularBias"].Get<int>();
			var minimumLandSeaThickness = settings["MinimumLandSeaThickness"].Get<int>();
			var minimumMountainThickness = settings["MinimumMountainThickness"].Get<int>();
			var water = settings["Water"].Get<float>();

			if (water < 0.0f || water > 1.0f)
			{
				throw new MapGenerationException("water setting must be between 0 and 1 inclusive");
			}

			// TODO
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

			if (terrainSmoothing > 0)
			{
				Log.Write("debug", "elevation: applying gaussian blur");
				var radius = terrainSmoothing;
				var kernel = GaussianKernel1D(radius, /*standardDeviation=*/radius);
				elevation = KernelBlur(elevation, kernel, new int2(radius, 0));
				elevation = KernelBlur(elevation, kernel.Transpose(), new int2(0, radius));
			}

			CalibrateHeightInPlace(
				elevation,
				0.0f,
				water);
			var externalCircleCenter = (size.ToFloat2() - new float2(0.5f, 0.5f)) / 2.0f;
			if (externalCircularBias != 0)
			{
				ReserveCircleInPlace(
					elevation,
					externalCircleCenter,
					minSpan / 2.0f - (minimumLandSeaThickness + minimumMountainThickness),
					(_, _) => externalCircularBias * EXTERNAL_BIAS,
					/*invert=*/true);
			}

			Log.Write("debug", "land planning: fixing terrain anomalies");
			var landPlan = ProduceTerrain(elevation, terrainSmoothing, smoothingThreshold, minimumLandSeaThickness, /*bias=*/water < 0.5, "land planning");

			// Makeshift map assembly

			var clearTile = new TerrainTile(255, 0);
			var waterTile = new TerrainTile(1, 0);

			foreach (var cell in map.AllCells)
			{
				var mpos = cell.ToMPos(map);
				map.Tiles[mpos] = landPlan[mpos.U, mpos.V] ? clearTile : waterTile;
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

		static Matrix<float> GaussianKernel1D(int radius, float standardDeviation)
		{
			var span = radius * 2 + 1;
			var kernel = new Matrix<float>(new int2(span, 1));
			var dsd2 = 2 * standardDeviation * standardDeviation;
			var total = 0.0f;
			for (var x = -radius; x <= radius; x++)
			{
				var value = (float)Math.Exp(-x * x / dsd2);
				kernel[x + radius] = value;
				total += value;
			}

			// Instead of dividing by sqrt(PI * dsd2), divide by the total.
			for (var i = 0; i < span; i++)
			{
				kernel[i] /= total;
			}

			return kernel;
		}

		static Matrix<float> KernelBlur(Matrix<float> input, Matrix<float> kernel, int2 kernelOffset)
		{
			var output = new Matrix<float>(input.Size);
			for (var cy = 0; cy < input.Size.Y; cy++)
			{
				for (var cx = 0; cx < input.Size.X; cx++)
				{
					var total = 0.0f;
					var samples = 0;
					for (var ky = 0; ky < kernel.Size.Y; ky++)
					{
						for (var kx = 0; kx < kernel.Size.X; kx++)
						{
							var x = cx + kx - kernelOffset.X;
							var y = cy + ky - kernelOffset.Y;
							if (!input.ContainsXY(x, y)) continue;
							total += input[x, y] * kernel[kx, ky];
							samples++;
						}
					}

					output[cx, cy] = total / samples;
				}
			}

			return output;
		}

		static void CalibrateHeightInPlace(Matrix<float> matrix, float target, float fraction)
		{
			var sorted = (float[])matrix.Data.Clone();
			Array.Sort(sorted);
			var adjustment = target - ArrayQuantile(sorted, fraction);
			for (var i = 0; i < matrix.Data.Length; i++)
			{
				matrix[i] += adjustment;
			}
		}

		static float ArrayQuantile(float[] array, float quantile)
		{
			if (array.Length == 0)
			{
				throw new ArgumentException("Cannot get quantile of empty array");
			}

			var iFloat = quantile * (array.Length - 1);
			if (iFloat < 0)
			{
				iFloat = 0;
			}

			if (iFloat > array.Length - 1)
			{
				iFloat = array.Length - 1;
			}

			var iLow = (int)iFloat;
			if (iLow == iFloat)
			{
				return array[iLow];
			}

			var iHigh = iLow + 1;
			var weight = iFloat - iLow;
			return array[iLow] * (1 - weight) + array[iHigh] * weight;
		}

		delegate T ReserveCircleSetTo<T>(float radiusSquared, T oldValue);
		static void ReserveCircleInPlace<T>(Matrix<T> matrix, float2 center, float radius, ReserveCircleSetTo<T> setTo, bool invert)
		{
			int minX;
			int minY;
			int maxX;
			int maxY;
			if (invert)
			{
				minX = 0;
				minY = 0;
				maxX = matrix.Size.X - 1;
				maxY = matrix.Size.Y - 1;
			}
			else
			{
				minX = (int)Math.Floor(center.X - radius);
				minY = (int)Math.Floor(center.Y - radius);
				maxX = (int)Math.Ceiling(center.X + radius);
				maxY = (int)Math.Ceiling(center.Y + radius);
				if (minX < 0)
					minX = 0;
				if (minY < 0)
					minY = 0;
				if (maxX >= matrix.Size.X)
					maxX = matrix.Size.X - 1;
				if (maxY >= matrix.Size.Y)
					maxY = matrix.Size.Y - 1;
			}

			var radiusSquared = radius * radius;
			for (var y = minY; y <= maxY; y++)
			{
				for (var x = minX; x <= maxX; x++)
				{
					var rx = x - center.X;
					var ry = y - center.Y;
					var thisRadiusSquared = rx * rx + ry * ry;
					if (rx * rx + ry * ry <= radiusSquared != invert)
					{
						matrix[x, y] = setTo(thisRadiusSquared, matrix[x, y]);
					}
				}
			}
		}

		static Matrix<bool> ProduceTerrain(Matrix<float> elevation, int terrainSmoothing, float smoothingThreshold, int minimumThickness, bool bias, string debugLabel)
		{
			Log.Write("debug", $"{debugLabel}: fixing terrain anomalies: primary median blur");
			var maxSpan = Math.Max(elevation.Size.X, elevation.Size.Y);
			var landmass = elevation.Map(v => v >= 0);

			(landmass, _) = BooleanBlur(landmass, terrainSmoothing, true, 0.0f);
			for (var i1 = 0; i1 < /*max passes*/16; i1++)
			{
				for (var i2 = 0; i2 < maxSpan; i2++)
				{
					int changes;
					var changesAcc = 0;
					for (var r = 1; r <= terrainSmoothing; r++)
					{
						(landmass, changes) = BooleanBlur(landmass, r, true, smoothingThreshold);
						changesAcc += changes;
					}

					if (changesAcc == 0)
					{
						break;
					}
				}

				{
					var changesAcc = 0;
					int changes;
					int thinnest;
					(landmass, changes) = ErodeAndDilate(landmass, true, minimumThickness);
					changesAcc += changes;
					(thinnest, changes) = FixThinMassesInPlaceFull(landmass, true, minimumThickness);
					changesAcc += changes;

					var midFixLandmass = landmass.Clone();

					(landmass, changes) = ErodeAndDilate(landmass, false, minimumThickness);
					changesAcc += changes;
					(thinnest, changes) = FixThinMassesInPlaceFull(landmass, false, minimumThickness);
					changesAcc += changes;
					if (changesAcc == 0)
					{
						break;
					}

					if (i1 >= 8 && i1 % 4 == 0)
					{
						var diff = Matrix<bool>.Zip(midFixLandmass, landmass, (a, b) => a != b);
						for (var y = 0; y < elevation.Size.Y; y++)
						{
							for (var x = 0; x < elevation.Size.X; x++)
							{
								if (diff[x, y])
									ReserveCircleInPlace(landmass, new float2(x, y), minimumThickness * 2, (_, _) => bias, false);
							}
						}
					}
				}
			}

			return landmass;
		}

		// // Perhaps replace with booleans or ints.
		// static (Matrix<float> Output, int Changes, int SignChanges) MedianBlur(Matrix<float> input, int radius, bool extendOut, float threshold)
		// {
		// 	var halfThreshold = threshold / 2.0f;
		// 	var output = new Matrix<float>(input.Size);
		// 	var changes = 0;
		// 	var signChanges = 0;
		// 	var samples = new float[(2 * radius + 1) * (2 * radius + 1)];
		// 	for (var cy = 0; cy < input.Size.Y; cy++)
		// 	{
		// 		for (var cx = 0; cx < input.Size.X; cx++)
		// 		{
		// 			// const ci = cy * size + cx;
		// 			var sampleCount = 0;
		// 			for (var oy = -radius; oy <= radius; oy++)
		// 			{
		// 				for (var ox = -radius; ox <= radius; ox++)
		// 				{
		// 					var x = cx + ox;
		// 					var y = cy + oy;
		// 					if (extendOut)
		// 					{
		// 						(x, y) = input.Clamp(x, y);
		// 					}
		// 					else
		// 					{
		// 						if (!input.ContainsXY(x, y)) continue;
		// 					}

		// 					samples[sampleCount++] = input[x, y];
		// 				}
		// 			}

		// 			var thisInput = input[cx, cy];
		// 			Array.Sort(samples, 0, sampleCount);
		// 			if (threshold != 0)
		// 			{
		// 				var low = ArrayQuantile(samples, 0.5f - halfThreshold);
		// 				var high = ArrayQuantile(samples, 0.5f + halfThreshold);
		// 				if (low <= thisInput && thisInput <= high)
		// 				{
		// 					output[cx, cy] = thisInput;
		// 					continue;
		// 				}
		// 			}

		// 			var thisOutput = ArrayQuantile(samples, 0.5f);
		// 			output[cx, cy] = thisOutput;
		// 			changes++;
		// 			if (Math.Sign(thisOutput) != Math.Sign(thisInput))
		// 			{
		// 				signChanges++;
		// 			}
		// 		}
		// 	}

		// 	return (output, changes, signChanges);
		// }

		static (Matrix<bool> Output, int Changes) BooleanBlur(Matrix<bool> input, int radius, bool extendOut, float threshold)
		{
			// var halfThreshold = threshold / 2.0f;
			var output = new Matrix<bool>(input.Size);
			var changes = 0;

			for (var cy = 0; cy < input.Size.Y; cy++)
			{
				for (var cx = 0; cx < input.Size.X; cx++)
				{
					var falseCount = 0;
					var trueCount = 0;
					for (var oy = -radius; oy <= radius; oy++)
					{
						for (var ox = -radius; ox <= radius; ox++)
						{
							var x = cx + ox;
							var y = cy + oy;
							if (extendOut)
							{
								(x, y) = input.Clamp(x, y);
							}
							else
							{
								if (!input.ContainsXY(x, y)) continue;
							}

							if (input[x, y])
								trueCount++;
							else
								falseCount++;
						}
					}

					var sampleCount = falseCount + trueCount;
					var requirement = (int)(sampleCount * threshold);
					var thisInput = input[cx, cy];
					bool thisOutput;
					if (trueCount - falseCount > requirement)
						thisOutput = true;
					else if (falseCount - trueCount > requirement)
						thisOutput = false;
					else
						thisOutput = input[cx, cy];

					output[cx, cy] = thisOutput;
					if (thisOutput != thisInput)
						changes++;
				}
			}

			return (output, changes);
		}

		static (Matrix<bool> Output, int Changes) ErodeAndDilate(Matrix<bool> input, bool foreground, int amount)
		{
			var output = new Matrix<bool>(input.Size).Fill(!foreground);
			for (var cy = 1 - amount; cy < input.Size.Y; cy++)
			{
				for (var cx = 1 - amount; cx < input.Size.X; cx++)
				{
					bool IsRetained()
					{
						for (var ry = 0; ry < amount; ry++)
						{
							for (var rx = 0; rx < amount; rx++)
							{
								var x = cx + rx;
								var y = cy + ry;
								if (!input.ContainsXY(x, y)) continue;

								if (input[x, y] != foreground)
								{
									return false;
								}
							}
						}

						return true;
					}

					if (!IsRetained()) continue;

					for (var ry = 0; ry < amount; ry++)
					{
						for (var rx = 0; rx < amount; rx++)
						{
							var x = cx + rx;
							var y = cy + ry;
							if (!input.ContainsXY(x, y)) continue;

							output[x, y] = foreground;
						}
					}
				}
			}

			var changes = 0;
			for (var i = 0; i < input.Data.Length; i++)
			{
				if (input[i] != output[i])
					changes++;
			}

			return (output, changes);
		}

		static (int Thinnest, int Changes) FixThinMassesInPlaceFull(Matrix<bool> input, bool dilate, int width)
		{
			int thinnest;
			int changes;
			int changesAcc;
			(thinnest, changes) = FixThinMassesInPlace(input, dilate, width);
			changesAcc = changes;
			while (changes > 0)
			{
				(_, changes) = FixThinMassesInPlace(input, dilate, width);
				changesAcc += changes;
			}

			return (thinnest, changesAcc);
		}

		static (int Thinnest, int Changes) FixThinMassesInPlace(Matrix<bool> input, bool dilate, int width)
		{
			var sizeMinus1 = input.Size - new int2(1, 1);
			var cornerMaskSpan = width + 1;

			// Zero means ignore.
			var cornerMask = new Matrix<int>(cornerMaskSpan, cornerMaskSpan);

			for (var y = 0; y < cornerMaskSpan; y++)
			{
				for (var x = 0; x < cornerMaskSpan; x++)
				{
					cornerMask[x, y] = 1 + width + width - x - y;
				}
			}

			cornerMask[0] = 0;

			// Higher number indicates a thinner area.
			var thinness = new Matrix<int>(input.Size);
			void SetThinness(int x, int y, int v)
			{
				if (!input.ContainsXY(x, y)) return;
				if (input[x, y] == dilate) return;
				thinness[x, y] = Math.Max(v, thinness[x, y]);
			}

			for (var cy = 0; cy < input.Size.Y; cy++)
			{
				for (var cx = 0; cx < input.Size.X; cx++)
				{
					if (input[cx, cy] == dilate)
						continue;

					// _L_eft _R_ight _U_p _D_own
					var l = input[Math.Max(cx - 1, 0), cy] == dilate;
					var r = input[Math.Min(cx + 1, sizeMinus1.X), cy] == dilate;
					var u = input[cx, Math.Max(cy - 1, 0)] == dilate;
					var d = input[cx, Math.Min(cy + 1, sizeMinus1.Y)] == dilate;
					var lu = l && u;
					var ru = r && u;
					var ld = l && d;
					var rd = r && d;
					for (var ry = 0; ry < cornerMaskSpan; ry++)
					{
						for (var rx = 0; rx < cornerMaskSpan; rx++)
						{
							if (rd)
							{
								var x = cx + rx;
								var y = cy + ry;
								SetThinness(x, y, cornerMask[rx, ry]);
							}

							if (ru)
							{
								var x = cx + rx;
								var y = cy - ry;
								SetThinness(x, y, cornerMask[rx, ry]);
							}

							if (ld)
							{
								var x = cx - rx;
								var y = cy + ry;
								SetThinness(x, y, cornerMask[rx, ry]);
							}

							if (lu)
							{
								var x = cx - rx;
								var y = cy - ry;
								SetThinness(x, y, cornerMask[rx, ry]);
							}
						}
					}
				}
			}

			var thinnest = thinness.Data.Max();
			if (thinnest == 0)
			{
				// No fixes
				return (0, 0);
			}

			var changes = 0;
			for (var y = 0; y < input.Size.Y; y++)
			{
				for (var x = 0; x < input.Size.X; x++)
				{
					if (thinness[x, y] == thinnest)
					{
						input[x, y] = dilate;
						changes++;
					}
				}
			}

			// Fixes made, with potentially more that can be in another pass.
			return (thinnest, changes);
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
