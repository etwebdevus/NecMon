using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PeerViewer.Models;

namespace PeerViewer.Network
{
    public class PeerConnection : IDisposable
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        public PeerInfo PeerInfo { get; private set; }
        public bool IsConnected => _client?.Connected == true;

        public event EventHandler<ScreenshotData> ScreenshotReceived;
        public event EventHandler<Exception> ErrorOccurred;
        public event EventHandler Disconnected;

        public PeerConnection(PeerInfo peerInfo)
        {
            PeerInfo = peerInfo;
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Attempting to connect to {PeerInfo.Name} at {PeerInfo.EndPoint}");
                
                _client = new TcpClient();
                _client.ReceiveTimeout = 10000; // 10 second timeout
                _client.SendTimeout = 10000;
                
                await _client.ConnectAsync(PeerInfo.EndPoint.Address, PeerInfo.EndPoint.Port);
                _stream = _client.GetStream();
                
                System.Diagnostics.Debug.WriteLine($"Successfully connected to {PeerInfo.Name}");
                
                // Start listening for data
                _ = Task.Run(ListenForDataAsync);
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to connect to {PeerInfo.Name}: {ex.Message}");
                OnErrorOccurred(ex);
                return false;
            }
        }

        public async Task<bool> SendMessageAsync(string message)
        {
            if (!IsConnected) return false;

            try
            {
                var data = System.Text.Encoding.UTF8.GetBytes(message);
                await _stream.WriteAsync(data, 0, data.Length);
                return true;
            }
            catch (Exception ex)
            {
                OnErrorOccurred(ex);
                return false;
            }
        }

        public async Task<bool> RequestScreenshotAsync()
        {
            return await SendMessageAsync("SCREENSHOT_REQUEST");
        }

        public async Task<bool> StopScreenshotStreamAsync()
        {
            return await SendMessageAsync("STOP_STREAMING");
        }



        private async Task ListenForDataAsync()
        {
            var buffer = new byte[8192];
            
            try
            {
                while (IsConnected && !_disposed)
                {
                    var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    // Process received data
                    await ProcessReceivedDataAsync(buffer, bytesRead);
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred(ex);
            }
            finally
            {
                OnDisconnected();
            }
        }

        private async Task ProcessReceivedDataAsync(byte[] buffer, int bytesRead)
        {
            try
            {
                if (bytesRead <= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠ Empty data received from {PeerInfo.Name}");
                    return;
                }
                
                var message = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                System.Diagnostics.Debug.WriteLine($"📨 Received message from {PeerInfo.Name}: {message.Substring(0, Math.Min(50, message.Length))}...");
                
                if (message.StartsWith("SCREENSHOT:"))
                {
                    System.Diagnostics.Debug.WriteLine($"📸 Processing screenshot request from {PeerInfo.Name}");
                    // Handle screenshot data
                    var screenshotData = await ReceiveScreenshotAsync();
                    if (screenshotData != null)
                    {
                        OnScreenshotReceived(screenshotData);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠ Failed to receive screenshot from {PeerInfo.Name}");
                    }
                }

                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠ Unknown message format from {PeerInfo.Name}: {message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Error processing data from {PeerInfo.Name}: {ex.Message}");
                OnErrorOccurred(ex);
            }
        }



        private async Task<ScreenshotData> ReceiveScreenshotAsync()
        {
            try
            {
                // Read screenshot size with timeout
                var sizeBuffer = new byte[8];
                var sizeBytesRead = await _stream.ReadAsync(sizeBuffer, 0, 8);
                
                if (sizeBytesRead != 8)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠ Incomplete size data: expected 8 bytes, got {sizeBytesRead}");
                    return null;
                }
                
                var size = BitConverter.ToInt64(sizeBuffer, 0);
                
                // Validate size to prevent overflow
                if (size <= 0 || size > 100 * 1024 * 1024) // Max 100MB
                {
                    System.Diagnostics.Debug.WriteLine($"⚠ Invalid screenshot size: {size} bytes (max allowed: 100MB)");
                    return null;
                }
                
                // Check if size can fit in int (for buffer allocation)
                if (size > int.MaxValue)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠ Screenshot size too large for buffer allocation: {size} bytes");
                    return null;
                }
                
                System.Diagnostics.Debug.WriteLine($"📸 Receiving screenshot: {size} bytes from {PeerInfo.Name}");

                // Read screenshot data with proper buffer management and timeout
                var screenshotBuffer = new byte[size];
                var totalRead = 0;
                var remainingBytes = (int)size;
                var startTime = DateTime.Now;
                var timeout = TimeSpan.FromSeconds(30); // 30 second timeout
                
                while (totalRead < size && remainingBytes > 0)
                {
                    // Check timeout
                    if (DateTime.Now - startTime > timeout)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠ Timeout while receiving screenshot from {PeerInfo.Name}");
                        return null;
                    }
                    
                    var bytesToRead = Math.Min(remainingBytes, 8192); // Read in 8KB chunks
                    var read = await _stream.ReadAsync(screenshotBuffer, totalRead, bytesToRead);
                    
                    if (read == 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠ Connection closed while receiving screenshot data");
                        break;
                    }
                    
                    totalRead += read;
                    remainingBytes -= read;
                    
                    System.Diagnostics.Debug.WriteLine($"📥 Read {read} bytes, total: {totalRead}/{size}");
                }

                if (totalRead == size)
                {
                    System.Diagnostics.Debug.WriteLine($"✓ Screenshot data received completely: {totalRead} bytes");
                    
                    using (var ms = new MemoryStream(screenshotBuffer))
                    {
                        var image = Image.FromStream(ms);
                        var screenshotData = new ScreenshotData
                        {
                            PeerId = PeerInfo.Id,
                            Screenshot = new Bitmap(image),
                            Width = image.Width,
                            Height = image.Height,
                            Timestamp = DateTime.Now
                        };
                        
                        System.Diagnostics.Debug.WriteLine($"✓ Screenshot processed: {image.Width}x{image.Height}");
                        return screenshotData;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠ Incomplete screenshot data: expected {size}, got {totalRead}");
                }
            }
            catch (OverflowException ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ OVERFLOW ERROR in ReceiveScreenshotAsync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   This usually indicates corrupted size data or extremely large screenshots");
                System.Diagnostics.Debug.WriteLine($"   Peer: {PeerInfo.Name}, EndPoint: {PeerInfo.EndPoint}");
                OnErrorOccurred(ex);
            }
            catch (OutOfMemoryException ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ OUT OF MEMORY ERROR in ReceiveScreenshotAsync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   Screenshot size may be too large for available memory");
                System.Diagnostics.Debug.WriteLine($"   Peer: {PeerInfo.Name}, EndPoint: {PeerInfo.EndPoint}");
                OnErrorOccurred(ex);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ERROR in ReceiveScreenshotAsync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"   Peer: {PeerInfo.Name}, EndPoint: {PeerInfo.EndPoint}");
                System.Diagnostics.Debug.WriteLine($"   Stack trace: {ex.StackTrace}");
                OnErrorOccurred(ex);
            }

            return null;
        }

        protected virtual void OnScreenshotReceived(ScreenshotData screenshot)
        {
            ScreenshotReceived?.Invoke(this, screenshot);
        }

        protected virtual void OnErrorOccurred(Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }

        protected virtual void OnDisconnected()
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }



        public void Disconnect()
        {
            lock (_lockObject)
            {
                if (_disposed) return;
                
                _stream?.Close();
                _client?.Close();
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Disconnect();
            _stream?.Dispose();
            _client?.Dispose();
        }
    }
}

