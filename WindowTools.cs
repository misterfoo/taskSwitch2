using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Forms;

namespace taskSwitch2
{
	static class WindowTools
	{
		/// <summary>
		/// Switches to the specified window.
		/// </summary>
		public static void SwitchToWindow( IntPtr window )
		{
			// passing false for this gives the behavior of Alt+Esc, which sends the current
			// foreground window to the back
			// see http://blogs.msdn.com/b/oldnewthing/archive/2011/11/07/10234436.aspx
			const bool isAltTab = true;

			// Note: this only works reliably if the uiAccess attribute is True in our manifest.
			SwitchToThisWindow( window, isAltTab );
		}

		/// <summary>
		/// Finds all application windows other than the switcher app, in Z order, using the UI
		/// Automation framework.
		/// </summary>
		public static List<AutomationElement> FindAppWindowsUIA()
		{
			var windows = new List<AutomationElement>();
			var condition = new NotCondition(
				new PropertyCondition( AutomationElement.ProcessIdProperty, Process.GetCurrentProcess().Id ) );
			foreach( AutomationElement element in
				AutomationElement.RootElement.FindAll( TreeScope.Children, condition ) )
			{
				string name = element.Current.Name;
				if( !string.IsNullOrEmpty( name ) )
					windows.Add( element );
			}
			return windows;
		}

		/// <summary>
		/// Finds all application windows in Z order, using basic native UI facilities.
		/// </summary>
		public static List<IntPtr> FindAppWindowsNative()
		{
			AppWindowEnumerator e = new AppWindowEnumerator();
			EnumWindows( e.ReadWindow, IntPtr.Zero );
			return e.Windows;
		}

		/// <summary>
		/// Gets the names of the taskbar buttons in the order they appear.
		/// </summary>
		public static List<AutomationElement> GetTaskbarButtonOrder()
		{
			AutomationElement taskList = FindExplorerTaskList();
			if( taskList == null )
				return new List<AutomationElement>();
			var buttons = new List<AutomationElement>();
			for( var child = TreeWalker.ControlViewWalker.GetFirstChild( taskList );
				child != null;
				child = TreeWalker.ControlViewWalker.GetNextSibling( child ) )
			{
				buttons.Add( child );
			}
			return buttons;
		}

		/// <summary>
		/// Gets the window text for a window.
		/// </summary>
		public static string GetWindowText( IntPtr window )
		{
			StringBuilder sb = new StringBuilder( capacity: 256 );
			GetWindowText( window, sb, sb.Capacity );
			return sb.ToString();
		}

		/// <summary>
		/// Gets the path of the executable that owns a window, or an empty string if we can't figure it out.
		/// </summary>
		public static string GetWindowProcess( IntPtr window )
		{
			uint pid;
			uint tid = GetWindowThreadProcessId( window, out pid );
			try
			{
				Process proc = Process.GetProcessById( (int)pid );
				return proc.MainModule.FileName;
			}
			catch( System.ComponentModel.Win32Exception )
			{
				return string.Empty;
			}
			catch( System.UnauthorizedAccessException )
			{
				return string.Empty;
			}
		}

		/// <summary>
		/// Determines whether a window object is still valid (i.e. not closed).
		/// </summary>
		public static bool IsWindow( TaskWindow wnd )
		{
			try
			{
				return IsWindow( wnd.WindowHandle );
			}
			catch( System.Windows.Automation.ElementNotAvailableException )
			{
				return false;
			}
		}

		/// <summary>
		/// Registers a handler to be called whenever a top-level window is created or destroyed.
		/// </summary>
		public static void HookWindowCreateDestroy( Action handler )
		{
			Automation.AddAutomationEventHandler( WindowPattern.WindowOpenedEvent,
				AutomationElement.RootElement, TreeScope.Children, (s, e) => handler() );
			Automation.AddAutomationEventHandler( WindowPattern.WindowClosedEvent,
				AutomationElement.RootElement, TreeScope.Subtree, ( s, e ) => handler() );
		}

