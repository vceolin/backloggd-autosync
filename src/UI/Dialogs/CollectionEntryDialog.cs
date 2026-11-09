using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace BackloggdAutoSync
{
    public static class CollectionEntryDialog
    {
        private class ComboItem
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public override string ToString() { return Name; }
        }

        public static void Show(
            IWin32Window owner,
            Font font,
            AppSettings settings,
            CollectionEntry entry,
            Action<CollectionEntry, bool> onSaved)
        {
            using (Form dlg = new Form())
            {
                dlg.Text = string.Format("Collection Library Entry — {0} ({1})", entry.LocalTitle, entry.Source);
                dlg.Size = new Size(620, 560);
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.MaximizeBox = false;
                dlg.MinimizeBox = false;
                dlg.BackColor = UITheme.BgCard;
                dlg.ForeColor = UITheme.TextLight;
                dlg.Font = font;

                string idTag = entry.Source == "Steam"
                    ? string.Format("Steam AppID {0}{1}", entry.AppId, entry.IsInstalled ? " [Installed]" : " [Uninstalled]")
                    : string.Format("Nintendo Switch ({0})", !string.IsNullOrEmpty(entry.SwitchApplicationId) ? entry.SwitchApplicationId : entry.AppId.ToString());

                var lblHeader = new Label
                {
                    Text = string.Format("{0}  •  {1}", entry.LocalTitle, idTag),
                    Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                    ForeColor = UITheme.TextLight,
                    AutoSize = true,
                    Location = new Point(18, 14)
                };

                var lblSearch = new Label
                {
                    Text = "1. Backloggd Game Association (search & select even before launching the game):",
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    ForeColor = UITheme.TextLight,
                    AutoSize = true,
                    Location = new Point(18, 44)
                };

                var txtQuery = new TextBox
                {
                    Location = new Point(18, 68),
                    Width = 568,
                    Text = !string.IsNullOrEmpty(entry.BackloggdTitle) ? entry.BackloggdTitle : GameTitleMatcher.CleanGameTitle(entry.LocalTitle),
                    BackColor = UITheme.BgInput,
                    ForeColor = UITheme.TextLight,
                    BorderStyle = BorderStyle.FixedSingle
                };

                var lblStatus = new Label
                {
                    Text = !string.IsNullOrEmpty(entry.BackloggdGameId)
                        ? string.Format("Currently mapped to: {0} (ID: {1})", entry.BackloggdTitle, entry.BackloggdGameId)
                        : "Searching Backloggd...",
                    ForeColor = !string.IsNullOrEmpty(entry.BackloggdGameId) ? UITheme.SuccessGreen : UITheme.TextMuted,
                    AutoSize = true,
                    Location = new Point(18, 96)
                };

                var lstResults = new ListBox
                {
                    Location = new Point(18, 120),
                    Size = new Size(568, 130),
                    BackColor = UITheme.BgInput,
                    ForeColor = UITheme.TextLight,
                    BorderStyle = BorderStyle.FixedSingle
                };

                var lblPropsHeader = new Label
                {
                    Text = "2. Library Ownership, Platform, Storefront & Edition:",
                    Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                    ForeColor = UITheme.TextLight,
                    AutoSize = true,
                    Location = new Point(18, 264)
                };

                var lblOwnership = new Label { Text = "Ownership:", Location = new Point(18, 296), AutoSize = true, ForeColor = UITheme.TextMuted };
                var cmbOwnership = new ComboBox
                {
                    Location = new Point(110, 292),
                    Width = 175,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    BackColor = UITheme.BgInput,
                    ForeColor = UITheme.TextLight
                };
                cmbOwnership.Items.Add(new ComboItem { Id = "1", Name = "Digital (Cloud)" });
                cmbOwnership.Items.Add(new ComboItem { Id = "0", Name = "Physical (Disc / Cartridge)" });
                cmbOwnership.Items.Add(new ComboItem { Id = "3", Name = "Lost / Sold" });
                cmbOwnership.SelectedIndex = entry.OwnershipId == 0 ? 1 : (entry.OwnershipId == 3 ? 2 : 0);

                var lblStorefront = new Label { Text = "Storefront:", Location = new Point(310, 296), AutoSize = true, ForeColor = UITheme.TextMuted };
                var cmbStorefront = new ComboBox
                {
                    Location = new Point(395, 292),
                    Width = 191,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    BackColor = UITheme.BgInput,
                    ForeColor = UITheme.TextLight
                };
                cmbStorefront.Items.Add(new ComboItem { Id = "", Name = "(None)" });
                cmbStorefront.Items.Add(new ComboItem { Id = "1", Name = "Steam" });
                cmbStorefront.Items.Add(new ComboItem { Id = "8", Name = "Nintendo eShop" });
                cmbStorefront.Items.Add(new ComboItem { Id = "5", Name = "GOG" });
                cmbStorefront.Items.Add(new ComboItem { Id = "4", Name = "Epic Games" });
                cmbStorefront.Items.Add(new ComboItem { Id = "11", Name = "Microsoft Store" });
                cmbStorefront.Items.Add(new ComboItem { Id = "2", Name = "Battle.net" });
                cmbStorefront.Items.Add(new ComboItem { Id = "3", Name = "EA App" });
                cmbStorefront.Items.Add(new ComboItem { Id = "6", Name = "Ubisoft Connect" });
                cmbStorefront.Items.Add(new ComboItem { Id = "24", Name = "Itch.io" });

                string initialStoreId = entry.StorefrontId.HasValue ? entry.StorefrontId.Value.ToString() : (entry.Source == "Steam" ? "1" : "8");
                cmbStorefront.SelectedIndex = 0;
                for (int i = 0; i < cmbStorefront.Items.Count; i++)
                {
                    if (((ComboItem)cmbStorefront.Items[i]).Id == initialStoreId)
                    {
                        cmbStorefront.SelectedIndex = i;
                        break;
                    }
                }

                cmbOwnership.SelectedIndexChanged += (s, e) =>
                {
                    var own = cmbOwnership.SelectedItem as ComboItem;
                    bool isDigital = (own != null && own.Id == "1");
                    cmbStorefront.Enabled = isDigital;
                    if (!isDigital)
                    {
                        cmbStorefront.SelectedIndex = 0;
                    }
                    else if (cmbStorefront.SelectedIndex == 0)
                    {
                        string defStore = entry.Source == "Steam" ? "1" : "8";
                        for (int i = 0; i < cmbStorefront.Items.Count; i++)
                        {
                            if (((ComboItem)cmbStorefront.Items[i]).Id == defStore)
                            {
                                cmbStorefront.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                };
                cmbStorefront.Enabled = (entry.OwnershipId == 1);

                var lblPlatform = new Label { Text = "Platform:", Location = new Point(18, 338), AutoSize = true, ForeColor = UITheme.TextMuted };
                var cmbPlatform = new ComboBox
                {
                    Location = new Point(110, 334),
                    Width = 476,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    BackColor = UITheme.BgInput,
                    ForeColor = UITheme.TextLight
                };

                var lblEdition = new Label { Text = "Game Edition:", Location = new Point(18, 380), AutoSize = true, ForeColor = UITheme.TextMuted };
                var cmbEdition = new ComboBox
                {
                    Location = new Point(110, 376),
                    Width = 476,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    BackColor = UITheme.BgInput,
                    ForeColor = UITheme.TextLight
                };

                Action<List<LibraryOptionItem>, List<LibraryOptionItem>> populateOptions = (plats, eds) =>
                {
                    cmbPlatform.Items.Clear();
                    var defaultPlats = new List<ComboItem>
                    {
                        new ComboItem { Id = "6", Name = "Windows PC" },
                        new ComboItem { Id = "130", Name = "Nintendo Switch" },
                        new ComboItem { Id = "508", Name = "Nintendo Switch 2" }
                    };

                    var seenPlats = new HashSet<string>();
                    if (plats != null && plats.Count > 0)
                    {
                        foreach (var p in plats)
                        {
                            seenPlats.Add(p.Id);
                            cmbPlatform.Items.Add(new ComboItem { Id = p.Id, Name = p.Name });
                        }
                    }
                    foreach (var dp in defaultPlats)
                    {
                        if (!seenPlats.Contains(dp.Id))
                        {
                            cmbPlatform.Items.Add(dp);
                        }
                    }

                    string targetPlat = entry.PlatformId.ToString();
                    int selPlatIdx = 0;
                    for (int i = 0; i < cmbPlatform.Items.Count; i++)
                    {
                        if (((ComboItem)cmbPlatform.Items[i]).Id == targetPlat)
                        {
                            selPlatIdx = i;
                            break;
                        }
                    }
                    if (cmbPlatform.Items.Count > 0) cmbPlatform.SelectedIndex = selPlatIdx;

                    cmbEdition.Items.Clear();
                    cmbEdition.Items.Add(new ComboItem { Id = "", Name = "(Standard / Base Game — No specific edition)" });
                    int selEdIdx = 0;
                    if (eds != null)
                    {
                        for (int i = 0; i < eds.Count; i++)
                        {
                            cmbEdition.Items.Add(new ComboItem { Id = eds[i].Id, Name = eds[i].Name });
                            if (!string.IsNullOrEmpty(entry.EditionId) && eds[i].Id == entry.EditionId)
                            {
                                selEdIdx = i + 1;
                            }
                        }
                    }
                    cmbEdition.SelectedIndex = selEdIdx;
                };

                populateOptions(null, null);

                int optionsReqSeq = 0;
                Action<string> loadBackloggdOptions = (gameId) =>
                {
                    if (string.IsNullOrWhiteSpace(gameId)) return;
                    int myOptSeq = Interlocked.Increment(ref optionsReqSeq);
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            var opts = BackloggdClient.GetLibraryOptions(gameId, settings);
                            if (dlg.IsDisposed || !dlg.IsHandleCreated) return;
                            dlg.BeginInvoke(new Action(() =>
                            {
                                if (myOptSeq != optionsReqSeq || dlg.IsDisposed) return;
                                populateOptions(opts.Platforms, opts.Editions);
                            }));
                        }
                        catch { }
                    });
                };

                var chkExclude = new CheckBox
                {
                    Text = "Hide / exclude this game from Collection sync",
                    Checked = entry.Excluded,
                    AutoSize = true,
                    ForeColor = UITheme.TextMuted,
                    Location = new Point(18, 420)
                };

                var btnSaveAndSync = UITheme.CreateStyledButton("✓ Save & Sync to Backloggd Library", UITheme.AccentPink, 260, 38);
                btnSaveAndSync.Location = new Point(18, 462);

                var btnSaveLocal = UITheme.CreateStyledButton("Save Mapping & Options Only", UITheme.AccentBlue, 210, 38);
                btnSaveLocal.Location = new Point(288, 462);

                var btnCancel = UITheme.CreateStyledButton("Cancel", UITheme.BgInput, 80, 38);
                btnCancel.Location = new Point(506, 462);
                btnCancel.Click += (s, e) => dlg.Close();

                string currentSelectedGameId = entry.BackloggdGameId;
                string currentSelectedTitle = entry.BackloggdTitle;
                string currentSelectedYear = entry.ReleaseYear;

                lstResults.SelectedIndexChanged += (s, e) =>
                {
                    var picked = lstResults.SelectedItem as GameSearchResult;
                    if (picked != null)
                    {
                        currentSelectedGameId = picked.GameId;
                        currentSelectedTitle = picked.Title;
                        currentSelectedYear = picked.ReleaseYear;
                        loadBackloggdOptions(picked.GameId);
                    }
                };

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
                                    GameSearchResult matchToSelect = null;
                                    if (!string.IsNullOrEmpty(currentSelectedGameId))
                                    {
                                        matchToSelect = res.Find(r => r.GameId == currentSelectedGameId);
                                    }
                                    if (matchToSelect == null)
                                    {
                                        matchToSelect = GameTitleMatcher.SelectBestMatch(q, res);
                                    }
                                    lstResults.SelectedItem = (matchToSelect != null && lstResults.Items.Contains(matchToSelect))
                                        ? matchToSelect
                                        : lstResults.Items[0];
                                    lblStatus.Text = string.Format("Found {0} match(es) — select one below:", res.Count);
                                    lblStatus.ForeColor = UITheme.SuccessGreen;
                                }
                                else
                                {
                                    lblStatus.Text = "No matches found. Try editing the search title above.";
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

                Action<bool> applyAndClose = (syncNow) =>
                {
                    var picked = lstResults.SelectedItem as GameSearchResult;
                    if (picked != null)
                    {
                        currentSelectedGameId = picked.GameId;
                        currentSelectedTitle = picked.Title;
                        currentSelectedYear = picked.ReleaseYear;
                    }

                    if (syncNow && string.IsNullOrEmpty(currentSelectedGameId))
                    {
                        MessageBox.Show("Please select a Backloggd game from the search results first.", "Select Game",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    entry.BackloggdGameId = currentSelectedGameId ?? "";
                    entry.BackloggdTitle = currentSelectedTitle ?? "";
                    entry.ReleaseYear = currentSelectedYear ?? "";
                    entry.Excluded = chkExclude.Checked;

                    var ownItem = cmbOwnership.SelectedItem as ComboItem;
                    int ownId = 1;
                    if (ownItem != null) int.TryParse(ownItem.Id, out ownId);
                    entry.OwnershipId = ownId;

                    var platItem = cmbPlatform.SelectedItem as ComboItem;
                    int platId = entry.Source == "Steam" ? 6 : 130;
                    if (platItem != null) int.TryParse(platItem.Id, out platId);
                    entry.PlatformId = platId;

                    var storeItem = cmbStorefront.SelectedItem as ComboItem;
                    int storeId;
                    if (ownId == 1 && storeItem != null && !string.IsNullOrEmpty(storeItem.Id) && int.TryParse(storeItem.Id, out storeId))
                    {
                        entry.StorefrontId = storeId;
                    }
                    else
                    {
                        entry.StorefrontId = null;
                    }

                    var edItem = cmbEdition.SelectedItem as ComboItem;
                    if (edItem != null && !string.IsNullOrEmpty(edItem.Id))
                    {
                        entry.EditionId = edItem.Id;
                        entry.EditionTitle = edItem.Name;
                    }
                    else
                    {
                        entry.EditionId = "";
                        entry.EditionTitle = "";
                    }

                    entry.LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    if (!syncNow)
                    {
                        entry.Synced = false;
                    }

                    // Persist updated entry to collection.json
                    var collection = DataStore.LoadCollection();
                    int existingIdx = collection.FindIndex(c => c.Id == entry.Id || (entry.AppId != 0 && c.AppId == entry.AppId));
                    if (existingIdx >= 0)
                    {
                        collection[existingIdx] = entry;
                    }
                    else
                    {
                        collection.Add(entry);
                    }
                    DataStore.SaveCollection(collection);

                    // Save mapping to mappings.json so future playtime sessions also use this exact Backloggd match
                    if (!string.IsNullOrEmpty(entry.BackloggdGameId) && entry.AppId != 0)
                    {
                        var mappings = DataStore.LoadMappings();
                        mappings.RemoveAll(m => m.AppId == entry.AppId);
                        mappings.Add(new GameMapping
                        {
                            AppId = entry.AppId,
                            SwitchApplicationId = entry.SwitchApplicationId,
                            BackloggdGameId = entry.BackloggdGameId,
                            SteamTitle = entry.LocalTitle,
                            BackloggdTitle = entry.BackloggdTitle,
                            ReleaseYear = entry.ReleaseYear,
                            ManualOverride = true,
                            LastUpdated = entry.LastUpdated
                        });
                        DataStore.SaveMappings(mappings);

                        // Also update any unsynced sessions for this AppId
                        var sessions = DataStore.LoadSessions();
                        bool sessionsUpdated = false;
                        foreach (var s in sessions)
                        {
                            if (s.AppId == entry.AppId && !s.Synced)
                            {
                                s.BackloggdGameId = entry.BackloggdGameId;
                                s.GameTitle = entry.BackloggdTitle;
                                sessionsUpdated = true;
                            }
                        }
                        if (sessionsUpdated) DataStore.SaveSessions(sessions);
                    }

                    if (onSaved != null) onSaved(entry, syncNow);
                    dlg.DialogResult = DialogResult.OK;
                    dlg.Close();
                };

                btnSaveAndSync.Click += (s, e) => applyAndClose(true);
                btnSaveLocal.Click += (s, e) => applyAndClose(false);
                lstResults.DoubleClick += (s, e) => applyAndClose(true);

                dlg.Controls.AddRange(new Control[]
                {
                    lblHeader, lblSearch, txtQuery, lblStatus, lstResults,
                    lblPropsHeader, lblOwnership, cmbOwnership, lblStorefront, cmbStorefront,
                    lblPlatform, cmbPlatform, lblEdition, cmbEdition,
                    chkExclude, btnSaveAndSync, btnSaveLocal, btnCancel
                });

                dlg.Shown += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(entry.BackloggdGameId))
                    {
                        loadBackloggdOptions(entry.BackloggdGameId);
                    }
                    runSearch();
                };

                dlg.ShowDialog(owner);
            }
        }
    }
}
