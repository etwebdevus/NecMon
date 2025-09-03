using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Media;
using System.IO;
using System.Threading.Tasks;
using PeerViewer.Models;
using PeerViewer.Network;

namespace PeerViewer
{
    public partial class ScreenshotViewerForm : Form
    {
        private PictureBox _screenshotPictureBox;
        private ScreenshotData _currentScreenshot;
        private PeerInfo _peerInfo;
        private Button _audioToggleButton;
        private Button _micToggleButton;
        private TrackBar _volumeTrackBar;
        private Label _volumeLabel;
        private Label _audioStatusLabel;
        private bool _audioEnabled = false;
        private bool _micEnabled = false;
        private PeerConnection _audioConnection;
        private MemoryStream _audioBuffer;
        private SoundPlayer _soundPlayer;

        public ScreenshotViewerForm(PeerInfo peerInfo)
        {
            _peerInfo = peerInfo;
            _audioBuffer = new MemoryStream();
            _soundPlayer = new SoundPlayer();
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = $"Screenshot Viewer - {_peerInfo.Name}";
            this.Size = new Size(1200, 850);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(800, 600);
            
            // Set application icon
            try
            {
                this.Icon = new Icon("Femfoyou-Angry-Birds-Angry-bird.512.ico");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load icon: {ex.Message}");
            }

            // Main container
            var mainContainer = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };

            // Screenshot panel (top)
            var screenshotPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 700,
                Padding = new Padding(5)
            };

