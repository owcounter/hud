using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Owmeta.Model
{
    public class ScreenshotProcessingResponse
    {
        public required MatchState MatchState { get; set; }
        public required Dictionary<HeroName, HeroAnalysis> BlueTeamAnalysis { get; set; }
        public required Dictionary<HeroName, HeroAnalysis> RedTeamAnalysis { get; set; }
        public List<string> PersistedBlueTeamSlots { get; set; } = new();
        public List<string> PersistedRedTeamSlots { get; set; } = new();
    }

    public class ScreenshotProcessingResponseConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(ScreenshotProcessingResponse);
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            JObject jo = JObject.Load(reader);

            // Parse MatchState first
            var matchStateToken = jo["matchState"];
            if (matchStateToken == null || !matchStateToken.HasValues)
            {
                return null; // Invalid JSON structure
            }

            var matchState = matchStateToken.ToObject<MatchState>(serializer)
                ?? throw new JsonSerializationException("Failed to deserialize MatchState");

            var response = new ScreenshotProcessingResponse
            {
                MatchState = matchState,
                BlueTeamAnalysis = new Dictionary<HeroName, HeroAnalysis>(),
                RedTeamAnalysis = new Dictionary<HeroName, HeroAnalysis>()
            };

            // Parse team analyses
            var matchStateAnalysis = jo["matchStateAnalysis"] as JObject;
            if (matchStateAnalysis != null)
            {
                // Parse blue team
                var blueTeam = matchStateAnalysis["blueTeamAnalysis"] as JObject;
                if (blueTeam != null)
                {
                    foreach (var prop in blueTeam.Properties())
                    {
                        if (int.TryParse(prop.Name, out int heroId))
                        {
                            var heroName = (HeroName)heroId;
                            var analysis = prop.Value.ToObject<HeroAnalysis>(serializer);
                            if (analysis != null)
                            {
                                response.BlueTeamAnalysis[heroName] = analysis;
                            }
                        }
                    }
                }

                // Parse red team
                var redTeam = matchStateAnalysis["redTeamAnalysis"] as JObject;
                if (redTeam != null)
                {
                    foreach (var prop in redTeam.Properties())
                    {
                        if (int.TryParse(prop.Name, out int heroId))
                        {
                            var heroName = (HeroName)heroId;
                            var analysis = prop.Value.ToObject<HeroAnalysis>(serializer);
                            if (analysis != null)
                            {
                                response.RedTeamAnalysis[heroName] = analysis;
                            }
                        }
                    }
                }
            }

            var persistedBlue = jo["persistedBlueTeamSlots"] as JArray;
            if (persistedBlue != null)
                response.PersistedBlueTeamSlots = persistedBlue.Select(t => t.ToString()).ToList();

            var persistedRed = jo["persistedRedTeamSlots"] as JArray;
            if (persistedRed != null)
                response.PersistedRedTeamSlots = persistedRed.Select(t => t.ToString()).ToList();

            return response;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var response = (ScreenshotProcessingResponse)value;
            var jo = new JObject();

            // Write MatchState
            jo["matchState"] = JToken.FromObject(response.MatchState, serializer);

            // Write team analyses
            var matchStateAnalysis = new JObject();

            var blueTeam = new JObject();
            if (response.BlueTeamAnalysis != null)
            {
                foreach (var kvp in response.BlueTeamAnalysis)
                {
                    blueTeam[((int)kvp.Key).ToString()] = JToken.FromObject(kvp.Value, serializer);
                }
            }
            matchStateAnalysis["blueTeamAnalysis"] = blueTeam;

            var redTeam = new JObject();
            if (response.RedTeamAnalysis != null)
            {
                foreach (var kvp in response.RedTeamAnalysis)
                {
                    redTeam[((int)kvp.Key).ToString()] = JToken.FromObject(kvp.Value, serializer);
                }
            }
            matchStateAnalysis["redTeamAnalysis"] = redTeam;

            jo["matchStateAnalysis"] = matchStateAnalysis;

            if (response.PersistedBlueTeamSlots?.Count > 0)
                jo["persistedBlueTeamSlots"] = new JArray(response.PersistedBlueTeamSlots);
            if (response.PersistedRedTeamSlots?.Count > 0)
                jo["persistedRedTeamSlots"] = new JArray(response.PersistedRedTeamSlots);

            jo.WriteTo(writer);
        }
    }
}