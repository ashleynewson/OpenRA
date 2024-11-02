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

namespace OpenRA.Mods.Common.MapUtils
{
	public static class Symmetry
	{
		const float Degrees0 = 0.0f;
		const float Degrees90 = MathF.Tau * 0.25f;
		const float Degrees180 = MathF.Tau * 0.5f;
		const float Degrees270 = MathF.Tau * 0.75f;
		const float Degrees360 = MathF.Tau * 1.0f;
		const float Degrees120 = MathF.Tau * (1.0f / 3.0f);
		const float Degrees240 = MathF.Tau * (2.0f / 3.0f);

		const float Cos0 = 1.0f;
		const float Cos90 = 0.0f;
		const float Cos180 = -1.0f;
		const float Cos270 = 0.0f;
		const float Cos360 = 1.0f;
		const float Cos120 = -0.5f;
		const float Cos240 = -0.5f;

		const float Sin0 = 0.0f;
		const float Sin90 = 1.0f;
		const float Sin180 = 0.0f;
		const float Sin270 = -1.0f;
		const float Sin360 = 0.0f;
		const float Sin120 = 0.86602540378443864676f;
		const float Sin240 = -0.86602540378443864676f;

		/// <summary>Trivial mirroring configurations.</summary>
		public enum Mirror
		{
			None = 0,
			LeftMatchesRight = 1,
			TopLeftMatchesBottomRight = 2,
			TopMatchesBottom = 3,
			TopRightMatchesBottomLeft = 4,
		}

		/// <summary>
		/// MathF.Cos, but with special casing for special angles to preserve accuracy.
		/// </summary>
		static float CosSnapF(float angle)
		{
			switch (angle)
			{
				case Degrees0:
					return Cos0;
				case Degrees90:
					return Cos90;
				case Degrees180:
					return Cos180;
				case Degrees270:
					return Cos270;
				case Degrees360:
					return Cos360;
				case Degrees120:
					return Cos120;
				case Degrees240:
					return Cos240;
				default:
					return MathF.Cos(angle);
			}
		}

		/// <summary>
		/// MathF.Sin, but with special casing for special angles to preserve accuracy.
		/// </summary>
		static float SinSnapF(float angle)
		{
			switch (angle)
			{
				case Degrees0:
					return Sin0;
				case Degrees90:
					return Sin90;
				case Degrees180:
					return Sin180;
				case Degrees270:
					return Sin270;
				case Degrees360:
					return Sin360;
				case Degrees120:
					return Sin120;
				case Degrees240:
					return Sin240;
				default:
					return MathF.Sin(angle);
			}
		}

		/// <summary>
		/// <para>
		/// Mirrors a grid square within an area of given size.
		/// </para>
		/// <para>
		/// For example, if using a size of (8, 8) a square at (0, 0) could be projected to
		/// (0, 0), (0, 7), (7, 0), (7, 7).
		/// </para>
		/// </summary>
		public static int2 MirrorGridSquare(Mirror mirror, int2 original, int2 size)
			=> MirrorPoint(mirror, original, size - new int2(1, 1));

		/// <summary>
		/// <para>
		/// Mirrors a grid square within an area of given size.
		/// </para>
		/// <para>
		/// For example, if using a size of (8, 8) a square at (0.1, 0.1) could be projected to
		/// (0.1, 0.1), (0.1, 6.9), (6.9, 0.1), (6.9, 6.9).
		/// </para>
		/// </summary>
		public static float2 MirrorGridSquare(Mirror mirror, float2 original, float2 size)
			=> MirrorPoint(mirror, original, size - new float2(1.0f, 1.0f));

