// Resource key constants for UnoDock VS2013 themes.
// WinUI / Uno does not have WPF's ComponentResourceKey; plain string constants
// are used instead.  XAML files reference them via {DynamicResource key}.

namespace UnoDock.Themes.VS2013.Themes
{
	/// <summary>Resource key constants shared by all VS2013 theme variants.</summary>
	public static class ResourceKeys
	{
		// ── General ───────────────────────────────────────────────────────────
		public const string ControlAccentBrush          = "UnoDock_VS2013_ControlAccentBrush";

		/// <summary>Main dock-manager background.</summary>
		public const string Background                   = "UnoDock_VS2013_Background";

		/// <summary>Border color around pane/panel areas.</summary>
		public const string PanelBorderBrush             = "UnoDock_VS2013_PanelBorderBrush";

		// ── Tab bar ───────────────────────────────────────────────────────────
		/// <summary>Background of the tab-strip bar.</summary>
		public const string TabBarBackground             = "UnoDock_VS2013_TabBarBackground";

		/// <summary>Bottom border of the tab-strip bar.</summary>
		public const string TabBarBorderBrush            = "UnoDock_VS2013_TabBarBorderBrush";

		/// <summary>Border / separator color between individual tabs.</summary>
		public const string TabBorderBrush               = "UnoDock_VS2013_TabBorderBrush";

		/// <summary>Default (unselected) tab text color.</summary>
		public const string TabText                      = "UnoDock_VS2013_TabText";

		/// <summary>Content-area background (below the tab strip).</summary>
		public const string ContentBackground            = "UnoDock_VS2013_ContentBackground";

		/// <summary>Grid-splitter (resizer) handle color.</summary>
		public const string ResizerBackground            = "UnoDock_VS2013_ResizerBackground";

		/// <summary>Close-button glyph color in the normal state.</summary>
		public const string CloseButtonForeground        = "UnoDock_VS2013_CloseButtonForeground";

		// ── Context / flyout menus ───────────────────────────────────────────
		public const string MenuBackground          = "UnoDock_VS2013_MenuBackground";
		public const string MenuBorder              = "UnoDock_VS2013_MenuBorder";
		public const string MenuSeparator           = "UnoDock_VS2013_MenuSeparator";
		public const string MenuText                = "UnoDock_VS2013_MenuText";
		public const string MenuTextInactive        = "UnoDock_VS2013_MenuTextInactive";
		public const string MenuItemHoveredBackground = "UnoDock_VS2013_MenuItemHoveredBackground";
		public const string MenuItemHoveredText     = "UnoDock_VS2013_MenuItemHoveredText";

		// ── Document well : overflow button ──────────────────────────────────
		public const string DocumentWellOverflowButtonDefaultGlyph      = "UnoDock_VS2013_DocumentWellOverflowButtonDefaultGlyph";
		public const string DocumentWellOverflowButtonHoveredBackground  = "UnoDock_VS2013_DocumentWellOverflowButtonHoveredBackground";
		public const string DocumentWellOverflowButtonHoveredBorder      = "UnoDock_VS2013_DocumentWellOverflowButtonHoveredBorder";
		public const string DocumentWellOverflowButtonHoveredGlyph       = "UnoDock_VS2013_DocumentWellOverflowButtonHoveredGlyph";
		public const string DocumentWellOverflowButtonPressedBackground  = "UnoDock_VS2013_DocumentWellOverflowButtonPressedBackground";
		public const string DocumentWellOverflowButtonPressedBorder      = "UnoDock_VS2013_DocumentWellOverflowButtonPressedBorder";
		public const string DocumentWellOverflowButtonPressedGlyph       = "UnoDock_VS2013_DocumentWellOverflowButtonPressedGlyph";

		// ── Active tab accent ─────────────────────────────────────────────────
		/// <summary>2 px accent line under the active document tab.</summary>
		public const string DocumentWellTabSelectedActiveBackground   = "UnoDock_VS2013_DocumentWellTabSelectedActiveBackground";
		public const string DocumentWellTabSelectedActiveText         = "UnoDock_VS2013_DocumentWellTabSelectedActiveText";
		public const string DocumentWellTabSelectedInactiveBackground = "UnoDock_VS2013_DocumentWellTabSelectedInactiveBackground";
		public const string DocumentWellTabSelectedInactiveText       = "UnoDock_VS2013_DocumentWellTabSelectedInactiveText";
		public const string DocumentWellTabUnselectedBackground       = "UnoDock_VS2013_DocumentWellTabUnselectedBackground";
		public const string DocumentWellTabUnselectedText             = "UnoDock_VS2013_DocumentWellTabUnselectedText";
		public const string DocumentWellTabUnselectedHoveredBackground = "UnoDock_VS2013_DocumentWellTabUnselectedHoveredBackground";
		public const string DocumentWellTabUnselectedHoveredText       = "UnoDock_VS2013_DocumentWellTabUnselectedHoveredText";