		/// <summary>
		/// Registers a callback to be called when the foreground window changes. Returns an object
		/// which cleans up the hook registration when Dispose is called.
		/// </summary>
		public static IDisposable HookForegroundChange( Action<IntPtr> changeHandler )
		{
			WinEventDelegate hookProc = ( IntPtr winEventHook, WinEventType eventType,
				IntPtr wnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime) =>
				{
					if( eventType == WinEventType.ForegroundChange &&
						idObject == OBJID_WINDOW &&
						idChild == CHILDID_SELF )
					{
						changeHandler( wnd );
					}
				};

			HookCleanup cleanup = new HookCleanup();
			cleanup.m_callback = hookProc;
			cleanup.m_hook = SetWinEventHook( WinEventType.ForegroundChange, WinEventType.ForegroundChange,
				IntPtr.Zero, hookProc, 0, 0,
				(uint)(WinEventFlag.OutOfContext | WinEventFlag.SkipOwnProcess) );
			return cleanup;
		}

		private class HookCleanup : IDisposable
		{
			// to keep the hook callback alive
			public WinEventDelegate m_callback;

			public IntPtr m_hook;

			public void Dispose()
			{
				UnhookWinEvent( m_hook );
			}
		}

		/// <summary>
		/// Cached task list element; finding this takes significant time, so we don't want to do it
		/// on every refresh.
		/// </summary>
		private static AutomationElement s_explorerTaskList;

		private static AutomationElement FindExplorerTaskList()
		{
			if( s_explorerTaskList != null )
				return s_explorerTaskList;
			var root = AutomationElement.RootElement;
			var explorers = Process.GetProcessesByName( "explorer.exe" );
			if( explorers.Length == 0 )
				explorers = Process.GetProcessesByName( "explorer" );
			foreach( var p in explorers )
			{
				AutomationElement taskList = root.FindFirst( TreeScope.Descendants, new AndCondition(
					new PropertyCondition( AutomationElement.ProcessIdProperty, p.Id ),
					new PropertyCondition( AutomationElement.ClassNameProperty, "MSTaskListWClass" ) ) );
				if( taskList != null )
				{
					p.EnableRaisingEvents = true;
					p.Exited += (sender, args) => { s_explorerTaskList = null; };
					s_explorerTaskList = taskList;
					return taskList;
				}
			}
			return null;
		}

		/// <summary>
		/// Gets the icon for a window.
		/// </summary>
		public static Icon GetIcon( IntPtr window )
		{
			IntPtr raw;

			// Does the window have an icon?
			const int WmGetIcon = 0x007F;
			const int IconSmall = 1;
			const int IconBig = 1;
			raw = SendMessageSafe( window, WmGetIcon, new IntPtr( IconBig ), IntPtr.Zero, IntPtr.Zero );
			if( raw != IntPtr.Zero )
				return Icon.FromHandle( raw );
			raw = SendMessageSafe( window, WmGetIcon, new IntPtr( IconSmall ), IntPtr.Zero, IntPtr.Zero );
			if( raw != IntPtr.Zero )
				return Icon.FromHandle( raw );

			// Does the class have an icon?
			const int ClassIcon = -14;
			const int ClassIconSmall = -34;
			raw = GetClassLong( window, ClassIcon );
			if( raw != IntPtr.Zero )
				return Icon.FromHandle( raw );
			raw = GetClassLong( window, ClassIconSmall );
			if( raw != IntPtr.Zero )
				return Icon.FromHandle( raw );

			// does its parent have an icon?
			IntPtr parent = GetParent( window );
			if( parent != IntPtr.Zero )
			{
				Icon parentIcon = GetIcon( parent );
				if( parentIcon != null )
					return parentIcon;
			}

			return null;
		}

		/// <summary>
		/// Helper for enumerating application windows.
		/// </summary>
		private class AppWindowEnumerator
		{
			public AppWindowEnumerator()
			{
				this.Windows = new List<IntPtr>();
			}

			public List<IntPtr> Windows { get; private set; }

			public bool ReadWindow( IntPtr wnd, IntPtr lParam )
			{
				if( !IsWindowVisible( wnd ) || IsHungAppWindow( wnd ) )
					return true;
				if( IsAppWindow( wnd ) )
					this.Windows.Add( wnd );
				return true;
			}
		}

