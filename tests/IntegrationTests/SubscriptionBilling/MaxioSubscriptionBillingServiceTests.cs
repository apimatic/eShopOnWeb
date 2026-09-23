using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.SubscriptionBilling;

public class MaxioSubscriptionBillingServiceTests
{
    private const string FamilyHandle = "eshop-subscribe";
    private static readonly SubscriberIdentity Buyer = new() { BuyerId = "demouser@microsoft.com", Email = "demouser@microsoft.com" };

    private const string FamiliesJson = """[{"product_family":{"id":3023074,"handle":"eshop-subscribe","name":"eShop"}}]""";
    private const string ProductsJson = """
        [{"product":{"id":7126957,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month"}},
         {"product":{"id":7126958,"handle":"basic-plan","name":"Basic Plan","price_in_cents":2900,"interval":1,"interval_unit":"month"}}]
        """;

    private static MaxioBillingContext NewContext() =>
        new(new DbContextOptionsBuilder<MaxioBillingContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static MaxioSubscriptionBillingService NewService(StubHttpMessageHandler handler, MaxioBillingContext db)
    {
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), new MaxioAdvancedBillingClientOptions());
        var settings = Options.Create(new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "test",
            ProductFamilyHandle = FamilyHandle,
            DefaultPlanHandle = "eshop-pro"
        });
        return new MaxioSubscriptionBillingService(client, db, settings, NullLogger<MaxioSubscriptionBillingService>.Instance);
    }

    [Fact]
    public async Task GetPlans_MapsProductsToPlans()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.EndsWith("/product_families.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, FamiliesJson);
            if (req.Method == HttpMethod.Get && path.Contains("/products.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
        });
        using var db = NewContext();
        var service = NewService(handler, db);

        var result = await service.GetPlansAsync(CancellationToken.None);

        Assert.Equal(2, result.Plans.Count);
        var pro = result.Plans.Single(p => p.Handle == "eshop-pro");
        Assert.Equal(29900, pro.PriceInCents);
        Assert.Equal("$299.00", pro.FormattedPrice);
        Assert.Equal("month", pro.IntervalUnit);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Subscribe_CreatesCustomerAndSubscription_WhenNew()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.EndsWith("/product_families.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, FamiliesJson);
            if (req.Method == HttpMethod.Get && path.Contains("/products.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (req.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}"); // no existing customer
            if (req.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.Created, """{"customer":{"id":555}}""");
            if (req.Method == HttpMethod.Get && path.EndsWith("/subscriptions/lookup.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}"); // not yet created
            if (req.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.Created,
                    """{"subscription":{"id":9001,"state":"active","product_price_in_cents":29900,"current_period_ends_at":"2026-10-23T13:53:06+05:00","reference":"eshop-sub:demouser@microsoft.com:eshop-pro","product":{"handle":"eshop-pro","name":"Pro Plan"}}}""");
            return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
        });
        using var db = NewContext();
        var service = NewService(handler, db);

        var result = await service.SubscribeAsync(Buyer, "eshop-pro", CancellationToken.None);

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(9001, result.SubscriptionId);
        Assert.Equal(555, result.CustomerId);
        Assert.Equal("active", result.State);
        Assert.NotNull(result.NextBillingDate);

        // Local records persisted.
        Assert.Equal(1, await db.CustomerLinks.CountAsync());
        var enrollment = await db.Enrollments.SingleAsync();
        Assert.True(enrollment.IsConfirmed);
        Assert.Equal(9001, enrollment.MaxioSubscriptionId);
        Assert.Equal(1, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_IsIdempotent_WhenAlreadyConfirmed()
    {
        using var db = NewContext();
        db.CustomerLinks.Add(new MaxioCustomerLink(Buyer.BuyerId, 555));
        var enrollment = new SubscriptionEnrollment(Buyer.BuyerId, "eshop-pro", "eshop-sub:demouser@microsoft.com:eshop-pro", 555);
        enrollment.Confirm(9001, "active");
        db.Enrollments.Add(enrollment);
        await db.SaveChangesAsync();

        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.EndsWith("/product_families.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, FamiliesJson);
            if (req.Method == HttpMethod.Get && path.Contains("/products.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (req.Method == HttpMethod.Get && path.EndsWith("/subscriptions/lookup.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK,
                    """{"subscription":{"id":9001,"state":"active","product_price_in_cents":29900,"reference":"eshop-sub:demouser@microsoft.com:eshop-pro","product":{"handle":"eshop-pro","name":"Pro Plan"}}}""");
            return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
        });
        var service = NewService(handler, db);

        var result = await service.SubscribeAsync(Buyer, "eshop-pro", CancellationToken.None);

        Assert.Equal(SubscribeOutcome.AlreadySubscribed, result.Outcome);
        Assert.Equal(9001, result.SubscriptionId);
        // No second subscription created.
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
        Assert.Equal(1, await db.Enrollments.CountAsync());
    }

    [Fact]
    public async Task Subscribe_UnknownPlan_Throws()
    {
        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.EndsWith("/product_families.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, FamiliesJson);
            if (req.Method == HttpMethod.Get && path.Contains("/products.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
        });
        using var db = NewContext();
        var service = NewService(handler, db);

        await Assert.ThrowsAsync<UnknownSubscriptionPlanException>(
            () => service.SubscribeAsync(Buyer, "no-such-plan", CancellationToken.None));
        Assert.Equal(0, handler.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task Subscribe_ReconcilesCustomer_OnDuplicateReference422()
    {
        var lookupCount = 0;
        var handler = new StubHttpMessageHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Get && path.EndsWith("/product_families.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, FamiliesJson);
            if (req.Method == HttpMethod.Get && path.Contains("/products.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, ProductsJson);
            if (req.Method == HttpMethod.Get && path.EndsWith("/customers/lookup.json"))
            {
                // First lookup: not found (drives a create); after the 422, the reconcile lookup finds it.
                var found = Interlocked.Increment(ref lookupCount) > 1;
                return found
                    ? StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"customer":{"id":777}}""")
                    : StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
            }
            if (req.Method == HttpMethod.Post && path.EndsWith("/customers.json"))
                return StubHttpMessageHandler.Json((HttpStatusCode)422, """{"errors":{"reference":["has already been taken"]}}""");
            if (req.Method == HttpMethod.Get && path.EndsWith("/subscriptions/lookup.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
            if (req.Method == HttpMethod.Post && path.EndsWith("/subscriptions.json"))
                return StubHttpMessageHandler.Json(HttpStatusCode.Created,
                    """{"subscription":{"id":9002,"state":"active","product_price_in_cents":29900,"product":{"handle":"eshop-pro","name":"Pro Plan"}}}""");
            return StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}");
        });
        using var db = NewContext();
        var service = NewService(handler, db);

        var result = await service.SubscribeAsync(Buyer, "eshop-pro", CancellationToken.None);

        Assert.Equal(SubscribeOutcome.Created, result.Outcome);
        Assert.Equal(777, result.CustomerId); // adopted the existing customer, not a duplicate
    }

    [Fact]
    public async Task GetMySubscriptions_ReturnsEmpty_WhenNoCustomer()
    {
        var handler = new StubHttpMessageHandler(req =>
            StubHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}")); // customer lookup 404
        using var db = NewContext();
        var service = NewService(handler, db);

        var result = await service.GetMySubscriptionsAsync(Buyer, CancellationToken.None);

        Assert.Empty(result);
    }
}
