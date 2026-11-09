using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace BackloggdAutoSync
{
    public static class UnmappedGameDialog
    {
        public static void Show(
            IWin32Window owner,
            Font font,
            AppSettings settings,
            SessionEntry entry,
            string rawTitle,
            List<GameSearchResult> preloadedResults,
            Action<List<string>> onBatchSyncRequested,
            Action onDataChanged)
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "Pick Backloggd Game — Auto-Suggest";
                dlg.Size = new Size(560, 400);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.BackColor = UITheme.BgCard;
                dlg.ForeColor = UITheme.TextLight;
                dlg.Font = font;

                var lblHeader = new Label
                {
                    Text = string.Format("Select Backloggd match for: {0} {1}",
                        rawTitle, entry.AppId > 0 ? ("(AppID " + entry.AppId + ")") : ""),
                    Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                    ForeColor = UITheme.TextLight,
                    AutoSize = true,
                    Location = new Point(18, 16)
                };

                var lblSearch = new Label
                {
                    Text = "Search Backloggd (auto-suggests as you type):",
                    ForeColor = UITheme.TextMuted,
                    AutoSize = true,
                    Location = new Point(18, 48)
                };

                var txtQuery = new TextBox
                {
                    Location = new Point(18, 72),
                    Width = 505,
                    Text = GameTitleMatcher.CleanGameTitle(rawTitle),
                    BackColor = UITheme.BgInput,
                    ForeColor = UITheme.TextLight,
                    BorderStyle = BorderStyle.FixedSingle
                };

                var lblStatus = new Label
                {
                    Text = "Loading suggestions...",
                    ForeColor = UITheme.TextMuted,
                    AutoSize = true,
                    Location = new Point(18, 102)
                };

                var lstResults = new ListBox
                {
                    Location = new Point(18, 126),
                    Size = new Size(505, 165),
                    BackColor = UITheme.BgInput,
                    ForeColor = UITheme.TextLight,
                    BorderStyle = BorderStyle.FixedSingle
                };

                var btnConfirm = UITheme.CreateStyledButton("✓ Confirm Match & Sync Session", UITheme.AccentPink, 250, 38);
                btnConfirm.Location = new Point(18, 304);

                var btnCancel = UITheme.CreateStyledButton("Later", UITheme.BgInput, 100, 38);
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
                    lblStatus.ForeColor = UITheme.TextMuted;

                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            var res = BackloggdClient.SearchGame(q, settings);
                            if (dlg.IsDisposed || !dlg.IsHandleCreated) return;
                            dlg.BeginInvoke(new Action(() =>
                            {
                                if (mySeq != modalReqSeq || dlg.IsDisposed) return;
                                lstResults.Items.Clear();
                                foreach (var r in res) lstResults.Items.Add(r);
                                if (lstResults.Items.Count > 0)
                                {
                                    var best = GameTitleMatcher.SelectBestMatch(q, res);
                                    lstResults.SelectedItem = (best != null && lstResults.Items.Contains(best)) ? best : lstResults.Items[0];
                                    lblStatus.Text = string.Format("Found {0} match(es) — double-click or click Confirm below:", res.Count);
                                    lblStatus.ForeColor = UITheme.SuccessGreen;
                                }
                                else
                                {
                                    lblStatus.Text = "No matches found. Try editing the game title above.";
                                    lblStatus.ForeColor = UITheme.WarnOrange;
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
                                lblStatus.ForeColor = UITheme.WarnOrange;
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
                    var idsToBatchSync = new List<string>();
                    foreach (var x in sessions)
                    {
                        if (x.Id == entry.Id || (entry.AppId != 0 && x.AppId == entry.AppId && !x.Synced))
                        {
                            x.BackloggdGameId = picked.GameId;
                            x.GameTitle = picked.Title;
                            x.Synced = false;
                            x.SyncError = "Syncing...";
                            idsToBatchSync.Add(x.Id);
                        }
                    }
                    DataStore.SaveSessions(sessions);

                    if (onDataChanged != null) onDataChanged();
                    if (onBatchSyncRequested != null) onBatchSyncRequested(idsToBatchSync);

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
                        lblStatus.ForeColor = UITheme.SuccessGreen;
                    }
                    else
                    {
                        runSearch();
                    }
                };

                dlg.FormClosed += (s, e) => debounce.Dispose();
                dlg.ShowDialog(owner);
            }
        }
    }
}
