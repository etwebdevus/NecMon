using System;
using System.Drawing;

namespace PeerViewer.Models
{
    public class ScreenshotData
    {
        public string PeerId { get; set; }
        public DateTime Timestamp { get; set; }
        public Image Screenshot { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Format { get; set; }

        public ScreenshotData()
        {
            Timestamp = DateTime.Now;
            Format = "PNG";
        }

        public void Dispose()
        {
            Screenshot?.Dispose();
        }
    }
}
