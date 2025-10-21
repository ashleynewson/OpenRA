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
using OpenRA.FileSystem;
using OpenRA.Primitives;
using OpenRA.Support;
using OpenRA.Traits;

namespace OpenRA
{
	public interface ITerrainLoader
	{
		ITerrainInfo ParseTerrain(IReadOnlyFileSystem fileSystem, string path);
	}

	public interface ITerrainInfo
	{
		string Id { get; }
		string Name { get; }
		Size TileSize { get; }
		TerrainTypeInfo[] TerrainTypes { get; }
		TerrainTileInfo GetTerrainInfo(TerrainTile r);
		bool TryGetTerrainInfo(TerrainTile r, out TerrainTileInfo info);
		byte GetTerrainIndex(string type);
		byte GetTerrainIndex(TerrainTile r);
		TerrainTile DefaultTerrainTile { get; }

		Color[] HeightDebugColors { get; }
		IEnumerable<Color> RestrictedPlayerColors { get; }
		float MinHeightColorBrightness { get; }
		float MaxHeightColorBrightness { get; }
	}

	public readonly struct Riser
	{
		public enum Connection
		{
			UL = 0,
			UR = 1,
			RU = 2,
			RD = 3,
			DR = 4,
			DL = 5,
			LD = 6,
			LU = 7,
		}

		public static CVec ConnectionFromCorner(Connection connection)
		{
			switch (connection)
			{
				case Connection.LU:
				case Connection.UL:
					return new CVec(0, 0);
				case Connection.UR:
				case Connection.RU:
					return new CVec(1, 0);
				case Connection.RD:
				case Connection.DR:
					return new CVec(1, 1);
				case Connection.DL:
				case Connection.LD:
					return new CVec(0, 1);
			}

			throw new ArgumentException("invalid connection");
		}

		public static CVec ConnectionToCorner(Connection connection)
		{
			switch (connection)
			{
				case Connection.LU:
					return new CVec(-1, 0);
				case Connection.UL:
					return new CVec(0, -1);
				case Connection.UR:
					return new CVec(1, -1);
				case Connection.RU:
					return new CVec(2, 0);
				case Connection.RD:
					return new CVec(2, 1);
				case Connection.DR:
					return new CVec(1, 2);
				case Connection.DL:
					return new CVec(0, 2);
				case Connection.LD:
					return new CVec(-1, 1);
			}

			throw new ArgumentException("invalid connection");
		}

		public const byte Default = byte.MaxValue;
		readonly ulong bits = ulong.MaxValue;

		public Riser()
		{ }

		public Riser(string definition)
		{
			if (definition == null)
				return;

			var parts = definition.Split(",");
			if (parts.Length == 8)
			{
				bits = 0;
				for (var i = 0; i < 8; i++)
				{
					if (!Exts.TryParseByteInvariant(parts[i], out var b))
						throw new YamlException($"{definition} is not a valid Riser definition");

					bits |= (ulong)b << (i * 8);
				}
			}
			else
			{
				// TODO: make stricter
				if (definition.Contains('U', StringComparison.InvariantCultureIgnoreCase))
					bits &= 0xff_ff_ff_ff_ff_ff_00_00;

				if (definition.Contains('R', StringComparison.InvariantCultureIgnoreCase))
					bits &= 0xff_ff_ff_ff_00_00_ff_ff;

				if (definition.Contains('D', StringComparison.InvariantCultureIgnoreCase))
					bits &= 0xff_ff_00_00_ff_ff_ff_ff;

				if (definition.Contains('L', StringComparison.InvariantCultureIgnoreCase))
					bits &= 0x00_00_ff_ff_ff_ff_ff_ff;
			}
		}

		public readonly byte? this[int i]
		{
			get
			{
				if (i < 0 || i >= 8)
					throw new IndexOutOfRangeException();

				var b = (byte)((bits >> (i * 8)) & 0xff);
				return b != Default ? b : null;
			}
		}

		public readonly byte? this[Connection c]
		{
			get => this[(int)c];
		}
	}

	public class TerrainTileInfo
	{
		[FieldLoader.Ignore]
		public readonly byte TerrainType = byte.MaxValue;
		public readonly byte Height;
		public readonly byte RampType;
		public readonly Color MinColor;
		public readonly Color MaxColor;
		[FieldLoader.LoadUsing(nameof(LoadRiser))]
		public readonly Riser Riser;

		// Needs to be defined for subclasses
		public static object LoadRiser(MiniYaml my)
		{
			return new Riser(my.NodeWithKeyOrDefault("Riser")?.Value.Value);
		}

		public Color GetColor(MersenneTwister random)
		{
			if (MinColor != MaxColor)
				return Exts.ColorLerp(random.NextFloat(), MinColor, MaxColor);

			return MinColor;
		}
	}

	public class TerrainTypeInfo
	{
		public readonly string Type;
		public readonly BitSet<TargetableType> TargetTypes;
		public readonly HashSet<string> AcceptsSmudgeType = [];
		public readonly Color Color;
		public readonly bool RestrictPlayerColor = false;

		public TerrainTypeInfo(MiniYaml my) { FieldLoader.Load(this, my); }
	}

	// HACK: Temporary placeholder to avoid having to change all the traits that reference this constant.
	// This can be removed after the palette references have been moved from traits to sequences.
	public static class TileSet
	{
		public const string TerrainPaletteInternalName = "terrain";
	}
}
