using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using taskSwitch2.Properties;

namespace taskSwitch2
{
	static class WindowTools
	{
		public enum ShellHookEvent
		{
			WindowCreated = 1,
			WindowDestroyed = 2,
			WindowActivated = 4,
			RudeWindowActivated = 0x8000 | WindowActivated,
		}

		/// <summary>
		/// Discards all internal caches for this class.
		/// </summary>
		public static void ClearCaches()
		{
			s_pidToPath.Clear();
		}

		/// <summary>
		/// Registers a shell hook window.
		/// </summary>
		public static void RegisterShellHook( IntPtr window, out int windowMsg )
		{
			windowMsg = RegisterWindowMessage( "SHELLHOOK" );
			bool ok = RegisterShellHookWindow( window );
			if( !ok )
				throw new System.InvalidOperationException( "Unable to register shell hook window: " + Marshal.GetLastWin32Error() );
		}

		/// <summary>
		/// Unregisters a shell hook window.
		/// </summary>
		public static void UnregisterShellHook( IntPtr window )
		{
			DeregisterShellHookWindow( window );
		}

		/// <summary>
		/// Switches to the specified window.
		/// </summary>
		public static void SwitchToWindow( IntPtr window )
		{
			// Note: This only works reliably if the uiAccess attribute is True in our manifest,
			// AND if this application is installed in Program Files. See this page for notes:
			// https://docs.microsoft.com/en-us/windows/win32/winauto/uiauto-securityoverview
			if( !SetForegroundWindow( window ) )
				DebugEvent.Record( "SetForegroundWindow failed: {0}", Marshal.GetLastWin32Error() );

			if( IsIconic( window ) )
				ShowWindow( window, 9 );
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
		public static List<IntPtr> FindAppWindowsNative( int maxCount = 0 )
		{
			AppWindowEnumerator e = new AppWindowEnumerator( maxCount );
			EnumWindows( e.ReadWindow, IntPtr.Zero );
			return e.Windows;
		}

		/// <summary>
		/// Gets the list of taskbar buttons in the order they appear. This returns cached data
		/// unless InvalidateWindowInfoCaches has been called.
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
		/// Gets the path of the executable that owns a window, or an empty string if we can't
		/// figure it out. This returns cached info unless InvalidateWindowInfoCaches has been called.
		/// </summary>
		public static string GetWindowProcess( IntPtr window, ConcurrentDictionary<IntPtr, string> cache )
		{
			return cache.GetOrAdd( window, ReallyGetWindowProcess );
		}

		private static readonly ConcurrentDictionary<uint, string> s_pidToPath = new ConcurrentDictionary<uint, string>();

		private static string ReallyGetWindowProcess( IntPtr window )
		{
			uint pid;
			GetWindowThreadProcessId( window, out pid );
			return s_pidToPath.GetOrAdd( pid, p => GetProcessExeUncached( p ) );
		}

		private static string GetProcessExeUncached( uint pid )
		{
			try
			{
				Process proc;
				using( Utilities.TimedBlock( "Process.GetProcessById" ) )
					proc = Process.GetProcessById( (int)pid );
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
			catch( System.InvalidOperationException )
			{
				return string.Empty;
			}
			catch( System.ArgumentException )
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
				string cmdLine = GetCommandLine( p );
				if( cmdLine != null && cmdLine.Contains( "/factory" ) )
					continue;

				AutomationElement taskList = root.FindFirst( TreeScope.Descendants, new AndCondition(
					new PropertyCondition( AutomationElement.ProcessIdProperty, p.Id ),
					new PropertyCondition( AutomationElement.ClassNameProperty, "MSTaskListWClass" ) ) );
				if( taskList != null )
				{
					p.EnableRaisingEvents = true;
					p.Exited += ( sender, args ) => { s_explorerTaskList = null; };
					s_explorerTaskList = taskList;
					return taskList;
				}
			}
			return null;
		}

		private static string GetCommandLine( Process process )
		{
			using( ManagementObjectSearcher searcher = new ManagementObjectSearcher( "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + process.Id ) )
			using( ManagementObjectCollection objects = searcher.Get() )
			{
				return objects.Cast<ManagementBaseObject>().SingleOrDefault()?["CommandLine"]?.ToString();
			}
		}

		/// <summary>
		/// Gets the icon for a window.
		/// </summary>
		public static IDisposable GetIcon( IntPtr window )
		{
			string text = GetWindowText( window );
			if( text.Contains( "Snip" ) )
				Debug.Write( "foo" );

			// get an icon directly from the window if we can
			IDisposable icon = GetWindowIcon( window ) ?? GetClassIcon( window );
			if( icon != null )
				return icon;

			// does its parent have an icon?
			IntPtr parent = GetParent( window );
			if( parent != IntPtr.Zero )
			{
				icon = GetIcon( parent );
				if( icon != null )
					return icon;
			}

			// try finding it Windows App style
			icon = GetWindowsAppIconCached( window );
			if( icon != null )
				return icon;

			// use a fallback icon
			return Resources.GenericApp;
		}

		private static IDisposable GetWindowIcon( IntPtr window )
		{
			const int WmGetIcon = 0x007F;
			const int IconSmall = 1;
			const int IconBig = 1;
			IntPtr raw;
			raw = SendMessageSafe( window, WmGetIcon, new IntPtr( IconSmall ), IntPtr.Zero, IntPtr.Zero );
			if( raw != IntPtr.Zero )
				return Icon.FromHandle( raw );
			raw = SendMessageSafe( window, WmGetIcon, new IntPtr( IconBig ), IntPtr.Zero, IntPtr.Zero );
			if( raw != IntPtr.Zero )
				return Icon.FromHandle( raw );
			return null;
		}

		private static IDisposable GetClassIcon( IntPtr window )
		{
			const int ClassIcon = -14;
			const int ClassIconSmall = -34;
			IntPtr raw;
			raw = GetClassLong( window, ClassIconSmall );
			if( raw != IntPtr.Zero )
				return Icon.FromHandle( raw );
			raw = GetClassLong( window, ClassIcon );
			if( raw != IntPtr.Zero )
				return Icon.FromHandle( raw );
			return null;
		}

		private static IDisposable GetWindowsAppIconCached( IntPtr window )
		{
			// Find the application for this window
			string exe = ReallyGetWindowProcess( window );
			if( string.IsNullOrEmpty( exe ) )
				return null;

			// ApplicationFrameHost.exe is not a real application, but one of its child windows should be
			// from the real app.
			if( Regex.IsMatch( exe, "ApplicationFrameHost" ) )
			{
				window = GetWindow( window, GetWindowTarget.FirstChild );
				while( window != IntPtr.Zero && Regex.IsMatch( ReallyGetWindowProcess( window ), "ApplicationFrameHost" ) )
					window = GetWindow( window, GetWindowTarget.Next );
				if( window == IntPtr.Zero )
					return null;
				exe = ReallyGetWindowProcess( window );
			}

			// Do we have this exe's icon (including possible lack of one) cached?
			IDisposable cached;
			if( m_windowsAppIcon.TryGetValue( exe, out cached ) )
				return cached;

			// Get the App User Model ID for this thing
			uint pid;
			GetWindowThreadProcessId( window, out pid );
			string package = GetApplicationUserModelId( pid );
			if( string.IsNullOrEmpty( package ) )
			{
				m_windowsAppIcon.TryAdd( exe, null );
				return null;
			}

			IDisposable image = GetWindowsAppIcon( package, exe );
			m_windowsAppIcon.TryAdd( exe, image );
			return image;
		}

		private static readonly ConcurrentDictionary<string, IDisposable> m_windowsAppIcon = new ConcurrentDictionary<string, IDisposable>();

		private static IDisposable GetWindowsAppIcon( string package, string exePath )
		{
			// Under HKCR are a set of keys named things like AppX2jm25qtmp2qxstv333wv5mne3k5bf4bm, which I don't
			// know the meaning of, but these keys are directly inked to App User Model IDs and contain paths for
			// the application icons.
			RegistryKey k = RegistryKey.OpenBaseKey( RegistryHive.ClassesRoot, RegistryView.Registry64 );
			RegistryKey info = null;
			foreach( string subName in k.GetSubKeyNames() )
			{
				var sub = k.OpenSubKey( subName, writable: false );
				var application = sub.OpenSubKey( "Application", writable: false );
				if( application == null )
					continue;
				var appUserModelId = (string)application.GetValue( "AppUserModelID" );
				if( appUserModelId == package )
				{
					info = application;
					break;
				}
			}
			if( info == null )
				return null;

			// Find the icon from this registration entry
			string icon = (string)info.GetValue( "ApplicationIcon" );
			if( icon == null )
				return null;

			// Does the icon specify an asset file we can load?
			var m = Regex.Match( icon, @"ms-resource://([\w\.]+)/Files/(Assets/\w+\.png)" );
			if( !m.Success )
				return null;

			// Find the components of the specified file, which will not actually represent a true
			// file path. From here we have to check for different variants of the image.
			string rootDir = Path.GetDirectoryName( exePath );
			string imagePath = Path.Combine( rootDir, m.Groups[2].Value );
			return LoadImageIfPresent( imagePath, "targetsize-16_contrast-white" ) ??
				LoadImageIfPresent( imagePath, "contrast-white_targetsize-16" ) ??
				null;
		}

		private static IDisposable LoadImageIfPresent( string basePath, string variant )
		{
			string maybe = Path.ChangeExtension( basePath, "." + variant + Path.GetExtension( basePath ) );
			if( !File.Exists( maybe ) )
				return null;

			return Image.FromFile( maybe );
		}

		/// <summary>
		/// Helper for enumerating application windows.
		/// </summary>
		private class AppWindowEnumerator
		{
			public AppWindowEnumerator( int maxCount )
			{
				this.Windows = new List<IntPtr>();
				m_maxCount = maxCount;
			}

			public List<IntPtr> Windows { get; private set; }

			public bool ReadWindow( IntPtr wnd, IntPtr lParam )
			{
				if( !IsWindowVisible( wnd ) )
					return true;

				// This doesn't seem to matter for behavior/performance, and it's annoying to have "hung" windows not appear in the switcher
				//if( IsHungAppWindow( wnd ) )
				//	return true;

				if( IsAppWindow( wnd ) )
					this.Windows.Add( wnd );
				return (m_maxCount == 0 || this.Windows.Count < m_maxCount);
			}

			private int m_maxCount;
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
			if( 0 != (style & (uint)WindowStyle.Child) ||                   // children
				0 == (style & (uint)(WindowStyle.Caption | WindowStyle.SysMenu)) || // windows with no caption or system menu
				0 != (exStyle & (uint)WindowExStyle.ToolWindow) ||          // tool windows
				0 != (exStyle & (uint)WindowExStyle.NoActivate) )           // windows that can't be activated
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
		private static extern IntPtr GetForegroundWindow();

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

		[DllImport( "user32.dll", CharSet = CharSet.Unicode )]
		static extern int GetWindowText( IntPtr wnd, StringBuilder buf, int bufSize );

		[DllImport( "user32.dll" )]
		static extern IntPtr GetWindow( IntPtr wnd, GetWindowTarget target );

		delegate bool EnumWindowsProc( IntPtr wnd, IntPtr lParam );

		[DllImport( "user32.dll" )]
		static extern bool EnumWindows( EnumWindowsProc enumProc, IntPtr lParam );

		[DllImport( "user32.dll", SetLastError = true )]
		private static extern int RegisterWindowMessage( string name );

		[DllImport( "user32", SetLastError = true )]
		private static extern bool RegisterShellHookWindow( IntPtr hWnd );

		[DllImport( "user32", SetLastError = true )]
		private static extern int DeregisterShellHookWindow( IntPtr hWnd );

		[DllImport( "user32.dll" )]
		public static extern bool IsIconic( IntPtr wnd );

		[DllImport( "user32.dll" )]
		private static extern bool ShowWindow( IntPtr wnd, int cmdShow );

		enum WindowLong : int
		{
			Style = -16,
			ExStyle = -20
		}

		enum GetWindowTarget
		{
			Next = 2,
			Owner = 4,
			FirstChild = 5
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

		[DllImport( "kernel32.dll", SetLastError = true )]
		internal static extern Int32 GetApplicationUserModelId(
			IntPtr hProcess,
			ref UInt32 AppModelIDLength,
			[MarshalAs( UnmanagedType.LPWStr )] StringBuilder sbAppUserModelID );

		[DllImport( "kernel32.dll" )]
		internal static extern IntPtr OpenProcess( int dwDesiredAccess, bool bInheritHandle, uint dwProcessId );

		[DllImport( "kernel32.dll" )]
		static extern bool CloseHandle( IntPtr hHandle );

		private const int ERROR_SUCCESS = 0;
		private const int ERROR_INSUFFICIENT_BUFFER = 0x7a;
		private const int QueryLimitedInformation = 0x1000;

		private static string GetApplicationUserModelId( uint pid )
		{
			IntPtr ptrProcess = OpenProcess( QueryLimitedInformation, false, pid );
			if( IntPtr.Zero == ptrProcess )
				return null;

			string id = null;
			uint cchLen = 130;
			StringBuilder sbName = new StringBuilder( (int)cchLen );
			Int32 lResult = GetApplicationUserModelId( ptrProcess, ref cchLen, sbName );
			if( ERROR_SUCCESS == lResult )
			{
				id = sbName.ToString();
			}
			else if( ERROR_INSUFFICIENT_BUFFER == lResult )
			{
				sbName = new StringBuilder( (int)cchLen );
				if( ERROR_SUCCESS == GetApplicationUserModelId( ptrProcess, ref cchLen, sbName ) )
				{
					id = sbName.ToString();
				}
			}
			CloseHandle( ptrProcess );

			return id;
		}
	}
}
