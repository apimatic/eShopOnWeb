using System;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Studio;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.StudioEndpoints;

/// <summary>Pure logic of the studio integration: no network involved.</summary>
[TestClass]
public class StudioClockTest
{
    private static readonly TimeZoneInfo LosAngeles = StudioClock.FindZone("America/Los_Angeles");

    [TestMethod]
    public void WallClockTimeGetsTheOffsetOfItsOwnDay()
    {
        var winter = StudioClock.ToStudioTime(StudioClock.ToInstant(new DateTime(2026, 11, 12, 6, 20, 0), LosAngeles), LosAngeles);
        var summer = StudioClock.ToStudioTime(StudioClock.ToInstant(new DateTime(2027, 7, 12, 6, 20, 0), LosAngeles), LosAngeles);

        Assert.AreEqual(TimeSpan.FromHours(-8), winter.Offset);
        Assert.AreEqual(TimeSpan.FromHours(-7), summer.Offset);
        Assert.AreEqual(new DateTime(2026, 11, 12, 6, 20, 0), winter.DateTime);
    }

    [TestMethod]
    public void TimesTheSdkReadAsUtcAreReinterpretedAsStudioClockTimes()
    {
        // The SDK parses "2026-11-12T06:20:00" (no offset) as 06:20 UTC; it really means 06:20 in the studio.
        var sdkValue = DateTimeOffset.Parse("2026-11-12T06:20:00Z");

        var fixedUp = StudioClock.FromMindbody(sdkValue, LosAngeles);

        Assert.AreEqual(new DateTimeOffset(2026, 11, 12, 6, 20, 0, TimeSpan.FromHours(-8)), fixedUp);
    }

    [TestMethod]
    public void AnEndTimeAcrossTheSpringForwardChangeKeepsItsLength()
    {
        var start = StudioClock.ToInstant(new DateTime(2027, 3, 14, 1, 30, 0), LosAngeles);

        var end = StudioClock.ToStudioTime(start.AddMinutes(60), LosAngeles);

        Assert.AreEqual(new DateTimeOffset(2027, 3, 14, 3, 30, 0, TimeSpan.FromHours(-7)), end);
    }

    [TestMethod]
    public async Task KeyedLockSerialisesTheSameKeyOnly()
    {
        var locks = new KeyedAsyncLock();
        var first = await locks.LockAsync("a");

        var sameKey = locks.LockAsync("a");
        using var otherKey = await locks.LockAsync("b");
        await Task.Delay(50);
        Assert.IsFalse(sameKey.IsCompleted);

        first.Dispose();
        using var acquired = await sameKey.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
