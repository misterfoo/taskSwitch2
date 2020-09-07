using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Automation;

namespace taskSwitch2
{
	/// <summary>
	/// Describes the state of the system, specifically the list of top-level windows.
	/// </summary>
	class SystemState
	{
		public SystemState( SystemState lastKnown )
		{
			// Bring along information from previous state, if any.
			if( lastKnown != null )
			{
				m_windowToProcessCache = lastKnown.m_windowToProcessCache;
				m_taskbarButtonOrder = lastKnown.m_taskbarButtonOrder;
			}
			else
			{
				m_windowToProcessCache = new ConcurrentDictionary<IntPtr, string>();
			}

			try
			{
				using( Utilities.TimedBlock( "SystemState->RefreshUnsafe" ) )
					RefreshUnsafe();
			}
			catch( System.Windows.Automation.ElementNotAvailableException ex )
			{
				DebugEvent.Record( ex.ToString() );

				// try one more time
				System.Threading.Thread.Sleep( TimeSpan.FromMilliseconds( 500 ) );
				try
				{
					RefreshUnsafe();
				}
				catch( System.Exception ex2 )
				{
					DebugEvent.Record( ex2.ToString() );
					this.WindowsByTaskbarOrder = new List<TaskbarButton>();
					this.WindowsByZOrder = new List<TaskWindow>();
				}
			}
		}

		private readonly ConcurrentDictionary<IntPtr, string> m_windowToProcessCache;
		private List<AutomationElement> m_taskbarButtonOrder;

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

		/// <summary>
		/// Refreshes the state to reflect a new window moving to the front of the Z-order.
		/// </summary>
		/// <param name="wnd">The window which is now at the front of the z-order.</param>
		public void NotifyNewForeground( IntPtr wnd )
		{
			var task = this.WindowsByZOrder.FirstOrDefault( x => x.WindowHandle == wnd );
			if( task == null )
			{
				DebugEvent.Record( $"SystemState.NotifyNewForeground can't find window {wnd}" );
				return;
			}

			this.WindowsByZOrder.Remove( task );
			this.WindowsByZOrder.Insert( 0, task );

			// DEBUG: trace the first few items of the Zorder
			//string first = string.Join( ", ", this.WindowsByZOrder.Take( 3 ).Select( x => x.TaskName ) );
			//DebugEvent.Record( $"SystemState zorder: {first}" );
		}

		private void RefreshUnsafe()
		{
			using( Utilities.TimedBlock( "SystemState->RefreshUnsafe->FindAppWindowsNative" ) )
			{
				this.WindowsByZOrder = WindowTools.FindAppWindowsNative()
					.Select( x => new TaskWindowNative( x ) )
					.Cast<TaskWindow>()
					.ToList();
			}

			using( Utilities.TimedBlock( "SystemState->RefreshUnsafe->GetTaskbarButtonOrder" ) )
			{
				this.WindowsByTaskbarOrder = GetTaskbarButtonOrder()
					.Where( x => x.Current.Name != "" )
					.Select( x => new TaskbarButton( x ) )
					.ToList();
			}

			// windows that don't have taskbar buttons should not be shown by this app
			using( Utilities.TimedBlock( "SystemState->RefreshUnsafe->[remove no-button windows]" ) )
			{
				HashSet<string> knownNames = new HashSet<string>( this.WindowsByTaskbarOrder.Select( x => x.TaskName ) );
				int before = this.WindowsByZOrder.Count;
				this.WindowsByZOrder.RemoveAll( x =>
					{
						string name = x.TaskName;
						if( !knownNames.Contains( name ) )
							return true;
						else
							return false;
					} );
				int after = this.WindowsByZOrder.Count;
				if( before != after )
					DebugEvent.Record( "Removed {0} foreground windows with no taskbar buttons", before - after );
			}

			// for windows without icons, try to use the icon of some other window from the same process
			// NOTE: this doesn't seem to accomplish anything, and WindowTools.GetWindowProcess is slooooow
			/*using( Utilities.TimedBlock( "SystemState->RefreshUnsafe->[fix broken icons]" ) )
			{
				var windowsByPid = this.WindowsByZOrder.GroupBy(
					x => WindowTools.GetWindowProcess( x.WindowHandle, m_windowToProcessCache ) )
					.ToArray();
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
			}*/

			// try to match windows and buttons, so we can get basic task info for the buttons
			using( Utilities.TimedBlock( "SystemState->RefreshUnsafe->[link windows together]" ) )
			{
				var windowsByName = new Dictionary<string, TaskWindow>();
				this.WindowsByZOrder.ForEach( x => windowsByName[x.TaskName] = x );
				this.WindowsByTaskbarOrder.ForEach( x =>
					{
						TaskWindow wnd;
						if( windowsByName.TryGetValue( x.TaskName, out wnd ) )
							x.AssociatedWindow = wnd;
					} );
				this.WindowsByTaskbarOrder.RemoveAll( x => x.AssociatedWindow == null );
			}
		}

		private IEnumerable<AutomationElement> GetTaskbarButtonOrder()
		{
			if( m_taskbarButtonOrder == null )
				m_taskbarButtonOrder = WindowTools.GetTaskbarButtonOrder();
			return m_taskbarButtonOrder;
		}
	}
}
