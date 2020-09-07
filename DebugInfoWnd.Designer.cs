namespace taskSwitch2
{
	partial class DebugInfoWnd
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose( bool disposing )
		{
			if( disposing && (components != null) )
			{
				components.Dispose();
			}
			base.Dispose( disposing );
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.m_content = new System.Windows.Forms.TextBox();
			this.SuspendLayout();
			// 
			// m_content
			// 
			this.m_content.Dock = System.Windows.Forms.DockStyle.Fill;
			this.m_content.Location = new System.Drawing.Point(0, 0);
			this.m_content.Multiline = true;
			this.m_content.Name = "m_content";
			this.m_content.ReadOnly = true;
			this.m_content.ScrollBars = System.Windows.Forms.ScrollBars.Both;
			this.m_content.Size = new System.Drawing.Size(847, 311);
			this.m_content.TabIndex = 0;
			// 
			// DebugInfoWnd
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(847, 311);
			this.Controls.Add(this.m_content);
			this.KeyPreview = true;
			this.Name = "DebugInfoWnd";
			this.Text = "Debug";
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.TextBox m_content;
	}
}