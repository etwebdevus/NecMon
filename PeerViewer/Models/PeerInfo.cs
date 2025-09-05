using System;
using System.Net;

namespace PeerViewer.Models
{
    public class PeerInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public IPEndPoint EndPoint { get; set; }
        public DateTime LastSeen { get; set; }
        public bool IsOnline { get; set; }
        public string MachineName { get; set; }
        public string UserName { get => Name; set => Name = value; }
        public string OSVersion { get; set; }
        private int _screenCount = 1;
        public int ScreenCount
        {
            get => _screenCount;
            set
            {
                _screenCount = value;
                // Debug: Log when ScreenCount changes
                System.Diagnostics.Debug.WriteLine($"PeerInfo ScreenCount changed to: {value} for peer: {Name}");
            }
        }

        public string Resolution { get; set; } = "Unknown";

        public PeerInfo()
        {
            Id = Guid.NewGuid().ToString();
            LastSeen = DateTime.Now;
            IsOnline = true;
            ScreenCount = 1; // Default to single screen
        }

        public override string ToString()
        {
            return $"{Name ?? "Unknown"} ({EndPoint?.Address}) - {MachineName}";
        }
    }
}