		/// <summary>
		/// Determines whether a window meets our internal definition of "app window".
		/// </summary>
		private static bool IsAppWindow( IntPtr wnd )
		{
			uint style = (uint)GetWindowLong( wnd, WindowLong.Style );
			uint exStyle = (uint)GetWindowLong( wnd, WindowLong.ExStyle );

			// always include windows with the official "app window" style
			if( 0 != (exStyle & (uint)WindowExStyle.AppWindow) )
				return true;

			// prune owned windows with visible owners
			IntPtr owner = GetWindow( wnd, GetWindowTarget.Owner );
			if( owner != IntPtr.Zero && IsWindowVisible( owner ) )
				return false;

			// prune undesirable styles
			if( 0 != (style & (uint)WindowStyle.Child) ||					// children
				0 == (style & (uint)(WindowStyle.Caption | WindowStyle.SysMenu)) || // windows with no caption or system menu
				0 != (exStyle & (uint)WindowExStyle.ToolWindow) ||			// tool windows
				0 != (exStyle & (uint)WindowExStyle.NoActivate) )			// windows that can't be activated
			{
				return false;
			}

			return true;
		}

		private static IntPtr SendMessageSafe( IntPtr window, uint msg, IntPtr wp, IntPtr lp, IntPtr failureValue )
		{
			const uint timeout = 10;
			const uint flags = 0;
			IntPtr result;
			int ok = SendMessageTimeout( window, msg, wp, lp, flags, timeout, out result );
			return (ok != 0) ? result : failureValue;
		}

		[DllImport( "user32.dll" )]
		private static extern void SwitchToThisWindow( IntPtr wnd, bool isAltTab );

		[DllImport( "user32.dll" )]
		private static extern bool SetForegroundWindow( IntPtr wnd );

		[DllImport( "user32.dll" )]
		private static extern IntPtr SetFocus( IntPtr wnd );

		[DllImport( "user32.dll" )]
		private static extern int SendMessageTimeout( IntPtr wnd, uint msg, IntPtr wp, IntPtr lp,
			uint flags, uint timeout, out IntPtr result );

		[DllImport( "user32.dll" )]
		private static extern IntPtr GetParent( IntPtr wnd );

		[DllImport( "user32.dll" )]
		private static extern IntPtr GetClassLong( IntPtr wnd, int which );

		[DllImport( "user32.dll" )]
		private static extern uint GetWindowThreadProcessId( IntPtr wnd, out uint pid );

		[DllImport( "user32.dll" )]
		private static extern bool IsWindow( IntPtr wnd );

		[DllImport( "user32.dll" )]
		static extern bool IsHungAppWindow( IntPtr wnd );

		[DllImport( "user32.dll" )]
		static extern bool IsWindowVisible( IntPtr wnd );

		[DllImport( "user32.dll" )]
		static extern int GetWindowLong( IntPtr wnd, WindowLong which );

		[DllImport( "user32.dll" )]
		static extern int GetWindowText( IntPtr wnd, StringBuilder buf, int bufSize );
		
		[DllImport( "user32.dll" )]
		static extern IntPtr GetWindow( IntPtr wnd, GetWindowTarget target );

		delegate bool EnumWindowsProc( IntPtr wnd, IntPtr lParam );

		[DllImport( "user32.dll" )]
		static extern bool EnumWindows( EnumWindowsProc enumProc, IntPtr lParam );

		enum WindowLong : int
		{
			Style = -16,
			ExStyle = -20
		}

		enum GetWindowTarget
		{
			Owner = 4
		}

		[Flags]
		enum WindowStyle
		{
			Child = 0x40000000,
			Border = 0x00800000,
			DlgFrame = 0x00400000,
			Caption = Border | DlgFrame,
			SysMenu = 0x00080000,
		}

		[Flags]
		enum WindowExStyle
		{
			AppWindow = 0x00040000,
			ToolWindow = 0x00000080,
			NoActivate = 0x08000000,
		}

		[DllImport( "user32.dll" )]
		private static extern IntPtr SetWinEventHook( WinEventType eventMin, WinEventType eventMax, IntPtr
		   hmodWinEventProc, WinEventDelegate winEventProc, uint idProcess,
		   uint idThread, uint dwFlags );

		[DllImport( "user32.dll" )]
		private static extern bool UnhookWinEvent( IntPtr winEventHook );

		private delegate void WinEventDelegate( IntPtr winEventHook, WinEventType eventType,
			IntPtr wnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime );

		private enum WinEventFlag : uint
		{
			OutOfContext = 0x0000, // Events are ASYNC
			SkipOwnProcess = 0x0002, // Don't call back for events on installer's process
		}

		private enum WinEventType : uint
		{
			ForegroundChange = 0x0003
		}

		private const uint OBJID_WINDOW = 0;
		private const uint CHILDID_SELF = 0;
	}
}
