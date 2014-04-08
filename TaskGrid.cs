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

		public TaskGrid( SystemState state )
		{
			PartitionTasks( state );
			ArrangeElements();
		}

		/// <summary>
		/// The total size required for the switcher.
		/// </summary>
		public Size TotalSize { get; private set; }

		/// <summary>
		/// The area of the MRU tasks.
		/// </summary>
		public Rectangle MruArea { get; private set; }

		/// <summary>
		/// The area of the left side collection of tasks.
		/// </summary>
		public Rectangle LeftSideArea { get; private set; }

		/// <summary>
		/// The area of the right side collection of tasks.
		/// </summary>
		public Rectangle RightSideArea { get; private set; }

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
		private void PartitionTasks( SystemState state )
		{
			this.MruTasks = state.WindowsByZOrder
				.Where( x => WindowTools.IsWindow( x ) )
				.Take( MruWindowCount )
				.ToArray();

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
						if( group.Pattern.IsMatch( btn.TaskName ) )
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
			List<TaskItem[]> rightSide = new List<TaskItem[]>();
			IEnumerable<TaskItem> rightRemaining = rightSideFlat;
			while( rightRemaining.Any() )
			{
				rightSide.Add( rightRemaining.Take( MaxRightSideColumnHeight ).ToArray() );
				rightRemaining = rightRemaining.Skip( MaxRightSideColumnHeight );
			}

			var leftSideFlat = leftSide.Select( x => MergeLists( x ) ).Where( x => x.Length > 0 ).ToList();
			m_leftSideTasks = leftSideFlat.ToArray();
			m_rightSideTasks = rightSide.ToArray();

			// build the final grid. the left and right side columns all start with an empty item,
			// so that the current active task is on a row by itself.
			List<List<TaskItem>> grid = new List<List<TaskItem>>();
			leftSideFlat.ForEach( x => grid.Add( new List<TaskItem>( x ) ) );
			int mruIndex = grid.Count;
			grid.Add( new List<TaskItem>( this.MruTasks ) );
			rightSide.ForEach( x => grid.Add( new List<TaskItem>( x ) ) );
			for( int i = 0; i < grid.Count; i++ )
			{
				if( i != mruIndex )
					grid[i].Insert( 0, null );
			}
			this.TasksByColumn = grid.Select( x => x.ToArray() ).ToArray();

			// build the flat task list
			List<TaskItem> flat = new List<TaskItem>();
			flat.AddRange( this.MruTasks );
			leftSideFlat.ForEach( x => flat.AddRange( x ) );
			rightSide.ForEach( x => flat.AddRange( x ) );
			this.TasksFlat = flat.ToArray();
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
		private void ArrangeElements()
		{
			int mruHeight = this.MruTasks.Length;
			mruHeight *= Metrics.AppTileHeight + AppTileBetweenGutter;
			mruHeight -= AppTileBetweenGutter; // we don't need a gutter after the last one

			int leftSideHeight = ComputeOneSideHeight( m_leftSideTasks );
			int rightSideHeight = ComputeOneSideHeight( m_rightSideTasks );

			int innerHeight = Math.Max( mruHeight, Math.Max( leftSideHeight, rightSideHeight ) );
			int totalHeight = innerHeight + (MajorAreaGutter * 2) + (OuterGutter * 2);

			Point upperLeft = new Point( OuterGutter, OuterGutter );

			// lay out the left side columns
			if( m_leftSideTasks.Length > 0 )
				this.LeftSideArea = ArrangeOneSide( upperLeft, leftSideHeight, m_leftSideTasks );
			else
				this.LeftSideArea = new Rectangle();
			upperLeft.X += this.LeftSideArea.Width;

			// lay out the MRU list
			this.MruArea = ArrangeMruList( upperLeft, mruHeight );
			upperLeft.X += this.MruArea.Width;

			// lay out the right side columns
			this.RightSideArea = ArrangeOneSide( upperLeft, rightSideHeight, m_rightSideTasks );
			upperLeft.X += this.RightSideArea.Width;

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

		private Rectangle ArrangeOneSide( Point upperLeft, int totalHeight, TaskItem[][] tasks )
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
			totalHeight += (MajorAreaGutter * 2);
			return new Rectangle( upperLeft, new Size( totalWidth, totalHeight ) );
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
					string pattern = xgroup.Element( "pattern" ).Value;
					group.Pattern = new Regex( pattern, RegexOptions.IgnoreCase );
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
			/// The pattern that identifies apps in this group.
			/// </summary>
			public Regex Pattern { get; set; }
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
		private const int MruWindowCount = 11;

		/// <summary>
		/// The maximum number of items to put in one of the right-side columns.
		/// </summary>
		private const int MaxRightSideColumnHeight = 9;

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
