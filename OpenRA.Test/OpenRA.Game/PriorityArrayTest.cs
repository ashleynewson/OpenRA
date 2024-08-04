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

using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class PriorityArrayTest
	{
		[TestCase(0)]
		[TestCase(5)]
		[TestCase(int.MinValue)]
		[TestCase(int.MaxValue)]
		public void PriorityArraySequentialTest(int initialValue)
		{
			var input = new KeyValuePair<int, int>[]{
				new KeyValuePair<int, int>(0, 1),
				new KeyValuePair<int, int>(1, 5),
				new KeyValuePair<int, int>(2, 3),
				new KeyValuePair<int, int>(3, 2),
				new KeyValuePair<int, int>(4, 8),
				new KeyValuePair<int, int>(5, 7),
				new KeyValuePair<int, int>(6, 4),
				new KeyValuePair<int, int>(7, 6)
			};
			var expected = new KeyValuePair<int, int>[]{
				new KeyValuePair<int, int>(0, 1),
				new KeyValuePair<int, int>(3, 2),
				new KeyValuePair<int, int>(2, 3),
				new KeyValuePair<int, int>(6, 4),
				new KeyValuePair<int, int>(1, 5),
				new KeyValuePair<int, int>(7, 6),
				new KeyValuePair<int, int>(5, 7),
				new KeyValuePair<int, int>(4, 8)
			};

			var pa = new PriorityArray<int>(8, initialValue);

			foreach (var kv in input)
			{
				pa[kv.Key] = kv.Value;
			}

			var readback = new KeyValuePair<int, int>[8];
			for (var i = 0; i < 8; i++)
			{
				var index = pa.GetMinIndex();
				readback[i] = new KeyValuePair<int, int>(index, pa[index]);
				pa[index] = int.MaxValue;
			}

			Assert.That(readback, Is.EquivalentTo(expected));
		}
	}
}
