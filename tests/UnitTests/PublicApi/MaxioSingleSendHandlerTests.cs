using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.PublicApi;

public class MaxioSingleSendHandlerTests
{
    [Fact]
    public async Task BlocksSdkTransportRetryBeforeSecondNetworkSend()
    {
        var primary = new ThrowingPrimaryHandler();
        var guard = new SingleSendHandler { InnerHandler = primary };
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with { MaxRetries = 1 },
            BasicAuth = new BasicAuthCredentials { Username = "test", Password = "x" }
        };
        options.Server.Production.Us.BaseUrl = "https://example.invalid";
        var client = new MaxioAdvancedBillingClient(new HttpClient(guard), options);

        using var scope = MaxioCallScope.Begin(enforceSingleSend: true);
        await Assert.ThrowsAsync<DuplicateSendBlockedException>(() =>
            client.Subscriptions.CreateSubscription(
                new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = "plan",
                        CustomerReference = "customer",
                        Reference = "subscription"
                    }
                },
                ct: default));

        Assert.Equal(1, primary.SendCount);
    }

    private sealed class ThrowingPrimaryHandler : HttpMessageHandler
    {
        public int SendCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            throw new HttpRequestException("simulated connection reset");
        }
    }
}
