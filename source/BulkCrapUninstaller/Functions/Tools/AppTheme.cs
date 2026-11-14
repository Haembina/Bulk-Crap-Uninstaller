using BrightIdeasSoftware;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Klocman.Controls;
using Microsoft.Win32;
using SimpleTreeMap;

namespace BulkCrapUninstaller.Functions.Tools
{
    /// <summary>Applies BCU's light or dark palette to open and newly shown windows.</summary>
    internal static class AppTheme
    {
        internal enum ThemePreference
        {
            System,
            Light,
            Dark
        }

        private enum PreferredAppMode
        {
            Default = 0,
            AllowDark = 1,
            ForceDark = 2,
            ForceLight = 3,
            Max = 4
        }

        private static readonly Color Window = Color.FromArgb(32, 32, 32);
        private static readonly Color Surface = Color.FromArgb(45, 45, 48);
        private static readonly Color Text = Color.FromArgb(235, 235, 235);
        private static readonly Color MutedText = Color.FromArgb(165, 165, 170);
        private static readonly Color Selection = Color.FromArgb(62, 91, 125);
        private static readonly Color HeaderHot = Color.FromArgb(58, 58, 62);
        private static readonly Color GroupHeaderText = Color.FromArgb(156, 220, 254);
        private static readonly Color GroupHeaderLine = Color.FromArgb(95, 100, 110);
        private static readonly Color TreemapBorder = Color.FromArgb(24, 24, 26);
        private const float DarkHighlightChromaWeight = 0.22f;
        private const float DarkHighlightNeutralWeight = 0.14f;
        private const float DarkTreemapChromaWeight = 0.36f;
        private const float DarkTreemapNeutralWeight = 0.22f;
        private static bool _dark;
        private static ThemePreference _preference = ThemePreference.System;
        private static bool _watching;
        private static bool _watchingSystemTheme;
        private static System.Windows.Forms.Timer _windowDiscoveryTimer;
        private static readonly HashSet<Form> WatchedForms = new();
        private static readonly ConditionalWeakTable<Control, OriginalControlStyle> OriginalStyles = new();
        private static readonly ConditionalWeakTable<ToolStripItem, OriginalItemStyle> OriginalItemStyles = new();
        private static readonly ConditionalWeakTable<TabControl, DarkTabControlWindow> TabPainters = new();

        internal static bool IsDark => _dark;
        internal static Color GetDefaultListItemBackColor(bool dark) => dark ? Surface : SystemColors.Window;
        internal static Color GetDefaultListItemForeColor(bool dark) => dark ? Text : SystemColors.WindowText;

        internal static Color GetListHighlightBackColor(Color color, bool dark) => dark
            ? ExtractDarkTint(Surface, color, DarkHighlightChromaWeight, DarkHighlightNeutralWeight)
            : color;

        internal static Color GetTreemapColor(Color color, bool dark) => dark
            ? ExtractDarkTint(Surface, color, DarkTreemapChromaWeight, DarkTreemapNeutralWeight)
            : color;

        internal static ThemePreference ParseThemePreference(string value) =>
            Enum.TryParse(value, true, out ThemePreference preference) && Enum.IsDefined(preference)
                ? preference
                : ThemePreference.System;

        internal static bool ResolveDark(ThemePreference preference, bool systemDark) => preference switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => systemDark
        };

        internal static void InitializeNativeTheme()
        {
            try
            {
                var pref = Properties.Settings.Default.WindowThemePreference;
                if (string.IsNullOrWhiteSpace(pref))
                    pref = Properties.Settings.Default.WindowDarkMode ? "Dark" : "System";
                _preference = ParseThemePreference(pref);
            }
            catch
            {
                _preference = ThemePreference.System;
            }

            _dark = ResolveDark(_preference, IsSystemDark());
            SetProcessDarkMode(_dark);
        }

        public static void Start(ThemePreference preference)
        {
            _preference = preference;
            if (!_watching)
            {
                _watching = true;
                _windowDiscoveryTimer = new System.Windows.Forms.Timer { Interval = 100 };
                _windowDiscoveryTimer.Tick += DiscoverWindows;
                _windowDiscoveryTimer.Start();
            }
            if (!_watchingSystemTheme)
            {
                _watchingSystemTheme = true;
                SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
            }
            _dark = ResolveDark(_preference, IsSystemDark());
            SetProcessDarkMode(_dark);
            ApplyToOpenWindows();
        }

        internal static void SetThemePreference(ThemePreference preference)
        {
            _preference = preference;
            _dark = ResolveDark(_preference, IsSystemDark());
            SetProcessDarkMode(_dark);
            ApplyToOpenWindows();
        }

        private static void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (_preference != ThemePreference.System) return;

            var dark = ResolveDark(_preference, IsSystemDark());
            if (_dark == dark) return;

