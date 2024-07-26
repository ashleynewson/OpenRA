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
		public interface IValue {}
		public interface ValueConverter<T>
		{
			T GetValue();
			void SetValue(T value);
		}

		public sealed class SectionValue : IValue {}

		public sealed class StringValue : IValue, ValueConverter<string>
		{
			public string Value;

			public StringValue(string value)
			{
				Value = value;
			}

			string ValueConverter<string>.GetValue() => Value;
			void ValueConverter<string>.SetValue(string value) => Value = value;
		}

		public sealed class IntegerValue : IValue, ValueConverter<long>, ValueConverter<int>
		{
			public long Value;

			public IntegerValue(long value)
			{
				Value = value;
			}

			long ValueConverter<long>.GetValue() => Value;
			void ValueConverter<long>.SetValue(long value) => Value = value;
			int ValueConverter<int>.GetValue()
			{
				checked
				{
					return (int)Value;
				}
			}

			void ValueConverter<int>.SetValue(int value) => Value = value;
		}

		public sealed class FloatValue : IValue, ValueConverter<double>, ValueConverter<float>
		{
			public double Value;

			public FloatValue(double value)
			{
				Value = value;
			}

			double ValueConverter<double>.GetValue() => Value;
			void ValueConverter<double>.SetValue(double value) => Value = value;
			float ValueConverter<float>.GetValue() => (float)Value;
			void ValueConverter<float>.SetValue(float value) => Value = value;
		}

		public sealed class BooleanValue : IValue, ValueConverter<bool>
		{
			public bool Value;

			public BooleanValue(bool value)
			{
				Value = value;
			}

			bool ValueConverter<bool>.GetValue() => Value;
			void ValueConverter<bool>.SetValue(bool value) => Value = value;
		}

		public sealed class EnumValue : IValue, ValueConverter<string>, ValueConverter<int>
		{
			// Maps internal names to (pre-translated) display labels.
			public readonly IReadOnlyList<KeyValuePair<string, string>> Choices;

			string _value;
			public string Value
			{
				get
				{
					return _value;
				}
				set
				{
					if (!Choices.Where(kv => kv.Key == value).Any())
						throw new ArgumentException($"Value {value} is not in a valid choice for this enum");
					_value = value;
				}
			}

			public string DisplayValue
			{
				get
				{
					return Choices.Where(kv => kv.Key == _value).First().Value;
				}
			}

			public EnumValue(IReadOnlyList<KeyValuePair<string, string>> choices, string value)
			{
				if (!choices.Any())
					throw new ArgumentException("Empty enum");
				Choices = choices;
				Value = value;
			}

			public EnumValue(IReadOnlyList<KeyValuePair<int, string>> choices, int value)
			{
				if (!choices.Any())
					throw new ArgumentException("Empty enum");
				Choices = choices.Select(kv => new KeyValuePair<string, string>(kv.Key.ToStringInvariant(), kv.Value)).ToImmutableList();
				Value = value.ToStringInvariant();
			}

			string ValueConverter<string>.GetValue() => Value;
			void ValueConverter<string>.SetValue(string value) => Value = value;
			int ValueConverter<int>.GetValue() => int.Parse(Value);
			void ValueConverter<int>.SetValue(int value) => Value = value.ToString();
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
			if (Value is ValueConverter<T> valueGetter)
			{
				return valueGetter.GetValue();
			} else {
				// Not really an argument.
				throw new ArgumentException("Value type incompatibility for getter");
			}
		}
		public void Set<T>(T value)
		{
			if (Value is ValueConverter<T> valueGetter)
			{
				valueGetter.SetValue(value);
			} else {
				// Not really an argument.
				throw new ArgumentException("Value type incompatibility for setter");
			}
		}
	}
}
