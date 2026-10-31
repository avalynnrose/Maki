using System.Collections.Concurrent;

namespace Maki.Api.Services;

/// <summary>
/// Keeps automatic source downloads away from a new series while its source measurement runs and the
/// language fallback may still be decided. One preview file would otherwise make the series ineligible
/// for ever, because the fallback only applies to a series with no files. In memory and bounded: a
/// restart or a measurement that never finishes releases the series after <see cref="MaxHold"/>.
/// </summary>
public static class LanguageFallbackHold
{
    public static readonly TimeSpan MaxHold = TimeSpan.FromMinutes(25);

    private static readonly ConcurrentDictionary<int, DateTime> Held = new();

    public static void Hold(int seriesId) => Held[seriesId] = DateTime.UtcNow;

    public static void Release(int seriesId) => Held.TryRemove(seriesId, out _);

    public static bool Holds(int seriesId, DateTime nowUtc) =>
        Held.TryGetValue(seriesId, out var since) && nowUtc - since < MaxHold;
}
