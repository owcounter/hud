namespace Owmeta.Services
{
    public static class OverwatchPresencePolicy
    {
        public static bool CanShowOverlay(bool devMode, bool processRunning, bool windowFound)
        {
            return devMode || processRunning || windowFound;
        }
    }
}
