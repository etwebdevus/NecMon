using System;
using System.Collections.Generic;
using PeerViewer.Models;

namespace PeerViewer.Network
{
    public class ScreenshotService
    {
        private readonly Dictionary<string, ScreenshotData> _screenshots = new Dictionary<string, ScreenshotData>();
        private readonly object _screenshotsLock = new object();

        public event EventHandler<ScreenshotData> ScreenshotUpdated;

        public IReadOnlyDictionary<string, ScreenshotData> Screenshots
        {
            get
            {
                lock (_screenshotsLock)
                {
                    return new Dictionary<string, ScreenshotData>(_screenshots);
                }
            }
        }

        public void AddScreenshot(ScreenshotData screenshot)
        {
            lock (_screenshotsLock)
            {
                if (_screenshots.ContainsKey(screenshot.PeerId))
                {
                    var oldScreenshot = _screenshots[screenshot.PeerId];
                    oldScreenshot.Dispose();
                }

                _screenshots[screenshot.PeerId] = screenshot;
            }

            ScreenshotUpdated?.Invoke(this, screenshot);
        }

        public ScreenshotData GetScreenshot(string peerId)
        {
            lock (_screenshotsLock)
            {
                return _screenshots.ContainsKey(peerId) ? _screenshots[peerId] : null;
            }
        }

        

        public void ClearScreenshots()
        {
            lock (_screenshotsLock)
            {
                foreach (var screenshot in _screenshots.Values)
                {
                    screenshot.Dispose();
                }
                _screenshots.Clear();
            }
        }

        public void RemoveScreenshot(string peerId)
        {
            lock (_screenshotsLock)
            {
                if (_screenshots.ContainsKey(peerId))
                {
                    var screenshot = _screenshots[peerId];
                    screenshot.Dispose();
                    _screenshots.Remove(peerId);
                }
            }
        }

        

        public void Dispose()
        {
            ClearScreenshots();
        }
    }
}
