namespace Owmeta.Services
{
    public sealed class ScreenshotCaptureThrottle
    {
        private readonly TimeSpan _cooldown;
        private DateTimeOffset? _lastCaptureStartedAt;
        private DateTimeOffset? _previousCaptureStartedAt;
        private bool _captureInProgress;

        public ScreenshotCaptureThrottle(TimeSpan cooldown)
        {
            _cooldown = cooldown;
        }

        public bool TryBeginCapture(DateTimeOffset now, out string reason)
        {
            if (_captureInProgress)
            {
                reason = "capture already in progress";
                return false;
            }

            if (_lastCaptureStartedAt.HasValue)
            {
                var elapsed = now - _lastCaptureStartedAt.Value;
                if (elapsed < _cooldown)
                {
                    var remaining = _cooldown - elapsed;
                    reason = $"cooldown ({Math.Ceiling(remaining.TotalMilliseconds)}ms remaining)";
                    return false;
                }
            }

            _previousCaptureStartedAt = _lastCaptureStartedAt;
            _lastCaptureStartedAt = now;
            _captureInProgress = true;
            reason = string.Empty;
            return true;
        }

        public void CompleteCapture()
        {
            _captureInProgress = false;
        }

        /// <summary>
        /// Ends a capture that was cancelled before any pixels were taken (Tab released too early):
        /// it does not count towards the cooldown, so holding Tab again right away still captures.
        /// </summary>
        public void CancelCapture()
        {
            if (!_captureInProgress) return;
            _lastCaptureStartedAt = _previousCaptureStartedAt;
            _captureInProgress = false;
        }
    }
}
