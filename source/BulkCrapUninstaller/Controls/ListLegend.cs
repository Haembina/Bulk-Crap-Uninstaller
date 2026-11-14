/*
    Copyright (c) 2017 Marcin Szeniak (https://github.com/Klocman/)
    Apache License Version 2.0
*/

using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using BulkCrapUninstaller.Functions.ApplicationList;
using BulkCrapUninstaller.Functions.Tools;
using Klocman.Forms.Tools;

namespace BulkCrapUninstaller.Controls
{
    [WindowStyleController.ControlStyle(false)]
    public partial class ListLegend : UserControl
    {
        public ListLegend()
        {
            InitializeComponent();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            Properties.Settings.Default.SettingBinder.Subscribe((x, y) => UpdateColors(), settings => settings.MiscColorblind, this);
            UpdateColors();
        }

        internal void UpdateColors()
        {
            var dark = AppTheme.IsDark;
            BackColor = dark ? Color.FromArgb(32, 32, 32) : (Enabled ? SystemColors.ControlLightLight : SystemColors.Control);
            labelLegend.BackColor = dark ? Color.FromArgb(45, 45, 48) : SystemColors.Control;
            labelLegend.ForeColor = dark ? Color.FromArgb(235, 235, 235) : SystemColors.ControlText;

            ApplyItemColor(flowLayoutPanellabelInvalid, labelInvalid, ApplicationListConstants.Colors.InvalidColor, dark);
            ApplyItemColor(flowLayoutPanellabelOrphaned, labelOrphaned, ApplicationListConstants.Colors.UnregisteredColor, dark);
            ApplyItemColor(flowLayoutPanellabelUnverified, labelUnverified, ApplicationListConstants.Colors.UnverifiedColor, dark);
            ApplyItemColor(flowLayoutPanellabelVerified, labelVerified, ApplicationListConstants.Colors.VerifiedColor, dark);
            ApplyItemColor(flowLayoutPanellabelWinFeature, labelWinFeature, ApplicationListConstants.Colors.WindowsFeatureColor, dark);
            ApplyItemColor(flowLayoutPanellabelStoreApp, labelStoreApp, ApplicationListConstants.Colors.WindowsStoreAppColor, dark);
        }

        private static void ApplyItemColor(Panel panel, Label label, Color color, bool dark)
        {
            var backColor = AppTheme.GetListHighlightBackColor(color, dark);
            panel.BackColor = backColor;
            label.ForeColor = AppTheme.GetReadableTextColor(backColor);
        }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool InvalidEnabled
        {
            get { return flowLayoutPanellabelInvalid.Visible; }
            set { flowLayoutPanellabelInvalid.Visible = value; }
        }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool WinFeatureEnabled
        {
            get { return flowLayoutPanellabelWinFeature.Visible; }
            set { flowLayoutPanellabelWinFeature.Visible = value; }
        }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool CertificatesEnabled
        {
            get { return flowLayoutPanellabelVerified.Visible; }
            set { flowLayoutPanellabelVerified.Visible = value; flowLayoutPanellabelUnverified.Visible = value; }
        }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool OrphanedEnabled
        {
            get { return flowLayoutPanellabelOrphaned.Visible; }
            set { flowLayoutPanellabelOrphaned.Visible = value; }
        }

        [Browsable(false), EditorBrowsable(EditorBrowsableState.Never)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool StoreAppEnabled
        {
            get { return flowLayoutPanellabelStoreApp.Visible; }
            set { flowLayoutPanellabelStoreApp.Visible = value; }
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            CloseRequested?.Invoke(sender, e);
        }

        private void ThisEnabledChanged(object sender, EventArgs e)
        {
            UpdateColors();
        }

        public event EventHandler CloseRequested;
    }
}