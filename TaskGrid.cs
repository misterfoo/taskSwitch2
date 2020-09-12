using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace taskSwitch2
{
	/// <summary>
	/// Handles the arrangement of the tasks in the switcher window.
	/// </summary>
	class TaskGrid
	{
		static TaskGrid()
		{
			LoadLeftSideColumns();
		}

		public TaskGrid( SystemState state, string textFilter )
		{
			PartitionTasks( state, textFilter );
			ArrangeElements( textFilter );
		}

		/// <summary>
		/// The total size required for the switcher.
		/// </summary>
		public Size TotalSize { get; private set; }

		/// <summary>
		/// The area in which to draw the header for switch-by-typing, if applicable.
		/// </summary>
		public Rectangle TextSearchHeader { get; private set; }

		/// <summary>
		/// The area of the MRU tasks.
		/// </summary>
		public Rectangle MruArea { get; private set; }

		/// <summary>
		/// Gets the grid of tasks in column-major order. Empty grid cells are represented by nulls.
		/// </summary>
		public TaskItem[][] TasksByColumn { get; private set; }

		/// <summary>
		/// Gets the list of tasks flat (unspecified) order.
		/// </summary>
		public TaskItem[] TasksFlat { get; private set; }

		/// <summary>
		/// Gets the list of MRU tasks, in usage order.
		/// </summary>
		public TaskItem[] MruTasks { get; private set; }

		/// <summary>
		/// Splits the current tasks into MRU, left side, and right side.
		/// </summary>
		private void PartitionTasks( SystemState state, string textFilter )
		{
			string textFilterLower = textFilter?.ToLower();
			this.MruTasks = state.WindowsByZOrder
				.Where( x => WindowTools.IsWindow( x ) )
				.Where( x => string.IsNullOrEmpty( textFilter ) || x.TaskName.ToLower().Contains( textFilterLower ) )
				.Take( MruWindowCount )
				.ToArray();

			// lay out the sides, which are empty if we have a filter
			List<TaskItem[]> leftSideColumns, rightSideColumns;
			if( textFilter == null )
				PartitionLeftAndRightSide( state, out leftSideColumns, out rightSideColumns );
			else
				leftSideColumns = rightSideColumns = new List<TaskItem[]>();
			m_leftSideTasks = leftSideColumns.ToArray();
			m_rightSideTasks = rightSideColumns.ToArray();

			// build the final grid. the left and right side columns all start with an empty item,
			// so that the current active task is on a row by itself.
			List<List<TaskItem>> grid = new List<List<TaskItem>>();
			leftSideColumns.ForEach( x => grid.Add( new List<TaskItem>( x ) ) );
			int mruIndex = grid.Count;
			grid.Add( new List<TaskItem>( this.MruTasks ) );
			rightSideColumns.ForEach( x => grid.Add( new List<TaskItem>( x ) ) );
			for( int i = 0; i < grid.Count; i++ )
			{
				if( i != mruIndex )
					grid[i].Insert( 0, null );
			}
			this.TasksByColumn = grid.Select( x => x.ToArray() ).ToArray();

			// build the flat task list
			List<TaskItem> flat = new List<TaskItem>();
			flat.AddRange( this.MruTasks );
			leftSideColumns.ForEach( x => flat.AddRange( x ) );
			rightSideColumns.ForEach( x => flat.AddRange( x ) );
			this.TasksFlat = flat.ToArray();
		}

		private void PartitionLeftAndRightSide( SystemState state, out List<TaskItem[]> leftSideColumns, out List<TaskItem[]> rightSideColumns )
		{
			// Partition the full set of tasks into left and right sides. 
			// For the left side the list is Column, then Group, then Task.

			var leftSide = new List<List<List<TaskItem>>>();
			foreach( var column in m_leftSideColumns )
			{
				var groups = new List<List<TaskItem>>();
				leftSide.Add( groups );
				foreach( var group in column.Groups )
					groups.Add( new List<TaskItem>() );
			}

			List<TaskItem> rightSideFlat = new List<TaskItem>();
			foreach( TaskbarButton btn in state.WindowsByTaskbarOrder )
			{
				if( btn.AssociatedWindow != null &&
					!WindowTools.IsWindow( btn.AssociatedWindow ) )
					continue;

				// does this belong in one of the left-side groups?
				bool isLeftSide = false;
				for( int iCol = 0; iCol < m_leftSideColumns.Count; iCol++ )
				{
					ColumnDefinition col = m_leftSideColumns[iCol];
					for( int iGroup = 0; iGroup < col.Groups.Count; iGroup++ )
					{
						ApplicationGroup group = col.Groups[iGroup];
						if( group.IsMatch( btn ) )
						{
							List<TaskItem> groupItems = leftSide[iCol][iGroup];
							if( groupItems.Count == 0 )
								btn.BeginGroup = true;
							else
								btn.BeginGroup = false;
							groupItems.Add( btn );
							isLeftSide = true;
							break;
						}
					}
				}

				if( !isLeftSide )
					rightSideFlat.Add( btn );
			}

			// split the right side into columns
			rightSideColumns = new List<TaskItem[]>();
			IEnumerable<TaskItem> rightRemaining = rightSideFlat;
			while( rightRemaining.Any() )
			{
				rightSideColumns.Add( rightRemaining.Take( MaxRightSideColumnHeight ).ToArray() );
				rightRemaining = rightRemaining.Skip( MaxRightSideColumnHeight );
			}

			// convert the left side temporary form into columns
			leftSideColumns = leftSide.Select( x => MergeLists( x ) ).Where( x => x.Length > 0 ).ToList();
		}

		private T[] MergeLists<T>( List<List<T>> lists )
		{
			List<T> final = new List<T>();
			lists.ForEach( x => final.AddRange( x ) );
			return final.ToArray();
		}

		/// <summary>
		/// Initializes the positions of the tasks.
		/// </summary>
		private void ArrangeElements( string textFilter )
		{
			int mruHeight = MruWindowCount;
			mruHeight *= Metrics.AppTileHeight + AppTileBetweenGutter;
			mruHeight -= AppTileBetweenGutter; // we don't need a gutter after the last one

			int leftSideHeight = ComputeOneSideHeight( m_leftSideTasks );
			int rightSideHeight = ComputeOneSideHeight( m_rightSideTasks );

			int innerHeight = Math.Max( mruHeight, Math.Max( leftSideHeight, rightSideHeight ) );
			int totalHeight = innerHeight + (MajorAreaGutter * 2) + (OuterGutter * 2);

			Point upperLeft = new Point( OuterGutter, OuterGutter );

			// lay out the left side columns
			int leftSideWidth;
			if( m_leftSideTasks.Length > 0 )
				leftSideWidth = ArrangeOneSide( upperLeft, leftSideHeight, m_leftSideTasks );
			else
				leftSideWidth = MajorAreaGutter * 4; // mainly relevant in TypeSearch mode
			upperLeft.X += leftSideWidth;

			// leave space for the text search header, which goes just above the MRU area
			if( textFilter != null )
			{
				Point location = upperLeft;
				location.X += MajorAreaGutter;
				this.TextSearchHeader = new Rectangle( location,
					new Size( Metrics.AppTileWidth, Metrics.AppTileHeight ) );
				int height = this.TextSearchHeader.Height + MajorAreaGutter;
				upperLeft.Y += height;
				totalHeight += height;
			}

			// lay out the MRU list
			this.MruArea = ArrangeMruList( upperLeft, mruHeight );
			upperLeft.X += this.MruArea.Width;

			// lay out the right side columns
			int rightSideWidth;
			if( m_rightSideTasks.Length > 0 )
				rightSideWidth = ArrangeOneSide( upperLeft, rightSideHeight, m_rightSideTasks );
			else
				rightSideWidth = MajorAreaGutter * 4; // mainly relevant in TypeSearch mode
			upperLeft.X += rightSideWidth;

			upperLeft.X += OuterGutter;

			this.TotalSize = new Size( upperLeft.X, totalHeight );
		}

		private Rectangle ArrangeMruList( Point upperLeft, int totalHeight )
		{
			Point startPoint = upperLeft + new Size( MajorAreaGutter, MajorAreaGutter );
			Point scratchPos = startPoint;

			foreach( TaskItem task in this.MruTasks )
			{
				task.Area = new Rectangle( scratchPos, new Size( Metrics.AppTileWidth, Metrics.AppTileHeight ) );
				scratchPos.Y += task.Area.Height + AppTileBetweenGutter;
			}

			int totalWidth = Metrics.AppTileWidth + (MajorAreaGutter * 2);
			totalHeight += (MajorAreaGutter * 2);
			return new Rectangle( upperLeft, new Size( totalWidth, totalHeight ) );
		}

		private int ComputeOneSideHeight( TaskItem[][] tasks )
		{
			if( tasks.Length == 0 )
				return 0;
			int height = tasks.Max( x => x.Length );
			height += 1; // left and right sides have one blank row at the top
			height *= Metrics.AppTileHeight + AppTileBetweenGutter;
			height -= AppTileBetweenGutter; // we don't need a gutter after the last one

			// tasks that begin a group get extra space, other than the first group in the column
			height += tasks.Max( x => Math.Max( x.Count( t => t.BeginGroup ) - 1, 0 ) ) * AppTileGroupGutter;

			return height;
		}

		/// <summary>
		/// Arranges one side of the switcher display, returning the width of the used area.
		/// </summary>
		private int ArrangeOneSide( Point upperLeft, int totalHeight, TaskItem[][] tasks )
		{
			Point startPoint = upperLeft + new Size( MajorAreaGutter, MajorAreaGutter );
			startPoint.Y += Metrics.AppTileHeight + AppTileBetweenGutter; // leave first row blank, for first MRU item
			Point scratchPos = startPoint;
			foreach( TaskItem[] column in tasks )
			{
				int index = 0;
				foreach( TaskItem task in column )
				{
					if( index++ > 0 && task.BeginGroup )
						scratchPos.Y += AppTileGroupGutter;
					task.Area = new Rectangle( scratchPos, new Size( Metrics.AppTileWidth, Metrics.AppTileHeight ) );
					scratchPos.Y += task.Area.Height + AppTileBetweenGutter;
				}
				scratchPos.X += Metrics.AppTileWidth + AppTileBetweenGutter;
				scratchPos.Y = startPoint.Y;
			}
			scratchPos.X -= AppTileBetweenGutter;
			int totalWidth = (scratchPos.X - startPoint.X) + (MajorAreaGutter * 2);
			return totalWidth;
		}

		private static void LoadLeftSideColumns()
		{
			m_leftSideColumns = new List<ColumnDefinition>();
			string exeDir = Path.GetDirectoryName( System.Windows.Forms.Application.ExecutablePath );
			string configFile = Path.Combine( exeDir, "taskSwitch2.config.xml" );
			if( !File.Exists( configFile ) )
				return;
			XDocument doc = XDocument.Load( configFile );
			XElement root = doc.Element( "config" ).Element( "leftSide" );
			foreach( XElement xcolumn in root.Elements( "column" ) )
			{
				ColumnDefinition column = new ColumnDefinition();
				m_leftSideColumns.Add( column );
				foreach( XElement xgroup in xcolumn.Elements( "group" ) )
				{
					ApplicationGroup group = new ApplicationGroup();
					column.Groups.Add( group );
					string pattern = xgroup.Element( "titlePattern" )?.Value;
					if( pattern != null )
						group.TitlePattern = new Regex( pattern, RegexOptions.IgnoreCase );
					pattern = xgroup.Element( "classPattern" )?.Value;
					if( pattern != null )
						group.ClassPattern = new Regex( pattern, RegexOptions.IgnoreCase );
				}
			}
		}

		private static List<ColumnDefinition> m_leftSideColumns;

		private class ColumnDefinition
		{
			public ColumnDefinition()
			{
				this.Groups = new List<ApplicationGroup>();
			}

			/// <summary>
			/// The groups that should go in this column.
			/// </summary>
			public List<ApplicationGroup> Groups { get; private set; }
		}

		private class ApplicationGroup
		{
			/// <summary>
			/// The pattern that identifies apps in this group, based on window title.
			/// </summary>
			public Regex TitlePattern { get; set; }

			/// <summary>
			/// The pattern that identifies apps in this group, based on window class.
			/// </summary>
			public Regex ClassPattern { get; set; }

			public bool IsMatch( TaskbarButton btn )
			{
				if( this.TitlePattern != null && this.TitlePattern.IsMatch( btn.TaskName ) )
					return true;

				string wndClass = btn.WindowClass;
				if( this.ClassPattern != null && wndClass != null && this.ClassPattern.IsMatch( wndClass ) )
					return true;

				return false;
			}

		}

		/// <summary>
		/// The left-side half of the full grid of tasks, in column-major order.
		/// </summary>
		private TaskItem[][] m_leftSideTasks;

		/// <summary>
		/// The right-side half of the full grid of tasks, in column-major order.
		/// </summary>
		private TaskItem[][] m_rightSideTasks;

		/// <summary>
		/// The number of windows to include in the MRU list.
		/// </summary>
		public const int MruWindowCount = 12;

		/// <summary>
		/// The maximum number of items to put in one of the right-side columns.
		/// </summary>
		private const int MaxRightSideColumnHeight = 10;

		/// <summary>
		/// The size of the gutter around the outer edge.
		/// </summary>
		private const int OuterGutter = AppTileBetweenGutter * 3;

		/// <summary>
		/// The size of the gutter around each of the major areas (left side, MRU, right side).
		/// </summary>
		private const int MajorAreaGutter = AppTileBetweenGutter * 3;

		/// <summary>
		/// The amount of space to leave between app tiles.
		/// </summary>
		private const int AppTileBetweenGutter = 3;

		/// <summary>
		/// The amount of space to leave between app groups.
		/// </summary>
		private const int AppTileGroupGutter = 20;
	}
}
