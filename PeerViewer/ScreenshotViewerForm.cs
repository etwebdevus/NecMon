using PeerViewer.Models;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace PeerViewer
{
    public partial class ScreenshotViewerForm : Form
    {
        private PictureBox _screenshotPictureBox;
        private ScreenshotData _currentScreenshot;
        private PeerInfo _peerInfo;

        public ScreenshotViewerForm(PeerInfo peerInfo)
        {
            _peerInfo = peerInfo;
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

            // Screenshot panel (full screen)
            var screenshotPanel = new Panel
            {
                Dock = DockStyle.Fill,
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
            mainContainer.Controls.Add(screenshotPanel);
            this.Controls.Add(mainContainer);
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
            }
            base.Dispose(disposing);
        }
    }
}
