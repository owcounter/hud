using Owmeta.Model;

namespace Owmeta.Services
{
    public sealed class MatchBoundaryTracker
    {
        private MapName? _lastKnownMap;

        public bool Observe(MapName currentMap)
        {
            var knownMapChanged = MatchBoundaryPolicy.IsKnownMapChange(_lastKnownMap, currentMap);

            if (MatchBoundaryPolicy.IsKnownMap(currentMap))
            {
                _lastKnownMap = currentMap;
            }

            return knownMapChanged;
        }

        public void Clear()
        {
            _lastKnownMap = null;
        }
    }
}
