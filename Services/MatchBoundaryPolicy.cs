using Owmeta.Model;

namespace Owmeta.Services
{
    public static class MatchBoundaryPolicy
    {
        public static bool IsKnownMapChange(MapName? previousMap, MapName? currentMap)
        {
            return previousMap.HasValue &&
                   currentMap.HasValue &&
                   IsKnownMap(previousMap.Value) &&
                   IsKnownMap(currentMap.Value) &&
                   previousMap.Value != currentMap.Value;
        }

        private static bool IsKnownMap(MapName map)
        {
            return map != MapName.MapUnspecified &&
                   map != MapName.AllMaps;
        }
    }
}
