using System.Net;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public sealed class MaxioWriteAttemptGuardHandlerTests
{
    [Fact]
    public async Task TransportRetryDoesNotSendSubscriptionCreationTwice()
    {
        var primaryHandler = new FailingTransportHandler();
        var guardHandler = new MaxioWriteAttemptGuardHandler
        {
            InnerHandler = primaryHandler
        };
        using var httpClient = new HttpClient(guardHandler);
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(5)
            }
        };
        options.Server.Production.Us.BaseUrl = "https://maxio.invalid";
        var client = new MaxioAdvancedBillingClient(httpClient, options);
        var request = new CreateSubscriptionRequest
        {
            Subscription = new CreateSubscription
            {
                ProductHandle = "test-plan",
                CustomerId = 42,
                PaymentCollectionMethod = CollectionMethod.Remittance,
                Reference = "test-subscription-reference"
            }
        };

        using var writeAttempt = MaxioWriteAttemptScope.Begin();
        await Assert.ThrowsAsync<MaxioRepeatedWriteBlockedException>(() =>
            client.Subscriptions.CreateSubscription(request, ct: default));

        Assert.Equal(1, primaryHandler.SendCount);
    }

    private sealed class FailingTransportHandler : HttpMessageHandler
    {
        private int _sendCount;

        public int SendCount => Volatile.Read(ref _sendCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _sendCount);
            throw new HttpRequestException("Simulated connection reset.");
        }
    }
}
