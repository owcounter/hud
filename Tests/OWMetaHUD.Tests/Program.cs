using Owmeta.Model;
using Owmeta.Services;

if (args.Length == 1 && args[0] == "--diagnostics")
{
    if (!HudDiagnostics.Enabled) throw new Exception("Enable OWMETA_HUD_DIAGNOSTICS for this test");
    var diagnosticRoot = Environment.GetEnvironmentVariable("OWMETA_CAPTURE_DIR")
        ?? throw new Exception("Set an isolated OWMETA_CAPTURE_DIR for this test");
    HudDiagnostics.Write("diagnostic-smoke-start");
    using (HudDiagnostics.Measure("diagnostic-smoke-span")) { }
    HudDiagnostics.Write("diagnostic-smoke-end");
    var verified = false;
    for (var attempt = 0; attempt < 100; attempt++)
    {
        await Task.Delay(50);
        if (!System.IO.Directory.Exists(diagnosticRoot)) continue;
        var log = System.IO.Directory.GetFiles(diagnosticRoot, $"hud-timing-*-{Environment.ProcessId}.log").SingleOrDefault();
        if (log == null) continue;
        using var logStream = new System.IO.FileStream(log, System.IO.FileMode.Open,
            System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
        using var logReader = new System.IO.StreamReader(logStream);
        var text = await logReader.ReadToEndAsync();
        if (text.Contains("diagnostic-smoke-start") && text.Contains("operation=diagnostic-smoke-span") && text.Contains("diagnostic-smoke-end"))
        {
            verified = true;
            break;
        }
    }
    if (!verified) throw new Exception("Diagnostic writer failed to flush events");
    Console.WriteLine("PASS background diagnostic writer, session header and timing span");
    return 0;
}

if (args.Length == 1 && args[0] == "--placement")
{
    OverlayPlacementTests.Run();
    return 0;
}

if (args.Length == 2 && args[0] == "--live")
{
    await RecognitionTests.RunLive(args[1]);
    return 0;
}

RecognitionTests.Run();
ServerHistoryTests.Run();
OverlayPlacementTests.Run();
var content = new OverlayContentState { GameAvailable = true };
content.Toggle(1);
if (!content.ShowSwap) throw new Exception("F2 did not show");
content.Capturing = true;
if (content.ShowSwap) throw new Exception("Capture did not suppress content");
content.Toggle(1); content.Capturing = false;
if (content.ShowSwap) throw new Exception("Tab release undid F2 hide");
content.Toggle(1); content.Capturing = true; content.Toggle(2); content.Capturing = false;
if (!content.ShowComposition || content.ShowSwap) throw new Exception("Tab release lost F3 selection");
content.GameAvailable = false;
if (content.ShowComposition) throw new Exception("Game absence did not suppress content");
content.GameAvailable = true;
for (int i = 0; i < 100; i++) content.Toggle(2);
if (!content.ShowComposition) throw new Exception("Rapid toggle parity lost");
Console.WriteLine("PASS content-only toggles, Tab suppression, F2/F3 during capture, presence and rapid toggle parity");
ScreenshotCaptureGuard.Check(CancellationToken.None, () => true);
foreach (var released in new[] { false, true })
{
    using var cancel = new CancellationTokenSource();
    if (!released) cancel.Cancel();
    try { ScreenshotCaptureGuard.Check(cancel.Token, () => !released); throw new Exception("Released capture was accepted"); }
    catch (OperationCanceledException) { }
}
Console.WriteLine("PASS Tab release cancellation and physical-key guard");

var tests = new (string Name, Action Run)[]
{
    ("resets when previous and current known maps differ", () =>
    {
        AssertTrue(MatchBoundaryPolicy.IsKnownMapChange(MapName.KingsRow, MapName.Route66));
    }),
    ("does not reset when known maps are the same", () =>
    {
        AssertFalse(MatchBoundaryPolicy.IsKnownMapChange(MapName.KingsRow, MapName.KingsRow));
    }),
    ("does not reset when previous map is unspecified", () =>
    {
        AssertFalse(MatchBoundaryPolicy.IsKnownMapChange(MapName.MapUnspecified, MapName.Route66));
    }),
    ("does not reset when current map is unspecified", () =>
    {
        AssertFalse(MatchBoundaryPolicy.IsKnownMapChange(MapName.KingsRow, MapName.MapUnspecified));
    }),
    ("tracks first known map without reset", () =>
    {
        var tracker = new MatchBoundaryTracker();

        AssertFalse(tracker.Observe(MapName.MapUnspecified));
        AssertFalse(tracker.Observe(MapName.Route66));
    }),
    ("tracks known map change across unspecified response", () =>
    {
        var tracker = new MatchBoundaryTracker();

        AssertFalse(tracker.Observe(MapName.KingsRow));
        AssertFalse(tracker.Observe(MapName.MapUnspecified));
        AssertTrue(tracker.Observe(MapName.Route66));
    }),
    ("does not reset when same known map returns after unspecified response", () =>
    {
        var tracker = new MatchBoundaryTracker();

        AssertFalse(tracker.Observe(MapName.KingsRow));
        AssertFalse(tracker.Observe(MapName.MapUnspecified));
        AssertFalse(tracker.Observe(MapName.KingsRow));
    }),
    ("does not infer new match before map is known from roster changes and persisted red", () =>
    {
        var tracker = new MatchBoundaryTracker();

        var first = tracker.Observe(MapName.KingsRow);
        var second = tracker.Observe(MapName.MapUnspecified);

        AssertFalse(first);
        AssertFalse(second);
    }),
    ("F2 suggestions use score threshold when suggestions pass", () =>
    {
        var result = SwapSuggestionPolicy.SelectSuggestions(new[]
        {
            CreateAnalysis(HeroName.Hanzo, -1),
            CreateAnalysis(HeroName.Sigma, 4),
            CreateAnalysis(HeroName.Ana, 2),
        }, minScore: 0);

        AssertEqual(3, result.TotalCount);
        AssertEqual(2, result.ShownCount);
        AssertHeroOrder(result.Suggestions, HeroName.Sigma, HeroName.Ana);
    }),
    ("F2 suggestions preserve threshold when it filters everything", () =>
    {
        var result = SwapSuggestionPolicy.SelectSuggestions(new[]
        {
            CreateAnalysis(HeroName.Hanzo, -5),
            CreateAnalysis(HeroName.Sigma, -1),
            CreateAnalysis(HeroName.Ana, -3),
        }, minScore: 0);

        AssertEqual(3, result.TotalCount);
        AssertEqual(0, result.ShownCount);
        AssertHeroOrder(result.Suggestions);
    }),
    ("F2 suggestions do not add extra hero filtering", () =>
    {
        var result = SwapSuggestionPolicy.SelectSuggestions(new[]
        {
            CreateAnalysis(HeroName.Unknown, 5),
            CreateAnalysis(HeroName.Sigma, 4),
        }, minScore: 0);

        AssertEqual(2, result.TotalCount);
        AssertEqual(2, result.ShownCount);
        AssertHeroOrder(result.Suggestions, HeroName.Unknown, HeroName.Sigma);
    }),
    ("screenshot throttle blocks captures inside cooldown", () =>
    {
        var throttle = new ScreenshotCaptureThrottle(TimeSpan.FromSeconds(2));
        var now = DateTimeOffset.Parse("2026-05-22T12:00:00Z");

        AssertTrue(throttle.TryBeginCapture(now, out _));
        throttle.CompleteCapture();

        AssertFalse(throttle.TryBeginCapture(now.AddMilliseconds(1999), out var reason));
        AssertContains(reason, "cooldown");
        AssertTrue(throttle.TryBeginCapture(now.AddSeconds(2), out _));
    }),
    ("screenshot throttle blocks overlapping captures", () =>
    {
        var throttle = new ScreenshotCaptureThrottle(TimeSpan.FromSeconds(2));
        var now = DateTimeOffset.Parse("2026-05-22T12:00:00Z");

        AssertTrue(throttle.TryBeginCapture(now, out _));

        AssertFalse(throttle.TryBeginCapture(now.AddSeconds(3), out var reason));
        AssertContains(reason, "already in progress");

        throttle.CompleteCapture();
        AssertTrue(throttle.TryBeginCapture(now.AddSeconds(3), out _));
    }),
    ("overlay is allowed in dev mode without Overwatch", () =>
    {
        AssertTrue(OverwatchPresencePolicy.CanShowOverlay(
            devMode: true,
            processRunning: false,
            windowFound: false));
    }),
    ("overlay is blocked in release mode when Overwatch is absent", () =>
    {
        AssertFalse(OverwatchPresencePolicy.CanShowOverlay(
            devMode: false,
            processRunning: false,
            windowFound: false));
    }),
    ("overlay is allowed when Overwatch process is running without a window handle", () =>
    {
        AssertTrue(OverwatchPresencePolicy.CanShowOverlay(
            devMode: false,
            processRunning: true,
            windowFound: false));
    }),
    ("F2 uses current selected hero when it is present in blue team", () =>
    {
        var result = SelectedHeroPolicy.ResolveForSwapPanel(
            HeroName.Sigma,
            previousPlayerHero: null,
            CreateTeam(HeroName.Sigma, HeroName.Juno));

        AssertTrue(result.Hero == HeroName.Sigma);
        AssertFalse(result.UsedPreviousHero);
    }),
    ("F2 reuses previous selected hero when current selected hero is unknown but still on team", () =>
    {
        var result = SelectedHeroPolicy.ResolveForSwapPanel(
            HeroName.Unknown,
            HeroName.Sigma,
            CreateTeam(HeroName.Sigma, HeroName.Juno));

        AssertTrue(result.Hero == HeroName.Sigma);
        AssertTrue(result.UsedPreviousHero);
    }),
    ("F2 clears when current selected hero is unknown and previous selected hero is absent", () =>
    {
        var result = SelectedHeroPolicy.ResolveForSwapPanel(
            HeroName.Unknown,
            HeroName.Sigma,
            CreateTeam(HeroName.Hazard, HeroName.Juno));

        AssertTrue(result.Hero == null);
        AssertContains(result.Reason, "unknown");
    }),
    ("F2 keeps resolved selected hero across repeated unknown selected hero responses", () =>
    {
        var tracker = new SelectedHeroTracker();
        var team = CreateTeam(HeroName.Reinhardt, HeroName.Ashe, HeroName.Sojourn, HeroName.Ana, HeroName.Moira);

        var first = tracker.ResolveForSwapPanel(HeroName.Reinhardt, team);
        var second = tracker.ResolveForSwapPanel(HeroName.Unknown, team);
        var third = tracker.ResolveForSwapPanel(HeroName.Unknown, team);

        AssertTrue(first.Hero == HeroName.Reinhardt);
        AssertTrue(second.Hero == HeroName.Reinhardt);
        AssertTrue(second.UsedPreviousHero);
        AssertTrue(third.Hero == HeroName.Reinhardt);
        AssertTrue(third.UsedPreviousHero);
    }),
    ("F2 clears resolved selected hero when it is no longer on blue team", () =>
    {
        var tracker = new SelectedHeroTracker();

        tracker.ResolveForSwapPanel(HeroName.Reinhardt, CreateTeam(HeroName.Reinhardt, HeroName.Ashe));
        var result = tracker.ResolveForSwapPanel(HeroName.Unknown, CreateTeam(HeroName.Hazard, HeroName.Ashe));

        AssertTrue(result.Hero == null);
        AssertFalse(result.UsedPreviousHero);
    }),
};

foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
        return 1;
    }
}

return 0;

static void AssertTrue(bool value)
{
    if (!value)
    {
        throw new Exception("expected true");
    }
}

static void AssertFalse(bool value)
{
    if (value)
    {
        throw new Exception("expected false");
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new Exception($"expected {expected}, got {actual}");
    }
}

static void AssertContains(string actual, string expectedSubstring)
{
    if (!actual.Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase))
    {
        throw new Exception($"expected '{actual}' to contain '{expectedSubstring}'");
    }
}

static void AssertHeroOrder(IReadOnlyList<HeroAnalysis> actual, params HeroName[] expected)
{
    AssertEqual(expected.Length, actual.Count);
    for (int i = 0; i < expected.Length; i++)
    {
        AssertEqual(expected[i], actual[i].Hero.Name);
    }
}

static HeroAnalysis CreateAnalysis(HeroName heroName, int score)
{
    return new HeroAnalysis
    {
        Hero = new Hero { Name = heroName },
        HeroScore = score
    };
}

static Dictionary<HeroName, HeroAnalysis> CreateTeam(params HeroName[] heroes)
{
    return heroes.ToDictionary(hero => hero, hero => CreateAnalysis(hero, 0));
}
