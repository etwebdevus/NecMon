using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using PeerViewer.Models;
using PeerViewer.Network;

namespace PeerViewer
{
    public partial class MainForm : Form
    {
        private PeerDiscovery _peerDiscovery;
        private ScreenshotService _screenshotService;
        private Dictionary<string, PeerConnection> _peerConnections;
        private ListView _peersListView;
        private FlowLayoutPanel _thumbnailsPanel;
        private Button _refreshButton;
        private Label _statusLabel;
        private Panel _mainPanel;
        private SplitContainer _splitContainer;
        private NumericUpDown _refreshRateNumeric;
        private Label _refreshRateLabel;
        private Dictionary<string, ThumbnailControl> _thumbnailControls;
        private Dictionary<string, ScreenshotViewerForm> _screenshotViewers;
        private bool _screenshotCaptureDisabled = false;
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private Dictionary<string, DateTime> _lastScreenshotReceivedAt;
        private Timer _heartbeatTimer;
        private bool _exitRequested = false;

        public MainForm()
        {
            InitializeComponent();
            InitializeServices();
            SetupEventHandlers();
        }

        private void InitializeComponent()
        {
            this.Text = "Peer Viewer - Network Screenshot Viewer";
            this.Size = new Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.ShowInTaskbar = false; // Hide from taskbar
            this.WindowState = FormWindowState.Minimized; // Start minimized
            
            // Set application icon
            try
            {
                this.Icon = new Icon("Femfoyou-Angry-Birds-Angry-bird.512.ico");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load icon: {ex.Message}");
            }

            _mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };

            _splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 250
            };

