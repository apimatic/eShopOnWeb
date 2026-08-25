using System.Net;
using System.Text;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public sealed class MaxioSdkContractTests
{
    [Fact]
    public async Task CreateSubscriptionSendsExpectedSelectorAndReference()
    {
        var transport = new StubHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """{"subscription":{"id":42,"reference":"eshop-sub-test"}}"""));
        var guard = new MaxioWriteGuard();
        var client = CreateClient(transport, guard);

        using (guard.BeginWrite())
        {
            await client.Subscriptions.CreateSubscription(
                body: new CreateSubscriptionRequest
                {
                    Subscription = new CreateSubscription
                    {
                        ProductHandle = "eshop-pro",
                        CustomerId = 17,
                        Reference = "eshop-sub-test",
                        PaymentCollectionMethod = CollectionMethod.Remittance
                    }
                },
                ct: default);
        }

        Assert.Single(transport.Requests);
        Assert.Equal(HttpMethod.Post, transport.Requests[0].Method);
        var json = transport.Bodies[0];
        Assert.Contains("\"product_handle\":\"eshop-pro\"", json);
        Assert.Contains("\"customer_id\":17", json);
        Assert.Contains("\"reference\":\"eshop-sub-test\"", json);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", json);
    }

    [Fact]
    public async Task WriteGuardBlocksSdkTransportRetryFromSendingTwice()
    {
        var transport = new StubHandler(_ => throw new HttpRequestException("connection reset"));
        var guard = new MaxioWriteGuard();
        var client = CreateClient(transport, guard);

        using (guard.BeginWrite())
        {
            await Assert.ThrowsAsync<MaxioWriteResendBlockedException>(() =>
                client.Subscriptions.CreateSubscription(
                    body: new CreateSubscriptionRequest
                    {
                        Subscription = new CreateSubscription
                        {
                            ProductHandle = "eshop-pro",
                            CustomerId = 17,
                            Reference = "eshop-sub-test",
                            PaymentCollectionMethod = CollectionMethod.Remittance
                        }
                    },
                    ct: default));
        }

        Assert.Single(transport.Requests);
    }

    private static MaxioAdvancedBillingClient CreateClient(
        HttpMessageHandler transport,
        MaxioWriteGuard guard)
    {
        var guardHandler = new MaxioWriteGuardHandler(guard) { InnerHandler = transport };
        var options = new MaxioAdvancedBillingClientOptions
        {
            Environment = ServerEnvironment.Us,
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Delay = TimeSpan.Zero,
                MaxJitter = TimeSpan.Zero,
                Timeout = TimeSpan.FromSeconds(2)
            }
        };
        options.Server.Production.Us.BaseUrl = "https://example.test";
        return new MaxioAdvancedBillingClient(new HttpClient(guardHandler), options);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return _response(request);
        }
    }
}
