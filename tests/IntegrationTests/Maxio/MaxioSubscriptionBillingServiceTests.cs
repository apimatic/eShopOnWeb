using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Caching.Memory;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Maxio;

/// <summary>
/// Exercises the service's error boundary against a stubbed HTTP transport: provider errors, transport
/// failures, and malformed bodies must all surface as <see cref="SubscriptionBillingException"/> with a
/// sensible HTTP status and a caller-safe message — never a raw SDK/JSON leak. (The success/idempotency
/// paths are validated end-to-end against the live Maxio sandbox.)
/// </summary>
public class MaxioSubscriptionBillingServiceTests
{
    private static readonly SubscriberIdentity Subscriber =
        new(Reference: "demouser@microsoft.com", Email: "demouser@microsoft.com", FirstName: "Demo", LastName: "User");

    private static MaxioSubscriptionBillingService BuildService(StubHttpMessageHandler handler)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "test",
            ProductFamilyHandle = "test-product-family"
        };
        var client = new MaxioAdvancedBillingClient(new HttpClient(handler), MaxioClientOptionsFactory.Create(settings));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var logger = Substitute.For<IAppLogger<MaxioSubscriptionBillingService>>();
        return new MaxioSubscriptionBillingService(client, settings, cache, logger);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task GetSubscriptions_ReturnsEmpty_WhenCustomerNotFound()
    {
        // ReadCustomerByReference returns 404 → treated as "no customer yet", so an empty list (no create).
        var handler = new StubHttpMessageHandler(_ => Json(HttpStatusCode.NotFound, "{\"errors\":[\"not found\"]}"));
        var service = BuildService(handler);

        var result = await service.GetSubscriptionsAsync(Subscriber);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPlans_Throws_WhenProviderReturnsServerError()
    {
        var handler = new StubHttpMessageHandler(_ => Json(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}"));
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.GetPlansAsync());

        // A provider 5xx is not caller-actionable → surfaced as a 5xx with a safe message.
        Assert.True((int)ex.StatusCode >= 500);
        Assert.DoesNotContain("MaxioAdvancedBilling", ex.Message);
        Assert.DoesNotContain("Exception", ex.Message);
    }

    [Fact]
    public async Task GetPlans_Throws503_WhenTransportFails()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("connection reset"));
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.GetPlansAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }

    [Fact]
    public async Task GetPlans_Throws_WhenSuccessBodyIsMalformed()
    {
        // A 200 whose body is not valid JSON surfaces as JsonException inside the SDK; the boundary must
        // convert it to a controlled error, not let it escape.
        var handler = new StubHttpMessageHandler(_ => Json(HttpStatusCode.OK, "{ this is not json"));
        var service = BuildService(handler);

        var ex = await Assert.ThrowsAsync<SubscriptionBillingException>(() => service.GetPlansAsync());

        Assert.DoesNotContain("System.Text.Json", ex.Message);
        Assert.Contains("could not be processed", ex.Message);
    }
}
