using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace taskSwitch2
{
	class Metrics
	{
		/// <summary>
		/// The icon size we prefer to use.
		/// </summary>
		public const int PreferredIconSize = 16;

		/// <summary>
		/// The amount of space to leave between a tile's icon/text and its outer border.
		/// </summary>
		public const int AppTileInsideGutter = 5;

		/// <summary>
		/// The total width to use for app tiles.
		/// </summary>
		public const int AppTileWidth = 240;

		/// <summary>
		/// The height of one application tile, in pixels.
		/// </summary>
		public static int AppTileHeight
		{
			get { return PreferredIconSize + (AppTileInsideGutter * 2); }
		}
	}
}
