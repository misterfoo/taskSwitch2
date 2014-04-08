using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Linq;

//
// NOTE: see the readme file for important information about debugging and installing this app
//
namespace taskSwitch2
{
	static class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main( string[] args )
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault( false );

			if( !ParseCommandLine( args ) )
			{
				MessageBox.Show( "Invalid command line." );
				return;
			}

			if( Program.UiTestMode )
			{
				Application.Run( new SwitcherForm() );
			}
			else
			{
				CheckManifest();
				Application.Run( new RunContext() );
			}
		}

		/// <summary>
		/// The key that should be used to trigger app switching (Alt+[key])
		/// </summary>
		public static Keys MainSwitcherKey = Keys.Tab;

		/// <summary>
		/// Specifies that we are in UI test mode.
		/// </summary>
		public static bool UiTestMode;

		private static bool ParseCommandLine( string[] args )
		{
			// Find and process command-line switches (expected form is /name[=value])
			foreach( string arg in args )
			{
				var match = Regex.Match( arg, @"^/(\w+)(?:=(.*))?$" );
				if( !match.Success )
					return false;
				string name = match.Groups[1].Value;
				string value = match.Groups[2].Value;
				switch( name.ToLowerInvariant() )
				{
				case "uitest":
					if( value != string.Empty )
						return false;
					Program.UiTestMode = true;
					break;
				case "switcherkey":
					if( value == string.Empty )
						return false;
					const bool ignoreCase = true;
					if( !Enum.TryParse( value, ignoreCase, out Program.MainSwitcherKey ) )
						return false;
					break;
				default:
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// A specialized implementation of ApplicationContext which does not show the form
		/// when the application starts.
		/// </summary>
		private class RunContext : ApplicationContext
		{
			public RunContext()
			{
				m_form = new SwitcherForm();
				m_form.Visible = false;
				m_form.FormClosed += form_FormClosed;
			}

			private void form_FormClosed( object sender, FormClosedEventArgs e )
			{
				this.ExitThread();
			}

			private SwitcherForm m_form;
		}

		private static void CheckManifest()
		{
			byte[] bytes = GetRawManifest();
			XDocument doc;
			using( MemoryStream stream = new MemoryStream( bytes ) )
				doc = XDocument.Load( stream );
			XNamespace asmv1 = "urn:schemas-microsoft-com:asm.v1";
			XNamespace asmv2 = "urn:schemas-microsoft-com:asm.v2";
			XNamespace asmv3 = "urn:schemas-microsoft-com:asm.v3";
			XAttribute xUiAccess = doc.Element( asmv1 + "assembly" )
			    .Element( asmv2 + "trustInfo" )
			    .Element( asmv2 + "security" )
			    .Element( asmv3 + "requestedPrivileges" )
			    .Element( asmv3 + "requestedExecutionLevel" )
			    .Attribute( "uiAccess" );
			bool uiAccess;
			if( !bool.TryParse( xUiAccess.Value, out uiAccess ) )
			    uiAccess = false;
			if( !uiAccess )
			{
				MessageBox.Show( "Warning: the application manifest does not specify uiAccess=true, so task swiching will not work reliably.",
					"Task Switch", MessageBoxButtons.OK, MessageBoxIcon.Warning );
			}
		}

		private static byte[] GetRawManifest()
		{
			IntPtr module = GetModuleHandle( null );
			IntPtr resInfo = FindResource( module, (IntPtr)1, (IntPtr)ResType.Manifest );
			uint size = SizeofResource( module, resInfo );
			IntPtr resData = LoadResource( module, resInfo );
			IntPtr rawData = LockResource( resData );
			byte[] bytes = new byte[size];
			Marshal.Copy( rawData, bytes, startIndex: 0, length: (int)size );
			return bytes;
		}

		[DllImport( "kernel32.dll" )]
		static extern IntPtr GetModuleHandle( string module );

		[DllImport( "kernel32.dll", SetLastError = true )]
		static extern IntPtr FindResource( IntPtr hmodule, IntPtr name, IntPtr type );

		[DllImport( "kernel32.dll" )]
		static extern uint SizeofResource( IntPtr hmodule, IntPtr hresource );

		[DllImport( "kernel32.dll" )]
		static extern IntPtr LoadResource( IntPtr hmodule, IntPtr hresource );

		[DllImport( "kernel32.dll" )]
		static extern IntPtr LockResource( IntPtr resourceData );

		[DllImport( "kernel32.dll" )]
		static extern bool EnumResourceNames( IntPtr hmodule, IntPtr type, EnumResNameProcDelegate enumFunc, IntPtr lParam );

		delegate bool EnumResNameProcDelegate( IntPtr hmodule, IntPtr type, IntPtr name, IntPtr lParam );

		enum ResType
		{
			Icon = 3,
			Manifest = 24
		}
	}
}
