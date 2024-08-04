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
using System.Text.RegularExpressions;

namespace OpenRA.Mods.Common.Terrain
{
	/// <summary>
	/// Information about how certain templates (like cliffs, beaches, roads) link together.
	/// </summary>
	public class TemplateSegment
	{
		public readonly string Start;
		public readonly string End;
		[FieldLoader.Ignore]
		public readonly int2[] Points;

		public TemplateSegment(MiniYaml my)
		{
			FieldLoader.Load(this, my);
			{
				// Ideally, shall we change the FieldLoader.ParseInt2Array (and similar) to ignore whitespace?
				// It should ultimately be better than this.
				var value = my.NodeWithKey("Points").Value.Value;
				var parts = Regex.Replace(value, @"\s+", string.Empty)
					.Split(',', StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length % 2 != 0)
					FieldLoader.InvalidValueAction(value, typeof(int2[]), "Points");
				Points = new int2[parts.Length / 2];
				for (var i = 0; i < Points.Length; i++)
					Points[i] = new int2(Exts.ParseInt32Invariant(parts[2 * i]), Exts.ParseInt32Invariant(parts[2 * i + 1]));
			}

		}

		public static bool MatchesType(string type, string matcher)
		{
			if (type == matcher)
			{
				return true;
			}
			return type.StartsWith($"{matcher}.", StringComparison.InvariantCulture);
		}

		public bool HasType(string matcher)
			=> MatchesType(Start, matcher) || MatchesType(End, matcher);
	}
}