            // Left panel - Peer list
            var leftPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };

            _peersListView = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                Dock = DockStyle.Fill
            };
            _peersListView.Columns.Add("Name", 150);
            _peersListView.Columns.Add("IP Address", 120);
            _peersListView.Columns.Add("Machine", 120);
            _peersListView.Columns.Add("Status", 80);
            _peersListView.Columns.Add("Resolution", 120);

            var buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50
            };

            _refreshRateLabel = new Label
            {
                Text = "Refresh Rate (ms):",
                Width = 100,
                Height = 20,
                Location = new Point(10, 15),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _refreshRateNumeric = new NumericUpDown
            {
                Minimum = 100,
                Maximum = 10000,
                Value = 1000,
                Width = 80,
                Height = 20,
                Location = new Point(110, 15)
            };

            _refreshButton = new Button
            {
                Text = "Refresh",
                Width = 80,
                Height = 30,
                Location = new Point(200, 10)
            };

            var disableScreencaptureButton = new Button
            {
                Text = "Disable Screencapture",
                Width = 150,
                Height = 30,
                Location = new Point(290, 10),
                BackColor = Color.LightCoral
            };
            disableScreencaptureButton.Click += OnDisableScreencaptureClicked;
            disableScreencaptureButton.Name = "disableScreencaptureButton";

            buttonPanel.Controls.AddRange(new Control[] { _refreshRateLabel, _refreshRateNumeric, _refreshButton, disableScreencaptureButton });

            leftPanel.Controls.AddRange(new Control[] { _peersListView, buttonPanel });

            // Right panel - Thumbnails viewer
            var rightPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5)
            };

            _thumbnailsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.LightGray,
                Padding = new Padding(5)
            };

            // Add a placeholder label for when no peers are connected
            var placeholderLabel = new Label
            {
                Text = "No peers discovered yet.\nPeers will appear here as thumbnails when discovered.",
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                AutoSize = false
            };
            _thumbnailsPanel.Controls.Add(placeholderLabel);

            _statusLabel = new Label
            {
                Text = "Ready",
                Dock = DockStyle.Bottom,
                Height = 25,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.LightGray
            };

            rightPanel.Controls.AddRange(new Control[] { _thumbnailsPanel, _statusLabel });

            _splitContainer.Panel1.Controls.Add(leftPanel);
            _splitContainer.Panel2.Controls.Add(rightPanel);
            _splitContainer.FixedPanel = FixedPanel.Panel1; // keep peer list small
            _splitContainer.Panel1MinSize = 400;
            _splitContainer.SplitterDistance = 400;

            _mainPanel.Controls.Add(_splitContainer);
            this.Controls.Add(_mainPanel);

            // Setup system tray icon
            SetupTrayIcon();
        }

        private void InitializeServices()
        {
            _peerConnections = new Dictionary<string, PeerConnection>();
            _thumbnailControls = new Dictionary<string, ThumbnailControl>();
            _screenshotViewers = new Dictionary<string, ScreenshotViewerForm>();
            _screenshotService = new ScreenshotService();
            _peerDiscovery = new PeerDiscovery();
            // Load saved user name from registry
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\\NecMon"))
                {
                    var val = key?.GetValue("UserName") as string;
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        _peerDiscovery.LocalUserName = val;
                    }
                }
            }
            catch { }

            _lastScreenshotReceivedAt = new Dictionary<string, DateTime>();
            _heartbeatTimer = new Timer();
            _heartbeatTimer.Interval = 5000; // 5s heartbeat
            _heartbeatTimer.Tick += OnHeartbeatTick;
            _heartbeatTimer.Start();

            _screenshotService.ScreenshotUpdated += OnScreenshotUpdated;
            _peerDiscovery.PeerDiscovered += async (sender, peerInfo) => await OnPeerDiscovered(sender, peerInfo);
            _peerDiscovery.PeerLost += async (sender, peerInfo) => await OnPeerLost(sender, peerInfo);
        }

        private void SetupEventHandlers()
        {
            _peersListView.SelectedIndexChanged += OnPeerSelectionChanged;
            _peersListView.DoubleClick += OnPeerDoubleClicked;
            _refreshButton.Click += OnRefreshClicked;
            _refreshRateNumeric.ValueChanged += OnRefreshRateChanged;
            this.Load += OnFormLoad;
            this.FormClosing += OnFormClosing;
            this.Resize += OnFormResize;
        }

        private void SetupTrayIcon()
        {
            // Create tray menu
            _trayMenu = new ContextMenuStrip();
            
            var settingsItem = new ToolStripMenuItem("Settings");
            settingsItem.Click += OnSettingsClicked;
            _trayMenu.Items.Add(settingsItem);

            var showWindowItem = new ToolStripMenuItem("Show Window");
            showWindowItem.Click += OnShowWindowClicked;
            _trayMenu.Items.Add(showWindowItem);

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += OnExitMenuClicked;
            _trayMenu.Items.Add(exitItem);

            // Create tray icon
            _trayIcon = new NotifyIcon();
            _trayIcon.Icon = this.Icon ?? SystemIcons.Application; // Use app icon or fallback to system icon
            _trayIcon.Text = "Peer Viewer - Network Screenshot Viewer";
            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.Visible = true;
            
            // Double-click tray icon to show window
            _trayIcon.DoubleClick += OnTrayIconDoubleClicked;
        }

        private void OnSettingsClicked(object sender, EventArgs e)
        {
            using (var form = new SettingsForm(_peerDiscovery.LocalUserName))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    _peerDiscovery.LocalUserName = form.UserName;
                    // Persist to registry for next start
                    try
                    {
                        using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\\NecMon"))
                        {
                            key?.SetValue("UserName", _peerDiscovery.LocalUserName);
                        }
                    }
                    catch { }

                    // Re-broadcast discovery so others see updated name
                    Task.Run(async () => { try { await _peerDiscovery.SendDiscoveryBroadcastAsync(); } catch { } });

                    UpdateStatus($"User name set to '{_peerDiscovery.LocalUserName}' and broadcasted.");
                }
            }
        }

        private async void OnFormLoad(object sender, EventArgs e)
        {
            try
            {
                // Set up Windows startup
                SetWindowsStartup();
                
                UpdateStatus("Starting peer discovery...");
                await _peerDiscovery.StartAsync();
                UpdateStatus("Peer discovery started. Scanning for peers...");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start peer discovery: {ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateStatus("Failed to start peer discovery");
            }
        }

        private void SetWindowsStartup()
        {
            try
            {
                string appPath = Application.ExecutablePath;
                string keyName = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                string appName = "PeerViewer";

                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyName, true))
                {
                    if (key != null)
                    {
                        // Check if already registered
                        string existingValue = key.GetValue(appName) as string;
                        if (existingValue != appPath)
                        {
                            // Register for startup
                            key.SetValue(appName, appPath);
                            System.Diagnostics.Debug.WriteLine($"Registered PeerViewer for Windows startup: {appPath}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set Windows startup: {ex.Message}");
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            // Always hide to tray instead of exiting
            if (e.CloseReason == CloseReason.UserClosing && !_exitRequested)
            {
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
                this.ShowInTaskbar = false;
                //this.Hide();
            }
        }

        private void OnExitMenuClicked(object sender, EventArgs e)
        {
            _exitRequested = true;
            _trayIcon?.Dispose();
            Application.Exit();
        }

        private async Task OnPeerDiscovered(object sender, PeerInfo peerInfo)
        {
            if (this.InvokeRequired)
            {
                await Task.Run(() => this.Invoke(new Action(async () => await OnPeerDiscovered(sender, peerInfo))));
                return;
            }

            AddPeerToList(peerInfo);
            CreateThumbnailControl(peerInfo);
            var resolutionInfo = !string.IsNullOrEmpty(peerInfo.Resolution) && peerInfo.Resolution != "Unknown" 
                ? $" ({peerInfo.Resolution})" : "";
            UpdateStatus($"Peer discovered: {peerInfo.Name}{resolutionInfo}");
            
            // Automatically connect to the peer
            await ConnectToPeerAsync(peerInfo);
        }

        private async Task OnPeerLost(object sender, PeerInfo peerInfo)
        {
            if (this.InvokeRequired)
            {
                await Task.Run(() => this.Invoke(new Action(async () => await OnPeerLost(sender, peerInfo))));
                return;
            }

            // Disconnect from the peer
            if (_peerConnections.TryGetValue(peerInfo.Id, out var connection))
            {
                try
                {
                    await connection.StopScreenshotStreamAsync();
                }
                catch
                {
                    // Ignore errors when stopping stream
                }
                
                connection.Dispose();
                _peerConnections.Remove(peerInfo.Id);
            }

            RemovePeerFromList(peerInfo);
            RemoveThumbnailControl(peerInfo);
            var resolutionInfo = !string.IsNullOrEmpty(peerInfo.Resolution) && peerInfo.Resolution != "Unknown" 
                ? $" ({peerInfo.Resolution})" : "";
            UpdateStatus($"Peer lost: {peerInfo.Name}{resolutionInfo}");
        }

        private void OnScreenshotUpdated(object sender, ScreenshotData screenshot)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => OnScreenshotUpdated(sender, screenshot)));
                return;
            }

            UpdateThumbnail(screenshot);
        }

        private void AddPeerToList(PeerInfo peerInfo)
        {
            var item = new ListViewItem(peerInfo.Name ?? "Unknown");
            item.SubItems.Add(peerInfo.EndPoint?.Address?.ToString() ?? "Unknown");
            item.SubItems.Add(peerInfo.MachineName ?? "Unknown");
            item.SubItems.Add(peerInfo.IsOnline ? "Online" : "Offline");
            item.SubItems.Add(peerInfo.Resolution ?? "Unknown");
            item.Tag = peerInfo;

            _peersListView.Items.Add(item);
        }

        private void RemovePeerFromList(PeerInfo peerInfo)
        {
            for (int i = 0; i < _peersListView.Items.Count; i++)
            {
                var item = _peersListView.Items[i];
                if (item.Tag is PeerInfo info && info.Id == peerInfo.Id)
                {
                    _peersListView.Items.RemoveAt(i);
                    break;
                }
            }
        }

        private void CreateThumbnailControl(PeerInfo peerInfo)
        {
            if (_thumbnailControls.ContainsKey(peerInfo.Id))
                return;

            // Debug: Log the screen count being used
            System.Diagnostics.Debug.WriteLine($"Creating thumbnail for peer: {peerInfo.Name}, ScreenCount: {peerInfo.ScreenCount}");
            System.Diagnostics.Debug.WriteLine($"PeerInfo details - Id: {peerInfo.Id}, MachineName: {peerInfo.MachineName}, OSVersion: {peerInfo.OSVersion}");

            // Remove placeholder label if this is the first peer
            if (_thumbnailControls.Count == 0)
            {
                foreach (Control control in _thumbnailsPanel.Controls)
                {
                    if (control is Label && control.Text.Contains("No peers discovered"))
                    {
                        _thumbnailsPanel.Controls.Remove(control);
                        control.Dispose();
                        break;
                    }
                }
            }

                            var thumbnail = new ThumbnailControl(peerInfo.Id, peerInfo.Name, peerInfo.ScreenCount, peerInfo.Resolution);
            thumbnail.ThumbnailClicked += OnThumbnailClicked;
            
            _thumbnailControls[peerInfo.Id] = thumbnail;
            _thumbnailsPanel.Controls.Add(thumbnail);
        }

        private void RemoveThumbnailControl(PeerInfo peerInfo)
        {
            if (_thumbnailControls.TryGetValue(peerInfo.Id, out var thumbnail))
            {
                _thumbnailsPanel.Controls.Remove(thumbnail);
                thumbnail.Dispose();
                _thumbnailControls.Remove(peerInfo.Id);

                // Add placeholder back if no more peers
                if (_thumbnailControls.Count == 0)
                {
                    var placeholderLabel = new Label
                    {
                        Text = "No peers discovered yet.\nPeers will appear here as thumbnails when discovered.",
                        Font = new Font("Segoe UI", 10),
                        ForeColor = Color.Gray,
                        TextAlign = ContentAlignment.MiddleCenter,
                        Dock = DockStyle.Fill,
                        AutoSize = false
                    };
                    _thumbnailsPanel.Controls.Add(placeholderLabel);
                }
            }

            // Also close any open screenshot viewer for this peer
            if (_screenshotViewers.TryGetValue(peerInfo.Id, out var viewer))
            {
                viewer.Close();
                _screenshotViewers.Remove(peerInfo.Id);
            }
        }

        private void OnThumbnailClicked(object sender, ThumbnailControl thumbnail)
        {
            // Find and select the corresponding peer in the list
            for (int i = 0; i < _peersListView.Items.Count; i++)
            {
                var item = _peersListView.Items[i];
                if (item.Tag is PeerInfo info && info.Id == thumbnail.PeerId)
                {
                    _peersListView.Items[i].Selected = true;
                    _peersListView.Items[i].Focused = true;
                    
                    // Open or show screenshot viewer for this peer
                    OpenScreenshotViewer(info);
                    break;
                }
            }
        }

        private void OnPeerSelectionChanged(object sender, EventArgs e)
        {
            var selectedPeer = GetSelectedPeer();
            if (selectedPeer != null)
            {
                UpdateStatus($"Selected peer: {selectedPeer.Name}");
            }
        }

        private void OnPeerDoubleClicked(object sender, EventArgs e)
        {
            var selectedPeer = GetSelectedPeer();
            if (selectedPeer != null)
            {
                UpdateStatus($"Opening screenshot viewer for: {selectedPeer.Name}");
                OpenScreenshotViewer(selectedPeer);
            }
        }

        private PeerInfo GetSelectedPeer()
        {
            if (_peersListView.SelectedItems.Count > 0)
            {
                return _peersListView.SelectedItems[0].Tag as PeerInfo;
            }
            return null;
        }

        private void OpenScreenshotViewer(PeerInfo peerInfo)
        {
            if (_screenshotViewers.TryGetValue(peerInfo.Id, out var existingViewer))
            {
                // Viewer already exists, bring it to front
                if (existingViewer.IsDisposed)
                {
                    _screenshotViewers.Remove(peerInfo.Id);
                }
                else
                {
                    existingViewer.BringToFront();
                    existingViewer.Focus();
                    return;
                }
            }

            // Create new viewer
            var viewer = new ScreenshotViewerForm(peerInfo);
            viewer.FormClosed += (sender, e) => 
            {
                // Remove from dictionary when form is closed
                if (_screenshotViewers.ContainsKey(peerInfo.Id))
                {
                    _screenshotViewers.Remove(peerInfo.Id);
                }
            };

            _screenshotViewers[peerInfo.Id] = viewer;
            viewer.Show();
        }

        private void UpdateThumbnail(ScreenshotData screenshot)
        {
            if (_thumbnailControls.TryGetValue(screenshot.PeerId, out var thumbnail))
            {
                thumbnail.UpdateScreenshot(screenshot);
                UpdateStatus($"Screenshot updated for {screenshot.PeerId} - {screenshot.Timestamp:HH:mm:ss}");
            }

            // Also update the screenshot viewer if it's open
            if (_screenshotViewers.TryGetValue(screenshot.PeerId, out var viewer))
            {
                viewer.UpdateScreenshot(screenshot);
            }
        }

        private async void OnRefreshClicked(object sender, EventArgs e)
        {
            try
            {
                UpdateStatus("Refreshing peer list...");
                _peersListView.Items.Clear();
                
                foreach (var peer in _peerDiscovery.DiscoveredPeers)
                {
                    AddPeerToList(peer);
                }
                
                UpdateStatus($"Peer list refreshed. Found {_peersListView.Items.Count} peers.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error refreshing peer list: {ex.Message}");
            }
        }

        private async Task ConnectToPeerAsync(PeerInfo peerInfo)
        {
            if (_peerConnections.ContainsKey(peerInfo.Id))
                return; // Already connected

            try
            {
                UpdateStatus($"Connecting to {peerInfo.Name}...");
                
                var connection = new PeerConnection(peerInfo);
                connection.ScreenshotReceived += OnScreenshotReceived;
                connection.ErrorOccurred += OnConnectionError;
                connection.Disconnected += OnConnectionDisconnected;

                if (await connection.ConnectAsync())
                {
                    _peerConnections[peerInfo.Id] = connection;
                    UpdateStatus($"Connected to {peerInfo.Name}");
                    
                    // Request initial screenshot
                    await connection.RequestScreenshotAsync();
                }
                else
                {
                    connection.Dispose();
                    UpdateStatus($"Failed to connect to {peerInfo.Name}");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error connecting to {peerInfo.Name}: {ex.Message}");
            }
        }

        private void OnScreenshotReceived(object sender, ScreenshotData screenshot)
        {
            _screenshotService.AddScreenshot(screenshot);
            _lastScreenshotReceivedAt[screenshot.PeerId] = DateTime.Now;
        }

        private void OnConnectionError(object sender, Exception ex)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => OnConnectionError(sender, ex)));
                return;
            }

            UpdateStatus($"Connection error: {ex.Message}");
            // Try to recover by reconnecting
            var connection = sender as PeerConnection;
            if (connection != null)
            {
                var peer = connection.PeerInfo;
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(2000);
                        await ConnectToPeerAsync(peer);
                    }
                    catch { }
                });
            }
        }

        private void OnConnectionDisconnected(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => OnConnectionDisconnected(sender, e)));
                return;
            }

            var connection = sender as PeerConnection;
            if (connection != null)
            {
                var peerId = connection.PeerInfo.Id;
                if (_peerConnections.ContainsKey(peerId))
                {
                    _peerConnections.Remove(peerId);
                }
                // Schedule reconnect
                var peer = connection.PeerInfo;
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(2000);
                        await ConnectToPeerAsync(peer);
                    }
                    catch { }
                });
            }

            UpdateStatus("Peer disconnected");
        }

        private void OnHeartbeatTick(object sender, EventArgs e)
        {
            try
            {
                // Reconnect dropped peers and re-request stale screenshots
                var now = DateTime.Now;
                foreach (var kvp in _peerConnections.ToList())
                {
                    var peerId = kvp.Key;
                    var conn = kvp.Value;
                    if (conn == null)
                        continue;

                    if (!conn.IsConnected)
                    {
                        var peer = conn.PeerInfo;
                        Task.Run(async () =>
                        {
                            try { await ConnectToPeerAsync(peer); } catch { }
                        });
                        continue;
                    }

                    if (_lastScreenshotReceivedAt.TryGetValue(peerId, out var lastTs))
                    {
                        if ((now - lastTs) > TimeSpan.FromSeconds(10))
                        {
                            // No recent screenshot, request one
                            Task.Run(async () =>
                            {
                                try { await conn.RequestScreenshotAsync(); } catch { }
                            });
                        }
                    }
                    else
                    {
                        // Never received one yet, request initial
                        Task.Run(async () =>
                        {
                            try { await conn.RequestScreenshotAsync(); } catch { }
                        });
                    }
                }
            }
            catch { }
        }

        private void OnRefreshRateChanged(object sender, EventArgs e)
        {
            var refreshRate = (int)_refreshRateNumeric.Value;
            UpdateStatus($"Refresh rate set to {refreshRate}ms");
        }

        private void UpdateStatus(string message)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateStatus(message)));
                return;
            }

            _statusLabel.Text = message;
        }

        private void OnDisableScreencaptureClicked(object sender, EventArgs e)
        {
            try
            {
                if (_screenshotCaptureDisabled)
                {
                    // Already disabled, re-enable
                    _screenshotCaptureDisabled = false;
                    _peerDiscovery.ScreenshotCaptureDisabled = false;
                    
                    // Update button appearance
                    var button = sender as Button;
                    if (button != null)
                    {
                        button.Text = "Disable Screencapture";
                        button.BackColor = Color.LightCoral;
                    }
                    
                    UpdateStatus("Screenshot capture re-enabled");
                    return;
                }

                // Show password input dialog
                using (var passwordForm = new Form())
                {
                    passwordForm.Text = "Enter Password";
                    passwordForm.Size = new Size(300, 150);
                    passwordForm.StartPosition = FormStartPosition.CenterParent;
                    passwordForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                    passwordForm.MaximizeBox = false;
                    passwordForm.MinimizeBox = false;

                    var passwordLabel = new Label
                    {
                        Text = "Enter password to disable screenshot capture:",
                        Location = new Point(10, 20),
                        Size = new Size(260, 20)
                    };

                    var passwordTextBox = new TextBox
                    {
                        Location = new Point(10, 50),
                        Size = new Size(260, 20),
                        PasswordChar = '*',
                        UseSystemPasswordChar = true
                    };

                    var okButton = new Button
                    {
                        Text = "OK",
                        Location = new Point(110, 80),
                        Size = new Size(75, 25),
                        DialogResult = DialogResult.OK
                    };

                    var cancelButton = new Button
                    {
                        Text = "Cancel",
                        Location = new Point(195, 80),
                        Size = new Size(75, 25),
                        DialogResult = DialogResult.Cancel
                    };

                    passwordForm.Controls.AddRange(new Control[] { passwordLabel, passwordTextBox, okButton, cancelButton });
                    passwordForm.AcceptButton = okButton;
                    passwordForm.CancelButton = cancelButton;

                    // Focus on password textbox
                    passwordTextBox.Focus();

                    if (passwordForm.ShowDialog(this) == DialogResult.OK)
                    {
                        var enteredPassword = passwordTextBox.Text;
                        if (enteredPassword == "nobodyknow")
                        {
                            _screenshotCaptureDisabled = true;
                            _peerDiscovery.ScreenshotCaptureDisabled = true;
                            
                            // Update button appearance
                            var button = sender as Button;
                            if (button != null)
                            {
                                button.Text = "Enable Screencapture";
                                button.BackColor = Color.LightGreen;
                            }
                            
                            UpdateStatus("Screenshot capture disabled - no screenshots will be sent to peers");
                            MessageBox.Show("Screenshot capture has been disabled.\nNo screenshots will be sent to other peers.", 
                                "Screenshot Capture Disabled", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show("Incorrect password.\nScreenshot capture remains enabled.", 
                                "Access Denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error disabling screenshot capture: {ex.Message}");
            }
        }

        private void OnShowWindowClicked(object sender, EventArgs e)
        {
            CheckPasswordAndShowWindow();
        }

        private void OnTrayIconDoubleClicked(object sender, EventArgs e)
        {
            CheckPasswordAndShowWindow();
        }

        private void CheckPasswordAndShowWindow()
        {
            try
            {
                // Show password input dialog
                using (var passwordForm = new Form())
                {
                    passwordForm.Text = "Enter Password";
                    passwordForm.Size = new Size(300, 160);
                    passwordForm.StartPosition = FormStartPosition.CenterScreen;
                    passwordForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                    passwordForm.MaximizeBox = false;
                    passwordForm.MinimizeBox = false;

                    var passwordLabel = new Label
                    {
                        Text = "Enter password to show Peer Viewer window:",
                        Location = new Point(10, 20),
                        Size = new Size(260, 20)
                    };

                    var passwordTextBox = new TextBox
                    {
                        Location = new Point(10, 50),
                        Size = new Size(260, 20),
                        PasswordChar = '*',
                        UseSystemPasswordChar = true
                    };

                    var okButton = new Button
                    {
                        Text = "OK",
                        Location = new Point(110, 80),
                        Size = new Size(75, 25),
                        DialogResult = DialogResult.OK
                    };

                    var cancelButton = new Button
                    {
                        Text = "Cancel",
                        Location = new Point(195, 80),
                        Size = new Size(75, 25),
                        DialogResult = DialogResult.Cancel
                    };

                    passwordForm.Controls.AddRange(new Control[] { passwordLabel, passwordTextBox, okButton, cancelButton });
                    passwordForm.AcceptButton = okButton;
                    passwordForm.CancelButton = cancelButton;

                    // Focus on password textbox
                    passwordTextBox.Focus();

                    if (passwordForm.ShowDialog() == DialogResult.OK)
                    {
                        var enteredPassword = passwordTextBox.Text;
                        if (enteredPassword == "nobodyknow")
                        {
                            ShowMainWindow();
                        }
                        else
                        {
                            MessageBox.Show("Incorrect password.\nWindow access denied.", 
                                "Access Denied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error showing window: {ex.Message}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowMainWindow()
        {
            this.ShowInTaskbar = true;
            this.WindowState = FormWindowState.Normal;
            this.Show();
            this.BringToFront();
            this.Focus();
        }

        

        private void OnFormResize(object sender, EventArgs e)
        {
            // Hide window to tray when minimized
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.ShowInTaskbar = false;
                // this.Hide();
            }
        }


    }
}
