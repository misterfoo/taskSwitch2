using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace taskSwitch2
{
	/// <summary>
	/// Describs some "event" which might be interesting to someone debugging the switcher.
	/// </summary>
	class DebugEvent
	{
		/// <summary>
		/// The timestamp of the event.
		/// </summary>
		public DateTimeOffset Timestamp { get; set; }

		/// <summary>
		/// The event content.
		/// </summary>
		public string Message { get; set; }

		/// <summary>
		/// Retrieves the most recent list of N debug events, oldest to newest.
		/// </summary>
		public static IEnumerable<DebugEvent> Recent
		{
			get { return s_recent; }
		}
		private static readonly ConcurrentQueue<DebugEvent> s_recent = new ConcurrentQueue<DebugEvent>();

		/// <summary>
		/// Raised when one or more events have been added to the list of recent events.
		/// </summary>
		public static event System.EventHandler EventsAdded;

		/// <summary>
		/// Adds a debug event to the list of events.
		/// </summary>
		public static void Record( string msgFomat, params object[] args )
		{
			string msg = (args.Length > 0) ? string.Format( msgFomat, args ) : msgFomat;
			Debug.WriteLine( msg );
#if DEBUG
			ThreadPool.QueueUserWorkItem( _ => Debug.WriteLine( msg ) );
#endif
			s_recent.Enqueue( new DebugEvent { Timestamp = DateTimeOffset.Now, Message = msg } );
			while( s_recent.Count > 100 )
			{
				DebugEvent junk;
				s_recent.TryDequeue( out junk );
			}
			var eh = EventsAdded;
			if( eh != null )
				eh( null, EventArgs.Empty );
		}
	}
}