		public const string DocumentWellTabButtonSelectedActiveGlyph = "UnoDock_VS2013_DocumentWellTabButtonSelectedActiveGlyph";
		public const string DocumentWellTabButtonSelectedInactiveGlyph = "UnoDock_VS2013_DocumentWellTabButtonSelectedInactiveGlyph";
		public const string DocumentWellTabButtonUnselectedTabHoveredGlyph = "UnoDock_VS2013_DocumentWellTabButtonUnselectedTabHoveredGlyph";

		public const string AutoHideTabDefaultBackground = "UnoDock_VS2013_AutoHideTabDefaultBackground";
		public const string AutoHideTabDefaultBorder = "UnoDock_VS2013_AutoHideTabDefaultBorder";
		public const string AutoHideTabDefaultText = "UnoDock_VS2013_AutoHideTabDefaultText";
		public const string AutoHideTabHoveredBackground = "UnoDock_VS2013_AutoHideTabHoveredBackground";
		public const string AutoHideTabHoveredBorder = "UnoDock_VS2013_AutoHideTabHoveredBorder";
		public const string AutoHideTabHoveredText = "UnoDock_VS2013_AutoHideTabHoveredText";

		public const string ToolWindowCaptionActiveBackground = "UnoDock_VS2013_ToolWindowCaptionActiveBackground";
		public const string ToolWindowCaptionActiveText = "UnoDock_VS2013_ToolWindowCaptionActiveText";
		public const string ToolWindowCaptionInactiveBackground = "UnoDock_VS2013_ToolWindowCaptionInactiveBackground";
		public const string ToolWindowCaptionInactiveText = "UnoDock_VS2013_ToolWindowCaptionInactiveText";

		public const string ToolWindowCaptionButtonActiveGlyph = "UnoDock_VS2013_ToolWindowCaptionButtonActiveGlyph";
		public const string ToolWindowCaptionButtonActiveHoveredBackground = "UnoDock_VS2013_ToolWindowCaptionButtonActiveHoveredBackground";
		public const string ToolWindowCaptionButtonActiveHoveredBorder = "UnoDock_VS2013_ToolWindowCaptionButtonActiveHoveredBorder";
		public const string ToolWindowCaptionButtonActiveHoveredGlyph = "UnoDock_VS2013_ToolWindowCaptionButtonActiveHoveredGlyph";
		public const string ToolWindowCaptionButtonActivePressedBackground = "UnoDock_VS2013_ToolWindowCaptionButtonActivePressedBackground";
		public const string ToolWindowCaptionButtonActivePressedBorder = "UnoDock_VS2013_ToolWindowCaptionButtonActivePressedBorder";
		public const string ToolWindowCaptionButtonActivePressedGlyph = "UnoDock_VS2013_ToolWindowCaptionButtonActivePressedGlyph";

		public const string ToolWindowCaptionActiveGrip   = "UnoDock_VS2013_ToolWindowCaptionActiveGrip";
		public const string ToolWindowCaptionInactiveGrip = "UnoDock_VS2013_ToolWindowCaptionInactiveGrip";

		public const string ToolWindowCaptionButtonInactiveGlyph = "UnoDock_VS2013_ToolWindowCaptionButtonInactiveGlyph";
		public const string ToolWindowCaptionButtonInactiveHoveredBackground = "UnoDock_VS2013_ToolWindowCaptionButtonInactiveHoveredBackground";
		public const string ToolWindowCaptionButtonInactiveHoveredBorder = "UnoDock_VS2013_ToolWindowCaptionButtonInactiveHoveredBorder";
		public const string ToolWindowCaptionButtonInactiveHoveredGlyph = "UnoDock_VS2013_ToolWindowCaptionButtonInactiveHoveredGlyph";
		public const string ToolWindowCaptionButtonInactivePressedBackground = "UnoDock_VS2013_ToolWindowCaptionButtonInactivePressedBackground";
		public const string ToolWindowCaptionButtonInactivePressedBorder = "UnoDock_VS2013_ToolWindowCaptionButtonInactivePressedBorder";
		public const string ToolWindowCaptionButtonInactivePressedGlyph = "UnoDock_VS2013_ToolWindowCaptionButtonInactivePressedGlyph";

