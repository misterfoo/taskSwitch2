using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace taskSwitch2
{
	/// <summary>
	/// Describes the state of the system, specifically the list of top-level windows.
	/// </summary>
	class SystemState
	{
		public SystemState()
		{
			try
			{
				RefreshUnsafe();
			}
			catch( System.Windows.Automation.ElementNotAvailableException )
			{
				// try one more time
				System.Threading.Thread.Sleep( TimeSpan.FromMilliseconds( 500 ) );
				try
				{
					RefreshUnsafe();
				}
				catch
				{
					this.WindowsByTaskbarOrder = new List<TaskbarButton>();
					this.WindowsByZOrder = new List<TaskWindow>();
				}
			}
		}

		private void RefreshUnsafe()
		{
			//this.WindowsByZOrder = WindowTools.FindAppWindowsUIA()
			//	.Select( x => new TaskWindowUIA( x ) )
			//	.Cast<TaskWindow>()
			//	.ToList();
			this.WindowsByZOrder = WindowTools.FindAppWindowsNative()
				.Select( x => new TaskWindowNative( x ) )
				.Cast<TaskWindow>()
				.ToList();
			this.WindowsByTaskbarOrder = WindowTools.GetTaskbarButtonOrder()
				.Select( x => new TaskbarButton( x ) )
				.ToList();

			// windows that don't have taskbar buttons should not be show by this app
			HashSet<string> knownNames = new HashSet<string>( this.WindowsByTaskbarOrder.Select( x => x.TaskName ) );
			this.WindowsByZOrder.RemoveAll( x => !knownNames.Contains( x.TaskName ) );

			// for windows without icons, try to use the icon of some other window from the same process
			var windowsByPid = this.WindowsByZOrder.GroupBy( x => WindowTools.GetWindowProcess( x.WindowHandle ) ).ToArray();
			foreach( var set in windowsByPid )
			{
				TaskWindow anyWithIcon = set.FirstOrDefault( x => x.Icon != null );
				if( anyWithIcon != null )
				{
					var withoutIcon = set.Where( x => x.Icon == null );
					foreach( TaskWindow wnd in withoutIcon )
						wnd.Icon = anyWithIcon.Icon;
				}
			}

			// try to match windows and buttons, so we can get basic task info for the buttons
			var windowsByName = new Dictionary<string, TaskWindow>();
			this.WindowsByZOrder.ForEach( x => windowsByName[x.TaskName] = x );
			this.WindowsByTaskbarOrder.ForEach( x =>
				{
					TaskWindow wnd;
					if( windowsByName.TryGetValue( x.TaskName, out wnd ) )
						x.AssociatedWindow = wnd;
				} );
		}

		/// <summary>
		/// The list of open applications in Z order 
		/// </summary>
		public List<TaskWindow> WindowsByZOrder
		{
			get; private set;
		}

		/// <summary>
		/// The list of open applications as shown in the taskbar.
		/// </summary>
		public List<TaskbarButton> WindowsByTaskbarOrder
		{
			get; private set;
		}
	}

	public abstract class TaskItem
	{
		/// <summary>
		/// Gets the name of the task.
		/// </summary>
		public string TaskName { get; protected set; }

		/// <summary>
		/// Stores the rectangle of the task in SwitcherForm client coordinates.
		/// </summary>
		public Rectangle Area { get; set; }

		/// <summary>
		/// Indicates whether the item is the first in a group of items.
		/// </summary>
		public bool BeginGroup { get; set; }

		/// <summary>
		/// Switches to the window for this task.
		/// </summary>
		public abstract void SwitchTo();
	}

	/// <summary>
	/// A task which is based on a window (rather than a button)
	/// </summary>
	public abstract class TaskWindow : TaskItem
	{
		/// <summary>
		/// Gets the native window handle of the window.
		/// </summary>
		public abstract IntPtr WindowHandle { get; }

		/// <summary>
		/// The icon for this window.
		/// </summary>
		public Icon Icon { get; set; }
	}

	/// <summary>
	/// Represents a top-level task window.
	/// </summary>
	public class TaskWindowUIA : TaskWindow
	{
		public TaskWindowUIA( AutomationElement raw )
		{
			m_raw = raw;
			this.TaskName = DetermineTaskName();
			this.Icon = WindowTools.GetIcon( this.WindowHandle );
		}

		private AutomationElement m_raw;

		/// <summary>
		/// Switches to the window for this task.
		/// </summary>
		public override void SwitchTo()
		{
			WindowTools.SwitchToWindow( this.WindowHandle );
		}

		/// <summary>
		/// Gets the native window handle of the window.
		/// </summary>
		public override IntPtr WindowHandle
		{
			get { return new IntPtr( m_raw.Current.NativeWindowHandle ); }
		}

		private string DetermineTaskName()
		{
			string name = m_raw.Current.Name;

			// Some apps (notably Cisco Webex) don't set the window text properly for their
			// top-level windows; instead the window title is just a number (e.g. 30086). For these
			// we have to dig around and find a TitleBar control. This takes significant time, so we
			// only do it when absolutely necessary.
			if( System.Text.RegularExpressions.Regex.IsMatch( name, @"^\d+$" ) )
				name = FindTitleBar().Current.Name;

			return name;
		}

		private AutomationElement FindTitleBar()
		{
			AutomationElement tb = m_raw.FindFirst( TreeScope.Children,
				new PropertyCondition( AutomationElement.ControlTypeProperty, ControlType.TitleBar ) );
			if( tb == null )
				return m_raw;
			return tb;
		}
	}

	/// <summary>
	/// Represents a top-level task window.
	/// </summary>
	public class TaskWindowNative : TaskWindow
	{
		public TaskWindowNative( IntPtr wnd )
		{
			m_wnd = wnd;
			this.TaskName = WindowTools.GetWindowText( m_wnd );
			this.Icon = WindowTools.GetIcon( m_wnd );
		}

		private IntPtr m_wnd;

		/// <summary>
		/// Switches to the window for this task.
		/// </summary>
		public override void SwitchTo()
		{
			WindowTools.SwitchToWindow( m_wnd );
		}

		/// <summary>
		/// Gets the native window handle of the window.
		/// </summary>
		public override IntPtr WindowHandle
		{
			get { return m_wnd; }
		}
	}

	/// <summary>
	/// Represents a button in the taskbar.
	/// </summary>
	public class TaskbarButton : TaskItem
	{
		public TaskbarButton( AutomationElement raw )
		{
			m_raw = raw;
			this.TaskName = m_raw.Current.Name;
		}

		private AutomationElement m_raw;

		/// <summary>
		/// The window we think corresponds to the task.
		/// </summary>
		public TaskWindow AssociatedWindow { get; set; }

		/// <summary>
		/// Switches to the window for this task.
		/// </summary>
		public override void SwitchTo()
		{
			InvokePattern invoker = (InvokePattern)m_raw.GetCurrentPattern( InvokePatternIdentifiers.Pattern );
			if( invoker == null )
				return;
			invoker.Invoke();
		}
	}
}