		/// <summary>
		/// <para>
		/// Mirrors a (zero-area) point within an area of given size.
		/// </para>
		/// <para>
		/// For example, if using a size of (8, 8) a point at (0, 0) could be projected to
		/// (0, 0), (0, 8), (8, 0), (8, 8).
		/// </para>
		/// </summary>
		public static int2 MirrorPoint(Mirror mirror, int2 original, int2 size)
		{
			switch (mirror)
			{
				case Mirror.None:
					throw new ArgumentException("Mirror.None has no transformed point");
				case Mirror.LeftMatchesRight:
					return new int2(size.X - original.X, original.Y);
				case Mirror.TopLeftMatchesBottomRight:
					return new int2(
						(size.Y - 2 * original.Y + size.X) / 2,
						(size.X - 2 * original.X + size.Y) / 2);
				case Mirror.TopMatchesBottom:
					return new int2(original.X, size.Y - original.Y);
				case Mirror.TopRightMatchesBottomLeft:
					return new int2(
						(size.X + 2 * original.Y - size.Y) / 2,
						(size.Y + 2 * original.X - size.X) / 2);
				default:
					throw new ArgumentException("Bad mirror");
			}
		}

		/// <summary>
		/// <para>
		/// Mirrors a (zero-area) point within an area of given size.
		/// </para>
		/// <para>
		/// For example, if using a size of (8, 8) a point at (0.1, 0.1) could be projected to
		/// (0.1, 0.1), (0.1, 7.9), (7.9, 0.1), (7.9, 7.9).
		/// </para>
		/// </summary>
		public static float2 MirrorPoint(Mirror mirror, float2 original, float2 size)
		{
			switch (mirror)
			{
				case Mirror.None:
					throw new ArgumentException("Mirror.None has no transformed point");
				case Mirror.LeftMatchesRight:
					return new float2(size.X - original.X, original.Y);
				case Mirror.TopLeftMatchesBottomRight:
					return new float2(
						(size.Y - 2.0f * original.Y + size.X) / 2.0f,
						(size.X - 2.0f * original.X + size.Y) / 2.0f);
				case Mirror.TopMatchesBottom:
					return new float2(original.X, size.Y - original.Y);
				case Mirror.TopRightMatchesBottomLeft:
					return new float2(
						(size.X + 2.0f * original.Y - size.Y) / 2.0f,
						(size.Y + 2.0f * original.X - size.X) / 2.0f);
				default:
					throw new ArgumentException("Bad mirror");
			}
		}

		/// <summary>
		/// Given rotation and mirror parameters, return the total number of projected points this
		/// would result in (including the original point).
		/// </summary>
		public static int RotateAndMirrorProjectionCount(int rotations, Mirror mirror)
			=> mirror == Mirror.None ? rotations : rotations * 2;

		/// <summary>
		/// <para>
		/// Duplicate an original grid square into an array of projected grid
		/// squares according to a rotation and mirror specification. Projected
		/// grid squares may lie outside of the bounds implied by size.
		/// </para>
		/// <para>
		/// Do not use this for points (which don't have area).
		/// </para>
		/// <para>
		/// For example, if using a size of (8, 8) a square at (0, 0) could be projected to
		/// (0, 0), (0, 7), (7, 0), (7, 7).
		/// </para>
		/// </summary>
		public static int2[] RotateAndMirrorGridSquare(int2 original, int2 size, int rotations, Mirror mirror)
		{
			var floatProjections = RotateAndMirrorPoint(original, size - new int2(1, 1), rotations, mirror);
			var intProjections = new int2[floatProjections.Length];
			for (var i = 0; i < floatProjections.Length; i++)
				intProjections[i] = new int2((int)MathF.Round(floatProjections[i].X), (int)MathF.Round(floatProjections[i].Y));

			return intProjections;
		}

		/// <summary>
	/// Determine the shortest distance between projected grid squares.
		/// </summary>
		public static int RotateAndMirrorProjectionProximity(int2 original, int2 size, int rotations, Mirror mirror)
		{
			if (RotateAndMirrorProjectionCount(rotations, mirror) == 1)
				return int.MaxValue;
			var projections = RotateAndMirrorGridSquare(original, size, rotations, mirror);
			var worstSpacingSq = int.MaxValue;
			for (var i1 = 0; i1 < projections.Length; i1++)
				for (var i2 = 0; i2 < projections.Length; i2++)
				{
					if (i1 == i2)
						continue;
					var spacingSq = (projections[i1] - projections[i2]).LengthSquared;
					if (spacingSq < worstSpacingSq)
						worstSpacingSq = spacingSq;
				}

			return (int)MathF.Sqrt(worstSpacingSq);
		}

