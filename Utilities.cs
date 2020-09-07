using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace taskSwitch2
{
	static class Utilities
	{
		public static IDisposable TimedBlock( string context )
		{
			return new StopwatchDisposabe( context );
		}

		private class StopwatchDisposabe : IDisposable
		{
			public StopwatchDisposabe( string context )
			{
				m_timer = Stopwatch.StartNew();
				m_context = context;
			}

			private readonly Stopwatch m_timer;
			private readonly string m_context;

			public void Dispose()
			{
				m_timer.Stop();
				DebugEvent.Record( $"{m_context} took {m_timer.ElapsedMilliseconds}" );
			}
		}

	}
}
