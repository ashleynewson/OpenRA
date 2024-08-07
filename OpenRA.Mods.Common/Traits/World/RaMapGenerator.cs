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
using OpenRA.Mods.Common.Terrain;
using OpenRA.Primitives;
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
		const ushort LAND_TILE = 255;
		const ushort WATER_TILE = 1;

		const double DEGREES_0   = 0.0;
		const double DEGREES_90  = Math.Tau * 0.25;
		const double DEGREES_180 = Math.Tau * 0.5;
		const double DEGREES_270 = Math.Tau * 0.75;
		const double DEGREES_360 = Math.Tau * 1.0;
		const double DEGREES_120 = Math.Tau * (1.0 / 3.0);
		const double DEGREES_240 = Math.Tau * (2.0 / 3.0);

		const float DEGREESF_0   = 0.0f;
		const float DEGREESF_90  = MathF.Tau * 0.25f;
		const float DEGREESF_180 = MathF.Tau * 0.5f;
		const float DEGREESF_270 = MathF.Tau * 0.75f;
		const float DEGREESF_360 = MathF.Tau * 1.0f;
		const float DEGREESF_120 = MathF.Tau * (1.0f / 3.0f);
		const float DEGREESF_240 = MathF.Tau * (2.0f / 3.0f);

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

		const int DIRECTION_NONE = -1;
		const int DIRECTION_R = 0;
		const int DIRECTION_RD = 1;
		const int DIRECTION_D = 2;
		const int DIRECTION_LD = 3;
		const int DIRECTION_L = 4;
		const int DIRECTION_LU = 5;
		const int DIRECTION_U = 6;
		const int DIRECTION_RU = 7;

		const int DIRECTION_M_R = 1 << DIRECTION_R;
		const int DIRECTION_M_RD = 1 << DIRECTION_RD;
		const int DIRECTION_M_D = 1 << DIRECTION_D;
		const int DIRECTION_M_LD = 1 << DIRECTION_LD;
		const int DIRECTION_M_L = 1 << DIRECTION_L;
		const int DIRECTION_M_LU = 1 << DIRECTION_LU;
		const int DIRECTION_M_U = 1 << DIRECTION_U;
		const int DIRECTION_M_RU = 1 << DIRECTION_RU;

		static readonly ImmutableArray<int2> SPREAD4 = ImmutableArray.Create(new[]
		{
			new int2(1, 0),
			new int2(0, 1),
			new int2(-1, 0),
			new int2(0, -1)
		});

		static int CalculateDirection(int dx, int dy)
		{
			if (dx > 0)
			{
				if (dy > 0)
					return DIRECTION_RD;
				else if (dy < 0)
					return DIRECTION_RU;
				else
					return DIRECTION_R;
			}
			else if (dx < 0)
			{
				if (dy > 0)
					return DIRECTION_LD;
				else if (dy < 0)
					return DIRECTION_LU;
				else
					return DIRECTION_L;
			}
			else
			{
				if (dy > 0)
					return DIRECTION_D;
				else if (dy < 0)
					return DIRECTION_U;
				else
					throw new ArgumentException("Bad direction");
			}
		}

		static int CalculateDirection(int2 delta)
			=> CalculateDirection(delta.X, delta.Y);

		static int ReverseDirection(int direction)
		{
			if (direction == DIRECTION_NONE)
				return DIRECTION_NONE;
			return direction ^ 4;
		}

		static string DirectionToString(int direction)
		{
			switch (direction)
			{
				case DIRECTION_NONE:
					return "None";
				case DIRECTION_R:
					return "R";
				case DIRECTION_RD:
					return "RD";
				case DIRECTION_D:
					return "D";
				case DIRECTION_LD:
					return "LD";
				case DIRECTION_L:
					return "L";
				case DIRECTION_LU:
					return "LU";
				case DIRECTION_U:
					return "U";
				case DIRECTION_RU:
					return "RU";
				default:
					throw new ArgumentException("bad direction");
			}
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

		// <summary>
		// A fixed-size 2D array that can be indexed either linearly or by coordinates.
		// </summary>
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

		readonly struct PathTerminal
		{
			public readonly string Type;
			public readonly int Direction;

			public string SegmentType
			{
				get => $"{Type}.{DirectionToString(Direction)}";
			}

			public PathTerminal(string type, int direction)
			{
				Type = type;
				Direction = direction;
			}
		}

		sealed class PermittedTemplates
		{
			// This should probably be changed to store Segments rather than Templates.
			public readonly IEnumerable<TerrainTemplateInfo> Start;
			public readonly IEnumerable<TerrainTemplateInfo> Inner;
			public readonly IEnumerable<TerrainTemplateInfo> End;
			public IEnumerable<TerrainTemplateInfo> All => Start.Union(Inner).Union(End);

			public PermittedTemplates(IEnumerable<TerrainTemplateInfo> start, IEnumerable<TerrainTemplateInfo> inner, IEnumerable<TerrainTemplateInfo> end)
			{
				Start = start;
				Inner = inner;
				End = end;
			}

			public PermittedTemplates(IEnumerable<TerrainTemplateInfo> all)
				: this(all, all, all)
			{ }

			public static IEnumerable<TerrainTemplateInfo> FindTemplates(ITemplatedTerrainInfo templatedTerrainInfo, string[] types)
				=> FindTemplates(templatedTerrainInfo, types, types);

			public static IEnumerable<TerrainTemplateInfo> FindTemplates(ITemplatedTerrainInfo templatedTerrainInfo, string[] startTypes, string[] endTypes)
			{
				return templatedTerrainInfo.Templates.Values
					.Where(
						template => template.Segments.Any(
							segment =>
								startTypes.Any(type => segment.HasStartType(type) &&
								endTypes.Any(type => segment.HasEndType(type)))))
					.ToArray();
			}
		}

		sealed class Path
		{
			public int2[] Points;
			public PathTerminal Start;
			public PathTerminal End;
			public PermittedTemplates PermittedTemplates;
			public bool IsLoop
			{
				get => Points[0] == Points[^1];
			}

			// <summary>
			// Simple constructor for paths
			public Path(int2[] points, string startType, string endType, PermittedTemplates permittedTemplates)
			{
				Points = points;
				var startDirection = CalculateDirection(Points[1] - Points[0]);
				Start = new PathTerminal(startType, startDirection);
				var endDirection = CalculateDirection(IsLoop ? Points[1] - Points[0] : Points[^1] - Points[^2]);
				End = new PathTerminal(endType, endDirection);
				PermittedTemplates = permittedTemplates;
			}
		}

		static double SinSnap(double angle)
		{
			switch (angle)
			{
				case DEGREES_0:
					return COS_0;
				case DEGREES_90:
					return COS_90;
				case DEGREES_180:
					return COS_180;
				case DEGREES_270:
					return COS_270;
				case DEGREES_360:
					return COS_360;
				case DEGREES_120:
					return COS_120;
				case DEGREES_240:
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
					return SIN_90;
				case DEGREES_180:
					return SIN_180;
				case DEGREES_270:
					return SIN_270;
				case DEGREES_360:
					return SIN_360;
				case DEGREES_120:
					return SIN_120;
				case DEGREES_240:
					return SIN_240;
				default:
					return Math.Sin(angle);
			}
		}

		static float SinSnapF(float angle)
		{
			switch (angle)
			{
				case DEGREESF_0:
					return (float)COS_0;
				case DEGREESF_90:
					return (float)COS_90;
				case DEGREESF_180:
					return (float)COS_180;
				case DEGREESF_270:
					return (float)COS_270;
				case DEGREESF_360:
					return (float)COS_360;
				case DEGREESF_120:
					return (float)COS_120;
				case DEGREESF_240:
					return (float)COS_240;
				default:
					return MathF.Cos(angle);
			}
		}

		static float CosSnapF(float angle)
		{
			switch (angle)
			{
				case DEGREESF_0:
					return (float)SIN_0;
				case DEGREESF_90:
					return (float)SIN_90;
				case DEGREESF_180:
					return (float)SIN_180;
				case DEGREESF_270:
					return (float)SIN_270;
				case DEGREESF_360:
					return (float)SIN_360;
				case DEGREESF_120:
					return (float)SIN_120;
				case DEGREESF_240:
					return (float)SIN_240;
				default:
					return MathF.Sin(angle);
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
			var tileset = modData.DefaultTerrainInfo[map.Tileset] as ITemplatedTerrainInfo;
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
			var forests = settings["Forests"].Get<float>();
			var forestClumpiness = settings["ForestClumpiness"].Get<float>();
			var mountains = settings["Mountains"].Get<float>();
			var roughness = settings["Roughness"].Get<float>();
			var roughnessRadius = settings["RoughnessRadius"].Get<int>();
			var maximumAltitude = settings["MaximumAltitude"].Get<int>();
			var minimumTerrainContourSpacing = settings["MinimumTerrainContourSpacing"].Get<int>();
			var minimumCliffLength = settings["MinimumCliffLength"].Get<int>();

			if (water < 0.0f || water > 1.0f)
				throw new MapGenerationException("water setting must be between 0 and 1 inclusive");

			if (forests < 0.0f || forests > 1.0f)
				throw new MapGenerationException("forest setting must be between 0 and 1 inclusive");

			if (forestClumpiness < 0.0f)
				throw new MapGenerationException("forestClumpiness setting must be >= 0");

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
			var pickAnyRandom = new MersenneTwister(random.Next());
			var waterRandom = new MersenneTwister(random.Next());
			var beachTilingRandom = new MersenneTwister(random.Next());
			var cliffTilingRandom = new MersenneTwister(random.Next());
			var forestRandom = new MersenneTwister(random.Next());
			var resourceRandom = new MersenneTwister(random.Next());

			TerrainTile PickTile(ushort tileType)
			{
				if (tileset.Templates.TryGetValue(tileType, out var template) && template.PickAny)
					return new TerrainTile(tileType, (byte)random.Next(0, template.TilesCount));
				else
					return new TerrainTile(tileType, 0);
			}

			Log.Write("debug", "clearing map");
			foreach (var cell in map.AllCells)
			{
				var mpos = cell.ToMPos(map);
				map.Tiles[mpos] = PickTile(LAND_TILE);
				map.Resources[mpos] = new ResourceTile(0, 0);
				map.Height[mpos] = 0;
			}

			Log.Write("debug", "elevation: generating noise");
			var elevation = FractalNoise2dWithSymmetry(
				waterRandom,
				size,
				rotations,
				mirror,
				wavelengthScale,
				PinkAmplitudeFunction);

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
					matrix: elevation,
					center: externalCircleCenter,
					radius: minSpan / 2.0f - (minimumLandSeaThickness + minimumMountainThickness),
					setTo: (_, _) => externalCircularBias * EXTERNAL_BIAS,
					invert: true);
			}

			Log.Write("debug", "land planning: producing terrain");
			var landPlan = ProduceTerrain(elevation, terrainSmoothing, smoothingThreshold, minimumLandSeaThickness, /*bias=*/water < 0.5, "land planning");

			Log.Write("debug", "beaches");
			var beaches = BordersToPoints(landPlan);
			if (beaches.Length > 0)
			{
				var beachPermittedTemplates = new PermittedTemplates(PermittedTemplates.FindTemplates(tileset, new[] { "Beach" }));
				var tiledBeaches = new int2[beaches.Length][];
				for (var i = 0; i < beaches.Length; i++)
				{
					var tweakedPoints = TweakPathPoints(beaches[i], size);
					var beachPath = new Path(tweakedPoints, "Beach", "Beach", beachPermittedTemplates);
					tiledBeaches[i] = TilePath(map, beachPath, beachTilingRandom, minimumLandSeaThickness);
				}

				Log.Write("debug", "filling water");
				var beachChirality = PointsChirality(size, tiledBeaches);
				foreach (var cell in map.AllCells)
				{
					var mpos = cell.ToMPos(map);
					var point = new int2(mpos.U, mpos.V);

					// `map.Tiles[mpos].Index == LAND_TILE` avoids overwriting beach tiles.
					if (beachChirality[mpos.U, mpos.V] < 0 && map.Tiles[mpos].Type == LAND_TILE)
						map.Tiles[mpos] = PickTile(WATER_TILE);
				}
			}
			else
			{
				// There weren't any coastlines
				var tileType = landPlan[0] ? LAND_TILE : WATER_TILE;
				foreach (var cell in map.AllCells)
				{
					var mpos = cell.ToMPos(map);
					map.Tiles[mpos] = PickTile(tileType);
				}
			}

			Matrix<float> forestNoise = null;
			if (forests > 0.0f)
			{
				Log.Write("debug", "forests: generating noise");
				forestNoise = FractalNoise2dWithSymmetry(
					forestRandom,
					size,
					rotations,
					mirror,
					wavelengthScale,
					wavelength => MathF.Pow(wavelength, forestClumpiness));
				CalibrateHeightInPlace(
					forestNoise,
					0.0f,
					1.0f - forests);
			}

			Log.Write("debug", "ORE: generating noise");
			var orePattern = FractalNoise2dWithSymmetry(
				resourceRandom,
				size,
				rotations,
				mirror,
				wavelengthScale,
				wavelength => MathF.Pow(wavelength, forestClumpiness));

			var nonLoopedCliffPermittedTemplates = new PermittedTemplates(
				PermittedTemplates.FindTemplates(tileset, new[] { "Clear" }, new[] { "Cliff" }),
				PermittedTemplates.FindTemplates(tileset, new[] { "Cliff" }),
				PermittedTemplates.FindTemplates(tileset, new[] { "Cliff" }, new[] { "Clear" }));
			var loopedCliffPermittedTemplates = new PermittedTemplates(
				PermittedTemplates.FindTemplates(tileset, new[] { "Cliff" }));
			if (externalCircularBias > 0)
			{
				Log.Write("debug", "creating circular cliff map border");
				var cliffRing = new Matrix<bool>(size);
				ReserveCircleInPlace(
					matrix: cliffRing,
					center: externalCircleCenter,
					radius: minSpan / 2.0f - minimumLandSeaThickness,
					setTo: (_, _) => true,
					invert: true);
				var cliffs = BordersToPoints(cliffRing);
				foreach (var cliff in cliffs)
				{
					var tweakedPoints = TweakPathPoints(cliff, size);
					var isLoop = tweakedPoints[0] == tweakedPoints[^1];
					Path cliffPath;
					if (isLoop)
						cliffPath = new Path(tweakedPoints, "Cliff", "Cliff", loopedCliffPermittedTemplates);
					else
						cliffPath = new Path(tweakedPoints, "Clear", "Clear", nonLoopedCliffPermittedTemplates);
					TilePath(map, cliffPath, cliffTilingRandom, minimumMountainThickness);
				}
			}

			if (mountains > 0.0f || externalCircularBias == 1)
			{
				Log.Write("debug", "mountains: calculating elevation roughness");
				var roughnessMatrix = GridVariance2d(elevation, roughnessRadius).Map(v => MathF.Sqrt(v));
				CalibrateHeightInPlace(
					roughnessMatrix,
					0.0f,
					1.0f - roughness);
				var cliffMask = roughnessMatrix.Map(v => v >= 0);
				var mountainElevation = elevation.Clone();
				var cliffPlan = landPlan;
				if (externalCircularBias > 0)
				{
					ReserveCircleInPlace(
						matrix: cliffPlan,
						center: externalCircleCenter,
						radius: minSpan / 2.0f - (minimumLandSeaThickness + minimumMountainThickness),
						setTo: (_, _) => false,
						invert: true);
				}

				for (var altitude = 1; altitude <= maximumAltitude; altitude++)
				{
					Log.Write("debug", $"mountains: altitude {altitude}: determining eligible area for cliffs");

					// Limit mountain area to the existing mountain space (starting with all available land)
					var roominess = CalculateRoominess(cliffPlan, true);
					var available = 0;
					var total = size.X * size.Y;
					for (var n = 0; n < mountainElevation.Data.Length; n++)
					{
						if (roominess.Data[n] < minimumTerrainContourSpacing)
						{
							// Too close to existing cliffs (or coastline)
							mountainElevation.Data[n] = -1.0f;
						}
						else
						{
							available++;
						}

						total++;
					}

					var availableFraction = (float)available / (float)total;
					CalibrateHeightInPlace(
						mountainElevation,
						0.0f,
						1.0f - availableFraction * mountains);
					Log.Write("debug", $"mountains: altitude {altitude}: fixing terrain anomalies");
					cliffPlan = ProduceTerrain(mountainElevation, terrainSmoothing, smoothingThreshold, minimumMountainThickness, false, $"mountains: altitude {altitude}");
					Log.Write("debug", $"mountains: altitude {altitude}: tracing cliffs");
					var unmaskedCliffs = BordersToPoints(cliffPlan);
					Log.Write("debug", $"mountains: altitude {altitude}: appling roughness mask to cliffs");
					var maskedCliffs = MaskPoints(unmaskedCliffs, cliffMask);
					var cliffs = maskedCliffs.Where(cliff => cliff.Length >= minimumCliffLength).ToArray();
					if (cliffs.Length == 0)
						break;
					Log.Write("debug", $"mountains: altitude {altitude}: fitting and laying tiles");
					foreach (var cliff in cliffs)
					{
						var tweakedPoints = TweakPathPoints(cliff, size);
						var isLoop = tweakedPoints[0] == tweakedPoints[^1];
						Path cliffPath;
						if (isLoop)
							cliffPath = new Path(tweakedPoints, "Cliff", "Cliff", loopedCliffPermittedTemplates);
						else
							cliffPath = new Path(tweakedPoints, "Clear", "Clear", nonLoopedCliffPermittedTemplates);
						TilePath(map, cliffPath, cliffTilingRandom, minimumMountainThickness);
					}
				}
			}

			// Makeshift map assembly

			foreach (var cell in map.AllCells)
			{
				var mpos = cell.ToMPos(map);
				map.Resources[mpos] = new ResourceTile(0, 0);
				map.Height[mpos] = 0;
			}

			map.PlayerDefinitions = new MapPlayers(map.Rules, 0).ToMiniYaml();
			map.ActorDefinitions = ImmutableArray<MiniYamlNode>.Empty;
		}

		static Matrix<float> FractalNoise2dWithSymmetry(MersenneTwister random, int2 size, int rotations, Mirror mirror, float wavelengthScale, AmplitudeFunction ampFunc)
		{
			if (rotations < 1)
			{
				throw new ArgumentException("rotations must be >= 1");
			}

			// Need higher resolution due to cropping and rotation artifacts
			var templateSpan = Math.Max(size.X, size.Y) * 2 + 2;
			var templateSize = new int2(templateSpan, templateSpan);
			var template = FractalNoise2d(random, templateSize, wavelengthScale, ampFunc);
			var unmirrored = new Matrix<float>(size);

			// This -1 is required to compensate for the top-left vs the center of a grid square.
			var offset = new float2((size.X - 1) / 2.0f, (size.Y - 1) / 2.0f);
			var templateOffset = new float2(templateSpan / 2.0f, templateSpan / 2.0f);
			for (var rotation = 0; rotation < rotations; rotation++)
			{
				var angle = rotation * MathF.Tau / rotations;
				var cosAngle = CosSnapF(angle);
				var sinAngle = SinSnapF(angle);
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

		delegate float AmplitudeFunction(float wavelength);
		static float PinkAmplitudeFunction(float wavelength) => wavelength;
		static Matrix<float> FractalNoise2d(MersenneTwister random, int2 size, float wavelengthScale, AmplitudeFunction ampFunc)
		{
			var span = Math.Max(size.X, size.Y);
			var wavelengths = new float[(int)Math.Log2(span)];
			for (var i = 0; i < wavelengths.Length; i++)
			{
				wavelengths[i] = (1 << i) * wavelengthScale;
			}

			// float AmpFunc(float wavelength) => wavelength / span / wavelengths.Length;
			var noise = new Matrix<float>(size);
			foreach (var wavelength in wavelengths)
			{
				var amps = ampFunc(wavelength);
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
					var phase = MathF.Tau * random.NextFloatExclusive();
					var vx = MathF.Cos(phase);
					var vy = MathF.Sin(phase);
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
			var xa = (int)MathF.Floor(x);
			var xb = (int)MathF.Ceiling(x);
			var ya = (int)MathF.Floor(y);
			var yb = (int)MathF.Ceiling(y);

			// "w" for "weight"
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
				var value = MathF.Exp(-x * x / dsd2);
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
				minX = (int)MathF.Floor(center.X - radius);
				minY = (int)MathF.Floor(center.Y - radius);
				maxX = (int)MathF.Ceiling(center.X + radius);
				maxY = (int)MathF.Ceiling(center.Y + radius);
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

		static int2[][] BordersToPoints(Matrix<bool> matrix)
		{
			// There is redundant memory/iteration, but I don't care enough.

			// These are really only the signs of the gradients.
			var gradientH = new Matrix<sbyte>(matrix.Size);
			var gradientV = new Matrix<sbyte>(matrix.Size);
			for (var y = 0; y < matrix.Size.Y; y++)
			{
				for (var x = 1; x < matrix.Size.X; x++)
				{
					var l = matrix[x - 1, y] ? 1 : 0;
					var r = matrix[x, y] ? 1 : 0;
					gradientV[x, y] = (sbyte)(r - l);
				}
			}

			for (var y = 1; y < matrix.Size.Y; y++)
			{
				for (var x = 0; x < matrix.Size.X; x++)
				{
					var u = matrix[x, y - 1] ? 1 : 0;
					var d = matrix[x, y] ? 1 : 0;
					gradientH[x, y] = (sbyte)(d - u);
				}
			}

			// Looping paths contain the start/end point twice.
			var paths = new List<int2[]>();
			void TracePath(int sx, int sy, int direction)
			{
				var points = new List<int2>();
				var x = sx;
				var y = sy;
				points.Add(new int2(x, y));
				do
				{
					switch (direction)
					{
						case DIRECTION_R:
							gradientH[x, y] = 0;
							x++;
							break;
						case DIRECTION_D:
							gradientV[x, y] = 0;
							y++;
							break;
						case DIRECTION_L:
							x--;
							gradientH[x, y] = 0;
							break;
						case DIRECTION_U:
							y--;
							gradientV[x, y] = 0;
							break;
						default:
							throw new ArgumentException("direction assertion failed");
					}

					points.Add(new int2(x, y));
					var r = gradientH.ContainsXY(x, y) && gradientH[x, y] > 0;
					var d = gradientV.ContainsXY(x, y) && gradientV[x, y] < 0;
					var l = gradientH.ContainsXY(x - 1, y) && gradientH[x - 1, y] < 0;
					var u = gradientV.ContainsXY(x, y - 1) && gradientV[x, y - 1] > 0;
					if (direction == DIRECTION_R && u)
						direction = DIRECTION_U;
					else if (direction == DIRECTION_D && r)
						direction = DIRECTION_R;
					else if (direction == DIRECTION_L && d)
						direction = DIRECTION_D;
					else if (direction == DIRECTION_U && l)
						direction = DIRECTION_L;
					else if (r)
						direction = DIRECTION_R;
					else if (d)
						direction = DIRECTION_D;
					else if (l)
						direction = DIRECTION_L;
					else if (u)
						direction = DIRECTION_U;
					else
						break; // Dead end (not a loop)
				}
				while (x != sx || y != sy);

				paths.Add(points.ToArray());
			}

			// Trace non-loops (from edge of map)
			for (var x = 1; x < matrix.Size.X; x++)
			{
				if (gradientV[x, 0] < 0)
					TracePath(x, 0, DIRECTION_D);
				if (gradientV[x, matrix.Size.Y - 1] > 0)
					TracePath(x, matrix.Size.Y, DIRECTION_U);
			}

			for (var y = 1; y < matrix.Size.Y; y++)
			{
				if (gradientH[0, y] > 0)
					TracePath(0, y, DIRECTION_R);
				if (gradientH[matrix.Size.X - 1, y] < 0)
					TracePath(matrix.Size.X, y, DIRECTION_L);
			}

			// Trace loops
			for (var y = 0; y < matrix.Size.Y; y++)
			{
				for (var x = 0; x < matrix.Size.X; x++)
				{
					if (gradientH[x, y] > 0)
						TracePath(x, y, DIRECTION_R);
					else if (gradientH[x, y] < 0)
						TracePath(x + 1, y, DIRECTION_L);

					if (gradientV[x, y] < 0)
						TracePath(x, y, DIRECTION_D);
					else if (gradientV[x, y] > 0)
						TracePath(x, y + 1, DIRECTION_U);
				}
			}

			return paths.ToArray();
		}

		// <summary>
		// Modifies a points (for a path) to be easier to tile.
		// For loops, the points are made to start and end within the longest straight.
		// For map edge-connected paths, the path is extended beyond the edge.
		static int2[] TweakPathPoints(int2[] points, int2 size)
		{
			var len = points.Length;
			var last = len - 1;
			if (points[0].X == points[last].X && points[0].Y == points[last].Y)
			{
				// Closed loop. Find the longest straight
				// (nrlen excludes the repeated point at the end.)
				var nrlen = len - 1;
				var prevDim = -1;
				var scanStart = -1;
				var bestScore = -1;
				var bestBend = -1;
				var prevBend = -1;
				var prevI = 0;
				for (var i = 1; ; i++)
				{
					if (i == nrlen)
						i = 0;
					var dim = points[i].X == points[prevI].X ? 1 : 0;
					if (prevDim != -1 && prevDim != dim)
					{
						if (scanStart == -1)
						{
							// This is technically just after the bend. But that's fine.
							scanStart = i;
						}
						else
						{
							var score = prevI - prevBend;
							if (score < 0)
								score += nrlen;

							if (score > bestScore)
							{
								bestBend = prevBend;
								bestScore = score;
							}

							if (i == scanStart)
							{
								break;
							}
						}

						prevBend = prevI;
					}

					prevDim = dim;
					prevI = i;
				}

				var favouritePoint = (bestBend + (bestScore >> 1)) % nrlen;

				// Repeat the start at the end.
				// [...points.slice(favouritePoint, nrlen), ...points.slice(0, favouritePoint + 1)];
				var tweaked = new int2[points.Length];
				Array.Copy(points, favouritePoint, tweaked, 0, nrlen - favouritePoint);
				Array.Copy(points, 0, tweaked, nrlen - favouritePoint, favouritePoint + 1);
				return tweaked;
			}
			else
			{
				// Not a loop.
				int2[] Extend(int2 point, int extensionLength)
				{
					var ox = (point.X == 0) ? -1
						: (point.X == size.X) ? 1
						: 0;
					var oy = (point.Y == 0) ? -1
						: (point.Y == size.Y) ? 1
						: 0;
					if (ox == 0 && oy == 0)
						return Array.Empty<int2>(); // We're not on an edge, so don't extend.
					var offset = new int2(ox, oy);

					var extension = new int2[extensionLength];
					var newPoint = point;
					for (var i = 0; i < extensionLength; i++)
					{
						newPoint += offset;
						extension[i] = newPoint;
					}

					return extension;
				}

				// Open paths. Extend if beyond edges.
				var startExt = Extend(points[0], /*extensionLength=*/4).Reverse().ToArray();
				var endExt = Extend(points[last], /*extensionLength=*/4);

				// [...startExt, ...points, ...endExt];
				var tweaked = new int2[points.Length + startExt.Length + endExt.Length];
				Array.Copy(startExt, 0, tweaked, 0, startExt.Length);
				Array.Copy(points, 0, tweaked, startExt.Length, points.Length);
				Array.Copy(endExt, 0, tweaked, points.Length + startExt.Length, endExt.Length);
				return tweaked;
			}
		}

		class TilePathSegment
		{
			public readonly TerrainTemplateInfo TemplateInfo;
			public readonly TemplateSegment TemplateSegment;
			public readonly int StartTypeId;
			public readonly int EndTypeId;
			public readonly int2 Offset;
			public readonly int2 Moves;
			public readonly int2[] RelativePoints;
			public readonly int[] Directions;
			public readonly int[] DirectionMasks;
			public readonly int[] ReverseDirectionMasks;

			public TilePathSegment(TerrainTemplateInfo templateInfo, TemplateSegment templateSegment, int startId, int endId)
			{
				TemplateInfo = templateInfo;
				TemplateSegment = templateSegment;
				StartTypeId = startId;
				EndTypeId = endId;
				Offset = templateSegment.Points[0];
				Moves = templateSegment.Points[^1] - Offset;
				RelativePoints = templateSegment.Points
					.Select(p => p - templateSegment.Points[0])
					.ToArray();

				Directions = new int[RelativePoints.Length];
				DirectionMasks = new int[RelativePoints.Length];
				ReverseDirectionMasks = new int[RelativePoints.Length];

				// Last point has no direction.
				Directions[^1] = DIRECTION_NONE;
				DirectionMasks[^1] = 0;
				ReverseDirectionMasks[^1] = 0;
				for (var i = 0; i < RelativePoints.Length - 1; i++)
				{
					var direction = CalculateDirection(RelativePoints[i + 1] - RelativePoints[i]);
					if (direction == DIRECTION_NONE)
						throw new ArgumentException("TemplateSegment has duplicate points in sequence");
					Directions[i] = direction;
					DirectionMasks[i] = 1 << direction;
					ReverseDirectionMasks[i] = 1 << ReverseDirection(direction);
				}
			}
		}

		static int2[] TilePath(Map map, Path path, MersenneTwister random, int minimumThickness)
		{
			var maxDeviation = (minimumThickness - 1) >> 1;
			var minPoint = new int2(
				path.Points.Min(p => p.X) - maxDeviation,
				path.Points.Min(p => p.Y) - maxDeviation);
			var maxPoint = new int2(
				path.Points.Max(p => p.X) + maxDeviation,
				path.Points.Max(p => p.Y) + maxDeviation);
			var points = path.Points.Select(point => point - minPoint).ToArray();

			var isLoop = path.IsLoop;

			// grid points (not squares), so these are offset 0.5 from tile centers.
			var size = new int2(1 + maxPoint.X - minPoint.X, 1 + maxPoint.Y - minPoint.Y);
			var sizeXY = size.X * size.Y;

			const int MAX_DEVIATION = int.MaxValue;

			// Bit masks of 8-angle directions which are considered a positive progress
			// traversal. Template choices with an overall negative progress traversal
			// are rejected.
			var directions = new Matrix<byte>(size);

			// How far away from the path this point is.
			var deviations = new Matrix<int>(size).Fill(MAX_DEVIATION);

			// Bit masks of 8-angle directions which define whether it's permitted
			// to traverse from one point to a given neighbour.
			var traversables = new Matrix<byte>(size);
			{
				var gradientX = new Matrix<int>(size);
				var gradientY = new Matrix<int>(size);
				for (var pointI = 0; pointI < points.Length; pointI++)
				{
					if (isLoop && pointI == 0)
					{
						// Same as last point.
						continue;
					}

					var point = points[pointI];
					var pointPrevI = pointI - 1;
					var pointNextI = pointI + 1;
					var directionX = 0;
					var directionY = 0;
					if (pointNextI < points.Length)
					{
						directionX += points[pointNextI].X - point.X;
						directionY += points[pointNextI].Y - point.Y;
					}

					if (pointPrevI >= 0)
					{
						directionX += point.X - points[pointPrevI].X;
						directionY += point.Y - points[pointPrevI].Y;
					}

					for (var deviation = 0; deviation <= maxDeviation; deviation++)
					{
						var minX = point.X - deviation;
						var minY = point.Y - deviation;
						var maxX = point.X + deviation;
						var maxY = point.Y + deviation;
						for (var y = minY; y <= maxY; y++)
						{
							for (var x = minX; x <= maxX; x++)
							{
								// const i = y * sizeX + x;
								if (deviation < deviations[x, y])
								{
									deviations[x, y] = deviation;
								}

								if (deviation == maxDeviation)
								{
									gradientX[x, y] += directionX;
									gradientY[x, y] += directionY;
									if (x > minX)
										traversables[x, y] |= DIRECTION_M_L;
									if (x < maxX)
										traversables[x, y] |= DIRECTION_M_R;
									if (y > minY)
										traversables[x, y] |= DIRECTION_M_U;
									if (y < maxY)
										traversables[x, y] |= DIRECTION_M_D;
									if (x > minX && y > minY)
										traversables[x, y] |= DIRECTION_M_LU;
									if (x > minX && y < maxY)
										traversables[x, y] |= DIRECTION_M_LD;
									if (x < maxX && y > minY)
										traversables[x, y] |= DIRECTION_M_RU;
									if (x < maxX && y < maxY)
										traversables[x, y] |= DIRECTION_M_RD;
								}
							}
						}
					}
				}

				// Probational
				for (var i = 0; i < sizeXY; i++)
				{
					if (gradientX[i] == 0 && gradientY[i] == 0)
					{
						directions[i] = 0;
						continue;
					}

					var direction = CalculateDirection(gradientX[i], gradientY[i]);

					// .... direction: 0123456701234567
					//                 UUU DDD UUU DDD
					//                 R LLL RRR LLL R
					directions[i] = (byte)(0b100000111000001 >> (7 - direction));
				}
			}

			var pathStart = points[0];
			var pathEnd = points[^1];
			var permittedTemplates = path.PermittedTemplates.All.ToImmutableHashSet();

			const int MAX_SCORE = int.MaxValue;
			var segmentTypeToId = new Dictionary<string, int>();
			var segmentsByStart = new List<List<TilePathSegment>>();
			var segmentsByEnd = new List<List<TilePathSegment>>();
			var scores = new List<Matrix<int>>();
			{
				void RegisterSegmentType(string type)
				{
					if (segmentTypeToId.ContainsKey(type)) return;
					var newId = segmentTypeToId.Count;
					segmentTypeToId.Add(type, newId);
					segmentsByStart.Add(new List<TilePathSegment>());
					segmentsByEnd.Add(new List<TilePathSegment>());
					scores.Add(new Matrix<int>(size).Fill(MAX_SCORE));
				}

				foreach (var template in permittedTemplates)
				{
					foreach (var segment in template.Segments)
					{
						RegisterSegmentType(segment.Start);
						RegisterSegmentType(segment.End);
						var startTypeId = segmentTypeToId[segment.Start];
						var endTypeId = segmentTypeToId[segment.End];
						var tilePathSegment = new TilePathSegment(template, segment, startTypeId, endTypeId);
						segmentsByStart[startTypeId].Add(tilePathSegment);
						segmentsByEnd[endTypeId].Add(tilePathSegment);
					}
				}
			}

			var totalTypeIds = segmentTypeToId.Count;

			var priorities = new PriorityArray<int>(totalTypeIds * size.X * size.Y, MAX_SCORE);
			void SetPriorityAt(int typeId, int2 pos, int priority)
				=> priorities[typeId * sizeXY + pos.Y * size.X + pos.X] = priority;
			(int TypeId, int2 Pos, int Priority) GetNextPriority()
			{
				var index = priorities.GetMinIndex();
				var priority = priorities[index];
				var typeId = index / sizeXY;
				var xy = index % sizeXY;
				return (typeId, new int2(xy % size.X, xy / size.X), priority);
			}

			var pathStartTypeId = segmentTypeToId[path.Start.SegmentType];
			var pathEndTypeId = segmentTypeToId[path.End.SegmentType];
			var innerTypeIds = path.PermittedTemplates.Inner
				.SelectMany(template => template.Segments)
				.SelectMany(segment => new[] { segment.Start, segment.End })
				.Select(segmentType => segmentTypeToId[segmentType])
				.ToImmutableHashSet();

			// Assumes both f and t are in the sizeX/sizeY bounds.
			// Lower (closer to zero) scores are better matches.
			// Higher scores are worse matches.
			// MAX_SCORE means totally unacceptable.
			int ScoreSegment(TilePathSegment segment, int2 from)
			{
				if (from == pathStart)
				{
					if (segment.StartTypeId != pathStartTypeId)
						return MAX_SCORE;
				}
				else
				{
					if (!innerTypeIds.Contains(segment.StartTypeId))
						return MAX_SCORE;
				}

				if (from + segment.Moves == pathEnd)
				{
					if (segment.EndTypeId != pathEndTypeId)
						return MAX_SCORE;
				}
				else
				{
					if (!innerTypeIds.Contains(segment.EndTypeId))
						return MAX_SCORE;
				}

				var deviationAcc = 0;
				var progressionAcc = 0;
				var lastPointI = segment.RelativePoints.Length - 1;
				for (var pointI = 0; pointI <= lastPointI; pointI++)
				{
					var point = from + segment.RelativePoints[pointI];
					var directionMask = segment.DirectionMasks[pointI];
					var reverseDirectionMask = segment.ReverseDirectionMasks[pointI];
					if (point.X < 0 || point.X >= size.X || point.Y < 0 || point.Y >= size.Y)
					{
						// Intermediate point escapes array bounds.
						return MAX_SCORE;
					}

					if (pointI < lastPointI)
					{
						if ((traversables[point] & directionMask) == 0)
						{
							// Next point escapes traversable area.
							return MAX_SCORE;
						}

						if ((directions[point] & directionMask) == directionMask)
						{
							progressionAcc++;
						}
						else if ((directions[point] & reverseDirectionMask) == reverseDirectionMask)
						{
							progressionAcc--;
						}
					}

					if (pointI > 0)
					{
						// Don't double-count the template's path's starts and ends
						deviationAcc += deviations[point];
					}
				}

				if (progressionAcc < 0)
				{
					// It's moved backwards
					return MAX_SCORE;
				}

				// Satisfies all requirements.
				return deviationAcc;
			}

			void UpdateFrom(int2 from, int fromTypeId)
			{
				var fromScore = scores[fromTypeId][from];
				foreach (var segment in segmentsByStart[fromTypeId])
				{
					var to = from + segment.Moves;
					if (to.X < 0 || to.X >= size.X || to.Y < 0 || to.Y >= size.Y)
					{
						continue;
					}

					// Most likely to fail. Check first.
					if (deviations[to] == MAX_DEVIATION)
					{
						// End escapes bounds.
						continue;
					}

					var segmentScore = ScoreSegment(segment, from);
					if (segmentScore == MAX_SCORE)
					{
						continue;
					}

					var toScore = fromScore + segmentScore;
					var toTypeId = segment.EndTypeId;
					if (toScore < scores[toTypeId][to])
					{
						scores[toTypeId][to] = toScore;
						SetPriorityAt(toTypeId, to, toScore);
					}
				}

				SetPriorityAt(fromTypeId, from, MAX_SCORE);
			}

			scores[pathStartTypeId][pathStart] = 0;
			UpdateFrom(pathStart, pathStartTypeId);

			// Needed in case we loop back to the start.
			scores[pathStartTypeId][pathStart] = MAX_SCORE;

			while (true)
			{
				var (fromTypeId, from, priority) = GetNextPriority();

				// TODO: Break if we're on the end point?
				if (priority == MAX_SCORE)
				{
					break;
				}

				UpdateFrom(from, fromTypeId);
			}

			// Trace back and update tiles
			var resultPoints = new List<int2>
			{
				pathEnd + minPoint
			};

			(int2 From, int FromTypeId) TraceBackStep(int2 to, int toTypeId)
			{
				var toScore = scores[toTypeId][to];
				var candidates = new List<TilePathSegment>();
				foreach (var segment in segmentsByEnd[toTypeId])
				{
					var from = to - segment.Moves;
					if (from.X < 0 || from.X >= size.X || from.Y < 0 || from.Y >= size.Y)
					{
						continue;
					}

					// Most likely to fail. Check first.
					if (deviations[from] == MAX_DEVIATION)
					{
						// Start escapes bounds.
						continue;
					}

					var segmentScore = ScoreSegment(segment, from);
					if (segmentScore == MAX_SCORE)
					{
						continue;
					}

					var fromScore = toScore - segmentScore;
					if (fromScore == scores[segment.StartTypeId][from])
					{
						candidates.Add(segment);
					}
				}

				Debug.Assert(candidates.Count >= 1, "TraceBack didn't find an original route");
				var chosenSegment = candidates[random.Next(candidates.Count)];
				var chosenFrom = to - chosenSegment.Moves;
				PaintTemplate(map, chosenFrom - chosenSegment.Offset + minPoint, chosenSegment.TemplateInfo);

				// Skip end point as it is recorded in the previous template.
				for (var i = chosenSegment.RelativePoints.Length - 2; i >= 0; i--)
				{
					var point = chosenSegment.RelativePoints[i];
					resultPoints.Add(chosenFrom + point + minPoint);
				}

				return (chosenFrom, chosenSegment.StartTypeId);
			}

			{
				var to = pathEnd;
				var toTypeId = pathEndTypeId;
				if (scores[toTypeId][to] == MAX_SCORE)
					throw new MapGenerationException("Could not fit tiles for path");
				(to, toTypeId) = TraceBackStep(to, toTypeId);

				// We previously set this to MAX_SCORE in case we were a loop. Reset it for getting back to the start.
				scores[pathStartTypeId][pathStart] = 0;

				// No need to check direction. If that is an issue, I have bigger problems to worry about.
				while (to != pathStart)
				{
					(to, toTypeId) = TraceBackStep(to, toTypeId);
				}
			}

			// Traced back in reverse, so reverse the reversal.
			resultPoints.Reverse();
			return resultPoints.ToArray();
		}

		static void PaintTemplate(Map map, int2 at, TerrainTemplateInfo template)
		{
			if (template.PickAny)
				throw new ArgumentException("PaintTemplate does not expect PickAny");
			for (var y = 0; y < template.Size.Y; y++)
			{
				for (var x = 0; x < template.Size.X; x++)
				{
					var i = (byte)(y * template.Size.X + x);
					if (template[i] == null)
						continue;
					var tile = new TerrainTile(template.Id, i);
					var mpos = new MPos(at.X + x, at.Y + y);
					if (map.Tiles.Contains(mpos))
						map.Tiles[mpos] = tile;
				}
			}
		}

		private Matrix<sbyte> PointsChirality(int2 size, int2[][] pointArrayArray)
		{
			var chirality = new Matrix<sbyte>(size);
			var next = new List<int2>();
			void SeedChirality(int2 point, sbyte value, bool firstPass)
			{
				if (!chirality.ContainsXY(point))
					return;
				if (firstPass)
				{
					// Some paths which overlap or go back on themselves
					// might fight for chirality. Vote on it.
					chirality[point] += value;
				}
				else
				{
					if (chirality[point] != 0)
						return;
					chirality[point] = value;
				}

				next.Add(point);
			}

			foreach (var pointArray in pointArrayArray)
			{
				for (var i = 1; i < pointArray.Length; i++)
				{
					var from = pointArray[i - 1];
					var to = pointArray[i];
					var direction = CalculateDirection(to - from);
					var fx = from.X;
					var fy = from.Y;
					switch (direction)
					{
						case DIRECTION_R:
							SeedChirality(new int2(fx    , fy    ),  1, true);
							SeedChirality(new int2(fx    , fy - 1), -1, true);
							break;
						case DIRECTION_D:
							SeedChirality(new int2(fx - 1, fy    ),  1, true);
							SeedChirality(new int2(fx    , fy    ), -1, true);
							break;
						case DIRECTION_L:
							SeedChirality(new int2(fx - 1, fy - 1),  1, true);
							SeedChirality(new int2(fx - 1, fy    ), -1, true);
							break;
						case DIRECTION_U:
							SeedChirality(new int2(fx    , fy - 1),  1, true);
							SeedChirality(new int2(fx - 1, fy - 1), -1, true);
							break;
						default:
							throw new ArgumentException("Unsupported direction for chirality");
					}
				}
			}

			while (next.Count != 0)
			{
				var current = next;
				next = new List<int2>();
				foreach (var point in current)
				{
					foreach (var offset in SPREAD4)
					{
						SeedChirality(point + offset, chirality[point], false);
					}
				}
			}

			return chirality;
		}

		// <summary>
		// Finds the local variance of points in a 2d grid (using a square sample area).
		// Sample areas are centered on data point corners, so output is (size + 1) * (size + 1).
		// </summary>
		static Matrix<float> GridVariance2d(Matrix<float> input, int radius)
		{
			var output = new Matrix<float>(input.Size + new int2(1, 1));
			for (var cy = 0; cy < output.Size.Y; cy++)
			{
				for (var cx = 0; cx < output.Size.X; cx++)
				{
					var total = 0.0f;
					var samples = 0;
					for (var ry = -radius; ry < radius; ry++)
					{
						for (var rx = -radius; rx < radius; rx++)
						{
							var y = cy + ry;
							var x = cx + rx;
							if (!input.ContainsXY(x, y))
								continue;
							total += input[x, y];
							samples++;
						}
					}

					var mean = total / samples;
					var sumOfSquares = 0.0f;
					for (var ry = -radius; ry < radius; ry++)
					{
						for (var rx = -radius; rx < radius; rx++)
						{
							var y = cy + ry;
							var x = cx + rx;
							if (!input.ContainsXY(x, y))
								continue;
							sumOfSquares += MathF.Pow(mean - input[x, y], 2);
						}
					}

					output[cx, cy] = sumOfSquares / samples;
				}
			}

			return output;
		}

		static Matrix<int> CalculateRoominess(Matrix<bool> elevations, bool roomyEdges)
		{
			var roominess = new Matrix<int>(elevations.Size);

			// This could be more efficient.
			var next = new List<int2>();

			// Find shores and map boundary
			for (var cy = 0; cy < elevations.Size.Y; cy++)
			{
				for (var cx = 0; cx < elevations.Size.X; cx++)
				{
					var pCount = 0;
					var nCount = 0;
					for (var oy = -1; oy <= 1; oy++)
					{
						for (var ox = -1; ox <= 1; ox++)
						{
							var x = cx + ox;
							var y = cy + oy;
							if (!elevations.ContainsXY(x, y))
							{
								// Boundary
							}
							else if (elevations[x, y])
								pCount++;
							else
								nCount++;
						}
					}

					if (roomyEdges && nCount + pCount != 9)
					{
						continue;
					}

					if (pCount != 9 && nCount != 9)
					{
						roominess[cx, cy] = elevations[cx, cy] ? 1 : -1;
						next.Add(new int2(cx, cy));
					}
				}
			}

			if (next.Count == 0)
			{
				// There were no shores. Use minSpan or -minSpan as appropriate.
				var minSpan = Math.Min(elevations.Size.X, elevations.Size.Y);
				roominess.Fill(elevations[0] ? minSpan : -minSpan);
				return roominess;
			}

			for (var distance = 2; next.Count != 0; distance++)
			{
				var current = next;
				next = new List<int2>();
				foreach (var point in current)
				{
					var cx = point.X;
					var cy = point.Y;
					for (var oy = -1; oy <= 1; oy++)
					{
						for (var ox = -1; ox <= 1; ox++)
						{
							if (ox == 0 && oy == 0)
								continue;
							var x = cx + ox;
							var y = cy + oy;
							if (!roominess.ContainsXY(x, y))
								continue;
							if (roominess[x, y] != 0)
								continue;
							roominess[x, y] = elevations[x, y] ? distance : -distance;
							next.Add(new int2(x, y));
						}
					}
				}
			}

			return roominess;
		}

		static int2[][] MaskPoints(int2[][] pointArrayArray, Matrix<bool> mask)
		{
			var newPointArrayArray = new List<int2[]>();

			foreach (var pointArray in pointArrayArray)
			{
				var isLoop = pointArray[0] == pointArray[^1];
				int firstBad;
				for (firstBad = 0; firstBad < pointArray.Length; firstBad++)
				{
					if (!mask[pointArray[firstBad]])
						break;
				}

				if (firstBad == pointArray.Length)
				{
					// The path is entirely within the mask already.
					newPointArrayArray.Add(pointArray);
					continue;
				}

				var startAt = isLoop ? firstBad : 0;
				var wrapAt = isLoop ? pointArray.Length - 1 : pointArray.Length;
				if (wrapAt == 0)
					throw new ArgumentException("single point paths should not exist");
				Debug.Assert(startAt < wrapAt, "start outside wrap bounds");
				var i = startAt;
				List<int2> currentPointArray = null;
				do
				{
					if (mask[pointArray[i]])
					{
						currentPointArray ??= new List<int2>();
						currentPointArray.Add(pointArray[i]);
					}
					else
					{
						if (currentPointArray != null && currentPointArray.Count > 1)
							newPointArrayArray.Add(currentPointArray.ToArray());
						currentPointArray = null;
					}

					i++;
					if (i == wrapAt)
						i = 0;
				}
				while (i != startAt);

				if (currentPointArray != null && currentPointArray.Count > 1)
					newPointArrayArray.Add(currentPointArray.ToArray());
			}

			return newPointArrayArray.ToArray();
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