            // Screenshot picture box
            _screenshotPictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle
            };

            screenshotPanel.Controls.Add(_screenshotPictureBox);

            // Audio controls panel (bottom)
            var audioPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 100,
                BackColor = Color.LightGray,
                Padding = new Padding(10)
            };

            // Audio toggle button
            _audioToggleButton = new Button
            {
                Text = "🔊 Enable Audio",
                Size = new Size(120, 30),
                Location = new Point(10, 10),
                BackColor = Color.LightGreen
            };
            _audioToggleButton.Click += OnAudioToggleClicked;

            // Mic toggle button
            _micToggleButton = new Button
            {
                Text = "🎤 Enable Mic",
                Size = new Size(120, 30),
                Location = new Point(140, 10),
                BackColor = Color.LightBlue
            };
            _micToggleButton.Click += OnMicToggleClicked;

            // Volume label
            _volumeLabel = new Label
            {
                Text = "Volume:",
                Size = new Size(60, 20),
                Location = new Point(270, 15),
                TextAlign = ContentAlignment.MiddleLeft
            };

            // Volume trackbar
            _volumeTrackBar = new TrackBar
            {
                Size = new Size(150, 30),
                Location = new Point(330, 10),
                Minimum = 0,
                Maximum = 100,
                Value = 50,
                TickFrequency = 25
            };
            _volumeTrackBar.ValueChanged += OnVolumeChanged;

            // Audio status label
            _audioStatusLabel = new Label
            {
                Text = "Audio: Disabled",
                Size = new Size(200, 20),
                Location = new Point(10, 50),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DarkRed
            };

            audioPanel.Controls.AddRange(new Control[] { 
                _audioToggleButton, _micToggleButton, _volumeLabel, 
                _volumeTrackBar, _audioStatusLabel 
            });

            mainContainer.Controls.AddRange(new Control[] { screenshotPanel, audioPanel });
            this.Controls.Add(mainContainer);
        }

        private async void OnAudioToggleClicked(object sender, EventArgs e)
        {
            if (!_audioEnabled)
            {
                await EnableAudioAsync();
            }
            else
            {
                DisableAudio();
            }
        }

        private async void OnMicToggleClicked(object sender, EventArgs e)
        {
            if (!_micEnabled)
            {
                await EnableMicAsync();
            }
            else
            {
                DisableMic();
            }
        }

        private void OnVolumeChanged(object sender, EventArgs e)
        {
            if (_soundPlayer != null)
            {
                // Note: SoundPlayer doesn't support volume control directly
                // This would need to be implemented with NAudio or similar library
                _volumeLabel.Text = $"Volume: {_volumeTrackBar.Value}%";
            }
        }

        private async Task EnableAudioAsync()
        {
            try
            {
                _audioConnection = new PeerConnection(_peerInfo);
                _audioConnection.AudioDataReceived += OnAudioDataReceived;
                _audioConnection.ErrorOccurred += OnAudioError;

                if (await _audioConnection.ConnectAsync())
                {
                    await _audioConnection.RequestAudioStreamAsync();
                    _audioEnabled = true;
                    _audioToggleButton.Text = "🔊 Disable Audio";
                    _audioToggleButton.BackColor = Color.LightCoral;
                    _audioStatusLabel.Text = "Audio: Enabled";
                    _audioStatusLabel.ForeColor = Color.DarkGreen;
                }
                else
                {
                    MessageBox.Show("Failed to connect for audio streaming", "Audio Error", 
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error enabling audio: {ex.Message}", "Audio Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DisableAudio()
        {
            _audioEnabled = false;
            _audioToggleButton.Text = "🔊 Enable Audio";
            _audioToggleButton.BackColor = Color.LightGreen;
            _audioStatusLabel.Text = "Audio: Disabled";
            _audioStatusLabel.ForeColor = Color.DarkRed;
            
            _audioConnection?.Dispose();
            _audioConnection = null;
        }

        private async Task EnableMicAsync()
        {
            try
            {
                if (_audioConnection != null && _audioConnection.IsConnected)
                {
                    await _audioConnection.RequestMicStreamAsync();
                    _micEnabled = true;
                    _micToggleButton.Text = "🎤 Disable Mic";
                    _micToggleButton.BackColor = Color.LightCoral;
                }
                else
                {
                    MessageBox.Show("Audio connection not available. Enable audio first.", 
                        "Mic Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error enabling mic: {ex.Message}", "Mic Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DisableMic()
        {
            _micEnabled = false;
            _micToggleButton.Text = "🎤 Enable Mic";
            _micToggleButton.BackColor = Color.LightBlue;
            
            _audioConnection?.StopMicStreamAsync();
        }

        private void OnAudioDataReceived(object sender, byte[] audioData)
        {
            try
            {
                // Add audio data to buffer
                _audioBuffer.Write(audioData, 0, audioData.Length);
                
                // Play audio when we have enough data
                if (_audioBuffer.Length > 1024) // 1KB buffer
                {
                    _audioBuffer.Position = 0;
                    _soundPlayer.Stream = _audioBuffer;
                    _soundPlayer.Play();
                    
                    // Reset buffer
                    _audioBuffer.SetLength(0);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Audio playback error: {ex.Message}");
            }
        }

        private void OnAudioError(object sender, Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Audio error: {ex.Message}");
            DisableAudio();
        }

        public void UpdateScreenshot(ScreenshotData screenshot)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateScreenshot(screenshot)));
                return;
            }

            if (screenshot != null && screenshot.Screenshot != null)
            {
                _currentScreenshot = screenshot;
                
                // Show the full screenshot
                ShowScreenshot(screenshot.Screenshot);

                // Update window title with timestamp
                this.Text = $"Screenshot Viewer - {_peerInfo.Name} ({screenshot.Timestamp:HH:mm:ss})";
            }
        }

        public void ClearScreenshot()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(ClearScreenshot));
                return;
            }

            if (_screenshotPictureBox.Image != null)
            {
                _screenshotPictureBox.Image.Dispose();
                _screenshotPictureBox.Image = null;
            }

            _currentScreenshot = null;
            this.Text = $"Screenshot Viewer - {_peerInfo.Name}";
        }

        private void ShowScreenshot(System.Drawing.Image screenshot)
        {
            if (_screenshotPictureBox.Image != null)
            {
                _screenshotPictureBox.Image.Dispose();
            }
            _screenshotPictureBox.Image = new Bitmap(screenshot);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_screenshotPictureBox.Image != null)
                {
                    _screenshotPictureBox.Image.Dispose();
                }
                DisableAudio();
                DisableMic();
                _soundPlayer?.Dispose();
                _audioBuffer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
