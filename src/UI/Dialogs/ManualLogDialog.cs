using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace BackloggdAutoSync
{
    public static class ManualLogDialog
    {
        public static void Show(
            IWin32Window owner,
            Font font,
            AppSettings settings,
            Action<string> onSyncRequested,
            Action onDataChanged)
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "Log Session Manually — With Live Auto-Suggest";
                dlg.Size = new Size(520, 420);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.BackColor = UITheme.BgCard;
                dlg.ForeColor = UITheme.TextLight;
                dlg.Font = font;

                var l1 = new Label { Text = "Search Game on Backloggd (auto-suggests as you type):", Location = new Point(20, 18), AutoSize = true };
                var txtTitle = new TextBox { Location = new Point(20, 42), Width = 460, BackColor = UITheme.BgInput, ForeColor = UITheme.TextLight, BorderStyle = BorderStyle.FixedSingle };

                var lblSuggest = new Label { Text = "Start typing a game name above...", Location = new Point(20, 70), AutoSize = true, ForeColor = UITheme.TextMuted };
                var lstMatches = new ListBox { Location = new Point(20, 94), Size = new Size(460, 130), BackColor = UITheme.BgInput, ForeColor = UITheme.TextLight, BorderStyle = BorderStyle.FixedSingle };

                var l3 = new Label { Text = "Date (YYYY-MM-DD):", Location = new Point(20, 240), AutoSize = true };
                var txtDate = new TextBox { Location = new Point(175, 237), Width = 110, Text = DateTime.Now.ToString("yyyy-MM-dd"), BackColor = UITheme.BgInput, ForeColor = UITheme.TextLight };

                var l4 = new Label { Text = "Minutes Played:", Location = new Point(20, 278), AutoSize = true };
                var numMins = new NumericUpDown { Location = new Point(175, 275), Width = 110, Minimum = 1, Maximum = 1440, Value = 60, BackColor = UITheme.BgInput, ForeColor = UITheme.TextLight };

                var btnAdd = UITheme.CreateStyledButton("✓ Log & Sync to Backloggd", UITheme.AccentPink, 230, 38);
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
                    lblSuggest.ForeColor = UITheme.TextMuted;

                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            var res = BackloggdClient.SearchGame(q, settings);
                            if (dlg.IsDisposed || !dlg.IsHandleCreated) return;
                            dlg.BeginInvoke(new Action(() =>
                            {
                                if (mySeq != reqSeq || dlg.IsDisposed) return;
                                lstMatches.Items.Clear();
                                foreach (var r in res) lstMatches.Items.Add(r);
                                if (lstMatches.Items.Count > 0)
                                {
                                    var best = GameTitleMatcher.SelectBestMatch(q, res);
                                    lstMatches.SelectedItem = (best != null && lstMatches.Items.Contains(best)) ? best : lstMatches.Items[0];
                                    lblSuggest.Text = string.Format("Found {0} game(s) — select one below:", res.Count);
                                    lblSuggest.ForeColor = UITheme.SuccessGreen;
                                }
                                else
                                {
                                    lblSuggest.Text = "No Backloggd matches found.";
                                    lblSuggest.ForeColor = UITheme.WarnOrange;
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
                                lblSuggest.ForeColor = UITheme.WarnOrange;
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

                    if (onDataChanged != null) onDataChanged();
                    if (onSyncRequested != null) onSyncRequested(entry.Id);

                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                };

                dlg.FormClosed += (s2, e2) => debounce.Dispose();
                dlg.Controls.AddRange(new Control[] { l1, txtTitle, lblSuggest, lstMatches, l3, txtDate, l4, numMins, btnAdd });
                dlg.ShowDialog(owner);
            }
        }
    }
}
