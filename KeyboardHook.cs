using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace taskSwitch2
{
	/// <summary>
	/// Handles keyboard input so we can catch Alt+Tab and respond to it.
	/// </summary>
	class KeyboardHook
	{
		public void HookSwitcherKey( Control targetWindow, Action<SwitchCommand> onSwitch )
		{
			m_targetWindow = targetWindow;
			m_triggerKey = Program.MainSwitcherKey;
			m_onSwitch = onSwitch;
			InstallHook();
		}

		/// <summary>
		/// Notifies the hook that the switcher window has been hidden, so it stops responding to
		/// switch-related input.
		/// </summary>
		public void SwitcherClosed()
		{
			m_isSwitching = false;
		}

		/// <summary>
		/// The commands which can be raised by the keyboard.
		/// </summary>
		public enum SwitchCommand
		{
			SwitchForward,
			SwitchReverse,
			MoveLeft,
			MoveRight,
			MoveUp,
			MoveDown,
			CommitSwitch,
			CancelSwitch,
		}

		private Control m_targetWindow;
		private Keys m_triggerKey;
		private Action<SwitchCommand> m_onSwitch;
		private bool m_isSwitching;

		private void InstallHook()
		{
			m_hookProc = KeyboardHookProc;
			m_hhook = SetWindowsHookEx( HookType.KeyboardLL, m_hookProc,
				GetModuleHandle( null ), thread: 0 );
			if( m_hhook == IntPtr.Zero )
				throw new System.InvalidOperationException( "SetWindowsHookEx failed: " + Marshal.GetLastWin32Error() );
		}

		private int KeyboardHookProc( int code, IntPtr wp, IntPtr lp )
		{
			if( code < 0 )
				return CallNextHookEx( m_hhook, code, wp, lp );

			KbdLLHookStruct info;
			unsafe
			{
				KbdLLHookStruct* pinfo = (KbdLLHookStruct*)lp.ToPointer();
				info = *pinfo;
			}

			// We don't want to process injected input.
			if( 0 != (info.flags & LlkFlags.Injected) )
				return CallNextHookEx( m_hhook, code, wp, lp );

//			Debug.WriteLine( "vk = {0}, flags = {1}", info.key, info.flags );

			if( info.IsPress )
			{
				if( info.key == m_triggerKey )
				{
					bool altIsDown = IsPressed( Keys.Menu ) || m_isSwitching;
					if( altIsDown && IsPressed( Keys.ShiftKey ) )
					{
						m_isSwitching = true;
						RaiseSwitchCommand( SwitchCommand.SwitchReverse );
						return 1;
					}
					else if( altIsDown )
					{
						m_isSwitching = true;
						RaiseSwitchCommand( SwitchCommand.SwitchForward );
						return 1;
					}
				}
				else if( m_isSwitching )
				{
					switch( info.key )
					{
					case Keys.Left:
						RaiseSwitchCommand( SwitchCommand.MoveLeft );
						return 1;
					case Keys.Right:
						RaiseSwitchCommand( SwitchCommand.MoveRight );
						return 1;
					case Keys.Up:
						RaiseSwitchCommand( SwitchCommand.MoveUp );
						return 1;
					case Keys.Down:
						RaiseSwitchCommand( SwitchCommand.MoveDown );
						return 1;
					case Keys.Enter:
						RaiseSwitchCommand( SwitchCommand.CommitSwitch );
						m_isSwitching = false;
						return 1;
					case Keys.Escape:
						RaiseSwitchCommand( SwitchCommand.CancelSwitch );
						m_isSwitching = false;
						return 1;
					}
				}
			}
			else // key up
			{
				if( IsMenuKey( info.key ) )
				{
					if( m_isSwitching )
					{
						RaiseSwitchCommand( SwitchCommand.CommitSwitch );
						m_isSwitching = false;

						// Originally we were returning here, and thus eating this keystroke. This
						// confuses some apps (like Visual Studio), so apparently it's the wrong
						// thing to do.
					}
				}
			}

			if( m_isSwitching )
				return 1; // we swallow all input when switching, so we don't confuse other apps
			else
				return CallNextHookEx( m_hhook, code, wp, lp );
		}

		private void RaiseSwitchCommand( SwitchCommand cmd )
		{
			m_targetWindow.BeginInvoke( new Action( () => m_onSwitch( cmd ) ) );
		}

		private static bool IsMenuKey( Keys key )
		{
			return (key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu);
		}

		private static bool IsPressed( Keys key )
		{
			ushort state = GetAsyncKeyState( key );
			return (state & 0x8000) != 0;
		}

		private HookProc m_hookProc; // to keep delegate alive
		private IntPtr m_hhook; 

		private enum HookType
		{
			KeyboardLL = 13,
		}

		[Flags]
		private enum LlkFlags : uint
		{
			Injected = 0x10,
			AltDown = 0x20,
			IsRelease = 0x80,
		}

		private delegate int HookProc( int code, IntPtr wp, IntPtr lp );

		[StructLayout( LayoutKind.Sequential, Pack = 1 )]
		private struct KbdLLHookStruct
		{
			public Keys key;
			public uint scanCode;
			public LlkFlags flags;
			public uint time;
			public UIntPtr extra;

			public bool IsPress
			{
				get { return (flags & LlkFlags.IsRelease) == 0; }
			}
		}

		[DllImport( "user32.dll" )]
		private static extern IntPtr SetWindowsHookEx( HookType type, HookProc hook, IntPtr instance, uint thread );

		[DllImport( "user32.dll" )]
		private static extern int CallNextHookEx( IntPtr hhook, int code, IntPtr wp, IntPtr lp );

		[DllImport( "user32.dll" )]
		private static extern ushort GetAsyncKeyState( Keys key );

		[DllImport( "kernel32.dll" )]
		private static extern IntPtr GetModuleHandle( string module );
	}
}
