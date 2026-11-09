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
        private readonly System.Windows.Forms.Timer _mappingDebounceTimer;
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _trayStatusItem;
        private bool _allowClose = false;
        private bool _suppressSearchTextChanged = false;
        private int _searchRequestSeq = 0;

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

        // Tab 3: Game Mappings (Auto-Suggest UI)
        private ComboBox _cmbInstalledGames;
        private Button _btnAutoMatchAllInstalled;
        private TextBox _txtMapAppId;
        private TextBox _txtMapSearchQuery;
        private Label _lblSuggestionStatus;
        private ListBox _lstSuggestions;
        private Button _btnSaveMapping;
        private Button _btnDeleteMapping;
        private ListView _lvMappings;

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

        // Colors
        private readonly Color BgDark = Color.FromArgb(22, 24, 30);
        private readonly Color BgCard = Color.FromArgb(31, 34, 43);
        private readonly Color BgInput = Color.FromArgb(42, 46, 58);
        private readonly Color AccentPink = Color.FromArgb(234, 55, 122);
        private readonly Color AccentBlue = Color.FromArgb(72, 133, 237);
        private readonly Color TextLight = Color.FromArgb(238, 240, 246);
        private readonly Color TextMuted = Color.FromArgb(156, 163, 179);
        private readonly Color SuccessGreen = Color.FromArgb(46, 204, 113);
        private readonly Color WarnOrange = Color.FromArgb(243, 156, 18);

        public MainForm(bool startMinimized)
        {
            _settings = DataStore.LoadSettings();
            _tracker = new SteamTracker();

            _mappingDebounceTimer = new System.Windows.Forms.Timer { Interval = 380 };
            _mappingDebounceTimer.Tick += MappingDebounceTimer_Tick;

            InitializeComponent();
            SetupTrayIcon();

            _tracker.OnGameStarted += OnGameStarted;
            _tracker.OnSessionCompleted += OnSessionCompleted;
            _tracker.OnSessionSynced += OnSessionSynced;
            _tracker.OnUnmappedGamePromptRequested += OnUnmappedGamePromptRequested;

            // Recover any interrupted session from last run
            _tracker.RecoverCrashCheckpoint(_settings);

            RefreshSessionsList();
            RefreshMappingsList();
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
            this.Icon = LoadOrGenerateIcon();
            this.Text = "Backloggd AutoSync — Steam Daily Playtime Tracker";
            this.Size = new Size(960, 680);
            this.MinimumSize = new Size(860, 580);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = BgDark;
            this.ForeColor = TextLight;
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Header Panel
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 76,
                BackColor = BgCard,
                Padding = new Padding(18, 12, 18, 12)
            };

            _lblLiveStatus = new Label
            {
                Text = "Waiting for a Steam game to launch...",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = TextLight,
                AutoSize = true,
                Location = new Point(18, 14)
            };

            _lblLiveTimer = new Label
            {
                Text = "Monitoring Steam Registry (HKCU\\Software\\Valve\\Steam\\RunningAppID)",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = TextMuted,
                AutoSize = true,
                Location = new Point(18, 42)
            };

            _lblAccountBadge = new Label
            {
                Text = "Not Connected",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = WarnOrange,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Size = new Size(300, 48),
                Location = new Point(620, 14),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            _headerPanel.Controls.Add(_lblLiveStatus);
            _headerPanel.Controls.Add(_lblLiveTimer);
            _headerPanel.Controls.Add(_lblAccountBadge);

            // Tabs
            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(14, 6)
            };

            var tabSessions = new TabPage("Daily Diary & Sessions") { BackColor = BgDark, ForeColor = TextLight, Padding = new Padding(14) };
            var tabCollection = new TabPage("Collection / Library") { BackColor = BgDark, ForeColor = TextLight, Padding = new Padding(14) };
            var tabMappings = new TabPage("Game Mappings (Auto-Suggest)") { BackColor = BgDark, ForeColor = TextLight, Padding = new Padding(14) };
            var tabSwitch = new TabPage("Nintendo Switch Sync") { BackColor = BgDark, ForeColor = TextLight, Padding = new Padding(14) };
            var tabSettings = new TabPage("Backloggd Account & Settings") { BackColor = BgDark, ForeColor = TextLight, Padding = new Padding(14) };

            BuildSessionsTab(tabSessions);
            BuildCollectionTab(tabCollection);
            BuildMappingsTab(tabMappings);
            BuildNintendoSwitchTab(tabSwitch);
            BuildSettingsTab(tabSettings);

            _tabControl.TabPages.Add(tabSessions);
            _tabControl.TabPages.Add(tabCollection);
            _tabControl.TabPages.Add(tabMappings);
            _tabControl.TabPages.Add(tabSwitch);
            _tabControl.TabPages.Add(tabSettings);
            _tabControl.SelectedIndexChanged += (s, e) =>
            {
                if (_tabControl.SelectedTab == tabCollection && !_collectionLoadedOnce)
                {
                    _collectionLoadedOnce = true;
                    ScanAndRefreshCollection(true);
                }
                else if (_tabControl.SelectedTab == tabMappings && _cmbInstalledGames.Items.Count == 0)
                {
                    PopulateInstalledSteamGamesDropdown();
                }
            };

            this.Controls.Add(_tabControl);
            this.Controls.Add(_headerPanel);
        }

        private void BuildSessionsTab(TabPage page)
        {
            var topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = BgCard,
                Padding = new Padding(12, 8, 12, 8)
            };

            _lblTodaySummary = new Label
            {
                Text = "Today's Playtime: 0m across 0 games",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = TextLight,
                AutoSize = true,
                Location = new Point(14, 15)
            };

            _btnSyncPending = CreateStyledButton("Sync Pending", AccentPink, 130, 34);
            _btnSyncPending.Location = new Point(page.Width - 575, 9);
            _btnSyncPending.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnSyncPending.Click += (s, e) => SyncAllPendingSessions();

            _btnPickGameForSession = CreateStyledButton("Pick / Change Game", AccentBlue, 155, 34);
            _btnPickGameForSession.Location = new Point(page.Width - 437, 9);
            _btnPickGameForSession.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnPickGameForSession.Click += BtnPickGameForSession_Click;

            _btnManualLog = CreateStyledButton("+ Log Session", BgInput, 125, 34);
            _btnManualLog.Location = new Point(page.Width - 274, 9);
            _btnManualLog.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnManualLog.Click += BtnManualLog_Click;

            _btnDeleteSession = CreateStyledButton("Delete", BgInput, 80, 34);
            _btnDeleteSession.Location = new Point(page.Width - 141, 9);
            _btnDeleteSession.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnDeleteSession.Click += BtnDeleteSession_Click;

            topBar.Controls.Add(_lblTodaySummary);
            topBar.Controls.Add(_btnSyncPending);
            topBar.Controls.Add(_btnPickGameForSession);
            topBar.Controls.Add(_btnManualLog);
            topBar.Controls.Add(_btnDeleteSession);

            _lvSessions = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                BackColor = BgCard,
                ForeColor = TextLight,
                BorderStyle = BorderStyle.FixedSingle
            };
            _lvSessions.Columns.Add("Date", 95);
            _lvSessions.Columns.Add("Game Title", 240);
            _lvSessions.Columns.Add("Platform / ID", 105);
            _lvSessions.Columns.Add("Session Duration", 110);
            _lvSessions.Columns.Add("Backloggd Status", 145);
            _lvSessions.Columns.Add("Details (Double-click row to change game)", 215);
            _lvSessions.DoubleClick += BtnPickGameForSession_Click;

            var spacer = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = BgDark };

            page.Controls.Add(_lvSessions);
            page.Controls.Add(spacer);
            page.Controls.Add(topBar);
        }

        private void BuildCollectionTab(TabPage page)
        {
            var topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 92,
                BackColor = BgCard,
                Padding = new Padding(12, 8, 12, 8)
            };

            _lblCollectionSummary = new Label
            {
                Text = "Collection: Click 'Scan Libraries' or open this tab to load your owned Steam & Nintendo Switch games",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = TextLight,
                AutoSize = true,
                Location = new Point(14, 12)
            };

            _btnScanCollection = CreateStyledButton("🔄 Scan Libraries", AccentBlue, 132, 32);
            _btnScanCollection.Location = new Point(page.Width - 690, 8);
            _btnScanCollection.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnScanCollection.Click += (s, e) => ScanAndRefreshCollection(true);

            _btnAutoMatchCollection = CreateStyledButton("✨ Auto-Match View", AccentBlue, 145, 32);
            _btnAutoMatchCollection.Location = new Point(page.Width - 552, 8);
            _btnAutoMatchCollection.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnAutoMatchCollection.Click += BtnAutoMatchCollection_Click;

            _btnEditCollectionEntry = CreateStyledButton("Edit / Map Game...", BgInput, 138, 32);
            _btnEditCollectionEntry.Location = new Point(page.Width - 401, 8);
            _btnEditCollectionEntry.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnEditCollectionEntry.Click += BtnEditCollectionEntry_Click;

            _btnSyncSelectedCollection = CreateStyledButton("Sync Selected", AccentPink, 120, 32);
            _btnSyncSelectedCollection.Location = new Point(page.Width - 257, 8);
            _btnSyncSelectedCollection.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnSyncSelectedCollection.Click += BtnSyncSelectedCollection_Click;

            _btnSyncAllCollection = CreateStyledButton("Sync All Mapped", AccentPink, 125, 32);
            _btnSyncAllCollection.Location = new Point(page.Width - 131, 8);
            _btnSyncAllCollection.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnSyncAllCollection.Click += BtnSyncAllCollection_Click;

            var lblFilter = new Label
            {
                Text = "Show:",
                ForeColor = TextMuted,
                AutoSize = true,
                Location = new Point(14, 54)
            };

            _cmbCollectionFilter = new ComboBox
            {
                Location = new Point(60, 50),
                Width = 220,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = BgInput,
                ForeColor = TextLight
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
                Text = "Filter:",
                ForeColor = TextMuted,
                AutoSize = true,
                Location = new Point(292, 54)
            };

            _txtCollectionSearch = new TextBox
            {
                Location = new Point(338, 51),
                Width = 200,
                BackColor = BgInput,
                ForeColor = TextLight,
                BorderStyle = BorderStyle.FixedSingle
            };
            _txtCollectionSearch.TextChanged += (s, e) => RefreshCollectionList();

            _chkCollectionShowHidden = new CheckBox
            {
                Text = "Include Steam Hidden",
                Checked = false,
                AutoSize = true,
                ForeColor = TextMuted,
                Location = new Point(552, 53)
            };
            _chkCollectionShowHidden.CheckedChanged += (s, e) => RefreshCollectionList();

            _btnTogglePhysicalDigital = CreateStyledButton("💿/☁ Toggle Physical / Digital", BgInput, 195, 28);
            _btnTogglePhysicalDigital.Location = new Point(page.Width - 201, 49);
            _btnTogglePhysicalDigital.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnTogglePhysicalDigital.Click += BtnTogglePhysicalDigital_Click;

            topBar.Controls.AddRange(new Control[]
            {
                _lblCollectionSummary, _btnScanCollection, _btnAutoMatchCollection,
                _btnEditCollectionEntry, _btnSyncSelectedCollection, _btnSyncAllCollection,
                lblFilter, _cmbCollectionFilter, lblSearch, _txtCollectionSearch,
                _chkCollectionShowHidden, _btnTogglePhysicalDigital
            });

            _lvCollection = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = true,
                GridLines = false,
                BackColor = BgCard,
                ForeColor = TextLight,
                BorderStyle = BorderStyle.FixedSingle
            };
            _lvCollection.Columns.Add("Game Title (Local)", 225);
            _lvCollection.Columns.Add("Source / State", 135);
            _lvCollection.Columns.Add("Backloggd Association", 220);
            _lvCollection.Columns.Add("Platform & Edition", 145);
            _lvCollection.Columns.Add("Ownership & Store", 135);
            _lvCollection.Columns.Add("Backloggd Library Status", 155);
            _lvCollection.DoubleClick += BtnEditCollectionEntry_Click;

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
                        RefreshCollectionList();
                        RefreshMappingsList();
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
            string search = (_txtCollectionSearch.Text ?? "").Trim().ToLowerInvariant();
            bool showHidden = _chkCollectionShowHidden.Checked;

            int totalOwned = 0;
            int totalSteam = 0;
            int totalSwitch = 0;
            int totalMapped = 0;
            int totalSynced = 0;

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
                if (filterMode == "Steam Only (All Owned)" && !string.Equals(c.Source, "Steam", StringComparison.OrdinalIgnoreCase)) continue;
                if (filterMode == "Steam Installed Only" && (!string.Equals(c.Source, "Steam", StringComparison.OrdinalIgnoreCase) || !c.IsInstalled)) continue;
                if (filterMode == "Nintendo Switch Only" && !isSwitchEntry) continue;
                if (filterMode == "Unsynced Only" && (c.Synced || c.Excluded)) continue;
                if (filterMode == "Unmapped Only" && !string.IsNullOrEmpty(c.BackloggdGameId)) continue;
                if (filterMode == "Synced on Backloggd" && !c.Synced) continue;

                // Apply search text
                if (!string.IsNullOrEmpty(search))
                {
                    string t1 = (c.LocalTitle ?? "").ToLowerInvariant();
                    string t2 = (c.BackloggdTitle ?? "").ToLowerInvariant();
                    string idStr = c.AppId.ToString();
                    if (!t1.Contains(search) && !t2.Contains(search) && !idStr.Contains(search)) continue;
                }

                var item = new ListViewItem(c.LocalTitle ?? ("App " + c.AppId));
                item.Tag = c;

                string sourceState;
                if (isSwitchEntry)
                {
                    sourceState = "Nintendo Switch";
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

            _lvCollection.EndUpdate();

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
                    RefreshCollectionList();
                    RefreshMappingsList();
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
                _tabControl.SelectedIndex = 4;
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
                int matched = 0;
                int total = targetEntries.Count;
                var collection = DataStore.LoadCollection();
                var mappings = DataStore.LoadMappings();

                for (int i = 0; i < targetEntries.Count; i++)
                {
                    var t = targetEntries[i];
                    int idx = i + 1;
                    if (IsHandleCreated && !IsDisposed)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            _lblCollectionSummary.Text = string.Format("Auto-matching ({0}/{1}): {2}...", idx, total, t.LocalTitle);
                        }));
                    }

                    try
                    {
                        string clean = BackloggdClient.CleanGameTitle(t.LocalTitle);
                        var results = BackloggdClient.SearchGame(clean, _settings);
                        if (results.Count == 0 && clean != t.LocalTitle)
                        {
                            results = BackloggdClient.SearchGame(t.LocalTitle, _settings);
                        }
                        var best = BackloggdClient.SelectBestMatch(clean, results) ?? (results.Count > 0 ? results[0] : null);
                        if (best != null)
                        {
                            var stored = collection.Find(x => x.Id == t.Id);
                            if (stored != null)
                            {
                                stored.BackloggdGameId = best.GameId;
                                stored.BackloggdTitle = best.Title;
                                stored.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            }

                            if (t.AppId != 0)
                            {
                                mappings.RemoveAll(m => m.AppId == t.AppId);
                                mappings.Add(new GameMapping
                                {
                                    AppId = t.AppId,
                                    BackloggdGameId = best.GameId,
                                    SteamTitle = t.LocalTitle,
                                    BackloggdTitle = best.Title,
                                    ReleaseYear = best.ReleaseYear,
                                    ManualOverride = false,
                                    LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                                });
                            }
                            matched++;
                            if (matched % 10 == 0)
                            {
                                DataStore.SaveCollection(collection);
                                DataStore.SaveMappings(mappings);
                            }
                        }
                        Thread.Sleep(220);
                    }
                    catch { }
                }

                DataStore.SaveCollection(collection);
                DataStore.SaveMappings(mappings);

                if (IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(new Action(() =>
                    {
                        _btnAutoMatchCollection.Enabled = true;
                        _btnAutoMatchCollection.Text = "✨ Auto-Match Unmapped";
                        RefreshCollectionList();
                        RefreshMappingsList();
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
                _tabControl.SelectedIndex = 4;
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
                var collection = DataStore.LoadCollection();

                for (int i = 0; i < entriesToSync.Count; i++)
                {
                    var item = entriesToSync[i];
                    if (string.IsNullOrEmpty(item.BackloggdGameId))
                    {
                        failedCount++;
                        continue;
                    }

                    int idx = i + 1;
                    if (IsHandleCreated && !IsDisposed)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            _lblCollectionSummary.Text = string.Format("Syncing to Backloggd Library ({0}/{1}): {2}...",
                                idx, total, item.BackloggdTitle ?? item.LocalTitle);
                        }));
                    }

                    var stored = collection.Find(x => x.Id == item.Id) ?? item;
                    var syncRes = BackloggdClient.SaveLibraryEntry(stored, _settings);
                    if (syncRes.Success)
                    {
                        stored.Synced = true;
                        stored.SyncError = null;
                        stored.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        syncedCount++;
                    }
                    else
                    {
                        stored.Synced = false;
                        stored.SyncError = syncRes.Error;
                        failedCount++;
                    }

                    if ((i + 1) % 5 == 0 || i == entriesToSync.Count - 1)
                    {
                        DataStore.SaveCollection(collection);
                    }
                    Thread.Sleep(250);
                }

                // Reconcile with Backloggd remote library to pick up any newly created entry IDs
                if (syncedCount > 0)
                {
                    try
                    {
                        _tracker.SyncService.RefreshCollection(true, null);
                    }
                    catch { }
                }

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

        private void BuildMappingsTab(TabPage page)
        {
            var editorCard = new Panel
            {
                Dock = DockStyle.Top,
                Height = 225,
                BackColor = BgCard,
                Padding = new Padding(14)
            };

            var lblStep1 = new Label
            {
                Text = "1. Pick an installed Steam game (or auto-match your entire library in 1 click):",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = TextLight,
                AutoSize = true,
                Location = new Point(14, 10)
            };

            _cmbInstalledGames = new ComboBox
            {
                Location = new Point(14, 35),
                Width = 430,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = BgInput,
                ForeColor = TextLight
            };
            _cmbInstalledGames.SelectedIndexChanged += CmbInstalledGames_SelectedIndexChanged;

            var lblAppId = new Label { Text = "AppID:", Location = new Point(458, 38), AutoSize = true, ForeColor = TextMuted };
            _txtMapAppId = new TextBox
            {
                Location = new Point(508, 35),
                Width = 85,
                BackColor = BgInput,
                ForeColor = TextLight,
                BorderStyle = BorderStyle.FixedSingle
            };

            _btnAutoMatchAllInstalled = CreateStyledButton("✨ Auto-Match All Installed Games", AccentBlue, 245, 29);
            _btnAutoMatchAllInstalled.Location = new Point(610, 33);
            _btnAutoMatchAllInstalled.Click += BtnAutoMatchAllInstalled_Click;

            var lblStep2 = new Label
            {
                Text = "2. Live Backloggd Auto-Suggest (type to search — suggestions update automatically):",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = TextLight,
                AutoSize = true,
                Location = new Point(14, 72)
            };

            _txtMapSearchQuery = new TextBox
            {
                Location = new Point(14, 96),
                Width = 430,
                BackColor = BgInput,
                ForeColor = TextLight,
                BorderStyle = BorderStyle.FixedSingle
            };
            _txtMapSearchQuery.TextChanged += TxtMapSearchQuery_TextChanged;

            _lblSuggestionStatus = new Label
            {
                Text = "Select a Steam game above or start typing to see Backloggd suggestions...",
                ForeColor = TextMuted,
                AutoSize = true,
                Location = new Point(458, 98)
            };

            _lstSuggestions = new ListBox
            {
                Location = new Point(14, 126),
                Size = new Size(580, 84),
                BackColor = BgInput,
                ForeColor = TextLight,
                BorderStyle = BorderStyle.FixedSingle
            };
            _lstSuggestions.DoubleClick += BtnSaveMapping_Click;

            _btnSaveMapping = CreateStyledButton("✓ Use Selected Suggestion", AccentPink, 200, 36);
            _btnSaveMapping.Location = new Point(610, 126);
            _btnSaveMapping.Click += BtnSaveMapping_Click;

            _btnDeleteMapping = CreateStyledButton("Remove Selected Mapping", BgInput, 200, 32);
            _btnDeleteMapping.Location = new Point(610, 174);
            _btnDeleteMapping.Click += BtnDeleteMapping_Click;

            editorCard.Controls.AddRange(new Control[]
            {
                lblStep1, _cmbInstalledGames, lblAppId, _txtMapAppId, _btnAutoMatchAllInstalled,
                lblStep2, _txtMapSearchQuery, _lblSuggestionStatus, _lstSuggestions,
                _btnSaveMapping, _btnDeleteMapping
            });

            _lvMappings = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                BackColor = BgCard,
                ForeColor = TextLight,
                BorderStyle = BorderStyle.FixedSingle
            };
            _lvMappings.Columns.Add("Steam AppID", 100);
            _lvMappings.Columns.Add("Steam Title", 260);
            _lvMappings.Columns.Add("Backloggd Title", 260);
            _lvMappings.Columns.Add("Backloggd ID", 105);
            _lvMappings.Columns.Add("Match Type", 120);
            _lvMappings.SelectedIndexChanged += (s, e) =>
            {
                if (_lvMappings.SelectedItems.Count > 0)
                {
                    var item = _lvMappings.SelectedItems[0];
                    _txtMapAppId.Text = item.SubItems[0].Text;
                    TriggerLiveSuggest(item.SubItems[1].Text);
                }
            };

            var spacer = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = BgDark };
            page.Controls.Add(_lvMappings);
            page.Controls.Add(spacer);
            page.Controls.Add(editorCard);
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
            var btn = new Button
            {
                Text = text,
                Size = new Size(width, height),
                BackColor = bgColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void PopulateInstalledSteamGamesDropdown()
        {
            _cmbInstalledGames.Items.Clear();
            var ownedGames = SteamLibrary.GetAllOwnedSteamGames(_settings.IgnoredAppIds, false);
            var seenIds = new HashSet<int>();

            foreach (var g in ownedGames)
            {
                _cmbInstalledGames.Items.Add(g);
                seenIds.Add(g.AppId);
            }

            // Also include any games from past sessions that aren't in the list yet
            foreach (var s in DataStore.LoadSessions())
            {
                if (s.AppId > 0 && !seenIds.Contains(s.AppId))
                {
                    seenIds.Add(s.AppId);
                    _cmbInstalledGames.Items.Add(new InstalledSteamGame
                    {
                        AppId = s.AppId,
                        Title = s.GameTitle ?? ("Steam App " + s.AppId)
                    });
                }
            }

            if (_cmbInstalledGames.Items.Count > 0)
            {
                _cmbInstalledGames.SelectedIndex = 0;
            }
        }

        private void CmbInstalledGames_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selected = _cmbInstalledGames.SelectedItem as InstalledSteamGame;
            if (selected == null) return;

            _txtMapAppId.Text = selected.AppId.ToString();
            TriggerLiveSuggest(BackloggdClient.CleanGameTitle(selected.Title));
        }

        private void TriggerLiveSuggest(string query)
        {
            _suppressSearchTextChanged = true;
            _txtMapSearchQuery.Text = query ?? "";
            _suppressSearchTextChanged = false;

            _mappingDebounceTimer.Stop();
            _mappingDebounceTimer.Start();
        }

        private void TxtMapSearchQuery_TextChanged(object sender, EventArgs e)
        {
            if (_suppressSearchTextChanged) return;
            _mappingDebounceTimer.Stop();
            _mappingDebounceTimer.Start();
        }

        private void MappingDebounceTimer_Tick(object sender, EventArgs e)
        {
            _mappingDebounceTimer.Stop();
            string query = _txtMapSearchQuery.Text.Trim();
            if (query.Length < 2)
            {
                _lstSuggestions.Items.Clear();
                _lblSuggestionStatus.Text = "Type at least 2 characters to auto-suggest Backloggd games...";
                return;
            }

            int reqId = Interlocked.Increment(ref _searchRequestSeq);
            _lblSuggestionStatus.Text = "Searching Backloggd for '" + query + "'...";
            _lblSuggestionStatus.ForeColor = TextMuted;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var results = BackloggdClient.SearchGame(query, _settings);
                    BeginInvoke(new Action(() =>
                    {
                        if (reqId != _searchRequestSeq) return;
                        _lstSuggestions.Items.Clear();
                        foreach (var r in results)
                        {
                            _lstSuggestions.Items.Add(r);
                        }

                        if (_lstSuggestions.Items.Count > 0)
                        {
                            var best = BackloggdClient.SelectBestMatch(query, results);
                            if (best != null && _lstSuggestions.Items.Contains(best))
                                _lstSuggestions.SelectedItem = best;
                            else
                                _lstSuggestions.SelectedIndex = 0;

                            _lblSuggestionStatus.Text = string.Format("Found {0} suggestion(s) — best match pre-selected!", results.Count);
                            _lblSuggestionStatus.ForeColor = SuccessGreen;
                        }
                        else
                        {
                            _lblSuggestionStatus.Text = "No Backloggd matches found for '" + query + "'. Try a shorter title.";
                            _lblSuggestionStatus.ForeColor = WarnOrange;
                        }
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (reqId != _searchRequestSeq) return;
                        _lblSuggestionStatus.Text = "Auto-suggest error: " + ex.Message;
                        _lblSuggestionStatus.ForeColor = WarnOrange;
                    }));
                }
            });
        }

        private void BtnAutoMatchAllInstalled_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_settings.SessionCookie))
            {
                MessageBox.Show("Please connect your _backloggd_session cookie in the Settings tab first!",
                    "Cookie Needed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _tabControl.SelectedIndex = 4;
                return;
            }

            var installed = SteamTracker.GetInstalledSteamGames(_settings.IgnoredAppIds);
            if (installed.Count == 0)
            {
                MessageBox.Show("No installed Steam games were detected in your local Steam library folders.",
                    "Steam Library", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _btnAutoMatchAllInstalled.Enabled = false;
            _btnAutoMatchAllInstalled.Text = "Matching installed games...";

            ThreadPool.QueueUserWorkItem(_ =>
            {
                int matchedCount = 0;
                int skippedCount = 0;
                var mappings = DataStore.LoadMappings();

                foreach (var g in installed)
                {
                    if (mappings.Exists(m => m.AppId == g.AppId && !string.IsNullOrEmpty(m.BackloggdGameId)))
                    {
                        skippedCount++;
                        continue;
                    }

                    try
                    {
                        string clean = BackloggdClient.CleanGameTitle(g.Title);
                        var results = BackloggdClient.SearchGame(clean, _settings);
                        if (results.Count == 0 && clean != g.Title)
                        {
                            results = BackloggdClient.SearchGame(g.Title, _settings);
                        }
                        var best = BackloggdClient.SelectBestMatch(clean, results) ?? (results.Count > 0 ? results[0] : null);
                        if (best != null)
                        {
                            mappings.RemoveAll(m => m.AppId == g.AppId);
                            mappings.Add(new GameMapping
                            {
                                AppId = g.AppId,
                                BackloggdGameId = best.GameId,
                                SteamTitle = g.Title,
                                BackloggdTitle = best.Title,
                                ReleaseYear = best.ReleaseYear,
                                ManualOverride = false,
                                LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                            });
                            matchedCount++;
                        }
                        Thread.Sleep(250);
                    }
                    catch { }
                }

                DataStore.SaveMappings(mappings);

                BeginInvoke(new Action(() =>
                {
                    _btnAutoMatchAllInstalled.Enabled = true;
                    _btnAutoMatchAllInstalled.Text = "✨ Auto-Match All Installed Games";
                    RefreshMappingsList();
                    MessageBox.Show(
                        string.Format("Scan complete!\r\n• Newly auto-matched: {0} game(s)\r\n• Already mapped: {1} game(s)", matchedCount, skippedCount),
                        "Auto-Match Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            });
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
                    ? "Nintendo Switch"
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
            _lvSessions.EndUpdate();

            _lblTodaySummary.Text = string.Format("Today ({0}): {1} played across {2} game(s)",
                todayStr, FormatDuration(todaySeconds), todayGames.Count);
        }

        private void RefreshMappingsList()
        {
            var mappings = DataStore.LoadMappings();
            _lvMappings.BeginUpdate();
            _lvMappings.Items.Clear();
            foreach (var m in mappings)
            {
                var item = new ListViewItem(m.AppId.ToString());
                item.Tag = m;
                item.SubItems.Add(m.SteamTitle ?? "");
                item.SubItems.Add(m.BackloggdTitle ?? "");
                item.SubItems.Add(m.BackloggdGameId ?? "");
                item.SubItems.Add(m.ManualOverride ? "Manual" : "Auto-Matched");
                _lvMappings.Items.Add(item);
            }
            _lvMappings.EndUpdate();

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

        private void BtnSaveMapping_Click(object sender, EventArgs e)
        {
            int appId;
            if (!int.TryParse(_txtMapAppId.Text.Trim(), out appId) || appId <= 0)
            {
                MessageBox.Show("Please select an installed Steam game from the dropdown (or enter its AppID).", "Select Steam Game", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var selectedResult = _lstSuggestions.SelectedItem as GameSearchResult;
            if (selectedResult == null)
            {
                MessageBox.Show("Please select a Backloggd game from the auto-suggestions list.", "Select Suggestion", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var mappings = DataStore.LoadMappings();
            mappings.RemoveAll(m => m.AppId == appId);
            mappings.Add(new GameMapping
            {
                AppId = appId,
                BackloggdGameId = selectedResult.GameId,
                SteamTitle = _tracker.ResolveSteamGameTitle(appId),
                BackloggdTitle = selectedResult.Title,
                ReleaseYear = selectedResult.ReleaseYear,
                ManualOverride = true,
                LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
            DataStore.SaveMappings(mappings);
            RefreshMappingsList();

            // Also update any unsynced sessions for this AppID and sync them automatically!
            var sessions = DataStore.LoadSessions();
            bool updatedAnySession = false;
            foreach (var s in sessions)
            {
                if (s.AppId == appId && !s.Synced)
                {
                    s.BackloggdGameId = selectedResult.GameId;
                    s.GameTitle = selectedResult.Title;
                    updatedAnySession = true;
                    string sid = s.Id;
                    ThreadPool.QueueUserWorkItem(_ => _tracker.SyncSessionEntryAsync(sid, false));
                }
            }
            if (updatedAnySession)
            {
                DataStore.SaveSessions(sessions);
                RefreshSessionsList();
            }

            _lblSuggestionStatus.Text = string.Format("✓ Mapped AppID {0} -> {1} (ID {2})", appId, selectedResult.Title, selectedResult.GameId);
            _lblSuggestionStatus.ForeColor = SuccessGreen;
        }

        private void BtnDeleteMapping_Click(object sender, EventArgs e)
        {
            if (_lvMappings.SelectedItems.Count == 0) return;
            var map = _lvMappings.SelectedItems[0].Tag as GameMapping;
            if (map == null) return;

            var mappings = DataStore.LoadMappings();
            mappings.RemoveAll(m => m.AppId == map.AppId);
            DataStore.SaveMappings(mappings);
            RefreshMappingsList();
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
