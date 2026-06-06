// WPF's System.Windows.GridLengthConverter is absent from WinUI/Uno. AvalonDock's
// LayoutPositionableGroup uses it to round-trip DockWidth/DockHeight to/from the
// invariant-culture strings stored in serialized layouts ("Auto", "*", "2*",
// "100.5"). This is a faithful, self-contained port of that parse/format logic so
// serialization round-trips are byte-compatible with the WPF original.
//
// Exposed to AvalonDock source via the `GridLengthConverter` global-using alias.

using System;
using System.Globalization;

namespace AvalonDock.Compatibility
{
	public sealed class GridLengthConverter
	{
		public string ConvertToInvariantString(object value)
		{
			if (value is not GridLength gl)
				throw new ArgumentException("Expected a GridLength.", nameof(value));

			return gl.GridUnitType switch
			{
				GridUnitType.Auto => "Auto",
				GridUnitType.Star => gl.Value == 1.0
					? "*"
					: Convert.ToString(gl.Value, CultureInfo.InvariantCulture) + "*",
				_ => Convert.ToString(gl.Value, CultureInfo.InvariantCulture),
			};
		}

		public object ConvertFromInvariantString(string s)
		{
			if (s is null) throw new ArgumentNullException(nameof(s));
			var value = s.Trim();

			if (value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
				return new GridLength(1.0, GridUnitType.Auto);

			if (value.EndsWith("*", StringComparison.Ordinal))
			{
				var num = value.Substring(0, value.Length - 1).Trim();
				var stars = num.Length == 0
					? 1.0
					: Convert.ToDouble(num, CultureInfo.InvariantCulture);
				return new GridLength(stars, GridUnitType.Star);
			}

			return new GridLength(Convert.ToDouble(value, CultureInfo.InvariantCulture), GridUnitType.Pixel);
		}
	}
}