		/// <summary>
		/// <para>
		/// Duplicate an original point into an array of projected points
		/// according to a rotation and mirror specification. Projected points
		/// may lie outside of the bounds implied by size.
		/// </para>
		/// <para>
		/// Do not use this for grid squares (which have area).
		/// </para>
		/// <para>
		/// For example, if using a size of (8, 8) a square at (0.1, 0.1) could be projected to
		/// (0.1, 0.1), (0.1, 7.9), (7.9, 0.1), (7.9, 7.9).
		/// </para>
		/// </summary>
		public static float2[] RotateAndMirrorPoint(float2 original, int2 size, int rotations, Mirror mirror)
		{
			var projections = new float2[RotateAndMirrorProjectionCount(rotations, mirror)];
			var projectionIndex = 0;

			var center = new float2(size.X / 2.0f, size.Y / 2.0f);
			for (var rotation = 0; rotation < rotations; rotation++)
			{
				var angle = rotation * MathF.Tau / rotations;
				var cosAngle = CosSnapF(angle);
				var sinAngle = SinSnapF(angle);
				var relOrig = original - center;
				var projX = relOrig.X * cosAngle - relOrig.Y * sinAngle + center.X;
				var projY = relOrig.X * sinAngle + relOrig.Y * cosAngle + center.Y;
				var projection = new float2(projX, projY);
				projections[projectionIndex++] = projection;

				if (mirror != Mirror.None)
					projections[projectionIndex++] = MirrorPoint(mirror, projection, size);
			}

			return projections;
		}

		/// <summary>
		/// Rotate and mirror multiple actor plans. See RotateAndMirrorActorPlan.
		/// </summary>
		public static ImmutableArray<ActorPlan> RotateAndMirrorActorPlans(IReadOnlyList<ActorPlan> originals, int rotations, Mirror mirror)
		{
			var projections = new List<ActorPlan>(
				originals.Count * RotateAndMirrorProjectionCount(rotations, mirror));
			foreach (var original in originals)
				projections.AddRange(RotateAndMirrorActorPlan(original, rotations, mirror));

			return projections.ToImmutableArray();
		}

		/// <summary>
		/// Rotate and mirror a single actor plan, adding to an accumulator list.
		/// Locations (CPos) are necessarily snapped to grid.
		/// </summary>
		public static ImmutableArray<ActorPlan> RotateAndMirrorActorPlan(ActorPlan original, int rotations, Mirror mirror)
		{
			var projections = new List<ActorPlan>(RotateAndMirrorProjectionCount(rotations, mirror));
			var size = original.Map.MapSize;
			var points = RotateAndMirrorPoint(original.CenterLocation, size, rotations, mirror);
			foreach (var point in points)
			{
				var plan = original.Clone();
				plan.CenterLocation = point;
				projections.Add(plan);
			}

			return projections.ToImmutableArray();
		}

		/// <summary>
		/// Calls action(sources, destination) over all possible destination
		/// grid squares, where each source in sources is a mirrored/rotated
		/// point. For non-trivial rotations, sources may be outside the bounds
		/// defined by size.
		/// </summary>
		public static void RotateAndMirrorOverGridSquares(int2 size, int rotations, Mirror mirror, Action<int2[], int2> action)
		{
			for (var y = 0; y < size.Y; y++)
				for (var x = 0; x < size.X; x++)
				{
					var destination = new int2(x, y);
					var sources = RotateAndMirrorGridSquare(destination, size, rotations, mirror);
					action(sources, destination);
				}
		}
	}
}
