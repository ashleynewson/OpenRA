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

namespace OpenRA.Mods.Common.MapUtils
{
	public static class Symmetry
	{
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

		public enum Mirror
		{
			None = 0,
			LeftMatchesRight = 1,
			TopLeftMatchesBottomRight = 2,
			TopMatchesBottom = 3,
			TopRightMatchesBottomLeft = 4,
		}

		// <summary>
		// Math.Cos, but with special casing for special angles to preserve accuracy.
		// </summary>
		public static double CosSnap(double angle)
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

		// <summary>
		// Math.Sin, but with special casing for special angles to preserve accuracy.
		// </summary>
		public static double SinSnap(double angle)
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

		// <summary>
		// MathF.Cos, but with special casing for special angles to preserve accuracy.
		// </summary>
		public static float CosSnapF(float angle)
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

		// <summary>
		// MathF.Sin, but with special casing for special angles to preserve accuracy.
		// </summary>
		public static float SinSnapF(float angle)
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

		// <summary>
		// Mirrors a grid square within an area of given size.
		//
		// For example, if using a size of (8, 8) a square at (0, 0) could be projected to
		// (0, 0), (0, 7), (7, 0), (7, 7).
		// </summary>
		public static int2 MirrorGridSquare(Mirror mirror, int2 original, int2 size)
			=> MirrorPoint(mirror, original, size - new int2(1, 1));

		// <summary>
		// Mirrors a grid square within an area of given size.
		//
		// For example, if using a size of (8, 8) a square at (0.1, 0.1) could be projected to
		// (0.1, 0.1), (0.1, 6.9), (6.9, 0.1), (6.9, 6.9).
		// </summary>
		public static float2 MirrorGridSquare(Mirror mirror, float2 original, float2 size)
			=> MirrorPoint(mirror, original, size - new float2(1.0f, 1.0f));

		// <summary>
		// Mirrors a (zero-area) point within an area of given size.
		//
		// For example, if using a size of (8, 8) a point at (0, 0) could be projected to
		// (0, 0), (0, 8), (8, 0), (8, 8).
		// </summary>
		public static int2 MirrorPoint(Mirror mirror, int2 original, int2 size)
		{
			if (size.X != size.Y)
			{
				throw new NotImplementedException("Size.X must match Size.Y for now");
			}

			switch (mirror)
			{
				case Mirror.None:
					throw new ArgumentException("Mirror.None has no transformed point");
				case Mirror.LeftMatchesRight:
					return new int2(size.X - original.X, original.Y);
				case Mirror.TopLeftMatchesBottomRight:
					return new int2(size.Y - original.Y, size.X - original.X);
				case Mirror.TopMatchesBottom:
					return new int2(original.X, size.Y - original.Y);
				case Mirror.TopRightMatchesBottomLeft:
					return new int2(original.Y, original.X);
				default:
					throw new ArgumentException("Bad mirror");
			}
		}

		// <summary>
		// Mirrors a (zero-area) point within an area of given size.
		//
		// For example, if using a size of (8, 8) a point at (0.1, 0.1) could be projected to
		// (0.1, 0.1), (0.1, 7.9), (7.9, 0.1), (7.9, 7.9).
		// </summary>
		public static float2 MirrorPoint(Mirror mirror, float2 original, float2 size)
		{
			if (size.X != size.Y)
			{
				throw new NotImplementedException("Size.X must match Size.Y for now");
			}

			switch (mirror)
			{
				case Mirror.None:
					throw new ArgumentException("Mirror.None has no transformed point");
				case Mirror.LeftMatchesRight:
					return new float2(size.X - original.X, original.Y);
				case Mirror.TopLeftMatchesBottomRight:
					return new float2(size.Y - original.Y, size.X - original.X);
				case Mirror.TopMatchesBottom:
					return new float2(original.X, size.Y - original.Y);
				case Mirror.TopRightMatchesBottomLeft:
					return new float2(original.Y, original.X);
				default:
					throw new ArgumentException("Bad mirror");
			}
		}

