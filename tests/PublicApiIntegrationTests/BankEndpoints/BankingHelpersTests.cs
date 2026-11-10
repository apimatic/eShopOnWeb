using System;
using Microsoft.eShopWeb.PublicApi.Banking;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BankEndpoints;

[TestClass]
public class BankingHelpersTests
{
    [TestMethod]
    public void NextBusinessDaySkipsWeekendsAndHolidays()
    {
        // Friday -> Monday
        Assert.AreEqual(new DateTime(2026, 11, 16), BusinessCalendar.NextBusinessDay(new DateTime(2026, 11, 13)));
        // Wednesday before Thanksgiving (Nov 26, 2026) -> Friday
        Assert.AreEqual(new DateTime(2026, 11, 27), BusinessCalendar.NextBusinessDay(new DateTime(2026, 11, 25)));
        // Friday before Christmas weekend: Dec 25, 2026 is a Friday and a holiday.
        Assert.AreEqual(new DateTime(2026, 12, 28), BusinessCalendar.NextBusinessDay(new DateTime(2026, 12, 24)));
    }

    [TestMethod]
    public void OrdersAboveTheAchLimitAreSplit()
    {
        var parts = BankCheckoutService.Split(25_000_000.50m, BankCheckoutService.AchTransferLimit);
        Assert.AreEqual(3, parts.Count);
        Assert.AreEqual(25_000_000.50m, parts[0] + parts[1] + parts[2]);
        Assert.AreEqual(1, BankCheckoutService.Split(10m, BankCheckoutService.AchTransferLimit).Count);
    }

    [TestMethod]
    public void AmountsUseTwoDecimalsAndNoCulture()
    {
        Assert.AreEqual("1234.50", BankCheckoutService.FormatAmount(1234.5m));
        Assert.AreEqual("0.10", BankCheckoutService.FormatAmount(0.1m));
    }

    [TestMethod]
    public void PlaidUserIdIsNotAnEmailAddress()
    {
        var id = BankConnectionService.PlaidUserId("Demouser@Microsoft.com");
        Assert.IsFalse(id.Contains('@'));
        Assert.AreEqual(id, BankConnectionService.PlaidUserId("demouser@microsoft.com"));
    }
}
