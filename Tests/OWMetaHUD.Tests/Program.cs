using Owmeta.Model;
using Owmeta.Services;

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
