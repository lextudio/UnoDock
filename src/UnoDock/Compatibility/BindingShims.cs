// WPF binding types absent from WinUI/Uno.

using System;
using System.Globalization;

namespace System.Windows.Data
{
	// WPF attribute decorating converters with input/output type info.
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
	public sealed class ValueConversionAttribute : Attribute
	{
		public ValueConversionAttribute(Type sourceType, Type targetType) { }
	}

	// WPF's Binding.DoNothing sentinel — NullToDoNothingConverter returns it.
	public static class Binding
	{
		public static readonly object DoNothing = new object();
	}

	// WPF's IMultiValueConverter — used by AnchorableContextMenuHideVisibilityConverter.
	// WinUI has no multi-binding converter interface; the converter compiles and can be
	// instantiated, but XAML multi-bindings are handled differently in WinUI (Phase 4+).
	public interface IMultiValueConverter
	{
		object Convert(object[] values, Type targetType, object parameter, CultureInfo culture);
		object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture);
	}
}
