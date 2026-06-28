// Real LayoutItem / LayoutAnchorableItem / LayoutDocumentItem.
// Replaces Phase1ControlStubs. Wires ActivateCommand, HideCommand, AutoHideCommand
// so context-menu buttons and converters work correctly.

using System.Windows.Input;
using AvalonDock.Layout;
using Microsoft.UI.Xaml;

namespace AvalonDock.Controls
{
	public class LayoutItem : FrameworkElement
	{
		internal LayoutItem(LayoutContent model, DockingManager manager)
		{
			Model   = model;
			Manager = manager;

			ActivateCommand = new RelayCommand(() =>
			{
				model.IsActive  = true;
				model.IsSelected = true;
			});

			CloseCommand = new RelayCommand(() =>
			{
				manager.ExecuteCloseCommand(model);
			}, () => model.CanClose);
		}

		public LayoutContent   Model   { get; }
		public DockingManager  Manager { get; }
		public UIElement       View    { get; internal set; }

		public ICommand ActivateCommand { get; }
		public ICommand CloseCommand    { get; }
	}

	public class LayoutAnchorableItem : LayoutItem
	{
		internal LayoutAnchorableItem(LayoutAnchorable model, DockingManager manager)
			: base(model, manager)
		{
			var anc = model;
			HideCommand     = new RelayCommand(() => manager.ExecuteHideCommand(anc),
			                                   () => anc.CanHide);
			AutoHideCommand = new RelayCommand(() => anc.ToggleAutoHide(),
			                                   () => anc.CanAutoHide);
		}

		public ICommand HideCommand     { get; }
		public ICommand AutoHideCommand { get; }
	}

	public class LayoutDocumentItem : LayoutItem
	{
		internal LayoutDocumentItem(LayoutDocument model, DockingManager manager)
			: base(model, manager) { }
	}

	// Minimal ICommand that wraps an Action + optional CanExecute predicate.
	internal sealed class RelayCommand : ICommand
	{
		private readonly System.Action _execute;
		private readonly System.Func<bool> _canExecute;

		internal RelayCommand(System.Action execute, System.Func<bool> canExecute = null)
		{
			_execute    = execute;
			_canExecute = canExecute;
		}

		public event System.EventHandler CanExecuteChanged;
		public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
		public void Execute(object parameter)    => _execute();
		public void RaiseCanExecuteChanged()     => CanExecuteChanged?.Invoke(this, System.EventArgs.Empty);
	}
}