		public const string ToolWindowTabSelectedActiveBackground = "UnoDock_VS2013_ToolWindowTabSelectedActiveBackground";
		public const string ToolWindowTabSelectedActiveText = "UnoDock_VS2013_ToolWindowTabSelectedActiveText";
		public const string ToolWindowTabSelectedInactiveBackground = "UnoDock_VS2013_ToolWindowTabSelectedInactiveBackground";
		public const string ToolWindowTabSelectedInactiveText = "UnoDock_VS2013_ToolWindowTabSelectedInactiveText";
		public const string ToolWindowTabUnselectedBackground = "UnoDock_VS2013_ToolWindowTabUnselectedBackground";
		public const string ToolWindowTabUnselectedText = "UnoDock_VS2013_ToolWindowTabUnselectedText";
		public const string ToolWindowTabUnselectedHoveredBackground = "UnoDock_VS2013_ToolWindowTabUnselectedHoveredBackground";
		public const string ToolWindowTabUnselectedHoveredText = "UnoDock_VS2013_ToolWindowTabUnselectedHoveredText";

		// ── Dock indicator / drop-zone (OverlayButtons.xaml equivalents) ─────
		public const string DockingButtonBackgroundBrush      = "UnoDock_VS2013_DockingButtonBackgroundBrush";
		public const string DockingButtonForegroundBrush      = "UnoDock_VS2013_DockingButtonForegroundBrush";
		public const string DockingButtonForegroundArrowBrush = "UnoDock_VS2013_DockingButtonForegroundArrowBrush";
		public const string DockingButtonStarBorderBrush      = "UnoDock_VS2013_DockingButtonStarBorderBrush";
		public const string DockingButtonStarBackgroundBrush  = "UnoDock_VS2013_DockingButtonStarBackgroundBrush";
		public const string PreviewBoxBorderBrush             = "UnoDock_VS2013_PreviewBoxBorderBrush";
		public const string PreviewBoxBackgroundBrush         = "UnoDock_VS2013_PreviewBoxBackgroundBrush";

		// Template + sizing keys (mirrors OverlayButtons.xaml concept in AvalonDock)
		public const string DockingButtonWidth  = "UnoDock_VS2013_DockingButtonWidth";
		public const string DockingButtonHeight = "UnoDock_VS2013_DockingButtonHeight";

		public const string DockAnchorableLeftTemplate   = "UnoDock_VS2013_DockAnchorableLeftTemplate";
		public const string DockAnchorableRightTemplate  = "UnoDock_VS2013_DockAnchorableRightTemplate";
		public const string DockAnchorableTopTemplate    = "UnoDock_VS2013_DockAnchorableTopTemplate";
		public const string DockAnchorableBottomTemplate = "UnoDock_VS2013_DockAnchorableBottomTemplate";
		public const string DockAnchorableInsideTemplate = "UnoDock_VS2013_DockAnchorableInsideTemplate";

		public const string DockDocumentLeftTemplate     = "UnoDock_VS2013_DockDocumentLeftTemplate";
		public const string DockDocumentRightTemplate    = "UnoDock_VS2013_DockDocumentRightTemplate";
		public const string DockDocumentTopTemplate      = "UnoDock_VS2013_DockDocumentTopTemplate";
		public const string DockDocumentBottomTemplate   = "UnoDock_VS2013_DockDocumentBottomTemplate";
		public const string DockDocumentInsideTemplate   = "UnoDock_VS2013_DockDocumentInsideTemplate";

		public const string DockDocumentAsAnchorableLeftTemplate   = "UnoDock_VS2013_DockDocumentAsAnchorableLeftTemplate";
		public const string DockDocumentAsAnchorableRightTemplate  = "UnoDock_VS2013_DockDocumentAsAnchorableRightTemplate";
		public const string DockDocumentAsAnchorableTopTemplate    = "UnoDock_VS2013_DockDocumentAsAnchorableTopTemplate";
		public const string DockDocumentAsAnchorableBottomTemplate = "UnoDock_VS2013_DockDocumentAsAnchorableBottomTemplate";
	}
}
