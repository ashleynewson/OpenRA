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
using System.Linq;

namespace OpenRA
{
	public class MapGeneratorSetting
	{
		public interface IValue { }
		public interface IValueConverter<T>
		{
			T GetValue();
			void SetValue(T value);
		}

		public sealed class SectionValue : IValue { }

		public sealed class StringValue : IValue, IValueConverter<string>
		{
			public string Value;

			public StringValue(string value)
			{
				Value = value;
			}

			string IValueConverter<string>.GetValue() => Value;
			void IValueConverter<string>.SetValue(string value) => Value = value;
		}

		public sealed class IntegerValue : IValue, IValueConverter<long>, IValueConverter<int>
		{
			public long Value;

			public IntegerValue(long value)
			{
				Value = value;
			}

			long IValueConverter<long>.GetValue() => Value;
			void IValueConverter<long>.SetValue(long value) => Value = value;
			int IValueConverter<int>.GetValue()
			{
				checked
				{
					return (int)Value;
				}
			}

			void IValueConverter<int>.SetValue(int value) => Value = value;
		}

		public sealed class FloatValue : IValue, IValueConverter<double>, IValueConverter<float>
		{
			public double Value;

			public FloatValue(double value)
			{
				Value = value;
			}

			double IValueConverter<double>.GetValue() => Value;
			void IValueConverter<double>.SetValue(double value) => Value = value;
			float IValueConverter<float>.GetValue() => (float)Value;
			void IValueConverter<float>.SetValue(float value) => Value = value;
		}

		public sealed class BooleanValue : IValue, IValueConverter<bool>
		{
			public bool Value;

			public BooleanValue(bool value)
			{
				Value = value;
			}

			bool IValueConverter<bool>.GetValue() => Value;
			void IValueConverter<bool>.SetValue(bool value) => Value = value;
		}

		public sealed class EnumValue : IValue, IValueConverter<string>, IValueConverter<int>
		{
			// Maps internal names to (pre-translated) display labels.
			public readonly IReadOnlyList<KeyValuePair<string, string>> Choices;

			string value;
			public string Value
			{
				get => value;
				set
				{
					if (!Choices.Any(kv => kv.Key == value))
						throw new ArgumentException($"Value {value} is not in a valid choice for this enum");
					this.value = value;
				}
			}

			public string DisplayValue
			{
				get
				{
					return Choices.First(kv => kv.Key == value).Value;
				}
			}

			public EnumValue(IReadOnlyList<KeyValuePair<string, string>> choices, string value)
			{
				if (choices.Count == 0)
					throw new ArgumentException("Empty enum");
				Choices = choices;
				Value = value;
			}

			public EnumValue(IReadOnlyList<KeyValuePair<int, string>> choices, int value)
			{
				if (choices.Count == 0)
					throw new ArgumentException("Empty enum");
				Choices = choices.Select(kv => new KeyValuePair<string, string>(kv.Key.ToStringInvariant(), kv.Value)).ToImmutableList();
				Value = value.ToStringInvariant();
			}

			string IValueConverter<string>.GetValue() => Value;
			void IValueConverter<string>.SetValue(string value) => Value = value;
			int IValueConverter<int>.GetValue() => Exts.ParseInt32Invariant(Value);
			void IValueConverter<int>.SetValue(int value) => Value = value.ToStringInvariant();
		}

		public readonly string Name;
		public readonly string Label;
		public readonly IValue Value;

		public MapGeneratorSetting(string name, string label, IValue value)
		{
			Name = name;
			Label = label;
			Value = value;
		}

		public T Get<T>()
		{
			if (Value is IValueConverter<T> valueGetter)
			{
				return valueGetter.GetValue();
			}
			else
			{
				// Not really an argument.
				throw new ArgumentException("Value type incompatibility for getter");
			}
		}

		public void Set<T>(T value)
		{
			if (Value is IValueConverter<T> valueGetter)
			{
				valueGetter.SetValue(value);
			}
			else
			{
				// Not really an argument.
				throw new ArgumentException("Value type incompatibility for setter");
			}
		}
	}
}
