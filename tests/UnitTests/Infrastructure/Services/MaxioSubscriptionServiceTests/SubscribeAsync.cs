using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Services.MaxioSubscriptionServiceTests;

public class SubscribeAsync
{
    private const string FamilyHandle = "test-family";
    private const string PlanHandle = "eshop-pro";

    private static readonly string FamiliesJson =
        """[{"product_family":{"id":1,"name":"Test Family","handle":"test-family"}}]""";

    private static readonly string ProductsJson =
        """[{"product":{"id":10,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false}}]""";

    private static readonly string SubscriptionJson =
        """{"subscription":{"state":"active","current_period_ends_at":"2026-10-05T00:00:00Z","product":{"id":10,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}""";

    private static readonly string ExistingSubscriptionListJson =
        """[{"subscription":{"state":"active","current_period_ends_at":"2026-10-05T00:00:00Z","product":{"id":10,"name":"Pro Plan","handle":"eshop-pro","price_in_cents":29900,"interval":1,"interval_unit":"month"}}}]""";

    private static MaxioSubscriptionService CreateService(StubMaxioHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            BasicAuth = new BasicAuthCredentials { Username = "test-key", Password = "x" }
        };
        options.Server.Production.Us.Site = "test-site";

        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), options);
        var settings = Options.Create(new MaxioSettings { ProductFamilyHandle = FamilyHandle });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Substitute.For<ILogger<MaxioSubscriptionService>>();

        return new MaxioSubscriptionService(client, settings, cache, logger);
    }

    [Fact]
    public async Task CreatesCustomerAndSubscriptionWhenNoneExistYet()
    {
        var handler = new StubMaxioHandler(new (HttpStatusCode, string)[]
        {
            (HttpStatusCode.OK, FamiliesJson),
            (HttpStatusCode.OK, ProductsJson),
            (HttpStatusCode.NotFound, """{"errors":["Customer not found"]}"""),
            (HttpStatusCode.OK, """{"customer":{"id":555,"first_name":"demouser","last_name":"Subscriber","email":"demouser@microsoft.com","reference":"user-guid-1"}}"""),
            (HttpStatusCode.OK, "[]"),
            (HttpStatusCode.OK, SubscriptionJson),
        });

        var service = CreateService(handler);

        var result = await service.SubscribeAsync("user-guid-1", "demouser@microsoft.com", PlanHandle);

        Assert.Equal(PlanHandle, result.PlanHandle);
        Assert.Equal("active", result.State);
        Assert.Equal(29900, result.PriceInCents);
        Assert.NotNull(result.NextBillingDate);
        Assert.Equal(6, handler.Requests.Count);
    }

    [Fact]
    public async Task ReturnsExistingLiveSubscriptionInsteadOfCreatingADuplicate()
    {
        var handler = new StubMaxioHandler(new (HttpStatusCode, string)[]
        {
            (HttpStatusCode.OK, FamiliesJson),
            (HttpStatusCode.OK, ProductsJson),
            (HttpStatusCode.OK, """{"customer":{"id":555,"first_name":"demouser","last_name":"Subscriber","email":"demouser@microsoft.com","reference":"user-guid-1"}}"""),
            (HttpStatusCode.OK, ExistingSubscriptionListJson),
        });

        var service = CreateService(handler);

        var result = await service.SubscribeAsync("user-guid-1", "demouser@microsoft.com", PlanHandle);

        Assert.Equal(PlanHandle, result.PlanHandle);
        Assert.Equal("active", result.State);
        // No 5th call (CreateSubscription) — the existing live subscription short-circuits creation.
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task ThrowsValidationExceptionForUnknownPlanHandle()
    {
        var handler = new StubMaxioHandler(new (HttpStatusCode, string)[]
        {
            (HttpStatusCode.OK, FamiliesJson),
            (HttpStatusCode.OK, ProductsJson),
        });

        var service = CreateService(handler);

        await Assert.ThrowsAsync<Microsoft.eShopWeb.ApplicationCore.Exceptions.SubscriptionValidationException>(
            () => service.SubscribeAsync("user-guid-1", "demouser@microsoft.com", "does-not-exist"));
    }
}
