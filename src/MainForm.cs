using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BackloggdAutoSync
{
    public class MainForm : Form
    {
        private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "BackloggdAutoSync";

        private AppSettings _settings;
        private readonly SteamTracker _tracker;
        private readonly System.Windows.Forms.Timer _pollTimer;
        private readonly System.Windows.Forms.Timer _mappingDebounceTimer;
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
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

        // Tab 2: Game Mappings (Auto-Suggest UI)
        private ComboBox _cmbInstalledGames;
        private Button _btnAutoMatchAllInstalled;
        private TextBox _txtMapAppId;
        private TextBox _txtMapSearchQuery;
        private Label _lblSuggestionStatus;
        private ListBox _lstSuggestions;
        private Button _btnSaveMapping;
        private Button _btnDeleteMapping;
        private ListView _lvMappings;

        // Tab 3: Settings
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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
            }
        }

        private Icon CreateAppIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush bg = new SolidBrush(AccentPink))
                {
                    g.FillEllipse(bg, 1, 1, 30, 30);
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
            _trayMenu.Items.Add("Open Backloggd AutoSync", null, (s, e) => ShowMainWindow());
            _trayMenu.Items.Add("Sync Pending Sessions Now", null, (s, e) => SyncAllPendingSessions());
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Exit Completely", null, (s, e) =>
            {
                _allowClose = true;
                _trayIcon.Visible = false;
                Application.Exit();
            });

            _trayIcon = new NotifyIcon
            {
                Icon = this.Icon,
                Text = "Backloggd AutoSync — Idle",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            UpdateLiveStatusUi();
            Activate();
        }

        private void InitializeComponent()
        {
            this.Icon = CreateAppIcon();
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
            var tabMappings = new TabPage("Game Mappings (Auto-Suggest)") { BackColor = BgDark, ForeColor = TextLight, Padding = new Padding(14) };
            var tabSettings = new TabPage("Backloggd Account & Settings") { BackColor = BgDark, ForeColor = TextLight, Padding = new Padding(14) };

            BuildSessionsTab(tabSessions);
            BuildMappingsTab(tabMappings);
            BuildSettingsTab(tabSettings);

            _tabControl.TabPages.Add(tabSessions);
            _tabControl.TabPages.Add(tabMappings);
            _tabControl.TabPages.Add(tabSettings);
            _tabControl.SelectedIndexChanged += (s, e) =>
            {
                if (_tabControl.SelectedIndex == 1 && _cmbInstalledGames.Items.Count == 0)
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
            _lvSessions.Columns.Add("Steam AppID", 90);
            _lvSessions.Columns.Add("Session Duration", 115);
            _lvSessions.Columns.Add("Backloggd Status", 150);
            _lvSessions.Columns.Add("Details (Double-click row to change game)", 220);
            _lvSessions.DoubleClick += BtnPickGameForSession_Click;

            var spacer = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = BgDark };

            page.Controls.Add(_lvSessions);
            page.Controls.Add(spacer);
            page.Controls.Add(topBar);
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
            var installed = SteamTracker.GetInstalledSteamGames(_settings.IgnoredAppIds);
            var seenIds = new HashSet<int>();

            foreach (var g in installed)
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
                _tabControl.SelectedIndex = 2;
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

            using (Form dlg = new Form())
            {
                dlg.Text = "Pick Backloggd Game — Auto-Suggest";
                dlg.Size = new Size(560, 400);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.BackColor = BgCard;
                dlg.ForeColor = TextLight;
                dlg.Font = this.Font;

                var lblHeader = new Label
                {
                    Text = string.Format("Select Backloggd match for: {0} {1}",
                        rawTitle, entry.AppId > 0 ? ("(AppID " + entry.AppId + ")") : ""),
                    Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                    ForeColor = TextLight,
                    AutoSize = true,
                    Location = new Point(18, 16)
                };

                var lblSearch = new Label
                {
                    Text = "Search Backloggd (auto-suggests as you type):",
                    ForeColor = TextMuted,
                    AutoSize = true,
                    Location = new Point(18, 48)
                };

                var txtQuery = new TextBox
                {
                    Location = new Point(18, 72),
                    Width = 505,
                    Text = BackloggdClient.CleanGameTitle(rawTitle),
                    BackColor = BgInput,
                    ForeColor = TextLight,
                    BorderStyle = BorderStyle.FixedSingle
                };

                var lblStatus = new Label
                {
                    Text = "Loading suggestions...",
                    ForeColor = TextMuted,
                    AutoSize = true,
                    Location = new Point(18, 102)
                };

                var lstResults = new ListBox
                {
                    Location = new Point(18, 126),
                    Size = new Size(505, 165),
                    BackColor = BgInput,
                    ForeColor = TextLight,
                    BorderStyle = BorderStyle.FixedSingle
                };

                var btnConfirm = CreateStyledButton("✓ Confirm Match & Sync Session", AccentPink, 250, 38);
                btnConfirm.Location = new Point(18, 304);

                var btnCancel = CreateStyledButton("Later", BgInput, 100, 38);
                btnCancel.Location = new Point(423, 304);
                btnCancel.Click += (s, e) => dlg.Close();

                var debounce = new System.Windows.Forms.Timer { Interval = 350 };
                int modalReqSeq = 0;

                Action runSearch = () =>
                {
                    string q = txtQuery.Text.Trim();
                    if (q.Length < 2) return;
                    int mySeq = Interlocked.Increment(ref modalReqSeq);
                    lblStatus.Text = "Searching Backloggd for '" + q + "'...";
                    lblStatus.ForeColor = TextMuted;

                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            var res = BackloggdClient.SearchGame(q, _settings);
                            if (dlg.IsDisposed || !dlg.IsHandleCreated) return;
                            dlg.BeginInvoke(new Action(() =>
                            {
                                if (mySeq != modalReqSeq || dlg.IsDisposed) return;
                                lstResults.Items.Clear();
                                foreach (var r in res) lstResults.Items.Add(r);
                                if (lstResults.Items.Count > 0)
                                {
                                    var best = BackloggdClient.SelectBestMatch(q, res);
                                    lstResults.SelectedItem = (best != null && lstResults.Items.Contains(best)) ? best : lstResults.Items[0];
                                    lblStatus.Text = string.Format("Found {0} match(es) — double-click or click Confirm below:", res.Count);
                                    lblStatus.ForeColor = SuccessGreen;
                                }
                                else
                                {
                                    lblStatus.Text = "No matches found. Try editing the game title above.";
                                    lblStatus.ForeColor = WarnOrange;
                                }
                            }));
                        }
                        catch (Exception ex)
                        {
                            if (dlg.IsDisposed || !dlg.IsHandleCreated) return;
                            dlg.BeginInvoke(new Action(() =>
                            {
                                if (dlg.IsDisposed) return;
                                lblStatus.Text = "Search error: " + ex.Message;
                                lblStatus.ForeColor = WarnOrange;
                            }));
                        }
                    });
                };

                debounce.Tick += (s, e) =>
                {
                    debounce.Stop();
                    runSearch();
                };

                txtQuery.TextChanged += (s, e) =>
                {
                    debounce.Stop();
                    debounce.Start();
                };

                EventHandler confirmAction = (s, e) =>
                {
                    var picked = lstResults.SelectedItem as GameSearchResult;
                    if (picked == null)
                    {
                        MessageBox.Show("Please select a game from the suggestions list.", "Select Game",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    if (entry.AppId != 0)
                    {
                        var mappings = DataStore.LoadMappings();
                        mappings.RemoveAll(m => m.AppId == entry.AppId);
                        mappings.Add(new GameMapping
                        {
                            AppId = entry.AppId,
                            BackloggdGameId = picked.GameId,
                            SteamTitle = rawTitle,
                            BackloggdTitle = picked.Title,
                            ReleaseYear = picked.ReleaseYear,
                            ManualOverride = true,
                            LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        });
                        DataStore.SaveMappings(mappings);
                    }

                    var sessions = DataStore.LoadSessions();
                    var target = sessions.Find(x => x.Id == entry.Id);
                    if (target != null)
                    {
                        target.BackloggdGameId = picked.GameId;
                        target.GameTitle = picked.Title;
                        target.Synced = false;
                        target.SyncError = "Syncing...";
                        DataStore.SaveSessions(sessions);
                    }

                    RefreshMappingsList();
                    RefreshSessionsList();

                    ThreadPool.QueueUserWorkItem(_ => _tracker.SyncSessionEntryAsync(entry.Id, false));
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                };

                btnConfirm.Click += confirmAction;
                lstResults.DoubleClick += confirmAction;

                dlg.Controls.AddRange(new Control[]
                {
                    lblHeader, lblSearch, txtQuery, lblStatus, lstResults, btnConfirm, btnCancel
                });

                dlg.Shown += (s, e) =>
                {
                    if (preloadedResults != null && preloadedResults.Count > 0)
                    {
                        foreach (var r in preloadedResults) lstResults.Items.Add(r);
                        lstResults.SelectedIndex = 0;
                        lblStatus.Text = string.Format("Found {0} suggestion(s) — pick the matching Backloggd entry:", preloadedResults.Count);
                        lblStatus.ForeColor = SuccessGreen;
                    }
                    else
                    {
                        runSearch();
                    }
                };

                dlg.FormClosed += (s, e) => debounce.Dispose();
                dlg.ShowDialog(this);
            }
        }

        private void PollTimer_Tick(object sender, EventArgs e)
        {
            _tracker.Poll(_settings);
            UpdateLiveStatusUi();
        }

        private void UpdateLiveStatusUi()
        {
            if (_tracker.CurrentAppId > 0)
            {
                string dur = FormatDuration(_tracker.CurrentElapsedSeconds);
                string trayText = string.Format("Playing: {0} ({1})", _tracker.CurrentTitle, dur);
                _trayIcon.Text = trayText.Length > 63 ? trayText.Substring(0, 63) : trayText;

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
                _trayIcon.Text = "Backloggd AutoSync — Idle";
                if (this.Visible && this.WindowState != FormWindowState.Minimized)
                {
                    _lblLiveStatus.Text = "Idle — Waiting for a Steam game to launch...";
                    _lblLiveStatus.ForeColor = TextLight;
                    _lblLiveTimer.Text = "Lightweight mode: checks Steam once per minute (close window to minimize to tray)";
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
                item.SubItems.Add(s.AppId > 0 ? s.AppId.ToString() : "Manual");
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
                for (int i = 0; i < pending.Count; i++)
                {
                    _tracker.SyncSessionEntryAsync(pending[i].Id, i == 0);
                }
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
            using (Form dlg = new Form())
            {
                dlg.Text = "Log Session Manually — With Live Auto-Suggest";
                dlg.Size = new Size(520, 420);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.BackColor = BgCard;
                dlg.ForeColor = TextLight;
                dlg.Font = this.Font;

                var l1 = new Label { Text = "Search Game on Backloggd (auto-suggests as you type):", Location = new Point(20, 18), AutoSize = true };
                var txtTitle = new TextBox { Location = new Point(20, 42), Width = 460, BackColor = BgInput, ForeColor = TextLight, BorderStyle = BorderStyle.FixedSingle };

                var lblSuggest = new Label { Text = "Start typing a game name above...", Location = new Point(20, 70), AutoSize = true, ForeColor = TextMuted };
                var lstMatches = new ListBox { Location = new Point(20, 94), Size = new Size(460, 130), BackColor = BgInput, ForeColor = TextLight, BorderStyle = BorderStyle.FixedSingle };

                var l3 = new Label { Text = "Date (YYYY-MM-DD):", Location = new Point(20, 240), AutoSize = true };
                var txtDate = new TextBox { Location = new Point(175, 237), Width = 110, Text = DateTime.Now.ToString("yyyy-MM-dd"), BackColor = BgInput, ForeColor = TextLight };

                var l4 = new Label { Text = "Minutes Played:", Location = new Point(20, 278), AutoSize = true };
                var numMins = new NumericUpDown { Location = new Point(175, 275), Width = 110, Minimum = 1, Maximum = 1440, Value = 60, BackColor = BgInput, ForeColor = TextLight };

                var btnAdd = CreateStyledButton("✓ Log & Sync to Backloggd", AccentPink, 230, 38);
                btnAdd.Location = new Point(140, 325);

                var debounce = new System.Windows.Forms.Timer { Interval = 350 };
                int reqSeq = 0;

                debounce.Tick += (s2, e2) =>
                {
                    debounce.Stop();
                    string q = txtTitle.Text.Trim();
                    if (q.Length < 2) return;
                    int mySeq = Interlocked.Increment(ref reqSeq);
                    lblSuggest.Text = "Searching Backloggd for '" + q + "'...";
                    lblSuggest.ForeColor = TextMuted;

                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            var res = BackloggdClient.SearchGame(q, _settings);
                            if (dlg.IsDisposed || !dlg.IsHandleCreated) return;
                            dlg.BeginInvoke(new Action(() =>
                            {
                                if (mySeq != reqSeq || dlg.IsDisposed) return;
                                lstMatches.Items.Clear();
                                foreach (var r in res) lstMatches.Items.Add(r);
                                if (lstMatches.Items.Count > 0)
                                {
                                    var best = BackloggdClient.SelectBestMatch(q, res);
                                    lstMatches.SelectedItem = (best != null && lstMatches.Items.Contains(best)) ? best : lstMatches.Items[0];
                                    lblSuggest.Text = string.Format("Found {0} game(s) — select one below:", res.Count);
                                    lblSuggest.ForeColor = SuccessGreen;
                                }
                                else
                                {
                                    lblSuggest.Text = "No Backloggd matches found.";
                                    lblSuggest.ForeColor = WarnOrange;
                                }
                            }));
                        }
                        catch (Exception ex)
                        {
                            if (dlg.IsDisposed || !dlg.IsHandleCreated) return;
                            dlg.BeginInvoke(new Action(() =>
                            {
                                if (dlg.IsDisposed) return;
                                lblSuggest.Text = "Search error: " + ex.Message;
                                lblSuggest.ForeColor = WarnOrange;
                            }));
                        }
                    });
                };

                txtTitle.TextChanged += (s2, e2) =>
                {
                    debounce.Stop();
                    debounce.Start();
                };

                btnAdd.Click += (s2, e2) =>
                {
                    var picked = lstMatches.SelectedItem as GameSearchResult;
                    string title = picked != null ? picked.Title : txtTitle.Text.Trim();
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        MessageBox.Show("Please type and select a game first.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    int mins = (int)numMins.Value;
                    string dateStr = txtDate.Text.Trim();

                    var entry = new SessionEntry
                    {
                        Id = string.Format("manual_{0}", DateTime.Now.Ticks),
                        AppId = 0,
                        GameTitle = title,
                        BackloggdGameId = picked != null ? picked.GameId : null,
                        DateStr = dateStr,
                        StartTimeIso = DateTime.Now.AddMinutes(-mins).ToString("o"),
                        EndTimeIso = DateTime.Now.ToString("o"),
                        DurationSeconds = mins * 60,
                        Synced = false
                    };

                    var sessions = DataStore.LoadSessions();
                    sessions.Insert(0, entry);
                    DataStore.SaveSessions(sessions);
                    RefreshSessionsList();

                    ThreadPool.QueueUserWorkItem(_ => _tracker.SyncSessionEntryAsync(entry.Id, true));
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                };

                dlg.FormClosed += (s2, e2) => debounce.Dispose();
                dlg.Controls.AddRange(new Control[] { l1, txtTitle, lblSuggest, lstMatches, l3, txtDate, l4, numMins, btnAdd });
                dlg.ShowDialog(this);
            }
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
                this.Hide();
                this.ShowInTaskbar = false;
                if (_settings.ShowNotifications)
                {
                    _trayIcon.ShowBalloonTip(2000, "Backloggd AutoSync Still Running",
                        "Minimized to system tray. Double-click the tray icon anytime to open.", ToolTipIcon.Info);
                }
                return;
            }
            _trayIcon.Visible = false;
            base.OnFormClosing(e);
        }

        public static string FormatDuration(int totalSeconds)
        {
            int hours = totalSeconds / 3600;
            int mins = (totalSeconds % 3600) / 60;
            int secs = totalSeconds % 60;
            if (hours > 0)
                return string.Format("{0}h {1}m {2}s", hours, mins, secs);
            if (mins > 0)
                return string.Format("{0}m {1}s", mins, secs);
            return string.Format("{0}s", secs);
        }
    }
}
