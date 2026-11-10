using System;
using Microsoft.eShopWeb.PublicApi.Plaid.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PlaidEndpoints;

[TestClass]
public class BusinessCalendarTests
{
    [TestMethod]
    public void WeekendsAndFederalHolidaysAreNotBusinessDays()
    {
        Assert.IsFalse(BusinessCalendar.IsBusinessDay(new DateTime(2026, 11, 14))); // Saturday
        Assert.IsFalse(BusinessCalendar.IsBusinessDay(new DateTime(2026, 11, 26))); // Thanksgiving
        Assert.IsFalse(BusinessCalendar.IsBusinessDay(new DateTime(2026, 12, 25))); // Christmas
        Assert.IsTrue(BusinessCalendar.IsBusinessDay(new DateTime(2026, 11, 10)));
    }

    [TestMethod]
    public void BusinessDaysSkipTheWeekend()
    {
        Assert.AreEqual(new DateTime(2026, 11, 16), BusinessCalendar.AddBusinessDays(new DateTime(2026, 11, 13), 1));
        Assert.AreEqual(new DateTime(2026, 11, 27), BusinessCalendar.AddBusinessDays(new DateTime(2026, 11, 25), 1));
    }

    [TestMethod]
    public void VeteransDayIsSkippedWhenRollingForward()
    {
        Assert.AreEqual(new DateTime(2026, 11, 12), BusinessCalendar.NextBusinessDay(new DateTime(2026, 11, 10)));
    }

    [TestMethod]
    public void SameDaySubmissionAfterTheCutoffRollsToTheNextBusinessDay()
    {
        var cutoff = new TimeSpan(15, 30, 0);
        // 2026-11-17 is a Tuesday; EST is UTC-5.
        Assert.AreEqual(new DateTime(2026, 11, 17), BusinessCalendar.ProcessingDay(new DateTimeOffset(2026, 11, 17, 14, 0, 0, TimeSpan.Zero), cutoff));
        Assert.AreEqual(new DateTime(2026, 11, 18), BusinessCalendar.ProcessingDay(new DateTimeOffset(2026, 11, 17, 22, 0, 0, TimeSpan.Zero), cutoff));
    }
}
