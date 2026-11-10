namespace Owmeta.Services
{
    public sealed class ScreenshotCaptureThrottle
    {
        private readonly TimeSpan _cooldown;
        private DateTimeOffset? _lastCaptureStartedAt;
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

            _lastCaptureStartedAt = now;
            _captureInProgress = true;
            reason = string.Empty;
            return true;
        }

        public void CompleteCapture()
        {
            _captureInProgress = false;
        }
    }
}
