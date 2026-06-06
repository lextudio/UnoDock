// WinUI's Microsoft.UI.Xaml.Markup.ContentPropertyAttribute requires Name= named
// argument syntax: [ContentProperty(Name = "X")]. AvalonDock uses the WPF positional
// form: [ContentProperty("X")]. We cannot inherit from the sealed WinUI attribute,
// so we define our own attribute in this assembly that:
//   1. Stores the Name so code reading it can find the content property.
//   2. Is picked up by Uno's XAML parser because Uno checks for ANY attribute named
//      "ContentPropertyAttribute" with a "Name" property — not just the WinUI one.
//
// This must be aliased in GlobalUsings.cs as the only ContentPropertyAttribute so
// that both the AvalonDock source files and the Uno XAML parser see the same attribute.

namespace AvalonDock.Compatibility
{
	[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
	public sealed class ContentPropertyAttribute : System.Attribute
	{
		public ContentPropertyAttribute() { }
		public ContentPropertyAttribute(string name) { Name = name; }
		public string Name { get; set; }
	}
}
