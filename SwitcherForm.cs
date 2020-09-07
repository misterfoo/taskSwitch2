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
	partial class SwitcherForm : Form
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

			InstallKeyboardHook();
			InstallTrayIcon();
			CreateHandle();

			if( !Program.NoBackgroundRefresh )
				StartRefreshTimer();

			if( Debugger.IsAttached )
				ShowDebugWindow();
		}

		private KeyboardHook m_kbHook;
		private ManualResetEvent m_shutdownEvent = new ManualResetEvent( initialState: false );
		private TaskItem m_selectedItem;
		private bool m_everAdjustedSelection;
		private TaskGrid m_taskGrid;
		private int m_shellHookMsg;
		private Theme m_theme = new Theme();
		private int m_refreshCount;

		/// <summary>
		/// If we are in switch-by-typing mode, the string to search for. A value of null indicates
		/// that we are in regular switching mode.
		/// </summary>
		private string m_searchString;

		/// <summary>
		/// the most recent state we've retrieved
		/// </summary>
		private SystemState m_state;

		/// <summary>
		/// the system state when the switcher opened (this is null if the switcher is not open)
		/// </summary>
		private SystemState m_switchState;

		protected override void OnHandleCreated( EventArgs e )
		{
			base.OnHandleCreated( e );

			// This allows us to keep track of things that are changing without repeatedly
			// re-scanning the system state.
			WindowTools.RegisterShellHook( this.Handle, out m_shellHookMsg );
		}

		protected override void OnLoad( EventArgs e )
		{
			base.OnLoad( e );
			if( Program.UiTestMode )
				m_state = RebuildTaskGrid();
		}

		protected override CreateParams CreateParams
		{
			get
			{
				const int CS_DROPSHADOW = 0x20000;
				CreateParams cp = base.CreateParams;
				cp.ClassStyle |= CS_DROPSHADOW;
				return cp;
			}
		}

		protected override void OnClosing( System.ComponentModel.CancelEventArgs e )
		{
			WindowTools.UnregisterShellHook( this.Handle );
			base.OnClosing( e );
		}

		protected override void WndProc( ref Message m )
		{
			if( m.Msg == m_shellHookMsg )
				HandleShellHook( (WindowTools.ShellHookEvent)m.WParam, m.LParam );
			base.WndProc( ref m );
		}

		/// <summary>
		/// Shows this window
		/// </summary>
		private void DisplaySwitcher()
		{
			if( this.Visible )
				return;

			m_everAdjustedSelection = false;
			m_searchString = null;

			// Setup the task grid based on the system state
			DebugEvent.Record( "Switcher opening..." );
			using( Utilities.TimedBlock( "DisplaySwitcher->RefreshTaskGrid" ) )
				m_switchState = RebuildTaskGrid();
			if( m_switchState == null )
				return;

			// Show the switcher
			using( Utilities.TimedBlock( "DisplaySwitcher->Show" ) )
				Show();

			WindowTools.SwitchToWindow( this.Handle );
			DebugEvent.Record( "Switcher opened" );
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
			DebugEvent.Record( "Switcher closing..." );
			if( m_kbHook != null )
				m_kbHook.SwitcherClosed();

			// If the user is very quick with the keyboard, we may close before registering any
			// selection changes, and thus never actually switch.
			if( switchToSelected && !m_everAdjustedSelection && m_searchString == null )
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
					m_switchState.NotifyNewForeground( wnd.WindowHandle );
			}

			m_switchState = null;
			Hide();
			DebugEvent.Record( "Switcher closed" );
		}

		private void HandleShellHook( WindowTools.ShellHookEvent evt, IntPtr window )
		{
			string text = WindowTools.GetWindowText( window );
			DebugEvent.Record( $"HandleShellHook->{evt} for {text} ({window:x})" );

			// ignore events related to the switcher itself
			if( window == this.Handle )
				return;

			// major changes should cause us to recompute state entirely
			if( evt == WindowTools.ShellHookEvent.WindowCreated ||
				evt == WindowTools.ShellHookEvent.WindowDestroyed )
			{
				m_state = null;
			}

			// ordering changes can be handled incrementally
			if( evt == WindowTools.ShellHookEvent.WindowActivated ||
				evt == WindowTools.ShellHookEvent.RudeWindowActivated )
			{
				SystemState state = m_state;
				if( state != null )
					state.NotifyNewForeground( window );
			}
		}

		/// <summary>
		/// Handler for responding to input events from the keyboard hook
		/// </summary>
		private void DoStandardSwitch( KeyboardHook.SwitchCommand cmd )
		{
			DebugEvent.Record( "PerformSwitch: {0}", cmd );
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
			case KeyboardHook.SwitchCommand.ActivateTypingMode:
				DisplaySwitcher();
				SetSearchString( "" );
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

		private void DoKeyboardSearch( Keys keys )
		{
			if( (keys >= Keys.A && keys <= Keys.Z) ||
				(keys >= Keys.D0 && keys <= Keys.D9) )
			{
				char c = (char)keys;
				SetSearchString( m_searchString + c );
			}
			else if( keys == Keys.Back )
			{
				if( m_searchString.Length > 0 )
					SetSearchString( m_searchString.Substring( 0, m_searchString.Length - 1 ) );
			}
		}

		private void SetSearchString( string str )
		{
			m_searchString = str.ToLower();
			m_taskGrid = new TaskGrid( m_state, m_searchString );
			PositionWindow();
			m_selectedItem = (m_taskGrid.MruTasks.Length > 0) ? m_taskGrid.MruTasks[0] : null;
			DebugEvent.Record( $"Selected item is {m_selectedItem?.TaskName}" );
			Invalidate();
		}

		/// <summary>
		/// Keyboard input handler. Note that this is only used on UI Test mode;
		/// normally our keyboard input comes from the keyboard hook.
		/// </summary>
		protected override void OnKeyDown( KeyEventArgs e )
		{
			base.OnKeyDown( e );
			DebugEvent.Record( $"OnKeyDown: {e.KeyCode}" );
			if( !Program.UiTestMode )
				return;
			switch( e.KeyCode )
			{
			case Keys.Left:
				DoStandardSwitch( KeyboardHook.SwitchCommand.MoveLeft );
				break;
			case Keys.Right:
				DoStandardSwitch( KeyboardHook.SwitchCommand.MoveRight );
				break;
			case Keys.Up:
				DoStandardSwitch( KeyboardHook.SwitchCommand.MoveUp );
				break;
			case Keys.Down:
				DoStandardSwitch( KeyboardHook.SwitchCommand.MoveDown );
				break;
			case Keys.Enter:
				m_selectedItem.SwitchTo();
				break;
			case Keys.Escape:
				Close();
				break;
			case Keys.F5:
				RebuildTaskGrid();
				Invalidate();
				break;
			default:
				DoKeyboardSearch( e.KeyCode );
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
		private SystemState RebuildTaskGrid()
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

			// Setup our window grid
			m_taskGrid = new TaskGrid( state, m_searchString );
			m_selectedItem = m_taskGrid.MruTasks[0];
			PositionWindow();

			// I don't know if this is just an issue with mstsc.exe or something about all full-screen windows,
			// but if you minimize a full-screen RDP session it stays at the top of the z-order even though it's
			// not visible. The built-in Windows alt-tab swicher somehow gets around this but Alt+Tab Terminator
			// sees the same weird view we do. I can't figure out a way around it so we'll just advance past it
			// when this happens.
			if( WindowTools.IsIconic( state.WindowsByZOrder[0].WindowHandle ) )
			{
				AdjustSelection( 0, 1 );
			}

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

			// draw the search text, if any
			if( m_searchString != null )
			{
				string text = "Seach for: " + m_searchString;
				e.Graphics.FillRectangle( Brushes.White, m_taskGrid.TextSearchHeader );
				e.Graphics.DrawRectangle( Pens.Black, m_taskGrid.TextSearchHeader );
				TextRenderer.DrawText( e.Graphics, text, m_theme.TileText, m_taskGrid.TextSearchHeader,
					m_theme.TileLabelColor, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter );
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
			DrawWindowIcon( g, task, iconSize, contentArea );

			// draw the window title
			Rectangle textArea = contentArea;
			int textOffset = iconSize.Width;
			textArea.X += textOffset;
			textArea.Width -= textOffset;
			Color textColor = isSelection ? m_theme.ActiveTileLabelColor : m_theme.TileLabelColor;
			string text = task.TaskName;

			if( !string.IsNullOrEmpty( m_searchString ) )
			{
				HighlightSearchText( g, text, textArea, isSelection );
			}

			TextRenderer.DrawText( g, text, m_theme.TileText, textArea, textColor,
				TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter );
		}

		private void DrawWindowIcon( Graphics g, TaskItem task, Size iconSize, Rectangle contentArea )
		{
			IDisposable icon = GetIcon( task );
			Rectangle iconRect = new Rectangle( contentArea.Location, iconSize );
			if( icon is Icon )
			{
				try
				{
					g.DrawIcon( (Icon)icon, iconRect );
				}
				catch( System.DivideByZeroException )
				{
					// this seems to happen sometimes, and I don't understand why
				}
			}
			else if( icon is Image )
			{
				try
				{
					g.DrawImage( (Image)icon, iconRect );
				}
				catch( System.DivideByZeroException )
				{
					// this seems to happen sometimes, and I don't understand why
				}
			}
		}

		private void HighlightSearchText( Graphics g, string text, Rectangle textArea, bool isSelection )
		{
			// Find where the search text appears in this window's title
			int index = text.ToLower().IndexOf( m_searchString.ToLower() );
			CharacterRange[] ranges = new CharacterRange[3];
			ranges[0].First = 0;
			ranges[0].Length = index;
			ranges[1].First = index;
			ranges[1].Length = m_searchString.Length;
			ranges[2].First = index + m_searchString.Length;
			ranges[2].Length = text.Length - ranges[2].First;
			StringFormat fmt = new StringFormat();
			fmt.LineAlignment = StringAlignment.Center;
			fmt.Trimming = StringTrimming.EllipsisCharacter;
			fmt.SetMeasurableCharacterRanges( ranges );

			// Highlight the search text in the window's title, before drawing it
			var regions = g.MeasureCharacterRanges( text, m_theme.TileText, textArea, fmt );
			for( int i = 1; i < 2; i++ )
			{
				var boundsf = regions[i].GetBounds( g );
				Rectangle bounds = new Rectangle(
					(int)Math.Round( boundsf.Left ), (int)Math.Round( boundsf.Top ),
					(int)Math.Ceiling( boundsf.Width ), (int)Math.Ceiling( boundsf.Height ) );
				g.FillRectangle( isSelection ? Brushes.Gray : Brushes.White, boundsf );
			}
		}

		private IDisposable GetIcon( TaskItem task )
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

		private SystemState RefreshSystemState()
		{
			SystemState state;
			using( Utilities.TimedBlock( "DisplaySwitcher->RefreshSystemState" ) )
				state = new SystemState( m_state );
			Interlocked.Exchange( ref m_state, state );
			return state;
		}

		private void InstallKeyboardHook()
		{
			// this gets its own thread because the keyboard hook affects all typing on the system,
			// so we want to make that as responsive as possible.
			m_kbHook = new KeyboardHook();
			var thread = new Thread( () =>
			{
				m_kbHook.HookSwitcherKey( this, DoStandardSwitch, DoKeyboardSearch );
				Application.Run();
			} );
			thread.Start();
		}

		/// <summary>
		/// Starts a timer to periodically force a refresh of certain cached information.
		/// </summary>
		private void StartRefreshTimer()
		{
			var refresh = new System.Windows.Forms.Timer();
			refresh.Interval = (int)TimeSpan.FromSeconds( 5 ).TotalMilliseconds;
			refresh.Tick += refreshTimer_Tick;
			refresh.Start();
		}

		private void refreshTimer_Tick( object sender, EventArgs e )
		{
			// Force a refresh of the various caches by creating a state with no previous state.
			using( Utilities.TimedBlock( "RefreshTimer-> new SystemState" ) )
				m_state = new SystemState( lastKnown: null );

			// Occasionally do a more complete refresh.
			++m_refreshCount;
			if( m_refreshCount % 10 == 0 )
				WindowTools.ClearCaches();
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

			ToolStripItem debugCmd = menu.Items.Add( "Show &Debug Console" );
			debugCmd.Click += ( s, a ) => ShowDebugWindow();

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

		private void ShowDebugWindow()
		{
			if( m_debugWnd == null )
				m_debugWnd = new DebugInfoWnd();
			m_debugWnd.Show();
		}

		private DebugInfoWnd m_debugWnd;
	}
}
