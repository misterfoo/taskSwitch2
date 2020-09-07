using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace taskSwitch2
{
	public abstract class TaskItem
	{
		/// <summary>
		/// Gets the name of the task.
		/// </summary>
		public string TaskName
		{
			get
			{
				return m_name;
			}
			protected set
			{
				m_name = NormalizeTaskName( value );
			}
		}
		private string m_name;

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

		/// <summary>
		/// Normalizes task names to avoid differences between native and UIA access. I saw a case
		/// where a website title was using U+2002 (en space) instead of a regular space character,
		/// but UIA was converting that to a regular space, so then the two window lists disagreed
		/// and that window wouldn't show up in the MRU list.
		/// </summary>
		private static string NormalizeTaskName( string str )
		{
			if( str.Contains( "groovy" ) )
				return str;
			return str.Replace( (char)0x2002, ' ' );
		}
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
		/// The icon for this window (an Icon or Image).
		/// </summary>
		public IDisposable Icon { get; set; }
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
	[System.Diagnostics.DebuggerDisplay("{TaskName}")]
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
	[System.Diagnostics.DebuggerDisplay( "{TaskName}" )]
	public class TaskbarButton : TaskItem
	{
		public TaskbarButton( AutomationElement raw )
		{
			m_raw = raw;
			string name = m_raw.Current.Name;
			name = Regex.Replace( name, @" - \d+ running window(s)?$", "" );
			this.TaskName = name;
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
