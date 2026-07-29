using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionDtoMapperTest
{
    [TestMethod]
    public void MapsPlanMoneyFromCents()
    {
        var plan = new SubscriptionPlan("eshop-pro", "Pro Plan", "Best plan", 29900, 1, "month", 7126957);

        var dto = SubscriptionDtoMapper.ToDto(plan);

        Assert.AreEqual("eshop-pro", dto.Handle);
        Assert.AreEqual("Pro Plan", dto.Name);
        Assert.AreEqual(29900, dto.PriceInCents);
        Assert.AreEqual(299.00m, dto.Price);
        Assert.AreEqual("$299.00", dto.PriceDisplay);
        Assert.AreEqual(1, dto.Interval);
        Assert.AreEqual("month", dto.IntervalUnit);
        Assert.AreEqual(7126957, dto.ProductId);
    }

    [TestMethod]
    public void MapsSubscriptionFieldsIncludingNextBillingDate()
    {
        var nextBilling = new DateTimeOffset(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);
        var subscription = new CustomerSubscription(555, "active", "eshop-pro", "Pro Plan", 2900, nextBilling);

        var dto = SubscriptionDtoMapper.ToDto(subscription);

        Assert.AreEqual(555, dto.SubscriptionId);
        Assert.AreEqual("active", dto.State);
        Assert.AreEqual("eshop-pro", dto.PlanHandle);
        Assert.AreEqual(29.00m, dto.Price);
        Assert.AreEqual("$29.00", dto.PriceDisplay);
        Assert.AreEqual(nextBilling, dto.NextBillingDate);
    }
}
