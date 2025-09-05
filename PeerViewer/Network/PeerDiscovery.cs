using PeerViewer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace PeerViewer.Network
{
    public class PeerDiscovery : IDisposable
    {
        private readonly List<PeerInfo> _discoveredPeers = new List<PeerInfo>();
        private readonly object _peersLock = new object();
        private UdpClient _discoveryClient;
        private TcpListener _listener;
        private bool _isRunning = false;
        private readonly int _discoveryPort = 8888;
        private readonly int _servicePort = 8889;
        public string LocalUserName { get; set; } = Environment.UserName;

        public event EventHandler<PeerInfo> PeerDiscovered;
        public event EventHandler<PeerInfo> PeerLost;

        public IReadOnlyList<PeerInfo> DiscoveredPeers
        {
            get
            {
                lock (_peersLock)
                {
                    return _discoveredPeers.AsReadOnly();
                }
            }
        }

        public bool ScreenshotCaptureDisabled { get; set; } = false;

        public async Task StartAsync()
        {
            if (_isRunning) return;

            try
            {
                System.Diagnostics.Debug.WriteLine("Starting PeerDiscovery...");

                // Start discovery listener
                _discoveryClient = new UdpClient(_discoveryPort);
                System.Diagnostics.Debug.WriteLine($"UDP Discovery listener started on port {_discoveryPort}");
                _ = Task.Run(DiscoveryListenerAsync);

                // Start service listener
                _listener = new TcpListener(IPAddress.Any, _servicePort);
                _listener.Start();
                System.Diagnostics.Debug.WriteLine($"TCP Service listener started on port {_servicePort}");
                _ = Task.Run(ServiceListenerAsync);

                // Start network scanning
                _ = Task.Run(ScanNetworkAsync);

                _isRunning = true;
                System.Diagnostics.Debug.WriteLine("PeerDiscovery started successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to start PeerDiscovery: {ex.Message}");
                throw new InvalidOperationException("Failed to start peer discovery", ex);
            }
        }

        public void Stop()
        {
            _isRunning = false;

            // Notify peers on the LAN that this app is exiting so they can remove us immediately
            try
            {
                BroadcastExitNotification();
            }
            catch (Exception)
            {
                // Best-effort; ignore errors during shutdown broadcast
            }

            _discoveryClient?.Close();
            _listener?.Stop();

            _discoveryClient?.Dispose();
            _listener = null;
        }

        private async Task DiscoveryListenerAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Discovery listener started, waiting for messages...");
                System.Diagnostics.Debug.WriteLine($"Listening on port {_discoveryPort} for UDP discovery messages");

                while (_isRunning)
                {
                    var result = await _discoveryClient.ReceiveAsync();
                    var message = System.Text.Encoding.UTF8.GetString(result.Buffer);

                    System.Diagnostics.Debug.WriteLine($"📨 RECEIVED: {result.Buffer.Length} bytes from {result.RemoteEndPoint}");
                    System.Diagnostics.Debug.WriteLine($"   Message: {message}");
                    System.Diagnostics.Debug.WriteLine($"   Timestamp: {DateTime.Now:HH:mm:ss.fff}");

                    if (message.StartsWith("PEER_DISCOVERY:"))
                    {
                        System.Diagnostics.Debug.WriteLine($"   ✓ Valid PEER_DISCOVERY message - processing...");
                        ProcessDiscoveryMessage(message, result.RemoteEndPoint);
                    }
                    else if (message.StartsWith("PEER_EXIT:"))
                    {
                        System.Diagnostics.Debug.WriteLine($"   ✓ PEER_EXIT message - removing peer...");
                        var parts = message.Split(':');
                        if (parts.Length >= 2)
                        {
                            var peerId = parts[1];
                            RemovePeerById(peerId);
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"   ⚠ Unknown message format - ignoring");
                        System.Diagnostics.Debug.WriteLine($"   First 50 chars: {message.Substring(0, Math.Min(50, message.Length))}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Discovery listener error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   Stack trace: {ex.StackTrace}");
                // Discovery listener stopped
            }
        }

        private async Task ServiceListenerAsync()
        {
            try
            {
                while (_isRunning)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleServiceConnectionAsync(client));
                }
            }
            catch (Exception)
            {
                // Service listener stopped
            }
        }

        private async Task HandleServiceConnectionAsync(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var buffer = new byte[1024];
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

                    if (bytesRead > 0)
                    {
                        var message = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        if (message == "SCREENSHOT_REQUEST")
                        {
                            // Start continuous screenshot streaming
                            await StartContinuousScreenshotStreamAsync(stream);
                        }
                        else if (message == "STOP_STREAMING")
                        {
                            // Stop streaming (handled by cancellation token)
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Connection handling error
            }
        }

        private async Task StartContinuousScreenshotStreamAsync(NetworkStream stream)
        {
            try
            {
                while (true)
                {
                    // Take screenshot
                    var screenshot = TakeScreenshot();
                    if (screenshot != null)
                    {
                        using (var ms = new System.IO.MemoryStream())
                        {
                            screenshot.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            var imageData = ms.ToArray();

                            // Send screenshot header
                            var header = "SCREENSHOT:";
                            var headerData = System.Text.Encoding.UTF8.GetBytes(header);
                            await stream.WriteAsync(headerData, 0, headerData.Length);

                            // Send screenshot size
                            var sizeData = BitConverter.GetBytes((long)imageData.Length);
                            await stream.WriteAsync(sizeData, 0, sizeData.Length);

                            // Send screenshot data
                            await stream.WriteAsync(imageData, 0, imageData.Length);
                            await stream.FlushAsync();
                            ms.Close();
                        }
                    }

                    // Wait before next screenshot (default 1 second, can be adjusted)
                    await Task.Delay(1000);
                }
            }
            catch (Exception)
            {
                // Stream ended or error occurred
            }
        }

        private System.Drawing.Image TakeScreenshot()
        {
            // Check if screenshot capture is disabled
            if (ScreenshotCaptureDisabled)
            {
                System.Diagnostics.Debug.WriteLine("Screenshot capture is disabled - returning null");
                return null;
            }

            try
            {
                // Get all screens
                var screens = System.Windows.Forms.Screen.AllScreens;

                if (screens.Length == 1)
                {
                    // Single monitor - capture with DPI awareness
                    var screen = screens[0];
                    var bounds = screen.Bounds;

                    // Get DPI scaling for this specific screen
                    var (dpiX, dpiY) = GetDpiForScreen(screen);

                    System.Diagnostics.Debug.WriteLine($"Single screen capture: {screen.DeviceName}");
                    System.Diagnostics.Debug.WriteLine($"  Bounds: {bounds.Width}x{bounds.Height} at ({bounds.X},{bounds.Y})");
                    System.Diagnostics.Debug.WriteLine($"  DPI: {dpiX}x{dpiY} (scaling: {dpiX / 96.0f:F2}x, {dpiY / 96.0f:F2}x)");

                    // Calculate actual pixel dimensions based on DPI
                    var actualWidth = (int)(bounds.Width * dpiX / 96.0f);
                    var actualHeight = (int)(bounds.Height * dpiY / 96.0f);

                    System.Diagnostics.Debug.WriteLine($"  Actual capture size: {actualWidth}x{actualHeight}");

                    // Create bitmap with actual pixel dimensions
                    var screenshot = new System.Drawing.Bitmap(actualWidth, actualHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    using (var g = System.Drawing.Graphics.FromImage(screenshot))
                    {
                        // Get desktop window handle
                        var desktopWnd = GetDesktopWindow();
                        var desktopDC = GetWindowDC(desktopWnd);
                        var memDC = CreateCompatibleDC(desktopDC);

                        var hBitmap = CreateCompatibleBitmap(desktopDC, actualWidth, actualHeight);
                        var oldBitmap = SelectObject(memDC, hBitmap);

                        // Copy the screen content with DPI-aware dimensions
                        BitBlt(memDC, 0, 0, actualWidth, actualHeight, desktopDC, bounds.X, bounds.Y, SRCCOPY);

                        using (var finalImg = System.Drawing.Image.FromHbitmap(hBitmap))
                        {
                            g.DrawImage(finalImg, 0, 0);
                        }

                        // Clean up resources
                        SelectObject(memDC, oldBitmap);
                        DeleteObject(hBitmap);
                        DeleteDC(memDC);
                        ReleaseDC(desktopWnd, desktopDC);
                    }

                    return screenshot;
                }
                else
                {
                    // Multiple monitors - iterate through each screen and merge with DPI awareness
                    System.Diagnostics.Debug.WriteLine($"Multi-screen capture: {screens.Length} screens");

                    // Calculate the total bounds that encompass all screens
                    var minX = screens.Min(s => s.Bounds.X);
                    var minY = screens.Min(s => s.Bounds.Y);
                    var maxX = screens.Max(s => s.Bounds.X + s.Bounds.Width);
                    var maxY = screens.Max(s => s.Bounds.Y + s.Bounds.Height);

                    var totalWidth = maxX - minX;
                    var totalHeight = maxY - minY;

                    // Calculate actual pixel dimensions for the combined image using max DPI
                    var actualTotalWidth = totalWidth;
                    var actualTotalHeight = totalHeight;

                    System.Diagnostics.Debug.WriteLine($"Actual combined size: {actualTotalWidth}x{actualTotalHeight}");

                    // Create combined bitmap with actual pixel dimensions
                    var combinedScreenshot = new System.Drawing.Bitmap(actualTotalWidth, actualTotalHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    using (var g = System.Drawing.Graphics.FromImage(combinedScreenshot))
                    {
                        // Set background to black for areas not covered by screens
                        g.Clear(System.Drawing.Color.Black);

                        // Get desktop window handle once for all screens
                        var desktopWnd = GetDesktopWindow();
                        var desktopDC = GetWindowDC(desktopWnd);

                        try
                        {
                            // Iterate through each screen
                            foreach (var screen in screens)
                            {
                                var bounds = screen.Bounds;

                                // Get DPI for this specific screen
                                var (screenDpiX, screenDpiY) = GetDpiForScreen(screen);

                                System.Diagnostics.Debug.WriteLine($"Processing screen: {screen.DeviceName}");
                                System.Diagnostics.Debug.WriteLine($"  Bounds: {bounds.Width}x{bounds.Height} at ({bounds.X},{bounds.Y})");
                                System.Diagnostics.Debug.WriteLine($"  Screen DPI: {screenDpiX}x{screenDpiY} (scaling: {screenDpiX / 96.0f:F2}x, {screenDpiY / 96.0f:F2}x)");

                                // Calculate actual pixel dimensions for this screen
                                var actualScreenWidth = bounds.Width;
                                var actualScreenHeight = bounds.Height;

                                System.Diagnostics.Debug.WriteLine($"  Actual screen size: {actualScreenWidth}x{actualScreenHeight}");

                                // Create memory DC for this screen
                                var memDC = CreateCompatibleDC(desktopDC);
                                var hBitmap = CreateCompatibleBitmap(desktopDC, actualScreenWidth, actualScreenHeight);
                                var oldBitmap = SelectObject(memDC, hBitmap);

                                try
                                {
                                    // Copy this screen's content with DPI-aware dimensions
                                    BitBlt(memDC, 0, 0, actualScreenWidth, actualScreenHeight, desktopDC, bounds.X, bounds.Y, SRCCOPY);

                                    using (var screenImg = System.Drawing.Image.FromHbitmap(hBitmap))
                                    {
                                        // Calculate position relative to the combined bitmap (scaled by max DPI)
                                        var relativeX = (int)((bounds.X - minX) * 96.0f / screenDpiX);
                                        var relativeY = (int)((bounds.Y - minY) * 96.0f / screenDpiY);

                                        System.Diagnostics.Debug.WriteLine($"  Position in combined: ({relativeX},{relativeY})");

                                        // Draw the screen at its correct position
                                        g.DrawImage(screenImg, relativeX, relativeY);
                                    }
                                }
                                finally
                                {
                                    // Clean up resources for this screen
                                    SelectObject(memDC, oldBitmap);
                                    DeleteObject(hBitmap);
                                    DeleteDC(memDC);
                                }
                            }
                        }
                        finally
                        {
                            // Clean up desktop DC
                            ReleaseDC(desktopWnd, desktopDC);
                        }
                    }

                    return combinedScreenshot;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Screenshot error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        // Win32 API declarations for screenshot capture
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr GetDesktopWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr GetWindowDC(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest,
            int wDest, int hDest, IntPtr hdcSource,
            int xSrc, int ySrc, int rop);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr hObject);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        static extern bool DeleteDC(IntPtr hdc);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        // DPI-aware API declarations
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr MonitorFromPoint([System.Runtime.InteropServices.In] System.Drawing.Point pt, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        const int MDT_EFFECTIVE_DPI = 0;

        const int SRCCOPY = 0x00CC0020;

        /// <summary>
        /// Gets the DPI for a specific screen
        /// </summary>
        private (uint dpiX, uint dpiY) GetDpiForScreen(System.Windows.Forms.Screen screen)
        {
            try
            {
                // Get the monitor handle for this screen
                var centerPoint = new System.Drawing.Point(
                    screen.Bounds.X + screen.Bounds.Width / 2,
                    screen.Bounds.Y + screen.Bounds.Height / 2);

                var monitorHandle = MonitorFromPoint(centerPoint, MONITOR_DEFAULTTONEAREST);

                if (monitorHandle != IntPtr.Zero)
                {
                    uint dpiX, dpiY;
                    var result = GetDpiForMonitor(monitorHandle, MDT_EFFECTIVE_DPI, out dpiX, out dpiY);

                    if (result == 0) // S_OK
                    {
                        System.Diagnostics.Debug.WriteLine($"Screen {screen.DeviceName} DPI: {dpiX}x{dpiY}");
                        return (dpiX, dpiY);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to get DPI for screen {screen.DeviceName}, result: {result}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to get monitor handle for screen {screen.DeviceName}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting DPI for screen {screen.DeviceName}: {ex.Message}");
            }

            // Fallback to primary monitor DPI
            var fallbackDpi = GetDpiForWindow(GetDesktopWindow());
            System.Diagnostics.Debug.WriteLine($"Using fallback DPI for screen {screen.DeviceName}: {fallbackDpi}");
            return (fallbackDpi, fallbackDpi);
        }

        /// <summary>
        /// Gets the maximum resolution across all screens
        /// </summary>
        private (int width, int height) GetMaxScreenResolution()
        {
            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;

                if (screens.Length == 1)
                {
                    var screen = screens[0];
                    var bounds = screen.Bounds;
                    System.Diagnostics.Debug.WriteLine($"Single screen max resolution: {bounds.Width}x{bounds.Height}");
                    return (bounds.Width, bounds.Height);
                }
                else
                {
                    // For multiple screens, calculate the total bounds that encompass all screens
                    var minX = screens.Min(s => s.Bounds.X);
                    var minY = screens.Min(s => s.Bounds.Y);
                    var maxX = screens.Max(s => s.Bounds.X + s.Bounds.Width);
                    var maxY = screens.Max(s => s.Bounds.Y + s.Bounds.Height);

                    var totalWidth = maxX - minX;
                    var totalHeight = maxY - minY;

                    System.Diagnostics.Debug.WriteLine($"Multi-screen max resolution: {totalWidth}x{totalHeight}");
                    return (totalWidth, totalHeight);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting max screen resolution: {ex.Message}");
                return (1920, 1080); // Fallback to common resolution
            }
        }



        private void ProcessDiscoveryMessage(string message, IPEndPoint remoteEndPoint)
        {
            try
            {
                var parts = message.Split(':');
                System.Diagnostics.Debug.WriteLine($"Processing discovery message: {message}");
                System.Diagnostics.Debug.WriteLine($"Message parts count: {parts.Length}");

                if (parts.Length >= 3)
                {
                    var peerId = parts[1];
                    var peerName = parts[2]; // user name
                    var machineName = remoteEndPoint.Address.ToString();
                    var osVersion = parts.Length > 3 ? parts[3] : "Unknown";

                    // Parse screen count - ensure we get the correct value
                    int screenCount = 1; // Default value
                    if (parts.Length > 4 && !string.IsNullOrEmpty(parts[4]))
                    {
                        System.Diagnostics.Debug.WriteLine($"Screen count part: '{parts[4]}'");
                        if (int.TryParse(parts[4], out int parsedScreenCount))
                        {
                            screenCount = parsedScreenCount;
                            System.Diagnostics.Debug.WriteLine($"Successfully parsed screen count: {screenCount}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to parse screen count from: '{parts[4]}'");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"No screen count part found, using default: {screenCount}");
                    }

                    // Parse resolution information
                    string resolution = "Unknown";
                    if (parts.Length > 5 && !string.IsNullOrEmpty(parts[5]))
                    {
                        resolution = parts[5];
                        System.Diagnostics.Debug.WriteLine($"Resolution part: '{resolution}'");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"No resolution part found, using default: {resolution}");
                    }

                    var peerInfo = new PeerInfo
                    {
                        Id = peerId,
                        Name = peerName,
                        EndPoint = new IPEndPoint(remoteEndPoint.Address, _servicePort),
                        MachineName = machineName,
                        OSVersion = osVersion,
                        ScreenCount = screenCount,
                        Resolution = resolution,
                        LastSeen = DateTime.Now
                    };

                    System.Diagnostics.Debug.WriteLine($"Created PeerInfo with ScreenCount: {peerInfo.ScreenCount}, Resolution: {resolution}");
                    AddOrUpdatePeer(peerInfo);
                }
            }
            catch (Exception ex)
            {
                // Log the error for debugging
                System.Diagnostics.Debug.WriteLine($"Error processing discovery message: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Message: {message}");
            }
        }

        private void AddOrUpdatePeer(PeerInfo peerInfo)
        {
            lock (_peersLock)
            {
                var existingPeer = _discoveredPeers.Find(p => p.Id == peerInfo.Id);
                if (existingPeer != null)
                {
                    // Debug: Log before update
                    System.Diagnostics.Debug.WriteLine($"Before update - Existing peer: {existingPeer.Name}, ScreenCount: {existingPeer.ScreenCount}");
                    System.Diagnostics.Debug.WriteLine($"Updating with - New peer: {peerInfo.Name}, ScreenCount: {peerInfo.ScreenCount}");

                    // Update existing peer with new information
                    existingPeer.LastSeen = peerInfo.LastSeen;
                    existingPeer.IsOnline = true;
                    existingPeer.ScreenCount = peerInfo.ScreenCount; // Update screen count
                    existingPeer.MachineName = peerInfo.MachineName; // Update machine name
                    existingPeer.OSVersion = peerInfo.OSVersion; // Update OS version
                    existingPeer.Resolution = peerInfo.Resolution; // Update resolution

                    // Debug: Log after update
                    System.Diagnostics.Debug.WriteLine($"After update - Existing peer: {existingPeer.Name}, ScreenCount: {existingPeer.ScreenCount}, Resolution: {existingPeer.Resolution}");
                }
                else
                {
                    _discoveredPeers.Add(peerInfo);
                    // Debug: Log new peer discovery
                    System.Diagnostics.Debug.WriteLine($"New peer discovered: {peerInfo.Name}, ScreenCount: {peerInfo.ScreenCount}");
                    PeerDiscovered?.Invoke(this, peerInfo);
                }
            }
        }

        private async Task ScanNetworkAsync()
        {
            while (_isRunning)
            {
                try
                {
                    // Send discovery broadcast
                    await SendDiscoveryBroadcastAsync();

                    // Clean up offline peers
                    CleanupOfflinePeers();

                    await Task.Delay(30000); // Scan every 30 seconds
                }
                catch (Exception)
                {
                    // Network scanning error
                }
            }
        }

        public async Task SendDiscoveryBroadcastAsync(string preferredInterface = null)
        {
            try
            {
                var screenCount = System.Windows.Forms.Screen.AllScreens.Length;
                var (maxWidth, maxHeight) = GetMaxScreenResolution();
                var message = $"PEER_DISCOVERY:{Environment.MachineName}:{LocalUserName}:{Environment.OSVersion}:{screenCount}:{maxWidth}x{maxHeight}";
                var data = System.Text.Encoding.UTF8.GetBytes(message);

                System.Diagnostics.Debug.WriteLine($"Broadcasting discovery message: {message}");
                System.Diagnostics.Debug.WriteLine($"Screen count detected: {screenCount}");
                System.Diagnostics.Debug.WriteLine($"Max resolution: {maxWidth}x{maxHeight}");

                // Filter network interfaces to prioritize local Ethernet and exclude VMware
                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                 ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                 !ni.Name.ToLower().Contains("vmware") &&
                                 !ni.Name.ToLower().Contains("virtual") &&
                                 !ni.Name.ToLower().Contains("vpn") &&
                                 !ni.Name.ToLower().Contains("tunnel"))
                    .OrderBy(ni => GetInterfacePriority(ni)) // Prioritize Ethernet over WiFi
                    .ToList();

                // If a specific interface is preferred, prioritize it
                if (!string.IsNullOrEmpty(preferredInterface))
                {
                    var preferred = networkInterfaces.FirstOrDefault(ni =>
                        ni.Name.Equals(preferredInterface, StringComparison.OrdinalIgnoreCase));
                    if (preferred != null)
                    {
                        networkInterfaces = new List<NetworkInterface> { preferred };
                        System.Diagnostics.Debug.WriteLine($"Using preferred interface: {preferred.Name}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Found {networkInterfaces.Count} active network interfaces (VMware filtered out)");

                foreach (var networkInterface in networkInterfaces)
                {
                    try
                    {
                        var properties = networkInterface.GetIPProperties();
                        var unicastAddresses = properties.UnicastAddresses
                            .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                                       IsPrivateNetwork(ip.Address)) // Focus on local network
                            .ToList();

                        System.Diagnostics.Debug.WriteLine($"Interface {networkInterface.Name} has {unicastAddresses.Count} private IP addresses");

                        foreach (var unicastAddress in unicastAddresses)
                        {
                            var localIP = unicastAddress.Address;
                            var subnetMask = unicastAddress.IPv4Mask;

                            System.Diagnostics.Debug.WriteLine($"  IP: {localIP}, Mask: {subnetMask}, IsPrivate: {IsPrivateNetwork(localIP)}");

                            if (subnetMask != null)
                            {
                                var broadcastIP = GetBroadcastAddress(localIP, subnetMask);
                                var broadcastEndPoint = new IPEndPoint(broadcastIP, _discoveryPort);

                                System.Diagnostics.Debug.WriteLine($"  Broadcasting on interface {networkInterface.Name} ({localIP}) to {broadcastIP}");

                                using (var client = new UdpClient())
                                {
                                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                                    await client.SendAsync(data, data.Length, broadcastEndPoint);
                                    System.Diagnostics.Debug.WriteLine($"  ✓ Broadcast sent successfully to {broadcastIP}");
                                }
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"  ⚠ No subnet mask for IP {localIP}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error broadcasting on interface {networkInterface.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Broadcast error: {ex.Message}");
            }
        }

        private int GetInterfacePriority(NetworkInterface ni)
        {
            var name = ni.Name.ToLower();

            // Ethernet has highest priority
            if (name.Contains("ethernet") || name.Contains("lan") || ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                return 1;

            // WiFi has medium priority
            if (name.Contains("wi-fi") || name.Contains("wireless") || ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                return 2;

            // Other interfaces have lower priority
            return 3;
        }

        private bool IsPrivateNetwork(IPAddress address)
        {
            var bytes = address.GetAddressBytes();

            // Specifically target 172.20.0.0 network
            if (bytes[0] == 172 && bytes[1] == 20) return true; // 172.20.0.0/16

            return false;
        }

        private IPAddress GetBroadcastAddress(IPAddress address, IPAddress subnetMask)
        {
            var ipAdressBytes = address.GetAddressBytes();
            var subnetMaskBytes = subnetMask.GetAddressBytes();

            var broadcastAddress = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                broadcastAddress[i] = (byte)(ipAdressBytes[i] | (subnetMaskBytes[i] ^ 255));
            }

            return new IPAddress(broadcastAddress);
        }

        private void CleanupOfflinePeers()
        {
            lock (_peersLock)
            {
                var now = DateTime.Now;
                var offlinePeers = new List<PeerInfo>();

                foreach (var peer in _discoveredPeers)
                {
                    if (now - peer.LastSeen > TimeSpan.FromMinutes(2))
                    {
                        peer.IsOnline = false;
                        offlinePeers.Add(peer);
                    }
                }

                foreach (var peer in offlinePeers)
                {
                    _discoveredPeers.Remove(peer);
                    PeerLost?.Invoke(this, peer);
                }
            }
        }

        private void RemovePeerById(string peerId)
        {
            lock (_peersLock)
            {
                var peer = _discoveredPeers.FirstOrDefault(p => p.Id == peerId);
                if (peer != null)
                {
                    _discoveredPeers.Remove(peer);
                    peer.IsOnline = false;
                    System.Diagnostics.Debug.WriteLine($"Peer removed due to exit: {peer.Name} ({peer.Id})");
                    PeerLost?.Invoke(this, peer);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Peer exit received, but no matching peer found for Id: {peerId}");
                }
            }
        }

        private void BroadcastExitNotification()
        {
            try
            {
                var message = $"PEER_EXIT:{Environment.MachineName}";
                var data = System.Text.Encoding.UTF8.GetBytes(message);

                using (var client = new UdpClient())
                {
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    var broadcastEndPoint = new IPEndPoint(IPAddress.Broadcast, _discoveryPort);
                    client.Send(data, data.Length, broadcastEndPoint);
                    System.Diagnostics.Debug.WriteLine($"Broadcasted exit notification: {message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to broadcast exit notification: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
            _discoveryClient?.Dispose();
        }


    }
}
