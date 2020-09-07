
using System.Drawing;

namespace taskSwitch2
{
	/// <summary>
	/// Determines the visual appearance of the switcher window.
	/// </summary>
	public class Theme
	{
		public Theme()
		{
			this.BackgroundColor = Color.FromArgb( 245, 245, 245 );
			this.BorderWidth = 1;
			this.BorderColor = Color.Gray;

			this.MruBackColor = Color.FromArgb( 230, 230, 230 );

			this.TileBorderWidth = 2;
			this.TileText = new Font( "Tahoma", 9 );

			this.ActiveTileBorderColor = Color.RoyalBlue;
			this.ActiveTileBackColor = Color.RoyalBlue;
			this.ActiveTileLabelColor = Color.White;

			this.TileBorderColor = Color.Transparent;
			this.TileBackColor = Color.Transparent;
			this.TileLabelColor = Color.Black;
		}

		public Color BackgroundColor { get; set; }
		public int BorderWidth { get; set; }
		public Color BorderColor { get; set; }

		public Color MruBackColor { get; set; }

		public int TileBorderWidth { get; set; }
		public Font TileText { get; set; }

		public Color ActiveTileBorderColor { get; set; }
		public Color ActiveTileBackColor { get; set; }
		public Color ActiveTileLabelColor { get; set; }

		public Color TileBorderColor { get; set; }
		public Color TileBackColor { get; set; }
		public Color TileLabelColor { get; set; }
	}

}
