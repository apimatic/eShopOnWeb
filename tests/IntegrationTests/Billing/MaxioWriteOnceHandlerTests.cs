using System;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Models;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Billing;

/// <summary>
/// Pins the behaviour the write-once guard exists for. The first test is the hazard: with the
/// stock pipeline a single subscribe call reaches the provider more than once when the connection
/// fails, because transport faults are retried on every verb and that cannot be disabled. The
/// second test is the guarantee.
/// </summary>
public class MaxioWriteOnceHandlerTests
{
    [Fact]
    public async Task WithoutTheGuardAFailedWriteIsSentMoreThanOnce()
    {
        var transport = FailingTransport();
        var client = ClientOver(transport);

        await Assert.ThrowsAnyAsync<Exception>(() => client.Subscriptions.CreateSubscription(Body(), ct: default));

        Assert.True(
            transport.CountOf(HttpMethod.Post, "/subscriptions.json") > 1,
            "The SDK retried the write on a transport fault, which is exactly the duplicate-enrollment hazard the guard removes.");
    }

    [Fact]
    public async Task WithTheGuardAFailedWriteIsSentExactlyOnce()
    {
        var transport = FailingTransport();
        var client = ClientOver(new MaxioWriteOnceHandler(NullLogger<MaxioWriteOnceHandler>.Instance)
        {
            InnerHandler = transport
        });

        using (MaxioWriteScope.Begin())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => client.Subscriptions.CreateSubscription(Body(), ct: default));
        }

        Assert.Equal(1, transport.CountOf(HttpMethod.Post, "/subscriptions.json"));
    }

    [Fact]
    public async Task TheGuardLeavesReadsAloneSoTheyStillRetry()
    {
        var transport = FailingTransport();
        var client = ClientOver(new MaxioWriteOnceHandler(NullLogger<MaxioWriteOnceHandler>.Instance)
        {
            InnerHandler = transport
        });

        // No write scope open: this is an ordinary read, and the resilience pipeline must still apply.
        await Assert.ThrowsAnyAsync<Exception>(() => client.Customers.ReadCustomerByReference("anything", ct: default));

        Assert.True(transport.CountOf(HttpMethod.Get, "/customers/lookup.json") > 1);
    }

    private static StubTransport FailingTransport() =>
        new StubTransport(_ => throw new HttpRequestException("connection reset"));

    private static MaxioAdvancedBillingClient ClientOver(HttpMessageHandler handler)
    {
        var options = new MaxioAdvancedBillingClientOptions
        {
            Retry = RetryOptions.Default() with
            {
                Delay = TimeSpan.FromMilliseconds(1),
                MaxJitter = TimeSpan.Zero
            }
        };

        options.Server.Production.Us.Site = "test-site";

        return new MaxioAdvancedBillingClient(new HttpClient(handler), options);
    }

    private static CreateSubscriptionRequest Body() => new CreateSubscriptionRequest
    {
        Subscription = new CreateSubscription
        {
            ProductHandle = "pro-plan",
            CustomerId = 500
        }
    };
}
