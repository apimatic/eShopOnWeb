using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.MaxioIntegrationTests.Builders;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// The retry policy the integration configures. A billing write must never be re-sent behind the
/// caller's back, and a failing read must give up quickly rather than stall a page render.
/// </summary>
public class MaxioBillingClientResilienceTests
{
    [Fact]
    public async Task AFailingRead_IsRetriedUpToTheConfiguredLimitAndThenSurfaces()
    {
        var builder = new BillingClientBuilder()
            .With(settings => settings.MaxRetries = 2);

        // One initial attempt plus two retries.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            builder.Respond(HttpStatusCode.InternalServerError, """{"error":"boom"}""");
        }

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().ListSubscriptionsForCustomerAsync(55001));

        Assert.Equal((int)HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Equal(3, builder.Handler.Requests.Count);
    }

    [Fact]
    public async Task AFailingRead_IsNotRetriedWhenRetriesAreDisabled()
    {
        var builder = new BillingClientBuilder()
            .With(settings => settings.MaxRetries = 0)
            .Respond(HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().ListSubscriptionsForCustomerAsync(55001));

        Assert.Single(builder.Handler.Requests);
    }

    [Fact]
    public async Task ANegativeRetryLimit_IsTreatedAsNoRetries()
    {
        var builder = new BillingClientBuilder()
            .With(settings => settings.MaxRetries = -5)
            .Respond(HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().ListSubscriptionsForCustomerAsync(55001));

        Assert.Single(builder.Handler.Requests);
    }

    [Fact]
    public async Task AFailingWrite_IsNeverRetriedSoUsageCannotBeDoubleBilled()
    {
        var builder = new BillingClientBuilder()
            .With(settings => settings.MaxRetries = 3)
            .Respond(HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().RecordUsageAsync(90001, 3057195, 1m, "one order"));

        // Exactly one attempt: re-sending a usage report would bill the customer twice.
        Assert.Single(builder.Handler.Requests);
    }

    [Fact]
    public async Task AFailingSubscribe_IsNeverRetriedSoACustomerCannotBeEnrolledTwice()
    {
        var builder = new BillingClientBuilder()
            .With(settings => settings.MaxRetries = 3)
            .RespondWithJson(MaxioResponses.Site())
            .Respond(HttpStatusCode.InternalServerError, """{"error":"boom"}""");

        await Assert.ThrowsAsync<BillingProviderException>(
            () => builder.Build().CreateSubscriptionAsync(55001, "eshop-pro"));

        // The site read plus exactly one subscribe attempt — the write is never re-sent.
        Assert.Equal(2, builder.Handler.Requests.Count);
    }
}
