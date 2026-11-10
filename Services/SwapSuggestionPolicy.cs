using Owmeta.Model;

namespace Owmeta.Services
{
    public static class SwapSuggestionPolicy
    {
        public const int DefaultMaxSuggestions = 21;

        public static SwapSuggestionSelection SelectSuggestions(
            IEnumerable<HeroAnalysis> suggestions,
            int minScore,
            int maxSuggestions = DefaultMaxSuggestions)
        {
            var orderedSuggestions = suggestions
                .OrderByDescending(s => s.HeroScore)
                .ToList();

            var filteredSuggestions = orderedSuggestions
                .Where(s => s.HeroScore >= minScore)
                .Take(maxSuggestions)
                .ToList();

            return new SwapSuggestionSelection(
                filteredSuggestions,
                orderedSuggestions.Count);
        }
    }

    public sealed class SwapSuggestionSelection
    {
        public SwapSuggestionSelection(
            IReadOnlyList<HeroAnalysis> suggestions,
            int totalCount)
        {
            Suggestions = suggestions;
            TotalCount = totalCount;
            ShownCount = suggestions.Count;
        }

        public IReadOnlyList<HeroAnalysis> Suggestions { get; }
        public int TotalCount { get; }
        public int ShownCount { get; }
    }
}
