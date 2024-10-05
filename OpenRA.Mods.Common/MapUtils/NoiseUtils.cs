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
using OpenRA.Support;

namespace OpenRA.Mods.Common.MapUtils
{
	public static class NoiseUtils
	{
		// <summary>Amplitude proportional to wavelength.</summary>
		public static float PinkAmplitude(float wavelength) => wavelength;

		// <summary>
		// Create noise by combining multiple layers of Perlin noise of halving wavelengths.
		//
		// wavelengthScale defines the largest wavelength as a fraction of the largest dimension of
		// the output.
		//
		// ampFunc specifies the amplitude of each wavelength. PinkAmplitudeFunction is often a
		// suitable choice.
		// </summary>
		public static Matrix<float> FractalNoise(
			MersenneTwister random,
			int2 size,
			float wavelengthScale,
			Func<float, float> ampFunc)
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
				var subNoise = PerlinNoise(random, subSpan);

				// Offsets should align to grid.
				// (The wavelength is divided back out later.)
				var offsetX = (int)(random.NextFloat() * wavelength);
				var offsetY = (int)(random.NextFloat() * wavelength);
				for (var y = 0; y < size.Y; y++)
				{
					for (var x = 0; x < size.X; x++)
					{
						noise[y * size.X + x] +=
							amps * MatrixUtils.Interpolate(
								subNoise,
								(offsetX + x) / wavelength,
								(offsetY + y) / wavelength);
					}
				}
			}

			return noise;
		}

		// TODO: This could accept a scale input and use interpolation.
		// <summary>
		// 2D Perlin Noise generator, producing a span-by-span sized matrix.
		// </summary>
		public static Matrix<float> PerlinNoise(MersenneTwister random, int span)
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

		public static Matrix<float> SymmetricFractalNoise(
			MersenneTwister random,
			int2 size,
			int rotations,
			Symmetry.Mirror mirror,
			float wavelengthScale,
			Func<float, float> ampFunc)
		{
			if (rotations < 1)
				throw new ArgumentException("rotations must be >= 1");

			// Need higher resolution due to cropping and rotation artifacts
			var templateSpan = Math.Max(size.X, size.Y) * 2 + 2;
			var templateSize = new int2(templateSpan, templateSpan);
			var template = FractalNoise(random, templateSize, wavelengthScale, ampFunc);
			var unmirrored = new Matrix<float>(size);

			// This -1 is required to compensate for the top-left vs the center of a grid square.
			var offset = new float2((size.X - 1) / 2.0f, (size.Y - 1) / 2.0f);
			var templateOffset = new float2(templateSpan / 2.0f, templateSpan / 2.0f);
			for (var rotation = 0; rotation < rotations; rotation++)
			{
				var angle = rotation * MathF.Tau / rotations;
				var cosAngle = Symmetry.CosSnapF(angle);
				var sinAngle = Symmetry.SinSnapF(angle);
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
						const float SQRT2 = 1.4142135623730951f;
						var midt = (xy - offset) * (float)SQRT2;
						var tx = (midt.X * cosAngle - midt.Y * sinAngle) + templateOffset.X;
						var ty = (midt.X * sinAngle + midt.Y * cosAngle) + templateOffset.Y;
						unmirrored[x, y] +=
							MatrixUtils.Interpolate(
								template,
								tx,
								ty) / rotations;
					}
				}
			}

			if (mirror == Symmetry.Mirror.None)
				return unmirrored;

			var mirrored = new Matrix<float>(size);
			for (var y = 0; y < size.Y; y++)
			{
				for (var x = 0; x < size.X; x++)
				{
					var txy = Symmetry.MirrorGridSquare(mirror, new int2(x, y), size);
					mirrored[x, y] = unmirrored[x, y] + unmirrored[txy];
				}
			}

			return mirrored;
		}
	}
}
