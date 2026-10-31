using Maki.Api.Services;

namespace Maki.Api.Tests;

public class LanguageFallbackHoldTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Automatic_downloads_wait_for_a_held_series_until_it_is_released()
    {
        var id = _db.SeedSeries("Held series");
        using var db = _db.NewContext();
        var now = DateTime.UtcNow;
        Assert.False(await VolumeFirstGate.HoldsAsync(db, new FakeAppSettings(), now, id, default));

        LanguageFallbackHold.Hold(id);
        try
        {
            Assert.True(await VolumeFirstGate.HoldsAsync(db, new FakeAppSettings(), now, id, default));
        }
        finally
        {
            LanguageFallbackHold.Release(id);
        }

        Assert.False(await VolumeFirstGate.HoldsAsync(db, new FakeAppSettings(), now, id, default));
    }

    [Fact]
    public void A_hold_that_was_never_released_lapses()
    {
        LanguageFallbackHold.Hold(987654);
        try
        {
            Assert.True(LanguageFallbackHold.Holds(987654, DateTime.UtcNow));
            Assert.False(LanguageFallbackHold.Holds(987654, DateTime.UtcNow + LanguageFallbackHold.MaxHold));
        }
        finally
        {
            LanguageFallbackHold.Release(987654);
        }
    }
}