            _dark = dark;
            SetProcessDarkMode(_dark);
            ApplyToOpenWindows();
        }

        private static bool IsSystemDark() => Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1) is int appsUseLightTheme && appsUseLightTheme == 0;

        internal static void ApplyToForm(Form form)
        {
            if (WatchedForms.Add(form))
            {
                form.Shown += FormShown;
                form.FormClosed += (s, args) => WatchedForms.Remove(form);
            }

            CaptureTree(form);
            Apply(form);
        }

        public static Color GetReadableTextColor(Color background) =>
            IsDarkColor(background) ? Text : Color.FromArgb(35, 35, 35);

        private static Color ExtractDarkTint(Color baseColor, Color color, float chromaWeight, float neutralWeight)
        {
            int min = Math.Min(color.R, Math.Min(color.G, color.B));
            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            int span = max - min;

            if (span < 12)
            {
                return Blend(baseColor, Color.FromArgb(135, 135, 142), neutralWeight);
            }

            var pure = Color.FromArgb(
                (color.R - min) * 255 / span,
                (color.G - min) * 255 / span,
                (color.B - min) * 255 / span);
            return Blend(baseColor, pure, chromaWeight);
        }

        private static Color Blend(Color baseColor, Color accent, float accentWeight) => Color.FromArgb(
            (int)(baseColor.R * (1 - accentWeight) + accent.R * accentWeight),
            (int)(baseColor.G * (1 - accentWeight) + accent.G * accentWeight),
            (int)(baseColor.B * (1 - accentWeight) + accent.B * accentWeight));

        internal static Image CreateDarkModeImage(Image source)
        {
            var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.DrawImage(source, 0, 0, source.Width, source.Height);

            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (color.A == 0) continue;

                var range = Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B));
                if (range > 24) continue;

                var luminance = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
                var alpha = color.A * (255 - luminance) / 255;
                bitmap.SetPixel(x, y, Color.FromArgb(alpha, 210, 215, 225));
            }

            return bitmap;
        }

        private static void DiscoverWindows(object sender, EventArgs e)
        {
            foreach (Form form in Application.OpenForms)
            {
                if (!WatchedForms.Add(form)) continue;
                form.Shown += FormShown;
                form.FormClosed += (s, args) => WatchedForms.Remove(form);
                if (_dark)
                {
                    CaptureTree(form);
                    Apply(form);
                }
            }
        }

        private static void FormShown(object sender, EventArgs e)
        {
            if (sender is Form form)
            {
                CaptureTree(form);
                Apply(form);
            }
        }

        private static void ApplyToOpenWindows()
        {
            ToolStripManager.Renderer = new DarkToolStripRenderer(_dark);

            foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
            {
                if (form.IsDisposed) continue;

                void UpdateWindow()
                {
                    if (form.IsDisposed) return;
                    CaptureTree(form);
                    Apply(form);
                }

                if (form.IsHandleCreated && form.InvokeRequired)
                {
                    try { form.BeginInvoke((Action)UpdateWindow); }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                }
                else
                    UpdateWindow();
            }
        }

        private static void CaptureTree(Control control)
        {
            OriginalStyles.GetValue(control, c => new OriginalControlStyle(c));
            if (control is ToolStrip strip)
                foreach (ToolStripItem item in strip.Items) CaptureItemTree(item);
            if (control.ContextMenuStrip is { } contextMenu)
            {
                OriginalStyles.GetValue(contextMenu, c => new OriginalControlStyle(c));
                foreach (ToolStripItem item in contextMenu.Items) CaptureItemTree(item);
            }
            foreach (Control child in control.Controls) CaptureTree(child);
        }

        private static void CaptureItemTree(ToolStripItem item)
        {
            OriginalItemStyles.GetValue(item, i => new OriginalItemStyle(i));
            if (item is ToolStripControlHost { Control: { } hostedControl })
                CaptureTree(hostedControl);
            if (item is ToolStripDropDownItem dropDown)
                foreach (ToolStripItem child in dropDown.DropDownItems) CaptureItemTree(child);
        }

        private static void Apply(Control control)
        {
            var original = OriginalStyles.GetValue(control, c => new OriginalControlStyle(c));

            if (control is Controls.ListLegend legend)
            {
                legend.UpdateColors();
                return;
            }

            if (!_dark)
            {
                control.BackColor = original.BackColor;
                control.ForeColor = original.ForeColor;
                if (control is SearchBox searchBoxToRestore)
                {
                    searchBoxToRestore.BackColor = original.BackColor;
                    searchBoxToRestore.NormalSearchColor = original.NormalSearchColor;
                    searchBoxToRestore.InactiveSearchColor = original.InactiveSearchColor;
                    searchBoxToRestore.SearchBoxBorderStyle = original.SearchBoxBorderStyle;
                }
                if (control is LinkLabel linkToRestore)
                {
                    linkToRestore.LinkColor = original.LinkColor;
                    linkToRestore.ActiveLinkColor = original.ActiveLinkColor;
                    linkToRestore.VisitedLinkColor = original.VisitedLinkColor;
                }
                if (control is Button buttonToRestore)
                {
                    buttonToRestore.UseVisualStyleBackColor = original.UseVisualStyleBackColor;
                    buttonToRestore.FlatStyle = original.FlatStyle;
                }
                if (control is GroupBox groupToRestore)
                    groupToRestore.FlatStyle = original.GroupBoxFlatStyle;
                if (control is ComboBox comboToRestore)
                    comboToRestore.FlatStyle = original.ComboFlatStyle;
                if (control is PictureBox pictureToRestore && IsLogoPictureBox(pictureToRestore))
                    pictureToRestore.Image = original.PictureBoxImage;
                if (control is TabPage pageToRestore)
                    pageToRestore.UseVisualStyleBackColor = original.TabPageUsesVisualStyles;
                if (control is ToolStrip stripToRestore)
                {
                    stripToRestore.Renderer = original.Renderer;
                    stripToRestore.RenderMode = original.RenderMode;
                    foreach (ToolStripItem item in stripToRestore.Items) Apply(item);
                }
                if (control is TabControl tabsToRestore)
                {
                    tabsToRestore.DrawItem -= original.TabDrawHandler;
                    tabsToRestore.DrawMode = original.TabDrawMode;
                    if (TabPainters.TryGetValue(tabsToRestore, out var painter))
                        painter.Detach();
                    tabsToRestore.Invalidate();
                }
                if (control is BrightIdeasSoftware.ObjectListView listToRestore)
                {
                    listToRestore.SelectedBackColor = original.SelectedBackColor;
                    listToRestore.SelectedForeColor = original.SelectedForeColor;
                    listToRestore.UnfocusedSelectedBackColor = original.UnfocusedSelectedBackColor;
                    listToRestore.UnfocusedSelectedForeColor = original.UnfocusedSelectedForeColor;
                    listToRestore.GroupHeaderBackColor = SystemColors.Window;
                    listToRestore.GroupHeaderForeColor = SystemColors.WindowText;
                    listToRestore.GroupHeaderBorderColor = SystemColors.ControlDark;
                    listToRestore.HeaderUsesThemes = original.HeaderUsesThemes;
                    listToRestore.HeaderFormatStyle = original.HeaderFormatStyle;
                    listToRestore.HyperlinkStyle = original.HyperlinkStyle;
                    if (listToRestore.EmptyListMsgOverlay is TextOverlay overlayToRestore)
                    {
                        overlayToRestore.BackColor = original.OverlayBackColor;
                        overlayToRestore.TextColor = original.OverlayTextColor;
                        overlayToRestore.BorderColor = original.OverlayBorderColor;
                    }
                    RefreshListRows(listToRestore);
                }
                if (control is TreeMap treeMapToRestore)
                {
                    treeMapToRestore.BackColor = original.BackColor;
                    treeMapToRestore.SelectedObjectColor = original.SelectedObjectColor;
                    treeMapToRestore.RefreshColors();
                }
                if (IsProductNameLabel(control) && control is Label labelToRestore)
                    labelToRestore.Enabled = original.Enabled;
                if (control is DataGridView gridToRestore && original.GridStyles != null)
                {
                    gridToRestore.BackgroundColor = original.GridBackgroundColor;
                    gridToRestore.GridColor = original.GridColor;
                    gridToRestore.DefaultCellStyle = original.GridStyles;
                    gridToRestore.ColumnHeadersDefaultCellStyle = original.GridHeaderStyles;
                    gridToRestore.RowHeadersDefaultCellStyle = original.GridRowHeaderStyles;
                    gridToRestore.EnableHeadersVisualStyles = original.GridUsesVisualStyles;
                }
            }
            else
            {
                if (control is Form formControl)
                {
                    var usesTransparencyKey = !formControl.TransparencyKey.IsEmpty &&
                                              original.BackColor == formControl.TransparencyKey;
                    control.BackColor = usesTransparencyKey ? original.BackColor : Window;
                }
                else if (IsInputSurface(control))
                    control.BackColor = Surface;
                else if (IsStandardSurface(original.BackColor))
                    control.BackColor = Surface;
                else
                    control.BackColor = original.BackColor;

                if (control is Form)
                    control.ForeColor = Text;
                else if (control is Label || control is CheckBox || control is RadioButton || control is LinkLabel ||
                    control is ButtonBase || control is GroupBox || control is TabPage)
                {
                    var background = IsInputSurface(control) ? Surface : EffectiveOriginalBackColor(control);
                    if (IsStandardSurface(background)) background = control is Form ? Window : Surface;
                    control.ForeColor = IsDarkColor(background) ? Text : Color.FromArgb(35, 35, 35);
                }
                else
                    control.ForeColor = original.ForeColor;

                if (control is GroupBox groupBox)
                {
                    groupBox.FlatStyle = FlatStyle.Standard;
                    groupBox.ForeColor = Text;
                }

                // The sidebar product name is intentionally disabled in the light theme.
                // WinForms dims disabled labels and ignores their ForeColor, so enable it
                // while dark mode is active to keep the name legible on the dark panel.
                if (IsProductNameLabel(control) && control is Label label)
                    label.Enabled = true;

                if (control is Button button)
                {
                    button.UseVisualStyleBackColor = false;
                    button.BackColor = IsStandardSurface(original.BackColor)
                        ? Surface
                        : GetListHighlightBackColor(original.BackColor, true);
                    button.ForeColor = Text;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 86);
                }

                if (control is ComboBox combo)
                {
                    combo.FlatStyle = FlatStyle.Standard;
                    combo.BackColor = Surface;
                    combo.ForeColor = Text;
                }

                if (control is SearchBox searchBox)
                {
                    searchBox.BackColor = Surface;
                    searchBox.NormalSearchColor = Text;
                    searchBox.InactiveSearchColor = MutedText;
                    searchBox.SearchBoxBorderStyle = BorderStyle.Fixed3D;
                }

                if (control is PictureBox pictureBox && IsLogoPictureBox(pictureBox))
                {
                    pictureBox.BackColor = control.Parent is Form ? Window : Surface;
                    pictureBox.Image = original.GetDarkModePictureBoxImage();
                }

                if (control is LinkLabel link)
                {
                    link.LinkColor = Color.FromArgb(115, 185, 250);
                    link.ActiveLinkColor = Color.FromArgb(160, 215, 255);
                    link.VisitedLinkColor = Color.FromArgb(200, 165, 245);
                }

                if (IsInputSurface(control))
                {
                    control.BackColor = Surface;
                    control.ForeColor = Text;
                }

                // TabPage defaults to a themed white page even when BackColor is set.
                if (control is TabPage page)
                {
                    page.UseVisualStyleBackColor = false;
                    page.BackColor = Surface;
                }

                if (control is BrightIdeasSoftware.ObjectListView list)
                {
                    list.SelectedBackColor = Selection;
                    list.SelectedForeColor = Text;
                    list.UnfocusedSelectedBackColor = Selection;
                    list.UnfocusedSelectedForeColor = Text;
                    list.GroupHeaderBackColor = Surface;
                    list.GroupHeaderForeColor = GroupHeaderText;
                    list.GroupHeaderBorderColor = GroupHeaderLine;
                    list.HeaderUsesThemes = false;
                    list.HeaderFormatStyle = CreateHeaderFormatStyle();
                    list.HyperlinkStyle = CreateDarkHyperlinkStyle();
                    if (list.EmptyListMsgOverlay is TextOverlay emptyOverlay)
                    {
                        emptyOverlay.BackColor = Window;
                        emptyOverlay.TextColor = Text;
                        emptyOverlay.BorderColor = HeaderHot;
                    }
                    RefreshListRows(list);
                }

                if (control is DataGridView grid)
                {
                    grid.BackgroundColor = Surface;
                    grid.GridColor = Color.FromArgb(68, 68, 72);
                    grid.DefaultCellStyle.BackColor = Surface;
                    grid.DefaultCellStyle.ForeColor = Text;
                    grid.DefaultCellStyle.SelectionBackColor = Selection;
                    grid.DefaultCellStyle.SelectionForeColor = Text;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = Window;
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                    grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Selection;
                    grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
                    grid.RowHeadersDefaultCellStyle.BackColor = Window;
                    grid.RowHeadersDefaultCellStyle.ForeColor = Text;
                    grid.RowHeadersDefaultCellStyle.SelectionBackColor = Selection;
                    grid.RowHeadersDefaultCellStyle.SelectionForeColor = Text;
                    grid.EnableHeadersVisualStyles = false;
                }

                if (control is ToolStrip strip)
                {
                    strip.BackColor = Surface;
                    strip.ForeColor = Text;
                    strip.Renderer = new DarkToolStripRenderer(true);
                    foreach (ToolStripItem item in strip.Items) Apply(item);
                }

                if (control is TabControl tabs)
                {
                    tabs.BackColor = Surface;
                    tabs.ForeColor = Text;
                    tabs.DrawItem -= original.TabDrawHandler;
                    tabs.DrawItem += original.TabDrawHandler;
                    tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                    if (tabs.IsHandleCreated)
                        AttachTabPainter(tabs);
                    tabs.Invalidate();
                }

                if (control is TreeMap treeMap)
                {
                    treeMap.BackColor = TreemapBorder;
                    treeMap.SelectedObjectColor = Selection;
                    treeMap.RefreshColors();
                }
            }

            if (control.ContextMenuStrip is { } contextMenuStrip)
                Apply(contextMenuStrip);

            foreach (Control child in control.Controls) Apply(child);

            if (control.IsHandleCreated)
                ApplyNativeControlTheme(control, _dark);

            if (control is BrightIdeasSoftware.ObjectListView themedList && themedList.IsHandleCreated)
            {
                themedList.HeaderControl.Invalidate();
                themedList.Invalidate();
            }
        }

        private static void AttachTabPainter(TabControl tabs)
        {
            if (!_dark || !tabs.IsHandleCreated) return;
            var painter = TabPainters.GetValue(tabs, t => new DarkTabControlWindow(t));
            painter.Attach();
        }

        private static void ApplyNativeControlTheme(Control control, bool dark)
        {
            if (!control.IsHandleCreated) return;

            try
            {
                AllowDarkModeForWindow(control.Handle, dark);

                if (control is Form)
                {
                    ApplyCaptionTheme(control.Handle, dark);
                }
                else if (control is BrightIdeasSoftware.ObjectListView olv)
                {
                    SetWindowTheme(olv.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                    if (olv.HeaderControl != null && olv.HeaderControl.Handle != IntPtr.Zero)
                    {
                        AllowDarkModeForWindow(olv.HeaderControl.Handle, dark);
                        SetWindowTheme(olv.HeaderControl.Handle, dark ? "DarkMode_ItemsView" : null, null);
                    }
                }
                else if (control is ListView || control is TreeView || control is ListBox || control is ScrollBar)
                {
                    SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
                }
                else if (control is ComboBox combo)
                {
                    SetWindowTheme(combo.Handle, dark ? "DarkMode_CFD" : null, null);
                    var info = new COMBOBOXINFO { cbSize = Marshal.SizeOf<COMBOBOXINFO>() };
                    if (GetComboBoxInfo(combo.Handle, ref info))
                    {
                        if (info.hwndList != IntPtr.Zero)
                        {
                            AllowDarkModeForWindow(info.hwndList, dark);
                            SetWindowTheme(info.hwndList, dark ? "DarkMode_Explorer" : "Explorer", null);
                        }
                        if (info.hwndItem != IntPtr.Zero)
                        {
                            AllowDarkModeForWindow(info.hwndItem, dark);
                            SetWindowTheme(info.hwndItem, dark ? "DarkMode_CFD" : null, null);
                        }
                    }
                }
                else if (control is TextBoxBase || control is UpDownBase)
                {
                    SetWindowTheme(control.Handle, dark ? "DarkMode_CFD" : null, null);
                }
                else if (control is ProgressBar)
                {
                    SetWindowTheme(control.Handle, dark ? " " : null, dark ? " " : null);
                }
                else if (control is TabControl tabs)
                {
                    if (dark)
                        AttachTabPainter(tabs);
                    else if (TabPainters.TryGetValue(tabs, out var painter))
                        painter.Detach();
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private static HeaderFormatStyle CreateHeaderFormatStyle()
        {
            var style = new HeaderFormatStyle();
            style.SetBackColor(Surface);
            style.SetForeColor(Text);
            style.Hot.BackColor = HeaderHot;
            style.Pressed.BackColor = Selection;
            return style;
        }

        private static HyperlinkStyle CreateDarkHyperlinkStyle()
        {
            var style = new HyperlinkStyle();
            style.Normal.ForeColor = Color.FromArgb(130, 195, 255);
            style.Over.ForeColor = Color.FromArgb(175, 220, 255);
            style.Visited.ForeColor = Color.FromArgb(205, 170, 255);
            return style;
        }

        private static void DrawDarkTab(object sender, DrawItemEventArgs e)
        {
            if (sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabPages.Count) return;

            var background = e.Index == tabs.SelectedIndex ? Surface : Window;
            using (var brush = new SolidBrush(background))
                e.Graphics.FillRectangle(brush, e.Bounds);
            using (var pen = new Pen(HeaderHot))
                e.Graphics.DrawRectangle(pen, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);

            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        private static void RefreshListRows(BrightIdeasSoftware.ObjectListView list)
        {
            if (list.VirtualMode || list.Objects == null)
            {
                list.Invalidate();
                return;
            }

            var objects = ((IEnumerable)list.Objects).Cast<object>().ToArray();
            if (objects.Length > 0)
                list.RefreshObjects(objects);
            else
                list.Invalidate();
        }

        private static void SetProcessDarkMode(bool dark)
        {
            try
            {
                SetPreferredAppMode(dark ? PreferredAppMode.ForceDark : PreferredAppMode.ForceLight);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            try
            {
                RefreshImmersiveColorPolicyState();
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            try
            {
                FlushMenuThemes();
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private static void ApplyCaptionTheme(IntPtr windowHandle, bool dark)
        {
            var value = dark ? 1 : 0;
            try
            {
                // Windows 10 20H1 and later use attribute 20. Earlier builds used 19.
                var result = DwmSetWindowAttribute(windowHandle, 20, ref value, sizeof(int));
                if (result != 0)
                    DwmSetWindowAttribute(windowHandle, 19, ref value, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);

        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        private static extern int SetPreferredAppMode(PreferredAppMode appMode);

        [DllImport("uxtheme.dll", EntryPoint = "#133")]
        private static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

        [DllImport("uxtheme.dll", EntryPoint = "#104")]
        private static extern void RefreshImmersiveColorPolicyState();

        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        private static extern void FlushMenuThemes();

        [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string subApp, string subId);

        [DllImport("user32.dll", EntryPoint = "GetComboBoxInfo")]
        private static extern bool GetComboBoxInfo(IntPtr hwndCombo, ref COMBOBOXINFO info);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct COMBOBOXINFO
        {
            public int cbSize;
            public NativeRect rcItem;
            public NativeRect rcButton;
            public int stateButton;
            public IntPtr hwndCombo;
            public IntPtr hwndItem;
            public IntPtr hwndList;
        }

        private static bool IsStandardSurface(Color color) => color == SystemColors.Control ||
            color == SystemColors.ControlLight || color == SystemColors.ControlLightLight ||
            color == SystemColors.Window || color == Color.White ||
            color == Window || color == Surface;

        private static void Apply(ToolStripItem item)
        {
            var original = OriginalItemStyles.GetValue(item, i => new OriginalItemStyle(i));
            item.BackColor = _dark ? Surface : original.BackColor;
            item.ForeColor = _dark ? Text : original.ForeColor;
            item.Image = _dark ? original.GetDarkModeImage() : original.Image;
            if (item is ToolStripStatusLabel statusLabel)
                statusLabel.BorderStyle = _dark ? Border3DStyle.Flat : original.StatusBorderStyle;
            if (item is ToolStripControlHost { Control: { } hostedControl })
                Apply(hostedControl);
            if (item is ToolStripDropDownItem dropDown)
            {
                if (dropDown.HasDropDownItems)
                {
                    dropDown.DropDown.BackColor = _dark ? Surface : SystemColors.Menu;
                    dropDown.DropDown.ForeColor = _dark ? Text : SystemColors.MenuText;
                    foreach (ToolStripItem child in dropDown.DropDownItems)
                        Apply(child);
                }
            }
        }

        private static bool IsInputSurface(Control control) => control is TextBoxBase || control is ComboBox ||
            control is ListBox || control is ListView || control is TreeView || control is UpDownBase;

        private static bool IsProductNameLabel(Control control) => control is Label { Name: "label1", Parent.Name: "settingsSidebarPanel" };

        private static bool IsLogoPictureBox(PictureBox pictureBox) =>
            pictureBox.Name is "startupSplashPictureBox" or "pictureBox1";

        private static bool IsDarkColor(Color color) => color.R * 299 + color.G * 587 + color.B * 114 < 145000;

        private static Color EffectiveOriginalBackColor(Control control)
        {
            for (var current = control; current != null; current = current.Parent)
            {
                var backColor = OriginalStyles.GetValue(current, c => new OriginalControlStyle(c)).BackColor;
                if (backColor != Color.Transparent) return backColor;
            }

            return Window;
        }

        private sealed class DarkTabControlWindow : NativeWindow
        {
            private const int WM_PAINT = 0x000F;
            private readonly TabControl _tabs;
            private bool _attached;

            public DarkTabControlWindow(TabControl tabs)
            {
                _tabs = tabs;
                _tabs.HandleDestroyed += (_, _) => Detach();
            }

            public void Attach()
            {
                if (_attached || !_tabs.IsHandleCreated) return;
                AssignHandle(_tabs.Handle);
                _attached = true;
            }

            public void Detach()
            {
                if (!_attached) return;
                ReleaseHandle();
                _attached = false;
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);

                if (m.Msg == WM_PAINT && _dark && _tabs.IsHandleCreated && _tabs.Visible)
                {
                    using var g = Graphics.FromHwnd(_tabs.Handle);
                    using var windowBrush = new SolidBrush(Window);
                    using var surfaceBrush = new SolidBrush(Surface);
                    using var borderPen = new Pen(HeaderHot);
                    using var accentPen = new Pen(Selection, 2f);

                    var clientRect = _tabs.ClientRectangle;
                    if (_tabs.SelectedTab != null)
                    {
                        var pageBounds = _tabs.SelectedTab.Bounds;
                        g.SetClip( Rectangle.Inflate(pageBounds, -1, -1), System.Drawing.Drawing2D.CombineMode.Exclude);
                        g.FillRectangle(windowBrush, clientRect);
                        g.ResetClip();
                        var frameRect = Rectangle.Inflate(pageBounds, 1, 1);
                        g.DrawRectangle(borderPen, frameRect.X, frameRect.Y, frameRect.Width - 1, frameRect.Height - 1);
                    }
                    else
                    {
                        g.FillRectangle(windowBrush, clientRect);
                    }

                    for (var i = 0; i < _tabs.TabCount; i++)
                    {
                        var tabRect = _tabs.GetTabRect(i);
                        var selected = i == _tabs.SelectedIndex;
                        g.FillRectangle(selected ? surfaceBrush : windowBrush, tabRect);
                        g.DrawRectangle(borderPen, tabRect.X, tabRect.Y, tabRect.Width - 1, tabRect.Height - 1);
                        if (selected)
                            g.DrawLine(accentPen, tabRect.Left + 1, tabRect.Top + 1, tabRect.Right - 2, tabRect.Top + 1);

                        TextRenderer.DrawText(g, _tabs.TabPages[i].Text, _tabs.Font, tabRect,
                            selected ? Text : MutedText,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                    }
                }
            }
        }

        private sealed class OriginalControlStyle
        {
            public OriginalControlStyle(Control control)
            {
                BackColor = control.BackColor;
                ForeColor = control.ForeColor;
                Enabled = control.Enabled;
                control.HandleCreated += (sender, _) =>
                {
                    if (sender is Control c)
                        ApplyNativeControlTheme(c, _dark);
                };

                if (control is SearchBox searchBox)
                {
                    NormalSearchColor = searchBox.NormalSearchColor;
                    InactiveSearchColor = searchBox.InactiveSearchColor;
                    SearchBoxBorderStyle = searchBox.SearchBoxBorderStyle;
                }
                if (control is Button button)
                {
                    FlatStyle = button.FlatStyle;
                    UseVisualStyleBackColor = button.UseVisualStyleBackColor;
                }
                if (control is GroupBox groupBox)
                    GroupBoxFlatStyle = groupBox.FlatStyle;
                if (control is ComboBox comboBox)
                    ComboFlatStyle = comboBox.FlatStyle;
                if (control is PictureBox pictureBox && IsLogoPictureBox(pictureBox))
                    PictureBoxImage = pictureBox.Image;
                if (control is ToolStrip strip)
                {
                    Renderer = strip.Renderer;
                    RenderMode = strip.RenderMode;
                }
                if (control is TabControl tabs)
                {
                    TabDrawMode = tabs.DrawMode;
                    TabDrawHandler = DrawDarkTab;
                }
                if (control is TabPage page)
                    TabPageUsesVisualStyles = page.UseVisualStyleBackColor;
                if (control is LinkLabel link)
                {
                    LinkColor = link.LinkColor;
                    ActiveLinkColor = link.ActiveLinkColor;
                    VisitedLinkColor = link.VisitedLinkColor;
                }
                if (control is BrightIdeasSoftware.ObjectListView list)
                {
                    SelectedBackColor = list.SelectedBackColor;
                    SelectedForeColor = list.SelectedForeColor;
                    UnfocusedSelectedBackColor = list.UnfocusedSelectedBackColor;
                    UnfocusedSelectedForeColor = list.UnfocusedSelectedForeColor;
                    HeaderUsesThemes = list.HeaderUsesThemes;
                    HeaderFormatStyle = list.HeaderFormatStyle;
                    HyperlinkStyle = list.HyperlinkStyle;
                    if (list.EmptyListMsgOverlay is TextOverlay emptyOverlay)
                    {
                        OverlayBackColor = emptyOverlay.BackColor;
                        OverlayTextColor = emptyOverlay.TextColor;
                        OverlayBorderColor = emptyOverlay.BorderColor;
                    }
                }
                if (control is TreeMap treeMap)
                    SelectedObjectColor = treeMap.SelectedObjectColor;
                if (control is DataGridView grid)
                {
                    GridBackgroundColor = grid.BackgroundColor;
                    GridColor = grid.GridColor;
                    GridStyles = grid.DefaultCellStyle.Clone();
                    GridHeaderStyles = grid.ColumnHeadersDefaultCellStyle.Clone();
                    GridRowHeaderStyles = grid.RowHeadersDefaultCellStyle.Clone();
                    GridUsesVisualStyles = grid.EnableHeadersVisualStyles;
                }
            }

            public Image GetDarkModePictureBoxImage() =>
                PictureBoxImage == null ? null : DarkPictureBoxImage ??= CreateDarkModeImage(PictureBoxImage);

            public Color BackColor { get; }
            public Color ForeColor { get; }
            public bool Enabled { get; }
            public Color NormalSearchColor { get; }
            public Color InactiveSearchColor { get; }
            public BorderStyle SearchBoxBorderStyle { get; }
            public FlatStyle FlatStyle { get; }
            public FlatStyle GroupBoxFlatStyle { get; }
            public FlatStyle ComboFlatStyle { get; }
            public bool UseVisualStyleBackColor { get; }
            public Image PictureBoxImage { get; }
            private Image DarkPictureBoxImage { get; set; }
            public ToolStripRenderer Renderer { get; }
            public ToolStripRenderMode RenderMode { get; }
            public TabDrawMode TabDrawMode { get; }
            public DrawItemEventHandler TabDrawHandler { get; }
            public bool TabPageUsesVisualStyles { get; }
            public Color LinkColor { get; }
            public Color ActiveLinkColor { get; }
            public Color VisitedLinkColor { get; }
            public Color SelectedBackColor { get; }
            public Color SelectedForeColor { get; }
            public Color UnfocusedSelectedBackColor { get; }
            public Color UnfocusedSelectedForeColor { get; }
            public bool HeaderUsesThemes { get; }
            public HeaderFormatStyle HeaderFormatStyle { get; }
            public HyperlinkStyle HyperlinkStyle { get; }
            public Color OverlayBackColor { get; }
            public Color OverlayTextColor { get; }
            public Color OverlayBorderColor { get; }
            public Color SelectedObjectColor { get; }
            public Color GridBackgroundColor { get; }
            public Color GridColor { get; }
            public DataGridViewCellStyle GridStyles { get; }
            public DataGridViewCellStyle GridHeaderStyles { get; }
            public DataGridViewCellStyle GridRowHeaderStyles { get; }
            public bool GridUsesVisualStyles { get; }
        }

        private sealed class OriginalItemStyle
        {
            public OriginalItemStyle(ToolStripItem item)
            {
                BackColor = item.BackColor;
                ForeColor = item.ForeColor;
                Image = item.Image;
                if (item is ToolStripStatusLabel statusLabel)
                    StatusBorderStyle = statusLabel.BorderStyle;
            }

            public Image GetDarkModeImage() => Image == null ? null : DarkModeImage ??= CreateDarkModeImage(Image);
            public Color BackColor { get; }
            public Color ForeColor { get; }
            public Image Image { get; }
            public Border3DStyle StatusBorderStyle { get; }
            private Image DarkModeImage { get; set; }
        }

        private sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
        {
            private readonly bool _dark;

            public DarkToolStripRenderer(bool dark) : base(new ThemeColorTable(dark))
            {
                _dark = dark;
                RoundedEdges = false;
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                if (_dark)
                {
                    e.TextColor = e.Item.Enabled ? Text : MutedText;
                }
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                if (_dark)
                {
                    e.ArrowColor = e.Item is { Enabled: true } ? Text : MutedText;
                }
                base.OnRenderArrow(e);
            }
        }

        private sealed class ThemeColorTable : ProfessionalColorTable
        {
            private readonly bool _dark;
            public ThemeColorTable(bool dark) => _dark = dark;
            private Color Base => _dark ? Surface : SystemColors.Control;
            private Color Border => _dark ? Color.FromArgb(75, 75, 78) : SystemColors.ControlDark;
            private Color Hover => _dark ? Selection : SystemColors.Highlight;
            private Color Pressed => _dark ? HeaderHot : SystemColors.ControlLight;

            public override Color ToolStripDropDownBackground => Base;
            public override Color ToolStripGradientBegin => Base;
            public override Color ToolStripGradientMiddle => Base;
            public override Color ToolStripGradientEnd => Base;
            public override Color ToolStripBorder => Border;
            public override Color MenuStripGradientBegin => Base;
            public override Color MenuStripGradientEnd => Base;
            public override Color StatusStripGradientBegin => Base;
            public override Color StatusStripGradientEnd => Base;
            public override Color ImageMarginGradientBegin => Base;
            public override Color ImageMarginGradientMiddle => Base;
            public override Color ImageMarginGradientEnd => Base;
            public override Color MenuItemSelected => Hover;
            public override Color MenuItemSelectedGradientBegin => Hover;
            public override Color MenuItemSelectedGradientEnd => Hover;
            public override Color MenuItemPressedGradientBegin => Pressed;
            public override Color MenuItemPressedGradientMiddle => Pressed;
            public override Color MenuItemPressedGradientEnd => Pressed;
            public override Color ButtonSelectedGradientBegin => Hover;
            public override Color ButtonSelectedGradientMiddle => Hover;
            public override Color ButtonSelectedGradientEnd => Hover;
            public override Color ButtonSelectedBorder => Border;
            public override Color ButtonPressedGradientBegin => Pressed;
            public override Color ButtonPressedGradientMiddle => Pressed;
            public override Color ButtonPressedGradientEnd => Pressed;
            public override Color ButtonPressedBorder => Border;
            public override Color ButtonCheckedGradientBegin => Pressed;
            public override Color ButtonCheckedGradientMiddle => Pressed;
            public override Color ButtonCheckedGradientEnd => Pressed;
            public override Color CheckBackground => Pressed;
            public override Color CheckSelectedBackground => Hover;
            public override Color CheckPressedBackground => Pressed;
            public override Color MenuItemBorder => Border;
            public override Color MenuBorder => Border;
            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Base;
            public override Color GripDark => Border;
            public override Color GripLight => Base;
        }
    }
}
