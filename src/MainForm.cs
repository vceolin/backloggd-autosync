using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BackloggdAutoSync
{
    public class MainForm : Form
    {
        private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "BackloggdAutoSync";

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static readonly uint WmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");
        private static readonly uint WmShowApp = RegisterWindowMessage(Program.AppShowMessageName);

        private AppSettings _settings;
        private readonly SteamTracker _tracker;
        private readonly System.Windows.Forms.Timer _pollTimer;
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _trayStatusItem;
        private bool _allowClose = false;

        private class ListViewStateSnapshot
        {
            public HashSet<string> SelectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public string FocusedKey = null;
            public int TopIndex = -1;
            public string TopKey = null;
        }

        private static ListViewStateSnapshot CaptureListViewState(ListView lv, Func<ListViewItem, string> keySelector)
        {
            var snap = new ListViewStateSnapshot();
            if (lv == null || lv.Items.Count == 0) return snap;

            try
            {
                if (lv.IsHandleCreated)
                {
                    var topItem = lv.TopItem;
                    if (topItem != null)
                    {
                        snap.TopIndex = topItem.Index;
                        snap.TopKey = keySelector(topItem);
                    }
                }
            }
            catch { }

            foreach (ListViewItem sel in lv.SelectedItems)
            {
                string k = keySelector(sel);
                if (!string.IsNullOrEmpty(k)) snap.SelectedKeys.Add(k);
            }

            if (lv.FocusedItem != null)
            {
                snap.FocusedKey = keySelector(lv.FocusedItem);
            }

            return snap;
        }

        private static void RestoreListViewState(ListView lv, ListViewStateSnapshot snap, Func<ListViewItem, string> keySelector)
        {
            if (lv == null || snap == null || lv.Items.Count == 0) return;

            foreach (ListViewItem item in lv.Items)
            {
                string k = keySelector(item);
                if (string.IsNullOrEmpty(k)) continue;

                if (snap.SelectedKeys.Contains(k))
                {
                    item.Selected = true;
                }
                if (!string.IsNullOrEmpty(snap.FocusedKey) && string.Equals(k, snap.FocusedKey, StringComparison.OrdinalIgnoreCase))
                {
                    item.Focused = true;
                }
            }
        }

        private static void RestoreListViewScrollPosition(ListView lv, ListViewStateSnapshot snap, Func<ListViewItem, string> keySelector)
        {
            if (lv == null || snap == null || lv.Items.Count == 0) return;
            try
            {
                if (!lv.IsHandleCreated || snap.TopIndex < 0) return;

                int targetIdx = -1;
                if (!string.IsNullOrEmpty(snap.TopKey))
                {
                    for (int i = 0; i < lv.Items.Count; i++)
                    {
                        if (string.Equals(keySelector(lv.Items[i]), snap.TopKey, StringComparison.OrdinalIgnoreCase))
                        {
                            targetIdx = i;
                            break;
                        }
                    }
                }

                if (targetIdx < 0)
                {
                    targetIdx = Math.Min(snap.TopIndex, lv.Items.Count - 1);
                }

                if (targetIdx > 0 && targetIdx < lv.Items.Count)
                {
                    lv.TopItem = lv.Items[targetIdx];
                }
            }
            catch { }
        }

        // Top Banner Controls
        private Panel _headerPanel;
        private Label _lblLiveStatus;
        private Label _lblLiveTimer;
        private Label _lblAccountBadge;

        // Tabs
        private TabControl _tabControl;

        // Tab 1: Sessions & Daily Summary
        private Label _lblTodaySummary;
        private ListView _lvSessions;
        private Button _btnSyncPending;
        private Button _btnPickGameForSession;
        private Button _btnManualLog;
        private Button _btnDeleteSession;

        // Tab 2: Collection / Library Entries
        private Label _lblCollectionSummary;
        private ComboBox _cmbCollectionFilter;
        private TextBox _txtCollectionSearch;
        private CheckBox _chkCollectionShowHidden;
        private Button _btnScanCollection;
        private Button _btnAutoMatchCollection;
        private Button _btnEditCollectionEntry;
        private Button _btnTogglePhysicalDigital;
        private Button _btnSyncSelectedCollection;
        private Button _btnSyncAllCollection;
        private ListView _lvCollection;
        private bool _collectionLoadedOnce = false;

        // Tab 3: Nintendo Switch Sync
        private NintendoLoginChallenge _nintendoChallenge;
        private Button _btnNintendoOpenLogin;
        private TextBox _txtNintendoAuthUrl;
        private Button _btnNintendoConnect;
        private Label _lblNintendoStatus;
        private Button _btnNintendoSyncNow;
        private CheckBox _chkNintendoAutoSync;
        private DateTime _lastNintendoAutoSyncTime = DateTime.MinValue;

        // Tab 4: Settings
        private TextBox _txtCookie;
        private Button _btnVerifyCookie;
        private Label _lblVerifyResult;
        private NumericUpDown _numMinSeconds;
        private CheckBox _chkAutoSync;
        private CheckBox _chkSplitMidnight;
        private CheckBox _chkNotifications;
        private CheckBox _chkRunOnStartup;
        private Button _btnSaveSettings;

        // Colors (Mapped directly to Backloggd's --back-* CSS custom properties)
        private readonly Color BgDark = UITheme.BgDark;
        private readonly Color BgCard = UITheme.BgCard;
        private readonly Color BgInput = UITheme.BgInput;
        private readonly Color AccentPink = UITheme.AccentPink;
        private readonly Color AccentBlue = UITheme.AccentBlue;
        private readonly Color TextLight = UITheme.TextLight;
        private readonly Color TextMuted = UITheme.TextMuted;
        private readonly Color SuccessGreen = UITheme.SuccessGreen;
        private readonly Color WarnOrange = UITheme.WarnOrange;
        private readonly List<Button> _navButtons = new List<Button>();

        public MainForm(bool startMinimized)
        {
            _settings = DataStore.LoadSettings();
            _tracker = new SteamTracker();

            InitializeComponent();
            SetupTrayIcon();

            _tracker.OnGameStarted += OnGameStarted;
            _tracker.OnSessionCompleted += OnSessionCompleted;
            _tracker.OnSessionSynced += OnSessionSynced;
            _tracker.OnUnmappedGamePromptRequested += OnUnmappedGamePromptRequested;

            // Recover any interrupted session from last run
            _tracker.RecoverCrashCheckpoint(_settings);

            RefreshSessionsList();
            if (DataStore.LoadCollection().Count > 0)
            {
                _collectionLoadedOnce = true;
                RefreshCollectionList();
            }
            UpdateAccountBadge();

            _pollTimer = new System.Windows.Forms.Timer();
            _pollTimer.Interval = 60000; // Lightweight: poll once every 60 seconds (1 minute)
            _pollTimer.Tick += PollTimer_Tick;
            _pollTimer.Start();

            // Run one initial check on startup
            PollTimer_Tick(this, EventArgs.Empty);

            if (startMinimized || _settings.StartMinimized)
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = false;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == (int)WmTaskbarCreated)
            {
                // When Windows Explorer restarts, re-register tray icon
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Visible = true;
                }
            }
            else if (m.Msg == (int)WmShowApp)
            {
                // Signal from another instance or shortcut to bring app to front
                ShowMainWindow();
            }
            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                ShowInTaskbar = false;
                if (_settings != null && _settings.ShowNotifications && _trayIcon != null)
                {
                    _trayIcon.ShowBalloonTip(1500, "Backloggd AutoSync",
                        "Running in background. Click the tray icon anytime to open.", ToolTipIcon.Info);
                }
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                ShowInTaskbar = false;
                if (_settings != null && _settings.ShowNotifications && _trayIcon != null)
                {
                    _trayIcon.ShowBalloonTip(2000, "Backloggd AutoSync Started",
                        "Running in system tray. Tracking Steam in background.", ToolTipIcon.Info);
                }
            }
        }

        private Icon LoadOrGenerateIcon()
        {
            try
            {
                Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (exeIcon != null)
                {
                    return exeIcon;
                }
            }
            catch { }

            try
            {
                string iconFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (System.IO.File.Exists(iconFile))
                {
                    return new Icon(iconFile);
                }
            }
            catch { }

            return CreateAppIcon();
        }

        private Icon CreateAppIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                using (SolidBrush bg = new SolidBrush(AccentPink))
                {
                    g.FillEllipse(bg, 1, 1, 30, 30);
                }
                using (Pen pen = new Pen(Color.White, 1.5f))
                {
                    g.DrawEllipse(pen, 2, 2, 28, 28);
                }
                using (Font f = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush fg = new SolidBrush(Color.White))
                {
                    StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("B", f, fg, new RectangleF(0, 0, 32, 32), sf);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        private void SetupTrayIcon()
        {
            _trayMenu = new ContextMenuStrip();
            var mnuOpen = new ToolStripMenuItem("Open Backloggd AutoSync", null, (s, e) => ShowMainWindow())
            {
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            _trayMenu.Items.Add(mnuOpen);

            _trayStatusItem = new ToolStripMenuItem("Status: Idle") { Enabled = false };
            _trayMenu.Items.Add(_trayStatusItem);

            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Sync Pending Sessions Now", null, (s, e) => SyncAllPendingSessions());
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Exit Completely", null, (s, e) =>
            {
                _allowClose = true;
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
                Application.Exit();
            });

            _trayIcon = new NotifyIcon
            {
                Icon = this.Icon,
                Text = "Backloggd AutoSync - Idle",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };

            // Left-click restores or brings window to front immediately
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowMainWindow();
                }
            };
            _trayIcon.DoubleClick += (s, e) => ShowMainWindow();
        }

        public void ShowMainWindow()
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(ShowMainWindow));
                return;
            }

            Show();
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            ShowInTaskbar = true;
            UpdateLiveStatusUi();
            Activate();
            BringToFront();
            try { SetForegroundWindow(this.Handle); } catch { }
        }

        private void InitializeComponent()
        {
            this.DoubleBuffered = true;
            this.Icon = LoadOrGenerateIcon();
            this.Text = "Backloggd AutoSync — Steam & Switch Playtime + Library Sync";
            this.Size = new Size(1060, 720);
            this.MinimumSize = new Size(920, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = BgDark;
            this.ForeColor = TextLight;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Header Panel (Responsive TableLayoutPanel)
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 74,
                BackColor = BgCard,
                Padding = new Padding(18, 10, 18, 10)
            };

            var headerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55f));
            headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45f));

            _lblLiveStatus = new Label
            {
                Text = "Waiting for a Steam game to launch...",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = TextLight,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            _lblLiveTimer = new Label
            {
                Text = "Monitoring Steam Registry (HKCU\\Software\\Valve\\Steam\\RunningAppID)",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = TextMuted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            _lblAccountBadge = new Label
            {
                Text = "Not Connected",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = WarnOrange,
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(12, 0, 0, 0)
            };

            headerLayout.Controls.Add(_lblLiveStatus, 0, 0);
            headerLayout.Controls.Add(_lblLiveTimer, 0, 1);
            headerLayout.Controls.Add(_lblAccountBadge, 1, 0);
            headerLayout.SetRowSpan(_lblAccountBadge, 2);
            _headerPanel.Controls.Add(headerLayout);

            // Backloggd-style Navigation Bar (Journal | Library | Nintendo Switch | Settings)
            var navBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 42,
                BackColor = BgDark,
                Padding = new Padding(14, 0, 14, 0)
            };
            navBar.Paint += (s, e) =>
            {
                using (var pen = new Pen(UITheme.BorderSubtle, 1))
                {
                    e.Graphics.DrawLine(pen, 0, navBar.Height - 1, navBar.Width, navBar.Height - 1);
                }
            };

            var navFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0)
            };

            // Borderless Tabs
            _tabControl = new BorderlessTabControl
            {
                Dock = DockStyle.Fill
            };

            var tabSessions = new TabPage("Journal & Sessions") { BackColor = BgDark, ForeColor = TextLight, Padding = new Padding(14, 12, 14, 14) };
            var tabCollection = new TabPage("Library") { BackColor = BgDark, ForeColor = TextLight, Padding = new Padding(14, 12, 14, 14) };
            var tabSwitch = new TabPage("Nintendo Switch") { BackColor = BgDark, ForeColor = TextLight, Padding = new Padding(14, 12, 14, 14) };
            var tabSettings = new TabPage("Settings") { BackColor = BgDark, ForeColor = TextLight, Padding = new Padding(14, 12, 14, 14) };

            BuildSessionsTab(tabSessions);
            BuildCollectionTab(tabCollection);
            BuildNintendoSwitchTab(tabSwitch);
            BuildSettingsTab(tabSettings);

            _tabControl.TabPages.Add(tabSessions);
            _tabControl.TabPages.Add(tabCollection);
            _tabControl.TabPages.Add(tabSwitch);
            _tabControl.TabPages.Add(tabSettings);

            string[] navTitles = new[] { "Journal & Sessions", "Library", "Nintendo Switch", "Settings" };
            _navButtons.Clear();
            for (int i = 0; i < navTitles.Length; i++)
            {
                int tabIdx = i;
                var navBtn = new Button
                {
                    Text = navTitles[i],
                    AutoSize = false,
                    Size = new Size(175, 41),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = BgDark,
                    ForeColor = tabIdx == 0 ? UITheme.TextWhite : TextMuted,
                    Font = new Font("Segoe UI", 10f, tabIdx == 0 ? FontStyle.Bold : FontStyle.Regular),
                    Cursor = Cursors.Hand,
                    Margin = new Padding(0, 0, 8, 0),
                    UseVisualStyleBackColor = false
                };
                navBtn.FlatAppearance.BorderSize = 0;
                navBtn.FlatAppearance.MouseOverBackColor = BgCard;
                navBtn.FlatAppearance.MouseDownBackColor = BgCard;
                navBtn.Paint += (s, e) =>
                {
                    if (_tabControl.SelectedIndex == tabIdx)
                    {
                        using (var accentBrush = new SolidBrush(UITheme.AccentPinkLight))
                        {
                            e.Graphics.FillRectangle(accentBrush, 10, navBtn.Height - 3, navBtn.Width - 20, 3);
                        }
                    }
                };
                navBtn.Click += (s, e) =>
                {
                    _tabControl.SelectedIndex = tabIdx;
                };
                _navButtons.Add(navBtn);
                navFlow.Controls.Add(navBtn);
            }
            navBar.Controls.Add(navFlow);

            _tabControl.SelectedIndexChanged += (s, e) =>
            {
                for (int i = 0; i < _navButtons.Count; i++)
                {
                    bool active = (_tabControl.SelectedIndex == i);
                    _navButtons[i].ForeColor = active ? UITheme.TextWhite : TextMuted;
                    _navButtons[i].Font = new Font("Segoe UI", 10f, active ? FontStyle.Bold : FontStyle.Regular);
                    _navButtons[i].Invalidate();
                }

                if (_tabControl.SelectedTab == tabCollection && !_collectionLoadedOnce)
                {
                    _collectionLoadedOnce = true;
                    ScanAndRefreshCollection(true);
                }
            };

            this.Controls.Add(_tabControl);
            this.Controls.Add(navBar);
            this.Controls.Add(_headerPanel);
        }

        private void BuildSessionsTab(TabPage page)
        {
            var topBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 54,
                BackColor = BgCard,
                Padding = new Padding(14, 9, 14, 9),
                ColumnCount = 2,
                RowCount = 1
            };
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            _lblTodaySummary = new Label
            {
                Text = "Today's Playtime: 0m across 0 games",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = TextLight,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            var actionFlow = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };

            _btnSyncPending = CreateStyledButton("Sync Pending", AccentPink, 130, 34);
            _btnSyncPending.Click += (s, e) => SyncAllPendingSessions();

            _btnPickGameForSession = CreateStyledButton("Pick / Change Game", AccentBlue, 158, 34);
            _btnPickGameForSession.Click += BtnPickGameForSession_Click;

            _btnManualLog = CreateStyledButton("+ Log Session", BgInput, 125, 34);
            _btnManualLog.Click += BtnManualLog_Click;

            _btnDeleteSession = CreateStyledButton("Delete", BgInput, 82, 34);
            _btnDeleteSession.Click += BtnDeleteSession_Click;

            actionFlow.Controls.AddRange(new Control[]
            {
                _btnSyncPending, _btnPickGameForSession, _btnManualLog, _btnDeleteSession
            });

            topBar.Controls.Add(_lblTodaySummary, 0, 0);
            topBar.Controls.Add(actionFlow, 1, 0);

            var lvSessions = new DarkListView
            {
                Dock = DockStyle.Fill
            };
            lvSessions.Columns.Add("Date", 95);
            lvSessions.Columns.Add("Game Title", 250);
            lvSessions.Columns.Add("Platform / ID", 125);
            lvSessions.Columns.Add("Session Duration", 120);
            lvSessions.Columns.Add("Backloggd Status", 150);
            lvSessions.Columns.Add("Details (Double-click row to change game)", 240);
            lvSessions.EnableProportionalColumns();
            lvSessions.DoubleClick += BtnPickGameForSession_Click;
            _lvSessions = lvSessions;

            var spacer = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = BgDark };

            page.Controls.Add(_lvSessions);
            page.Controls.Add(spacer);
            page.Controls.Add(topBar);
        }

        private void BuildCollectionTab(TabPage page)
        {
            var topBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 96,
                BackColor = BgCard,
                Padding = new Padding(14, 8, 14, 8),
                ColumnCount = 2,
                RowCount = 2
            };
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
            topBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));

            _lblCollectionSummary = new Label
            {
                Text = "Collection: Click 'Scan Libraries' or open this tab to load your owned Steam & Nintendo Switch games",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = TextLight,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            var row1Actions = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 3, 0, 0)
            };

            _btnScanCollection = CreateStyledButton("🔄 Scan Libraries", AccentBlue, 134, 32);
            _btnScanCollection.Click += (s, e) => ScanAndRefreshCollection(true);

            _btnAutoMatchCollection = CreateStyledButton("✨ Auto-Match View", AccentBlue, 146, 32);
            _btnAutoMatchCollection.Click += BtnAutoMatchCollection_Click;

            _btnEditCollectionEntry = CreateStyledButton("✏️ Edit / Map Game", BgInput, 142, 32);
            _btnEditCollectionEntry.Click += BtnEditCollectionEntry_Click;

            _btnSyncSelectedCollection = CreateStyledButton("Sync Selected", AccentPink, 120, 32);
            _btnSyncSelectedCollection.Click += BtnSyncSelectedCollection_Click;

            _btnSyncAllCollection = CreateStyledButton("Sync All Mapped", AccentPink, 132, 32);
            _btnSyncAllCollection.Click += BtnSyncAllCollection_Click;

            row1Actions.Controls.AddRange(new Control[]
            {
                _btnScanCollection, _btnAutoMatchCollection, _btnEditCollectionEntry,
                _btnSyncSelectedCollection, _btnSyncAllCollection
            });

            var filterFlow = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 0)
            };

            var lblFilter = new Label
            {
                Text = "Show:",
                ForeColor = TextMuted,
                AutoSize = true,
                Margin = new Padding(0, 6, 6, 0)
            };

            _cmbCollectionFilter = new ComboBox
            {
                Width = 220,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = BgInput,
                ForeColor = TextLight,
                Margin = new Padding(0, 2, 14, 0)
            };
            _cmbCollectionFilter.Items.AddRange(new object[]
            {
                "All Owned Games (Steam + Switch)",
                "Steam Games (All Owned)",
                "Steam Games (Installed Only)",
                "Nintendo Switch Games Only",
                "Mapped & Ready to Sync",
                "Synced on Backloggd Library",
                "Unmapped Games Only"
            });
            _cmbCollectionFilter.SelectedIndex = 0;
            _cmbCollectionFilter.SelectedIndexChanged += (s, e) => RefreshCollectionList();

            var lblSearch = new Label
            {
                Text = "Search:",
                ForeColor = TextMuted,
                AutoSize = true,
                Margin = new Padding(0, 6, 6, 0)
            };

            _txtCollectionSearch = new TextBox
            {
                Width = 215,
                BackColor = BgInput,
                ForeColor = TextLight,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 3, 16, 0)
            };
            _txtCollectionSearch.TextChanged += (s, e) => RefreshCollectionList();

            _chkCollectionShowHidden = new CheckBox
            {
                Text = "Include Steam Hidden",
                Checked = false,
                AutoSize = true,
                ForeColor = TextMuted,
                Margin = new Padding(0, 5, 0, 0)
            };
            _chkCollectionShowHidden.CheckedChanged += (s, e) => RefreshCollectionList();

            filterFlow.Controls.AddRange(new Control[]
            {
                lblFilter, _cmbCollectionFilter, lblSearch, _txtCollectionSearch, _chkCollectionShowHidden
            });

            var row2RightFlow = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 0)
            };

            _btnTogglePhysicalDigital = CreateStyledButton("💿/☁ Toggle Physical / Digital", BgInput, 205, 30);
            _btnTogglePhysicalDigital.Click += BtnTogglePhysicalDigital_Click;
            row2RightFlow.Controls.Add(_btnTogglePhysicalDigital);

            topBar.Controls.Add(_lblCollectionSummary, 0, 0);
            topBar.Controls.Add(row1Actions, 1, 0);
            topBar.Controls.Add(filterFlow, 0, 1);
            topBar.Controls.Add(row2RightFlow, 1, 1);

            var lvCollection = new DarkListView
            {
                Dock = DockStyle.Fill,
                MultiSelect = true
            };
            lvCollection.Columns.Add("Game Title (Local)", 230);
            lvCollection.Columns.Add("Source / State", 140);
            lvCollection.Columns.Add("Backloggd Association", 230);
            lvCollection.Columns.Add("Platform & Edition", 145);
            lvCollection.Columns.Add("Ownership & Store", 135);
            lvCollection.Columns.Add("Backloggd Library Status", 160);
            lvCollection.EnableProportionalColumns();
            lvCollection.DoubleClick += BtnEditCollectionEntry_Click;
            _lvCollection = lvCollection;

            var spacer = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = BgDark };

            page.Controls.Add(_lvCollection);
            page.Controls.Add(spacer);
            page.Controls.Add(topBar);
        }

        private void ScanAndRefreshCollection(bool fetchRemote)
        {
            _btnScanCollection.Enabled = false;
            _btnScanCollection.Text = "Scanning...";
            _lblCollectionSummary.Text = "Scanning owned Steam & Nintendo Switch games and Backloggd Library...";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    _tracker.SyncService.RefreshCollection(fetchRemote, status =>
                    {
                        if (IsHandleCreated && !IsDisposed)
                        {
                            BeginInvoke(new Action(() =>
                            {
                                _lblCollectionSummary.Text = status;
                            }));
                        }
                    });
                }
                catch (Exception ex)
                {
                    DataStore.Log("ScanAndRefreshCollection failed: " + ex.Message);
                }

                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(new Action(() =>
                    {
                        _btnScanCollection.Enabled = true;
                        _btnScanCollection.Text = "🔍 Scan Owned Games";
                        RefreshMappingsList();
                        if (!_collectionLoadedOnce) RefreshCollectionList();
                    }));
                }
            });
        }

        private void RefreshCollectionList()
        {
            if (_lvCollection == null) return;

            var collection = DataStore.LoadCollection();
            var mappings = DataStore.LoadMappings();
            bool collectionDirty = false;

            // Keep collection mappings in sync with mappings.json
            foreach (var c in collection)
            {
                if (c.AppId != 0)
                {
                    var m = mappings.Find(x => x.AppId == c.AppId);
                    if (m != null && !string.IsNullOrEmpty(m.BackloggdGameId) && c.BackloggdGameId != m.BackloggdGameId)
                    {
                        c.BackloggdGameId = m.BackloggdGameId;
                        c.BackloggdTitle = m.BackloggdTitle;
                        collectionDirty = true;
                    }
                }
            }
            if (collectionDirty)
            {
                DataStore.SaveCollection(collection);
            }

            string filterMode = _cmbCollectionFilter.SelectedItem != null ? _cmbCollectionFilter.SelectedItem.ToString() : "All Owned Games";
            string search = GameTitleMatcher.StripDiacritics((_txtCollectionSearch.Text ?? "").Trim()).ToLowerInvariant();
            bool showHidden = _chkCollectionShowHidden.Checked;

            int totalOwned = 0;
            int totalSteam = 0;
            int totalSwitch = 0;
            int totalMapped = 0;
            int totalSynced = 0;

            Func<ListViewItem, string> getCollectionKey = lvi =>
            {
                var ce = lvi != null ? lvi.Tag as CollectionEntry : null;
                return ce != null ? ce.Id : null;
            };
            var stateSnap = CaptureListViewState(_lvCollection, getCollectionKey);

            _lvCollection.BeginUpdate();
            _lvCollection.Items.Clear();

            foreach (var c in collection)
            {
                if (c.IsHiddenInSteam && !showHidden) continue;

                bool isSwitchEntry = (c.Source ?? "").IndexOf("Switch", StringComparison.OrdinalIgnoreCase) >= 0;

                totalOwned++;
                if (isSwitchEntry) totalSwitch++;
                else totalSteam++;

                if (!string.IsNullOrEmpty(c.BackloggdGameId)) totalMapped++;
                if (c.Synced) totalSynced++;

                // Apply filter dropdown
                if (filterMode == "Steam Games (All Owned)" && !string.Equals(c.Source, "Steam", StringComparison.OrdinalIgnoreCase)) continue;
                if (filterMode == "Steam Games (Installed Only)" && (!string.Equals(c.Source, "Steam", StringComparison.OrdinalIgnoreCase) || !c.IsInstalled)) continue;
                if (filterMode == "Nintendo Switch Games Only" && !isSwitchEntry) continue;
                if (filterMode == "Mapped & Ready to Sync" && (c.Synced || c.Excluded || string.IsNullOrEmpty(c.BackloggdGameId))) continue;
                if (filterMode == "Synced on Backloggd Library" && !c.Synced) continue;
                if (filterMode == "Unmapped Games Only" && !string.IsNullOrEmpty(c.BackloggdGameId)) continue;

                // Apply search text (diacritic-insensitive so both "pokemon" and "pokémon" match)
                if (!string.IsNullOrEmpty(search))
                {
                    string t1 = GameTitleMatcher.StripDiacritics(c.LocalTitle ?? "").ToLowerInvariant();
                    string t2 = GameTitleMatcher.StripDiacritics(c.BackloggdTitle ?? "").ToLowerInvariant();
                    string idStr = c.AppId.ToString();
                    if (!t1.Contains(search) && !t2.Contains(search) && !idStr.Contains(search)) continue;
                }

                var item = new ListViewItem(c.LocalTitle ?? ("App " + c.AppId));
                item.Tag = c;

                string sourceState;
                if (isSwitchEntry)
                {
                    sourceState = (c.PlatformId == 508 || NintendoSwitchClient.IsSwitch2Game(c.SwitchApplicationId, c.LocalTitle))
                        ? "Nintendo Switch 2"
                        : "Nintendo Switch";
                }
                else
                {
                    sourceState = c.IsInstalled ? "Steam (Installed)" : "Steam (Owned)";
                    if (c.IsHiddenInSteam) sourceState += " [Hidden]";
                }
                item.SubItems.Add(sourceState);

                if (!string.IsNullOrEmpty(c.BackloggdGameId))
                {
                    item.SubItems.Add(string.Format("{0} (ID {1})", c.BackloggdTitle ?? c.LocalTitle, c.BackloggdGameId));
                }
                else
                {
                    item.SubItems.Add("Unmapped — Double-click to map");
                }

                string platEdition = c.GetPlatformDisplayName();
                if (!string.IsNullOrEmpty(c.EditionTitle))
                {
                    platEdition += " • " + c.EditionTitle;
                }
                item.SubItems.Add(platEdition);

                string ownStore = c.GetOwnershipDisplayName();
                string storeName = c.GetStorefrontDisplayName();
                if (!string.IsNullOrEmpty(storeName) && storeName != "—" && c.OwnershipId == 1)
                {
                    ownStore += " (" + storeName + ")";
                }
                item.SubItems.Add(ownStore);

                if (c.Excluded)
                {
                    item.SubItems.Add("Excluded");
                    item.ForeColor = TextMuted;
                }
                else if (c.Synced)
                {
                    item.SubItems.Add(string.Format("✓ In Library (#{0})", c.BackloggdLibraryEntryId ?? "synced"));
                    item.ForeColor = Color.FromArgb(170, 240, 195);
                }
                else if (!string.IsNullOrEmpty(c.SyncError))
                {
                    item.SubItems.Add("✗ " + c.SyncError);
                    item.ForeColor = Color.FromArgb(255, 195, 140);
                }
                else if (!string.IsNullOrEmpty(c.BackloggdGameId))
                {
                    item.SubItems.Add("Ready to Sync");
                    item.ForeColor = Color.FromArgb(180, 215, 255);
                }
                else
                {
                    item.SubItems.Add("Needs Mapping");
                    item.ForeColor = TextLight;
                }

                _lvCollection.Items.Add(item);
            }

            RestoreListViewState(_lvCollection, stateSnap, getCollectionKey);
            _lvCollection.EndUpdate();
            RestoreListViewScrollPosition(_lvCollection, stateSnap, getCollectionKey);

            _lblCollectionSummary.Text = string.Format(
                "Collection: {0} owned ({1} Steam, {2} Switch)  •  {3} mapped  •  {4} synced to Backloggd Library  (Showing {5})",
                totalOwned, totalSteam, totalSwitch, totalMapped, totalSynced, _lvCollection.Items.Count);
        }

        private void BtnEditCollectionEntry_Click(object sender, EventArgs e)
        {
            if (_lvCollection.SelectedItems.Count == 0)
            {
                MessageBox.Show("Select a game in your collection first (or double-click any row) to map its Backloggd game or configure Physical/Digital & Edition options.",
                    "Select Game", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var selected = _lvCollection.SelectedItems[0].Tag as CollectionEntry;
            if (selected == null) return;

            CollectionEntryDialog.Show(
                this,
                this.Font,
                _settings,
                selected,
                (updatedEntry, syncImmediately) =>
                {
                    RefreshMappingsList();
                    if (!_collectionLoadedOnce) RefreshCollectionList();
                    RefreshSessionsList();

                    if (syncImmediately && !string.IsNullOrEmpty(updatedEntry.BackloggdGameId))
                    {
                        SyncCollectionEntriesBatch(new List<CollectionEntry> { updatedEntry });
                    }
                });
        }

        private void BtnTogglePhysicalDigital_Click(object sender, EventArgs e)
        {
            if (_lvCollection.SelectedItems.Count == 0)
            {
                MessageBox.Show("Select one or more games in the collection list to toggle between Physical and Digital.",
                    "Select Game(s)", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var collection = DataStore.LoadCollection();
            int changed = 0;

            foreach (ListViewItem lvi in _lvCollection.SelectedItems)
            {
                var entry = lvi.Tag as CollectionEntry;
                if (entry == null) continue;

                var stored = collection.Find(x => x.Id == entry.Id);
                if (stored == null) continue;

                if (stored.OwnershipId == 0)
                {
                    // Switch from Physical -> Digital
                    stored.OwnershipId = 1;
                    bool isSwitchEntry = (stored.Source ?? "").IndexOf("Switch", StringComparison.OrdinalIgnoreCase) >= 0;
                    stored.StorefrontId = isSwitchEntry ? 8 : 1;
                }
                else
                {
                    // Switch from Digital -> Physical
                    stored.OwnershipId = 0;
                    stored.StorefrontId = null;
                }
                stored.Synced = false;
                stored.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                changed++;
            }

            if (changed > 0)
            {
                DataStore.SaveCollection(collection);
                RefreshCollectionList();
            }
        }

        private void BtnAutoMatchCollection_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_settings.SessionCookie))
            {
                MessageBox.Show("Please connect your _backloggd_session cookie in the Settings tab first!",
                    "Cookie Needed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _tabControl.SelectedIndex = 3;
                return;
            }

            var targetEntries = new List<CollectionEntry>();
            if (_lvCollection.SelectedItems.Count > 0)
            {
                foreach (ListViewItem item in _lvCollection.SelectedItems)
                {
                    var ce = item.Tag as CollectionEntry;
                    if (ce != null && string.IsNullOrEmpty(ce.BackloggdGameId) && !ce.Excluded)
                    {
                        targetEntries.Add(ce);
                    }
                }
            }

            if (targetEntries.Count == 0)
            {
                // Match all currently visible unmapped rows
                foreach (ListViewItem item in _lvCollection.Items)
                {
                    var ce = item.Tag as CollectionEntry;
                    if (ce != null && string.IsNullOrEmpty(ce.BackloggdGameId) && !ce.Excluded)
                    {
                        targetEntries.Add(ce);
                    }
                }
            }

            if (targetEntries.Count == 0)
            {
                MessageBox.Show("All currently visible games in the list are already mapped to Backloggd!",
                    "Already Mapped", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _btnAutoMatchCollection.Enabled = false;
            _btnAutoMatchCollection.Text = "Auto-Matching...";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                int total = targetEntries.Count;
                int matched = _tracker.SyncService.AutoMatchCollectionEntries(targetEntries, (idx, tot, title) =>
                {
                    if (IsHandleCreated && !IsDisposed)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            _lblCollectionSummary.Text = string.Format("Auto-matching ({0}/{1}): {2}...", idx, tot, title);
                        }));
                    }
                });

                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(new Action(() =>
                    {
                        _btnAutoMatchCollection.Enabled = true;
                        _btnAutoMatchCollection.Text = "✨ Auto-Match Unmapped";
                        RefreshMappingsList();
                        if (!_collectionLoadedOnce) RefreshCollectionList();
                        MessageBox.Show(
                            string.Format("Auto-match complete!\r\n• Matched {0} of {1} game(s) to Backloggd.", matched, total),
                            "Collection Auto-Match", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
            });
        }

        private void BtnSyncSelectedCollection_Click(object sender, EventArgs e)
        {
            if (_lvCollection.SelectedItems.Count == 0)
            {
                MessageBox.Show("Select one or more games in the collection list to sync to your Backloggd Library.",
                    "Select Game(s)", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var toSync = new List<CollectionEntry>();
            foreach (ListViewItem lvi in _lvCollection.SelectedItems)
            {
                var ce = lvi.Tag as CollectionEntry;
                if (ce != null && !ce.Excluded)
                {
                    toSync.Add(ce);
                }
            }

            if (toSync.Count == 1 && string.IsNullOrEmpty(toSync[0].BackloggdGameId))
            {
                BtnEditCollectionEntry_Click(sender, e);
                return;
            }

            SyncCollectionEntriesBatch(toSync);
        }

        private void BtnSyncAllCollection_Click(object sender, EventArgs e)
        {
            var toSync = new List<CollectionEntry>();
            foreach (ListViewItem lvi in _lvCollection.Items)
            {
                var ce = lvi.Tag as CollectionEntry;
                if (ce != null && !ce.Excluded && !string.IsNullOrEmpty(ce.BackloggdGameId) && !ce.Synced)
                {
                    toSync.Add(ce);
                }
            }

            if (toSync.Count == 0)
            {
                MessageBox.Show("All mapped games currently shown in the list are already synced to your Backloggd Library!\r\n\r\n" +
                    "Tip: Use '✨ Auto-Match Unmapped' or double-click any unmapped game to associate it with Backloggd first.",
                    "Collection Up to Date", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var confirm = MessageBox.Show(
                string.Format("Sync {0} mapped game(s) to your Backloggd Library now?\r\n\r\n" +
                    "Note: This adds them to your Backloggd Collection / Library Entries with their configured Platform, Ownership (Digital/Physical), Storefront, and Edition — without marking unplayed games as 'Played'.",
                    toSync.Count),
                "Confirm Collection Sync",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            SyncCollectionEntriesBatch(toSync);
        }

        private void SyncCollectionEntriesBatch(List<CollectionEntry> entriesToSync)
        {
            if (string.IsNullOrWhiteSpace(_settings.SessionCookie))
            {
                MessageBox.Show("Please connect your _backloggd_session cookie in the Settings tab first!",
                    "Cookie Needed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _tabControl.SelectedIndex = 3;
                return;
            }

            _btnSyncSelectedCollection.Enabled = false;
            _btnSyncAllCollection.Enabled = false;
            _btnSyncAllCollection.Text = "Syncing Library...";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                int syncedCount = 0;
                int failedCount = 0;
                int total = entriesToSync.Count;

                _tracker.SyncService.SyncCollectionEntries(entriesToSync, out syncedCount, out failedCount, (idx, tot, title) =>
                {
                    if (IsHandleCreated && !IsDisposed)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            _lblCollectionSummary.Text = string.Format("Syncing to Backloggd Library ({0}/{1}): {2}...",
                                idx, tot, title);
                        }));
                    }
                });

                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(new Action(() =>
                    {
                        _btnSyncSelectedCollection.Enabled = true;
                        _btnSyncAllCollection.Enabled = true;
                        _btnSyncAllCollection.Text = "🔄 Sync All Mapped";
                        RefreshCollectionList();

                        if (total > 1 || failedCount > 0)
                        {
                            MessageBox.Show(
                                string.Format("Backloggd Library sync finished!\r\n• Synced: {0} game(s)\r\n• Failed/Skipped: {1} game(s)",
                                    syncedCount, failedCount),
                                "Library Sync Complete",
                                MessageBoxButtons.OK,
                                failedCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                        }
                    }));
                }
            });
        }

        private void BuildNintendoSwitchTab(TabPage page)
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BgCard,
                Padding = new Padding(20),
                AutoScroll = true
            };

            var lblHeader = new Label
            {
                Text = "Nintendo Switch / Switch 2 — Official Daily Game History Sync",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                ForeColor = TextLight,
                AutoSize = true,
                Location = new Point(20, 16)
            };

            var lblDesc = new Label
            {
                Text = "Syncs your exact day-by-day minutes per game across all Switch 1 & Switch 2 titles (2017–present) directly from\r\n" +
                       "Nintendo's official Game History API — works automatically on any Nintendo Account with no Parental Controls setup required!",
                ForeColor = TextMuted,
                AutoSize = true,
                Location = new Point(20, 44)
            };

            var lblStep1 = new Label
            {
                Text = "Step 1: Click below to open Nintendo Account sign-in in your browser:",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = TextLight,
                AutoSize = true,
                Location = new Point(20, 102)
            };

            _btnNintendoOpenLogin = CreateStyledButton("1. Open Nintendo Account Sign-In", AccentBlue, 280, 34);
            _btnNintendoOpenLogin.Location = new Point(20, 128);
            _btnNintendoOpenLogin.Click += BtnNintendoOpenStoreLogin_Click;

            var lblStep2 = new Label
            {
                Text = "Step 2: On the Nintendo page, right-click the red 'Select this person' button -> 'Copy link address', and paste it here:",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = TextLight,
                AutoSize = true,
                Location = new Point(20, 178)
            };

            _txtNintendoAuthUrl = new TextBox
            {
                Location = new Point(20, 204),
                Width = 600,
                BackColor = BgInput,
                ForeColor = TextLight,
                BorderStyle = BorderStyle.FixedSingle
            };

            _btnNintendoConnect = CreateStyledButton("2. Connect Nintendo Account", AccentPink, 220, 30);
            _btnNintendoConnect.Location = new Point(632, 201);
            _btnNintendoConnect.Click += BtnNintendoConnect_Click;

            _lblNintendoStatus = new Label
            {
                Text = GetNintendoConnectionStatusText(),
                ForeColor = !string.IsNullOrEmpty(_settings.NintendoStoreSessionToken) ? SuccessGreen : TextMuted,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(20, 244)
            };

            var lblStep3 = new Label
            {
                Text = "3. Sync Switch Daily & All-Time Playtime to Backloggd Diary (Platform: Nintendo Switch)",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = TextLight,
                AutoSize = true,
                Location = new Point(20, 288)
            };

            _btnNintendoSyncNow = CreateStyledButton("🔄 Sync Nintendo Switch Playtime Now", AccentPink, 310, 38);
            _btnNintendoSyncNow.Location = new Point(20, 320);
            _btnNintendoSyncNow.Click += BtnNintendoSyncNow_Click;

            _chkNintendoAutoSync = new CheckBox
            {
                Text = "Automatically check & sync Nintendo Switch daily playtime once a day in the background",
                Checked = _settings.NintendoAutoSync,
                Location = new Point(20, 374),
                AutoSize = true
            };
            _chkNintendoAutoSync.CheckedChanged += (s, e) =>
            {
                _settings.NintendoAutoSync = _chkNintendoAutoSync.Checked;
                DataStore.SaveSettings(_settings);
            };

            card.Controls.AddRange(new Control[]
            {
                lblHeader, lblDesc, lblStep1, _btnNintendoOpenLogin,
                lblStep2, _txtNintendoAuthUrl, _btnNintendoConnect, _lblNintendoStatus,
                lblStep3, _btnNintendoSyncNow, _chkNintendoAutoSync
            });

            page.Controls.Add(card);
        }

        private string GetNintendoConnectionStatusText()
        {
            if (!string.IsNullOrEmpty(_settings.NintendoStoreSessionToken))
            {
                return string.Format("✓ Connected to Nintendo Account ({0})", _settings.NintendoAccountNickname ?? "Linked");
            }
            return "Not connected to Nintendo Account yet.";
        }

        private void BtnNintendoOpenStoreLogin_Click(object sender, EventArgs e)
        {
            try
            {
                _nintendoChallenge = NintendoSwitchClient.CreateLoginChallenge(true);
                Process.Start(_nintendoChallenge.LoginUrl);
                _lblNintendoStatus.Text = "Browser opened! Sign in, right-click 'Select this person' -> 'Copy link address', and paste in Step 2.";
                _lblNintendoStatus.ForeColor = WarnOrange;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open browser: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnNintendoConnect_Click(object sender, EventArgs e)
        {
            if (_nintendoChallenge == null || string.IsNullOrEmpty(_nintendoChallenge.CodeVerifier))
            {
                MessageBox.Show("Please click '1. Open Nintendo Account Sign-In' above first so a login challenge is generated.",
                    "Step 1 Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string pasted = _txtNintendoAuthUrl.Text.Trim();
            if (string.IsNullOrEmpty(pasted))
            {
                MessageBox.Show("Please paste the 'npf...://auth#...' link copied from the 'Select this person' button.",
                    "Paste Link", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _btnNintendoConnect.Enabled = false;
            _btnNintendoConnect.Text = "Connecting...";
            _lblNintendoStatus.Text = "Exchanging token with Nintendo...";
            _lblNintendoStatus.ForeColor = TextMuted;

            string verifier = _nintendoChallenge.CodeVerifier;
            string clientId = _nintendoChallenge.ClientId;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string sessionToken = NintendoSwitchClient.ExchangeCodeForSessionToken(pasted, verifier, clientId);
                    List<NintendoAllTimeGameRecord> allTime;
                    List<NintendoDailyGameRecord> recent;
                    string nick;
                    NintendoSwitchClient.FetchStorePlayHistory(sessionToken, out allTime, out recent, out nick);
                    _settings.NintendoStoreSessionToken = sessionToken;
                    if (!string.IsNullOrEmpty(nick)) _settings.NintendoAccountNickname = nick;
                    DataStore.SaveSettings(_settings);

                    BeginInvoke(new Action(() =>
                    {
                        _btnNintendoConnect.Enabled = true;
                        _btnNintendoConnect.Text = "2. Connect Nintendo Account";
                        _txtNintendoAuthUrl.Text = "";
                        _lblNintendoStatus.Text = string.Format("✓ Connected ({0})! Found {1} games in history. Syncing day-by-day playtime now...",
                            _settings.NintendoAccountNickname ?? "Connected", allTime.Count);
                        _lblNintendoStatus.ForeColor = SuccessGreen;
                        RunNintendoSwitchSync(false);
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(() =>
                    {
                        _btnNintendoConnect.Enabled = true;
                        _btnNintendoConnect.Text = "2. Connect Nintendo Account";
                        _lblNintendoStatus.Text = "✗ Connection failed: " + ex.Message;
                        _lblNintendoStatus.ForeColor = Color.FromArgb(231, 76, 60);
                    }));
                }
            });
        }

        private void BtnNintendoSyncNow_Click(object sender, EventArgs e)
        {
            RunNintendoSwitchSync(false);
        }

        private void RunNintendoSwitchSync(bool fetchEntireHistory)
        {
            if (string.IsNullOrWhiteSpace(_settings.NintendoStoreSessionToken))
            {
                MessageBox.Show("Please connect your Nintendo Account in Steps 1 & 2 above first.",
                    "Nintendo Account Not Connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _btnNintendoSyncNow.Enabled = false;
            _btnNintendoSyncNow.Text = "Syncing Switch Playtime...";
            _lblNintendoStatus.Text = "Fetching Nintendo Switch game history & syncing day-by-day playtime to Backloggd...";
            _lblNintendoStatus.ForeColor = TextMuted;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int count;
                    string msg = _tracker.FetchAndSyncNintendoSwitchSummaries(true, out count, fetchEntireHistory);
                    _lastNintendoAutoSyncTime = DateTime.Now;
                    _settings.LastNintendoAutoSyncDate = DateTime.Now.ToString("yyyy-MM-dd");
                    DataStore.SaveSettings(_settings);
                    BeginInvoke(new Action(() =>
                    {
                        _btnNintendoSyncNow.Enabled = true;
                        _btnNintendoSyncNow.Text = "🔄 Sync Nintendo Switch Playtime Now";
                        _lblNintendoStatus.Text = "✓ " + msg;
                        _lblNintendoStatus.ForeColor = SuccessGreen;
                        RefreshSessionsList();
                        RefreshMappingsList();
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(() =>
                    {
                        _btnNintendoSyncNow.Enabled = true;
                        _btnNintendoSyncNow.Text = "🔄 Sync Nintendo Switch Playtime Now";
                        _lblNintendoStatus.Text = "✗ Switch sync error: " + ex.Message;
                        _lblNintendoStatus.ForeColor = Color.FromArgb(231, 76, 60);
                    }));
                }
            });
        }

        private void BuildSettingsTab(TabPage page)
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BgCard,
                Padding = new Padding(20),
                AutoScroll = true
            };

            var lblAuthHeader = new Label
            {
                Text = "1. Backloggd Session Authentication",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = TextLight,
                AutoSize = true,
                Location = new Point(20, 18)
            };

            var lblInstructions = new Label
            {
                Text = "Because Backloggd has no official public API, this app uses your browser's '_backloggd_session' cookie:\r\n" +
                       "  1. Log into backloggd.com in your browser and press F12 (Developer Tools).\r\n" +
                       "  2. Open the 'Application' tab (Chrome/Edge) or 'Storage' tab (Firefox) -> Cookies -> https://backloggd.com.\r\n" +
                       "  3. Copy the Value of '_backloggd_session' and paste it below. (CSRF token & User ID are fetched automatically!)",
                ForeColor = TextMuted,
                AutoSize = true,
                Location = new Point(20, 46)
            };

            var lblCookie = new Label
            {
                Text = "_backloggd_session Cookie:",
                Location = new Point(20, 128),
                AutoSize = true
            };

            _txtCookie = new TextBox
            {
                Location = new Point(20, 152),
                Width = 620,
                Text = _settings.SessionCookie,
                BackColor = BgInput,
                ForeColor = TextLight,
                BorderStyle = BorderStyle.FixedSingle
            };

            _btnVerifyCookie = CreateStyledButton("Verify & Connect", AccentPink, 155, 30);
            _btnVerifyCookie.Location = new Point(652, 149);
            _btnVerifyCookie.Click += BtnVerifyCookie_Click;

            _lblVerifyResult = new Label
            {
                Text = !string.IsNullOrEmpty(_settings.Username)
                    ? string.Format("Connected as @{0} (User ID: {1})", _settings.Username, _settings.UserId ?? "auto")
                    : "Not verified yet.",
                ForeColor = !string.IsNullOrEmpty(_settings.Username) ? SuccessGreen : TextMuted,
                AutoSize = true,
                Location = new Point(20, 188)
            };

            var lblPrefsHeader = new Label
            {
                Text = "2. Tracking & Auto-Sync Preferences",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = TextLight,
                AutoSize = true,
                Location = new Point(20, 232)
            };

            var lblMinSec = new Label
            {
                Text = "Minimum session duration to record (seconds):",
                Location = new Point(20, 268),
                AutoSize = true
            };

            _numMinSeconds = new NumericUpDown
            {
                Location = new Point(320, 265),
                Width = 90,
                Minimum = 5,
                Maximum = 3600,
                Value = Math.Max(5, Math.Min(3600, _settings.MinSessionSeconds)),
                BackColor = BgInput,
                ForeColor = TextLight
            };

            _chkAutoSync = new CheckBox
            {
                Text = "Automatically sync playtime to Backloggd diary when a game closes",
                Checked = _settings.AutoSyncOnExit,
                Location = new Point(20, 305),
                AutoSize = true
            };

            _chkSplitMidnight = new CheckBox
            {
                Text = "Split sessions across midnight so each calendar day gets its exact hours",
                Checked = _settings.SplitAtMidnight,
                Location = new Point(20, 337),
                AutoSize = true
            };

            _chkNotifications = new CheckBox
            {
                Text = "Show Windows tray notifications when games start and sync",
                Checked = _settings.ShowNotifications,
                Location = new Point(20, 369),
                AutoSize = true
            };

            _chkRunOnStartup = new CheckBox
            {
                Text = "Start Backloggd AutoSync automatically when I log into Windows (minimized to tray)",
                Checked = IsStartupEnabled(),
                Location = new Point(20, 401),
                AutoSize = true
            };

            _btnSaveSettings = CreateStyledButton("Save Settings", AccentBlue, 160, 36);
            _btnSaveSettings.Location = new Point(20, 445);
            _btnSaveSettings.Click += BtnSaveSettings_Click;

            card.Controls.AddRange(new Control[]
            {
                lblAuthHeader, lblInstructions, lblCookie, _txtCookie, _btnVerifyCookie,
                _lblVerifyResult, lblPrefsHeader, lblMinSec, _numMinSeconds,
                _chkAutoSync, _chkSplitMidnight, _chkNotifications, _chkRunOnStartup, _btnSaveSettings
            });

            page.Controls.Add(card);
        }

        private Button CreateStyledButton(string text, Color bgColor, int width, int height)
        {
            return UITheme.CreateStyledButton(text, bgColor, width, height);
        }

        private void OnUnmappedGamePromptRequested(SessionEntry entry, string rawTitle, List<GameSearchResult> initialResults)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<SessionEntry, string, List<GameSearchResult>>(OnUnmappedGamePromptRequested),
                    entry, rawTitle, initialResults);
                return;
            }

            RefreshSessionsList();
            ShowAutoSuggestModalForSession(entry, rawTitle, initialResults);
        }

        private void BtnPickGameForSession_Click(object sender, EventArgs e)
        {
            if (_lvSessions.SelectedItems.Count == 0)
            {
                MessageBox.Show("Select a session row first (or double-click any session) to pick or change its Backloggd game.",
                    "Select Session", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var entry = _lvSessions.SelectedItems[0].Tag as SessionEntry;
            if (entry == null) return;

            string rawTitle = entry.AppId > 0 ? _tracker.ResolveSteamGameTitle(entry.AppId) : (entry.GameTitle ?? "");
            ShowAutoSuggestModalForSession(entry, rawTitle, null);
        }

        private void ShowAutoSuggestModalForSession(SessionEntry entry, string rawTitle, List<GameSearchResult> preloadedResults)
        {
            ShowMainWindow();
            UnmappedGameDialog.Show(
                this,
                this.Font,
                _settings,
                entry,
                rawTitle,
                preloadedResults,
                idsToBatchSync => ThreadPool.QueueUserWorkItem(_ => _tracker.BatchSyncSessionsByGame(idsToBatchSync, false)),
                () =>
                {
                    RefreshMappingsList();
                    RefreshSessionsList();
                });
        }

        private void PollTimer_Tick(object sender, EventArgs e)
        {
            _tracker.Poll(_settings);
            UpdateLiveStatusUi();

            // Check Nintendo Switch daily history once a day in background if enabled
            string todayStr = DateTime.Now.ToString("yyyy-MM-dd");
            if (_settings.NintendoAutoSync &&
                !string.IsNullOrWhiteSpace(_settings.NintendoStoreSessionToken) &&
                _settings.LastNintendoAutoSyncDate != todayStr &&
                (DateTime.Now - _lastNintendoAutoSyncTime).TotalMinutes >= 60)
            {
                _lastNintendoAutoSyncTime = DateTime.Now;
                _settings.LastNintendoAutoSyncDate = todayStr;
                DataStore.SaveSettings(_settings);
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        int count;
                        _tracker.FetchAndSyncNintendoSwitchSummaries(false, out count);
                        if (count > 0 && IsHandleCreated && !IsDisposed)
                        {
                            BeginInvoke(new Action(() =>
                            {
                                RefreshSessionsList();
                                RefreshMappingsList();
                            }));
                        }
                    }
                    catch { }
                });
            }
        }

        private void UpdateLiveStatusUi()
        {
            if (_tracker.CurrentAppId > 0)
            {
                string dur = FormatDuration(_tracker.CurrentElapsedSeconds);
                string trayText = string.Format("Playing: {0} ({1})", _tracker.CurrentTitle, dur);
                if (trayText.Length > 63) trayText = trayText.Substring(0, 63);

                if (_trayIcon != null)
                {
                    _trayIcon.Text = trayText;
                }
                if (_trayStatusItem != null)
                {
                    _trayStatusItem.Text = string.Format("Playing: {0} ({1})", _tracker.CurrentTitle, dur);
                }

                if (this.Visible && this.WindowState != FormWindowState.Minimized)
                {
                    _lblLiveStatus.Text = string.Format("Playing: {0}  ({1})", _tracker.CurrentTitle, dur);
                    _lblLiveStatus.ForeColor = SuccessGreen;
                    _lblLiveTimer.Text = string.Format("Steam AppID: {0}  •  Started at {1:HH:mm}  •  Checks every 1 min",
                        _tracker.CurrentAppId, _tracker.SessionStart);
                }
            }
            else
            {
                if (_trayIcon != null)
                {
                    _trayIcon.Text = "Backloggd AutoSync - Idle";
                }
                if (_trayStatusItem != null)
                {
                    _trayStatusItem.Text = "Status: Idle";
                }

                if (this.Visible && this.WindowState != FormWindowState.Minimized)
                {
                    _lblLiveStatus.Text = "Idle — Waiting for a Steam game to launch...";
                    _lblLiveStatus.ForeColor = TextLight;
                    _lblLiveTimer.Text = "Lightweight mode: checks Steam once per minute (minimize or close window to send to tray)";
                }
            }
        }

        private void OnGameStarted(int appId, string title)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, string>(OnGameStarted), appId, title);
                return;
            }
            if (_settings.ShowNotifications)
            {
                _trayIcon.ShowBalloonTip(3000, "Tracking Steam Game",
                    string.Format("Now timing '{0}' (AppID {1})", title, appId), ToolTipIcon.Info);
            }
        }

        private void OnSessionCompleted(SessionEntry entry)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<SessionEntry>(OnSessionCompleted), entry);
                return;
            }
            RefreshSessionsList();
        }

        private void OnSessionSynced(SessionEntry entry)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<SessionEntry>(OnSessionSynced), entry);
                return;
            }
            RefreshSessionsList();
            RefreshMappingsList();

            if (_settings.ShowNotifications)
            {
                if (entry.Synced)
                {
                    int h = entry.TotalDayMinutesOnBackloggd / 60;
                    int m = entry.TotalDayMinutesOnBackloggd % 60;
                    _trayIcon.ShowBalloonTip(4000, "Synced to Backloggd Diary!",
                        string.Format("{0} ({1}) logged! Day total on Backloggd: {2}h {3}m.",
                            entry.GameTitle, FormatDuration(entry.DurationSeconds), h, m),
                        ToolTipIcon.Info);
                }
                else
                {
                    _trayIcon.ShowBalloonTip(4000, "Backloggd Sync Queued",
                        string.Format("{0}: {1}", entry.GameTitle, entry.SyncError ?? "Saved locally"),
                        ToolTipIcon.Warning);
                }
            }
        }

        private void RefreshSessionsList()
        {
            var sessions = DataStore.LoadSessions();
            string todayStr = DateTime.Now.ToString("yyyy-MM-dd");
            int todaySeconds = 0;
            var todayGames = new HashSet<int>();

            Func<ListViewItem, string> getSessionKey = lvi =>
            {
                var se = lvi != null ? lvi.Tag as SessionEntry : null;
                return se != null ? se.Id : null;
            };
            var stateSnap = CaptureListViewState(_lvSessions, getSessionKey);

            _lvSessions.BeginUpdate();
            _lvSessions.Items.Clear();

            foreach (var s in sessions)
            {
                if (s.DateStr == todayStr)
                {
                    todaySeconds += s.DurationSeconds;
                    todayGames.Add(s.AppId);
                }

                var item = new ListViewItem(s.DateStr);
                item.Tag = s;
                item.SubItems.Add(s.GameTitle ?? ("App " + s.AppId));
                string platText = string.Equals(s.Platform, "Switch", StringComparison.OrdinalIgnoreCase)
                    ? NintendoSwitchClient.GetSwitchPlatformName(s.SwitchApplicationId, s.GameTitle)
                    : (s.AppId > 0 ? ("Steam (" + s.AppId + ")") : "Manual");
                item.SubItems.Add(platText);
                item.SubItems.Add(FormatDuration(s.DurationSeconds));

                if (s.Synced)
                {
                    item.SubItems.Add(s.AccumulatedOnBackloggd ? "Synced (Accumulated)" : "Synced to Diary");
                    int h = s.TotalDayMinutesOnBackloggd / 60;
                    int m = s.TotalDayMinutesOnBackloggd % 60;
                    item.SubItems.Add(string.Format("Day total: {0}h {1}m (ID {2})", h, m, s.BackloggdGameId));
                    item.ForeColor = Color.FromArgb(170, 240, 195);
                }
                else if (!string.IsNullOrEmpty(s.SyncError))
                {
                    item.SubItems.Add("Needs Retry / Pick Game");
                    item.SubItems.Add(s.SyncError);
                    item.ForeColor = Color.FromArgb(255, 195, 140);
                }
                else
                {
                    item.SubItems.Add("Syncing / Local");
                    item.SubItems.Add("Waiting to sync...");
                }

                _lvSessions.Items.Add(item);
            }

            RestoreListViewState(_lvSessions, stateSnap, getSessionKey);
            _lvSessions.EndUpdate();
            RestoreListViewScrollPosition(_lvSessions, stateSnap, getSessionKey);

            _lblTodaySummary.Text = string.Format("Today ({0}): {1} played across {2} game(s)",
                todayStr, FormatDuration(todaySeconds), todayGames.Count);
        }

        private void RefreshMappingsList()
        {
            if (_collectionLoadedOnce && _lvCollection != null)
            {
                RefreshCollectionList();
            }
        }

        private void UpdateAccountBadge()
        {
            if (!string.IsNullOrEmpty(_settings.Username))
            {
                _lblAccountBadge.Text = string.Format("Backloggd: @{0}\r\nAuto-Sync Active", _settings.Username);
                _lblAccountBadge.ForeColor = SuccessGreen;
            }
            else if (!string.IsNullOrEmpty(_settings.SessionCookie))
            {
                _lblAccountBadge.Text = "Backloggd: Cookie Saved\r\nClick Verify in Settings";
                _lblAccountBadge.ForeColor = WarnOrange;
            }
            else
            {
                _lblAccountBadge.Text = "Backloggd Not Connected\r\nSet Cookie in Settings Tab";
                _lblAccountBadge.ForeColor = WarnOrange;
            }
        }

        private void SyncAllPendingSessions()
        {
            var sessions = DataStore.LoadSessions();
            var pending = sessions.FindAll(s => !s.Synced);
            if (pending.Count == 0)
            {
                MessageBox.Show("All recorded sessions are already synced to Backloggd!", "Up to Date",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _btnSyncPending.Enabled = false;
            _btnSyncPending.Text = "Syncing...";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var ids = new List<string>();
                foreach (var p in pending) ids.Add(p.Id);
                _tracker.BatchSyncSessionsByGame(ids, true);
                BeginInvoke(new Action(() =>
                {
                    _btnSyncPending.Enabled = true;
                    _btnSyncPending.Text = "Sync Pending";
                    RefreshSessionsList();
                }));
            });
        }

        private void BtnManualLog_Click(object sender, EventArgs e)
        {
            ManualLogDialog.Show(
                this,
                this.Font,
                _settings,
                sessionId => ThreadPool.QueueUserWorkItem(_ => _tracker.SyncSessionEntryAsync(sessionId, true)),
                () => RefreshSessionsList());
        }

        private void BtnDeleteSession_Click(object sender, EventArgs e)
        {
            if (_lvSessions.SelectedItems.Count == 0) return;
            var selected = _lvSessions.SelectedItems[0].Tag as SessionEntry;
            if (selected == null) return;

            var sessions = DataStore.LoadSessions();
            sessions.RemoveAll(s => s.Id == selected.Id);
            DataStore.SaveSessions(sessions);
            RefreshSessionsList();
        }

        private void BtnVerifyCookie_Click(object sender, EventArgs e)
        {
            string rawCookie = _txtCookie.Text.Trim();
            if (string.IsNullOrEmpty(rawCookie))
            {
                MessageBox.Show("Please paste your _backloggd_session cookie first.", "Missing Cookie", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _btnVerifyCookie.Enabled = false;
            _btnVerifyCookie.Text = "Verifying...";
            _lblVerifyResult.Text = "Connecting to Backloggd...";
            _lblVerifyResult.ForeColor = TextMuted;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var res = BackloggdClient.ValidateSession(rawCookie);
                BeginInvoke(new Action(() =>
                {
                    _btnVerifyCookie.Enabled = true;
                    _btnVerifyCookie.Text = "Verify & Connect";

                    if (res.Valid)
                    {
                        _settings.SessionCookie = BackloggdClient.NormalizeCookie(rawCookie);
                        _settings.CsrfToken = res.CsrfToken;
                        _settings.Username = res.Username;
                        _settings.UserId = res.UserId;
                        DataStore.SaveSettings(_settings);

                        _lblVerifyResult.Text = string.Format("✓ Verified! Logged in as @{0} (User ID: {1})", res.Username, res.UserId ?? "auto");
                        _lblVerifyResult.ForeColor = SuccessGreen;
                        UpdateAccountBadge();
                    }
                    else
                    {
                        _lblVerifyResult.Text = "✗ Verification failed: " + res.Error;
                        _lblVerifyResult.ForeColor = Color.FromArgb(231, 76, 60);
                    }
                }));
            });
        }

        private void BtnSaveSettings_Click(object sender, EventArgs e)
        {
            _settings.SessionCookie = BackloggdClient.NormalizeCookie(_txtCookie.Text);
            _settings.MinSessionSeconds = (int)_numMinSeconds.Value;
            _settings.AutoSyncOnExit = _chkAutoSync.Checked;
            _settings.SplitAtMidnight = _chkSplitMidnight.Checked;
            _settings.ShowNotifications = _chkNotifications.Checked;
            DataStore.SaveSettings(_settings);

            SetStartupEnabled(_chkRunOnStartup.Checked);
            UpdateAccountBadge();

            MessageBox.Show("Settings saved successfully!", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false))
                {
                    if (key != null)
                    {
                        return key.GetValue(StartupValueName) != null;
                    }
                }
            }
            catch { }
            return false;
        }

        private static void SetStartupEnabled(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string exePath = Application.ExecutablePath;
                            key.SetValue(StartupValueName, string.Format("\"{0}\" --minimized", exePath));
                        }
                        else
                        {
                            key.DeleteValue(StartupValueName, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DataStore.Log("Failed to update Windows startup registry: " + ex.Message);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
                this.Hide();
                this.ShowInTaskbar = false;
                if (_settings.ShowNotifications && _trayIcon != null)
                {
                    _trayIcon.ShowBalloonTip(2000, "Backloggd AutoSync Still Running",
                        "Minimized to system tray. Click the tray icon anytime to open.", ToolTipIcon.Info);
                }
                return;
            }
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            base.OnFormClosing(e);
        }

        public static string FormatDuration(int totalSeconds)
        {
            return TimeUtils.FormatDuration(totalSeconds);
        }
    }
}
