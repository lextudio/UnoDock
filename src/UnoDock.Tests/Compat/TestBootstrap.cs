// Module initializer: runs before any test code, bootstrapping Uno's Skia
// dispatcher so DependencyObject-derived model types can be instantiated in
// headless tests. NativeDispatcher is internal, NativeDispatcherPriority is an
// internal enum — we build the Action<Action, priority> delegate dynamically via
// System.Reflection.Emit so the parameter types match exactly.

using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UnoDock.Tests
{
	internal static class TestBootstrap
	{
		[ModuleInitializer]
		internal static void Init()
		{
			var mainThread = Thread.CurrentThread;

			var dispatchingAsm = Assembly.Load("Uno.UI.Dispatching");
			var dispatcherType = dispatchingAsm.GetType(
				"Uno.UI.Dispatching.NativeDispatcher", throwOnError: true);

			// Set HasThreadAccessOverride: Func<bool>
			var hasAccessField = dispatcherType.GetField("HasThreadAccessOverride",
				BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
			hasAccessField?.SetValue(null, (Func<bool>)(() => Thread.CurrentThread == mainThread));

			// Set DispatchOverride: Action<Action, NativeDispatcherPriority>
			// NativeDispatcherPriority is an internal enum — build a matching delegate
			// via DynamicMethod that simply invokes the Action and ignores the priority.
			var dispatchField = dispatcherType.GetField("DispatchOverride",
				BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
			if (dispatchField != null)
			{
				var priorityType = dispatchingAsm.GetType(
					"Uno.UI.Dispatching.NativeDispatcherPriority", throwOnError: true);
				var delegateType = typeof(Action<,>).MakeGenericType(typeof(Action), priorityType);

				// DynamicMethod: void (Action action, NativeDispatcherPriority _priority) => action()
				var dm = new DynamicMethod(
					"_UnoDispatchShim",
					typeof(void),
					new[] { typeof(Action), priorityType },
					typeof(TestBootstrap).Module,
					skipVisibility: true);
				var il = dm.GetILGenerator();
				il.Emit(OpCodes.Ldarg_0);                                 // action
				il.EmitCall(OpCodes.Callvirt, typeof(Action).GetMethod("Invoke")!, null);
				il.Emit(OpCodes.Ret);

				var del = dm.CreateDelegate(delegateType);
				dispatchField.SetValue(null, del);
			}
		}
	}
}
