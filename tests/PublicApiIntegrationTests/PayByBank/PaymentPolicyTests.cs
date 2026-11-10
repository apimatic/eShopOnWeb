using System;
using System.Linq;
using Microsoft.eShopWeb.PublicApi.PayByBank;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PayByBank;

[TestClass]
public class PaymentPolicyTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static PaymentSpeedPolicy PolicyAt(string easternLocal)
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Eastern Standard Time" : "America/New_York");
        var local = DateTime.Parse(easternLocal, System.Globalization.CultureInfo.InvariantCulture);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, eastern);
        return new PaymentSpeedPolicy(new FixedTime(new DateTimeOffset(utc, TimeSpan.Zero)), Options.Create(new PayByBankOptions { ExpressFee = 5m }));
    }

    [TestMethod]
    public void Express_IsOfferedOnABusinessMorning()
    {
        CollectionAssert.AreEqual(new[] { "standard", "express" }, PolicyAt("2026-11-10 10:00").OptionsFor(100m).ToArray());
    }

    [TestMethod]
    public void Express_IsNotOfferedAfterTheSameDayCutoff()
    {
        CollectionAssert.AreEqual(new[] { "standard" }, PolicyAt("2026-11-10 15:30").OptionsFor(100m).ToArray());
        CollectionAssert.AreEqual(new[] { "standard" }, PolicyAt("2026-11-10 14:50").OptionsFor(100m).ToArray());
    }

    [TestMethod]
    public void Express_IsNotOfferedOnWeekendsOrFederalHolidays()
    {
        CollectionAssert.AreEqual(new[] { "standard" }, PolicyAt("2026-11-14 10:00").OptionsFor(100m).ToArray()); // Saturday
        CollectionAssert.AreEqual(new[] { "standard" }, PolicyAt("2026-11-11 10:00").OptionsFor(100m).ToArray()); // Veterans Day
        CollectionAssert.AreEqual(new[] { "standard" }, PolicyAt("2026-11-26 10:00").OptionsFor(100m).ToArray()); // Thanksgiving
    }

    [TestMethod]
    public void Express_IsNotOfferedAboveTheSameDayAchLimit()
    {
        var policy = PolicyAt("2026-11-10 10:00");
        CollectionAssert.AreEqual(new[] { "standard", "express" }, policy.OptionsFor(999_995m).ToArray());
        CollectionAssert.AreEqual(new[] { "standard" }, policy.OptionsFor(999_995.01m).ToArray());
        CollectionAssert.AreEqual(new[] { "standard" }, policy.OptionsFor(25_000_000m).ToArray());
    }

    [TestMethod]
    public void LargeOrders_AreSplitIntoTransfersWithinTheAchLimit()
    {
        var parts = BankPaymentService.Split(25_000_000.50m);
        CollectionAssert.AreEqual(new[] { 10_000_000m, 10_000_000m, 5_000_000.50m }, parts.ToArray());
        Assert.AreEqual(1, BankPaymentService.Split(10_000_000m).Count);
        Assert.AreEqual(25_000_000.50m, parts.Sum());
    }
}
