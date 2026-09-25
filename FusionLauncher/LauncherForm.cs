namespace FusionLauncher;

internal sealed class LauncherForm : Form
{
    private static readonly Color Back = Color.FromArgb(18, 20, 25), Panel = Color.FromArgb(29, 32, 39), Fore = Color.FromArgb(238, 240, 244), Accent = Color.FromArgb(79, 128, 255);
    private readonly ModIoService _modIo = new();
    private readonly SteamLobbyService _steam;
    private readonly Label _status = new() { AutoSize = true, Text = "Ready" };
    private readonly DataGridView _servers = Grid(), _codeMods = Grid(), _mods = Grid();
    private readonly Button _connect = MakeButton("Connect with Steam"), _refresh = MakeButton("Refresh", false), _join = MakeButton("Join selected server", false);
    private readonly TextBox _modSearch = new() { Width = 300, PlaceholderText = "Search BONELAB mods" };

    public LauncherForm()
    {
        _steam = new SteamLobbyService(_modIo);
        Text = "OneOfUs Laucher for Fusion"; MinimumSize = new Size(900, 560); Size = new Size(1120, 720); StartPosition = FormStartPosition.CenterScreen;
        BackColor = Back; ForeColor = Fore; Font = new Font("Segoe UI Variable Text", 10f);
        var tabs = new TabControl { Dock = DockStyle.Fill }; tabs.TabPages.Add(ServerTab()); tabs.TabPages.Add(CodeModsTab()); tabs.TabPages.Add(ModIoTab());
        tabs.TabPages.Add(FusionProfileTab()); tabs.TabPages.Add(DedicatedServerTab()); tabs.TabPages.Add(CreditsTab());
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(18), BackColor = Back };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { Text = "ONEOFUS LAUCHER FOR FUSION", AutoSize = true, ForeColor = Fore, Font = new Font("Segoe UI Variable Display Semibold", 25f) }, 0, 0); root.Controls.Add(tabs, 0, 1); root.Controls.Add(_status, 0, 2); Controls.Add(root);
        ApplyDarkTheme(this);
        _connect.Click += async (_, _) => await ConnectAsync(); _refresh.Click += async (_, _) => await RefreshServersAsync(); _join.Click += (_, _) => JoinSelected();
        _servers.SelectionChanged += (_, _) => _join.Enabled = _servers.SelectedRows.Count == 1; _servers.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) JoinSelected(); }; FormClosed += (_, _) => _steam.Dispose();
    }

    private TabPage ServerTab()
    {
        _servers.RowTemplate.Height = 82;
        _servers.Columns.Add(new DataGridViewImageColumn { HeaderText = "Map", DataPropertyName = nameof(LobbyEntry.MapPicture), MinimumWidth = 135, Width = 135, FillWeight = 35, ImageLayout = DataGridViewImageCellLayout.Zoom });
        AddColumn(_servers, "Server", nameof(LobbyEntry.DisplayName), 180); AddColumn(_servers, "Host", nameof(LobbyEntry.Host), 120); AddColumn(_servers, "Players", nameof(LobbyEntry.PlayerCount), 65); AddColumn(_servers, "Max", nameof(LobbyEntry.MaxPlayers), 50); AddColumn(_servers, "Level", nameof(LobbyEntry.Level), 170); AddColumn(_servers, "Mode", nameof(LobbyEntry.DisplayMode), 100); AddColumn(_servers, "Version", nameof(LobbyEntry.Version), 70);
        var launch = MakeButton("Launch BONELAB"); launch.Click += (_, _) => Run(GameLauncher.LaunchGame, "Launching BONELAB...");
        var install = MakeButton("Install/Update Fusion 1.14.2"); install.Click += (_, _) => Run(() => _status.Text = $"Installed to {GameLauncher.InstallBundledFusion()}", null);
        return MakeTab("Servers", [_connect, _refresh, _join, launch, install], _servers);
    }

    private TabPage CodeModsTab()
    {
        AddColumn(_codeMods, "Code mod", nameof(CodeModEntry.Name), 300); AddColumn(_codeMods, "State", nameof(CodeModEntry.State), 100);
        var refresh = MakeButton("Refresh"); refresh.Click += (_, _) => LoadCodeMods(); var toggle = MakeButton("Enable / Disable selected"); toggle.Click += (_, _) => ToggleCodeMod(); var open = MakeButton("Open Mods folder"); open.Click += (_, _) => OpenModsFolder();
        var tab = MakeTab("Code Mods", [refresh, toggle, open], _codeMods); tab.Enter += (_, _) => LoadCodeMods(); return tab;
    }

    private TabPage ModIoTab()
    {
        _mods.RowTemplate.Height = 105;
        _mods.Columns.Add(new DataGridViewImageColumn { HeaderText = "Preview", DataPropertyName = nameof(ModIoEntry.Picture), MinimumWidth = 175, Width = 175, FillWeight = 45, ImageLayout = DataGridViewImageCellLayout.Zoom });
        _mods.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Installed", DataPropertyName = nameof(ModIoEntry.Installed), MinimumWidth = 72, Width = 72, FillWeight = 18 });
        _mods.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Subscribed", DataPropertyName = nameof(ModIoEntry.Subscribed), MinimumWidth = 82, Width = 82, FillWeight = 20 });
        AddColumn(_mods, "Mod", nameof(ModIoEntry.Name), 190); AddColumn(_mods, "Creator", nameof(ModIoEntry.Author), 110); AddColumn(_mods, "About", nameof(ModIoEntry.Summary), 260); AddColumn(_mods, "Downloads", nameof(ModIoEntry.Downloads), 85);
        var connect = MakeButton("Connect to mod.io"); connect.Click += (_, _) => { using var dialog = new ModIoConnectForm(_modIo); if (dialog.ShowDialog(this) == DialogResult.OK) _status.Text = "Connected to mod.io."; };
        var sort = new ComboBox { Width = 145, DropDownStyle = ComboBoxStyle.DropDownList };
        sort.Items.AddRange(["Recently updated", "Newest", "Most downloaded", "Most subscribed", "Highest rated", "Name A-Z"]); sort.SelectedIndex = 0;
        var type = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
        type.Items.AddRange(["All types", "Avatar", "Level", "Spawnable", "Code Mod", "Utility"]); type.SelectedIndex = 0;
        var mature = new CheckBox { Text = "Mature", AutoSize = true };
        string SortValue() => sort.SelectedIndex switch { 1 => "-date_live", 2 => "-downloads_total", 3 => "-subscribers_total", 4 => "-ratings_weighted_aggregate", 5 => "name", _ => "-date_updated" };
        string TagValue() => type.SelectedIndex == 0 ? string.Empty : type.SelectedItem?.ToString() ?? string.Empty;
        var search = MakeButton("Search"); search.Click += async (_, _) => await SearchModsAsync(SortValue(), TagValue(), mature.Checked); var open = MakeButton("Open selected on mod.io"); open.Click += (_, _) => { if (_mods.CurrentRow?.DataBoundItem is ModIoEntry mod) ModIoService.Open(mod); };
        var subscriptions = MakeButton("My subscriptions"); subscriptions.Click += async (_, _) => await LoadSubscriptionsAsync();
        var install = MakeButton("Download and install");
        install.Click += async (_, _) =>
        {
            if (_mods.CurrentRow?.DataBoundItem is not ModIoEntry mod) return;
            try
            {
                install.Enabled = false; _status.Text = $"Downloading {mod.Name}...";
                var progress = new Progress<int>(percent => _status.Text = $"Downloading {mod.Name}: {percent}%");
                string folder = await _modIo.DownloadAndInstallAsync(mod, progress);
                _mods.Refresh();
                _status.Text = $"Installed {mod.Name} into {folder}.";
            }
            catch (Exception exception) { MessageBox.Show(this, exception.Message, "mod.io install failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { install.Enabled = true; }
        };
        return MakeTab("mod.io Browser", [connect, _modSearch, sort, type, mature, search, subscriptions, install, open], _mods);
    }

    private TabPage DedicatedServerTab()
    {
        DedicatedServerConfig saved = DedicatedServerLauncher.Load();
        var name = new TextBox { Text = saved.Name, Width = 300 };
        var description = new TextBox { Text = saved.Description, Width = 300 };
        var map = new ComboBox { Width = 420, DropDownStyle = ComboBoxStyle.DropDownList };
        void LoadMaps()
        {
            var maps = MapCatalog.Load().ToList();
            map.DataSource = null;
            map.DataSource = maps;
        }
        LoadMaps();
        if (!string.IsNullOrWhiteSpace(saved.MapBarcode))
            map.SelectedItem = map.Items.Cast<MapEntry>().FirstOrDefault(entry => entry.Barcode == saved.MapBarcode);
        decimal Limit(decimal value, decimal min, decimal max) => Math.Clamp(value, min, max);
        var players = new NumericUpDown { Minimum = 2, Maximum = 255, Value = Limit(saved.MaxPlayers, 2, 255), Width = 90 };
        var tick = new NumericUpDown { Minimum = 20, Maximum = 30, Value = Limit(saved.TickRate, 20, 30), Width = 90 };
        var playerRange = new NumericUpDown { Minimum = 5, Maximum = 1000, Value = Limit((decimal)saved.PlayerRange, 5, 1000), Width = 90 };
        var propRange = new NumericUpDown { Minimum = 5, Maximum = 1000, Value = Limit((decimal)saved.PropRange, 5, 1000), Width = 90 };
        var voiceRange = new NumericUpDown { Minimum = 5, Maximum = 1000, Value = Limit((decimal)saved.VoiceRange, 5, 1000), Width = 90 };
        var propLimit = new NumericUpDown { Minimum = 1, Maximum = 255, Value = Limit(saved.PropLimit, 1, 255), Width = 90 };
        var spawnLimit = new NumericUpDown { Minimum = 1, Maximum = 500, Value = Limit(saved.SpawnLimit, 1, 500), Width = 90 };
        var mapProps = new CheckBox { Text = "Synchronize props included in maps", Checked = saved.MapProps, AutoSize = true };
        var privateServer = new CheckBox { Text = "Private server", Checked = saved.Private, AutoSize = true };
        var voice = new CheckBox { Text = "Voice chat", Checked = saved.VoiceChat, AutoSize = true };
        var friendlyFire = new CheckBox { Text = "Friendly fire", Checked = saved.FriendlyFire, AutoSize = true };
        var mortality = new CheckBox { Text = "Player mortality", Checked = saved.Mortality, AutoSize = true };
        var adaptive = new CheckBox { Text = "Adaptive pose rates", Checked = saved.AdaptivePoseRates, AutoSize = true };
        var relevance = new CheckBox { Text = "Host relay culling (host limits what each client receives)", Checked = saved.HostRelayCulling, AutoSize = true };

        DedicatedServerConfig Read() => new() { Name = name.Text, Description = description.Text, MapBarcode = (map.SelectedItem as MapEntry)?.Barcode ?? string.Empty, MaxPlayers = (int)players.Value, TickRate = (int)tick.Value, PlayerRange = (float)playerRange.Value, PropRange = (float)propRange.Value, VoiceRange = (float)voiceRange.Value, PropLimit = (int)propLimit.Value, HostPropLimit = 10000, SpawnLimit = (int)spawnLimit.Value, MapProps = mapProps.Checked, Private = privateServer.Checked, VoiceChat = voice.Checked, FriendlyFire = friendlyFire.Checked, Mortality = mortality.Checked, AdaptivePoseRates = adaptive.Checked, HostRelayCulling = relevance.Checked };

        void SaveSettings(object? _, EventArgs __) => DedicatedServerLauncher.Save(Read());
        name.TextChanged += SaveSettings; description.TextChanged += SaveSettings; map.SelectedIndexChanged += SaveSettings;
        players.ValueChanged += SaveSettings; tick.ValueChanged += SaveSettings; playerRange.ValueChanged += SaveSettings;
        propRange.ValueChanged += SaveSettings; voiceRange.ValueChanged += SaveSettings; propLimit.ValueChanged += SaveSettings; spawnLimit.ValueChanged += SaveSettings;
        mapProps.CheckedChanged += SaveSettings; privateServer.CheckedChanged += SaveSettings; voice.CheckedChanged += SaveSettings;
        friendlyFire.CheckedChanged += SaveSettings; mortality.CheckedChanged += SaveSettings; adaptive.CheckedChanged += SaveSettings; relevance.CheckedChanged += SaveSettings;

        var apply = MakeButton("Apply live settings / Change map"); apply.Click += (_, _) => { DedicatedServerLauncher.Save(Read()); _status.Text = "Dedicated settings saved; a running server will apply them shortly."; };
        var start = MakeButton("Start dedicated server"); start.Click += async (_, _) => { try { _status.Text = "Dedicated server starting headless…"; await DedicatedServerLauncher.StartAsync(Read()); _status.Text = "Dedicated server stopped; code mods restored."; } catch (Exception e) { MessageBox.Show(this, e.Message, "Dedicated server", MessageBoxButtons.OK, MessageBoxIcon.Error); } };
        var stop = MakeButton("Stop dedicated server"); stop.Click += (_, _) => DedicatedServerLauncher.Stop();
        var refreshMaps = MakeButton("Rescan maps"); refreshMaps.Click += (_, _) => LoadMaps();

        var playerGrid = Grid();
        playerGrid.Height = 245;
        playerGrid.RowTemplate.Height = 64;
        playerGrid.Columns.Add(new DataGridViewImageColumn { HeaderText = "Avatar", DataPropertyName = nameof(DedicatedPlayerEntry.Picture), Width = 100, ImageLayout = DataGridViewImageCellLayout.Zoom });
        AddColumn(playerGrid, "Player", nameof(DedicatedPlayerEntry.Name), 150);
        AddColumn(playerGrid, "Avatar", nameof(DedicatedPlayerEntry.Avatar), 140);
        AddColumn(playerGrid, "Permission", nameof(DedicatedPlayerEntry.Permission), 90);
        AddColumn(playerGrid, "Spawning", nameof(DedicatedPlayerEntry.SpawnAccess), 75);
        DedicatedPlayerEntry? SelectedPlayer() => playerGrid.CurrentRow?.DataBoundItem as DedicatedPlayerEntry;
        void Command(string action, string? value = null) { var player = SelectedPlayer(); if (player is null || player.IsHost) return; DedicatedServerBridge.Send(action, player, value); _status.Text = $"Sent {action} for {player.Name}."; }
        var kick = MakeButton("Kick"); kick.Click += (_, _) => Command("kick");
        var ban = MakeButton("Ban"); ban.Click += (_, _) => Command("ban");
        var teleportToMe = MakeButton("Teleport to me"); teleportToMe.Click += (_, _) => Command("teleportToMe");
        var teleportToThem = MakeButton("Teleport to them"); teleportToThem.Click += (_, _) => Command("teleportToThem");
        var blockSpawns = MakeButton("Toggle spawning / Spawn gun"); blockSpawns.Click += (_, _) => { var player = SelectedPlayer(); if (player != null) Command(player.SpawnBlocked ? "allowSpawns" : "blockSpawns"); };
        var permission = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, DataSource = new[] { "GUEST", "DEFAULT", "OPERATOR", "OWNER" } };
        var setPermission = MakeButton("Set permission"); setPermission.Click += (_, _) => Command("permission", permission.SelectedItem?.ToString());
        var playerActions = new FlowLayoutPanel { AutoSize = true };
        playerActions.Controls.AddRange([kick, ban, permission, setPermission, teleportToMe, teleportToThem, blockSpawns]);
        var imageCache = new Dictionary<int, Image?>();
        var playerTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        playerTimer.Tick += async (_, _) =>
        {
            ulong? selectedId = SelectedPlayer()?.PlatformId;
            var entries = DedicatedServerBridge.ReadPlayers().ToList();
            foreach (var entry in entries.Where(entry => entry.AvatarModId >= 0))
            {
                if (!imageCache.TryGetValue(entry.AvatarModId, out var picture)) imageCache[entry.AvatarModId] = picture = await _modIo.GetModImageAsync(entry.AvatarModId);
                entry.Picture = picture;
            }
            playerGrid.DataSource = entries;
            if (selectedId.HasValue)
                foreach (DataGridViewRow row in playerGrid.Rows)
                    if (row.DataBoundItem is DedicatedPlayerEntry entry && entry.PlatformId == selectedId.Value) { row.Selected = true; playerGrid.CurrentCell = row.Cells.Cast<DataGridViewCell>().First(cell => cell.Visible); break; }
        };
        playerTimer.Start();

        var form = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(18) };
        void Row(string label, Control control) { form.RowStyles.Add(new RowStyle(SizeType.AutoSize)); form.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 7, 12, 0) }); form.Controls.Add(control); }
        Row("Server name", name); Row("Description", description); Row("Map barcode", map); Row("Maximum players", players); Row("Network tick rate", tick); Row("Player relevance range", playerRange); Row("Prop relevance range", propRange); Row("Voice relevance range", voiceRange); Row("Props per player", propLimit); Row("Host prop limit", new Label { Text = "Unlimited", AutoSize = true, Padding = new Padding(0, 7, 0, 0) }); Row("Spawns per 10 seconds", spawnLimit); Row("Map props", mapProps); Row("Privacy", privateServer); Row("Audio", voice); Row("Combat", friendlyFire); Row("Health", mortality); Row("Pose networking", adaptive); Row("Traffic filtering", relevance);
        var buttons = new FlowLayoutPanel { AutoSize = true }; buttons.Controls.Add(start); buttons.Controls.Add(apply); buttons.Controls.Add(stop); buttons.Controls.Add(refreshMaps); Row("Controls", buttons);
        var mapActions = new FlowLayoutPanel { AutoSize = true };
        var cleanMap = MakeButton("Clean map"); cleanMap.Click += (_, _) => DedicatedServerBridge.SendGlobal("cleanMap");
        var respawnProps = MakeButton("Respawn all props"); respawnProps.Click += (_, _) => DedicatedServerBridge.SendGlobal("respawnProps");
        mapActions.Controls.AddRange([cleanMap, respawnProps]); Row("Map actions", mapActions);
        Row("Connected players", playerGrid); Row("Player actions", playerActions);
        var tab = new TabPage("Dedicated Server") { BackColor = Back, ForeColor = Fore };
        tab.Controls.Add(form);
        tab.Disposed += (_, _) => { playerTimer.Stop(); playerTimer.Dispose(); DedicatedServerLauncher.Save(Read()); };
        return tab;
    }

    private TabPage FusionProfileTab()
    {
        List<FusionProfile> profiles = FusionProfileService.LoadProfiles();
        var profile = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList, DataSource = profiles };
        var profileName = new TextBox { Width = 260 }; var nickname = new TextBox { Width = 320 }; var description = new TextBox { Width = 480 };
        var visibility = new ComboBox { Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
        visibility.Items.AddRange(["SHOW_WITH_PREFIX", "SHOW", "HIDE"]);
        var nameTags = new CheckBox { Text = "Show client nametags", AutoSize = true };
        var hue = new NumericUpDown { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05m, Width = 100 };
        var saturation = new NumericUpDown { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05m, Width = 100 };
        var value = new NumericUpDown { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.05m, Width = 100 };
        void Show(FusionProfile item) { profileName.Text = item.ProfileName; nickname.Text = item.Nickname; description.Text = item.Description; visibility.SelectedItem = item.NicknameVisibility; if (visibility.SelectedIndex < 0) visibility.SelectedIndex = 0; nameTags.Checked = item.NameTags; hue.Value = (decimal)Math.Clamp(item.Hue, 0, 1); saturation.Value = (decimal)Math.Clamp(item.Saturation, 0, 1); value.Value = (decimal)Math.Clamp(item.Value, 0, 1); }
        FusionProfile Read() => new() { ProfileName = string.IsNullOrWhiteSpace(profileName.Text) ? "Profile" : profileName.Text.Trim(), Nickname = nickname.Text, Description = description.Text, NicknameVisibility = visibility.SelectedItem?.ToString() ?? "SHOW_WITH_PREFIX", NameTags = nameTags.Checked, Hue = (float)hue.Value, Saturation = (float)saturation.Value, Value = (float)value.Value };
        profile.SelectedIndexChanged += (_, _) => { if (profile.SelectedItem is FusionProfile item) Show(item); };
        if (profiles.Count > 0) Show(profiles[0]);
        void Store(bool apply)
        {
            FusionProfile item = Read(); int index = profiles.FindIndex(existing => existing.ProfileName.Equals(item.ProfileName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) profiles[index] = item; else profiles.Add(item);
            FusionProfileService.SaveProfiles(profiles); profile.DataSource = null; profile.DataSource = profiles; profile.SelectedItem = item;
            if (apply) FusionProfileService.Apply(item);
            _status.Text = apply ? $"Fusion profile '{item.ProfileName}' applied." : $"Fusion profile '{item.ProfileName}' saved.";
        }
        var save = MakeButton("Save profile"); save.Click += (_, _) => Run(() => Store(false), null);
        var apply = MakeButton("Apply before launch"); apply.Click += (_, _) => Run(() => Store(true), null);
        var launch = MakeButton("Apply and launch BONELAB"); launch.Click += (_, _) => Run(() => { Store(true); GameLauncher.LaunchGame(); }, "Applying Fusion profile and launching BONELAB...");
        var remove = MakeButton("Delete profile"); remove.Click += (_, _) => { if (profile.SelectedItem is not FusionProfile item || profiles.Count <= 1) return; profiles.Remove(item); FusionProfileService.SaveProfiles(profiles); profile.DataSource = null; profile.DataSource = profiles; };
        var form = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(28) };
        void Row(string label, Control control) { form.RowStyles.Add(new RowStyle(SizeType.AutoSize)); form.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 8, 16, 0) }); form.Controls.Add(control); }
        Row("Saved profile", profile); Row("Profile name", profileName); Row("Fusion nickname", nickname); Row("Profile description", description); Row("Nickname visibility", visibility); Row("Nametags", nameTags); Row("Nametag hue", hue); Row("Nametag saturation", saturation); Row("Nametag brightness", value);
        var actions = new FlowLayoutPanel { AutoSize = true }; actions.Controls.AddRange([save, apply, launch, remove]); Row("Actions", actions);
        var tab = new TabPage("Fusion Profile") { BackColor = Back, ForeColor = Fore }; tab.Controls.Add(form); return tab;
    }

    private TabPage CreditsTab()
    {
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(28), AutoScroll = true };
        layout.Controls.Add(new Label { Text = "Credits", AutoSize = true, Font = new Font("Segoe UI Variable Display Semibold", 24f), Margin = new Padding(0, 0, 0, 20) });
        layout.Controls.Add(new Label { Text = "Lakatrazz - Fusion dev", AutoSize = true, Font = new Font("Segoe UI Variable Text Semibold", 12f), Margin = new Padding(0, 4, 0, 4) });
        layout.Controls.Add(new Label { Text = "Kuri - Launcher creator", AutoSize = true, Font = new Font("Segoe UI Variable Text Semibold", 12f), Margin = new Padding(0, 4, 0, 22) });
        layout.Controls.Add(new Label { Text = "One Of Us Community Discord:", AutoSize = true, Margin = new Padding(0, 4, 0, 6) });
        var discord = new LinkLabel { Text = "https://discord.gg/fZWHmPPqA", AutoSize = true, LinkColor = Color.FromArgb(120, 160, 255), ActiveLinkColor = Color.White };
        discord.LinkClicked += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(discord.Text) { UseShellExecute = true });
        layout.Controls.Add(discord);
        var tab = new TabPage("Credits") { BackColor = Back, ForeColor = Fore };
        tab.Controls.Add(layout);
        return tab;
    }

    private async Task ConnectAsync() { try { SetBusy(true, "Connecting to Steam..."); _steam.Connect(); _connect.Enabled = false; _refresh.Enabled = true; await RefreshServersAsync(); } catch (Exception e) { _connect.Enabled = true; SetBusy(false, $"Steam connection failed: {e.Message}"); } }
    private async Task RefreshServersAsync() { try { SetBusy(true, "Loading Fusion servers and pictures..."); var rows = await _steam.GetLobbiesAsync(); _servers.DataSource = rows.ToList(); SetBusy(false, $"Connected as {_steam.UserName} — {rows.Count} server(s)"); } catch (Exception e) { SetBusy(false, $"Could not load servers: {e.Message}"); } }
    private void JoinSelected() { if (_servers.CurrentRow?.DataBoundItem is LobbyEntry lobby) Run(() => GameLauncher.Join(lobby), $"Launching and joining {lobby.DisplayName}..."); }
    private void LoadCodeMods()
    {
        string? game = GameLauncher.GetBonelabDirectory(); if (game is null) { _status.Text = "BONELAB installation was not found."; return; }
        string folder = Path.Combine(game, "Mods"); Directory.CreateDirectory(folder);
        _codeMods.DataSource = Directory.EnumerateFiles(folder).Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase)).Select(p => new CodeModEntry { Path = p }).OrderBy(m => m.Name).ToList(); _status.Text = $"Loaded {_codeMods.RowCount} code mod(s).";
    }
    private void ToggleCodeMod() { if (_codeMods.CurrentRow?.DataBoundItem is not CodeModEntry mod) return; try { File.Move(mod.Path, mod.Enabled ? mod.Path + ".disabled" : mod.Path[..^".disabled".Length]); LoadCodeMods(); } catch (Exception e) { MessageBox.Show(this, e.Message, "Could not change code mod", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
    private void OpenModsFolder() { string? game = GameLauncher.GetBonelabDirectory(); if (game is null) return; string folder = Path.Combine(game, "Mods"); Directory.CreateDirectory(folder); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true }); }
    private async Task SearchModsAsync(string sort = "-date_updated", string tag = "", bool showMature = false) { try { SetBusy(true, "Searching mod.io..."); var rows = await _modIo.SearchAsync(_modSearch.Text, sort, tag, showMature); _mods.DataSource = rows.ToList(); SetBusy(false, $"Found {rows.Count} mod(s)."); } catch (Exception e) { SetBusy(false, $"mod.io search failed: {e.Message}"); } }

    private async Task LoadSubscriptionsAsync() { try { SetBusy(true, "Loading your mod.io subscriptions..."); var rows = await _modIo.GetSubscriptionsAsync(); _mods.DataSource = rows.ToList(); SetBusy(false, $"Loaded {rows.Count} subscribed mod(s); checked items are installed."); } catch (Exception e) { SetBusy(false, $"Could not load subscriptions: {e.Message}"); } }
    private void Run(Action action, string? status) { try { if (status is not null) _status.Text = status; action(); } catch (Exception e) { MessageBox.Show(this, e.Message, "Fusion Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
    private void SetBusy(bool busy, string status) { UseWaitCursor = busy; _refresh.Enabled = !busy && !_connect.Enabled; _status.Text = status; }

    private static TabPage MakeTab(string title, Control[] actions, Control content) { var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) }; bar.Controls.AddRange(actions); var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 }; layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.Controls.Add(bar, 0, 0); layout.Controls.Add(content, 0, 1); var tab = new TabPage(title) { BackColor = Back, ForeColor = Fore }; tab.Controls.Add(layout); return tab; }
    private static DataGridView Grid() => new() { Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, BackgroundColor = Back, GridColor = Color.FromArgb(55, 59, 68), BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private static Button MakeButton(string text, bool enabled = true) => new() { Text = text, AutoSize = true, Enabled = enabled, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White };
    private static void AddColumn(DataGridView grid, string title, string property, int width) => grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, DataPropertyName = property, MinimumWidth = width });
    private static void ApplyDarkTheme(Control root) { if (root is not Button) root.BackColor = Back; root.ForeColor = Fore; if (root is TextBox box) { box.BackColor = Panel; box.ForeColor = Fore; box.BorderStyle = BorderStyle.FixedSingle; } if (root is DataGridView grid) { grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersDefaultCellStyle.BackColor = Panel; grid.ColumnHeadersDefaultCellStyle.ForeColor = Fore; grid.DefaultCellStyle.BackColor = Back; grid.DefaultCellStyle.ForeColor = Fore; grid.DefaultCellStyle.SelectionBackColor = Accent; grid.DefaultCellStyle.SelectionForeColor = Color.White; } foreach (Control child in root.Controls) ApplyDarkTheme(child); }
}
