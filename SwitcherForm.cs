using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace taskSwitch2
{
	/// <summary>
	/// The form which is displayed when switching between applications.
	/// </summary>
	public partial class SwitcherForm : Form
	{
		public SwitcherForm()
		{
			InitializeComponent();
			this.BackColor = m_theme.BackgroundColor;
			this.DoubleBuffered = true;

			if( Program.UiTestMode )
			{
				this.TopMost = false;
				this.ShowInTaskbar = true;
				return;
			}

			StartRefreshThread();
			InstallKeyboardHook();
			InstallAccessibilityHook();
			InstallTrayIcon();
			CreateHandle();
		}

		private KeyboardHook m_kbHook;
		private IDisposable m_foregroundChangeHook;
		private ManualResetEvent m_shutdownEvent = new ManualResetEvent( initialState: false );
		private AutoResetEvent m_refreshTickleEvent = new AutoResetEvent( initialState: false );
		private TaskItem m_selectedItem;
		private bool m_everAdjustedSelection;
		private TaskGrid m_taskGrid;
		private Theme m_theme = new Theme();

		/// <summary>
		/// the most recent state we've retrieved
		/// </summary>
		private SystemState m_state;

		/// <summary>
		/// the system state when the switcher opened (this is null if the switcher is not open)
		/// </summary>
		private SystemState m_switchState;

		protected override void OnLoad( EventArgs e )
		{
			base.OnLoad( e );
			if( Program.UiTestMode )
				RefreshTaskGrid();
		}

		protected override void OnFormClosed( FormClosedEventArgs e )
		{
			base.OnFormClosed( e );
			if( m_foregroundChangeHook != null )
				m_foregroundChangeHook.Dispose();
		}

		/// <summary>
		/// Shows this window
		/// </summary>
		private void DisplaySwitcher()
		{
			if( this.Visible )
				return;
			m_everAdjustedSelection = false;
			m_switchState = RefreshTaskGrid();
			if( m_switchState == null )
				return;
			Show();
			WindowTools.SwitchToWindow( this.Handle );
		}

		/// <summary>
		/// Hides this window
		/// </summary>
		private void CloseSwitcher( bool switchToSelected )
		{
			// this gets called recursively when the window loses focus from a switch, which can
			// cause problems, so we ignore recursive calls
			if( m_isClosing )
				return;
			m_isClosing = true;
			try
			{
				CloseSwitcherInternal( switchToSelected );
			}
			finally
			{
				m_isClosing = false;
			}
		}

		private bool m_isClosing;

		private void CloseSwitcherInternal( bool switchToSelected )
		{
			if( m_kbHook != null )
				m_kbHook.SwitcherClosed();

			// If the user is very quick with the keyboard, we may close before registering any
			// selection changes, and thus never actually switch.
			if( switchToSelected && !m_everAdjustedSelection )
				AdjustSelection( 0, 1 );

			if( switchToSelected && m_selectedItem != null )
			{
				m_selectedItem.SwitchTo();

				// If we switched to a recent window, update the internal state in case we switch
				// again before the next refresh. We could do a full refresh right here, but that's
				// slow and would block the main thread. We're operating on m_switchState instead of
				// m_state because the latter may already have been updated by the refresh thread.
				TaskWindow wnd = m_selectedItem as TaskWindow;
				if( wnd != null && m_switchState != null )
				{
					m_switchState.WindowsByZOrder.Remove( wnd );
					m_switchState.WindowsByZOrder.Insert( 0, wnd );
				}
			}

			m_switchState = null;
			Hide();
		}

		private void OnForegroundChange( IntPtr newForeground )
		{
			// Activating the switcher window is not interesting to us.
			if( this.IsHandleCreated && newForeground == this.Handle )
				return;
			HandleEnvironmentChange();
		}

		private void OnWindowCreatedOrDestroyed()
		{
			HandleEnvironmentChange();
		}

		private void HandleEnvironmentChange()
		{
			// we need to refresh our system information
			m_refreshTickleEvent.Set();
		}

		/// <summary>
		/// Handler for responding to input events from the keyboard hook
		/// </summary>
		private void PerformSwitch( KeyboardHook.SwitchCommand cmd )
		{
			switch( cmd )
			{
			case KeyboardHook.SwitchCommand.SwitchForward:
				DisplaySwitcher();
				AdjustSelection( 0, 1 );
				break;
			case KeyboardHook.SwitchCommand.SwitchReverse:
				DisplaySwitcher();
				AdjustSelection( 0, -1 );
				break;
			case KeyboardHook.SwitchCommand.MoveLeft:
				AdjustSelection( -1, 0 );
				break;
			case KeyboardHook.SwitchCommand.MoveRight:
				AdjustSelection( 1, 0 );
				break;
			case KeyboardHook.SwitchCommand.MoveUp:
				AdjustSelection( 0, -1 );
				break;
			case KeyboardHook.SwitchCommand.MoveDown:
				AdjustSelection( 0, 1 );
				break;
			case KeyboardHook.SwitchCommand.CommitSwitch:
				CloseSwitcher( switchToSelected: true );
				break;
			case KeyboardHook.SwitchCommand.CancelSwitch:
				CloseSwitcher( switchToSelected: false );
				break;
			}
		}

		/// <summary>
		/// Keyboard input handler. Note that this is only used on UI Test mode;
		/// normally our keyboard input comes from the keyboard hook.
		/// </summary>
		protected override void OnKeyDown( KeyEventArgs e )
		{
			base.OnKeyDown( e );
			if( !Program.UiTestMode )
				return;
			switch( e.KeyCode )
			{
			case Keys.Left:
				PerformSwitch( KeyboardHook.SwitchCommand.MoveLeft );
				break;
			case Keys.Right:
				PerformSwitch( KeyboardHook.SwitchCommand.MoveRight );
				break;
			case Keys.Up:
				PerformSwitch( KeyboardHook.SwitchCommand.MoveUp );
				break;
			case Keys.Down:
				PerformSwitch( KeyboardHook.SwitchCommand.MoveDown );
				break;
			case Keys.Enter:
				m_selectedItem.SwitchTo();
				break;
			case Keys.Escape:
				Close();
				break;
			case Keys.F5:
				RefreshTaskGrid();
				Invalidate();
				break;
			default:
				break;
			}
		}

		protected override void OnMouseClick( MouseEventArgs e )
		{
			base.OnMouseClick( e );
			if( m_taskGrid == null )
				return;

			// did they click on one of the tasks?
			TaskItem task = m_taskGrid.TasksFlat.FirstOrDefault( x => x.Area.Contains( e.Location ) );
			if( task != null )
			{
				m_selectedItem = task;
				CloseSwitcher( switchToSelected: true );
			}
		}

		/// <summary>
		/// Changes the selected item in the grid of tasks, usually in response to some keyboard input.
		/// </summary>
		private void AdjustSelection( int dx, int dy )
		{
			TaskItem oldSel = m_selectedItem;
			Point pos = GetSelectionPos();
			pos.X += dx;
			pos.Y += dy;

			if( pos.X < 0 )
				pos.X = m_taskGrid.TasksByColumn.Length - 1;
			else if( pos.X >= m_taskGrid.TasksByColumn.Length )
				pos.X = 0;

			var column = m_taskGrid.TasksByColumn[pos.X];
			if( pos.Y < 0 )
			{
				pos.Y = column.Length - 1;
			}
			else if( dy == -1 && column[pos.Y] == null )
			{
				pos.Y = column.Length - 1;
			}
			else if( pos.Y >= column.Length )
			{
				if( dx != 0 )
					// we got here from another column, so stay close to the row we were on
					pos.Y = column.Length - 1;
				else
					// we're moving within the same column, so wrap around
					pos.Y = 0;
			}
			while( column[pos.Y] == null )
				pos.Y++;

			m_selectedItem = column[pos.Y];
			m_everAdjustedSelection = true;
			Invalidate();
		}

		/// <summary>
		/// Gets the position of the selected item in Column,Row coordinates.
		/// </summary>
		private Point GetSelectionPos()
		{
			for( int iColumn = 0; iColumn < m_taskGrid.TasksByColumn.Length; iColumn++ )
			{
				var column = m_taskGrid.TasksByColumn[iColumn];
				for( int iRow = 0; iRow < column.Length; iRow++ )
				{
					if( column[iRow] == m_selectedItem )
						return new Point( iColumn, iRow );
				}
			}
			return new Point();
		}

		/// <summary>
		/// Computes a new task grid and adjusts the window accordingly. Note that this does not
		/// refresh the cached system state.
		/// </summary>
		private SystemState RefreshTaskGrid()
		{
			SystemState state = m_state;
			if( state == null )
				state = RefreshSystemState();
			if( state == null ||
				state.WindowsByZOrder.Count == 0 )
			{
				m_state = null;
				return null;
			}
			m_taskGrid = new TaskGrid( state );
			m_selectedItem = m_taskGrid.MruTasks[0];
			PositionWindow();
			return state;
		}

		/// <summary>
		/// Positions the picker window on the screen.
		/// </summary>
		private void PositionWindow()
		{
			if( !this.IsHandleCreated )
				return;
			Screen mainScreen = Screen.PrimaryScreen;

			// we want the MRU area to be centered on the screen
			int mruX = (mainScreen.WorkingArea.Width / 2) - (m_taskGrid.MruArea.Width / 2);
			int x = mruX - m_taskGrid.MruArea.Left;
			int y = (mainScreen.WorkingArea.Height / 2) - (m_taskGrid.TotalSize.Height / 2);
			this.Location = new Point( x, y );
			this.Size = m_taskGrid.TotalSize;
		}

		protected override void OnLostFocus( EventArgs e )
		{
			base.OnLostFocus( e );
			if( !Program.UiTestMode )
				CloseSwitcher( switchToSelected: false );
		}

		protected override void OnPaint( PaintEventArgs e )
		{
			base.OnPaint( e );
			if( m_taskGrid == null || m_taskGrid.TasksByColumn.Length == 0 )
				return;

			// draw our main border
			if( m_theme.BorderColor != Color.Transparent )
			{
				Rectangle area = new Rectangle( new Point(), this.ClientSize );
				area.Width -= m_theme.BorderWidth;
				area.Height -= m_theme.BorderWidth;
				using( Pen p = new Pen( m_theme.BorderColor, m_theme.BorderWidth ) )
					e.Graphics.DrawRectangle( p, area );
			}

			// color the MRU area
			DrawRectangle( e.Graphics, m_taskGrid.MruArea, m_theme.MruBackColor, Color.Transparent, 0 );

			// draw all of the tiles
			foreach( TaskItem task in m_taskGrid.TasksFlat )
				DrawAppTile( e.Graphics, task );
		}

		private void DrawAppTile( Graphics g, TaskItem task )
		{
			Size iconSize = new Size( Metrics.PreferredIconSize, Metrics.PreferredIconSize );
			Rectangle totalArea = task.Area;
			bool isSelection = (task == m_selectedItem);

			// draw the tile background area
			Color backColor = isSelection ? m_theme.ActiveTileBackColor : m_theme.TileBackColor;
			Color borderColor = isSelection ? m_theme.ActiveTileBorderColor : m_theme.TileBorderColor;
			DrawRectangle( g, totalArea, backColor, borderColor, m_theme.TileBorderWidth );

			// the area in which the main content is drawn
			Rectangle contentArea = totalArea;
			contentArea.Inflate( -Metrics.AppTileInsideGutter, -Metrics.AppTileInsideGutter );

			// draw the window icon
			Icon icon = GetIcon( task );
			if( icon != null )
			{
				Rectangle iconRect = new Rectangle( contentArea.Location, iconSize );
				try
				{
					g.DrawIcon( icon, iconRect );
				}
				catch( System.DivideByZeroException )
				{
					// this seems to happen sometimes, and I don't understand why
				}
			}

			// draw the window title
			Rectangle textArea = contentArea;
			int textOffset = iconSize.Width;
			textArea.X += textOffset;
			textArea.Width -= textOffset;
			Color textColor = isSelection ? m_theme.ActiveTileLabelColor : m_theme.TileLabelColor;
			string text = task.TaskName;
			TextRenderer.DrawText( g, text, m_theme.TileText, textArea, textColor,
				TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter );
		}

		private Icon GetIcon( TaskItem task )
		{
			if( task is TaskWindow )
			{
				return ((TaskWindow)task).Icon;
			}
			else
			{
				TaskbarButton btn = (TaskbarButton)task;
				if( btn.AssociatedWindow == null )
					return null;
				return btn.AssociatedWindow.Icon;
			}
		}

		private void DrawRectangle( Graphics g, Rectangle area, Color backColor, Color borderColor, int borderWidth )
		{
			if( backColor != Color.Transparent )
			{
				using( Brush back = new SolidBrush( backColor ) )
					g.FillRectangle( back, area );
			}
			if( borderColor != Color.Transparent )
			{
				using( Pen pen = new Pen( borderColor, borderWidth ) )
					g.DrawRectangle( pen, area );
			}
		}

		private void StartRefreshThread()
		{
			var thread = new Thread( StateRefreshThread );

			// this is important for avoiding handle leaks when talking to the UIAutomation framework
			thread.SetApartmentState( ApartmentState.STA );

			thread.Start();
		}

		private void StateRefreshThread()
		{
			WaitHandle[] handles = new WaitHandle[] { m_shutdownEvent, m_refreshTickleEvent };
			for(;;)
			{
				RefreshSystemState();
				int wait = WaitHandle.WaitAny( handles, TimeSpan.FromSeconds( 10 ) );
				if( wait == 0 )
					return;
				// we timed out or were tickled
			}
		}

		private SystemState RefreshSystemState()
		{
			Stopwatch sw = Stopwatch.StartNew();
			SystemState state = new SystemState();
			sw.Stop();
//			Debug.WriteLine( "Computed new system state in {0} ms", sw.ElapsedMilliseconds );
			Interlocked.Exchange( ref m_state, state );
//			Debug.WriteLine( "===> {0} active tasks", state.WindowsByTaskbarOrder.Count );
			return state;
		}

		private void InstallKeyboardHook()
		{
			// this gets its own thread because the keyboard hook affects all typing on the system,
			// so we want to make that as responsive as possible.
			m_kbHook = new KeyboardHook();
			var thread = new Thread( () =>
			{
				m_kbHook.HookSwitcherKey( this, PerformSwitch );
				Application.Run();
			} );
			thread.Start();
		}

		private void InstallAccessibilityHook()
		{
			m_foregroundChangeHook = WindowTools.HookForegroundChange( OnForegroundChange );
			WindowTools.HookWindowCreateDestroy( OnWindowCreatedOrDestroyed );
		}

		private void InstallTrayIcon()
		{
			// this gets its own thread so it's not blocked by our UI thread doing slow stuff (like
			// refreshing the list of apps)
			var thread = new Thread( TrayIconThread );
			thread.Start();
		}

		private void TrayIconThread()
		{
			ContextMenuStrip menu = new ContextMenuStrip();
			ToolStripItem exitCmd = menu.Items.Add( "E&xit" );
			exitCmd.Click += ( s, a ) =>
			{
				m_shutdownEvent.Set();
				Application.Exit();
			};

			NotifyIcon trayIcon = new NotifyIcon();
			trayIcon.ContextMenuStrip = menu;
			trayIcon.Icon = Properties.Resources.Application;
			trayIcon.Text = "Task Switcher 2";
			trayIcon.Visible = true;

			Application.Run();

			trayIcon.Visible = false;
		}
	}
}