		// <summary>
		// Given rotation and mirror parameters, return the total number of projected points this
		// would result in (including the original point).
		// </summary>
		public static int RotateAndMirrorProjectionCount(int rotations, Mirror mirror)
			=> mirror == Mirror.None ? rotations : rotations * 2;

		// <summary>
		// Duplicate an original grid square into an array of projected grid
		// squares according to a rotation and mirror specification. Projected
		// grid squares may lie outside of the bounds implied by size.
		//
		// Do not use this for points (which don't have area).
		//
		// For example, if using a size of (8, 8) a square at (0, 0) could be projected to
		// (0, 0), (0, 7), (7, 0), (7, 7).
		// </summary>
		public static int2[] RotateAndMirrorGridSquare(int2 original, int2 size, int rotations, Mirror mirror)
		{
			var floatProjections = RotateAndMirrorPoint(original, size - new int2(1, 1), rotations, mirror);
			var intProjections = new int2[floatProjections.Length];
			for (var i = 0; i < floatProjections.Length; i++)
			{
				intProjections[i] = new int2((int)MathF.Round(floatProjections[i].X), (int)MathF.Round(floatProjections[i].Y));
			}

			return intProjections;
		}

		// <summary>
		// Determine the shortest distance between projected grid squares
		// </summary>
		public static int RotateAndMirrorProjectionProximity(int2 original, int2 size, int rotations, Mirror mirror)
		{
			if (RotateAndMirrorProjectionCount(rotations, mirror) == 1)
				return int.MaxValue;
			var projections = RotateAndMirrorGridSquare(original, size, rotations, mirror);
			var worstSpacingSq = int.MaxValue;
			for (var i1 = 0; i1 < projections.Length; i1++)
			{
				for (var i2 = 0; i2 < projections.Length; i2++)
				{
					if (i1 == i2)
						continue;
					var spacingSq = (projections[i1] - projections[i2]).LengthSquared;
					if (spacingSq < worstSpacingSq)
						worstSpacingSq = spacingSq;
				}
			}

			return (int)MathF.Sqrt(worstSpacingSq);
		}

		// <summary>
		// Duplicate an original point into an array of projected points
		// according to a rotation and mirror specification. Projected points
		// may lie outside of the bounds implied by size.
		//
		// Do not use this for grid squares (which have area).
		//
		// For example, if using a size of (8, 8) a square at (0.1, 0.1) could be projected to
		// (0.1, 0.1), (0.1, 7.9), (7.9, 0.1), (7.9, 7.9).
		// </summary>
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

		// <summary>
		// Rotate and mirror multiple actor plans. See RotateAndMirrorActorPlan.
		// </summary>
		public static void RotateAndMirrorActorPlans(IList<ActorPlan> accumulator, IReadOnlyList<ActorPlan> originals, int rotations, Mirror mirror)
		{
			foreach (var original in originals)
			{
				RotateAndMirrorActorPlan(accumulator, original, rotations, mirror);
			}
		}

		// <summary>
		// Rotate and mirror a single actor plan, adding to an accumulator list.
		// Locations (CPos) are necessarily snapped to grid.
		// </summary>
		public static void RotateAndMirrorActorPlan(IList<ActorPlan> accumulator, ActorPlan original, int rotations, Mirror mirror)
		{
			var size = original.Map.MapSize;
			var points = RotateAndMirrorPoint(original.CenterLocation, size, rotations, mirror);
			foreach (var point in points)
			{
				var plan = original.Clone();
				plan.CenterLocation = point;
				accumulator.Add(plan);
			}
		}

		// <summary>
		// Calls action(sources, destination) over all possible destination
		// grid squares, where each source in sources is a mirrored/rotated
		// point. For non-trivial rotations, sources may be outside the bounds
		// defined by size.
		// </summary>
		public static void RotateAndMirrorOverGridSquares(int2 size, int rotations, Symmetry.Mirror mirror, Action<int2[], int2> action)
		{
			for (var y = 0; y < size.Y; y++)
			{
				for (var x = 0; x < size.X; x++)
				{
					var destination = new int2(x, y);
					var sources = Symmetry.RotateAndMirrorGridSquare(destination, size, rotations, mirror);
					action(sources, destination);
				}
			}
		}
	}
}
