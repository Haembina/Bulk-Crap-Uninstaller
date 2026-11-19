/*
    Copyright (c) 2017 Marcin Szeniak (https://github.com/Klocman/)
    Apache License Version 2.0
*/

using System.Drawing;
using System.Windows.Forms;

namespace Klocman.Controls
{
    public sealed partial class LineSeparator : UserControl
    {
        #region Constructors

        public LineSeparator()
        {
            InitializeComponent();

            Paint += LineSeparator_Paint;

            MaximumSize = new Size(2000, 2);

            MinimumSize = new Size(0, 2);

            Width = 350;
        }

        #endregion Constructors

        #region Methods

        private void LineSeparator_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var bg = Parent?.BackColor ?? BackColor;
            var isDark = bg.R * 299 + bg.G * 587 + bg.B * 114 < 145000;
            if (isDark)
            {
                using var topPen = new Pen(Color.FromArgb(72, 72, 78));
                using var bottomPen = new Pen(Color.FromArgb(40, 40, 44));
                g.DrawLine(topPen, new Point(0, 0), new Point(Width, 0));
                g.DrawLine(bottomPen, new Point(0, 1), new Point(Width, 1));
            }
            else
            {
                g.DrawLine(Pens.DarkGray, new Point(0, 0), new Point(Width, 0));
                g.DrawLine(Pens.White, new Point(0, 1), new Point(Width, 1));
            }
        }

        #endregion Methods
    }
}