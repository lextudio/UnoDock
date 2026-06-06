extern alias winshims;

// AvalonDock relies on ImplicitUsings (disabled here to avoid Uno's WinUI globals
// colliding with WindowsShims' System.Windows.* surface). Re-add the one global
// namespace its Layout source assumes beyond per-file usings.
global using System.Linq;

// WPF → Uno/WinUI type aliases for AvalonDock source files.
// Mirrors the proven UnoEdit/WpfTypeAliases.cs approach: AvalonDock files that
// reference these names (after `using System.Windows;`) compile against Uno
// without modification, as long as no WindowsShims type in System.Windows.*
// also defines the same name (which would cause CS0104 ambiguity).

// ── Core property system (WinUI-native) ──────────────────────────────────────
global using DependencyObject                   = Microsoft.UI.Xaml.DependencyObject;
global using DependencyProperty                 = Microsoft.UI.Xaml.DependencyProperty;
// DependencyPropertyChangedEventArgs: pin to the WindowsShims WPF struct (not the
// WinUI type) so model callbacks like OnTitlePropertyChanged match the
// System.Windows.PropertyChangedCallback delegate UIPropertyMetadata expects. The
// shim struct carries an implicit conversion from the WinUI args, so the bridge
// from Uno's property-change path still works.
global using DependencyPropertyChangedEventArgs = winshims::System.Windows.DependencyPropertyChangedEventArgs;
global using PropertyMetadata                   = Microsoft.UI.Xaml.PropertyMetadata;
global using PropertyChangedCallback            = Microsoft.UI.Xaml.PropertyChangedCallback;
global using UIElement                          = Microsoft.UI.Xaml.UIElement;
global using FrameworkElement                   = Microsoft.UI.Xaml.FrameworkElement;

// ── Geometry / layout primitives ─────────────────────────────────────────────
global using Rect                               = Windows.Foundation.Rect;
global using Size                               = Windows.Foundation.Size;
global using Point                              = Windows.Foundation.Point;
global using Thickness                          = Microsoft.UI.Xaml.Thickness;
global using GridLength                         = Microsoft.UI.Xaml.GridLength;
global using GridUnitType                       = Microsoft.UI.Xaml.GridUnitType;
// Orientation: the model both fully-qualifies System.Windows.Controls.Orientation
// and uses it unqualified, so both must be the SAME type. Pin to the WPF enum
// (WindowsShims); the control/converter layer maps it to WinUI's in Phase 3.
global using Orientation                        = System.Windows.Controls.Orientation;
global using FlowDirection                      = Microsoft.UI.Xaml.FlowDirection;

// ── Media ────────────────────────────────────────────────────────────────────
global using Brush                              = Microsoft.UI.Xaml.Media.Brush;
global using SolidColorBrush                    = Microsoft.UI.Xaml.Media.SolidColorBrush;
global using Color                              = Windows.UI.Color;

// ── ContentPropertyAttribute: pin to WinUI's version so Uno's XAML source generator
//    recognises [ContentProperty(Name = "X")] on AvalonDock model types. The Layout
//    model files that used the WPF positional form [ContentProperty("X")] have been
//    forked (src/UnoDock/Layout/) with Name= named-arg syntax. ──
global using ContentPropertyAttribute           = Microsoft.UI.Xaml.Markup.ContentPropertyAttribute;

// ── GridLengthConverter: WPF has it, WinUI/Uno does not. Use our faithful port. ──
global using GridLengthConverter                = AvalonDock.Compatibility.GridLengthConverter;

// ── Control / input types used by the control + converter layer ───────────────
global using ICommand                           = System.Windows.Input.ICommand;
global using Visibility                         = Microsoft.UI.Xaml.Visibility;
global using Canvas                             = Microsoft.UI.Xaml.Controls.Canvas;
global using Image                              = Microsoft.UI.Xaml.Controls.Image;
global using BitmapImage                        = Microsoft.UI.Xaml.Media.Imaging.BitmapImage;
global using RotateTransform                    = Microsoft.UI.Xaml.Media.RotateTransform;
global using Thumb                              = Microsoft.UI.Xaml.Controls.Primitives.Thumb;
global using HorizontalAlignment                = Microsoft.UI.Xaml.HorizontalAlignment;
global using VerticalAlignment                  = Microsoft.UI.Xaml.VerticalAlignment;
global using SelectionChangedEventArgs          = Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs;
