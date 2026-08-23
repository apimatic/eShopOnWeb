using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.Infrastructure.Subscriptions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Subscriptions;

public class MaxioSingleSendGuardTests
{
    [Fact]
    public async Task BlocksSdkTransportRetryBeforeASecondPostReachesTheNetwork()
    {
        var transport = new ThrowingTransportHandler();
        var guard = new MaxioSingleSendGuard();
        var guardHandler = new MaxioSingleSendHandler(guard) { InnerHandler = transport };
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Delay = TimeSpan.Zero,
                MaxJitter = TimeSpan.Zero,
                Timeout = TimeSpan.FromSeconds(1)
            },
            BasicAuth = new BasicAuthCredentials { Username = "test", Password = "x" }
        };
        options.Server.Production.Us.BaseUrl = "https://maxio.invalid";
        var client = new MaxioAdvancedBillingClient(new HttpClient(guardHandler), options);

        using var scope = guard.BeginSubscriptionCreate();
        await Assert.ThrowsAnyAsync<Exception>(() => client.Subscriptions.CreateSubscription(
            new CreateSubscriptionRequest
            {
                Subscription = new CreateSubscription
                {
                    ProductHandle = "test-plan",
                    CustomerReference = "test-customer",
                    Reference = "test-subscription"
                }
            },
            ct: default));

        Assert.Equal(1, transport.SendCount);
    }

    private sealed class ThrowingTransportHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            throw new HttpRequestException("Simulated connection reset after send.");
        }
    }
}
