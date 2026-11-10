using Owmeta.Model;

namespace Owmeta.Services
{
    public sealed class SelectedHeroTracker
    {
        private HeroName? _lastResolvedHero;

        public SelectedHeroResolution ResolveForSwapPanel(
            HeroName currentPlayerHero,
            IReadOnlyDictionary<HeroName, HeroAnalysis> blueTeamAnalysis)
        {
            var resolution = SelectedHeroPolicy.ResolveForSwapPanel(
                currentPlayerHero,
                _lastResolvedHero,
                blueTeamAnalysis);

            _lastResolvedHero = resolution.Hero;
            return resolution;
        }

        public void Clear()
        {
            _lastResolvedHero = null;
        }
    }
}
