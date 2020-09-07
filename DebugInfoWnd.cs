using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using System.Windows.Forms;

namespace taskSwitch2
{
	/// <summary>
	/// Displays debug information from the switcher.
	/// </summary>
	partial class DebugInfoWnd : Form
	{
		public DebugInfoWnd()
		{
			InitializeComponent();
			RefreshMessages();
			m_timer = new System.Windows.Forms.Timer();
			m_timer.Tick += timer_Tick;
			m_timer.Interval = 500;
			m_timer.Start();
		}

		private readonly System.Windows.Forms.Timer m_timer;

		private void timer_Tick( object sender, EventArgs e )
		{
			RefreshMessages();
		}

		protected override void OnFormClosing( FormClosingEventArgs e )
		{
			if( e.CloseReason == CloseReason.UserClosing )
			{
				Hide();
				e.Cancel = true;
			}
			base.OnFormClosing( e );
		}

		protected override void OnShown( EventArgs e )
		{
			base.OnShown( e );
			RefreshMessages();
		}

		protected override void OnKeyDown( KeyEventArgs e )
		{
			base.OnKeyDown( e );
			if( e.KeyCode == Keys.F5 )
			{
				RefreshMessages();
				e.Handled = true;
			}
		}

		private void RefreshMessages()
		{
			if( !this.IsHandleCreated || this.IsDisposed )
				return;
			DebugEvent[] evt = DebugEvent.Recent.ToArray();

			StringBuilder sb = new StringBuilder();
			for( int i = 0; i < evt.Length; i++ )
			{
				if( i > 0 )
				{
					var delta = evt[i].Timestamp - evt[i - 1].Timestamp;
					if( delta.TotalMilliseconds >= 1000 )
						sb.AppendLine( "-----------------------------" );
				}

				var e = evt[i];
				sb.AppendFormat( "{0:HH:mm:ss.fffff}: {1}", e.Timestamp, e.Message.Replace( "\n", "\r\n" ) );
				sb.Append( "\r\n" );
			}

			m_content.Text = sb.ToString();
			m_content.Select( sb.Length, 0 );
			m_content.ScrollToCaret();
		}
	}
}
