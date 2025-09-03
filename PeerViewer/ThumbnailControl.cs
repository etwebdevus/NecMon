using System;
using System.Drawing;
using System.Windows.Forms;
using System.Linq;
using PeerViewer.Models;

namespace PeerViewer
{
    public class ThumbnailControl : UserControl
    {
        private PictureBox _thumbnailPictureBox;
        private Label _peerNameLabel;
        private Label _timestampLabel;
        private Label _resolutionLabel;
        private Panel _infoPanel;
        private ScreenshotData _currentScreenshot;
        private ToolTip _toolTip;

        public string PeerId { get; private set; }
        public string PeerName { get; private set; }
        public int ScreenCount { get; private set; }
        public string PeerResolution { get; private set; }

        public event EventHandler<ThumbnailControl> ThumbnailClicked;

        public ThumbnailControl(string peerId, string peerName, int screenCount = 1, string peerResolution = "Unknown")
        {
            PeerId = peerId;
            PeerName = peerName;
            ScreenCount = screenCount;
            PeerResolution = peerResolution;
            
            // Debug: Log the constructor values
            System.Diagnostics.Debug.WriteLine($"ThumbnailControl Constructor - PeerId: {peerId}, PeerName: {peerName}, ScreenCount: {screenCount}, Resolution: {peerResolution}");
            
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            // Debug: Log the ScreenCount value at the start of InitializeComponent
            System.Diagnostics.Debug.WriteLine($"InitializeComponent - ScreenCount: {ScreenCount}, PeerName: {PeerName}");
            
            // Calculate size based on screen count
            int baseWidth = 500;
            int thumbnailWidth = 480;
            int thumbnailHeight = 270;
            int baseHeight = thumbnailHeight + 90; // Height = thumbnail height + info panel height + margins

            if (ScreenCount > 1)
            {
                // For multiple screens, make the thumbnail wider to accommodate the aspect ratio
                float aspectRatio = (float)ScreenCount * 16.0f / 9.0f; // Assume 16:9 aspect ratio per screen
                thumbnailWidth = Math.Min(1280, (int)(thumbnailHeight * aspectRatio));
                baseWidth = thumbnailWidth + 20; // Add padding
                
                // Debug: Log the calculated dimensions
                System.Diagnostics.Debug.WriteLine($"Multi-screen thumbnail - ScreenCount: {ScreenCount}, Calculated Width: {thumbnailWidth}, Base Width: {baseWidth}");
            }
            else
            {
                // Debug: Log single screen dimensions
                System.Diagnostics.Debug.WriteLine($"Single-screen thumbnail - ScreenCount: {ScreenCount}, Width: {thumbnailWidth}, Base Width: {baseWidth}");
            }

            this.Size = new Size(baseWidth, baseHeight);
            this.BorderStyle = BorderStyle.FixedSingle;
            this.BackColor = Color.White;
            this.Margin = new Padding(5);

            _toolTip = new ToolTip();

            // Thumbnail picture box
            _thumbnailPictureBox = new PictureBox
            {
                Size = new Size(thumbnailWidth, thumbnailHeight),
                Location = new Point(10, 10),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle,
                Cursor = Cursors.Hand
            };
            _thumbnailPictureBox.Click += OnThumbnailClicked;

            // Info panel
            _infoPanel = new Panel
            {
                Size = new Size(thumbnailWidth, 60), // Increased height for better spacing
                Location = new Point(10, thumbnailHeight + 20), // Position below the thumbnail with some spacing
                BackColor = Color.LightGray
            };

            // Peer name label
            _peerNameLabel = new Label
            {
                Text = PeerName ?? "Unknown",
                Font = new Font("Segoe UI", 10, FontStyle.Bold), // Slightly larger font
                Size = new Size(thumbnailWidth - 10, 25),
                Location = new Point(5, 5),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };

            // Timestamp label
            _timestampLabel = new Label
            {
                Text = "No screenshot",
                Font = new Font("Segoe UI", 8), // Slightly larger font
                Size = new Size((thumbnailWidth - 10) / 2, 20),
                Location = new Point(5, 35),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };

            // Resolution label
            _resolutionLabel = new Label
            {
                Text = PeerResolution,
                Font = new Font("Segoe UI", 8), // Slightly larger font
                Size = new Size((thumbnailWidth - 10) / 2, 20),
                Location = new Point(5 + (thumbnailWidth - 10) / 2, 35),
                TextAlign = ContentAlignment.MiddleRight,
                BackColor = Color.Transparent
            };

            _infoPanel.Controls.AddRange(new Control[] { _peerNameLabel, _timestampLabel, _resolutionLabel });
            this.Controls.AddRange(new Control[] { _thumbnailPictureBox, _infoPanel });
        }

        public void UpdateScreenshot(ScreenshotData screenshot)
        {
            if (screenshot != null && screenshot.Screenshot != null)
            {
                _currentScreenshot = screenshot;
                
                // Update thumbnail
                if (_thumbnailPictureBox.Image != null)
                {
                    _thumbnailPictureBox.Image.Dispose();
                }
                _thumbnailPictureBox.Image = new Bitmap(screenshot.Screenshot);

                // Update info
                _timestampLabel.Text = screenshot.Timestamp.ToString("HH:mm:ss");
                // Keep the peer's resolution, don't override with screenshot resolution
                _resolutionLabel.Text = PeerResolution;

                // Update tooltip
                _toolTip.SetToolTip(this, "Last updated: " + screenshot.Timestamp.ToString("HH:mm:ss") + "\nPeer Resolution: " + PeerResolution + "\nScreenshot: " + screenshot.Width.ToString() + "x" + screenshot.Height.ToString());
            }
        }

        public void ClearScreenshot()
        {
            if (_thumbnailPictureBox.Image != null)
            {
                _thumbnailPictureBox.Image.Dispose();
                _thumbnailPictureBox.Image = null;
            }
            _timestampLabel.Text = "No screenshot";
            _resolutionLabel.Text = PeerResolution; // Restore peer's resolution
            _currentScreenshot = null;
        }

        private void OnThumbnailClicked(object sender, EventArgs e)
        {
            ThumbnailClicked?.Invoke(this, this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_thumbnailPictureBox.Image != null)
                {
                    _thumbnailPictureBox.Image.Dispose();
                }
                _toolTip?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
