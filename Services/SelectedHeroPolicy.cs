using Owmeta.Model;

namespace Owmeta.Services
{
    public static class SelectedHeroPolicy
    {
        public static SelectedHeroResolution ResolveForSwapPanel(
            HeroName currentPlayerHero,
            HeroName? previousPlayerHero,
            IReadOnlyDictionary<HeroName, HeroAnalysis> blueTeamAnalysis)
        {
            if (IsKnownHero(currentPlayerHero) && blueTeamAnalysis.ContainsKey(currentPlayerHero))
            {
                return new SelectedHeroResolution(currentPlayerHero, usedPreviousHero: false, "current selected hero recognized");
            }

            if (previousPlayerHero.HasValue &&
                IsKnownHero(previousPlayerHero.Value) &&
                blueTeamAnalysis.ContainsKey(previousPlayerHero.Value))
            {
                return new SelectedHeroResolution(previousPlayerHero.Value, usedPreviousHero: true, "current selected hero unknown; reused previous selected hero still present on blue team");
            }

            return new SelectedHeroResolution(null, usedPreviousHero: false, $"current selected hero {currentPlayerHero} is unknown or absent from blue team");
        }

        private static bool IsKnownHero(HeroName hero)
        {
            return hero != HeroName.NameUnspecified &&
                   hero != HeroName.Unknown &&
                   hero != HeroName.Hidden;
        }
    }

    public sealed class SelectedHeroResolution
    {
        public SelectedHeroResolution(HeroName? hero, bool usedPreviousHero, string reason)
        {
            Hero = hero;
            UsedPreviousHero = usedPreviousHero;
            Reason = reason;
        }

        public HeroName? Hero { get; }
        public bool UsedPreviousHero { get; }
        public string Reason { get; }
    }
}
